using System;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.UI
{
    public enum DropTargetKind
    {
        Outside,   // 인벤토리 패널 밖 → 바닥에 버리기
        Grid,      // 그리드 칸
        Slot,      // 장착 슬롯
        Panel,     // 패널 안 빈 곳 → 취소(원위치)
    }

    // 포인터를 놓은 곳. 뷰가 포인터 아래 요소에서 만든다.
    public readonly struct DropTarget
    {
        public readonly DropTargetKind Kind;
        public readonly GridRef Grid;
        public readonly Vector2Int Cell;
        public readonly EquipSlot Slot;

        private DropTarget(DropTargetKind kind, GridRef grid, Vector2Int cell, EquipSlot slot)
        {
            Kind = kind;
            Grid = grid;
            Cell = cell;
            Slot = slot;
        }

        public static DropTarget Outside() => new DropTarget(DropTargetKind.Outside, default, default, default);
        public static DropTarget Panel() => new DropTarget(DropTargetKind.Panel, default, default, default);
        public static DropTarget OnGrid(GridRef grid, Vector2Int cell) => new DropTarget(DropTargetKind.Grid, grid, cell, default);
        public static DropTarget OnSlot(EquipSlot slot) => new DropTarget(DropTargetKind.Slot, default, default, slot);
    }

    // SYS-INV 드래그 상태. 배치 규칙은 IInventoryService에만 있고 여기서는 판정·명령을 호출만 한다(설계 D2).
    // 회전(R)은 드래그 중에만 이 모델이 들고 있다가 TryMove(..., rotated)로 넘긴다(설계 D4).
    // 놓기에 실패하면 서비스가 아무것도 바꾸지 않으므로 아이템은 원래 자리에 남는다.
    public sealed class InventoryDragModel
    {
        private readonly IInventoryService inventory;

        public InventoryDragModel(IInventoryService inventory)
        {
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public ItemInstance Dragging { get; private set; }
        public bool Rotated { get; private set; }
        public bool IsDragging => Dragging != null;

        // 현재 회전을 반영한 칸 크기. 드래그 중이 아니면 0.
        public Vector2Int Size => Dragging != null ? Dragging.SizeFor(Rotated) : Vector2Int.zero;

        // 현재 배치의 회전으로 시작한다. 검색 전 루트 아이템은 집을 수 없다.
        public bool Begin(ItemInstance item)
        {
            Cancel();
            if (item == null || !CanPick(inventory, item))
                return false;

            Dragging = item;
            Rotated = inventory.TryGetLocation(item, out ItemLocation location)
                      && !location.IsEquipped && location.Rotated;
            return true;
        }

        // R
        public void ToggleRotate()
        {
            if (Dragging != null)
                Rotated = !Rotated;
        }

        public bool CanDropAt(GridRef grid, Vector2Int cell) =>
            Dragging != null && inventory.CanPlace(Dragging, grid, cell, Rotated);

        public bool CanDropOnSlot(EquipSlot slot) =>
            Dragging != null && inventory.CanEquip(Dragging, slot);

        // 강조 색 판정. 바깥(버리기)은 서비스가 판정할 방법이 없어 일단 가능으로 본다.
        public bool CanDrop(in DropTarget target)
        {
            switch (target.Kind)
            {
                case DropTargetKind.Grid: return CanDropAt(target.Grid, target.Cell);
                case DropTargetKind.Slot: return CanDropOnSlot(target.Slot);
                case DropTargetKind.Outside: return Dragging != null;
                default: return false;
            }
        }

        public bool DropAt(GridRef grid, Vector2Int cell)
        {
            if (!TakeDragging(out ItemInstance item, out bool rotated))
                return false;
            return inventory.TryMove(item, grid, cell, rotated);
        }

        public bool DropOnSlot(EquipSlot slot)
        {
            if (!TakeDragging(out ItemInstance item, out _))
                return false;
            return inventory.TryEquip(item, slot);
        }

        public bool DropOutside()
        {
            if (!TakeDragging(out ItemInstance item, out _))
                return false;
            return inventory.TryDrop(item);
        }

        public bool Drop(in DropTarget target)
        {
            switch (target.Kind)
            {
                case DropTargetKind.Grid: return DropAt(target.Grid, target.Cell);
                case DropTargetKind.Slot: return DropOnSlot(target.Slot);
                case DropTargetKind.Outside: return DropOutside();
                default:
                    Cancel();
                    return false;
            }
        }

        public void Cancel()
        {
            Dragging = null;
            Rotated = false;
        }

        // 인벤토리가 바뀐 뒤(InventoryChanged): 드래그 중 아이템이 다른 경로(재장전으로 꺼낸 탄창, 소진된 의약품 등)로
        // 인벤토리를 떠났으면 드래그를 취소한다. 취소했으면 true.
        public bool CancelIfGone()
        {
            if (Dragging == null || inventory.TryGetLocation(Dragging, out _))
                return false;
            Cancel();
            return true;
        }

        // 사용 중(잠긴) 아이템은 집을 수 없다. 열린 루트 안의 아이템은 검색이 끝난 것만 집을 수 있다 (§6 검색 연출).
        public static bool CanPick(IInventoryService inventory, ItemInstance item)
        {
            if (inventory == null || item == null || inventory.IsLocked(item))
                return false;
            ILootView loot = inventory.ActiveLoot;
            if (loot == null)
                return true;
            foreach (PlacedItem placed in loot.Items)
            {
                if (ReferenceEquals(placed.Item, item))
                    return loot.IsRevealed(item);
            }
            return true;
        }

        // 드래그를 끝내고 아이템·회전을 돌려준다 (성공 여부와 관계없이 드래그는 끝난다).
        private bool TakeDragging(out ItemInstance item, out bool rotated)
        {
            item = Dragging;
            rotated = Rotated;
            Cancel();
            return item != null;
        }
    }
}
