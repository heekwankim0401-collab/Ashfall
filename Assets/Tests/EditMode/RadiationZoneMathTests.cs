using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 방사능 구역률 (§4.3 STAT-RAD, M7 설계 §1.5·§3.10, Q39): (반경 − 감쇠 폭) 안 최대, 가장자리까지 선형 0, 밖 0, 겹치면 합.
    public class RadiationZoneMathTests
    {
        private const float Eps = 1e-5f;

        // 설계 §3.10 RAD-REF 값 (중심 (300, −60), 반경 70, 0.5/s, 감쇠 15)
        private static RadiationZoneConfig Refinery() => Zone(new Vector2(300f, -60f), 70f, 0.5f, 15f);

        private static RadiationZoneConfig Zone(Vector2 center, float radius, float rate, float falloff) =>
            new RadiationZoneConfig { id = "RAD-TEST", centerXZ = center, radiusM = radius, ratePerSec = rate, edgeFalloffM = falloff };

        private static Vector3 At(float x, float z, float y = 0f) => new Vector3(x, y, z);

        [Test]
        public void Center_IsMaxRate()
        {
            var zones = new List<RadiationZoneConfig> { Refinery() };
            Assert.AreEqual(0.5f, RadiationZoneMath.RateAt(zones, At(300f, -60f)), Eps);
        }

        [Test]
        public void InsideInnerRadius_IsMaxRate_IncludingInnerBoundary()
        {
            var zones = new List<RadiationZoneConfig> { Refinery() };
            Assert.AreEqual(0.5f, RadiationZoneMath.RateAt(zones, At(300f + 30f, -60f)), Eps);
            Assert.AreEqual(0.5f, RadiationZoneMath.RateAt(zones, At(300f, -60f + 55f)), Eps, "반경 − 감쇠 폭 = 55m 경계");
        }

        [Test]
        public void EdgeBand_IsLinearToZero()
        {
            var zones = new List<RadiationZoneConfig> { Refinery() };
            // 55m → 0.5, 62.5m → 0.25, 70m → 0
            Assert.AreEqual(0.25f, RadiationZoneMath.RateAt(zones, At(300f + 62.5f, -60f)), Eps);
            Assert.AreEqual(0.5f * (70f - 60f) / 15f, RadiationZoneMath.RateAt(zones, At(300f - 60f, -60f)), Eps);
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(zones, At(300f + 70f, -60f)), Eps, "반경 위 = 0");
        }

        [Test]
        public void EdgeBand_DiagonalUsesHorizontalDistance()
        {
            var zones = new List<RadiationZoneConfig> { Refinery() };
            // 대각 (36, 48) = 60m
            Assert.AreEqual(0.5f * 10f / 15f, RadiationZoneMath.RateAt(zones, At(300f + 36f, -60f + 48f)), Eps);
        }

        [Test]
        public void Outside_IsZero()
        {
            var zones = new List<RadiationZoneConfig> { Refinery() };
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(zones, At(300f + 70.01f, -60f)));
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(zones, At(0f, 0f)));
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(zones, At(-1000f, 1000f)));
        }

        [Test]
        public void Height_IsIgnored()
        {
            var zones = new List<RadiationZoneConfig> { Refinery() };
            Assert.AreEqual(0.5f, RadiationZoneMath.RateAt(zones, At(300f, -60f, 250f)), Eps);
            Assert.AreEqual(0.25f, RadiationZoneMath.RateAt(zones, At(300f + 62.5f, -60f, -40f)), Eps);
        }

        [Test]
        public void Overlapping_ZonesAreSummed()
        {
            var zones = new List<RadiationZoneConfig>
            {
                Zone(Vector2.zero, 20f, 0.5f, 10f),
                Zone(new Vector2(10f, 0f), 20f, 1f, 10f),
            };
            // x = 0: 첫째 최대 0.5, 둘째 d=10 ≤ 10(안쪽) → 1 → 합 1.5
            Assert.AreEqual(1.5f, RadiationZoneMath.RateAt(zones, At(0f, 0f)), Eps);
            // x = 15: 첫째 d=15 → 0.5 × 5/10 = 0.25, 둘째 d=5 → 1 → 1.25
            Assert.AreEqual(1.25f, RadiationZoneMath.RateAt(zones, At(15f, 0f)), Eps);
            // x = 25: 첫째 밖, 둘째 d=15 → 1 × 5/10 = 0.5
            Assert.AreEqual(0.5f, RadiationZoneMath.RateAt(zones, At(25f, 0f)), Eps);
        }

        [Test]
        public void NullOrEmptyZones_IsZero()
        {
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(null, At(0f, 0f)));
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(new List<RadiationZoneConfig>(), At(0f, 0f)));
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(new RadiationZoneConfig[0], At(0f, 0f)), "배열도 받는다");
        }

        [Test]
        public void ZeroFalloff_IsHardEdge()
        {
            var zone = Zone(Vector2.zero, 10f, 2f, 0f);
            Assert.AreEqual(2f, RadiationZoneMath.RateAt(zone, At(9.99f, 0f)), Eps);
            Assert.AreEqual(2f, RadiationZoneMath.RateAt(zone, At(10f, 0f)), Eps, "감쇠 폭 0 = 반경까지 최대");
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(zone, At(10.01f, 0f)));
            Assert.AreEqual(2f, RadiationZoneMath.RateAt(Zone(Vector2.zero, 10f, 2f, -5f), At(9f, 0f)), Eps, "음수 감쇠 = 0");
        }

        [Test]
        public void FalloffAtLeastRadius_IsLinearFromCenter()
        {
            var zone = Zone(Vector2.zero, 10f, 1f, 25f);
            Assert.AreEqual(1f, RadiationZoneMath.RateAt(zone, At(0f, 0f)), Eps);
            Assert.AreEqual(0.5f, RadiationZoneMath.RateAt(zone, At(5f, 0f)), Eps);
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(zone, At(10f, 0f)), Eps);
        }

        [Test]
        public void InvalidZones_AreZero()
        {
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(Zone(Vector2.zero, 0f, 1f, 0f), At(0f, 0f)), "반경 0");
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(Zone(Vector2.zero, -5f, 1f, 0f), At(0f, 0f)), "음수 반경");
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(Zone(Vector2.zero, 10f, 0f, 2f), At(0f, 0f)), "률 0");
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(Zone(Vector2.zero, 10f, -1f, 2f), At(0f, 0f)), "음수 률");
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(Zone(Vector2.zero, float.NaN, 1f, 2f), At(0f, 0f)), "NaN 반경");
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(Zone(Vector2.zero, 10f, float.NaN, 2f), At(0f, 0f)), "NaN 률");
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(Zone(Vector2.zero, float.PositiveInfinity, 1f, 2f), At(0f, 0f)), "무한 반경");
            Assert.AreEqual(1f, RadiationZoneMath.RateAt(Zone(Vector2.zero, 10f, 1f, float.NaN), At(9f, 0f)), Eps, "NaN 감쇠 = 0");
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(Zone(Vector2.zero, 10f, 1f, 2f), At(float.NaN, 0f)), "NaN 위치");
        }

        [Test]
        public void InvalidZone_DoesNotAffectSum()
        {
            var zones = new List<RadiationZoneConfig> { Zone(Vector2.zero, -1f, 5f, 0f), Refinery() };
            Assert.AreEqual(0.5f, RadiationZoneMath.RateAt(zones, At(300f, -60f)), Eps);
        }

        [Test]
        public void RaidConfig_DefaultZones_EmptyList()
        {
            var config = ScriptableObject.CreateInstance<RaidConfigSO>();
            try
            {
                Assert.IsNotNull(config.radiationZones);
                Assert.AreEqual(0, config.radiationZones.Count);
                Assert.AreEqual(0f, RadiationZoneMath.RateAt(config.radiationZones, At(300f, -60f)));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void IRadiationField_ReturnsOnlyRate()
        {
            // 계약 확인: 구역 서비스는 위치 → 률 하나만 (Core, 레이드 밖엔 등록 없음)
            var methods = typeof(IRadiationField).GetMethods();
            Assert.AreEqual(1, methods.Length);
            Assert.AreEqual(typeof(float), methods[0].ReturnType);
        }
    }
}
