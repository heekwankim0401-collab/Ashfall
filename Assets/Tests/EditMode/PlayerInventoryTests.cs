using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // SYS-INV 장착 슬롯·저장 공간·무게·퀵슬롯·루트, IMagazineSource 재장전 연동 (§5.1, §6)
    public class PlayerInventoryTests
    {
        private PlayerInventory inventory;
        private MagazineDefinitionSO mag30;

        [SetUp]
        public void SetUp()
        {
            inventory = new PlayerInventory(TestData.Inventory());
            mag30 = TestData.Magazine30();
        }

        private ItemInstance EquipVest()
        {
            var vest = new ItemInstance(TestData.Vest());
            Assert.IsTrue(inventory.TryEquipNew(vest, EquipSlot.TacticalVest));
            return vest;
        }

        private ItemInstance EquipBackpack()
        {
            var bag = new ItemInstance(TestData.Backpack());
            Assert.IsTrue(inventory.TryEquipNew(bag, EquipSlot.Backpack));
            return bag;
        }

        private static LootContainerModel Loot(params ItemInstance[] items) =>
            new LootContainerModel("상자", new Vector2Int(4, 4), items, 0.5f);

        // ── 저장 공간 ──────────────────────────────────────

        [Test]
        public void Pockets_Four1x1_Only()
        {
            Assert.AreEqual(4, inventory.StorageGrids.Count);
            for (int i = 0; i < 4; i++)
                Assert.IsTrue(inventory.TryAutoStore(new ItemInstance(TestData.Wires())), $"주머니 {i}");
            Assert.IsFalse(inventory.TryAutoStore(new ItemInstance(TestData.Wires())), "주머니 4개가 다 참");

            var fresh = new PlayerInventory(TestData.Inventory());
            Assert.IsFalse(fresh.TryAutoStore(Magazine.Full(mag30)), "1×2는 주머니에 못 들어간다");
        }

        [Test]
        public void StorageGrids_PocketsThenVestThenBackpack()
        {
            EquipBackpack();
            EquipVest();
            var kinds = inventory.StorageGrids.Select(g => g.Ref.Kind).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                GridKind.Pocket, GridKind.Pocket, GridKind.Pocket, GridKind.Pocket, GridKind.TacticalVest, GridKind.Backpack,
            }, kinds);
            Assert.AreEqual(new Vector2Int(4, 2), inventory.StorageGrids[4].Size);
            Assert.AreEqual(new Vector2Int(4, 5), inventory.StorageGrids[5].Size);
        }

        [Test]
        public void AutoStore_Order_VestThenPocketsThenBackpack()
        {
            EquipBackpack();
            EquipVest();
            var wires = new ItemInstance(TestData.Wires());
            inventory.TryAutoStore(wires);
            Assert.IsTrue(inventory.TryGetLocation(wires, out ItemLocation loc));
            Assert.AreEqual(GridKind.TacticalVest, loc.Grid.Kind);

            // 조끼(4×2)를 1×2 탄창 4개로 채우면 다음 1×1은 주머니, 1×2는 배낭
            var fresh = new PlayerInventory(TestData.Inventory());
            fresh.TryEquipNew(new ItemInstance(TestData.Vest()), EquipSlot.TacticalVest);
            fresh.TryEquipNew(new ItemInstance(TestData.Backpack()), EquipSlot.Backpack);
            for (int i = 0; i < 4; i++)
                Assert.IsTrue(fresh.TryAutoStore(Magazine.Full(mag30)));
            var small = new ItemInstance(TestData.Wires());
            fresh.TryAutoStore(small);
            fresh.TryGetLocation(small, out loc);
            Assert.AreEqual(GridKind.Pocket, loc.Grid.Kind);
            var splint = new MedicalItem(TestData.Splint());
            fresh.TryAutoStore(splint);
            fresh.TryGetLocation(splint, out loc);
            Assert.AreEqual(GridKind.Backpack, loc.Grid.Kind);
        }

        [Test]
        public void TryMove_Rotated_StoresRotation()
        {
            EquipVest();
            var splint = new MedicalItem(TestData.Splint());   // 1×2
            inventory.TryAutoStore(splint);
            var vestRef = new GridRef(GridKind.TacticalVest);

            Assert.IsTrue(inventory.CanPlace(splint, vestRef, new Vector2Int(2, 1), true));
            Assert.IsFalse(inventory.CanPlace(splint, vestRef, new Vector2Int(3, 1), true), "회전하면 2×1 → 오른쪽 넘침");
            Assert.IsTrue(inventory.TryMove(splint, vestRef, new Vector2Int(2, 1), true));
            Assert.IsTrue(inventory.TryGetLocation(splint, out ItemLocation loc));
            Assert.AreEqual(new Vector2Int(2, 1), loc.Cell);
            Assert.IsTrue(loc.Rotated);
        }

        [Test]
        public void TryMove_ToMissingGrid_Fails()
        {
            var wires = new ItemInstance(TestData.Wires());
            inventory.TryAutoStore(wires);
            Assert.IsFalse(inventory.TryMove(wires, new GridRef(GridKind.Backpack), Vector2Int.zero, false), "배낭 없음");
            Assert.IsFalse(inventory.TryMove(wires, new GridRef(GridKind.Pocket, 9), Vector2Int.zero, false));
        }

        // ── 장착 ─────────────────────────────────────────

        [Test]
        public void Equip_OccupiedSlot_Fails_NoSwap()
        {
            Assert.IsTrue(inventory.TryEquipNew(new WeaponItem(TestData.Rifle()), EquipSlot.Primary1));
            var second = new WeaponItem(TestData.Rifle());
            Assert.IsFalse(inventory.CanEquip(second, EquipSlot.Primary1));
            Assert.IsFalse(inventory.TryEquipNew(second, EquipSlot.Primary1));
            Assert.IsTrue(inventory.TryEquipNew(second, EquipSlot.Primary2));
        }

        [Test]
        public void Equip_WrongSlot_Fails()
        {
            Assert.IsFalse(inventory.TryEquipNew(new ItemInstance(TestData.Helmet()), EquipSlot.BodyArmor));
            Assert.IsFalse(inventory.TryEquipNew(Magazine.Full(mag30), EquipSlot.Primary1));
        }

        [Test]
        public void VestWithContents_CannotUnequipMoveOrDrop()
        {
            EquipBackpack();
            var vest = EquipVest();
            var wires = new ItemInstance(TestData.Wires());
            inventory.TryAutoStore(wires);   // 조끼로

            Assert.IsFalse(inventory.TryMove(vest, new GridRef(GridKind.Backpack), Vector2Int.zero, false));
            Assert.IsFalse(inventory.TryDrop(vest));
            Assert.AreSame(vest, inventory.GetEquipped(EquipSlot.TacticalVest));
        }

        [Test]
        public void EmptyVest_MovesIntoBackpack_GridRemoved()
        {
            EquipBackpack();
            var vest = EquipVest();
            var events = new List<(EquipSlot, ItemInstance)>();
            inventory.EquipmentChanged += (s, i) => events.Add((s, i));

            Assert.IsFalse(inventory.CanPlace(vest, new GridRef(GridKind.TacticalVest), Vector2Int.zero, false),
                "자기 내용물 그리드에는 못 넣는다");
            Assert.IsTrue(inventory.TryMove(vest, new GridRef(GridKind.Backpack), Vector2Int.zero, false));
            Assert.IsNull(inventory.GetEquipped(EquipSlot.TacticalVest));
            Assert.AreEqual(5, inventory.StorageGrids.Count, "조끼 그리드 제거");
            Assert.IsTrue(inventory.IsInStorage(vest));
            CollectionAssert.Contains(events, (EquipSlot.TacticalVest, (ItemInstance)null));
        }

        [Test]
        public void Equip_FromStorage_RemovesFromGrid_RaisesEvent()
        {
            EquipBackpack();
            var helmet = new ItemInstance(TestData.Helmet());
            inventory.TryAutoStore(helmet);
            ItemInstance raised = null;
            inventory.EquipmentChanged += (s, i) => { if (s == EquipSlot.Helmet) raised = i; };

            Assert.IsTrue(inventory.TryEquip(helmet, EquipSlot.Helmet));
            Assert.IsFalse(inventory.IsInStorage(helmet));
            Assert.AreSame(helmet, raised);
            Assert.IsTrue(inventory.TryGetLocation(helmet, out ItemLocation loc));
            Assert.IsTrue(loc.IsEquipped);
            Assert.AreEqual(EquipSlot.Helmet, loc.Slot);
        }

        // ── 무게 ─────────────────────────────────────────

        [Test]
        public void Weight_EquippedPlusStorage_IncludesLoadedMagazine_ExcludesLoot()
        {
            var rifleDef = TestData.Rifle();
            rifleDef.weightKg = 3.5f;
            mag30.weightKg = 0.5f;
            int changed = 0;
            inventory.Changed += () => changed++;

            inventory.TryEquipNew(new WeaponItem(rifleDef, Magazine.Full(mag30)), EquipSlot.Primary1);
            EquipVest();                                                  // 1.2
            inventory.TryAutoStore(new ItemInstance(TestData.Wires()));  // 0.3
            Assert.AreEqual(3.5f + 0.5f + 1.2f + 0.3f, inventory.TotalWeightKg, 1e-4f);
            Assert.Greater(changed, 0);

            inventory.OpenLoot(Loot(new ItemInstance(TestData.EnginePart())));
            Assert.AreEqual(5.5f, inventory.TotalWeightKg, 1e-4f, "루트는 제외");
        }

        [Test]
        public void Drop_RemovesAndRaisesDropped()
        {
            var wires = new ItemInstance(TestData.Wires());
            inventory.TryAutoStore(wires);
            ItemInstance dropped = null;
            inventory.Dropped += i => dropped = i;
            Assert.IsTrue(inventory.TryDrop(wires));
            Assert.AreSame(wires, dropped);
            Assert.IsFalse(inventory.IsInStorage(wires));
            Assert.AreEqual(0f, inventory.TotalWeightKg);
        }

        // ── 퀵슬롯·의약품 ─────────────────────────────────

        [Test]
        public void QuickSlot_MedicalInStorageOnly()
        {
            var bandage = new MedicalItem(TestData.Bandage());
            Assert.IsFalse(inventory.TryAssignQuickSlot(0, bandage), "저장 공간에 없음");
            inventory.TryAutoStore(bandage);
            Assert.IsTrue(inventory.TryAssignQuickSlot(0, bandage));
            Assert.IsTrue(inventory.TryAssignQuickSlot(2, bandage));
            Assert.IsNull(inventory.GetQuickSlot(0), "같은 아이템은 한 슬롯에만");
            Assert.AreSame(bandage, inventory.GetQuickSlot(2));

            var wires = new ItemInstance(TestData.Wires());
            inventory.TryAutoStore(wires);
            Assert.IsFalse(inventory.TryAssignQuickSlot(1, wires), "의약품만");
            Assert.IsFalse(inventory.TryAssignQuickSlot(4, bandage), "0~3");
            Assert.IsNull(inventory.GetQuickSlot(-1));
        }

        [Test]
        public void QuickSlot_ClearedWhenItemLeavesStorage()
        {
            var bandage = new MedicalItem(TestData.Bandage());
            inventory.TryAutoStore(bandage);
            inventory.TryAssignQuickSlot(1, bandage);
            inventory.OpenLoot(Loot());
            Assert.IsTrue(inventory.TryQuickMove(bandage));
            Assert.IsNull(inventory.GetQuickSlot(1));
        }

        [Test]
        public void TryUse_RaisesRequest_LockedItemCannotMoveOrUse()
        {
            var bandage = new MedicalItem(TestData.Bandage());
            inventory.TryAutoStore(bandage);
            MedicalItem requested = null;
            inventory.UseRequested += m => requested = m;

            Assert.IsTrue(inventory.TryUse(bandage));
            Assert.AreSame(bandage, requested);
            Assert.IsFalse(inventory.TryUse(new ItemInstance(TestData.Wires())));

            inventory.SetItemLocked(bandage, true);
            Assert.IsFalse(inventory.TryUse(bandage));
            Assert.IsFalse(inventory.TryDrop(bandage));
            Assert.IsFalse(inventory.TryMove(bandage, new GridRef(GridKind.Pocket, 3), Vector2Int.zero, false));
            inventory.SetItemLocked(bandage, false);
            Assert.IsTrue(inventory.TryMove(bandage, new GridRef(GridKind.Pocket, 3), Vector2Int.zero, false));
        }

        [Test]
        public void DeadLocked_RejectsAllCommands()
        {
            var wires = new ItemInstance(TestData.Wires());
            inventory.TryAutoStore(wires);
            inventory.IsDeadLocked = true;
            Assert.IsFalse(inventory.TryMove(wires, new GridRef(GridKind.Pocket, 3), Vector2Int.zero, false));
            Assert.IsFalse(inventory.TryDrop(wires));
            Assert.IsFalse(inventory.TryEquipNew(new ItemInstance(TestData.Helmet()), EquipSlot.Helmet));
            Assert.IsFalse(inventory.TryAutoStore(new ItemInstance(TestData.Wires())));
            inventory.OpenLoot(Loot());
            Assert.IsNull(inventory.ActiveLoot);
        }

        // ── 루트 ─────────────────────────────────────────

        [Test]
        public void Loot_UnrevealedItemsCannotBeTaken_RevealedCan()
        {
            var helmet = new ItemInstance(TestData.Helmet());
            var loot = Loot(helmet);
            bool? session = null;
            inventory.LootSessionChanged += open => session = open;
            EquipBackpack();
            inventory.OpenLoot(loot);
            Assert.AreEqual(true, session);
            Assert.AreSame(loot, inventory.ActiveLoot);

            Assert.IsFalse(inventory.TryQuickMove(helmet));
            Assert.IsFalse(inventory.TryMove(helmet, new GridRef(GridKind.Backpack), Vector2Int.zero, false));
            Assert.IsFalse(inventory.TryDrop(helmet));
            Assert.IsFalse(inventory.TryEquip(helmet, EquipSlot.Helmet));

            loot.Tick(0.5f);
            Assert.IsTrue(inventory.TryQuickMove(helmet));
            Assert.IsTrue(inventory.IsInStorage(helmet));
            Assert.IsFalse(loot.Grid.Contains(helmet));

            inventory.CloseLoot();
            Assert.AreEqual(false, session);
            Assert.IsNull(inventory.ActiveLoot);
        }

        [Test]
        public void Loot_PlayerCanPutOwnItemIntoEmptyCell()
        {
            var hidden = new ItemInstance(TestData.Wires());
            var loot = Loot(hidden);
            var mine = new ItemInstance(TestData.Wires());
            inventory.TryAutoStore(mine);
            inventory.OpenLoot(loot);

            var lootRef = new GridRef(GridKind.Loot);
            Assert.IsFalse(inventory.CanPlace(mine, lootRef, Vector2Int.zero, false), "검색 전 아이템 자리");
            Assert.IsTrue(inventory.TryMove(mine, lootRef, new Vector2Int(3, 3), false));
            Assert.IsTrue(loot.IsRevealed(mine), "내가 넣은 것은 드러나 있다");
            Assert.IsTrue(inventory.TryQuickMove(mine), "다시 저장 공간으로");
        }

        [Test]
        public void Loot_ClosedGridNotAddressable()
        {
            var wires = new ItemInstance(TestData.Wires());
            inventory.TryAutoStore(wires);
            Assert.IsFalse(inventory.TryMove(wires, new GridRef(GridKind.Loot), Vector2Int.zero, false));
            Assert.IsFalse(inventory.TryQuickMove(wires), "열린 루트 없음");
        }

        [Test]
        public void ItemLocation_InLoot()
        {
            var wires = new ItemInstance(TestData.Wires());
            var loot = Loot(wires);
            inventory.OpenLoot(loot);
            Assert.IsTrue(inventory.TryGetLocation(wires, out ItemLocation loc));
            Assert.IsFalse(loc.IsEquipped);
            Assert.AreEqual(GridKind.Loot, loc.Grid.Kind);
        }

        // ── IMagazineSource ──────────────────────────────

        [Test]
        public void MagazineSource_FullestFromVestAndPockets_BackpackExcluded()
        {
            EquipVest();
            EquipBackpack();
            var ten = new Magazine(mag30, 10);
            var twenty = new Magazine(mag30, 20);
            var full = Magazine.Full(mag30);
            inventory.TryAutoStore(ten);
            inventory.TryAutoStore(twenty);
            // 배낭에 직접 넣는다
            inventory.TryAutoStore(full);
            Assert.IsTrue(inventory.TryMove(full, new GridRef(GridKind.Backpack), Vector2Int.zero, false));

            Assert.AreSame(twenty, inventory.TakeFullest(AmmoType.Rifle556));
            Assert.IsFalse(inventory.IsInStorage(twenty));
            Assert.AreSame(ten, inventory.TakeFullest(AmmoType.Rifle556));
            Assert.IsFalse(inventory.HasLoadable(AmmoType.Rifle556), "배낭은 제외 (reloadFromBackpack = false)");
            Assert.IsNull(inventory.TakeFullest(AmmoType.Rifle556));
        }

        [Test]
        public void MagazineSource_ReloadFromBackpack_WhenConfigured()
        {
            var config = TestData.Inventory();
            config.reloadFromBackpack = true;
            var inv = new PlayerInventory(config);
            inv.TryEquipNew(new ItemInstance(TestData.Backpack()), EquipSlot.Backpack);
            var full = Magazine.Full(mag30);
            Assert.IsTrue(inv.TryAutoStore(full));
            Assert.IsTrue(inv.HasLoadable(AmmoType.Rifle556));
            Assert.AreSame(full, inv.TakeFullest(AmmoType.Rifle556));
        }

        [Test]
        public void MagazineSource_IgnoresEmptyAndWrongAmmo()
        {
            EquipVest();
            inventory.TryAutoStore(new Magazine(mag30, 0));
            inventory.TryAutoStore(Magazine.Full(TestData.PistolMagazine()));
            Assert.IsFalse(inventory.HasLoadable(AmmoType.Rifle556));
            Assert.IsTrue(inventory.HasLoadable(AmmoType.Pistol9mm));
        }

        [Test]
        public void MagazineSource_TryStore_NoSpace_ReturnsFalse()
        {
            // 조끼 없음 → 1×2 탄창이 들어갈 곳 없음
            Assert.IsFalse(inventory.TryStore(new Magazine(mag30, 5)));
            EquipVest();
            Assert.IsTrue(inventory.TryStore(new Magazine(mag30, 5)));
        }

        // ── WeaponModel + PlayerInventory (§5.1 재장전) ─────────────

        private (WeaponModel model, Magazine loaded, Magazine spare) RifleWithVestSpare()
        {
            EquipVest();
            var loaded = new Magazine(mag30, 7);
            var spare = Magazine.Full(mag30);
            Assert.IsTrue(inventory.TryAutoStore(spare));
            var item = new WeaponItem(TestData.Rifle(), loaded);
            Assert.IsTrue(inventory.TryEquipNew(item, EquipSlot.Primary1));
            return (new WeaponModel(item, inventory), loaded, spare);
        }

        [Test]
        public void NormalReload_OldMagazineBackToInventory()
        {
            var (model, loaded, spare) = RifleWithVestSpare();
            Magazine dropped = null;
            model.MagazineDropped += m => dropped = m;

            Assert.IsTrue(model.PressReload());
            model.Tick(3f);

            Assert.AreSame(spare, model.Current);
            Assert.IsFalse(inventory.IsInStorage(spare));
            Assert.IsTrue(inventory.IsInStorage(loaded), "일반 재장전 = 회수");
            Assert.AreEqual(7, loaded.Rounds, "탄 수 보존");
            Assert.IsNull(dropped);
        }

        [Test]
        public void FastReload_OldMagazineDropped()
        {
            var (model, loaded, spare) = RifleWithVestSpare();
            Magazine dropped = null;
            model.MagazineDropped += m => dropped = m;

            model.PressReload();
            model.PressReload();
            model.Tick(2f);

            Assert.AreSame(spare, model.Current);
            Assert.AreSame(loaded, dropped);
            Assert.IsFalse(inventory.IsInStorage(loaded));
        }

        [Test]
        public void NormalReload_TakenSlotReusedForReturn()
        {
            // 주머니 4개가 꽉 차 있어도 꺼낸 자리(1×1)로 같은 크기 탄창을 회수한다.
            var pistolMag = TestData.PistolMagazine();
            var spare = Magazine.Full(pistolMag);
            inventory.TryAutoStore(spare);
            for (int i = 0; i < 3; i++)
                inventory.TryAutoStore(new ItemInstance(TestData.Wires()));
            var loaded = new Magazine(pistolMag, 3);
            var model = new WeaponModel(new WeaponItem(TestData.Pistol(), loaded), inventory);
            Magazine dropped = null;
            model.MagazineDropped += m => dropped = m;

            model.PressReload();
            model.Tick(3f);

            Assert.AreSame(spare, model.Current);
            Assert.IsNull(dropped, "꺼낸 자리로 회수");
            Assert.IsTrue(inventory.IsInStorage(loaded));
        }

        [Test]
        public void NormalReload_NoSpace_DropsOldMagazine()
        {
            // 조끼 없음. 예비는 1×1 5.56 탄창(주머니), 나머지 주머니는 꽉 참. 빼낸 1×2 탄창은 들어갈 곳이 없다.
            var smallDef = ScriptableObject.CreateInstance<MagazineDefinitionSO>();
            smallDef.id = "TEST-MAG-556-10"; smallDef.size = Vector2Int.one;
            smallDef.ammo = AmmoType.Rifle556; smallDef.capacity = 10;
            var spare = Magazine.Full(smallDef);
            inventory.TryAutoStore(spare);
            for (int i = 0; i < 3; i++)
                inventory.TryAutoStore(new ItemInstance(TestData.Wires()));
            var loaded = new Magazine(mag30, 2);
            var model = new WeaponModel(new WeaponItem(TestData.Rifle(), loaded), inventory);
            Magazine dropped = null;
            model.MagazineDropped += m => dropped = m;

            Assert.IsTrue(model.PressReload());
            model.Tick(3f);

            Assert.AreSame(spare, model.Current);
            Assert.AreSame(loaded, dropped, "회수 공간 없음 → 바닥");
            Assert.IsFalse(inventory.IsInStorage(loaded));
        }

        [Test]
        public void Reload_RefusedWhenOnlyBackpackHasMagazines()
        {
            EquipBackpack();
            var spare = Magazine.Full(mag30);
            inventory.TryAutoStore(spare);   // 조끼 없음, 주머니 1×1 → 배낭
            var model = new WeaponModel(new WeaponItem(TestData.Rifle(), new Magazine(mag30, 1)), inventory);
            Assert.IsFalse(model.CanReload);
            Assert.IsFalse(model.PressReload());
        }

        // ── 리뷰 수정: 다른 경로로 빠진 아이템 복제·부활 방지 ─────────────

        // 시나리오 A: 드래그 중 재장전 완료로 TakeFullest가 탄창을 꺼냄 → 그 탄창을 놓아도 다시 배치되지 않는다.
        [Test]
        public void MagazineTakenByReload_CannotBeMovedBackIn()
        {
            EquipVest();
            var spare = Magazine.Full(mag30);
            Assert.IsTrue(inventory.TryAutoStore(spare));
            Assert.AreSame(spare, inventory.TakeFullest(AmmoType.Rifle556));

            var vestRef = new GridRef(GridKind.TacticalVest);
            Assert.IsFalse(inventory.CanPlace(spare, vestRef, new Vector2Int(3, 0), false));
            Assert.IsFalse(inventory.TryMove(spare, vestRef, new Vector2Int(3, 0), false));
            Assert.IsFalse(inventory.IsInStorage(spare), "장전된 탄창이 조끼에 다시 생기면 안 된다");
            Assert.IsFalse(inventory.TryGetLocation(spare, out _));
            Assert.IsFalse(inventory.TryDrop(spare), "인벤토리 밖 아이템은 버리기도 거부");
            Assert.IsFalse(inventory.TryQuickMove(spare));
        }

        // 시나리오 B: 사용 완료로 소진·제거된 의약품을 놓아도 부활하지 않는다.
        [Test]
        public void ConsumedMedical_CannotBeMovedBackIn()
        {
            var bandage = new MedicalItem(TestData.Bandage());   // uses 1
            inventory.TryAutoStore(bandage);
            inventory.SetItemLocked(bandage, true);
            inventory.SetItemLocked(bandage, false);
            inventory.ConsumeMedicalUse(bandage);
            Assert.AreEqual(0, bandage.UsesLeft);
            Assert.IsFalse(inventory.IsInStorage(bandage));

            var pocket = new GridRef(GridKind.Pocket, 2);
            Assert.IsFalse(inventory.TryMove(bandage, pocket, Vector2Int.zero, false));
            Assert.IsFalse(inventory.IsInStorage(bandage));
            Assert.IsFalse(inventory.TryEquip(bandage, EquipSlot.Helmet));
            Assert.IsFalse(inventory.TryDrop(bandage));
        }

        [Test]
        public void ItemNotInInventory_EquipRefused_EquipNewAccepted()
        {
            var helmet = new ItemInstance(TestData.Helmet());
            Assert.IsFalse(inventory.CanEquip(helmet, EquipSlot.Helmet), "밖의 아이템은 TryEquip 대상 아님");
            Assert.IsFalse(inventory.TryEquip(helmet, EquipSlot.Helmet));
            Assert.IsTrue(inventory.TryEquipNew(helmet, EquipSlot.Helmet));
            Assert.IsFalse(inventory.TryEquipNew(helmet, EquipSlot.Helmet), "이미 인벤토리에 있으면 TryEquipNew 거부");
        }

        [Test]
        public void LockedMedical_EquipAndAutoStoreRefused()
        {
            EquipBackpack();
            var bandage = new MedicalItem(TestData.Bandage());
            inventory.TryAutoStore(bandage);
            inventory.SetItemLocked(bandage, true);
            Assert.IsTrue(inventory.IsLocked(bandage));
            Assert.IsFalse(inventory.TryQuickMove(bandage));
            Assert.IsFalse(inventory.CanPlace(bandage, new GridRef(GridKind.Backpack), new Vector2Int(3, 4), false));
        }

        [Test]
        public void TryUse_DepletedMedical_Refused()
        {
            var bandage = new MedicalItem(TestData.Bandage());   // uses 1
            inventory.TryAutoStore(bandage);
            bandage.ConsumeUse();   // 인벤토리 밖에서 0이 된 경우 (방어)
            bool requested = false;
            inventory.UseRequested += _ => requested = true;
            Assert.IsFalse(inventory.TryUse(bandage));
            Assert.IsFalse(requested);
        }

        // 사망 후 재장전 완료가 인벤토리를 건드리지 않는다.
        [Test]
        public void DeadLocked_MagazineSourceGivesAndTakesNothing()
        {
            EquipVest();
            var spare = Magazine.Full(mag30);
            inventory.TryAutoStore(spare);
            inventory.IsDeadLocked = true;

            Assert.IsFalse(inventory.HasLoadable(AmmoType.Rifle556));
            Assert.IsNull(inventory.TakeFullest(AmmoType.Rifle556));
            Assert.IsTrue(inventory.IsInStorage(spare));
            Assert.IsFalse(inventory.TryStore(new Magazine(mag30, 3)));
        }

        [Test]
        public void DeadLocked_ReloadInProgress_DoesNotSwapMagazines()
        {
            var (model, loaded, spare) = RifleWithVestSpare();
            Assert.IsTrue(model.PressReload());
            inventory.IsDeadLocked = true;
            model.Tick(3f);

            Assert.AreSame(loaded, model.Current, "탄창 그대로");
            Assert.IsTrue(inventory.IsInStorage(spare), "예비 탄창은 조끼에 그대로");
        }

        [Test]
        public void TotalWeight_DoesNotAllocate()
        {
            EquipVest();
            EquipBackpack();
            inventory.TryAutoStore(Magazine.Full(mag30));
            inventory.TryAutoStore(new ItemInstance(TestData.Wires()));
            float sum = inventory.TotalWeightKg;   // JIT 워밍업
            Assert.That(() =>
            {
                for (int i = 0; i < 100; i++)
                    sum += inventory.TotalWeightKg;
            }, UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(Is.Not));
            Assert.Greater(sum, 0f);
        }

        [Test]
        public void TotalWeight_ReflectsMagazineSwappedOutsideInventory()
        {
            mag30.weightKg = 0.5f;
            var rifleDef = TestData.Rifle();
            rifleDef.weightKg = 3f;
            var rifle = new WeaponItem(rifleDef, Magazine.Full(mag30));
            inventory.TryEquipNew(rifle, EquipSlot.Primary1);
            Assert.AreEqual(3.5f, inventory.TotalWeightKg, 1e-4f);
            rifle.LoadedMagazine = null;   // 인벤토리 이벤트 없이 무게가 바뀐다 (빠른 재장전으로 버림 등)
            Assert.AreEqual(3f, inventory.TotalWeightKg, 1e-4f);
        }
    }
}
