using Ashfall.Player;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // SYS-PLR §4.1 이동·스태미나 규칙
    public class PlayerMovementTests
    {
        private PlayerConfigSO config;
        private LocomotionModel locomotion;
        private StaminaModel stamina;

        [SetUp]
        public void SetUp()
        {
            config = TestData.Player();
            locomotion = new LocomotionModel(config);
            stamina = new StaminaModel(config);
        }

        [Test]
        public void Speeds_FollowTable()
        {
            Assert.AreEqual(3.0f, locomotion.Speed(false, 0f), 1e-5f);
            Assert.AreEqual(5.5f, locomotion.Speed(true, 0f), 1e-5f);
            locomotion.ToggleCrouch();
            Assert.AreEqual(1.6f, locomotion.Speed(true, 0f), 1e-5f, "웅크리면 질주 입력이 있어도 웅크리기 속도");
            locomotion.ToggleProne();
            Assert.AreEqual(0.8f, locomotion.Speed(false, 0f), 1e-5f);
        }

        [Test]
        public void NoiseRadius_FollowTable()
        {
            Assert.AreEqual(0f, locomotion.NoiseRadius(false, false), "멈춰 있으면 소음 없음");
            Assert.AreEqual(8f, locomotion.NoiseRadius(true, false));
            Assert.AreEqual(20f, locomotion.NoiseRadius(true, true));
            Assert.AreEqual(15f, locomotion.JumpNoise);
            locomotion.ToggleCrouch();
            Assert.AreEqual(3f, locomotion.NoiseRadius(true, false));
            locomotion.ToggleProne();
            Assert.AreEqual(1f, locomotion.NoiseRadius(true, false));
        }

        [Test]
        public void StanceToggles_CAndZ()
        {
            locomotion.ToggleCrouch();
            Assert.AreEqual(Stance.Crouch, locomotion.Stance);
            locomotion.ToggleProne();
            Assert.AreEqual(Stance.Prone, locomotion.Stance, "웅크림에서 Z → 엎드림");
            locomotion.ToggleCrouch();
            Assert.AreEqual(Stance.Crouch, locomotion.Stance, "엎드림에서 C → 웅크림");
            locomotion.ToggleCrouch();
            Assert.AreEqual(Stance.Stand, locomotion.Stance);
            locomotion.ToggleProne();
            locomotion.ToggleProne();
            Assert.AreEqual(Stance.Stand, locomotion.Stance);
        }

        [Test]
        public void WeightPenalty_TwoPercentPerKgOver25()
        {
            Assert.AreEqual(1f, locomotion.WeightMultiplier(25f), 1e-5f);
            Assert.AreEqual(0.9f, locomotion.WeightMultiplier(30f), 1e-5f);
            Assert.AreEqual(3.0f * 0.9f, locomotion.Speed(false, 30f), 1e-5f);
        }

        [Test]
        public void Sprint_BlockedOver40kg_AndWhenNotStanding()
        {
            Assert.IsTrue(locomotion.CanSprint(true, 40f, stamina));
            Assert.IsFalse(locomotion.CanSprint(true, 40.1f, stamina));
            Assert.IsFalse(locomotion.CanSprint(false, 0f, stamina), "움직이지 않으면 질주 아님");
            locomotion.ToggleCrouch();
            Assert.IsFalse(locomotion.CanSprint(true, 0f, stamina));
        }

        [Test]
        public void Jump_VelocityReachesOneMeter()
        {
            const float g = 9.81f;
            float v = locomotion.JumpVelocity(-g);
            Assert.AreEqual(1.0f, v * v / (2f * g), 1e-4f);
        }

        [Test]
        public void Stamina_SprintDrains15PerSec()
        {
            stamina.Tick(2f, sprinting: true, moving: true);
            Assert.AreEqual(70f, stamina.Current, 1e-4f);
        }

        [Test]
        public void Stamina_RegenMoving10_Stationary20_CappedAtMax()
        {
            stamina.Tick(4f, true, true); // 40
            stamina.Tick(1f, false, true);
            Assert.AreEqual(50f, stamina.Current, 1e-4f);
            stamina.Tick(1f, false, false);
            Assert.AreEqual(70f, stamina.Current, 1e-4f);
            stamina.Tick(10f, false, false);
            Assert.AreEqual(100f, stamina.Current, 1e-4f);
        }

        [Test]
        public void Stamina_Exhausted_LocksSprintFor3Sec()
        {
            stamina.Tick(7f, true, true);  // 100 - 105 → 0, 잠금
            Assert.AreEqual(0f, stamina.Current);
            Assert.IsTrue(stamina.SprintLocked);
            Assert.IsFalse(locomotion.CanSprint(true, 0f, stamina));

            stamina.Tick(2.9f, false, true);
            Assert.IsTrue(stamina.SprintLocked, "3초 전에는 잠금 유지");
            stamina.Tick(0.2f, false, true);
            Assert.IsFalse(stamina.SprintLocked);
            Assert.IsTrue(locomotion.CanSprint(true, 0f, stamina));
        }

        [Test]
        public void Stamina_JumpCosts20_RefusedWhenShort()
        {
            Assert.IsTrue(stamina.TrySpend(config.jumpStaminaCost));
            Assert.AreEqual(80f, stamina.Current, 1e-4f);

            stamina.Tick(5.4f, true, true); // 80 - 81 → 0
            stamina.Tick(1f, false, true);  // 10
            Assert.IsFalse(stamina.TrySpend(config.jumpStaminaCost));
            Assert.AreEqual(10f, stamina.Current, 1e-4f);
        }
    }
}
