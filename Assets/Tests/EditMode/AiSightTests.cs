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
}
