using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7B-1 계약: 거점 반경 anchorRadiusM (M7 설계 §1.4, Q28·Q29). 이동 목적지를 시작 위치 반경 안으로 자른다. 0 = 무제한.
    public class AnchorTests
    {
        private AiConfigSO config;
        private FakeEnemyWorld world;

        [SetUp]
        public void SetUp()
        {
            config = M3TestData.AiConfig();
            world = new FakeEnemyWorld();
        }

        private EnemyBrain Make(EnemyArchetypeSO archetype, IReadOnlyList<Vector3> route = null) =>
            new EnemyBrain(1, archetype, config, world, route, null, new System.Random(7));

        private static EnemyArchetypeSO Scv(float anchor)
        {
            EnemyArchetypeSO a = M3TestData.Scv();
            a.anchorRadiusM = anchor;
            return a;
        }

        // ── AnchorRules.Clamp (순수) ──

        [Test]
        public void Clamp_Inside_Unchanged()
        {
            var p = new Vector3(3f, 2f, 4f);
            Assert.AreEqual(p, AnchorRules.Clamp(p, Vector3.zero, 10f));
        }

        [Test]
        public void Clamp_OnBoundary_Unchanged()
        {
            var p = new Vector3(6f, 0f, 8f);   // 거리 10
            Assert.AreEqual(p, AnchorRules.Clamp(p, Vector3.zero, 10f));
        }

        [Test]
        public void Clamp_Outside_ToRadiusSameDirection_KeepsHeight()
        {
            var center = new Vector3(100f, 5f, -50f);
            Vector3 p = center + new Vector3(30f, 7f, 40f);   // 수평 50
            Vector3 c = AnchorRules.Clamp(p, center, 25f);
            Assert.AreEqual(center.x + 15f, c.x, 1e-4f);
            Assert.AreEqual(center.z + 20f, c.z, 1e-4f);
            Assert.AreEqual(p.y, c.y, "높이는 원래 목적지 값");
        }

        [Test]
        public void Clamp_IgnoresVerticalDistance()
        {
            var p = new Vector3(0f, 100f, 5f);
            Assert.AreEqual(p, AnchorRules.Clamp(p, Vector3.zero, 10f), "수평 거리만 본다");
        }

        [TestCase(0f)]
        [TestCase(-5f)]
        [TestCase(float.NaN)]
        public void Clamp_ZeroOrInvalidRadius_Unlimited(float radius)
        {
            var p = new Vector3(500f, 0f, -300f);
            Assert.AreEqual(p, AnchorRules.Clamp(p, Vector3.zero, radius));
        }

        // ── EnemyBrain 연결 ──

        // SCV Alert(안 보임) = LKP로 달림. 반경 10이면 목적지가 시작 위치에서 10m로 잘린다.
        [Test]
        public void Brain_ChaseBeyondAnchor_DestinationClamped()
        {
            var spawn = new Vector3(5f, 0f, 0f);
            EnemyBrain brain = Make(Scv(10f));
            brain.OnHeard(new Vector3(5f, 0f, 50f), NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, spawn), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.IsTrue(brain.HasAnchor);
            Assert.AreEqual(spawn, brain.AnchorCenter);
            Assert.IsTrue(brain.Intent.HasDestination);
            Assert.AreEqual(5f, brain.Intent.Destination.x, 1e-4f);
            Assert.AreEqual(10f, brain.Intent.Destination.z, 1e-4f);
            Assert.AreEqual(new Vector3(5f, 0f, 50f), brain.LastKnownPosition, "LKP 자체는 자르지 않는다");
        }

        [Test]
        public void Brain_ZeroAnchor_DestinationUnlimited()
        {
            EnemyBrain brain = Make(Scv(0f));
            brain.OnHeard(new Vector3(0f, 0f, 50f), NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, Vector3.zero), 0.1f);
            Assert.IsFalse(brain.HasAnchor);
            Assert.AreEqual(new Vector3(0f, 0f, 50f), brain.Intent.Destination);
        }

        // 중심은 첫 Tick 위치로 고정 — 움직인 뒤에도 바뀌지 않는다.
        [Test]
        public void Brain_AnchorCenter_FixedAtFirstTick()
        {
            EnemyBrain brain = Make(Scv(10f));
            brain.Tick(AiTest.NoTarget(0.1f, Vector3.zero), 0.1f);
            brain.Tick(AiTest.NoTarget(0.2f, new Vector3(8f, 0f, 0f)), 0.1f);
            Assert.AreEqual(Vector3.zero, brain.AnchorCenter);

            brain.OnHeard(new Vector3(40f, 0f, 0f), NoiseKind.Gunshot, 0.2f);
            brain.Tick(AiTest.Unseen(0.3f, new Vector3(8f, 0f, 0f)), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(10f, brain.Intent.Destination.x, 1e-4f, "중심 (0,0,0) 기준 10m");
        }

        // 반경 밖 웨이포인트는 경계까지만 가고, 경계에 닿으면 도착으로 본다(대기 → 다음 웨이포인트).
        [Test]
        public void Brain_PatrolWaypointOutside_ReachedAtBoundary()
        {
            var route = new List<Vector3> { new Vector3(0f, 0f, 30f), new Vector3(30f, 0f, 30f) };
            EnemyBrain brain = Make(Scv(10f), route);
            brain.Tick(AiTest.NoTarget(3f, Vector3.zero), 3f);
            Assert.AreEqual(EnemyState.Patrol, brain.State);
            brain.Tick(AiTest.NoTarget(3.2f, Vector3.zero), 0.2f);
            Assert.AreEqual(EnemyMoveMode.Walk, brain.Intent.Move);
            Assert.AreEqual(new Vector3(0f, 0f, 10f), brain.Intent.Destination);

            brain.Tick(AiTest.NoTarget(3.4f, new Vector3(0f, 0f, 10f)), 0.2f);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move, "경계 = 도착 → 대기");
            Assert.IsFalse(brain.Intent.HasDestination);
        }

        // 모든 목적지가 반경 안: 순찰·경계·교전·수색을 돌려도 목적지 수평 거리 ≤ 반경 (반경 밖 엄폐 포함).
        [Test]
        public void Brain_AllDestinationsWithinAnchor()
        {
            const float radius = 6f;
            world.AddCover(new Vector3(0f, 0f, 20f), Vector3.back);   // 반경 밖 엄폐
            EnemyArchetypeSO rdr = M3TestData.Rdr();
            rdr.anchorRadiusM = radius;
            rdr.flanks = false;   // 보스·호위와 같게 (우회 유예 없이 Search까지)
            var route = new List<Vector3> { new Vector3(0f, 0f, 30f), new Vector3(-30f, 0f, 0f) };
            EnemyBrain brain = Make(rdr, route);
            Vector3 self = Vector3.zero;
            float t = 0f;
            var seenStates = new HashSet<EnemyState>();
            for (int i = 0; i < 600; i++)
            {
                t += 0.1f;
                world.Now = t;
                if (i == 50)
                    brain.OnHeard(new Vector3(0f, 0f, 40f), NoiseKind.Gunshot, t);
                EnemySenses s = i >= 60 && i < 90 ? AiTest.Seen(t, self, new Vector3(0f, 0f, 40f)) : AiTest.Unseen(t, self);
                brain.Tick(s, 0.1f);
                seenStates.Add(brain.State);
                EnemyIntent intent = brain.Intent;
                if (!intent.HasDestination)
                    continue;
                Vector3 d = intent.Destination;
                d.y = 0f;
                Assert.LessOrEqual(d.magnitude, radius + 1e-3f, $"{brain.State} 목적지 {intent.Destination}");
                // 목적지 쪽으로 조금씩 이동
                Vector3 step = intent.Destination - self;
                step.y = 0f;
                if (step.magnitude > 0.3f)
                    self += step.normalized * 0.3f;
                else
                    self = new Vector3(intent.Destination.x, 0f, intent.Destination.z);
            }
            Assert.IsTrue(seenStates.Contains(EnemyState.Patrol));
            Assert.IsTrue(seenStates.Contains(EnemyState.Combat));
            Assert.IsTrue(seenStates.Contains(EnemyState.Search));
        }

        // ── 7B 리뷰 수정 ──

        // 리뷰 6: 분대원은 리더(보스)의 거점 중심을 같이 쓴다. 리더가 아직 Tick 전이면 기다린다(거점 없음).
        [Test]
        public void Brain_SquadFollower_SharesLeaderAnchor_WaitsForLeader()
        {
            var squad = new Squad(14, config);
            var boss = new EnemyBrain(1, Scv(25f), config, world, null, squad, new System.Random(1), 0);
            var guard = new EnemyBrain(2, Scv(25f), config, world, null, squad, new System.Random(2), 1);
            var bossSpawn = new Vector3(-270f, 0f, 120f);
            var guardSpawn = new Vector3(-266f, 0f, 116f);

            guard.Tick(AiTest.NoTarget(0.1f, guardSpawn), 0.1f);
            Assert.IsFalse(guard.HasAnchor, "리더가 거점을 정하기 전 = 기다림");

            boss.Tick(AiTest.NoTarget(0.1f, bossSpawn), 0.1f);
            Assert.IsTrue(boss.HasAnchor);
            Assert.AreEqual(bossSpawn, boss.AnchorCenter, "리더 = 자기 첫 Tick 위치");

            guard.Tick(AiTest.NoTarget(0.2f, guardSpawn), 0.1f);
            Assert.IsTrue(guard.HasAnchor);
            Assert.AreEqual(bossSpawn, guard.AnchorCenter, "호위 = 보스 거점");

            // 리더가 거점 유형이 아니면 자기 위치
            var squad2 = new Squad(15, config);
            var leader = new EnemyBrain(3, Scv(0f), config, world, null, squad2, new System.Random(3), 0);
            var member = new EnemyBrain(4, Scv(10f), config, world, null, squad2, new System.Random(4), 1);
            member.Tick(AiTest.NoTarget(0.1f, new Vector3(3f, 0f, 0f)), 0.1f);
            Assert.IsFalse(leader.HasAnchor);
            Assert.IsTrue(member.HasAnchor);
            Assert.AreEqual(new Vector3(3f, 0f, 0f), member.AnchorCenter, "리더 거점 없음 = 자기 첫 Tick 위치");
        }

        // 리뷰 7: 잘린 점은 NavMesh에 붙인다(높이 = 샘플). 샘플이 반경 밖으로 밀려도 다시 반경 안으로.
        [Test]
        public void Brain_ClampedDestination_SampledToNavMesh_StaysInRadius()
        {
            world.NavPointRule = (p, r) => new Vector3(p.x + 2f, 7f, p.z + 2f);   // 바닥 높이 7, 바깥쪽으로 밀림
            var spawn = new Vector3(0f, 0f, 0f);
            EnemyBrain brain = Make(Scv(10f));
            brain.OnHeard(new Vector3(0f, 0f, 50f), NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, spawn), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Vector3 d = brain.Intent.Destination;
            Assert.AreEqual(7f, d.y, 1e-4f, "NavMesh 높이");
            Assert.LessOrEqual(new Vector2(d.x, d.z).magnitude, 10f + 1e-3f, "반경 안");

            // 같은 목적지를 다시 물어도 같은 점(MoveTo·Reached 일관), NavMesh 질의는 늘지 않는다
            int calls = world.SampleNavCalls;
            brain.Tick(AiTest.Unseen(0.2f, spawn), 0.1f);
            Assert.AreEqual(d, brain.Intent.Destination);
            Assert.AreEqual(calls, world.SampleNavCalls, "캐시");
        }

        // 리뷰 2: 거점 유형은 엄폐 없음 대체 행동(약탈자 Combat)에서 선호 거리 밖 표적에게 다가가지 않고 제자리 사격.
        [Test]
        public void Brain_AnchoredFallback_FarVisibleTarget_HoldsAndFires_UnanchoredStillApproaches()
        {
            Vector3 target = new Vector3(0f, 0f, 30f);   // SCV 선호 8~20m 밖, 사거리 50m 안
            EnemyBrain anchored = Make(Scv(25f));
            EnterCombat(anchored, target);
            Assert.AreEqual(EnemyMoveMode.Hold, anchored.Intent.Move, "다가가지 않음");
            Assert.IsFalse(anchored.Intent.HasDestination);
            Assert.IsTrue(anchored.Intent.Fire, "보이고 사거리 안 = 제자리 사격");

            // 사거리 밖(55m)이면 쏘지 않는다
            anchored.Tick(AiTest.Seen(9.9f, Vector3.zero, new Vector3(0f, 0f, 55f)), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Hold, anchored.Intent.Move);
            Assert.IsFalse(anchored.Intent.Fire, "사거리 밖");

            // 회귀: 거점 없는 SCV는 기존대로 달려가 좁힌다(사격 안 함)
            EnemyBrain free = Make(Scv(0f));
            EnterCombat(free, target);
            Assert.AreEqual(EnemyMoveMode.Run, free.Intent.Move);
            Assert.IsFalse(free.Intent.Fire);
        }

        // 리뷰 2: 레이더 엄폐 질의에 거점이 실린다(거점 없으면 기존 질의 그대로)
        [Test]
        public void Brain_AnchoredRaider_CoverQueryCarriesAnchor()
        {
            world.AddCover(new Vector3(0f, 0f, 5f), Vector3.forward);
            EnemyArchetypeSO rdr = M3TestData.Rdr();
            rdr.anchorRadiusM = 25f;
            var spawn = new Vector3(4f, 0f, -2f);
            EnemyBrain brain = Make(rdr);
            brain.OnHeard(new Vector3(4f, 0f, 40f), NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, spawn), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.Greater(world.TryFindCoverCalls, 0);
            Assert.IsTrue(world.LastCoverQuery.HasAnchor);
            Assert.AreEqual(spawn, world.LastCoverQuery.AnchorCenter);
            Assert.AreEqual(25f, world.LastCoverQuery.AnchorRadiusM, 1e-4f);

            var world2 = new FakeEnemyWorld();
            world2.AddCover(new Vector3(0f, 0f, 5f), Vector3.forward);
            var free = new EnemyBrain(2, M3TestData.Rdr(), config, world2, null, null, new System.Random(7));
            free.OnHeard(new Vector3(4f, 0f, 40f), NoiseKind.Gunshot, 0f);
            free.Tick(AiTest.Unseen(0.1f, spawn), 0.1f);
            Assert.IsFalse(world2.LastCoverQuery.HasAnchor, "거점 없는 RDR = 기존 질의");
        }

        // 피격 + 보이는 Tick으로 Combat까지 (반응 시간 지남). 마지막 Tick 시각 < 9.9
        private static void EnterCombat(EnemyBrain brain, Vector3 target)
        {
            brain.OnDamaged(target, 0f);
            float t = 0f;
            for (int k = 0; k < 30 && brain.State != EnemyState.Combat; k++)
            {
                t += 0.1f;
                brain.Tick(AiTest.Seen(t, Vector3.zero, target), 0.1f);
            }
            Assert.AreEqual(EnemyState.Combat, brain.State, "Combat 진입");
            t += 0.1f;
            brain.Tick(AiTest.Seen(t, Vector3.zero, target), 0.1f);
        }
    }
}
