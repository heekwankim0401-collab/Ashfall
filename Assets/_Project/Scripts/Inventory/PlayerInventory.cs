using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Inventory
{
    // SYS-INV 플레이어 인벤토리 (§6): 장착 슬롯 7개, 저장 공간(주머니 + 조끼 + 배낭), 퀵슬롯, 무게, 루트 세션.
    // IInventoryService(UI 조회·명령)와 IMagazineSource(재장전 탄창)를 구현한다. 버스는 모른다 (브리지가 옮김).
    // 규칙 (설계 §5.3, D5, D7)
    // - 조끼·배낭의 내용물 그리드는 슬롯별로 여기서 가진다. 내용물이 있으면 해제·이동·버리기 불가.
    // - 검색 전 루트 아이템, 사용 중 의약품은 옮길 수 없다. 사망·레이드 종료 후 모든 명령 거부(IsFrozen).
    // - 이동·장착·버리기 명령은 지금 이 인벤토리(장착 슬롯·저장 공간·열린 루트)에 있는 아이템만 받는다.
    //   UI 드래그 중 다른 경로(재장전, 의약품 소진)로 빠진 아이템을 다시 넣어 복제·부활시키지 않게 한다 (리뷰 수정).
    //   밖에서 새로 들어오는 아이템은 TryAutoStore(줍기·회수)와 TryEquipNew(시작 장비)로만 받는다.
    // - 자동 배치 순서: 조끼 → 주머니 → 배낭 (TODO(GDD-?)).
    // - M5: 반입(Loadout)을 위치 그대로 놓는다(TryApplyLoadout, TryPlaceNew). 은신처는 버리기·의약품 사용을 끈다(AllowDrop/AllowUse, Q27).
    public sealed class PlayerInventory : IInventoryService, IMagazineSource
    {
        // §12.1 의약품 퀵슬롯 키 4~7 (입력 액션 QuickSlot1~4와 같은 수). M5: 반입(Loadout)과 같은 수
        public const int QuickSlotCount = Loadout.QuickSlotCount;

        private static readonly int SlotCount = Enum.GetValues(typeof(EquipSlot)).Length;

        private readonly InventoryConfigSO config;
        private readonly ItemGrid[] pockets;
        private readonly ItemInstance[] equipped = new ItemInstance[SlotCount];
        private readonly ItemInstance[] quickSlots = new ItemInstance[QuickSlotCount];
        private readonly HashSet<ItemInstance> locked = new HashSet<ItemInstance>();
        private readonly List<IGridView> storageView = new List<IGridView>();
        private ItemGrid[] storageGrids = Array.Empty<ItemGrid>();   // StorageGrids 순서 캐시 (할당 없는 순회용)
        private ItemGrid vestGrid;
        private ItemGrid backpackGrid;
        private LootContainerModel activeLoot;

        // → InventoryChanged (브리지가 무게 변화도 확인해 CarriedWeightChanged)
        public event Action Changed;
        public event Action<EquipSlot, ItemInstance> EquipmentChanged;
        // 바닥에 버림 → WorldItem 생성
        public event Action<ItemInstance> Dropped;
        // 의약품 사용 요청 → MedicalUseRequested
        public event Action<MedicalItem> UseRequested;
        public event Action<bool> LootSessionChanged;

        public PlayerInventory(InventoryConfigSO config)
        {
            this.config = config ? config : throw new ArgumentNullException(nameof(config));
            pockets = new ItemGrid[Mathf.Max(0, config.pocketCount)];
            for (int i = 0; i < pockets.Length; i++)
                pockets[i] = new ItemGrid(new GridRef(GridKind.Pocket, i), config.pocketSize);
            RebuildStorageView();
        }

        // 사망 후 모든 명령 거부
        public bool IsDeadLocked { get; set; }
        // M4 리뷰 수정(RULE-01): 레이드 종료(RaidEnded) 후 잠금. 사망 잠금과 같이 모든 변경을 거부한다
        // (재장전 탄창 교환·의약품 사용 완료 차감·신호탄 소모 포함). 결과 스냅샷 이후 소지품이 바뀌지 않게 한다.
        public bool IsRaidEndLocked { get; set; }
        // 둘 중 하나라도 걸리면 변경 거부 (조회는 그대로). Remove(테스트·디버그용, 규칙 무시)는 예외.
        public bool IsFrozen => IsDeadLocked || IsRaidEndLocked;
        // M5(Q27): false면 TryDrop·TryUse를 거부한다 (은신처 — 버리기·의약품 사용 없음). 기본 true(레이드).
        public bool AllowDrop { get; set; } = true;
        public bool AllowUse { get; set; } = true;
        public LootContainerModel ActiveLootModel => activeLoot;

        // ── IInventoryService 읽기 ─────────────────────────────

        public IReadOnlyList<IGridView> StorageGrids => storageView;
        public ItemInstance GetEquipped(EquipSlot slot) => equipped[(int)slot];
        public ILootView ActiveLoot => activeLoot;

        public ItemInstance GetQuickSlot(int index) =>
            index >= 0 && index < QuickSlotCount ? quickSlots[index] : null;

        // 장착 + 저장 공간 (무기는 장착 탄창 포함). 루트 컨테이너는 제외.
        // 브리지가 매 프레임 부르므로 할당 없이 계산한다 (배열 캐시 + 인덱스 순회).
        public float TotalWeightKg
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < equipped.Length; i++)
                    if (equipped[i] != null)
                        total += equipped[i].WeightKg;
                for (int i = 0; i < storageGrids.Length; i++)
                    total += storageGrids[i].TotalWeightKg;
                return total;
            }
        }

        public bool TryGetLocation(ItemInstance item, out ItemLocation location)
        {
            if (TryFindEquippedSlot(item, out EquipSlot slot))
            {
                location = ItemLocation.Equipped(slot);
                return true;
            }
            ItemGrid grid = FindContainingGrid(item);
            if (grid != null && grid.TryGetPlacement(item, out PlacedItem placed))
            {
                location = ItemLocation.InGrid(grid.Ref, placed.Cell, placed.Rotated);
                return true;
            }
            location = default;
            return false;
        }

        public bool IsInStorage(ItemInstance item)
        {
            if (item == null)
                return false;
            for (int i = 0; i < storageGrids.Length; i++)
                if (storageGrids[i].Contains(item))
                    return true;
            return false;
        }

        public bool IsLocked(ItemInstance item) => item != null && locked.Contains(item);

        // M4: 장착 슬롯·저장 공간에 같은 정의가 있나 (열린 루트 제외). 할당 없음 (Raid가 매 프레임 부를 수 있다).
        public bool Contains(ItemDefinitionSO definition)
        {
            if (definition == null)
                return false;
            for (int i = 0; i < equipped.Length; i++)
                if (equipped[i] != null && equipped[i].Definition == definition)
                    return true;
            return FindInStorage(definition, skipLocked: false) != null;
        }

        // M4 리뷰 수정: 지금 TryConsume이 성공하나 (같은 범위 — 저장 공간만, 잠긴 것 제외, 사망·종료 잠금이면 false). 할당 없음.
        // EXT-03 대기 판정이 이것을 써서 "대기는 진행되는데 완료 순간 소모 실패 → 처음부터"가 반복되지 않게 한다.
        public bool CanConsume(ItemDefinitionSO definition) =>
            !IsFrozen && definition != null && FindInStorage(definition, skipLocked: true) != null;

        // ── 판정 ─────────────────────────────────────────────

        public bool CanPlace(ItemInstance item, GridRef grid, Vector2Int cell, bool rotated)
        {
            ItemGrid target = FindGrid(grid);
            return target != null && CanTakeFrom(item) && !IsOwnGrid(item, target)
                   && target.CanPlace(item, cell, rotated);
        }

        public bool CanEquip(ItemInstance item, EquipSlot slot) =>
            CanTakeFrom(item) && CanEquipInto(item, slot);

        // ── 명령 ─────────────────────────────────────────────

        public bool TryMove(ItemInstance item, GridRef grid, Vector2Int cell, bool rotated)
        {
            if (!CanPlace(item, grid, cell, rotated))
                return false;
            ItemGrid target = FindGrid(grid);
            if (!Detach(item))
                return false;
            target.TryPlace(item, cell, rotated);
            RaiseChanged();
            return true;
        }

        // 이미 인벤토리 안에 있는 아이템을 슬롯으로. 슬롯이 차 있으면 실패 (교환 없음, TODO(GDD-?)).
        public bool TryEquip(ItemInstance item, EquipSlot slot)
        {
            if (!CanEquip(item, slot) || !Detach(item))
                return false;
            PutInSlot(item, slot);
            return true;
        }

        // 루트 ↔ 저장 공간 자동 배치. 장착 아이템은 대상 아님 (TODO(GDD-?)).
        public bool TryQuickMove(ItemInstance item)
        {
            if (activeLoot == null || !CanTakeFrom(item))
                return false;

            if (activeLoot.Grid.Contains(item))
            {
                if (!TryFindAutoStoreSpot(item, out ItemGrid grid, out Vector2Int cell, out bool rotated))
                    return false;
                return Relocate(item, grid, cell, rotated);
            }

            if (IsInStorage(item) && activeLoot.Grid.TryFindFreeCell(item, out Vector2Int lootCell, out bool lootRotated))
                return Relocate(item, activeLoot.Grid, lootCell, lootRotated);
            return false;
        }

        public bool TryDrop(ItemInstance item)
        {
            if (!AllowDrop || !CanTakeFrom(item) || !Detach(item))
                return false;
            RaiseChanged();
            Dropped?.Invoke(item);
            return true;
        }

        // 의약품만, 저장 공간에 있는 것만. item null = 슬롯 비우기.
        public bool TryAssignQuickSlot(int index, ItemInstance item)
        {
            if (IsFrozen || index < 0 || index >= QuickSlotCount)
                return false;
            if (item == null)
            {
                quickSlots[index] = null;
                RaiseChanged();
                return true;
            }
            if (!(item is MedicalItem) || !IsInStorage(item))
                return false;

            for (int i = 0; i < QuickSlotCount; i++)
                if (ReferenceEquals(quickSlots[i], item))
                    quickSlots[i] = null;
            quickSlots[index] = item;
            RaiseChanged();
            return true;
        }

        // 의약품 사용 요청. 실제 사용(시간·대상)은 Player가 정한다. 저장 공간의 것만 (TODO(GDD-?)).
        // 사용 중(잠김)이거나 남은 횟수가 0이면 거부.
        public bool TryUse(ItemInstance item)
        {
            if (IsFrozen || !AllowUse || !(item is MedicalItem medical) || medical.UsesLeft <= 0
                || !IsInStorage(medical) || locked.Contains(medical))
                return false;
            UseRequested?.Invoke(medical);
            return true;
        }

        // M4: 저장 공간에서 같은 정의 하나를 없앤다 (§8.2 EXT-03 신호탄 소모).
        // 순서는 StorageGrids(주머니 → 조끼 → 배낭)·배치 순의 첫 번째. 장착 슬롯·열린 루트·잠긴 아이템 제외, 사망 후 거부.
        // 성공하면 Changed(→ InventoryChanged), 퀵슬롯 참조는 RaiseChanged가 정리한다.
        public bool TryConsume(ItemDefinitionSO definition)
        {
            if (IsFrozen || definition == null)
                return false;
            ItemInstance item = FindInStorage(definition, skipLocked: true);
            if (item == null || !Detach(item))
                return false;
            RaiseChanged();
            return true;
        }

        public void CloseLoot()
        {
            if (activeLoot == null)
                return;
            activeLoot = null;
            LootSessionChanged?.Invoke(false);
            RaiseChanged();
        }

        // ── IInventoryService 밖 (브리지·월드 오브젝트용) ───────────

        public void OpenLoot(LootContainerModel loot)
        {
            if (IsFrozen || loot == null || ReferenceEquals(activeLoot, loot))
                return;
            if (activeLoot != null)
                CloseLoot();
            activeLoot = loot;
            LootSessionChanged?.Invoke(true);
            RaiseChanged();
        }

        // 줍기·회수: 조끼 → 주머니 → 배낭 순 (TODO(GDD-?)). 이미 저장 공간에 있으면 false.
        // 밖에서 들어오는 아이템도 받는다. 인벤토리 안(장착·루트)의 아이템이면 옮길 수 있는 상태여야 한다.
        public bool TryAutoStore(ItemInstance item)
        {
            if (item == null || IsFrozen || IsInStorage(item) || locked.Contains(item))
                return false;
            if (IsInInventory(item) && !CanTakeFrom(item))
                return false;
            if (!TryFindAutoStoreSpot(item, out ItemGrid grid, out Vector2Int cell, out bool rotated))
                return false;
            if (IsInInventory(item) && !Detach(item))
                return false;
            if (!grid.TryPlace(item, cell, rotated))
                return false;
            RaiseChanged();
            return true;
        }

        // 밖에서 새로 들어온 아이템을 바로 장착 (시작 장비 등). 이미 이 인벤토리에 있는 아이템은 TryEquip.
        public bool TryEquipNew(ItemInstance item, EquipSlot slot)
        {
            if (item == null || IsFrozen || IsInInventory(item) || locked.Contains(item) || !CanEquipInto(item, slot))
                return false;
            PutInSlot(item, slot);
            return true;
        }

        // M5: 밖에서 새로 들어온 아이템을 저장 공간의 정확한 자리에 놓는다 (반입 장비 복원).
        // 열린 루트(Loot) 자리는 받지 않는다. 이미 이 인벤토리에 있는 아이템·잠긴 아이템·사망/종료 잠금이면 거부.
        public bool TryPlaceNew(ItemInstance item, GridRef grid, Vector2Int cell, bool rotated)
        {
            if (item == null || IsFrozen || grid.Kind == GridKind.Loot || IsInInventory(item) || locked.Contains(item))
                return false;
            ItemGrid target = FindGrid(grid);
            if (target == null || !target.TryPlace(item, cell, rotated))
                return false;
            RaiseChanged();
            return true;
        }

        // M5(RULE-01, 설계 §1.3): 반입을 위치 그대로 놓는다. 놓은 아이템 수를 돌려준다.
        // 순서: ① 장착 항목(TryEquipNew — 조끼·배낭 그리드가 먼저 생긴다) ② 그리드 항목(TryPlaceNew, 정확한 자리)
        //       ③ ①②에서 실패한 것을 Entries 순으로 자동 배치(TryAutoStore — 정확한 자리를 다 채운 뒤라 남의 자리를 뺏지 않는다)
        //       ④ 그래도 안 되면 rejected에 더한다(null이면 버려진다 — 호출자 몫) ⑤ 퀵슬롯은 저장 공간에 놓인 의약품만.
        // 이미 이 인벤토리에 있는 인스턴스는 건너뛴다(놓은 수·rejected 모두 아님 — 같은 아이템이 두 곳에 생기지 않게).
        // 잘못된 자리(없는 주머니 번호, 범위 밖·겹치는 칸, 장착 규칙 위반, 장착 안 된 조끼·배낭 그리드)는 ③으로 간다.
        // 사망·종료 잠금 중이면 전부 rejected.
        public int TryApplyLoadout(Loadout loadout, List<ItemInstance> rejected)
        {
            if (loadout == null)
                return 0;

            IReadOnlyList<LoadoutEntry> entries = loadout.Entries;
            var failed = new List<ItemInstance>();
            int placed = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                LoadoutEntry entry = entries[i];
                if (!entry.Location.IsEquipped || IsInInventory(entry.Item))
                    continue;
                if (TryEquipNew(entry.Item, entry.Location.Slot)) placed++;
                else failed.Add(entry.Item);
            }
            for (int i = 0; i < entries.Count; i++)
            {
                LoadoutEntry entry = entries[i];
                if (entry.Location.IsEquipped || IsInInventory(entry.Item))
                    continue;
                ItemLocation at = entry.Location;
                if (TryPlaceNew(entry.Item, at.Grid, at.Cell, at.Rotated)) placed++;
                else failed.Add(entry.Item);
            }
            // TODO(GDD-?): 자리를 잃은 반입 아이템의 자동 배치 순서는 줍기와 같게 (조끼 → 주머니 → 배낭).
            for (int i = 0; i < failed.Count; i++)
            {
                if (TryAutoStore(failed[i])) placed++;
                else rejected?.Add(failed[i]);
            }

            IReadOnlyList<ItemInstance> quick = loadout.QuickSlots;
            for (int i = 0; i < QuickSlotCount && i < quick.Count; i++)
                if (quick[i] != null && IsInStorage(quick[i]))
                    TryAssignQuickSlot(i, quick[i]);
            return placed;
        }

        // 어디에 있든 꺼낸다 (잠금·규칙 무시). 없으면 false.
        public bool Remove(ItemInstance item)
        {
            if (!Detach(item))
                return false;
            locked.Remove(item);
            RaiseChanged();
            return true;
        }

        // 의약품 1회 사용 처리. 남은 횟수가 0이면 제거하고 퀵슬롯을 비운다.
        // 사망·레이드 종료 잠금 중에는 차감하지 않는다 (M4 리뷰 수정: 종료 뒤 사용 완료가 결과 스냅샷과 어긋나지 않게).
        public void ConsumeMedicalUse(MedicalItem item)
        {
            if (item == null || IsFrozen)
                return;
            item.ConsumeUse();
            if (item.UsesLeft <= 0)
            {
                locked.Remove(item);
                Detach(item);
            }
            RaiseChanged();
        }

        // 사용 중 의약품 이동·버리기 거부
        public void SetItemLocked(ItemInstance item, bool isLocked)
        {
            if (item == null)
                return;
            if (isLocked) locked.Add(item);
            else locked.Remove(item);
        }

        // ── IMagazineSource (§5.1 재장전, 설계 D7) ─────────────────
        // 순서: 조끼 → 주머니 → (설정 시) 배낭. 사망 후에는 아무것도 내주거나 받지 않는다.

        public bool HasLoadable(AmmoType ammo)
        {
            if (IsFrozen)
                return false;
            Magazine best = null;
            FindFullestInReloadGrids(ammo, ref best);
            return best != null;
        }

        // 탄이 가장 많은 것. 같으면 먼저 찾은 것 (조끼 → 주머니 순).
        public Magazine TakeFullest(AmmoType ammo)
        {
            if (IsFrozen)
                return null;
            Magazine best = null;
            FindFullestInReloadGrids(ammo, ref best);
            if (best == null || !Detach(best))
                return null;
            RaiseChanged();
            return best;
        }

        // 일반 재장전 회수 = 자동 배치 (조끼 → 주머니 → 배낭). 공간 없으면 false.
        public bool TryStore(Magazine magazine) => !IsFrozen && TryAutoStore(magazine);

        // ── 내부 ─────────────────────────────────────────────

        // 이 인벤토리 안에 있고 옮길 수 있는 상태인가 (원래 자리 기준)
        private bool CanTakeFrom(ItemInstance item)
        {
            if (item == null || IsFrozen || locked.Contains(item))
                return false;
            if (TryFindEquippedSlot(item, out EquipSlot slot))
            {
                ItemGrid contents = ContainerGridFor(slot);
                return contents == null || contents.IsEmpty;
            }
            ItemGrid grid = FindContainingGrid(item);
            if (grid == null)
                return false;   // 이미 빠진 아이템 (재장전으로 꺼낸 탄창, 소진된 의약품 등)
            if (activeLoot != null && grid == activeLoot.Grid && !activeLoot.IsRevealed(item))
                return false;
            return true;
        }

        // 슬롯 규칙만 (아이템 위치는 보지 않는다)
        private bool CanEquipInto(ItemInstance item, EquipSlot slot) =>
            EquipRules.Accepts(slot, item.Definition) && equipped[(int)slot] == null;

        private void PutInSlot(ItemInstance item, EquipSlot slot)
        {
            equipped[(int)slot] = item;
            if (EquipRules.TryGetContainerGrid(slot, out GridKind kind) && item.Definition is ContainerDefinitionSO container)
            {
                var grid = new ItemGrid(new GridRef(kind), container.gridSize);
                if (slot == EquipSlot.TacticalVest) vestGrid = grid;
                else backpackGrid = grid;
                RebuildStorageView();
            }
            EquipmentChanged?.Invoke(slot, item);
            RaiseChanged();
        }

        private bool IsLoadable(ItemInstance item, AmmoType ammo) =>
            item is Magazine m && !m.IsEmpty && m.Definition.ammo == ammo && !locked.Contains(m);

        // 할당 없이 순회 (WeaponModel.CanReload가 자주 부를 수 있다)
        private void FindFullestInReloadGrids(AmmoType ammo, ref Magazine best)
        {
            if (vestGrid != null)
                FindFullestIn(vestGrid, ammo, ref best);
            for (int i = 0; i < pockets.Length; i++)
                FindFullestIn(pockets[i], ammo, ref best);
            if (config.reloadFromBackpack && backpackGrid != null)
                FindFullestIn(backpackGrid, ammo, ref best);
        }

        private void FindFullestIn(ItemGrid grid, AmmoType ammo, ref Magazine best)
        {
            IReadOnlyList<PlacedItem> items = grid.Items;
            for (int i = 0; i < items.Count; i++)
            {
                ItemInstance item = items[i].Item;
                if (IsLoadable(item, ammo) && (best == null || ((Magazine)item).Rounds > best.Rounds))
                    best = (Magazine)item;
            }
        }

        // 저장 공간(StorageGrids 순)에서 정의가 같은 첫 아이템. 할당 없이 순회.
        private ItemInstance FindInStorage(ItemDefinitionSO definition, bool skipLocked)
        {
            for (int g = 0; g < storageGrids.Length; g++)
            {
                IReadOnlyList<PlacedItem> items = storageGrids[g].Items;
                for (int i = 0; i < items.Count; i++)
                {
                    ItemInstance item = items[i].Item;
                    if (item.Definition == definition && !(skipLocked && locked.Contains(item)))
                        return item;
                }
            }
            return null;
        }

        private IEnumerable<ItemGrid> AutoStoreOrder()
        {
            if (vestGrid != null) yield return vestGrid;
            foreach (ItemGrid pocket in pockets) yield return pocket;
            if (backpackGrid != null) yield return backpackGrid;
        }

        private bool TryFindAutoStoreSpot(ItemInstance item, out ItemGrid grid, out Vector2Int cell, out bool rotated)
        {
            foreach (ItemGrid candidate in AutoStoreOrder())
            {
                if (IsOwnGrid(item, candidate))
                    continue;
                if (candidate.TryFindFreeCell(item, out cell, out rotated))
                {
                    grid = candidate;
                    return true;
                }
            }
            grid = null;
            cell = default;
            rotated = false;
            return false;
        }

        // 인벤토리 안의 아이템을 옮긴다. 원래 자리에서 빼지 못하면 아무것도 바꾸지 않는다.
        private bool Relocate(ItemInstance item, ItemGrid grid, Vector2Int cell, bool rotated)
        {
            if (!Detach(item))
                return false;
            grid.TryPlace(item, cell, rotated);
            RaiseChanged();
            return true;
        }

        // 그리드·슬롯에서 뺀다. 장착 해제 부수효과(내용물 그리드 제거, EquipmentChanged) 포함.
        private bool Detach(ItemInstance item)
        {
            if (item == null)
                return false;
            if (TryFindEquippedSlot(item, out EquipSlot slot))
            {
                equipped[(int)slot] = null;
                if (slot == EquipSlot.TacticalVest) { vestGrid = null; RebuildStorageView(); }
                else if (slot == EquipSlot.Backpack) { backpackGrid = null; RebuildStorageView(); }
                EquipmentChanged?.Invoke(slot, null);
                return true;
            }
            ItemGrid grid = FindContainingGrid(item);
            return grid != null && grid.Remove(item);
        }

        private bool IsInInventory(ItemInstance item) => TryFindEquippedSlot(item, out _) || FindContainingGrid(item) != null;

        private bool TryFindEquippedSlot(ItemInstance item, out EquipSlot slot)
        {
            if (item != null)
            {
                for (int i = 0; i < SlotCount; i++)
                {
                    if (ReferenceEquals(equipped[i], item))
                    {
                        slot = (EquipSlot)i;
                        return true;
                    }
                }
            }
            slot = default;
            return false;
        }

        private ItemGrid FindContainingGrid(ItemInstance item)
        {
            if (item == null)
                return null;
            for (int i = 0; i < storageGrids.Length; i++)
                if (storageGrids[i].Contains(item))
                    return storageGrids[i];
            if (activeLoot != null && activeLoot.Grid.Contains(item))
                return activeLoot.Grid;
            return null;
        }

        private ItemGrid FindGrid(GridRef grid)
        {
            switch (grid.Kind)
            {
                case GridKind.Pocket:
                    return grid.Index >= 0 && grid.Index < pockets.Length ? pockets[grid.Index] : null;
                case GridKind.TacticalVest:
                    return vestGrid;
                case GridKind.Backpack:
                    return backpackGrid;
                case GridKind.Loot:
                    return activeLoot?.Grid;
                default:
                    return null;
            }
        }

        private ItemGrid ContainerGridFor(EquipSlot slot) =>
            slot == EquipSlot.TacticalVest ? vestGrid : slot == EquipSlot.Backpack ? backpackGrid : null;

        // 장착된 조끼·배낭을 자기 내용물 그리드에 넣을 수는 없다.
        private bool IsOwnGrid(ItemInstance item, ItemGrid grid) =>
            grid != null && ((grid == vestGrid && ReferenceEquals(equipped[(int)EquipSlot.TacticalVest], item))
                             || (grid == backpackGrid && ReferenceEquals(equipped[(int)EquipSlot.Backpack], item)));

        // 주머니 → 조끼 → 배낭 (StorageGrids 표시 순서). 장착이 바뀔 때만 다시 만든다.
        private void RebuildStorageView()
        {
            var grids = new List<ItemGrid>(pockets.Length + 2);
            grids.AddRange(pockets);
            if (vestGrid != null) grids.Add(vestGrid);
            if (backpackGrid != null) grids.Add(backpackGrid);
            storageGrids = grids.ToArray();

            storageView.Clear();
            foreach (ItemGrid grid in storageGrids)
                storageView.Add(grid);
        }

        // 저장 공간을 떠난 의약품은 퀵슬롯에서 뺀다.
        private void RaiseChanged()
        {
            for (int i = 0; i < QuickSlotCount; i++)
                if (quickSlots[i] != null && !IsInStorage(quickSlots[i]))
                    quickSlots[i] = null;
            Changed?.Invoke();
        }
    }
}
