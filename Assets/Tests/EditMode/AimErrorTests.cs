using Ashfall.AI;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 적 명중 오차 (M3 설계 §1.4). 값은 AI-SCV 표 (TODO(GDD-?)).
    public class AimErrorTests
    {
        private EnemyArchetypeSO scv;

        [SetUp]
        public void SetUp() => scv = M3TestData.Scv();

        [Test]
        public void Radius_SettledStationary_BasePlusPerMeter()
        {
            // 0.3 + 0.04 × 20 = 1.1
            Assert.AreEqual(1.1f, AimError.Radius(scv, 20f, 10f, false, false, 0), 1e-4f);
        }

        [Test]
        public void Radius_Unsettled_ShrinksToOneOverSettleTime()
        {
            Assert.AreEqual(2.2f, AimError.Radius(scv, 20f, 0f, false, false, 0), 1e-4f, "처음 본 순간 ×2");
            Assert.AreEqual(1.65f, AimError.Radius(scv, 20f, 0.75f, false, false, 0), 1e-4f, "절반 → ×1.5");
            Assert.AreEqual(1.1f, AimError.Radius(scv, 20f, 1.5f, false, false, 0), 1e-4f);
        }

        [Test]
        public void Radius_MovementMultipliers()
        {
            Assert.AreEqual(2.2f, AimError.Radius(scv, 20f, 10f, true, false, 0), 1e-4f, "자신 이동 ×2");
            Assert.AreEqual(1.65f, AimError.Radius(scv, 20f, 10f, false, true, 0), 1e-4f, "표적 이동 ×1.5");
            Assert.AreEqual(3.3f, AimError.Radius(scv, 20f, 10f, true, true, 0), 1e-4f);
        }

        [Test]
        public void Radius_BurstGrowthPerShot()
        {
            Assert.AreEqual(1.1f + 0.08f * 3, AimError.Radius(scv, 20f, 10f, false, false, 3), 1e-4f);
        }

        [Test]
        public void Direction_ZeroRadius_ExactlyAtAimPoint()
        {
            var muzzle = new Vector3(0f, 1.4f, 0f);
            var aim = new Vector3(3f, 1f, 20f);
            Vector3 dir = AimError.Direction(muzzle, aim, 0f, new System.Random(1));
            Assert.Less(Vector3.Angle((aim - muzzle).normalized, dir), 1e-3f);
        }

        [Test]
        public void Direction_OffsetsLieWithinDiscPerpendicularToLine()
        {
            var muzzle = new Vector3(0f, 1.4f, 0f);
            var aim = new Vector3(0f, 1f, 20f);
            Vector3 line = aim - muzzle;
            float radius = 1.5f;
            var random = new System.Random(42);
            Vector3 sum = Vector3.zero;
            int outsideHalf = 0;
            const int n = 2000;
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = AimError.Direction(muzzle, aim, radius, random);
                Assert.AreEqual(1f, dir.magnitude, 1e-4f);
                // 사격선에 수직인 평면(조준점을 지남)과의 교점
                float t = Vector3.Dot(line, line) / Vector3.Dot(line, dir);
                Vector3 offset = muzzle + dir * t - aim;
                Assert.LessOrEqual(offset.magnitude, radius + 1e-3f);
                Assert.AreEqual(0f, Vector3.Dot(offset, line.normalized), 1e-3f);
                sum += offset;
                if (offset.magnitude > radius / Mathf.Sqrt(2f))
                    outsideHalf++;
            }
            Assert.Less((sum / n).magnitude, 0.1f, "평균은 조준점 (치우침 없음)");
            // 균일 원판이면 반지름 r/√2 바깥이 면적의 절반
            Assert.That(outsideHalf / (float)n, Is.InRange(0.45f, 0.55f));
        }

        [Test]
        public void Direction_SameSeed_SameResult()
        {
            Vector3 a = AimError.Direction(Vector3.zero, Vector3.forward * 10f, 1f, new System.Random(5));
            Vector3 b = AimError.Direction(Vector3.zero, Vector3.forward * 10f, 1f, new System.Random(5));
            Assert.AreEqual(a, b);
        }
    }
}
