using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // UI 프레젠터 테스트용 가짜 서비스 (설계 §6 2b). 배치 규칙은 흉내 내지 않고 호출만 기록한다.

    internal class FakeGridView : IGridView
    {
        public readonly List<PlacedItem> Placed = new List<PlacedItem>();

        public FakeGridView(GridRef gridRef, Vector2Int size)
        {
            Ref = gridRef;
            Size = size;
        }

        public GridRef Ref { get; }
        public Vector2Int Size { get; }
        public IReadOnlyList<PlacedItem> Items => Placed;
    }

    internal sealed class FakeLootView : FakeGridView, ILootView
    {
        public readonly HashSet<ItemInstance> Revealed = new HashSet<ItemInstance>();

        public FakeLootView(Vector2Int size, string displayName = "상자")
            : base(new GridRef(GridKind.Loot), size)
        {
            DisplayName = displayName;
        }

        public string DisplayName { get; }
        public bool IsRevealed(ItemInstance item) => Revealed.Contains(item);
        public bool SearchComplete => Placed.TrueForAll(p => Revealed.Contains(p.Item));
    }

    internal sealed class FakeInventoryService : IInventoryService
    {
        public readonly List<IGridView> Grids = new List<IGridView>();
        public readonly Dictionary<EquipSlot, ItemInstance> Equipped = new Dictionary<EquipSlot, ItemInstance>();
        public readonly Dictionary<ItemInstance, ItemLocation> Locations = new Dictionary<ItemInstance, ItemLocation>();
        public readonly ItemInstance[] QuickSlots = new ItemInstance[4];
        public readonly HashSet<ItemInstance> Locked = new HashSet<ItemInstance>();
        public FakeLootView Loot;

        // 결과 조절
        public bool CanPlaceResult = true;
        public bool CanEquipResult = true;
        public bool MoveResult = true;
        public bool EquipResult = true;
        public bool DropResult = true;
        public bool AssignResult = true;

        // 호출 기록
        public readonly List<string> Calls = new List<string>();
        public (ItemInstance Item, GridRef Grid, Vector2Int Cell, bool Rotated)? LastCanPlace;
        public (ItemInstance Item, GridRef Grid, Vector2Int Cell, bool Rotated)? LastMove;
        public (ItemInstance Item, EquipSlot Slot)? LastEquip;
        public ItemInstance LastDrop;
        public (int Index, ItemInstance Item)? LastAssign;
        public int CloseLootCalls;

        public IReadOnlyList<IGridView> StorageGrids => Grids;
        public ItemInstance GetEquipped(EquipSlot slot) => Equipped.TryGetValue(slot, out var item) ? item : null;
        public ILootView ActiveLoot => Loot;
        public ItemInstance GetQuickSlot(int index) => index >= 0 && index < QuickSlots.Length ? QuickSlots[index] : null;
        public float TotalWeightKg => 0f;

        public bool TryGetLocation(ItemInstance item, out ItemLocation location) =>
            Locations.TryGetValue(item, out location);

        public bool IsLocked(ItemInstance item) => item != null && Locked.Contains(item);

        public bool CanPlace(ItemInstance item, GridRef grid, Vector2Int cell, bool rotated)
        {
            LastCanPlace = (item, grid, cell, rotated);
            return CanPlaceResult;
        }

        public bool CanEquip(ItemInstance item, EquipSlot slot) => CanEquipResult;

        public bool TryMove(ItemInstance item, GridRef grid, Vector2Int cell, bool rotated)
        {
            Calls.Add(nameof(TryMove));
            LastMove = (item, grid, cell, rotated);
            if (MoveResult)
                Locations[item] = ItemLocation.InGrid(grid, cell, rotated);
            return MoveResult;
        }

        public bool TryEquip(ItemInstance item, EquipSlot slot)
        {
            Calls.Add(nameof(TryEquip));
            LastEquip = (item, slot);
            if (EquipResult)
                Locations[item] = ItemLocation.Equipped(slot);
            return EquipResult;
        }

        public bool TryQuickMove(ItemInstance item)
        {
            Calls.Add(nameof(TryQuickMove));
            return true;
        }

        public bool TryDrop(ItemInstance item)
        {
            Calls.Add(nameof(TryDrop));
            LastDrop = item;
            if (DropResult)
                Locations.Remove(item);
            return DropResult;
        }

        public bool TryAssignQuickSlot(int index, ItemInstance item)
        {
            Calls.Add(nameof(TryAssignQuickSlot));
            LastAssign = (index, item);
            return AssignResult;
        }

        public bool TryUse(ItemInstance item)
        {
            Calls.Add(nameof(TryUse));
            return true;
        }

        // M4: 장착·그리드(루트 제외)에서 정의를 찾는다.
        public bool Contains(ItemDefinitionSO definition)
        {
            if (definition == null)
                return false;
            foreach (ItemInstance item in Equipped.Values)
                if (item != null && item.Definition == definition)
                    return true;
            foreach (IGridView grid in Grids)
                foreach (PlacedItem placed in grid.Items)
                    if (placed.Item.Definition == definition)
                        return true;
            return false;
        }

        // M4 리뷰 수정: TryConsume과 같은 범위 — 그리드(장착 제외)에서 잠기지 않은 같은 정의가 있나.
        // 가짜 TryConsume은 FakeGridView만 지울 수 있으므로, 다른 IGridView에 둔 아이템은 "판정은 되는데 소모 실패" 경로를 만든다.
        public bool CanConsume(ItemDefinitionSO definition)
        {
            if (definition == null)
                return false;
            foreach (IGridView grid in Grids)
                foreach (PlacedItem placed in grid.Items)
                    if (placed.Item.Definition == definition && !Locked.Contains(placed.Item))
                        return true;
            return false;
        }

        // M4: 가짜 그리드(FakeGridView)에서 첫 번째 같은 정의를 뺀다. 없으면 false.
        public bool TryConsume(ItemDefinitionSO definition)
        {
            Calls.Add(nameof(TryConsume));
            if (definition == null)
                return false;
            foreach (IGridView grid in Grids)
            {
                if (!(grid is FakeGridView fake))
                    continue;
                int index = fake.Placed.FindIndex(p => p.Item.Definition == definition && !Locked.Contains(p.Item));
                if (index < 0)
                    continue;
                Locations.Remove(fake.Placed[index].Item);
                fake.Placed.RemoveAt(index);
                return true;
            }
            return false;
        }

        public void CloseLoot()
        {
            Calls.Add(nameof(CloseLoot));
            CloseLootCalls++;
            Loot = null;
        }
    }

    internal sealed class FakeBodyStatusView : IBodyStatusView
    {
        public readonly Dictionary<BodyPart, HealthTier> Tiers = new Dictionary<BodyPart, HealthTier>();
        public readonly HashSet<(BodyPart, StatusEffect)> Statuses = new HashSet<(BodyPart, StatusEffect)>();

        public bool IsDead { get; set; }
        public HealthTier GetTier(BodyPart part) => Tiers.TryGetValue(part, out var tier) ? tier : HealthTier.Healthy;
        public bool HasStatus(BodyPart part, StatusEffect effect) => Statuses.Contains((part, effect));

        // M7 7C-1: 전신 생존 등급 (없으면 Normal)
        public readonly Dictionary<SurvivalStat, SurvivalTier> SurvivalTiers = new Dictionary<SurvivalStat, SurvivalTier>();
        public SurvivalTier GetSurvivalTier(SurvivalStat stat) =>
            SurvivalTiers.TryGetValue(stat, out var tier) ? tier : SurvivalTier.Normal;
    }
}
