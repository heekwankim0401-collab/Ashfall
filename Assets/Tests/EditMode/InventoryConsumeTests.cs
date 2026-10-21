using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M4: IInventoryService.Contains·TryConsume (§8.2 EXT-03 신호탄 소모, RULE-06 지도 보유) — PlayerInventory 구현
    public class InventoryConsumeTests
    {
        private PlayerInventory inventory;
        private ItemDefinitionSO flare;
        private int changedCount;

        [SetUp]
        public void SetUp()
        {
            inventory = new PlayerInventory(TestData.Inventory());
            flare = M4TestData.Flare();
            changedCount = 0;
            inventory.Changed += () => changedCount++;
        }

        private ItemInstance EquipVest()
        {
            var vest = new ItemInstance(TestData.Vest());
            Assert.IsTrue(inventory.TryEquipNew(vest, EquipSlot.TacticalVest));
            return vest;
        }

        private ItemInstance StoreFlare()
        {
            var item = new ItemInstance(flare);
            Assert.IsTrue(inventory.TryAutoStore(item));
            return item;
        }

        // ── Contains ─────────────────────────────────────────

        [Test]
        public void Contains_EmptyOrNull_False()
        {
            Assert.IsFalse(inventory.Contains(flare));
            Assert.IsFalse(inventory.Contains(null));
        }

        [Test]
        public void Contains_InStorage_True()
        {
            EquipVest();
            StoreFlare();
            Assert.IsTrue(inventory.Contains(flare));
            Assert.IsFalse(inventory.Contains(M4TestData.MapItem()), "다른 정의");
        }

        [Test]
        public void Contains_Pocket_True()
        {
            var map = M4TestData.MapItem();   // 1×1 → 주머니
            Assert.IsTrue(inventory.TryAutoStore(new ItemInstance(map)));
            Assert.IsTrue(inventory.Contains(map));
        }

        [Test]
        public void Contains_Equipped_True()
        {
            var backpackDef = TestData.Backpack();
            Assert.IsTrue(inventory.TryEquipNew(new ItemInstance(backpackDef), EquipSlot.Backpack));
            Assert.IsTrue(inventory.Contains(backpackDef));
        }

        [Test]
        public void Contains_OnlyInOpenLoot_False()
        {
            inventory.OpenLoot(new LootContainerModel("상자", new Vector2Int(4, 4), new[] { new ItemInstance(flare) }, 0.5f));
            Assert.IsFalse(inventory.Contains(flare));
        }

        [Test]
        public void Contains_LockedItem_StillTrue()
        {
            EquipVest();
            ItemInstance item = StoreFlare();
            inventory.SetItemLocked(item, true);
            Assert.IsTrue(inventory.Contains(flare), "잠긴 아이템도 가지고는 있다");
        }

        // ── TryConsume ───────────────────────────────────────

        [Test]
        public void TryConsume_RemovesOne_RaisesChanged()
        {
            EquipVest();
            ItemInstance first = StoreFlare();
            ItemInstance second = StoreFlare();
            changedCount = 0;

            Assert.IsTrue(inventory.TryConsume(flare));

            Assert.AreEqual(1, changedCount);
            Assert.IsFalse(inventory.IsInStorage(first), "배치 순 첫 번째를 쓴다");
            Assert.IsTrue(inventory.IsInStorage(second));
            Assert.IsTrue(inventory.Contains(flare));

            Assert.IsTrue(inventory.TryConsume(flare));
            Assert.IsFalse(inventory.Contains(flare));
            Assert.IsFalse(inventory.TryConsume(flare), "없으면 실패");
            Assert.AreEqual(2, changedCount, "실패는 Changed 없음");
        }

        [Test]
        public void TryConsume_StorageGridOrder_PocketBeforeVest()
        {
            var map = M4TestData.MapItem();
            var inPocket = new ItemInstance(map);
            Assert.IsTrue(inventory.TryAutoStore(inPocket));   // 조끼 없음 → 주머니
            EquipVest();
            var inVest = new ItemInstance(map);
            Assert.IsTrue(inventory.TryAutoStore(inVest));     // 조끼 먼저

            Assert.IsTrue(inventory.TryConsume(map));

            Assert.IsFalse(inventory.IsInStorage(inPocket), "StorageGrids 순서(주머니 → 조끼 → 배낭)");
            Assert.IsTrue(inventory.IsInStorage(inVest));
        }

        [Test]
        public void TryConsume_NullDefinition_False()
        {
            Assert.IsFalse(inventory.TryConsume(null));
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void TryConsume_EquippedOnly_False()
        {
            var backpackDef = TestData.Backpack();
            var bag = new ItemInstance(backpackDef);
            Assert.IsTrue(inventory.TryEquipNew(bag, EquipSlot.Backpack));

            Assert.IsFalse(inventory.TryConsume(backpackDef), "장착 슬롯은 소모 대상 아님");
            Assert.AreSame(bag, inventory.GetEquipped(EquipSlot.Backpack));
        }

        [Test]
        public void TryConsume_OnlyInOpenLoot_False()
        {
            var lootFlare = new ItemInstance(flare);
            var loot = new LootContainerModel("상자", new Vector2Int(4, 4), new[] { lootFlare }, 0.5f);
            inventory.OpenLoot(loot);

            Assert.IsFalse(inventory.TryConsume(flare));
            Assert.IsTrue(loot.Grid.Contains(lootFlare));
        }

        [Test]
        public void TryConsume_SkipsLocked()
        {
            EquipVest();
            ItemInstance lockedFlare = StoreFlare();
            inventory.SetItemLocked(lockedFlare, true);

            Assert.IsFalse(inventory.TryConsume(flare), "잠긴 것만 있으면 실패");
            Assert.IsTrue(inventory.IsInStorage(lockedFlare));

            ItemInstance free = StoreFlare();
            Assert.IsTrue(inventory.TryConsume(flare));
            Assert.IsTrue(inventory.IsInStorage(lockedFlare));
            Assert.IsFalse(inventory.IsInStorage(free));
        }

        [Test]
        public void TryConsume_AfterDeath_Rejected()
        {
            EquipVest();
            ItemInstance item = StoreFlare();
            inventory.IsDeadLocked = true;
            changedCount = 0;

            Assert.IsFalse(inventory.TryConsume(flare));
            Assert.IsTrue(inventory.IsInStorage(item));
            Assert.AreEqual(0, changedCount);
            Assert.IsTrue(inventory.Contains(flare), "사망 후에도 조회는 된다");
        }

        [Test]
        public void TryConsume_ClearsQuickSlotReference()
        {
            var bandageDef = TestData.Bandage();
            ItemInstance bandage = ItemFactory.Create(bandageDef);
            Assert.IsTrue(inventory.TryAutoStore(bandage));
            Assert.IsTrue(inventory.TryAssignQuickSlot(0, bandage));

            Assert.IsTrue(inventory.TryConsume(bandageDef));

            Assert.IsNull(inventory.GetQuickSlot(0));
        }

        [Test]
        public void TryConsume_ReducesWeight()
        {
            EquipVest();
            StoreFlare();
            float before = inventory.TotalWeightKg;
            Assert.IsTrue(inventory.TryConsume(flare));
            Assert.AreEqual(before - flare.weightKg, inventory.TotalWeightKg, 1e-4f);
        }

        // ── CanConsume (M4 리뷰 수정 낮음 3: TryConsume과 같은 범위) ──────────

        [Test]
        public void CanConsume_MatchesTryConsume_Storage()
        {
            Assert.IsFalse(inventory.CanConsume(flare));
            Assert.IsFalse(inventory.CanConsume(null));
            EquipVest();
            StoreFlare();
            Assert.IsTrue(inventory.CanConsume(flare));
            Assert.IsTrue(inventory.TryConsume(flare));
            Assert.IsFalse(inventory.CanConsume(flare));
        }

        [Test]
        public void CanConsume_LockedOnly_False_ContainsTrue()
        {
            EquipVest();
            ItemInstance item = StoreFlare();
            inventory.SetItemLocked(item, true);
            Assert.IsTrue(inventory.Contains(flare));
            Assert.IsFalse(inventory.CanConsume(flare));
            Assert.IsFalse(inventory.TryConsume(flare));
        }

        [Test]
        public void CanConsume_EquippedOnly_False_ContainsTrue()
        {
            var vestDef = TestData.Vest();
            Assert.IsTrue(inventory.TryEquipNew(new ItemInstance(vestDef), EquipSlot.TacticalVest));
            Assert.IsTrue(inventory.Contains(vestDef));
            Assert.IsFalse(inventory.CanConsume(vestDef), "장착 슬롯은 소모 대상 아님");
            Assert.IsFalse(inventory.TryConsume(vestDef));
        }

        [Test]
        public void CanConsume_DeadOrRaidEnded_False()
        {
            EquipVest();
            StoreFlare();
            inventory.IsDeadLocked = true;
            Assert.IsFalse(inventory.CanConsume(flare));
            inventory.IsDeadLocked = false;
            inventory.IsRaidEndLocked = true;
            Assert.IsFalse(inventory.CanConsume(flare));
            Assert.IsFalse(inventory.TryConsume(flare));
        }

        [Test]
        public void CanConsume_DoesNotAllocate()
        {
            EquipVest();
            StoreFlare();
            bool any = inventory.CanConsume(flare);
            AiTest.AssertNoAlloc(() =>
            {
                for (int i = 0; i < 100; i++)
                    any |= inventory.CanConsume(flare);
            });
            Assert.IsTrue(any);
        }

        // ── 가짜 서비스 (UI·Raid 테스트용) ─────────────────────────

        [Test]
        public void FakeInventoryService_ContainsAndConsume()
        {
            var fake = new FakeInventoryService();
            var pocket = new FakeGridView(new GridRef(GridKind.Pocket, 0), new Vector2Int(2, 2));
            fake.Grids.Add(pocket);
            Assert.IsFalse(fake.Contains(flare));

            var placedFlare = new ItemInstance(flare);
            pocket.Placed.Add(new PlacedItem(placedFlare, Vector2Int.zero, false));
            Assert.IsTrue(fake.Contains(flare));
            Assert.IsTrue(fake.CanConsume(flare));
            fake.Locked.Add(placedFlare);
            Assert.IsFalse(fake.CanConsume(flare), "잠긴 것은 소모 대상 아님");
            fake.Locked.Remove(placedFlare);
            Assert.IsTrue(fake.TryConsume(flare));
            Assert.IsFalse(fake.Contains(flare));
            Assert.IsFalse(fake.CanConsume(flare));
            Assert.IsFalse(fake.TryConsume(flare));
            Assert.AreEqual(2, fake.Calls.FindAll(c => c == nameof(IInventoryService.TryConsume)).Count);
        }
    }
}
