using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // SYS-INV 인벤토리 화면 (그리기 + 포인터 처리). 규칙 판정은 IInventoryService, 드래그 상태는 InventoryDragModel.
    // 레이아웃: 왼쪽 장착 슬롯 7개 / 가운데 저장 공간(주머니·조끼·배낭) + 퀵슬롯 / 오른쪽 루트 패널(열렸을 때).
    // - 누르고 끌면 드래그(R 회전), 놓은 곳: 그리드 칸 → TryMove, 슬롯 → TryEquip, 패널 밖 → TryDrop, 패널 안 빈 곳 → 취소.
    // - 더블클릭: 의약품 사용. Ctrl+클릭: 루트 ↔ 내 공간 빠른 이동.
    // - 무게·탄 수·사용 횟수 숫자는 그리지 않는다 (RULE-04, 정보 희소성. TODO(GDD-?)).
    public sealed class InventoryScreenView
    {
        public const string ScreenName = "inventory-screen";
        public const string EquipSlotsName = "inv-equip-slots";
        public const string StorageName = "inv-storage-grids";
        public const string QuickSlotsName = "inv-quick-slots";
        public const string LootPanelName = "inv-loot-panel";
        public const string LootTitleName = "inv-loot-title";
        public const string LootStatusName = "inv-loot-status";
        public const string LootGridName = "inv-loot-grid-host";
        public const string DragLayerName = "inv-drag-layer";

        // USS 클래스 (Art/UI/Ashfall.uss)
        public const string PanelClass = "inv-panel";
        public const string SectionClass = "inv-section";
        public const string SectionTitleClass = "inv-section-title";
        public const string PocketRowClass = "inv-pocket-row";
        public const string GridClass = "inv-grid";
        public const string GridRowClass = "inv-grid-row";
        public const string CellClass = "inv-cell";
        public const string GridItemsClass = "inv-grid-items";
        public const string ItemClass = "inv-item";
        public const string HiddenItemClass = "inv-item--hidden";
        public const string SlotItemClass = "inv-item--slot";
        public const string DragSourceClass = "inv-item--drag-source";
        public const string GhostClass = "inv-ghost";
        public const string GhostOutsideClass = "inv-ghost--outside";
        public const string SlotClass = "inv-slot";
        public const string SlotLabelClass = "inv-slot-label";
        public const string QuickSlotClass = "inv-quick";
        public const string QuickKeyClass = "inv-quick-key";
        public const string QuickItemClass = "inv-quick-item";
        public const string HighlightClass = "inv-drop-highlight";
        public const string DropOkClass = "drop-ok";
        public const string DropBadClass = "drop-bad";

        private static readonly EquipSlot[] Slots = (EquipSlot[])Enum.GetValues(typeof(EquipSlot));

        private sealed class GridTag
        {
            public readonly GridRef Ref;
            public readonly Vector2Int Size;
            public readonly VisualElement Overlay;

            public GridTag(GridRef gridRef, Vector2Int size, VisualElement overlay)
            {
                Ref = gridRef;
                Size = size;
                Overlay = overlay;
            }
        }

        private sealed class SlotTag
        {
            public readonly EquipSlot Slot;
            public SlotTag(EquipSlot slot) { Slot = slot; }
        }

        private sealed class ItemTag
        {
            public readonly ItemInstance Item;
            public ItemTag(ItemInstance item) { Item = item; }
        }

        private readonly VisualElement screen;
        private readonly VisualElement equipSlots;
        private readonly VisualElement storage;
        private readonly VisualElement quickSlots;
        private readonly VisualElement lootPanel;
        private readonly Label lootTitle;
        private readonly Label lootStatus;
        private readonly VisualElement lootGridHost;
        private readonly VisualElement dragLayer;

        private readonly float dragThresholdPx;
        private readonly IReadOnlyList<string> quickSlotKeys;
        private readonly Func<bool> quickMoveModifier;
        private readonly List<VisualElement> grids = new List<VisualElement>();

        private IInventoryService inventory;
        private InventoryDragModel drag;

        // 포인터 상태
        private bool pressed;          // 아이템을 눌렀다 (아직 드래그 전일 수 있음)
        private bool dragging;         // 임계 거리를 넘어 드래그 중
        private int pointerId = -1;
        private ItemInstance pressItem;
        private Vector2 pressPosition;
        private Vector2 lastPointer;
        private bool hasPointer;
        private float cellPx;

        private Label ghost;
        private VisualElement highlight;
        private VisualElement highlightedSlot;

        public InventoryScreenView(VisualElement documentRoot, float dragThresholdPx,
            IReadOnlyList<string> quickSlotKeys, Func<bool> quickMoveModifier)
        {
            screen = documentRoot.Q<VisualElement>(ScreenName) ?? throw HudView.Missing(ScreenName);
            equipSlots = documentRoot.Q<VisualElement>(EquipSlotsName) ?? throw HudView.Missing(EquipSlotsName);
            storage = documentRoot.Q<VisualElement>(StorageName) ?? throw HudView.Missing(StorageName);
            quickSlots = documentRoot.Q<VisualElement>(QuickSlotsName) ?? throw HudView.Missing(QuickSlotsName);
            lootPanel = documentRoot.Q<VisualElement>(LootPanelName) ?? throw HudView.Missing(LootPanelName);
            lootTitle = documentRoot.Q<Label>(LootTitleName) ?? throw HudView.Missing(LootTitleName);
            lootStatus = documentRoot.Q<Label>(LootStatusName) ?? throw HudView.Missing(LootStatusName);
            lootGridHost = documentRoot.Q<VisualElement>(LootGridName) ?? throw HudView.Missing(LootGridName);
            dragLayer = documentRoot.Q<VisualElement>(DragLayerName) ?? throw HudView.Missing(DragLayerName);

            this.dragThresholdPx = dragThresholdPx;
            this.quickSlotKeys = quickSlotKeys ?? Array.Empty<string>();
            this.quickMoveModifier = quickMoveModifier;

            dragLayer.pickingMode = PickingMode.Ignore;
            screen.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            screen.RegisterCallback<PointerUpEvent>(OnPointerUp);
            screen.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);

            SetVisible(false);
        }

        // GameUIRoot가 꺼질 때. 같은 요소에 새 뷰가 다시 붙을 수 있으므로 콜백을 떼어 둔다.
        public void Dispose()
        {
            CancelPointer();
            screen.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            screen.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            screen.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            inventory = null;
            drag = null;
        }

        public void SetVisible(bool visible)
        {
            if (!visible)
                CancelPointer();
            screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Bind(IInventoryService inventoryService, InventoryDragModel dragModel)
        {
            if (ReferenceEquals(inventory, inventoryService) && ReferenceEquals(drag, dragModel))
                return;
            CancelPointer();
            inventory = inventoryService;
            drag = dragModel;
        }

        // 퀵슬롯 배정(4~7)용: 마지막 포인터 위치 아래의 아이템
        public ItemInstance ItemUnderPointer()
        {
            if (!hasPointer || screen.panel == null)
                return null;
            for (VisualElement e = screen.panel.Pick(lastPointer); e != null; e = e.parent)
            {
                if (e.userData is ItemTag tag)
                    return tag.Item;
                if (e == screen)
                    break;
            }
            return null;
        }

        // 매 프레임: 모델 쪽에서 드래그가 끝났으면(Esc 등) 화면 정리
        public void SyncDrag()
        {
            if (dragging && (drag == null || !drag.IsDragging))
            {
                CancelPointer();
                Render();
            }
        }

        // R 회전 뒤 고스트 크기·강조 갱신
        public void RefreshDrag()
        {
            if (!dragging || drag == null || !drag.IsDragging)
                return;
            UpdateGhostSize();
            UpdateDragVisuals();
        }

        // 드래그·눌림 상태를 모델과 함께 버린다.
        public void CancelPointer()
        {
            if (dragging && drag != null)
                drag.Cancel();
            EndPointer();
        }

        // ── 그리기 ───────────────────────────────────────────

        public void Render()
        {
            equipSlots.Clear();
            storage.Clear();
            quickSlots.Clear();
            lootGridHost.Clear();
            grids.Clear();
            highlight = null;
            highlightedSlot = null;

            if (inventory == null)
            {
                lootPanel.style.display = DisplayStyle.None;
                return;
            }

            foreach (EquipSlot slot in Slots)
                equipSlots.Add(BuildSlot(slot, inventory.GetEquipped(slot)));

            // StorageGrids 순서 = 주머니 4 + 조끼 + 배낭. 주머니는 한 줄로 묶는다.
            VisualElement pocketRow = null;
            foreach (IGridView grid in inventory.StorageGrids)
            {
                if (grid.Ref.Kind == GridKind.Pocket)
                {
                    if (pocketRow == null)
                    {
                        VisualElement section = Section(UiText.GridTitle(GridKind.Pocket));
                        pocketRow = new VisualElement { pickingMode = PickingMode.Ignore };
                        pocketRow.AddToClassList(PocketRowClass);
                        section.Add(pocketRow);
                        storage.Add(section);
                    }
                    pocketRow.Add(BuildGrid(grid, null));
                }
                else
                {
                    VisualElement section = Section(UiText.GridTitle(grid.Ref.Kind));
                    section.Add(BuildGrid(grid, null));
                    storage.Add(section);
                }
            }

            for (int i = 0; i < quickSlotKeys.Count; i++)
                quickSlots.Add(BuildQuickSlot(quickSlotKeys[i], inventory.GetQuickSlot(i)));

            ILootView loot = inventory.ActiveLoot;
            if (loot == null)
            {
                lootPanel.style.display = DisplayStyle.None;
            }
            else
            {
                lootPanel.style.display = DisplayStyle.Flex;
                lootTitle.text = string.IsNullOrEmpty(loot.DisplayName) ? UiText.GridTitle(GridKind.Loot) : loot.DisplayName;
                lootStatus.text = loot.SearchComplete ? string.Empty : UiText.LootSearching;
                lootStatus.style.display = loot.SearchComplete ? DisplayStyle.None : DisplayStyle.Flex;
                lootGridHost.Add(BuildGrid(loot, loot));
            }
            // 드래그 중 다시 그렸으면 강조는 다음 포인터 이동에서 새 요소(배치 계산 후)에 다시 단다.
        }

        private static VisualElement Section(string title)
        {
            var section = new VisualElement { pickingMode = PickingMode.Ignore };
            section.AddToClassList(SectionClass);
            var label = new Label(title) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(SectionTitleClass);
            section.Add(label);
            return section;
        }

        private VisualElement BuildSlot(EquipSlot slot, ItemInstance item)
        {
            var element = new VisualElement { userData = new SlotTag(slot) };
            element.AddToClassList(SlotClass);
            var label = new Label(UiText.SlotLabel(slot)) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(SlotLabelClass);
            element.Add(label);
            if (item != null)
            {
                Label itemElement = BuildItem(item);
                itemElement.AddToClassList(SlotItemClass);
                element.Add(itemElement);
            }
            return element;
        }

        private static VisualElement BuildQuickSlot(string keyLabel, ItemInstance item)
        {
            // 퀵슬롯은 저장 공간 아이템을 가리키기만 한다 → 여기서는 끌 수 없다.
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(QuickSlotClass);
            var key = new Label(keyLabel) { pickingMode = PickingMode.Ignore };
            key.AddToClassList(QuickKeyClass);
            element.Add(key);
            var name = new Label(UiText.ItemLabel(item)) { pickingMode = PickingMode.Ignore };
            name.AddToClassList(QuickItemClass);
            element.Add(name);
            return element;
        }

        // 칸 배경(행·칸, 포인터 무시) + 아이템 겹침 층. 아이템 위치·크기는 그리드 대비 퍼센트라
        // C#이 칸 크기(px)를 몰라도 된다 (칸 크기는 USS --ash-cell-size).
        private VisualElement BuildGrid(IGridView view, ILootView loot)
        {
            var grid = new VisualElement();
            grid.AddToClassList(GridClass);
            for (int y = 0; y < view.Size.y; y++)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList(GridRowClass);
                for (int x = 0; x < view.Size.x; x++)
                {
                    var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                    cell.AddToClassList(CellClass);
                    row.Add(cell);
                }
                grid.Add(row);
            }

            var overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.AddToClassList(GridItemsClass);
            grid.Add(overlay);
            grid.userData = new GridTag(view.Ref, view.Size, overlay);

            foreach (PlacedItem placed in view.Items)
            {
                bool hidden = loot != null && !loot.IsRevealed(placed.Item);
                VisualElement element = hidden ? BuildHiddenItem() : BuildItem(placed.Item);
                PlaceInGrid(element, placed.Cell, placed.Size, view.Size);
                overlay.Add(element);
            }

            grids.Add(grid);
            return grid;
        }

        private Label BuildItem(ItemInstance item)
        {
            var element = new Label(UiText.ItemLabel(item)) { userData = new ItemTag(item) };
            element.AddToClassList(ItemClass);
            if (drag != null && ReferenceEquals(drag.Dragging, item))
                element.AddToClassList(DragSourceClass);
            element.RegisterCallback<PointerDownEvent>(OnItemPointerDown);
            return element;
        }

        // 검색 전 아이템: "?"만, 집을 수 없다 (§6 검색 연출)
        private static Label BuildHiddenItem()
        {
            var element = new Label(UiText.HiddenItem) { pickingMode = PickingMode.Ignore };
            element.AddToClassList(ItemClass);
            element.AddToClassList(HiddenItemClass);
            return element;
        }

        private static void PlaceInGrid(VisualElement element, Vector2Int cell, Vector2Int size, Vector2Int gridSize)
        {
            element.style.position = Position.Absolute;
            element.style.left = Length.Percent(cell.x * 100f / gridSize.x);
            element.style.top = Length.Percent(cell.y * 100f / gridSize.y);
            element.style.width = Length.Percent(size.x * 100f / gridSize.x);
            element.style.height = Length.Percent(size.y * 100f / gridSize.y);
        }

        // ── 포인터 ───────────────────────────────────────────

        private void OnItemPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || inventory == null || drag == null || pressed)
                return;
            if (!((evt.currentTarget as VisualElement)?.userData is ItemTag tag))
                return;
            evt.StopPropagation();

            ItemInstance item = tag.Item;
            if (!InventoryDragModel.CanPick(inventory, item))
                return;

            // 결과는 InventoryChanged → 다시 그리기로 반영된다.
            if (evt.ctrlKey || (quickMoveModifier != null && quickMoveModifier()))
            {
                inventory.TryQuickMove(item);
                return;
            }
            if (evt.clickCount >= 2 && item is MedicalItem)
            {
                inventory.TryUse(item);
                return;
            }

            pressed = true;
            pressItem = item;
            pressPosition = evt.position;
            lastPointer = evt.position;
            hasPointer = true;
            pointerId = evt.pointerId;
            screen.CapturePointer(pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            lastPointer = evt.position;
            hasPointer = true;
            if (!pressed || evt.pointerId != pointerId)
                return;

            if (!dragging)
            {
                if (!InventoryLayout.ExceedsThreshold(pressPosition, lastPointer, dragThresholdPx))
                    return;
                if (!drag.Begin(pressItem))
                {
                    EndPointer();
                    return;
                }
                dragging = true;
                MeasureCellPx();
                CreateGhost();
                MarkDragSource(pressItem);
            }
            UpdateDragVisuals();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!pressed || evt.pointerId != pointerId)
                return;
            lastPointer = evt.position;

            bool wasDragging = dragging && drag.IsDragging;
            if (wasDragging)
            {
                DropTarget target = ResolveTarget(lastPointer, out _);
                drag.Drop(target);   // 실패하면 서비스가 아무것도 바꾸지 않음 → 원위치
            }
            EndPointer();
            if (wasDragging)
                Render();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!pressed)
                return;
            bool wasDragging = dragging;
            CancelPointer();
            if (wasDragging)
                Render();
        }

        // 상태를 먼저 지우고 캡처를 푼다 (ReleasePointer가 CaptureOut을 다시 부르므로).
        private void EndPointer()
        {
            int id = pointerId;
            pressed = false;
            dragging = false;
            pressItem = null;
            pointerId = -1;

            if (ghost != null)
            {
                ghost.RemoveFromHierarchy();
                ghost = null;
            }
            ClearHighlight();

            if (id >= 0 && screen.HasPointerCapture(id))
                screen.ReleasePointer(id);
        }

        // 포인터 아래 요소에서 위로 올라가며 놓을 곳을 찾는다.
        private DropTarget ResolveTarget(Vector2 position, out VisualElement targetElement)
        {
            targetElement = null;
            if (screen.panel == null)
                return DropTarget.Panel();

            for (VisualElement e = screen.panel.Pick(position); e != null; e = e.parent)
            {
                if (e.userData is GridTag grid)
                {
                    targetElement = e;
                    float px = InventoryLayout.CellPx(e.layout.width, grid.Size.x);
                    Vector2 local = e.WorldToLocal(position);
                    return DropTarget.OnGrid(grid.Ref, InventoryLayout.TopLeftCellForPointer(local, px, drag.Size));
                }
                if (e.userData is SlotTag slot)
                {
                    targetElement = e;
                    return DropTarget.OnSlot(slot.Slot);
                }
                if (e.ClassListContains(PanelClass))
                {
                    targetElement = e;
                    return DropTarget.Panel();
                }
                if (e == screen)
                    break;
            }
            return DropTarget.Outside();
        }

        private void UpdateDragVisuals()
        {
            PositionGhost();
            ClearHighlight();

            DropTarget target = ResolveTarget(lastPointer, out VisualElement element);
            bool ok = drag.CanDrop(target);
            ghost?.EnableInClassList(GhostOutsideClass, target.Kind == DropTargetKind.Outside);

            switch (target.Kind)
            {
                case DropTargetKind.Grid:
                    var grid = (GridTag)element.userData;
                    highlight = new VisualElement { pickingMode = PickingMode.Ignore };
                    highlight.AddToClassList(HighlightClass);
                    highlight.AddToClassList(ok ? DropOkClass : DropBadClass);
                    PlaceInGrid(highlight, target.Cell, drag.Size, grid.Size);
                    grid.Overlay.Add(highlight);
                    break;
                case DropTargetKind.Slot:
                    highlightedSlot = element;
                    element.AddToClassList(ok ? DropOkClass : DropBadClass);
                    break;
            }
        }

        private void ClearHighlight()
        {
            if (highlight != null)
            {
                highlight.RemoveFromHierarchy();
                highlight = null;
            }
            if (highlightedSlot != null)
            {
                highlightedSlot.RemoveFromClassList(DropOkClass);
                highlightedSlot.RemoveFromClassList(DropBadClass);
                highlightedSlot = null;
            }
        }

        private void MeasureCellPx()
        {
            cellPx = 0f;
            foreach (VisualElement grid in grids)
            {
                if (!(grid.userData is GridTag tag))
                    continue;
                float px = InventoryLayout.CellPx(grid.layout.width, tag.Size.x);
                if (px > 0f)
                {
                    cellPx = px;
                    return;
                }
            }
        }

        private void MarkDragSource(ItemInstance item)
        {
            screen.Query<Label>(className: ItemClass).ForEach(e =>
            {
                if (e.userData is ItemTag tag && ReferenceEquals(tag.Item, item))
                    e.AddToClassList(DragSourceClass);
            });
        }

        private void CreateGhost()
        {
            ghost = new Label(UiText.ItemLabel(drag.Dragging)) { pickingMode = PickingMode.Ignore };
            ghost.AddToClassList(ItemClass);
            ghost.AddToClassList(GhostClass);
            ghost.style.position = Position.Absolute;
            dragLayer.Add(ghost);
            UpdateGhostSize();
        }

        private void UpdateGhostSize()
        {
            if (ghost == null || cellPx <= 0f)
                return;
            Vector2Int size = drag.Size;
            ghost.style.width = cellPx * size.x;
            ghost.style.height = cellPx * size.y;
        }

        private void PositionGhost()
        {
            if (ghost == null)
                return;
            Vector2 local = dragLayer.WorldToLocal(lastPointer);
            Vector2 half = cellPx > 0f ? (Vector2)drag.Size * (cellPx * 0.5f) : Vector2.zero;
            ghost.style.left = local.x - half.x;
            ghost.style.top = local.y - half.y;
        }
    }
}
