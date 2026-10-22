using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M5 AC2·AC3 — 반입(Loadout)을 PlayerInventory에 위치 그대로 놓기 (M5 설계 §4.3 TryApplyLoadout·TryPlaceNew, RULE-01).
    // Capture → TryApplyLoadout 왕복이 장착·주머니·조끼·배낭·회전·퀵슬롯·탄 수를 보존하는지, 잘못된 자리는 자동 배치 → 거부되는지.
    public class LoadoutApplyTests
    {
        private FakeCatalog catalog;

        [SetUp]
        public void SetUp() => catalog = FakeCatalog.Standard();

        private static PlayerInventory Fresh() => new PlayerInventory(TestData.Inventory());

        private static void AssertSameLocation(ItemLocation expected, ItemLocation actual, string what)
        {
            Assert.AreEqual(expected.IsEquipped, actual.IsEquipped, what + " 장착 여부");
            if (expected.IsEquipped)
            {
                Assert.AreEqual(expected.Slot, actual.Slot, what + " 슬롯");
                return;
            }
            Assert.AreEqual(expected.Grid, actual.Grid, what + " 그리드");
            Assert.AreEqual(expected.Cell, actual.Cell, what + " 칸");
            Assert.AreEqual(expected.Rotated, actual.Rotated, what + " 회전");
        }

        [Test]
        public void QuickSlotCount_IsLoadoutQuickSlotCount()
        {
            Assert.AreEqual(Loadout.QuickSlotCount, PlayerInventory.QuickSlotCount);
        }

        [Test]
        public void RoundTrip_CaptureThenApply_PreservesEveryLocationAndQuickSlot()
        {
            PlayerInventory source = M5TestData.EquippedInventory(catalog, out Dictionary<string, ItemInstance> items);
            Loadout loadout = Loadout.Capture(source);
            var rejected = new List<ItemInstance>();

            PlayerInventory target = Fresh();
            int placed = target.TryApplyLoadout(loadout, rejected);

            Assert.AreEqual(loadout.Entries.Count, placed);
            Assert.AreEqual(8, placed, "소총·헬멧·조끼·배낭 + 붕대·탄창 2·전선");
            Assert.IsEmpty(rejected);
            foreach (LoadoutEntry entry in loadout.Entries)
            {
                Assert.IsTrue(target.TryGetLocation(entry.Item, out ItemLocation at), $"{entry.Item} 없음");
                AssertSameLocation(entry.Location, at, entry.Item.ToString());
            }
            Assert.AreSame(items["bandage"], target.GetQuickSlot(0), "퀵슬롯 유지");
            for (int i = 1; i < PlayerInventory.QuickSlotCount; i++)
                Assert.IsNull(target.GetQuickSlot(i));
        }

        [Test]
        public void RoundTrip_SecondCaptureEqualsFirst()
        {
            PlayerInventory source = M5TestData.EquippedInventory(catalog, out _);
            Loadout first = Loadout.Capture(source);
            PlayerInventory target = Fresh();
            target.TryApplyLoadout(first, null);

            Loadout second = Loadout.Capture(target);

            Assert.AreEqual(first.Entries.Count, second.Entries.Count);
            for (int i = 0; i < first.Entries.Count; i++)
            {
                Assert.AreSame(first.Entries[i].Item, second.Entries[i].Item, $"순서 {i}");
                AssertSameLocation(first.Entries[i].Location, second.Entries[i].Location, $"항목 {i}");
            }
            CollectionAssert.AreEqual(first.QuickSlots, second.QuickSlots);
        }

        [Test]
        public void RoundTrip_KeepsInstanceState_RoundsAndLoadedMagazine()
        {
            PlayerInventory source = M5TestData.EquippedInventory(catalog, out Dictionary<string, ItemInstance> items);
            PlayerInventory target = Fresh();
            target.TryApplyLoadout(Loadout.Capture(source), null);

            var rifle = (WeaponItem)target.GetEquipped(EquipSlot.Primary1);
            Assert.AreSame(items["rifle"], rifle, "같은 인스턴스");
            Assert.IsNotNull(rifle.LoadedMagazine);
            Assert.AreEqual(17, rifle.LoadedMagazine.Rounds);
            Assert.AreEqual(12, ((Magazine)items["magPart"]).Rounds);
            Assert.AreEqual(30, ((Magazine)items["magFull"]).Rounds);
        }

        [Test]
        public void Apply_SampleLoadout_ExactPlacement()
        {
            Loadout loadout = M5TestData.SampleLoadout(catalog, out Dictionary<string, ItemInstance> items);
            PlayerInventory inv = Fresh();
            var rejected = new List<ItemInstance>();

            Assert.AreEqual(4, inv.TryApplyLoadout(loadout, rejected));
            Assert.IsEmpty(rejected);

            Assert.AreSame(items["rifle"], inv.GetEquipped(EquipSlot.Primary1));
            Assert.AreSame(items["vest"], inv.GetEquipped(EquipSlot.TacticalVest));
            Assert.IsTrue(inv.TryGetLocation(items["mag"], out ItemLocation mag));
            AssertSameLocation(ItemLocation.InGrid(new GridRef(GridKind.TacticalVest), new Vector2Int(0, 1), true), mag, "탄창");
            Assert.IsTrue(inv.TryGetLocation(items["bandage"], out ItemLocation band));
            AssertSameLocation(ItemLocation.InGrid(new GridRef(GridKind.Pocket, 1), Vector2Int.zero, false), band, "붕대");
            Assert.AreSame(items["bandage"], inv.GetQuickSlot(0));
        }

        [Test]
        public void Apply_WrongPocketIndex_AutoStored_NotRejected()
        {
            var wires = M5TestData.Plain(catalog, "JNK-03");
            var loadout = new Loadout(new[]
            {
                new LoadoutEntry(wires, ItemLocation.InGrid(new GridRef(GridKind.Pocket, 9), Vector2Int.zero, false)),
            }, null);
            PlayerInventory inv = Fresh();
            var rejected = new List<ItemInstance>();

            Assert.AreEqual(1, inv.TryApplyLoadout(loadout, rejected));
            Assert.IsEmpty(rejected);
            Assert.IsTrue(inv.IsInStorage(wires), "자동 배치 (조끼 → 주머니 → 배낭)");
        }

        [Test]
        public void Apply_OutOfRangeOrOverlappingCell_AutoStored()
        {
            var a = M5TestData.Plain(catalog, "JNK-03");
            var b = M5TestData.Plain(catalog, "JNK-03");
            var c = M5TestData.Plain(catalog, "JNK-03");
            var pocket0 = new GridRef(GridKind.Pocket, 0);
            var loadout = new Loadout(new[]
            {
                new LoadoutEntry(a, ItemLocation.InGrid(pocket0, Vector2Int.zero, false)),
                new LoadoutEntry(b, ItemLocation.InGrid(pocket0, Vector2Int.zero, false)),        // 겹침
                new LoadoutEntry(c, ItemLocation.InGrid(pocket0, new Vector2Int(5, 5), false)),   // 범위 밖
            }, null);
            PlayerInventory inv = Fresh();
            var rejected = new List<ItemInstance>();

            Assert.AreEqual(3, inv.TryApplyLoadout(loadout, rejected));
            Assert.IsEmpty(rejected);
            Assert.IsTrue(inv.TryGetLocation(a, out ItemLocation at));
            AssertSameLocation(ItemLocation.InGrid(pocket0, Vector2Int.zero, false), at, "정확한 자리가 먼저");
            Assert.IsTrue(inv.IsInStorage(b));
            Assert.IsTrue(inv.IsInStorage(c));
        }

        [Test]
        public void Apply_ExactPlacementsWinOverFallbacks()
        {
            // 앞 항목이 자리를 잃어도(잘못된 주머니) 뒤 항목의 정확한 자리(주머니 0)를 뺏지 않는다.
            var lost = M5TestData.Plain(catalog, "JNK-03");
            var exact = M5TestData.Plain(catalog, "JNK-03");
            var loadout = new Loadout(new[]
            {
                new LoadoutEntry(lost, ItemLocation.InGrid(new GridRef(GridKind.Pocket, 7), Vector2Int.zero, false)),
                new LoadoutEntry(exact, ItemLocation.InGrid(new GridRef(GridKind.Pocket, 0), Vector2Int.zero, false)),
            }, null);
            PlayerInventory inv = Fresh();
            inv.TryApplyLoadout(loadout, null);

            Assert.IsTrue(inv.TryGetLocation(exact, out ItemLocation at));
            Assert.AreEqual(new GridRef(GridKind.Pocket, 0), at.Grid);
            Assert.IsTrue(inv.TryGetLocation(lost, out ItemLocation lostAt));
            Assert.AreEqual(new GridRef(GridKind.Pocket, 1), lostAt.Grid, "남은 첫 주머니");
        }

        [Test]
        public void Apply_VestContentsWithoutVest_AutoStoredOrRejected()
        {
            var wires = M5TestData.Plain(catalog, "JNK-03");
            var engine = M5TestData.Plain(catalog, "JNK-02");   // 3×2: 주머니(1×1)에 안 들어간다
            var vestGrid = new GridRef(GridKind.TacticalVest);
            var loadout = new Loadout(new[]
            {
                new LoadoutEntry(wires, ItemLocation.InGrid(vestGrid, Vector2Int.zero, false)),
                new LoadoutEntry(engine, ItemLocation.InGrid(vestGrid, new Vector2Int(1, 0), false)),
            }, null);
            PlayerInventory inv = Fresh();
            var rejected = new List<ItemInstance>();

            Assert.AreEqual(1, inv.TryApplyLoadout(loadout, rejected));
            Assert.IsTrue(inv.IsInStorage(wires), "조끼가 없으면 주머니로");
            CollectionAssert.AreEqual(new[] { engine }, rejected, "들어갈 곳이 없으면 거부");
            Assert.IsFalse(inv.TryGetLocation(engine, out _));
        }

        [Test]
        public void Apply_EquipRuleViolation_FallsBackToStorage()
        {
            var wires = M5TestData.Plain(catalog, "JNK-03");
            var rifle = M5TestData.Rifle(catalog);
            var loadout = new Loadout(new[]
            {
                new LoadoutEntry(wires, ItemLocation.Equipped(EquipSlot.Helmet)),
                new LoadoutEntry(rifle, ItemLocation.Equipped(EquipSlot.Backpack)),
            }, null);
            PlayerInventory inv = Fresh();
            var rejected = new List<ItemInstance>();

            Assert.AreEqual(1, inv.TryApplyLoadout(loadout, rejected));
            Assert.IsNull(inv.GetEquipped(EquipSlot.Helmet));
            Assert.IsTrue(inv.IsInStorage(wires));
            CollectionAssert.AreEqual(new ItemInstance[] { rifle }, rejected, "소총(4×2)은 주머니에 안 들어간다");
        }

        [Test]
        public void Apply_ItemAlreadyInInventory_Skipped_NotDuplicated()
        {
            Loadout loadout = M5TestData.SampleLoadout(catalog, out Dictionary<string, ItemInstance> items);
            PlayerInventory inv = Fresh();
            inv.TryApplyLoadout(loadout, null);
            var rejected = new List<ItemInstance>();

            Assert.AreEqual(0, inv.TryApplyLoadout(loadout, rejected), "두 번째 적용은 아무것도 놓지 않는다");
            Assert.IsEmpty(rejected, "이미 있는 아이템을 거부 목록(→ 보관함)에 넣으면 복제된다");
            Assert.AreEqual(4, Loadout.Capture(inv).Entries.Count);
        }

        [Test]
        public void Apply_QuickSlot_OnlyForPlacedMedical()
        {
            var bandage = M5TestData.Bandage(catalog);
            var engine = M5TestData.Plain(catalog, "JNK-02");
            var loadout = new Loadout(new[]
            {
                // 잘못된 자리지만 주머니에 자동 배치된다 → 퀵슬롯 유지
                new LoadoutEntry(bandage, ItemLocation.InGrid(new GridRef(GridKind.Backpack), Vector2Int.zero, false)),
                new LoadoutEntry(engine, ItemLocation.InGrid(new GridRef(GridKind.Pocket, 0), Vector2Int.zero, false)),
            }, new ItemInstance[] { null, null, bandage, engine });
            PlayerInventory inv = Fresh();

            inv.TryApplyLoadout(loadout, null);

            Assert.AreSame(bandage, inv.GetQuickSlot(2));
            Assert.IsNull(inv.GetQuickSlot(3), "의약품이 아니고 놓이지도 않음");
        }

        [Test]
        public void Apply_Frozen_RejectsEverything()
        {
            Loadout loadout = M5TestData.SampleLoadout(catalog, out _);
            PlayerInventory inv = Fresh();
            inv.IsRaidEndLocked = true;
            var rejected = new List<ItemInstance>();

            Assert.AreEqual(0, inv.TryApplyLoadout(loadout, rejected));
            Assert.AreEqual(4, rejected.Count);
            Assert.IsTrue(Loadout.Capture(inv).IsEmpty);
        }

        [Test]
        public void Apply_NullOrEmpty_PlacesNothing()
        {
            PlayerInventory inv = Fresh();
            var rejected = new List<ItemInstance>();
            Assert.AreEqual(0, inv.TryApplyLoadout(null, rejected));
            Assert.AreEqual(0, inv.TryApplyLoadout(Loadout.Empty, rejected));
            Assert.IsEmpty(rejected);
        }

        [Test]
        public void Apply_RaisesChanged()
        {
            PlayerInventory inv = Fresh();
            int changed = 0;
            inv.Changed += () => changed++;
            inv.TryApplyLoadout(M5TestData.SampleLoadout(catalog, out _), null);
            Assert.Greater(changed, 0);
        }

        // ── TryPlaceNew ──────────────────────────────────────

        [Test]
        public void TryPlaceNew_ExactCell()
        {
            PlayerInventory inv = Fresh();
            var wires = M5TestData.Plain(catalog, "JNK-03");
            Assert.IsTrue(inv.TryPlaceNew(wires, new GridRef(GridKind.Pocket, 3), Vector2Int.zero, false));
            Assert.IsTrue(inv.TryGetLocation(wires, out ItemLocation at));
            Assert.AreEqual(new GridRef(GridKind.Pocket, 3), at.Grid);
        }

        [Test]
        public void TryPlaceNew_RejectsLootOccupiedMissingGridKnownItemAndFrozen()
        {
            PlayerInventory inv = Fresh();
            var loot = M5TestData.Loot();
            inv.OpenLoot(loot);
            var pocket = new GridRef(GridKind.Pocket, 0);
            var a = M5TestData.Plain(catalog, "JNK-03");
            var b = M5TestData.Plain(catalog, "JNK-03");

            Assert.IsFalse(inv.TryPlaceNew(a, new GridRef(GridKind.Loot), Vector2Int.zero, false), "루트 자리 불가");
            Assert.IsFalse(inv.TryPlaceNew(a, new GridRef(GridKind.Backpack), Vector2Int.zero, false), "배낭 없음");
            Assert.IsTrue(inv.TryPlaceNew(a, pocket, Vector2Int.zero, false));
            Assert.IsFalse(inv.TryPlaceNew(a, new GridRef(GridKind.Pocket, 1), Vector2Int.zero, false), "이미 인벤토리에 있음");
            Assert.IsFalse(inv.TryPlaceNew(b, pocket, Vector2Int.zero, false), "칸이 차 있음");
            Assert.IsFalse(inv.TryPlaceNew(null, pocket, Vector2Int.zero, false));

            inv.IsDeadLocked = true;
            Assert.IsFalse(inv.TryPlaceNew(b, new GridRef(GridKind.Pocket, 1), Vector2Int.zero, false), "사망 잠금");
        }

        [Test]
        public void RaidInventory_DefaultsAllowDropAndUse()
        {
            PlayerInventory inv = Fresh();
            Assert.IsTrue(inv.AllowDrop);
            Assert.IsTrue(inv.AllowUse);
            var bandage = M5TestData.Bandage(catalog);
            Assert.IsTrue(inv.TryAutoStore(bandage));
            Assert.IsTrue(inv.TryUse(bandage), "레이드 기본값은 사용 가능 (M2 그대로)");
        }
    }
}
