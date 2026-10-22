using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M5 AC1 — 은신처 인벤토리 (M5 설계 §3.1, §4.3, Q27·Q28·Q29).
    // 보관함(PlayerProfile.Stash)을 열린 루트 자리로 열고 기존 인벤토리 규칙으로 장비 ↔ 보관함을 옮긴다.
    // HideoutInventoryBehaviour.CreateInventory(순수 준비)를 MonoBehaviour 없이 쓴다.
    public class HideoutInventoryTests
    {
        private FakeCatalog catalog;
        private InventoryConfigSO config;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
            config = TestData.Inventory();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        private static PlayerProfile NewProfile() => new PlayerProfile(M5TestData.StashSize, M5TestData.StartingCasings);

        private PlayerInventory Create(PlayerProfile profile, out LootContainerModel stash, List<ItemInstance> dropped = null) =>
            HideoutInventoryBehaviour.CreateInventory(config, profile, "보관함", out stash, dropped);

        // ── LootContainerModel.ForExistingGrid ───────────────────

        [Test]
        public void ForExistingGrid_WrapsSameGrid_AllRevealed_NoSearch()
        {
            var grid = new ItemGrid(new GridRef(GridKind.Loot), new Vector2Int(10, 30));
            var wires = M5TestData.Plain(catalog, "JNK-03");
            Assert.IsTrue(grid.TryPlace(wires, new Vector2Int(3, 4), false));

            LootContainerModel model = LootContainerModel.ForExistingGrid("보관함", grid);

            Assert.AreSame(grid, model.Grid, "복사하지 않는다");
            Assert.AreEqual("보관함", model.DisplayName);
            Assert.AreEqual(new GridRef(GridKind.Loot), model.Ref);
            Assert.AreEqual(new Vector2Int(10, 30), model.Size);
            Assert.IsTrue(model.SearchComplete);
            Assert.IsTrue(model.IsRevealed(wires));
            Assert.IsFalse(model.Tick(1f), "검색 연출 없음");
            Assert.IsTrue(model.Items[0].Cell == new Vector2Int(3, 4), "자리 그대로");

            var later = M5TestData.Plain(catalog, "JNK-01");
            Assert.IsTrue(grid.TryPlace(later, Vector2Int.zero, false));
            Assert.AreEqual(2, model.Items.Count, "그리드 주인(상인·키트)이 바꾼 것이 바로 보인다");
            Assert.IsTrue(model.IsRevealed(later));
        }

        [Test]
        public void ForExistingGrid_RejectsNullAndNonLootGrid()
        {
            Assert.Throws<System.ArgumentNullException>(() => LootContainerModel.ForExistingGrid("x", null));
            Assert.Throws<System.ArgumentException>(() =>
                LootContainerModel.ForExistingGrid("x", new ItemGrid(new GridRef(GridKind.Pocket), Vector2Int.one)));
        }

        // ── 준비 ─────────────────────────────────────────────

        [Test]
        public void Create_AppliesLoadout_OpensStashAsLoot_DisablesDropAndUse()
        {
            PlayerProfile profile = NewProfile();
            profile.Loadout = M5TestData.SampleLoadout(catalog, out Dictionary<string, ItemInstance> items);
            var dropped = new List<ItemInstance>();

            PlayerInventory inv = Create(profile, out LootContainerModel stash, dropped);

            Assert.AreSame(items["rifle"], inv.GetEquipped(EquipSlot.Primary1));
            Assert.AreSame(items["vest"], inv.GetEquipped(EquipSlot.TacticalVest));
            Assert.AreSame(items["bandage"], inv.GetQuickSlot(0));
            Assert.AreSame(stash, inv.ActiveLoot);
            Assert.AreSame(profile.Stash, inv.ActiveLootModel.Grid, "보관함 그리드 그대로");
            Assert.AreEqual(new Vector2Int(10, 30), inv.ActiveLoot.Size);
            Assert.AreEqual("보관함", inv.ActiveLoot.DisplayName);
            Assert.IsFalse(inv.AllowDrop);
            Assert.IsFalse(inv.AllowUse);
            Assert.IsEmpty(dropped);
        }

        [Test]
        public void Create_EmptyProfile_EmptyEquipment_StashOpen()
        {
            PlayerProfile profile = NewProfile();
            PlayerInventory inv = Create(profile, out LootContainerModel stash);
            Assert.IsTrue(Loadout.Capture(inv).IsEmpty);
            Assert.IsNotNull(inv.ActiveLoot);
            Assert.IsTrue(profile.Loadout.IsEmpty);
        }

        [Test]
        public void Create_RejectedLoadoutItems_GoToStash_AndLeaveProfileLoadout()
        {
            PlayerProfile profile = NewProfile();
            var engine = M5TestData.Plain(catalog, "JNK-02");   // 3×2 — 조끼·배낭이 없으면 들어갈 곳이 없다
            var wires = M5TestData.Plain(catalog, "JNK-03");
            profile.Loadout = new Loadout(new[]
            {
                new LoadoutEntry(engine, ItemLocation.InGrid(new GridRef(GridKind.Backpack), Vector2Int.zero, false)),
                new LoadoutEntry(wires, ItemLocation.InGrid(new GridRef(GridKind.Pocket, 0), Vector2Int.zero, false)),
            }, null);
            var dropped = new List<ItemInstance>();

            PlayerInventory inv = Create(profile, out _, dropped);

            Assert.IsTrue(profile.Stash.Contains(engine), "거부 → 보관함 자동 배치");
            Assert.IsTrue(profile.Stash.TryGetPlacement(engine, out PlacedItem placed));
            Assert.AreEqual(Vector2Int.zero, placed.Cell, "위→아래·왼→오른쪽 첫 칸");
            Assert.IsTrue(inv.IsInStorage(wires));
            Assert.IsEmpty(dropped);
            Assert.IsFalse(profile.Loadout.Entries.Any(e => e.Item == engine), "보관함으로 간 것은 은신처 장비에서 빠진다 (두 번 저장되지 않게)");
            Assert.IsTrue(profile.Loadout.Entries.Any(e => e.Item == wires));
        }

        [Test]
        public void Create_StashFull_Dropped()
        {
            var profile = new PlayerProfile(new Vector2Int(2, 2), 0);
            var engine = M5TestData.Plain(catalog, "JNK-02");
            profile.Loadout = new Loadout(new[]
            {
                new LoadoutEntry(engine, ItemLocation.InGrid(new GridRef(GridKind.Backpack), Vector2Int.zero, false)),
            }, null);
            var dropped = new List<ItemInstance>();

            PlayerInventory inv = Create(profile, out _, dropped);

            CollectionAssert.AreEqual(new[] { engine }, dropped);
            Assert.IsFalse(profile.Stash.Contains(engine));
            Assert.IsFalse(inv.TryGetLocation(engine, out _));
            Assert.IsTrue(profile.Loadout.IsEmpty);
        }

        [Test]
        public void Create_ProfileLoadout_EqualsActualEquipment()
        {
            PlayerProfile profile = NewProfile();
            PlayerInventory source = M5TestData.EquippedInventory(catalog, out _);
            profile.Loadout = Loadout.Capture(source);
            int before = profile.Loadout.Entries.Count;

            PlayerInventory inv = Create(profile, out _);

            Assert.AreEqual(before, profile.Loadout.Entries.Count);
            Loadout actual = Loadout.Capture(inv);
            for (int i = 0; i < actual.Entries.Count; i++)
                Assert.AreSame(actual.Entries[i].Item, profile.Loadout.Entries[i].Item);
        }

        // ── 은신처 규칙 (Q27) ───────────────────────────────────

        [Test]
        public void DropAndUse_Refused_InHideout()
        {
            PlayerProfile profile = NewProfile();
            profile.Loadout = M5TestData.SampleLoadout(catalog, out Dictionary<string, ItemInstance> items);
            PlayerInventory inv = Create(profile, out _);
            int dropped = 0, used = 0;
            inv.Dropped += _ => dropped++;
            inv.UseRequested += _ => used++;

            Assert.IsFalse(inv.TryDrop(items["mag"]), "버리기 없음");
            Assert.IsFalse(inv.TryDrop(items["rifle"]));
            Assert.IsFalse(inv.TryUse(items["bandage"]), "의약품 사용 없음");
            Assert.AreEqual(0, dropped);
            Assert.AreEqual(0, used);
            Assert.IsTrue(inv.IsInStorage(items["mag"]), "제자리");
        }

        [Test]
        public void StashItem_DropRefused()
        {
            PlayerProfile profile = NewProfile();
            var wires = M5TestData.Plain(catalog, "JNK-03");
            Assert.IsTrue(profile.Stash.TryPlace(wires, Vector2Int.zero, false));
            PlayerInventory inv = Create(profile, out _);
            Assert.IsFalse(inv.TryDrop(wires));
            Assert.IsTrue(profile.Stash.Contains(wires));
        }

        // ── 장비 ↔ 보관함 (기존 인벤토리 규칙 그대로) ───────────────

        [Test]
        public void MoveBetweenEquipmentAndStash()
        {
            PlayerProfile profile = NewProfile();
            profile.Loadout = M5TestData.SampleLoadout(catalog, out Dictionary<string, ItemInstance> items);
            PlayerInventory inv = Create(profile, out _);
            var stashRef = new GridRef(GridKind.Loot);

            // 조끼 안 탄창 → 보관함 (5,10)
            Assert.IsTrue(inv.CanPlace(items["mag"], stashRef, new Vector2Int(5, 10), false));
            Assert.IsTrue(inv.TryMove(items["mag"], stashRef, new Vector2Int(5, 10), false));
            Assert.IsTrue(profile.Stash.Contains(items["mag"]));

            // 장착 소총 → 보관함 (회전)
            Assert.IsTrue(inv.TryMove(items["rifle"], stashRef, new Vector2Int(0, 0), true));
            Assert.IsNull(inv.GetEquipped(EquipSlot.Primary1));
            Assert.IsTrue(profile.Stash.TryGetPlacement(items["rifle"], out PlacedItem rifleAt));
            Assert.IsTrue(rifleAt.Rotated);

            // 보관함 → 장착
            Assert.IsTrue(inv.TryEquip(items["rifle"], EquipSlot.Primary1));
            Assert.IsFalse(profile.Stash.Contains(items["rifle"]));

            // 보관함 → 조끼 그리드
            Assert.IsTrue(inv.TryMove(items["mag"], new GridRef(GridKind.TacticalVest), new Vector2Int(3, 0), false));
            Assert.IsFalse(profile.Stash.Contains(items["mag"]));
        }

        [Test]
        public void EquipFromStash_ContainerCreatesGrid_AndCaptureSeesIt()
        {
            PlayerProfile profile = NewProfile();
            var backpack = M5TestData.Plain(catalog, "BAG-01");
            var wires = M5TestData.Plain(catalog, "JNK-03");
            Assert.IsTrue(profile.Stash.TryPlace(backpack, Vector2Int.zero, false));
            Assert.IsTrue(profile.Stash.TryPlace(wires, new Vector2Int(5, 0), false));
            PlayerInventory inv = Create(profile, out _);

            Assert.IsTrue(inv.TryEquip(backpack, EquipSlot.Backpack));
            Assert.IsTrue(inv.TryMove(wires, new GridRef(GridKind.Backpack), new Vector2Int(1, 1), false));

            Loadout captured = Loadout.Capture(inv);
            Assert.AreEqual(2, captured.Entries.Count);
            Assert.IsFalse(captured.Entries.Any(e => !e.Location.IsEquipped && e.Location.Grid.Kind == GridKind.Loot),
                "보관함은 장비가 아니다");
        }

        [Test]
        public void QuickMove_BothDirections()
        {
            PlayerProfile profile = NewProfile();
            var bandage = M5TestData.Bandage(catalog);
            Assert.IsTrue(profile.Stash.TryPlace(bandage, new Vector2Int(9, 29), false));
            PlayerInventory inv = Create(profile, out _);

            Assert.IsTrue(inv.TryQuickMove(bandage), "보관함 → 저장 공간 (Ctrl+클릭)");
            Assert.IsTrue(inv.IsInStorage(bandage));
            Assert.IsTrue(inv.TryQuickMove(bandage), "저장 공간 → 보관함");
            Assert.IsTrue(profile.Stash.TryGetPlacement(bandage, out PlacedItem at));
            Assert.AreEqual(Vector2Int.zero, at.Cell, "보관함 첫 빈 칸");
        }

        [Test]
        public void QuickSlot_AssignableInHideout_ClearedWhenMovedToStash()
        {
            PlayerProfile profile = NewProfile();
            profile.Loadout = M5TestData.SampleLoadout(catalog, out Dictionary<string, ItemInstance> items);
            PlayerInventory inv = Create(profile, out _);

            Assert.IsTrue(inv.TryAssignQuickSlot(3, items["bandage"]));
            Assert.AreSame(items["bandage"], inv.GetQuickSlot(3));
            Assert.IsTrue(inv.TryQuickMove(items["bandage"]));
            Assert.IsNull(inv.GetQuickSlot(3), "보관함으로 옮기면 퀵슬롯에서 빠진다");
            Assert.IsFalse(inv.TryAssignQuickSlot(0, items["bandage"]), "보관함 아이템은 배정 불가");
        }

        [Test]
        public void CloseLoot_ThenReopen_SameStash()
        {
            // 브리지는 LootSessionChanged(false)에서 다시 연다 — 모델 경로가 같은 보관함을 다시 받는지만 확인
            PlayerProfile profile = NewProfile();
            PlayerInventory inv = Create(profile, out LootContainerModel stash);
            inv.CloseLoot();
            Assert.IsNull(inv.ActiveLoot);
            inv.OpenLoot(stash);
            Assert.AreSame(profile.Stash, inv.ActiveLootModel.Grid);
        }

        // M5 리뷰 5: 은신처 인벤토리는 "프로필 로드됨"이 아니라 "이 씬의 진입 절차 완료"를 확인한다.
        // 두 번째 진입에서 순서가 어긋나면 프로필은 이미 로드돼 있지만 결과 반영 전이다 → 아직 아님.
        [Test]
        public void IsEntryComplete_RequiresTokenOfThisScene()
        {
            var profiles = new ProfileService(new MemorySaveStorage(), new FakeClock().Now);
            Assert.IsFalse(HideoutInventoryBehaviour.IsEntryComplete(null, 1));
            Assert.IsFalse(HideoutInventoryBehaviour.IsEntryComplete(profiles, 1), "미로드");

            profiles.EnsureLoaded(catalog, M5TestData.Defaults());
            Assert.IsFalse(HideoutInventoryBehaviour.IsEntryComplete(profiles, 1), "로드됐지만 진입 절차 전");

            profiles.MarkHideoutEntered(1);
            Assert.IsTrue(HideoutInventoryBehaviour.IsEntryComplete(profiles, 1));
            Assert.IsFalse(HideoutInventoryBehaviour.IsEntryComplete(profiles, 2), "이전 은신처 씬의 표시");

            Assert.IsTrue(profiles.BeginRaid(Loadout.Empty));
            Assert.IsFalse(HideoutInventoryBehaviour.IsEntryComplete(profiles, 1), "레이드 시작으로 지워짐");
        }
    }
}
