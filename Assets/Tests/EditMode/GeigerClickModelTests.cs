using Ashfall.Audio;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M6 2b: 가이거 클릭 (§11, Q22) — 초당 = 방사능률 × 6, 상한 20, 지수 간격
    public class GeigerClickModelTests
    {
        private const float PerRad = 6f;
        private const float Max = 20f;
        private const float Dt = 1f / 60f;

        private static int Run(GeigerClickModel m, float seconds, float rad)
        {
            int total = 0;
            int frames = (int)(seconds / Dt);
            for (int i = 0; i < frames; i++)
                total += m.Tick(Dt, rad);
            return total;
        }

        [Test]
        public void ZeroRadiation_NoClicks()
        {
            var m = new GeigerClickModel(PerRad, Max, new System.Random(1));
            Assert.AreEqual(0, Run(m, 60f, 0f));
            Assert.AreEqual(0, m.Tick(1f, -3f));
            Assert.AreEqual(0, m.Tick(1f, float.NaN));
            Assert.AreEqual(0, m.Tick(0f, 1f), "dt 0");
        }

        [Test]
        public void MeanRate_IsRadiationTimesSix()
        {
            var m = new GeigerClickModel(PerRad, Max, new System.Random(42));
            const float seconds = 300f;
            int clicks = Run(m, seconds, 1f);
            Assert.AreEqual(6f * seconds, clicks, 6f * seconds * 0.08f, "평균 6/s (±8%)");

            var half = new GeigerClickModel(PerRad, Max, new System.Random(7));
            int halfClicks = Run(half, seconds, 0.5f);
            Assert.AreEqual(3f * seconds, halfClicks, 3f * seconds * 0.1f, "강도 0.5 → 3/s");
        }

        [Test]
        public void Rate_CappedAtMax()
        {
            var m = new GeigerClickModel(PerRad, Max, new System.Random(3));
            Assert.AreEqual(20f, m.Rate(10f));
            const float seconds = 120f;
            int clicks = Run(m, seconds, 10f);
            Assert.AreEqual(Max * seconds, clicks, Max * seconds * 0.08f);
        }

        [Test]
        public void Deterministic_WithSeed()
        {
            var a = new GeigerClickModel(PerRad, Max, new System.Random(9));
            var b = new GeigerClickModel(PerRad, Max, new System.Random(9));
            for (int i = 0; i < 600; i++)
                Assert.AreEqual(a.Tick(Dt, 1f), b.Tick(Dt, 1f));
        }

        [Test]
        public void LongFrame_BoundedClicks()
        {
            var m = new GeigerClickModel(PerRad, Max, new System.Random(5));
            int clicks = m.Tick(1000f, 5f);
            Assert.LessOrEqual(clicks, GeigerClickModel.MaxClicksPerTick);
            Assert.Greater(clicks, 0);
        }
    }
}
