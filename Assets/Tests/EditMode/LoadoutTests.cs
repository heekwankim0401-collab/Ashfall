using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // §18 반입(Loadout) (M5 설계 §1.3, §4.1): Capture 순서·위치·회전·루트 제외·퀵슬롯, 생성자 규칙, MedicalItem 복원, RaidResult.CarriedOutLoadout
    public class LoadoutTests
    {
        private FakeCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
        }

        // ── Capture ───────────────────────────────────────────

        [Test]
        public void Capture_Order_EqualsInventorySnapshotCollect()
        {
            PlayerInventory inventory = M5TestData.EquippedInventory(catalog, out _);

            Loadout loadout = Loadout.Capture(inventory);
            var fromLoadout = new List<ItemInstance>();
            int added = loadout.CollectItems(fromLoadout);
            var fromSnapshot = new List<ItemInstance>();
            InventorySnapshot.Collect(inventory, fromSnapshot);

            CollectionAssert.AreEqual(fromSnapshot, fromLoadout);
            Assert.AreEqual(fromSnapshot.Count, added);
        }

        [Test]
        public void Capture_EquippedInSlotOrder_ThenPocketsVestBackpack()
        {
            PlayerInventory inventory = M5TestData.EquippedInventory(catalog, out var items);

            var order = new List<ItemInstance>();
            Loadout.Capture(inventory).CollectItems(order);

            CollectionAssert.AreEqual(new[]
            {
                items["rifle"], items["helmet"], items["vest"], items["backpack"],   // 장착 (EquipSlot 순)
                items["bandage"],                                                    // 주머니 2
                items["magFull"], items["magPart"],                                  // 조끼
                items["wires"],                                                      // 배낭
            }, order);
        }

        [Test]
        public void Capture_RecordsLocationsAndRotation()
        {
            PlayerInventory inventory = M5TestData.EquippedInventory(catalog, out var items);
            Loadout loadout = Loadout.Capture(inventory);

            ItemLocation rifle = Find(loadout, items["rifle"]);
            Assert.IsTrue(rifle.IsEquipped);
            Assert.AreEqual(EquipSlot.Primary1, rifle.Slot);

            ItemLocation bandage = Find(loadout, items["bandage"]);
            Assert.IsFalse(bandage.IsEquipped);
            Assert.AreEqual(new GridRef(GridKind.Pocket, 2), bandage.Grid);

            ItemLocation magPart = Find(loadout, items["magPart"]);
            Assert.AreEqual(new GridRef(GridKind.TacticalVest), magPart.Grid);
            Assert.AreEqual(new Vector2Int(1, 0), magPart.Cell);
            Assert.IsTrue(magPart.Rotated);

            ItemLocation magFull = Find(loadout, items["magFull"]);
            Assert.AreEqual(Vector2Int.zero, magFull.Cell);
            Assert.IsFalse(magFull.Rotated);

            ItemLocation wires = Find(loadout, items["wires"]);
            Assert.AreEqual(new GridRef(GridKind.Backpack), wires.Grid);
            Assert.AreEqual(new Vector2Int(2, 3), wires.Cell);
        }

        [Test]
        public void Capture_LoadedMagazine_StaysInsideWeapon()
        {
            PlayerInventory inventory = M5TestData.EquippedInventory(catalog, out var items);
            var rifle = (WeaponItem)items["rifle"];

            var order = new List<ItemInstance>();
            Loadout.Capture(inventory).CollectItems(order);

            CollectionAssert.DoesNotContain(order, rifle.LoadedMagazine, "장착 탄창은 무기에 포함 (따로 넣지 않음)");
            Assert.AreEqual(17, rifle.LoadedMagazine.Rounds);
        }

        [Test]
        public void Capture_ExcludesOpenLoot()
        {
            var inventory = new PlayerInventory(TestData.Inventory());
            var mine = M5TestData.Plain(catalog, "JNK-03");
            Assert.IsTrue(inventory.TryAutoStore(mine));
            inventory.OpenLoot(M5TestData.Loot(M5TestData.Plain(catalog, "JNK-01")));
            Assert.IsNotNull(inventory.ActiveLoot);

            Loadout loadout = Loadout.Capture(inventory);

            Assert.AreEqual(1, loadout.Entries.Count);
            Assert.AreSame(mine, loadout.Entries[0].Item);
        }

        [Test]
        public void Capture_QuickSlots()
        {
            PlayerInventory inventory = M5TestData.EquippedInventory(catalog, out var items);
            Loadout loadout = Loadout.Capture(inventory);

            Assert.AreEqual(Loadout.QuickSlotCount, loadout.QuickSlots.Count);
            Assert.AreSame(items["bandage"], loadout.QuickSlots[0]);
            Assert.IsNull(loadout.QuickSlots[1]);
            Assert.IsNull(loadout.QuickSlots[3]);
        }

        [Test]
        public void Capture_Null_IsEmpty()
        {
            Assert.AreSame(Loadout.Empty, Loadout.Capture(null));
        }

        [Test]
        public void Capture_EmptyInventory_IsEmpty()
        {
            Loadout loadout = Loadout.Capture(new PlayerInventory(TestData.Inventory()));
            Assert.IsTrue(loadout.IsEmpty);
            Assert.AreEqual(4, loadout.QuickSlots.Count);
        }

        // 가짜 인벤토리가 StorageGrids에 루트 그리드를 넣어도 장비로 세지 않는다
        [Test]
        public void Capture_SkipsLootKindGridInStorage()
        {
            var fake = new FakeInventoryService();
            var loot = new ItemGrid(new GridRef(GridKind.Loot), new Vector2Int(4, 4));
            Assert.IsTrue(loot.TryPlace(M5TestData.Plain(catalog, "JNK-03"), Vector2Int.zero, false));
            var pocket = new ItemGrid(new GridRef(GridKind.Pocket, 0), Vector2Int.one);
            var wires = M5TestData.Plain(catalog, "JNK-03");
            Assert.IsTrue(pocket.TryPlace(wires, Vector2Int.zero, false));
            fake.Grids.Add(loot);
            fake.Grids.Add(pocket);

            Loadout loadout = Loadout.Capture(fake);

            Assert.AreEqual(1, loadout.Entries.Count);
            Assert.AreSame(wires, loadout.Entries[0].Item);
        }

        // ── 생성자 ───────────────────────────────────────────

        [Test]
        public void Constructor_Null_IsEmpty()
        {
            var loadout = new Loadout(null, null);
            Assert.IsTrue(loadout.IsEmpty);
            Assert.AreEqual(4, loadout.QuickSlots.Count);
            Assert.IsTrue(Loadout.Empty.IsEmpty);
        }

        [Test]
        public void Constructor_Copies_DropsNullDuplicateAndLoot()
        {
            var a = M5TestData.Plain(catalog, "JNK-01");
            var b = M5TestData.Plain(catalog, "JNK-03");
            var c = M5TestData.Plain(catalog, "JNK-02");
            var source = new List<LoadoutEntry>
            {
                new LoadoutEntry(a, ItemLocation.Equipped(EquipSlot.Primary2)),
                new LoadoutEntry(null, ItemLocation.Equipped(EquipSlot.Helmet)),
                new LoadoutEntry(a, ItemLocation.InGrid(new GridRef(GridKind.Pocket, 0), Vector2Int.zero, false)),
                new LoadoutEntry(c, ItemLocation.InGrid(new GridRef(GridKind.Loot), Vector2Int.zero, false)),
                new LoadoutEntry(b, ItemLocation.InGrid(new GridRef(GridKind.Backpack), new Vector2Int(1, 1), true)),
            };

            var loadout = new Loadout(source, null);
            source.Clear();

            Assert.AreEqual(2, loadout.Entries.Count, "생성 후 원본을 바꿔도 그대로");
            Assert.AreSame(a, loadout.Entries[0].Item);
            Assert.IsTrue(loadout.Entries[0].Location.IsEquipped, "중복은 첫 번째 자리");
            Assert.AreSame(b, loadout.Entries[1].Item);
        }

        [Test]
        public void Constructor_QuickSlots_OnlyEntries_LengthFour()
        {
            var bandage = M5TestData.Bandage(catalog);
            var outsider = M5TestData.Bandage(catalog);
            var entries = new[]
            {
                new LoadoutEntry(bandage, ItemLocation.InGrid(new GridRef(GridKind.Pocket, 0), Vector2Int.zero, false)),
            };

            var loadout = new Loadout(entries, new ItemInstance[] { outsider, bandage, null, null, bandage });

            Assert.AreEqual(4, loadout.QuickSlots.Count, "넘치는 칸은 자른다");
            Assert.IsNull(loadout.QuickSlots[0], "Entries 밖 아이템은 null");
            Assert.AreSame(bandage, loadout.QuickSlots[1]);
        }

        [Test]
        public void CollectItems_AppendsInEntryOrder_NullIsZero()
        {
            Loadout loadout = M5TestData.SampleLoadout(catalog, out var items);
            var existing = M5TestData.Plain(catalog, "JNK-01");
            var into = new List<ItemInstance> { existing };

            Assert.AreEqual(4, loadout.CollectItems(into));
            CollectionAssert.AreEqual(new[] { existing, items["rifle"], items["vest"], items["mag"], items["bandage"] }, into);
            Assert.AreEqual(0, loadout.CollectItems(null));
        }

        // ── MedicalItem 복원 (세이브) ─────────────────────────

        [Test]
        public void MedicalItem_UsesLeftConstructor_Clamps()
        {
            var def = TestData.Medical("TEST-MED", "약", Vector2Int.one, 0.1f, 1f, StatusEffect.Bleed, uses: 3);
            Assert.AreEqual(2, new MedicalItem(def, 2).UsesLeft);
            Assert.AreEqual(0, new MedicalItem(def, -5).UsesLeft);
            Assert.AreEqual(3, new MedicalItem(def, 99).UsesLeft);
            Assert.AreEqual(3, new MedicalItem(def).UsesLeft);

            var depleted = new MedicalItem(def, 0);
            Assert.IsFalse(depleted.ConsumeUse(), "0 횟수도 유지, 사용은 거부");
        }

        // ── RaidResult.CarriedOutLoadout (RULE-01) ───────────

        [Test]
        public void RaidResult_CarriedOutLoadout_OnlyWhenExtracted()
        {
            Loadout loadout = M5TestData.SampleLoadout(catalog, out _);

            Assert.AreSame(loadout, M5TestData.Extracted(loadout).CarriedOutLoadout);
            foreach (RaidOutcome outcome in new[] { RaidOutcome.Killed, RaidOutcome.MissingInAction, RaidOutcome.Abandoned })
            {
                var result = new RaidResult(outcome, null, null, false, BodyPart.Head, 1f, 0, null, null, null, loadout);
                Assert.IsNull(result.CarriedOutLoadout, outcome.ToString());
            }
            Assert.IsNull(M4TestData.Result(RaidOutcome.Extracted, "EXT-01", "남쪽 도로").CarriedOutLoadout,
                "M4 목록 생성자 → null");
        }

        private static ItemLocation Find(Loadout loadout, ItemInstance item)
        {
            foreach (LoadoutEntry entry in loadout.Entries)
                if (ReferenceEquals(entry.Item, item))
                    return entry.Location;
            Assert.Fail($"{item}이 Loadout에 없다");
            return default;
        }
    }
}
