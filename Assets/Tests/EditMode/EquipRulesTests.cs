using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // §6 장착 슬롯 판정 (설계 §3.5 표)
    public class EquipRulesTests
    {
        [Test]
        public void SlotTable()
        {
            var rifle = TestData.Rifle();
            var pistol = TestData.Pistol();
            var helmet = TestData.Helmet();
            var armor = TestData.BodyArmor();
            var vest = TestData.Vest();
            var bag = TestData.Backpack();
            var mag = TestData.Magazine30();
            var junk = TestData.Wires();

            Assert.IsTrue(EquipRules.Accepts(EquipSlot.Primary1, rifle));
            Assert.IsTrue(EquipRules.Accepts(EquipSlot.Primary2, rifle));
            Assert.IsFalse(EquipRules.Accepts(EquipSlot.Secondary, rifle));
            Assert.IsFalse(EquipRules.Accepts(EquipSlot.Primary1, pistol));
            Assert.IsTrue(EquipRules.Accepts(EquipSlot.Secondary, pistol));

            Assert.IsTrue(EquipRules.Accepts(EquipSlot.Helmet, helmet));
            Assert.IsFalse(EquipRules.Accepts(EquipSlot.BodyArmor, helmet));
            Assert.IsTrue(EquipRules.Accepts(EquipSlot.BodyArmor, armor));
            Assert.IsFalse(EquipRules.Accepts(EquipSlot.Helmet, armor));

            Assert.IsTrue(EquipRules.Accepts(EquipSlot.TacticalVest, vest));
            Assert.IsFalse(EquipRules.Accepts(EquipSlot.Backpack, vest));
            Assert.IsTrue(EquipRules.Accepts(EquipSlot.Backpack, bag));
            Assert.IsFalse(EquipRules.Accepts(EquipSlot.TacticalVest, bag));

            foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
            {
                Assert.IsFalse(EquipRules.Accepts(slot, mag), $"탄창 → {slot}");
                Assert.IsFalse(EquipRules.Accepts(slot, junk), $"잡동사니 → {slot}");
            }
        }
    }
}
