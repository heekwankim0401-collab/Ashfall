using System.Linq;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7A-2a 산탄 펠릿별 퍼짐 (Q8): 지향 퍼짐은 묶음 방향 1회(조준 시 0), 펠릿마다 pelletSpreadDeg 원뿔(조준 중에도).
    public class PelletSpreadTests
    {
        private const float PelletSpread = 2.5f;   // WPN-SHT-01 (설계 §3.1)
        private const float HipSpread = 3.0f;
        private const int Pellets = 8;             // AMMO-12GA (GDD 18×8)
        private const float Eps = 1e-3f;

        private static readonly Vector3 Forward = new Vector3(0.3f, -0.1f, 1f).normalized;

        [Test]
        public void PelletDirections_EightDistinct_InsideCone()
        {
            var buffer = new Vector3[Pellets];
            int n = Ballistics.PelletDirections(Forward, PelletSpread, Pellets, new System.Random(7), buffer);
            Assert.AreEqual(Pellets, n);
            foreach (Vector3 d in buffer)
            {
                Assert.AreEqual(1f, d.magnitude, 1e-4f, "단위 벡터");
                Assert.LessOrEqual(Vector3.Angle(Forward, d), PelletSpread + Eps, "원뿔 안");
            }
            for (int i = 0; i < Pellets; i++)
                for (int j = i + 1; j < Pellets; j++)
                    Assert.Greater(Vector3.Angle(buffer[i], buffer[j]), 1e-3f, $"펠릿 {i}·{j}가 같은 방향");
        }

        [Test]
        public void Ads_BundleIsForward_PelletsStillSpread()
        {
            var buffer = new Vector3[Pellets];
            Vector3 bundle = Ballistics.ShotDirections(Forward, HipSpread, 1f, PelletSpread, Pellets, new System.Random(11), buffer);
            Assert.Less(Vector3.Angle(Forward, bundle), 1e-3f, "조준 시 묶음 방향 = 정면 (RULE-07)");
            Assert.IsTrue(buffer.Any(d => Vector3.Angle(Forward, d) > 0.1f), "조준 중에도 펠릿은 퍼진다");
            foreach (Vector3 d in buffer)
                Assert.LessOrEqual(Vector3.Angle(Forward, d), PelletSpread + Eps);
        }

        [Test]
        public void Hip_BundleInsideHipCone_PelletsAroundBundle()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var buffer = new Vector3[Pellets];
                Vector3 bundle = Ballistics.ShotDirections(Forward, HipSpread, 0f, PelletSpread, Pellets, new System.Random(seed), buffer);
                Assert.LessOrEqual(Vector3.Angle(Forward, bundle), HipSpread + Eps, "묶음 = 지향 퍼짐 원뿔 안");
                foreach (Vector3 d in buffer)
                {
                    Assert.LessOrEqual(Vector3.Angle(bundle, d), PelletSpread + Eps, "펠릿 = 묶음 둘레 원뿔 안");
                    Assert.LessOrEqual(Vector3.Angle(Forward, d), HipSpread + PelletSpread + Eps);
                }
            }
        }

        // 펠릿 퍼짐 0 + 1발(소총 등)은 예전 Shoot과 같은 방향·난수 순서 (회귀)
        [Test]
        public void SingleBullet_NoPelletSpread_MatchesOldApplySpread()
        {
            var buffer = new Vector3[1];
            var a = new System.Random(42);
            var b = new System.Random(42);
            Vector3 bundle = Ballistics.ShotDirections(Forward, HipSpread, 0.25f, 0f, 1, a, buffer);
            Vector3 old = Ballistics.ApplySpread(Forward, Mathf.Lerp(HipSpread, 0f, 0.25f), b);
            Assert.Less(Vector3.Angle(old, bundle), 1e-5f);
            Assert.Less(Vector3.Angle(old, buffer[0]), 1e-5f);
            Assert.AreEqual(b.Next(), a.Next(), "같은 수의 난수를 썼다");
        }

        [Test]
        public void PelletDirections_ClampsToBuffer()
        {
            var buffer = new Vector3[4];
            Assert.AreEqual(4, Ballistics.PelletDirections(Forward, PelletSpread, Pellets, new System.Random(1), buffer));
            Assert.AreEqual(0, Ballistics.PelletDirections(Forward, PelletSpread, -3, new System.Random(1), buffer));
        }

        [Test]
        public void Spread_IsStatisticallyCentred()
        {
            var random = new System.Random(2024);
            var buffer = new Vector3[Pellets];
            Vector3 sum = Vector3.zero;
            float maxAngle = 0f;
            for (int shot = 0; shot < 200; shot++)
            {
                Ballistics.PelletDirections(Vector3.forward, PelletSpread, Pellets, random, buffer);
                foreach (Vector3 d in buffer)
                {
                    sum += d;
                    maxAngle = Mathf.Max(maxAngle, Vector3.Angle(Vector3.forward, d));
                }
            }
            Assert.Less(Vector3.Angle(Vector3.forward, sum), 0.3f, "평균 방향 = 묶음 방향");
            Assert.Greater(maxAngle, PelletSpread * 0.8f, "원뿔 가장자리 근처까지 퍼진다");
        }
    }
}
