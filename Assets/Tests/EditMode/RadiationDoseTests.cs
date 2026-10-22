using Ashfall.Player;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M6 방사능 누적 (§8.3 +1/s, §4.3 STAT-RAD — 효과는 M7, Q27)
    public class RadiationDoseTests
    {
        [Test]
        public void StartsAtZero()
        {
            Assert.AreEqual(0f, new RadiationDoseModel().Dose);
        }

        [Test]
        public void Accumulates_RateTimesTime()
        {
            var model = new RadiationDoseModel();
            for (int i = 0; i < 600; i++)
                model.Tick(1f / 60f, 1f);
            Assert.AreEqual(10f, model.Dose, 1e-3f, "+1/s × 10초");
            model.Tick(2f, 0.5f);
            Assert.AreEqual(11f, model.Dose, 1e-3f, "강도 0.5");
        }

        [Test]
        public void NoUpperCap_InM6()
        {
            var model = new RadiationDoseModel();
            model.Tick(300f, 1f);
            Assert.AreEqual(300f, model.Dose, 1e-3f, "상한·효과는 M7");
        }

        [TestCase(0f, 1f)]
        [TestCase(-1f, 1f)]
        [TestCase(float.NaN, 1f)]
        [TestCase(1f, 0f)]
        [TestCase(1f, -3f)]
        [TestCase(1f, float.NaN)]
        [TestCase(1f, float.PositiveInfinity)]
        [TestCase(float.PositiveInfinity, 1f)]
        public void InvalidInput_Ignored(float dt, float rate)
        {
            var model = new RadiationDoseModel();
            model.Tick(1f, 1f);
            model.Tick(dt, rate);
            Assert.AreEqual(1f, model.Dose, 1e-6f);
        }

        [Test]
        public void Reset_ReturnsToZero()
        {
            var model = new RadiationDoseModel();
            model.Tick(5f, 1f);
            model.Reset();
            Assert.AreEqual(0f, model.Dose);
        }
    }
}
