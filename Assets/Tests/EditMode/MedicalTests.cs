using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // §4.3 해제: 붕대(출혈) / 지혈대(중출혈) / 부목(골절). 사용 시간 후 적용, 대상 부위 자동 선택 (설계 D8).
    public class MedicalTests
    {
        private BodyHealthModel body;
        private MedicalUseModel medical;

        [SetUp]
        public void SetUp()
        {
            body = new BodyHealthModel(TestData.Player(), new FixedRandom(0.99));
            medical = new MedicalUseModel(body);
        }

        [Test]
        public void Bandage_RemovesBleedOnly_AfterUseTime()
        {
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.Bleed);
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.HeavyBleed);

            Assert.AreEqual(MedicalStartResult.Started, medical.TryStart(new MedicalItem(TestData.Bandage())));
            Assert.AreEqual(BodyPart.LeftLeg, medical.Target);

            Assert.IsFalse(medical.Tick(2.9f));
            Assert.IsTrue(body.HasStatus(BodyPart.LeftLeg, StatusEffect.Bleed), "사용 시간 동안 효과 없음");

            Assert.IsTrue(medical.Tick(0.2f));
            Assert.IsFalse(body.HasStatus(BodyPart.LeftLeg, StatusEffect.Bleed));
            Assert.IsTrue(body.HasStatus(BodyPart.LeftLeg, StatusEffect.HeavyBleed), "붕대는 중출혈을 못 막는다");
            Assert.IsFalse(medical.IsUsing);
        }

        [Test]
        public void Tourniquet_RemovesHeavyBleed()
        {
            body.AddStatus(BodyPart.RightArm, StatusEffect.HeavyBleed);
            Assert.AreEqual(MedicalStartResult.Started, medical.TryStart(new MedicalItem(TestData.Tourniquet())));
            Assert.IsTrue(medical.Tick(4f));
            Assert.IsFalse(body.HasStatus(BodyPart.RightArm, StatusEffect.HeavyBleed));
        }

        [Test]
        public void Splint_RemovesFracture_RestoresMovement()
        {
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.Fracture);
            Assert.AreEqual(0.8f, body.Modifiers.MoveSpeedMultiplier, 1e-5f);
            Assert.AreEqual(MedicalStartResult.Started, medical.TryStart(new MedicalItem(TestData.Splint())));
            Assert.IsTrue(medical.Tick(5f));
            Assert.IsFalse(body.HasStatus(BodyPart.LeftLeg, StatusEffect.Fracture));
            Assert.AreEqual(ConditionModifiers.Healthy, body.Modifiers);
        }

        [Test]
        public void NotNeeded_WhenNoMatchingStatus()
        {
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.HeavyBleed);
            Assert.AreEqual(MedicalStartResult.NotNeeded, medical.TryStart(new MedicalItem(TestData.Bandage())));
            Assert.IsFalse(medical.IsUsing);
        }

        [Test]
        public void Busy_WhileUsingAnother()
        {
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.Bleed);
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.Fracture);
            var bandage = new MedicalItem(TestData.Bandage());
            Assert.AreEqual(MedicalStartResult.Started, medical.TryStart(bandage));
            Assert.AreEqual(MedicalStartResult.Busy, medical.TryStart(new MedicalItem(TestData.Splint())));
            Assert.AreSame(bandage, medical.Current);
        }

        [Test]
        public void Dead_RejectsUse()
        {
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.Bleed);
            body.ApplyHit(BodyPart.Head, 35f);
            Assert.AreEqual(MedicalStartResult.Dead, medical.TryStart(new MedicalItem(TestData.Bandage())));
        }

        [Test]
        public void DyingDuringUse_NoEffect()
        {
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.Bleed);
            medical.TryStart(new MedicalItem(TestData.Bandage()));
            body.ApplyHit(BodyPart.Head, 35f);
            Assert.IsFalse(medical.Tick(5f));
            Assert.IsFalse(medical.IsUsing);
        }

        [Test]
        public void Target_FirstPartInBodyOrder()
        {
            body.AddStatus(BodyPart.RightLeg, StatusEffect.Bleed);
            body.AddStatus(BodyPart.Stomach, StatusEffect.Bleed);
            Assert.AreEqual(BodyPart.Stomach, MedicalUseModel.FindTarget(body, TestData.Bandage()));
        }

        [Test]
        public void HealItem_TargetsLowestRatioNonDestroyedPart()
        {
            var kit = TestData.Medical("TEST-KIT", "kit", Vector2Int.one, 0.1f, 1f, StatusEffect.Bleed, heal: 20f);
            Assert.IsNull(MedicalUseModel.FindTarget(body, kit), "다친 곳이 없으면 대상 없음");

            body.ApplyDamage(BodyPart.LeftArm, 60f);   // 파괴 → 제외
            body.ApplyDamage(BodyPart.RightLeg, 30f);  // 35/65
            body.ApplyDamage(BodyPart.Head, 10f);      // 25/35
            Assert.AreEqual(BodyPart.RightLeg, MedicalUseModel.FindTarget(body, kit));

            medical.TryStart(new MedicalItem(kit));
            medical.Tick(1f);
            Assert.AreEqual(55f, body.GetHp(BodyPart.RightLeg), 1e-4f);
        }

        [Test]
        public void ConsumeMedicalUse_LastUse_RemovesItemAndClearsQuickSlot()
        {
            var inventory = new PlayerInventory(TestData.Inventory());
            var bandage = new MedicalItem(TestData.Bandage());
            Assert.IsTrue(inventory.TryAutoStore(bandage));
            Assert.IsTrue(inventory.TryAssignQuickSlot(0, bandage));

            inventory.ConsumeMedicalUse(bandage);

            Assert.AreEqual(0, bandage.UsesLeft);
            Assert.IsFalse(inventory.IsInStorage(bandage));
            Assert.IsNull(inventory.GetQuickSlot(0));
        }

        [Test]
        public void ConsumeMedicalUse_UsesLeft_StaysInInventory()
        {
            var inventory = new PlayerInventory(TestData.Inventory());
            var kit = new MedicalItem(TestData.Medical("TEST-KIT", "kit", Vector2Int.one, 0.1f, 1f, StatusEffect.Bleed, uses: 2));
            inventory.TryAutoStore(kit);
            inventory.ConsumeMedicalUse(kit);
            Assert.AreEqual(1, kit.UsesLeft);
            Assert.IsTrue(inventory.IsInStorage(kit));
        }

        // 리뷰 수정: 남은 횟수 0인 의약품은 사용을 시작하지 않는다.
        [Test]
        public void TryStart_DepletedItem_Refused()
        {
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.Bleed);
            var bandage = new MedicalItem(TestData.Bandage());   // uses 1
            bandage.ConsumeUse();
            Assert.AreEqual(MedicalStartResult.Depleted, medical.TryStart(bandage));
            Assert.IsFalse(medical.IsUsing);
        }
    }
}
