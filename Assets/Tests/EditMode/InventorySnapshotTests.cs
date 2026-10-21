using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // RULE-01 반입·반출 목록 (M4 설계 §1.5·§1.6): Core InventorySnapshot.Collect
    public class InventorySnapshotTests
    {
        private PlayerInventory inventory;

        [SetUp]
        public void SetUp()
        {
            inventory = new PlayerInventory(TestData.Inventory());
        }

        [Test]
        public void Collect_EquippedInSlotOrder_ThenPocketsVestBackpack()
        {
            // 주머니 (조끼·배낭 장착 전이라 자동 배치가 주머니로)
            var pocketItem = new ItemInstance(TestData.Wires());
            Assert.IsTrue(inventory.TryAutoStore(pocketItem));

            var backpack = new ItemInstance(TestData.Backpack());
            var vest = new ItemInstance(TestData.Vest());
            var helmet = new ItemInstance(TestData.Helmet());
            var mag30 = TestData.Magazine30();
            var weapon = new WeaponItem(TestData.Rifle(), Magazine.Full(mag30));
            // 장착 순서를 슬롯 순서와 다르게
            Assert.IsTrue(inventory.TryEquipNew(backpack, EquipSlot.Backpack));
            Assert.IsTrue(inventory.TryEquipNew(vest, EquipSlot.TacticalVest));
            Assert.IsTrue(inventory.TryEquipNew(helmet, EquipSlot.Helmet));
            Assert.IsTrue(inventory.TryEquipNew(weapon, EquipSlot.Primary1));

            var vestItem = Magazine.Full(mag30);
            Assert.IsTrue(inventory.TryAutoStore(vestItem));   // 조끼 먼저
            var bagItem = new ItemInstance(TestData.Pipe());
            Assert.IsTrue(inventory.TryAutoStore(bagItem));
            Assert.IsTrue(inventory.TryMove(bagItem, new GridRef(GridKind.Backpack), Vector2Int.zero, false));

            var into = new List<ItemInstance>();
            int added = InventorySnapshot.Collect(inventory, into);

            CollectionAssert.AreEqual(
                new ItemInstance[] { weapon, helmet, vest, backpack, pocketItem, vestItem, bagItem }, into);
            Assert.AreEqual(7, added);
            CollectionAssert.DoesNotContain(into, weapon.LoadedMagazine, "무기의 장착 탄창은 무기에 포함 (따로 넣지 않음)");
        }

        [Test]
        public void Collect_ExcludesOpenLoot()
        {
            var mine = new ItemInstance(TestData.Wires());
            Assert.IsTrue(inventory.TryAutoStore(mine));
            var lootItem = new ItemInstance(TestData.Pipe());
            inventory.OpenLoot(new LootContainerModel("상자", new Vector2Int(4, 4), new[] { lootItem }, 0.5f));
            Assert.IsNotNull(inventory.ActiveLoot);

            var into = new List<ItemInstance>();
            InventorySnapshot.Collect(inventory, into);

            CollectionAssert.AreEqual(new[] { mine }, into);
        }

        [Test]
        public void Collect_AppendsToList_ReturnsAddedCount()
        {
            var existing = new ItemInstance(TestData.Pipe());
            var mine = new ItemInstance(TestData.Wires());
            Assert.IsTrue(inventory.TryAutoStore(mine));

            var into = new List<ItemInstance> { existing };
            int added = InventorySnapshot.Collect(inventory, into);

            Assert.AreEqual(1, added);
            CollectionAssert.AreEqual(new[] { existing, mine }, into);
        }

        [Test]
        public void Collect_Empty_ReturnsZero()
        {
            var into = new List<ItemInstance>();
            Assert.AreEqual(0, InventorySnapshot.Collect(inventory, into));
            CollectionAssert.IsEmpty(into);
        }

        [Test]
        public void Collect_NullArguments_ReturnZero()
        {
            var into = new List<ItemInstance>();
            Assert.AreEqual(0, InventorySnapshot.Collect(null, into));
            Assert.AreEqual(0, InventorySnapshot.Collect(inventory, null));
            CollectionAssert.IsEmpty(into);
        }

        // 구현이 같은 인스턴스를 두 곳에 보여 주거나 루트 그리드를 저장 공간에 넣어도 소지품은 한 번씩만
        [Test]
        public void Collect_SameInstanceOnce_SkipsLootKindGrid()
        {
            var fake = new FakeInventoryService();
            var item = new ItemInstance(TestData.Wires());
            var other = new ItemInstance(TestData.Pipe());
            var lootOnly = new ItemInstance(TestData.EnginePart());
            fake.Equipped[EquipSlot.Primary1] = item;
            var pocket = new FakeGridView(new GridRef(GridKind.Pocket, 0), new Vector2Int(4, 4));
            pocket.Placed.Add(new PlacedItem(item, Vector2Int.zero, false));
            pocket.Placed.Add(new PlacedItem(other, new Vector2Int(1, 0), false));
            var loot = new FakeGridView(new GridRef(GridKind.Loot), new Vector2Int(4, 4));
            loot.Placed.Add(new PlacedItem(lootOnly, Vector2Int.zero, false));
            fake.Grids.Add(pocket);
            fake.Grids.Add(loot);

            var into = new List<ItemInstance>();
            int added = InventorySnapshot.Collect(fake, into);

            Assert.AreEqual(2, added);
            CollectionAssert.AreEqual(new[] { item, other }, into);
        }

        // 스냅샷은 부른 시점의 소지품이다. 버린 아이템은 빠진다.
        [Test]
        public void Collect_AfterItemRemoved_NotIncluded()
        {
            var a = new ItemInstance(TestData.Wires());
            var b = new ItemInstance(TestData.Wires());
            Assert.IsTrue(inventory.TryAutoStore(a));
            Assert.IsTrue(inventory.TryAutoStore(b));
            Assert.IsTrue(inventory.TryDrop(a));

            var into = new List<ItemInstance>();
            InventorySnapshot.Collect(inventory, into);

            CollectionAssert.AreEqual(new[] { b }, into);
        }
    }
}
