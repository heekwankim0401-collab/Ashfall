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
    // - 검색 전 루트 아이템, 사용 중 의약품은 옮길 수 없다. 사망 후 모든 명령 거부.
    // - 이동·장착·버리기 명령은 지금 이 인벤토리(장착 슬롯·저장 공간·열린 루트)에 있는 아이템만 받는다.
    //   UI 드래그 중 다른 경로(재장전, 의약품 소진)로 빠진 아이템을 다시 넣어 복제·부활시키지 않게 한다 (리뷰 수정).
    //   밖에서 새로 들어오는 아이템은 TryAutoStore(줍기·회수)와 TryEquipNew(시작 장비)로만 받는다.
    // - 자동 배치 순서: 조끼 → 주머니 → 배낭 (TODO(GDD-?)).
    public sealed class PlayerInventory : IInventoryService, IMagazineSource
    {
        // §12.1 의약품 퀵슬롯 키 4~7 (입력 액션 QuickSlot1~4와 같은 수)
        public const int QuickSlotCount = 4;

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
            if (!CanTakeFrom(item) || !Detach(item))
                return false;
            RaiseChanged();
            Dropped?.Invoke(item);
            return true;
        }

        // 의약품만, 저장 공간에 있는 것만. item null = 슬롯 비우기.
        public bool TryAssignQuickSlot(int index, ItemInstance item)
        {
            if (IsDeadLocked || index < 0 || index >= QuickSlotCount)
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
            if (IsDeadLocked || !(item is MedicalItem medical) || medical.UsesLeft <= 0
                || !IsInStorage(medical) || locked.Contains(medical))
                return false;
            UseRequested?.Invoke(medical);
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
            if (IsDeadLocked || loot == null || ReferenceEquals(activeLoot, loot))
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
            if (item == null || IsDeadLocked || IsInStorage(item) || locked.Contains(item))
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
            if (item == null || IsDeadLocked || IsInInventory(item) || locked.Contains(item) || !CanEquipInto(item, slot))
                return false;
            PutInSlot(item, slot);
            return true;
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
        public void ConsumeMedicalUse(MedicalItem item)
        {
            if (item == null)
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
            if (IsDeadLocked)
                return false;
            Magazine best = null;
            FindFullestInReloadGrids(ammo, ref best);
            return best != null;
        }

        // 탄이 가장 많은 것. 같으면 먼저 찾은 것 (조끼 → 주머니 순).
        public Magazine TakeFullest(AmmoType ammo)
        {
            if (IsDeadLocked)
                return null;
            Magazine best = null;
            FindFullestInReloadGrids(ammo, ref best);
            if (best == null || !Detach(best))
                return null;
            RaiseChanged();
            return best;
        }

        // 일반 재장전 회수 = 자동 배치 (조끼 → 주머니 → 배낭). 공간 없으면 false.
        public bool TryStore(Magazine magazine) => !IsDeadLocked && TryAutoStore(magazine);

        // ── 내부 ─────────────────────────────────────────────

        // 이 인벤토리 안에 있고 옮길 수 있는 상태인가 (원래 자리 기준)
        private bool CanTakeFrom(ItemInstance item)
        {
            if (item == null || IsDeadLocked || locked.Contains(item))
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
