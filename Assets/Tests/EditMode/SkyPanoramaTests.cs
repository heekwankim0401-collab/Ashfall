using Ashfall.Core;
using Ashfall.EditorTools;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 AC1 하늘 파노라마 순수 함수 (§10.2 Ash_Black → Rust_Red·낮게 걸린 흐린 태양, M6 설계 §1.3·§3.2, Q3·Q4).
    public class SkyPanoramaTests
    {
        private const float ColorEps = 1.5f / 255f;
        private AtmosphereConfigSO cfg;
        private PaletteSO palette;

        [SetUp]
        public void SetUp()
        {
            palette = ScriptableObject.CreateInstance<PaletteSO>(); // 필드 초기값 = §10.1 HEX
            cfg = ScriptableObject.CreateInstance<AtmosphereConfigSO>();
            cfg.palette = palette;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(cfg);
            Object.DestroyImmediate(palette);
        }

        private static void AssertColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, ColorEps, message + " r");
            Assert.AreEqual(expected.g, actual.g, ColorEps, message + " g");
            Assert.AreEqual(expected.b, actual.b, ColorEps, message + " b");
        }

        // 태양 반대편 방위(태양 영향 없음)의 고도 elevation 방향
        private Vector3 AwayFromSun(float elevationDeg) =>
            SkyPanorama.DirectionFromAngles(cfg.sunAzimuthDeg + 180f, elevationDeg);

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        [Test]
        public void Zenith_IsAshBlack()
        {
            AssertColor(palette.ashBlack, SkyPanorama.Sample(Vector3.up, cfg), "천정");
        }

        [Test]
        public void Horizon_IsRustRed()
        {
            AssertColor(palette.rustRed, SkyPanorama.Sample(AwayFromSun(0f), cfg), "지평선");
        }

        [Test]
        public void BelowHorizon_IsDriedBlood_FogColor()
        {
            AssertColor(palette.driedBlood, SkyPanorama.Sample(AwayFromSun(-cfg.horizonBandDeg - 1f), cfg), "지평선 아래");
            AssertColor(palette.driedBlood, SkyPanorama.Sample(Vector3.down, cfg), "천저");
            AssertColor(palette.Get(cfg.fogToken), SkyPanorama.Sample(AwayFromSun(-45f), cfg), "지평선 아래 = 안개색 (Q3)");
        }

        [Test]
        public void Gradient_DarkensMonotonically_FromHorizonToZenith()
        {
            float previous = float.MaxValue;
            for (int e = 0; e <= 90; e += 5)
            {
                float luma = Luma(SkyPanorama.Sample(AwayFromSun(e), cfg));
                Assert.LessOrEqual(luma, previous + 1e-5f, $"고도 {e}°에서 다시 밝아짐");
                previous = luma;
            }
        }

        [Test]
        public void SunDirection_FollowsAzimuthClockwiseFromPlusZ_AndElevation()
        {
            Vector3 sun = SkyPanorama.SunDirection(cfg);
            Assert.AreEqual(1f, sun.magnitude, 1e-4f);
            Assert.AreEqual(cfg.sunElevationDeg, Mathf.Asin(sun.y) * Mathf.Rad2Deg, 1e-3f, "고도");
            // 245° = 서남서: -X, -Z
            Assert.Less(sun.x, 0f);
            Assert.Less(sun.z, 0f);
            Assert.AreEqual(cfg.sunAzimuthDeg, Mathf.Repeat(Mathf.Atan2(sun.x, sun.z) * Mathf.Rad2Deg, 360f), 1e-3f, "방위");

            Vector3 east = SkyPanorama.DirectionFromAngles(90f, 0f);
            Assert.AreEqual(1f, east.x, 1e-5f, "90° = +X (시계 방향)");
        }

        [Test]
        public void SunPixel_IsBrightest()
        {
            Vector3 sun = SkyPanorama.SunDirection(cfg);
            float sunLuma = Luma(SkyPanorama.Sample(sun, cfg));
            for (int az = 0; az < 360; az += 10)
            for (int el = -60; el <= 90; el += 10)
            {
                Vector3 dir = SkyPanorama.DirectionFromAngles(az, el);
                if (Vector3.Angle(dir, sun) < 1f)
                    continue;
                Assert.Less(Luma(SkyPanorama.Sample(dir, cfg)), sunLuma, $"방위 {az}° 고도 {el}°가 태양보다 밝다");
            }
            // 원반 중심은 sunCoreToken 쪽으로 기운다
            Assert.Greater(sunLuma, Luma(SkyPanorama.Sample(AwayFromSun(cfg.sunElevationDeg), cfg)) + 0.05f, "태양이 배경과 구분되지 않는다");
        }

        [Test]
        public void Haze_DimsSun_OneMeansInvisible()
        {
            Vector3 sun = SkyPanorama.SunDirection(cfg);
            cfg.sunHaze = 0f;
            float sharp = Luma(SkyPanorama.Sample(sun, cfg));
            Color sharpColor = SkyPanorama.Sample(sun, cfg);
            cfg.sunHaze = 0.6f;
            float hazy = Luma(SkyPanorama.Sample(sun, cfg));
            cfg.sunHaze = 1f;
            Color invisible = SkyPanorama.Sample(sun, cfg);

            Assert.Greater(sharp, hazy, "흐림 0이 0.6보다 밝아야 한다");
            AssertColor(palette.bone, sharpColor, "흐림 0: 원반 중심 = sunCoreToken");
            // 흐림 1: 태양이 없다 = 같은 고도의 반대편 하늘과 같다
            AssertColor(SkyPanorama.Sample(AwayFromSun(cfg.sunElevationDeg), cfg), invisible, "흐림 1이면 태양이 안 보여야 한다");
        }

        [Test]
        public void Haze_WidensDiskEdge()
        {
            // 원반 바로 바깥(반지름 + 1°): 흐릴수록 원반 색이 더 번진다(가장자리 퍼짐). 번짐(glow) 차이는 빼고 본다.
            cfg.sunGlowDeg = 0.001f;
            Vector3 sun = SkyPanorama.SunDirection(cfg);
            Vector3 edge = Quaternion.AngleAxis(cfg.sunDiskDeg + 1f, Vector3.up) * sun;
            cfg.sunHaze = 0f;
            float sharpEdge = Luma(SkyPanorama.Sample(edge, cfg)) / Mathf.Max(1e-4f, Luma(SkyPanorama.Sample(sun, cfg)));
            cfg.sunHaze = 0.6f;
            float hazyEdge = Luma(SkyPanorama.Sample(edge, cfg)) / Mathf.Max(1e-4f, Luma(SkyPanorama.Sample(sun, cfg)));
            Assert.Greater(hazyEdge, sharpEdge, "흐릴수록 가장자리 / 중심 밝기 비가 커야 한다");
        }

        [Test]
        public void Uv_MatchesPanoramicConvention()
        {
            Assert.AreEqual(1f, SkyPanorama.DirectionToUv(Vector3.up).y, 1e-5f, "천정 v = 1");
            Assert.AreEqual(0f, SkyPanorama.DirectionToUv(Vector3.down).y, 1e-5f, "천저 v = 0");
            Assert.AreEqual(0.5f, SkyPanorama.DirectionToUv(Vector3.forward).y, 1e-5f, "지평선 v = 0.5");
            Assert.AreEqual(0.5f, SkyPanorama.DirectionToUv(Vector3.right).x, 1e-5f, "+X u = 0.5");
            Assert.AreEqual(0.25f, SkyPanorama.DirectionToUv(Vector3.forward).x, 1e-5f, "+Z u = 0.25");
            Assert.AreEqual(0.75f, SkyPanorama.DirectionToUv(Vector3.back).x, 1e-5f, "-Z u = 0.75");
        }

        [Test]
        public void Uv_RoundTrips()
        {
            for (int az = 0; az < 360; az += 15)
            for (int el = -80; el <= 80; el += 20)
            {
                Vector3 dir = SkyPanorama.DirectionFromAngles(az, el);
                Vector3 back = SkyPanorama.UvToDirection(SkyPanorama.DirectionToUv(dir));
                Assert.Less(Vector3.Angle(dir, back), 0.01f, $"방위 {az}° 고도 {el}°");
            }
        }

        [Test]
        public void Render_TopRowZenith_BottomRowFog_IsDeterministic()
        {
            const int w = 256, h = 128;
            Color32[] a = SkyPanorama.Render(w, h, cfg);
            Color32[] b = SkyPanorama.Render(w, h, cfg);
            Assert.AreEqual(w * h, a.Length);
            CollectionAssert.AreEqual(a, b, "같은 입력 = 같은 픽셀");
            AssertColor(palette.ashBlack, a[(h - 1) * w + w / 2], "맨 위 행 ≈ 천정");
            AssertColor(palette.driedBlood, a[w / 2], "맨 아래 행 = 안개색");
        }
    }
}
