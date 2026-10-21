using Ashfall.Core;
using Ashfall.Player;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // SYS-HP-01 §4.2 부위 체력, §4.3 STAT-BLEED / STAT-HBLEED / STAT-FRAC
    public class BodyHealthTests
    {
        private const float Eps = 1e-4f;
        // 상태이상이 절대 나오지 않는 굴림
        private const double NoRoll = 0.99;

        private static BodyHealthModel NewBody(params double[] rolls) =>
            new BodyHealthModel(TestData.Player(), new FixedRandom(rolls.Length > 0 ? rolls : new[] { NoRoll }));

        [Test]
        public void MaxHp_MatchesGddTable()
        {
            var body = NewBody();
            Assert.AreEqual(35f, body.GetMaxHp(BodyPart.Head));
            Assert.AreEqual(80f, body.GetMaxHp(BodyPart.Thorax));
            Assert.AreEqual(70f, body.GetMaxHp(BodyPart.Stomach));
            Assert.AreEqual(60f, body.GetMaxHp(BodyPart.LeftArm));
            Assert.AreEqual(60f, body.GetMaxHp(BodyPart.RightArm));
            Assert.AreEqual(65f, body.GetMaxHp(BodyPart.LeftLeg));
            Assert.AreEqual(65f, body.GetMaxHp(BodyPart.RightLeg));
            Assert.AreEqual(35f, body.GetHp(BodyPart.Head), "시작은 최대 HP");
            Assert.AreEqual(HealthTier.Healthy, body.GetTier(BodyPart.Head));
        }

        [Test]
        public void Head35_KillsInstantly()
        {
            var body = NewBody();
            BodyPart? died = null;
            int diedCount = 0;
            body.Died += p => { died = p; diedCount++; };

            body.ApplyHit(BodyPart.Head, 35f);

            Assert.IsTrue(body.IsDead);
            Assert.AreEqual(BodyPart.Head, body.DeathCause);
            Assert.AreEqual(BodyPart.Head, died);
            body.ApplyHit(BodyPart.Head, 10f);
            Assert.AreEqual(1, diedCount, "사망 이벤트는 한 번");
        }

        [Test]
        public void Thorax80_KillsInstantly()
        {
            var body = NewBody();
            body.ApplyHit(BodyPart.Thorax, 80f);
            Assert.IsTrue(body.IsDead);
            Assert.AreEqual(BodyPart.Thorax, body.DeathCause);
        }

        [Test]
        public void Arm60_DestroysButDoesNotKill()
        {
            var body = NewBody();
            body.ApplyHit(BodyPart.LeftArm, 60f);
            Assert.IsFalse(body.IsDead);
            Assert.IsTrue(body.IsDestroyed(BodyPart.LeftArm));
            Assert.AreEqual(HealthTier.Destroyed, body.GetTier(BodyPart.LeftArm));
        }

        [Test]
        public void Overkill_IsDiscarded_NotRedistributed()
        {
            var body = NewBody();
            body.ApplyHit(BodyPart.LeftArm, 100f);
            Assert.AreEqual(0f, body.GetHp(BodyPart.LeftArm));
            Assert.AreEqual(80f, body.GetHp(BodyPart.Thorax), "0 아래 초과분은 버린다");
            Assert.AreEqual(70f, body.GetHp(BodyPart.Stomach));
        }

        [Test]
        public void StomachDestroyed_HitDamageTimes1_5()
        {
            var body = NewBody();
            body.ApplyDamage(BodyPart.Stomach, 70f);
            body.ApplyHit(BodyPart.LeftArm, 10f);
            Assert.AreEqual(45f, body.GetHp(BodyPart.LeftArm), Eps, "10 × 1.5 = 15 감소");
        }

        [Test]
        public void StomachDestroyed_BleedDamageNotMultiplied()
        {
            var body = NewBody();
            body.ApplyDamage(BodyPart.Stomach, 70f);
            body.AddStatus(BodyPart.LeftArm, StatusEffect.Bleed);
            body.Tick(1f);
            Assert.AreEqual(59f, body.GetHp(BodyPart.LeftArm), Eps);
        }

        [Test]
        public void DestroyedArm_Spreads70PercentToThoraxAndStomach()
        {
            var body = NewBody();
            body.ApplyDamage(BodyPart.LeftArm, 60f);
            body.ApplyDamage(BodyPart.LeftArm, 20f);
            Assert.AreEqual(73f, body.GetHp(BodyPart.Thorax), Eps, "20 × 0.7 / 2 = 7");
            Assert.AreEqual(63f, body.GetHp(BodyPart.Stomach), Eps);
        }

        [Test]
        public void DestroyedArm_StomachAlsoDestroyed_AllToThorax()
        {
            var body = NewBody();
            body.ApplyDamage(BodyPart.Stomach, 70f);
            body.ApplyDamage(BodyPart.LeftArm, 60f);
            body.ApplyDamage(BodyPart.LeftArm, 20f);
            Assert.AreEqual(66f, body.GetHp(BodyPart.Thorax), Eps, "20 × 0.7 = 14 전부 흉부");
        }

        [Test]
        public void DestroyedStomachHit_SpreadsToThorax()
        {
            var body = NewBody();
            body.ApplyDamage(BodyPart.Stomach, 70f);
            body.ApplyDamage(BodyPart.Stomach, 10f);
            Assert.AreEqual(73f, body.GetHp(BodyPart.Thorax), Eps);
        }

        [Test]
        public void SpreadDamage_CanKillViaThorax()
        {
            var body = NewBody();
            body.ApplyDamage(BodyPart.Stomach, 70f);
            body.ApplyDamage(BodyPart.Thorax, 75f);
            body.ApplyDamage(BodyPart.LeftLeg, 65f);
            body.ApplyDamage(BodyPart.LeftLeg, 10f);   // 7 → 흉부
            Assert.IsTrue(body.IsDead);
            Assert.AreEqual(BodyPart.Thorax, body.DeathCause);
        }

        [Test]
        public void ArmsDestroyed_AimSwayAddsPerArm()
        {
            var body = NewBody();
            Assert.AreEqual(ConditionModifiers.Healthy, body.Modifiers);
            body.ApplyDamage(BodyPart.LeftArm, 60f);
            Assert.AreEqual(2f, body.Modifiers.AimSwayMultiplier, Eps);
            body.ApplyDamage(BodyPart.RightArm, 60f);
            Assert.AreEqual(3f, body.Modifiers.AimSwayMultiplier, Eps);
        }

        [Test]
        public void FracturedArm_AddsHalfSway()
        {
            var body = NewBody();
            body.AddStatus(BodyPart.RightArm, StatusEffect.Fracture);
            Assert.AreEqual(1.5f, body.Modifiers.AimSwayMultiplier, Eps);
            body.ApplyDamage(BodyPart.RightArm, 60f);
            Assert.AreEqual(2f, body.Modifiers.AimSwayMultiplier, Eps, "파괴가 골절을 대신한다 (합산 아님)");
        }

        [Test]
        public void LegDestroyed_SlowsAndBlocksSprint()
        {
            var body = NewBody();
            body.ApplyDamage(BodyPart.LeftLeg, 65f);
            Assert.AreEqual(0.6f, body.Modifiers.MoveSpeedMultiplier, Eps);
            Assert.IsFalse(body.Modifiers.SprintAllowed);
        }

        [Test]
        public void FracturedLeg_SlowsButAllowsSprint()
        {
            var body = NewBody();
            body.AddStatus(BodyPart.RightLeg, StatusEffect.Fracture);
            Assert.AreEqual(0.8f, body.Modifiers.MoveSpeedMultiplier, Eps);
            Assert.IsTrue(body.Modifiers.SprintAllowed);
        }

        [Test]
        public void LegPenalties_WorstOneApplies_NotMultiplied()
        {
            var body = NewBody();
            body.AddStatus(BodyPart.RightLeg, StatusEffect.Fracture);
            body.ApplyDamage(BodyPart.LeftLeg, 65f);
            Assert.AreEqual(0.6f, body.Modifiers.MoveSpeedMultiplier, Eps);

            var same = NewBody();
            same.AddStatus(BodyPart.LeftLeg, StatusEffect.Fracture);
            same.ApplyDamage(BodyPart.LeftLeg, 65f);
            Assert.AreEqual(0.6f, same.Modifiers.MoveSpeedMultiplier, Eps);
        }

        [Test]
        public void Bleeding_PerSecond()
        {
            var body = NewBody();
            body.AddStatus(BodyPart.LeftArm, StatusEffect.Bleed);
            body.AddStatus(BodyPart.RightArm, StatusEffect.HeavyBleed);
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.Bleed);
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.HeavyBleed);
            body.Tick(1f);
            Assert.AreEqual(59f, body.GetHp(BodyPart.LeftArm), Eps, "출혈 -1/s");
            Assert.AreEqual(57f, body.GetHp(BodyPart.RightArm), Eps, "중출혈 -3/s");
            Assert.AreEqual(61f, body.GetHp(BodyPart.LeftLeg), Eps, "둘 다 -4/s");
        }

        [Test]
        public void ThoraxBleedToZero_Dies()
        {
            var body = NewBody();
            body.ApplyDamage(BodyPart.Thorax, 78f);
            body.AddStatus(BodyPart.Thorax, StatusEffect.HeavyBleed);
            body.Tick(1f);
            Assert.IsTrue(body.IsDead);
            Assert.AreEqual(BodyPart.Thorax, body.DeathCause);
        }

        [Test]
        public void Roll_LowValue_HeavyBleedOnly()
        {
            var body = NewBody(0.1);
            body.ApplyHit(BodyPart.Thorax, 15f);
            Assert.IsTrue(body.HasStatus(BodyPart.Thorax, StatusEffect.HeavyBleed));
            Assert.IsFalse(body.HasStatus(BodyPart.Thorax, StatusEffect.Bleed));
        }

        [Test]
        public void Roll_MidValue_Bleed()
        {
            var body = NewBody(0.3);
            body.ApplyHit(BodyPart.Thorax, 15f);
            Assert.IsTrue(body.HasStatus(BodyPart.Thorax, StatusEffect.Bleed));
            Assert.IsFalse(body.HasStatus(BodyPart.Thorax, StatusEffect.HeavyBleed));
        }

        [Test]
        public void Roll_HighValue_Nothing()
        {
            var body = NewBody(0.9);
            body.ApplyHit(BodyPart.LeftLeg, 30f);
            foreach (StatusEffect e in new[] { StatusEffect.Bleed, StatusEffect.HeavyBleed, StatusEffect.Fracture })
                Assert.IsFalse(body.HasStatus(BodyPart.LeftLeg, e), e.ToString());
        }

        [Test]
        public void Roll_BelowMinDamage_NoRoll()
        {
            var body = NewBody(0.0);
            body.ApplyHit(BodyPart.LeftArm, 9f);
            Assert.IsFalse(body.HasStatus(BodyPart.LeftArm, StatusEffect.HeavyBleed));
            Assert.IsFalse(body.HasStatus(BodyPart.LeftArm, StatusEffect.Fracture));
        }

        [Test]
        public void Fracture_OnlyLimbs()
        {
            var torso = NewBody(0.0);
            torso.ApplyHit(BodyPart.Stomach, 30f);
            torso.ApplyHit(BodyPart.Head, 25f);
            Assert.IsFalse(torso.HasStatus(BodyPart.Stomach, StatusEffect.Fracture));
            Assert.IsFalse(torso.HasStatus(BodyPart.Head, StatusEffect.Fracture));
            torso.AddStatus(BodyPart.Thorax, StatusEffect.Fracture);
            Assert.IsFalse(torso.HasStatus(BodyPart.Thorax, StatusEffect.Fracture), "AddStatus도 몸통 골절 거부");

            var arm = NewBody(0.1);
            arm.ApplyHit(BodyPart.LeftArm, 20f);
            Assert.IsTrue(arm.HasStatus(BodyPart.LeftArm, StatusEffect.Fracture), "피해 20 이상, 0.1 < 0.2");

            var weak = NewBody(0.1);
            weak.ApplyHit(BodyPart.LeftArm, 19f);
            Assert.IsFalse(weak.HasStatus(BodyPart.LeftArm, StatusEffect.Fracture), "피해 20 미만");
        }

        [Test]
        public void StatusChanged_OnlyOnTierOrStatusChange_NotEveryBleedTick()
        {
            var body = NewBody();
            int statusEvents = 0;
            body.StatusChanged += () => statusEvents++;

            body.ApplyHit(BodyPart.Thorax, 5f);       // 75/80 → 정상 유지
            Assert.AreEqual(0, statusEvents);

            body.AddStatus(BodyPart.Thorax, StatusEffect.Bleed);
            Assert.AreEqual(1, statusEvents);

            for (int i = 0; i < 10; i++)
                body.Tick(0.1f);                       // 74 → 여전히 정상
            Assert.AreEqual(1, statusEvents, "출혈 틱마다 발행하지 않는다");

            body.ApplyDamage(BodyPart.Thorax, 15f);    // 59/80 < 0.75 → 부상
            Assert.AreEqual(HealthTier.Injured, body.GetTier(BodyPart.Thorax));
            Assert.AreEqual(2, statusEvents);

            Assert.IsTrue(body.RemoveStatus(BodyPart.Thorax, StatusEffect.Bleed));
            Assert.AreEqual(3, statusEvents);
            Assert.IsFalse(body.RemoveStatus(BodyPart.Thorax, StatusEffect.Bleed));
            Assert.AreEqual(3, statusEvents);
        }

        [Test]
        public void ModifiersChanged_OnlyWhenValueChanges()
        {
            var body = NewBody();
            int count = 0;
            body.ModifiersChanged += () => count++;
            body.ApplyDamage(BodyPart.LeftArm, 30f);
            Assert.AreEqual(0, count);
            body.ApplyDamage(BodyPart.LeftArm, 30f);
            Assert.AreEqual(1, count);
            body.ApplyDamage(BodyPart.LeftArm, 10f);   // 분산만, 패널티 같음
            Assert.AreEqual(1, count);
        }

        [Test]
        public void Tiers_FollowHpRatio()
        {
            var body = NewBody();
            body.ApplyDamage(BodyPart.LeftLeg, 17f);   // 48/65 = 0.738 → 부상
            Assert.AreEqual(HealthTier.Injured, body.GetTier(BodyPart.LeftLeg));
            body.ApplyDamage(BodyPart.LeftLeg, 26f);   // 22/65 = 0.338 → 중상
            Assert.AreEqual(HealthTier.Critical, body.GetTier(BodyPart.LeftLeg));
        }

        [Test]
        public void Heal_NotOnDestroyedPart_CappedAtMax()
        {
            var body = NewBody();
            body.ApplyDamage(BodyPart.LeftArm, 60f);
            body.Heal(BodyPart.LeftArm, 30f);
            Assert.AreEqual(0f, body.GetHp(BodyPart.LeftArm), "파괴 부위는 회복 안 됨");

            body.ApplyDamage(BodyPart.RightArm, 10f);
            body.Heal(BodyPart.RightArm, 30f);
            Assert.AreEqual(60f, body.GetHp(BodyPart.RightArm));
        }

        [Test]
        public void Dead_IgnoresFurtherDamage()
        {
            var body = NewBody();
            body.ApplyHit(BodyPart.Head, 40f);
            body.ApplyHit(BodyPart.LeftArm, 30f);
            Assert.AreEqual(60f, body.GetHp(BodyPart.LeftArm));
        }

        [Test]
        public void BodyStatusView_HasNoNumericMembers()
        {
            // RULE-05: IBodyStatusView는 수치를 내주지 않는다.
            foreach (var m in typeof(IBodyStatusView).GetMethods())
            {
                Assert.AreNotEqual(typeof(float), m.ReturnType, m.Name);
                Assert.AreNotEqual(typeof(int), m.ReturnType, m.Name);
            }
        }
    }
}
