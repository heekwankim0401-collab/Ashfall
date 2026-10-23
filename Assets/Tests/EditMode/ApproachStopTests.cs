using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 7D-4 (7D-3 발견 1): 약탈자·습격자(엄폐 없음 대체 = 약탈자 경로, 보스·호위도 같은 경로)가 LKP·표적 위치로 다가갈 때
    // AiConfigSO.approachStopDistanceM(2.0, TODO(GDD-?)) 앞에서 멈춘다. 목적지·도착 판정이 같은 점. 수색은 그대로.
    public class ApproachStopTests
    {
        private AiConfigSO config;
        private FakeEnemyWorld world;
        private static readonly Vector3 Self = Vector3.zero;

        [SetUp]
        public void SetUp()
        {
            config = M3TestData.AiConfig();
            world = new FakeEnemyWorld();
        }

        private EnemyBrain Make(EnemyArchetypeSO archetype, IReadOnlyList<Vector3> route = null) =>
            new EnemyBrain(1, archetype, config, world, route, null, new System.Random(7));

        private static float Flat(Vector3 a, Vector3 b)
        {
            Vector3 d = a - b;
            d.y = 0f;
            return d.magnitude;
        }

        // ── ApproachRules (순수) ──

        [Test]
        public void Default_IsTwoMeters()
        {
            Assert.AreEqual(2f, config.approachStopDistanceM, 1e-6f, "코드 초기값 = 설계 임시값 2.0 (TODO(GDD-?))");
        }

        [Test]
        public void StopShort_PullsTowardSelf_Horizontal_KeepsTargetHeight()
        {
            var self = new Vector3(0f, 5f, 0f);
            var target = new Vector3(0f, 1f, 10f);
            Vector3 p = ApproachRules.StopShort(self, target, 2f);
            Assert.AreEqual(0f, p.x, 1e-4f);
            Assert.AreEqual(8f, p.z, 1e-4f);
            Assert.AreEqual(1f, p.y, 1e-6f, "높이 = 표적 값(NavMesh 샘플이 바닥에 붙인다)");

            Vector3 diag = ApproachRules.StopShort(Vector3.zero, new Vector3(30f, 0f, 40f), 2f);   // 수평 50
            Assert.AreEqual(48f, diag.magnitude, 1e-3f);
            Assert.AreEqual(2f, Flat(diag, new Vector3(30f, 0f, 40f)), 1e-3f);
        }

        [Test]
        public void StopShort_AlreadyInside_ReturnsSelfHorizontal()
        {
            var self = new Vector3(1f, 0f, 0f);
            var target = new Vector3(0f, 2f, 0.5f);
            Vector3 p = ApproachRules.StopShort(self, target, 2f);
            Assert.AreEqual(new Vector3(1f, 2f, 0f), p, "더 다가가지 않는다");
            Assert.IsTrue(ApproachRules.IsWithinStop(self, target, 2f));
            Assert.IsFalse(ApproachRules.IsWithinStop(new Vector3(0f, 0f, 3f), Vector3.zero, 2f));
            Assert.IsTrue(ApproachRules.IsWithinStop(new Vector3(0f, 0f, 2f), Vector3.zero, 2f), "경계 = 안");
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void StopShort_ZeroOrInvalid_TargetUnchanged(float stop)
        {
            var target = new Vector3(3f, 1f, 4f);
            Assert.AreEqual(target, ApproachRules.StopShort(Vector3.zero, target, stop));
            Assert.IsFalse(ApproachRules.IsWithinStop(target, target, stop));
        }

        // ── EnemyBrain 연결 ──

        [Test]
        public void Scavenger_Alert_DestinationStopsShortOfLkp_AndSampled()
        {
            world.NavPointRule = (p, r) => new Vector3(p.x, 0.3f, p.z);   // 바닥 높이 0.3
            EnemyBrain brain = Make(M3TestData.Scv());
            var shot = new Vector3(0f, 1.4f, 20f);
            brain.OnHeard(shot, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(EnemyMoveMode.Run, brain.Intent.Move);
            Assert.AreEqual(2f, Flat(brain.Intent.Destination, brain.LastKnownPosition), 1e-3f, "LKP에서 2m 앞");
            Assert.Less(brain.Intent.Destination.z, brain.LastKnownPosition.z, "자기 쪽");
            Assert.AreEqual(0.3f, brain.Intent.Destination.y, 1e-4f, "NavMesh에 붙임");

            // 같은 LKP를 다시 물어도 같은 점, NavMesh 질의는 늘지 않는다(캐시)
            Vector3 d = brain.Intent.Destination;
            int calls = world.SampleNavCalls;
            brain.Tick(AiTest.Unseen(0.2f, new Vector3(0.5f, 0f, 3f)), 0.1f);
            Assert.AreEqual(d, brain.Intent.Destination);
            Assert.AreEqual(calls, world.SampleNavCalls, "캐시");
        }

        [Test]
        public void Scavenger_Alert_ArrivesAtStopPoint_HoldsAndLooksAtLkp()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            var shot = new Vector3(0f, 0f, 20f);
            brain.OnHeard(shot, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, Self), 0.1f);
            Vector3 stop = brain.Intent.Destination;

            brain.Tick(AiTest.Unseen(0.2f, stop), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move, "정지 점 = 도착");
            Assert.IsFalse(brain.Intent.HasDestination);
            Assert.IsTrue(brain.Intent.LookAtPoint);
            Assert.AreEqual(brain.LastKnownPosition, brain.Intent.LookPoint, "LKP 쪽을 본다");
        }

        [Test]
        public void Scavenger_Alert_AlreadyWithinStop_DoesNotStepIn()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            var self = new Vector3(0f, 0f, 18.6f);   // LKP에서 1.4m (pointArriveDistanceM 1.0 밖, 정지 거리 2.0 안)
            brain.OnHeard(new Vector3(0f, 0f, 20f), NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move, "이미 정지 거리 안 → 더 다가가지 않음");
            Assert.IsFalse(brain.Intent.HasDestination);
        }

        [Test]
        public void ZeroStop_M3Behavior_RunsToLkpItself()
        {
            config.approachStopDistanceM = 0f;
            EnemyBrain brain = Make(M3TestData.Scv());
            var shot = new Vector3(20f, 0f, 20f);
            brain.OnHeard(shot, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, Self), 0.1f);
            Assert.AreEqual(shot, brain.Intent.Destination);
        }

        [Test]
        public void Raider_NoCoverFallback_AlsoStopsShort()
        {
            world.ForcedCoverResult = CoverResult.None;
            EnemyBrain brain = Make(M3TestData.Rdr());
            var shot = new Vector3(0f, 0f, 25f);
            brain.OnHeard(shot, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Run, brain.Intent.Move);
            Assert.AreEqual(new Vector3(0f, 0f, 23f), brain.Intent.Destination);
        }

        [Test]
        public void Scavenger_CombatFarVisible_ApproachStopsShortOfTarget()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            var target = new Vector3(0f, 0f, 35f);   // 선호 거리(8~20) 밖
            brain.OnHeard(target, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Seen(0f, Self, target), 0.1f);
            brain.Tick(AiTest.Seen(1f, Self, target), 1f);
            Assert.AreEqual(EnemyState.Combat, brain.State);
            brain.Tick(AiTest.Seen(1.1f, Self, target), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Run, brain.Intent.Move);
            Assert.AreEqual(new Vector3(0f, 0f, 33f), brain.Intent.Destination);
        }

        [Test]
        public void Anchored_StopPointStillClampedToAnchor()
        {
            EnemyArchetypeSO a = M3TestData.Scv();
            a.anchorRadiusM = 10f;
            EnemyBrain brain = Make(a);
            brain.OnHeard(new Vector3(0f, 0f, 50f), NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, Self), 0.1f);
            Assert.AreEqual(10f, brain.Intent.Destination.z, 1e-4f, "정지 점(48m)도 거점 반경으로 잘린다");
        }
    }
}
