using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // §7 시야: 콘 110°, 60m, 웅크림 ×0.6, 엎드림 ×0.3 (SYS-AI, M3 AC1)
    public class AiSightTests
    {
        private AiConfigSO config;

        [SetUp]
        public void SetUp() => config = M3TestData.AiConfig();

        private static Vector3 AtAngle(float deg, float distance) =>
            Quaternion.Euler(0f, deg, 0f) * Vector3.forward * distance;

        [Test]
        public void Cone_54DegInside_56DegOutside()
        {
            Assert.IsTrue(SightCone.InCone(Vector3.zero, Vector3.forward, AtAngle(54f, 20f), 110f, 60f));
            Assert.IsTrue(SightCone.InCone(Vector3.zero, Vector3.forward, AtAngle(-54f, 20f), 110f, 60f));
            Assert.IsFalse(SightCone.InCone(Vector3.zero, Vector3.forward, AtAngle(56f, 20f), 110f, 60f));
            Assert.IsFalse(SightCone.InCone(Vector3.zero, Vector3.forward, AtAngle(-56f, 20f), 110f, 60f));
            Assert.IsFalse(SightCone.InCone(Vector3.zero, Vector3.forward, AtAngle(180f, 5f), 110f, 60f), "등 뒤");
        }

        [Test]
        public void Cone_VerticalAngleUsesSame3DAngle()
        {
            Vector3 up54 = Quaternion.Euler(-54f, 0f, 0f) * Vector3.forward * 10f;
            Vector3 up56 = Quaternion.Euler(-56f, 0f, 0f) * Vector3.forward * 10f;
            Assert.IsTrue(SightCone.InCone(Vector3.zero, Vector3.forward, up54, 110f, 60f));
            Assert.IsFalse(SightCone.InCone(Vector3.zero, Vector3.forward, up56, 110f, 60f));
        }

        [Test]
        public void Range_60Inside_61Outside()
        {
            Assert.IsTrue(SightCone.InCone(Vector3.zero, Vector3.forward, Vector3.forward * 60f, 110f, 60f));
            Assert.IsFalse(SightCone.InCone(Vector3.zero, Vector3.forward, Vector3.forward * 61f, 110f, 60f));
        }

        [Test]
        public void EffectiveRange_ByPosture()
        {
            Assert.AreEqual(60f, SightCone.EffectiveRange(60f, TargetPosture.Standing, config), 1e-4f);
            Assert.AreEqual(36f, SightCone.EffectiveRange(60f, TargetPosture.Crouching, config), 1e-4f);
            Assert.AreEqual(18f, SightCone.EffectiveRange(60f, TargetPosture.Prone, config), 1e-4f);
        }

        // M6 재 폭풍 시야 상한 (§8.3, Q26): min(자세 반영 거리, cap)
        [Test]
        public void EffectiveRange_Cap_InfinityKeepsExisting()
        {
            foreach (TargetPosture p in new[] { TargetPosture.Standing, TargetPosture.Crouching, TargetPosture.Prone })
                Assert.AreEqual(SightCone.EffectiveRange(60f, p, config),
                    SightCone.EffectiveRange(60f, p, config, float.PositiveInfinity), 1e-4f, p.ToString());
        }

        [TestCase(TargetPosture.Standing, 30f, 30f)]     // 60 vs 30 → 30
        [TestCase(TargetPosture.Crouching, 30f, 30f)]    // 36 vs 30 → 30
        [TestCase(TargetPosture.Prone, 30f, 18f)]        // 18 vs 30 → 18
        [TestCase(TargetPosture.Standing, 450f, 60f)]    // 맑을 때 가시거리는 시야보다 멀다 → 기존
        [TestCase(TargetPosture.Standing, 0f, 0f)]
        [TestCase(TargetPosture.Standing, -5f, 0f)]      // cap ≤ 0 → 0
        [TestCase(TargetPosture.Standing, float.NaN, 60f)]   // 잘못된 값 → 상한 없음
        public void EffectiveRange_Cap(TargetPosture posture, float cap, float expected)
        {
            Assert.AreEqual(expected, SightCone.EffectiveRange(60f, posture, config, cap), 1e-4f);
        }

        // 폭풍 Peak(30m): 선 자세 45m는 안 보이고 20m는 보인다. 콘 밖이면 레이를 쏘지 않는다.
        [TestCase(45f, false)]
        [TestCase(31f, false)]
        [TestCase(29f, true)]
        [TestCase(20f, true)]
        public void StormCap30_StandingVisibility(float distance, bool expected)
        {
            var los = new FakeLineOfSight();
            float range = SightCone.EffectiveRange(60f, TargetPosture.Standing, config, 30f);
            Vector3 eye = Vector3.up * 1.6f;
            Vector3 center = eye + Vector3.forward * distance;
            Assert.AreEqual(expected, SightCone.IsVisible(eye, Vector3.forward, center, center, 110f, range, los, null, null));
            if (!expected)
                Assert.AreEqual(0, los.TotalCalls, "상한 밖이면 레이 없음");
        }

        [TestCase(TargetPosture.Standing, 59f, true)]
        [TestCase(TargetPosture.Crouching, 35f, true)]
        [TestCase(TargetPosture.Crouching, 37f, false)]
        [TestCase(TargetPosture.Prone, 17f, true)]
        [TestCase(TargetPosture.Prone, 19f, false)]
        public void Posture_VisibilityAtDistance(TargetPosture posture, float distance, bool expected)
        {
            float range = SightCone.EffectiveRange(60f, posture, config);
            Vector3 eye = Vector3.up * 1.6f;
            Vector3 center = eye + Vector3.forward * distance;
            bool visible = SightCone.IsVisible(eye, Vector3.forward, center, center, 110f, range,
                new FakeLineOfSight(), null, null);
            Assert.AreEqual(expected, visible);
        }

        [Test]
        public void Blocked_NotVisible()
        {
            var los = new FakeLineOfSight((from, to) => true);
            Vector3 eye = Vector3.up * 1.6f;
            bool visible = SightCone.IsVisible(eye, Vector3.forward, eye + Vector3.forward * 10f,
                Vector3.up * 0.9f + Vector3.forward * 10f, 110f, 60f, los, null, null);
            Assert.IsFalse(visible);
            Assert.AreEqual(2, los.CanSeeCalls, "눈 → 표적 눈, 실패 시 표적 중심 (최대 2레이)");
        }

        [Test]
        public void EyeBlocked_CenterVisible_IsVisible()
        {
            // 표적 눈(높이 1.6 이상)으로 가는 레이만 막힘
            var los = new FakeLineOfSight((from, to) => to.y > 1.5f);
            Vector3 eye = Vector3.up * 1.6f;
            Vector3 targetEye = new Vector3(0f, 1.6f, 10f);
            Vector3 targetCenter = new Vector3(0f, 0.9f, 10f);
            Assert.IsTrue(SightCone.IsVisible(eye, Vector3.forward, targetEye, targetCenter, 110f, 60f, los, null, null));
        }

        [Test]
        public void OutsideCone_NoRaycast()
        {
            var los = new FakeLineOfSight();
            Vector3 eye = Vector3.up * 1.6f;
            Vector3 behind = eye - Vector3.forward * 10f;
            Assert.IsFalse(SightCone.IsVisible(eye, Vector3.forward, behind, behind, 110f, 60f, los, null, null));
            Assert.AreEqual(0, los.TotalCalls, "콘·거리 밖이면 레이를 쏘지 않는다");
        }

        [Test]
        public void HitRegion_ByHeightRatio()
        {
            // 높이 비율 0.86/0.84/0.46/0.44 (머리 ≥ 0.85, 몸통 ≥ 0.45)
            Assert.AreEqual(EnemyHitRegion.Head, EnemyHitResolver.Resolve(0.86f * 1.8f, 1.8f, config));
            Assert.AreEqual(EnemyHitRegion.Body, EnemyHitResolver.Resolve(0.84f * 1.8f, 1.8f, config));
            Assert.AreEqual(EnemyHitRegion.Body, EnemyHitResolver.Resolve(0.46f * 1.8f, 1.8f, config));
            Assert.AreEqual(EnemyHitRegion.Legs, EnemyHitResolver.Resolve(0.44f * 1.8f, 1.8f, config));
            // 웅크림 캡슐(1.2)도 비율로
            Assert.AreEqual(EnemyHitRegion.Head, EnemyHitResolver.Resolve(1.1f, 1.2f, config));
        }
    }

    // M6 적 발소리 걸음 선택 (§11, Q13, 설계 §3.6): NavMeshAgent 수평 속도 → 걸음 종류·보폭
    public class EnemyFootstepGaitTests
    {
        private AiConfigSO config;

        [SetUp]
        public void SetUp()
        {
            config = M3TestData.AiConfig();
            config.footstepStrideWalkM = 1.5f;
            config.footstepStrideRunM = 2.2f;
            config.footstepRunSpeedMps = 3.5f;
            config.footstepMinSpeedMps = 0.3f;
        }

        [TestCase(0f)]
        [TestCase(0.29f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void BelowMinSpeed_NoStep(float speed)
        {
            Assert.IsFalse(EnemyAgent.TryPickFootstep(speed, false, config, out _, out _));
            Assert.IsFalse(EnemyAgent.TryPickFootstep(speed, true, config, out _, out _));
        }

        [TestCase(0.3f, FootstepGait.Walk, 1.5f)]
        [TestCase(1.5f, FootstepGait.Walk, 1.5f)]    // 순찰
        [TestCase(3.2f, FootstepGait.Walk, 1.5f)]    // RDR 이동
        [TestCase(3.5f, FootstepGait.Walk, 1.5f)]    // 경계값은 걷기 ("보다 빠르면")
        [TestCase(3.51f, FootstepGait.Sprint, 2.2f)]
        [TestCase(5.5f, FootstepGait.Sprint, 2.2f)]  // 질주
        public void Standing_BySpeed(float speed, FootstepGait gait, float stride)
        {
            Assert.IsTrue(EnemyAgent.TryPickFootstep(speed, false, config, out FootstepGait g, out float s));
            Assert.AreEqual(gait, g);
            Assert.AreEqual(stride, s, 1e-5f);
        }

        [Test]
        public void Crouched_Crouch_WalkStride()
        {
            Assert.IsTrue(EnemyAgent.TryPickFootstep(5f, true, config, out FootstepGait g, out float s));
            Assert.AreEqual(FootstepGait.Crouch, g);
            Assert.AreEqual(1.5f, s, 1e-5f);
        }

        [Test]
        public void ZeroStride_NoStep_NullConfig_NoStep()
        {
            config.footstepStrideWalkM = 0f;
            Assert.IsFalse(EnemyAgent.TryPickFootstep(1.5f, false, config, out _, out _));
            Assert.IsFalse(EnemyAgent.TryPickFootstep(1.5f, false, null, out _, out _));
        }

        // 순찰 1.5m/s × 60fps: 걸음 간격 = 보폭 / 속도 = 1초 (첫 걸음은 보폭 절반 = 0.5초)
        [Test]
        public void Patrol_CadenceWithStrideCadence_OneStepPerSecond()
        {
            var cadence = new StrideCadence();
            const float dt = 1f / 60f;
            var times = new System.Collections.Generic.List<float>();
            for (int f = 1; f <= 60 * 5; f++)
            {
                Assert.IsTrue(EnemyAgent.TryPickFootstep(1.5f, false, config, out _, out float stride));
                if (cadence.Tick(1.5f * dt, stride))
                    times.Add(f * dt);
            }
            Assert.AreEqual(0.5f, times[0], 2f * dt, "첫 걸음 = 보폭 절반");
            for (int i = 1; i < times.Count; i++)
                Assert.AreEqual(1f, times[i] - times[i - 1], 2f * dt);
            Assert.AreEqual(5, times.Count);
        }
    }
}
