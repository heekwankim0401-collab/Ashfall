using Ashfall.Core;
using Ashfall.Player;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // §4.1 이동 × §4.2/§4.3 체력 패널티
    public class LocomotionConditionTests
    {
        private LocomotionModel locomotion;
        private StaminaModel stamina;

        [SetUp]
        public void SetUp()
        {
            var config = TestData.Player();
            locomotion = new LocomotionModel(config);
            stamina = new StaminaModel(config);
        }

        [Test]
        public void HealthyOverload_SameAsOriginal()
        {
            Assert.AreEqual(locomotion.Speed(false, 0f), locomotion.Speed(false, 0f, ConditionModifiers.Healthy));
            Assert.AreEqual(locomotion.Speed(true, 30f), locomotion.Speed(true, 30f, ConditionModifiers.Healthy));
            Assert.AreEqual(3.0f, locomotion.Speed(false, 0f), 1e-5f);
        }

        [Test]
        public void Speed_IsBaseTimesWeightTimesCondition()
        {
            var legDestroyed = new ConditionModifiers(0.6f, false, 1f);
            Assert.AreEqual(1.8f, locomotion.Speed(false, 0f, legDestroyed), 1e-5f);
            // 30kg → ×0.9
            Assert.AreEqual(3.0f * 0.9f * 0.6f, locomotion.Speed(false, 30f, legDestroyed), 1e-5f);
        }

        [Test]
        public void CanSprint_BlockedByCondition()
        {
            Assert.IsTrue(locomotion.CanSprint(true, 0f, stamina));
            Assert.IsTrue(locomotion.CanSprint(true, 0f, stamina, ConditionModifiers.Healthy));
            Assert.IsFalse(locomotion.CanSprint(true, 0f, stamina, new ConditionModifiers(0.6f, false, 1f)));
            Assert.IsTrue(locomotion.CanSprint(true, 0f, stamina, new ConditionModifiers(0.8f, true, 1f)), "골절은 질주 가능");
        }
    }
}
