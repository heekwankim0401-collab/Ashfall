using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Weapons;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M7 7A-2a 무기 교체 1/2/3 (§12.1, Q1·Q2)
    public class WeaponSwitchModelTests
    {
        private const float SwitchSec = 0.5f;

        private static System.Func<EquipSlot, bool> Has(params EquipSlot[] slots)
        {
            var set = new HashSet<EquipSlot>(slots);
            return s => set.Contains(s);
        }

        [Test]
        public void KeyOrder_Is_Primary1_Primary2_Secondary()
        {
            Assert.AreEqual(3, WeaponSwitchModel.SlotCount);
            Assert.AreEqual(EquipSlot.Primary1, WeaponSwitchModel.SlotForKey(0));
            Assert.AreEqual(EquipSlot.Primary2, WeaponSwitchModel.SlotForKey(1));
            Assert.AreEqual(EquipSlot.Secondary, WeaponSwitchModel.SlotForKey(2));
        }

        [Test]
        public void IsWeaponSlot_OnlyThreeWeaponSlots()
        {
            Assert.IsTrue(WeaponSwitchModel.IsWeaponSlot(EquipSlot.Primary1));
            Assert.IsTrue(WeaponSwitchModel.IsWeaponSlot(EquipSlot.Primary2));
            Assert.IsTrue(WeaponSwitchModel.IsWeaponSlot(EquipSlot.Secondary));
            Assert.IsFalse(WeaponSwitchModel.IsWeaponSlot(EquipSlot.Helmet));
            Assert.IsFalse(WeaponSwitchModel.IsWeaponSlot(EquipSlot.TacticalVest));
            Assert.IsFalse(WeaponSwitchModel.IsWeaponSlot(EquipSlot.Backpack));
        }

        // Q2: Primary1 → Primary2 → Secondary 첫 비지 않은 슬롯, 교체 시간 없음
        [TestCase(new[] { EquipSlot.Primary1, EquipSlot.Primary2, EquipSlot.Secondary }, EquipSlot.Primary1)]
        [TestCase(new[] { EquipSlot.Primary2, EquipSlot.Secondary }, EquipSlot.Primary2)]
        [TestCase(new[] { EquipSlot.Secondary }, EquipSlot.Secondary)]
        [TestCase(new EquipSlot[0], EquipSlot.Primary1)]
        public void PickInitial_FirstNonEmpty(EquipSlot[] filled, EquipSlot expected)
        {
            var m = new WeaponSwitchModel(SwitchSec);
            m.PickInitial(Has(filled));
            Assert.AreEqual(expected, m.ActiveSlot);
            Assert.IsFalse(m.IsSwitching, "레이드 시작은 교체 시간 없음");
        }

        [Test]
        public void PickInitial_Resets_PendingSwitch()
        {
            var m = new WeaponSwitchModel(SwitchSec);
            Assert.IsTrue(m.Request(EquipSlot.Primary2, Has(EquipSlot.Primary2)));
            m.PickInitial(Has(EquipSlot.Secondary));
            Assert.AreEqual(EquipSlot.Secondary, m.ActiveSlot);
            Assert.IsFalse(m.IsSwitching);
        }

        [Test]
        public void Request_OtherFilledSlot_Switches_WithTime()
        {
            var m = new WeaponSwitchModel(SwitchSec);
            m.PickInitial(Has(EquipSlot.Primary1, EquipSlot.Secondary));
            Assert.IsTrue(m.Request(EquipSlot.Secondary, Has(EquipSlot.Primary1, EquipSlot.Secondary)));
            Assert.AreEqual(EquipSlot.Secondary, m.ActiveSlot);
            Assert.IsTrue(m.IsSwitching);
            Assert.AreEqual(0f, m.Progress, 1e-5f);

            Assert.IsFalse(m.Tick(0.3f));
            Assert.IsTrue(m.IsSwitching, "0.3초 — 아직 교체 중");
            Assert.AreEqual(0.6f, m.Progress, 1e-4f);
            Assert.IsTrue(m.Tick(0.25f), "0.55초 — 끝난 Tick에 true");
            Assert.IsFalse(m.IsSwitching);
            Assert.AreEqual(1f, m.Progress);
            Assert.IsFalse(m.Tick(0.1f), "끝난 뒤에는 false");
        }

        [Test]
        public void Request_SameSlot_Ignored()
        {
            var m = new WeaponSwitchModel(SwitchSec);
            m.PickInitial(Has(EquipSlot.Primary1));
            Assert.IsFalse(m.Request(EquipSlot.Primary1, Has(EquipSlot.Primary1)));
            Assert.IsFalse(m.IsSwitching, "든 슬롯 키는 무시");
        }

        [Test]
        public void Request_EmptySlot_Ignored()
        {
            var m = new WeaponSwitchModel(SwitchSec);
            m.PickInitial(Has(EquipSlot.Primary1));
            Assert.IsFalse(m.Request(EquipSlot.Secondary, Has(EquipSlot.Primary1)));
            Assert.AreEqual(EquipSlot.Primary1, m.ActiveSlot);
            Assert.IsFalse(m.IsSwitching, "빈 슬롯 키는 무시");
        }

        [Test]
        public void Request_NonWeaponSlot_Ignored()
        {
            var m = new WeaponSwitchModel(SwitchSec);
            m.PickInitial(Has(EquipSlot.Primary1));
            Assert.IsFalse(m.Request(EquipSlot.Helmet, _ => true));
            Assert.AreEqual(EquipSlot.Primary1, m.ActiveSlot);
        }

        [Test]
        public void Request_DuringSwitch_ToThirdSlot_RestartsTime()
        {
            var m = new WeaponSwitchModel(SwitchSec);
            var all = Has(EquipSlot.Primary1, EquipSlot.Primary2, EquipSlot.Secondary);
            m.PickInitial(all);
            Assert.IsTrue(m.Request(EquipSlot.Primary2, all));
            m.Tick(0.4f);
            Assert.IsFalse(m.Request(EquipSlot.Primary2, all), "교체 중인 슬롯 키 = 든 슬롯 키 (무시)");
            Assert.IsTrue(m.Request(EquipSlot.Secondary, all));
            Assert.AreEqual(EquipSlot.Secondary, m.ActiveSlot);
            Assert.IsFalse(m.Tick(0.4f), "다시 처음부터 0.5초");
            Assert.IsTrue(m.Tick(0.11f));
        }

        [Test]
        public void BeginSwitch_AppliesTime_WithoutChangingSlot()
        {
            var m = new WeaponSwitchModel(SwitchSec);
            m.PickInitial(Has());
            m.BeginSwitch();
            Assert.AreEqual(EquipSlot.Primary1, m.ActiveSlot);
            Assert.IsTrue(m.IsSwitching, "든 슬롯에 새 무기 = 교체 시간 적용");
        }

        [Test]
        public void ZeroSwitchTime_NeverSwitching()
        {
            var m = new WeaponSwitchModel(0f);
            m.PickInitial(Has(EquipSlot.Primary1, EquipSlot.Primary2));
            Assert.IsTrue(m.Request(EquipSlot.Primary2, Has(EquipSlot.Primary1, EquipSlot.Primary2)));
            Assert.AreEqual(EquipSlot.Primary2, m.ActiveSlot);
            Assert.IsFalse(m.IsSwitching);
        }

        [Test]
        public void NegativeSwitchTime_ClampedToZero()
        {
            Assert.AreEqual(0f, new WeaponSwitchModel(-1f).SwitchTimeSec);
        }

        [Test]
        public void HandlingDefault_IsHalfSecond()
        {
            var handling = M7TestData.Handling();
            Assert.AreEqual(0.5f, handling.weaponSwitchTimeSec, 1e-6f, "TODO(GDD-?) Q1 임시값");
            Assert.AreEqual(0.5f, new WeaponSwitchModel(handling.weaponSwitchTimeSec).SwitchTimeSec, 1e-6f);
            UnityEngine.Object.DestroyImmediate(handling);
        }
    }
}
