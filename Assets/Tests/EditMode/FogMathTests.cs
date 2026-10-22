using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 Exp² 안개 ↔ 가시거리 (M6 설계 §1.3·§1.5, Q3·Q26)
    public class FogMathTests
    {
        private const float Threshold = 0.05f;   // AtmosphereConfigSO.fogVisibilityThreshold 기본값

        [TestCase(30f)]
        [TestCase(450f)]
        [TestCase(1f)]
        [TestCase(2500f)]
        public void Transmittance_AtVisibility_EqualsThreshold(float visibility)
        {
            float density = FogMath.DensityForVisibility(visibility, Threshold);
            Assert.AreEqual(Threshold, FogMath.Transmittance(visibility, density), 1e-4f);
        }

        [TestCase(30f)]
        [TestCase(450f)]
        [TestCase(123.4f)]
        public void RoundTrip_VisibilityDensityVisibility(float visibility)
        {
            float density = FogMath.DensityForVisibility(visibility, Threshold);
            Assert.AreEqual(visibility, FogMath.VisibilityForDensity(density, Threshold), visibility * 1e-4f);
        }

        [Test]
        public void Infinity_ZeroDensity_AndBack()
        {
            Assert.AreEqual(0f, FogMath.DensityForVisibility(float.PositiveInfinity, Threshold));
            Assert.AreEqual(0f, FogMath.DensityForVisibility(float.NaN, Threshold));
            Assert.IsTrue(float.IsPositiveInfinity(FogMath.VisibilityForDensity(0f, Threshold)));
            Assert.IsTrue(float.IsPositiveInfinity(FogMath.VisibilityForDensity(-1f, Threshold)));
        }

        [Test]
        public void ZeroOrNegativeVisibility_ClampsToMaxDensity()
        {
            Assert.AreEqual(FogMath.MaxDensity, FogMath.DensityForVisibility(0f, Threshold));
            Assert.AreEqual(FogMath.MaxDensity, FogMath.DensityForVisibility(-5f, Threshold));
        }

        [Test]
        public void ShorterVisibility_DenserFog()
        {
            Assert.Greater(FogMath.DensityForVisibility(30f, Threshold), FogMath.DensityForVisibility(450f, Threshold));
        }

        [Test]
        public void KnownValue_30m()
        {
            // √(−ln 0.05) / 30 ≈ 1.7308 / 30
            Assert.AreEqual(Mathf.Sqrt(-Mathf.Log(0.05f)) / 30f, FogMath.DensityForVisibility(30f, Threshold), 1e-6f);
            Assert.AreEqual(0.0577f, FogMath.DensityForVisibility(30f, Threshold), 1e-4f);
        }

        [Test]
        public void BadThreshold_DoesNotProduceNaN()
        {
            foreach (float t in new[] { 0f, 1f, -1f, 2f, float.NaN })
            {
                float d = FogMath.DensityForVisibility(100f, t);
                Assert.IsFalse(float.IsNaN(d) || float.IsInfinity(d), $"threshold {t} → {d}");
                Assert.GreaterOrEqual(d, 0f);
            }
        }
    }
}
