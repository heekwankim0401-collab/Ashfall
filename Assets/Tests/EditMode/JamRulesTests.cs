using System;
using Ashfall.Core;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // §5.1 고장 확률: 내구도 < 50%면 발당 (50 − 내구도) × 0.1% (M7 7A, JamRules)
    public class JamRulesTests
    {
        private const float Threshold = 50f;          // GDD §5.1
        private const float PerPoint = 0.001f;        // GDD §5.1 0.1%

        [TestCase(100f, 0f)]
        [TestCase(50f, 0f)]
        [TestCase(49f, 0.001f)]
        [TestCase(30f, 0.02f)]
        [TestCase(0f, 0.05f)]
        public void Chance_FollowsGddFormula(float durability, float expected)
        {
            Assert.AreEqual(expected, JamRules.Chance(durability, Threshold, PerPoint), 1e-6f);
        }

        [Test]
        public void Chance_AboveThreshold_IsZero_NeverNegative()
        {
            Assert.AreEqual(0f, JamRules.Chance(75f, Threshold, PerPoint));
            Assert.AreEqual(0f, JamRules.Chance(float.NaN, Threshold, PerPoint));
            Assert.AreEqual(0f, JamRules.Chance(0f, Threshold, 0f));
        }

        [Test]
        public void Chance_ClampedToOne()
        {
            Assert.AreEqual(1f, JamRules.Chance(0f, Threshold, 1f));
        }

        [Test]
        public void Roll_ZeroChance_FalseAndDoesNotDrawRandom()
        {
            var used = new Random(7);
            Assert.IsFalse(JamRules.Roll(0f, used));
            Assert.AreEqual(new Random(7).NextDouble(), used.NextDouble(), "확률 0이면 난수열을 건드리지 않는다");
        }

        [Test]
        public void Roll_OneChance_AlwaysTrue()
        {
            var random = new Random(1);
            for (int i = 0; i < 100; i++)
                Assert.IsTrue(JamRules.Roll(1f, random));
        }

        [Test]
        public void Roll_Seeded_FrequencyMatchesChance()
        {
            var random = new Random(12345);
            float chance = JamRules.Chance(0f, Threshold, PerPoint);   // 5%
            const int trials = 20000;
            int jams = 0;
            for (int i = 0; i < trials; i++)
                if (JamRules.Roll(chance, random))
                    jams++;
            // 기대 1000, 표준편차 ≈ 31 → ±5σ
            Assert.That(jams, Is.InRange(845, 1155));
        }

        [Test]
        public void Roll_SameSeed_SameSequence()
        {
            var a = new Random(99);
            var b = new Random(99);
            for (int i = 0; i < 200; i++)
                Assert.AreEqual(JamRules.Roll(0.3f, a), JamRules.Roll(0.3f, b));
        }
    }
}
