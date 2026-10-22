using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 재 폭풍 표시 순수 계산 (§8.3, M6 리뷰 중간 1·낮음 2) — AshStormVisualMath.
    public class AshStormVisualMathTests
    {
        private const float Eps = 1e-4f;

        // ---------- 하늘 단계 (중간 1) ----------

        [Test]
        public void FogSky_OnlyAboveSwapIntensity()
        {
            Assert.IsFalse(AshStormVisualMath.UsesFogSky(0f, 0.5f));
            Assert.IsFalse(AshStormVisualMath.UsesFogSky(0.5f, 0.5f), "교체 강도와 같으면 아직 생성 하늘");
            Assert.IsTrue(AshStormVisualMath.UsesFogSky(0.51f, 0.5f));
            Assert.IsTrue(AshStormVisualMath.UsesFogSky(1f, 0.5f), "Peak = 안개 색 하늘");
        }

        [Test]
        public void SwapAtOne_MeansNoSwap_OldBehaviour()
        {
            Assert.IsFalse(AshStormVisualMath.UsesFogSky(1f, 1f));
            Assert.AreEqual(0.7f, AshStormVisualMath.TexturedSkyProgress(0.7f, 1f), Eps, "교체 없음 = 강도 그대로");
            Assert.AreEqual(0f, AshStormVisualMath.FogSkyBrightness(1f, 1f), Eps);
        }

        [Test]
        public void Progress_And_Brightness_AreContinuousAtSwap()
        {
            const float swap = 0.5f;
            Assert.AreEqual(0f, AshStormVisualMath.TexturedSkyProgress(0f, swap), Eps);
            Assert.AreEqual(0.5f, AshStormVisualMath.TexturedSkyProgress(0.25f, swap), Eps);
            Assert.AreEqual(1f, AshStormVisualMath.TexturedSkyProgress(swap, swap), Eps, "교체 순간 생성 하늘은 끝(가장 어두움)");
            Assert.AreEqual(0f, AshStormVisualMath.FogSkyBrightness(swap, swap), Eps, "교체 순간 단색 하늘 밝기 0");
            Assert.AreEqual(0f, AshStormVisualMath.FogSkyBrightness(swap + 1e-6f, swap), 1e-4f, "교체 직후 이어짐");
            Assert.AreEqual(0.5f, AshStormVisualMath.FogSkyBrightness(0.75f, swap), Eps);
            Assert.AreEqual(1f, AshStormVisualMath.FogSkyBrightness(1f, swap), Eps, "Peak 밝기 1 = 안개 색 그대로");
        }

        [Test]
        public void SwapAtZeroOrNegative_FogSkyAsSoonAsStormStarts()
        {
            Assert.IsTrue(AshStormVisualMath.UsesFogSky(0.01f, 0f));
            Assert.AreEqual(1f, AshStormVisualMath.TexturedSkyProgress(0.01f, 0f), Eps);
            Assert.AreEqual(0.5f, AshStormVisualMath.FogSkyBrightness(0.5f, -1f), Eps);
        }

        [Test]
        public void NaN_IsSafe()
        {
            Assert.IsFalse(AshStormVisualMath.UsesFogSky(float.NaN, 0.5f));
            Assert.IsFalse(AshStormVisualMath.UsesFogSky(1f, float.NaN));
            Assert.AreEqual(0f, AshStormVisualMath.TexturedSkyProgress(float.NaN, 0.5f), Eps);
            Assert.AreEqual(0f, AshStormVisualMath.FogSkyBrightness(float.NaN, 0.5f), Eps);
        }

        // ---------- Panoramic 틴트: 하늘 = 안개 색 ----------

        private static Color Hex(PaletteToken token)
        {
            Assert.IsTrue(ColorUtility.TryParseHtmlString(PaletteSO.GddHex(token), out Color c));
            return c;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PanoramicTint_AtFullBrightness_RendersFogColor(bool linear)
        {
            foreach (PaletteToken token in new[] { PaletteToken.AshGrey, PaletteToken.DriedBlood, PaletteToken.Bone })
            {
                Color fog = Hex(token);
                Color tint = AshStormVisualMath.PanoramicTintFor(fog, 1f, linear);
                Color sky = AshStormVisualMath.PanoramicOutput(tint, 1f, Color.white, linear);
                Color expected = linear ? fog.linear : fog;
                Assert.AreEqual(expected.r, sky.r, 1e-4f, $"{token} R");
                Assert.AreEqual(expected.g, sky.g, 1e-4f, $"{token} G");
                Assert.AreEqual(expected.b, sky.b, 1e-4f, $"{token} B");
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PanoramicTint_BrightnessScalesOutput_ZeroIsBlack(bool linear)
        {
            Color fog = Hex(PaletteToken.AshGrey);
            Color expected = linear ? fog.linear : fog;
            Color half = AshStormVisualMath.PanoramicOutput(AshStormVisualMath.PanoramicTintFor(fog, 0.5f, linear), 1f, Color.white, linear);
            Assert.AreEqual(expected.r * 0.5f, half.r, 1e-4f);
            Assert.AreEqual(expected.g * 0.5f, half.g, 1e-4f);
            Color black = AshStormVisualMath.PanoramicOutput(AshStormVisualMath.PanoramicTintFor(fog, 0f, linear), 1f, Color.white, linear);
            Assert.AreEqual(0f, black.r + black.g + black.b, 1e-6f);
        }

        // 기본 머티리얼 틴트 0.5(회색)·노출 1이면 Panoramic은 텍스처를 그대로 그린다(셰이더 상수 확인 — Linear에서 ≈ 1배)
        [Test]
        public void PanoramicOutput_DefaultTint_IsIdentity()
        {
            Color grey = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Color tex = new Color(0.2f, 0.4f, 0.6f, 1f);
            Color linear = AshStormVisualMath.PanoramicOutput(grey, 1f, tex, true);
            Assert.AreEqual(tex.r, linear.r, 0.02f);
            Assert.AreEqual(tex.b, linear.b, 0.02f);
            Color gamma = AshStormVisualMath.PanoramicOutput(grey, 1f, tex, false);
            Assert.AreEqual(tex.g, gamma.g, 1e-5f);
        }

        // ---------- 재 방출 배율 (낮음 2) ----------

        [Test]
        public void AshEmission_ClampedToStormCapacity()
        {
            // 레이드 기본: 600/s × 10s = 6000, 상한 12000 → 설정 ×4는 ×2로
            Assert.AreEqual(2f, AshStormVisualMath.AshEmissionMultiplier(4f, 600f, 10f, 12000), Eps);
            // 상한이 충분하면 설정 그대로
            Assert.AreEqual(4f, AshStormVisualMath.AshEmissionMultiplier(4f, 600f, 10f, 24000), Eps);
            Assert.AreEqual(4f, AshStormVisualMath.AshEmissionMultiplier(4f, 50f, 5f, 12000), Eps);
            // 상한이 평시보다 작아도 1 밑으로 줄이지 않는다
            Assert.AreEqual(1f, AshStormVisualMath.AshEmissionMultiplier(4f, 600f, 10f, 3000), Eps);
        }

        [Test]
        public void AshEmission_EdgeCases_ReturnConfigured()
        {
            Assert.AreEqual(0.5f, AshStormVisualMath.AshEmissionMultiplier(0.5f, 600f, 10f, 12000), Eps, "줄이는 설정은 그대로");
            Assert.AreEqual(4f, AshStormVisualMath.AshEmissionMultiplier(4f, 0f, 10f, 12000), Eps, "평시 방출 없음");
            Assert.AreEqual(4f, AshStormVisualMath.AshEmissionMultiplier(4f, 600f, 0f, 12000), Eps, "수명 없음");
            Assert.AreEqual(4f, AshStormVisualMath.AshEmissionMultiplier(4f, 600f, 10f, 0), Eps, "상한 없음");
            Assert.AreEqual(1f, AshStormVisualMath.AshEmissionMultiplier(float.NaN, 600f, 10f, 12000), Eps);
        }

        [Test]
        public void AshEmission_SteadyStateFitsCap()
        {
            float m = AshStormVisualMath.AshEmissionMultiplier(4f, 600f, 10f, 12000);
            Assert.LessOrEqual(600f * m * 10f, 12000f + 1e-3f);
        }
    }
}
