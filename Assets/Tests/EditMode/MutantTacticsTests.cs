using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7B-2b: AI-MUT 변이체 전술 (M7 설계 §1.4, Q24·Q25). 무장·엄폐·우회 없음, 질주로 표적 둘레 순번 자리, 근접 의도.
    public class MutantTacticsTests
    {
        private AiConfigSO config;
        private FakeEnemyWorld world;

        [SetUp]
        public void SetUp()
        {
            config = M3TestData.AiConfig();
            world = new FakeEnemyWorld();
        }

        // §3.4 AI-MUT 값 (데이터 에셋과 독립 — 테스트 안에서 만든다)
        private static EnemyArchetypeSO Mut()
        {
            EnemyArchetypeSO a = M3TestData.Scv();
            a.id = "AI-MUT";
            a.tactics = EnemyTacticsKind.Mutant;
            a.loadouts = new List<EnemyLoadout>();
            a.hp = 60f; a.headHp = 30f; a.legsHp = 50f;
            a.sightRange = 40f; a.sightAngle = 140f; a.hearingMultiplier = 1.5f;
            a.patrolSpeed = 1.2f; a.moveSpeed = 4f; a.sprintSpeed = 5f;
            a.reactionTimeSec = 0f;
            a.usesCover = false; a.flanks = false; a.investigatesNoise = true;
            a.meleeDamage = 14f; a.meleeRangeM = 1.8f; a.meleeIntervalSec = 1.1f; a.meleeWindupSec = 0.3f;
            a.surroundRadiusM = 2.5f;
            return a;
        }

        private EnemyBrain Make(EnemyArchetypeSO a, int id = 1, Squad squad = null, int order = 0,
                                IReadOnlyList<Vector3> route = null) =>
            new EnemyBrain(id, a, config, world, route, squad, new System.Random(id), order);

        private static float Flat(Vector3 a, Vector3 b) => MutantSurround.HorizontalDistance(a, b);

        // 피격(직접 감지) → 보이는 채로 2틱 = Alert → Combat (반응 0초)
        private static void EnterCombat(EnemyBrain brain, Vector3 self, Vector3 target, ref float t)
        {
            brain.OnDamaged(target, t);
            t += 0.1f;
            brain.Tick(AiTest.Seen(t, self, target), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            t += 0.1f;
            brain.Tick(AiTest.Seen(t, self, target), 0.1f);
            Assert.AreEqual(EnemyState.Combat, brain.State);
        }

        [Test]
        public void Factory_MakesMutant()
        {
            Assert.AreEqual(EnemyTacticsKind.Mutant, Make(Mut()).TacticsKind);
        }

        // ── 무리 둘레 분산 (Q25) ──

        [Test]
        public void Pack4_Combat_SurroundPointsDiffer_Sprint_MeleeIntent_NoCover()
        {
            world.AddCover(new Vector3(3f, 0f, 3f), Vector3.forward);
            world.AddCover(new Vector3(-3f, 0f, 3f), Vector3.forward, CoverHeight.High);
            var squad = new Squad(5, config);
            EnemyArchetypeSO a = Mut();
            var selves = new[] { new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(6f, 0f, 0f) };
            var brains = new EnemyBrain[4];
            for (int k = 0; k < 4; k++)
                brains[k] = Make(a, k + 1, squad, k);
            Vector3 target = new Vector3(3f, 0f, 30f);

            float t = 0f;
            for (int k = 0; k < 4; k++)
            {
                float tk = t;
                EnterCombat(brains[k], selves[k], target, ref tk);
            }

            var points = new List<Vector3>();
            for (int k = 0; k < 4; k++)
            {
                EnemyIntent i = brains[k].Intent;
                Assert.AreEqual(EnemyMoveMode.Sprint, i.Move, $"{k}: 질주");
                Assert.IsTrue(i.HasDestination, $"{k}: 목적지");
                Assert.AreEqual(a.surroundRadiusM, Flat(i.Destination, target), 1e-3f, $"{k}: 표적 둘레 반경");
                Assert.IsTrue(i.Melee, $"{k}: 근접 의도");
                Assert.IsFalse(i.Fire, $"{k}: 사격 없음");
                Assert.IsTrue(i.LookAtPoint, $"{k}: 표적을 본다");
                Assert.AreEqual(target + Vector3.up * AiTest.CenterHeight, i.LookPoint);
                points.Add(i.Destination);
            }
            for (int p = 0; p < 4; p++)
                for (int q = p + 1; q < 4; q++)
                    Assert.Greater(Flat(points[p], points[q]), 3f, $"자리 {p}·{q}가 서로 다름 (한 줄로 몰리지 않음)");

            Assert.AreEqual(0, world.TryFindCoverCalls, "엄폐 질의 없음");
            Assert.AreEqual(-1, world.ReservedBy(1));
            Assert.AreEqual(-1, squad.FlankHolder, "측면 우회권 없음");
        }

        [Test]
        public void Leader_GoesStraight_OthersRotateByQuarter()
        {
            var squad = new Squad(5, config);
            EnemyArchetypeSO a = Mut();
            var brains = new EnemyBrain[4];
            for (int k = 0; k < 4; k++)
                brains[k] = Make(a, k + 1, squad, k);
            Vector3 self = Vector3.zero;
            Vector3 target = new Vector3(0f, 0f, 20f);
            float t = 0f;
            for (int k = 0; k < 4; k++)
            {
                float tk = t;
                EnterCombat(brains[k], self, target, ref tk);
            }
            // 기준 = 표적 → 리더(-z). 순번 k = 90° × k
            Assert.That(Vector3.Distance(brains[0].Intent.Destination, target + new Vector3(0f, 0f, -2.5f)), Is.LessThan(1e-3f), "리더: 정면");
            Assert.That(Vector3.Distance(brains[2].Intent.Destination, target + new Vector3(0f, 0f, 2.5f)), Is.LessThan(1e-3f), "순번 2: 반대편");
        }

        [Test]
        public void Alert_Visible_AlreadySprintsWithMelee()
        {
            EnemyBrain brain = Make(Mut());
            Vector3 target = new Vector3(0f, 0f, 20f);
            brain.OnDamaged(target, 0f);
            brain.Tick(AiTest.Seen(0.1f, Vector3.zero, target), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(EnemyMoveMode.Sprint, brain.Intent.Move);
            Assert.IsTrue(brain.Intent.Melee);
            Assert.AreEqual(2.5f, Flat(brain.Intent.Destination, target), 1e-3f, "단독 = 자기 쪽 둘레");
        }

        [Test]
        public void Close_TightensToMeleeRadius_InsideHolds()
        {
            EnemyArchetypeSO a = Mut();
            EnemyBrain brain = Make(a);
            Vector3 target = new Vector3(0f, 0f, 3.5f);   // 3.5 ≤ 2.5 + 1.8 → 가까움
            float t = 0f;
            EnterCombat(brain, Vector3.zero, target, ref t);
            Assert.AreEqual(EnemyMoveMode.Sprint, brain.Intent.Move);
            Assert.AreEqual(a.meleeRangeM * config.mutantCloseRadiusRatio, Flat(brain.Intent.Destination, target), 1e-3f);

            // 사거리 × 0.75 안쪽 = 멈추고 표적을 본다(근접 의도 유지)
            t += 0.1f;
            Vector3 self = new Vector3(0f, 0f, 2.5f);   // 1.0m
            brain.Tick(AiTest.Seen(t, self, target), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move);
            Assert.IsFalse(brain.Intent.HasDestination);
            Assert.IsTrue(brain.Intent.Melee);
            Assert.IsTrue(brain.Intent.LookAtPoint);
        }

        [Test]
        public void Combat_LostSight_SprintsToSurroundPointAroundLastKnown()
        {
            EnemyArchetypeSO a = Mut();
            EnemyBrain brain = Make(a);
            Vector3 target = new Vector3(0f, 0f, 20f);
            float t = 0f;
            EnterCombat(brain, Vector3.zero, target, ref t);
            t += 0.1f;
            brain.Tick(AiTest.Unseen(t, Vector3.zero), 0.1f);
            Assert.AreEqual(EnemyState.Combat, brain.State, "loseSightSec 전");
            Assert.AreEqual(EnemyMoveMode.Sprint, brain.Intent.Move);
            // 7B 리뷰 15: LKP(표적 발)로 곧장 가지 않고 LKP 둘레 자리 (단독 = 자기 쪽 2.5m)
            Assert.AreEqual(a.surroundRadiusM, Flat(brain.Intent.Destination, target), 1e-3f, "LKP 둘레");
            Assert.Less(brain.Intent.Destination.z, target.z, "자기 쪽");
            Assert.IsTrue(brain.Intent.Melee);
            Assert.IsFalse(brain.Intent.Fire);
        }

        // 7B 리뷰 15: 안 보여도 LKP와 사거리 × mutantHoldInsideRangeRatio 안쪽이면 멈춘다 (표적 발까지 밀고 들어가 겹치지 않음)
        [Test]
        public void Combat_LostSight_NearLastKnown_HoldsInsideRatio_NeverDestinationAtLkp()
        {
            EnemyArchetypeSO a = Mut();
            EnemyBrain brain = Make(a);
            Vector3 target = new Vector3(0f, 0f, 3.5f);
            float t = 0f;
            EnterCombat(brain, Vector3.zero, target, ref t);

            // 안 보이는 채로 LKP 쪽으로 다가간다: 목적지는 늘 LKP에서 ≥ 사거리 × 0.5, 1.35m 안쪽이면 Hold
            float holdAt = a.meleeRangeM * config.mutantHoldInsideRangeRatio;
            for (float z = 0f; z <= 3.0f; z += 0.25f)
            {
                t += 0.1f;
                Vector3 self = new Vector3(0f, 0f, z);
                brain.Tick(AiTest.Unseen(t, self), 0.1f);
                Assert.AreEqual(EnemyState.Combat, brain.State);
                float d = Flat(self, target);
                if (d <= holdAt)
                {
                    Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move, $"{d:0.00}m: 멈춤");
                    Assert.IsFalse(brain.Intent.HasDestination);
                    Assert.IsTrue(brain.Intent.LookAtPoint, "LKP 쪽을 본다");
                }
                else
                {
                    Assert.IsTrue(brain.Intent.HasDestination, $"{d:0.00}m: 이동");
                    Assert.GreaterOrEqual(Flat(brain.Intent.Destination, target),
                        a.meleeRangeM * config.mutantCloseRadiusRatio - 1e-3f, $"{d:0.00}m: 목적지가 LKP 위가 아님");
                }
            }
        }

        // 7B 리뷰 3: 둘레 비율은 AiConfigSO 값 (바꾸면 따라간다)
        [Test]
        public void SurroundRatios_ReadFromConfig()
        {
            config.mutantCloseRadiusRatio = 0.4f;
            config.mutantHoldInsideRangeRatio = 0.9f;
            EnemyArchetypeSO a = Mut();
            EnemyBrain brain = Make(a);
            Vector3 target = new Vector3(0f, 0f, 3.5f);
            float t = 0f;
            EnterCombat(brain, Vector3.zero, target, ref t);
            Assert.AreEqual(a.meleeRangeM * 0.4f, Flat(brain.Intent.Destination, target), 1e-3f, "가까울 때 반경 = 사거리 × 0.4");

            t += 0.1f;
            Vector3 self = new Vector3(0f, 0f, 3.5f - a.meleeRangeM * 0.85f);   // 0.85 × 사거리 ≤ 0.9 × 사거리
            brain.Tick(AiTest.Seen(t, self, target), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move, "멈춤 거리 = 사거리 × 0.9");
        }

        // ── 평시·의심 ──

        [Test]
        public void Suspicious_WalksToNoise()
        {
            EnemyBrain brain = Make(Mut());
            brain.Tick(AiTest.NoTarget(0.1f, Vector3.zero), 0.1f);
            Vector3 noise = new Vector3(5f, 0f, 12f);
            brain.OnHeard(noise, NoiseKind.Footstep, 0.1f);
            brain.Tick(AiTest.Unseen(0.2f, Vector3.zero), 0.1f);
            Assert.AreEqual(EnemyState.Suspicious, brain.State);
            Assert.AreEqual(EnemyMoveMode.Walk, brain.Intent.Move);
            Assert.AreEqual(noise, brain.Intent.Destination);
            Assert.IsFalse(brain.Intent.Melee, "의심 중 근접 의도 없음");
        }

        [Test]
        public void Patrol_DefaultWalk()
        {
            var route = new List<Vector3> { new Vector3(0f, 0f, 10f), new Vector3(10f, 0f, 10f) };
            EnemyBrain brain = Make(Mut(), route: route);
            float t = 0f;
            for (int k = 0; k < 40 && brain.State != EnemyState.Patrol; k++)
            {
                t += 0.2f;
                brain.Tick(AiTest.NoTarget(t, Vector3.zero), 0.2f);
            }
            t += 0.2f;
            brain.Tick(AiTest.NoTarget(t, Vector3.zero), 0.2f);
            Assert.AreEqual(EnemyState.Patrol, brain.State);
            Assert.AreEqual(EnemyMoveMode.Walk, brain.Intent.Move, "느린 배회 = 걷기(patrolSpeed)");
            Assert.IsFalse(brain.Intent.Melee);
        }

        // ── 순수 둘레 계산 ──

        [Test]
        public void Surround_Static_RadiusAndAngles()
        {
            Vector3 target = new Vector3(10f, 1f, 10f);
            Vector3 self = new Vector3(10f, 0f, -10f);
            // 단독: 자기 쪽
            Vector3 p = MutantSurround.ApproachPoint(target, self, self, -1, 1, 2.5f, 1.8f, 0.5f);
            Assert.That(Vector3.Distance(p, target + new Vector3(0f, 0f, -2.5f)), Is.LessThan(1e-4f));
            // 3명: 120° 간격, 모두 반경 2.5
            var pts = new Vector3[3];
            for (int k = 0; k < 3; k++)
            {
                pts[k] = MutantSurround.ApproachPoint(target, self, self, k, 3, 2.5f, 1.8f, 0.5f);
                Assert.AreEqual(2.5f, Flat(pts[k], target), 1e-4f);
                Assert.AreEqual(target.y, pts[k].y, 1e-5f, "높이는 표적 발");
            }
            Assert.AreEqual(2.5f * Mathf.Sqrt(3f), Vector3.Distance(pts[0], pts[1]), 1e-3f);
            // 반경 0 = 표적 위치
            Assert.AreEqual(target, MutantSurround.ApproachPoint(target, self, self, 1, 3, 0f, 0f, 0.5f));
        }

        [Test]
        public void CombatTick_NoAllocation()
        {
            var squad = new Squad(5, config);
            EnemyBrain a = Make(Mut(), 1, squad, 0);
            EnemyBrain b = Make(Mut(), 2, squad, 1);
            Vector3 target = new Vector3(0f, 0f, 20f);
            float t = 0f;
            EnterCombat(a, Vector3.zero, target, ref t);
            float t2 = 0f;
            EnterCombat(b, new Vector3(2f, 0f, 0f), target, ref t2);
            float now = t;
            AiTest.AssertNoAlloc(() =>
            {
                for (int k = 0; k < 50; k++)
                {
                    now += 0.1f;
                    a.Tick(AiTest.Seen(now, Vector3.zero, target), 0.1f);
                    b.Tick(AiTest.Seen(now, new Vector3(2f, 0f, 0f), target), 0.1f);
                }
            });
        }
    }
}
