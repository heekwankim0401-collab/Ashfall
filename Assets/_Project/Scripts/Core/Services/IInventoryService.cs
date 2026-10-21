using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Core
{
    // 그리드 하나를 가리키는 키. 주머니는 Index 0~3, 나머지는 0.
    public readonly struct GridRef : IEquatable<GridRef>
    {
        public readonly GridKind Kind;
        public readonly int Index;

        public GridRef(GridKind kind, int index = 0)
        {
            Kind = kind;
            Index = index;
        }

        public bool Equals(GridRef other) => Kind == other.Kind && Index == other.Index;
        public override bool Equals(object obj) => obj is GridRef other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Index;
        public static bool operator ==(GridRef a, GridRef b) => a.Equals(b);
        public static bool operator !=(GridRef a, GridRef b) => !a.Equals(b);
        public override string ToString() => $"{Kind}[{Index}]";
    }

    // 그리드 안의 아이템 배치. 회전은 아이템이 아니라 배치의 속성이다 (설계 D4).
    public readonly struct PlacedItem
    {
        public readonly ItemInstance Item;
        public readonly Vector2Int Cell;      // 왼쪽 위 칸 (x 오른쪽, y 아래쪽)
        public readonly bool Rotated;         // true면 W↔H

        public PlacedItem(ItemInstance item, Vector2Int cell, bool rotated)
        {
            Item = item;
            Cell = cell;
            Rotated = rotated;
        }

        public Vector2Int Size => Item.SizeFor(Rotated);
    }

    // 아이템이 있는 곳: 장착 슬롯 또는 그리드의 칸.
    public readonly struct ItemLocation
    {
        public readonly bool IsEquipped;
        public readonly EquipSlot Slot;       // IsEquipped일 때
        public readonly GridRef Grid;         // 아닐 때
        public readonly Vector2Int Cell;
        public readonly bool Rotated;

        public static ItemLocation Equipped(EquipSlot slot) =>
            new ItemLocation(true, slot, default, default, false);

        public static ItemLocation InGrid(GridRef grid, Vector2Int cell, bool rotated) =>
            new ItemLocation(false, default, grid, cell, rotated);

        public ItemLocation(bool isEquipped, EquipSlot slot, GridRef grid, Vector2Int cell, bool rotated)
        {
            IsEquipped = isEquipped;
            Slot = slot;
            Grid = grid;
            Cell = cell;
            Rotated = rotated;
        }
    }

    // 그리드 읽기 전용 뷰 (UI가 그린다)
    public interface IGridView
    {
        GridRef Ref { get; }
        Vector2Int Size { get; }
        IReadOnlyList<PlacedItem> Items { get; }
    }

    // 열린 루트 컨테이너 (§6 검색 연출)
    public interface ILootView : IGridView
    {
        string DisplayName { get; }
        bool IsRevealed(ItemInstance item);   // 검색 전 아이템은 UI가 빈칸/"?"로 그린다
        bool SearchComplete { get; }
    }

    // SYS-INV 플레이어 인벤토리 조회·명령 (설계 D2). Inventory가 구현해 GameManager.Services에 등록한다.
    // 변경 알림은 이벤트(InventoryChanged 등)로 받는다.
    public interface IInventoryService
    {
        // ── 읽기
        IReadOnlyList<IGridView> StorageGrids { get; }   // 주머니 4 + (장착 시) 조끼 + 배낭, 이 순서
        ItemInstance GetEquipped(EquipSlot slot);        // 비었으면 null
        ILootView ActiveLoot { get; }                    // 열린 루트 컨테이너, 없으면 null
        ItemInstance GetQuickSlot(int index);            // 0~3 = 키 4~7
        float TotalWeightKg { get; }
        bool TryGetLocation(ItemInstance item, out ItemLocation location);   // 장착·저장 공간·열린 루트에 없으면 false
        bool IsLocked(ItemInstance item);                // 사용 중 의약품처럼 옮길 수 없게 잠긴 아이템 (드래그 시작 거부)
        // M4: 장착 슬롯·저장 공간에 같은 정의가 있나 (열린 루트 제외). EXT-03 신호탄, 지도 아이템 (§8.2, RULE-06)
        bool Contains(ItemDefinitionSO definition);
        // M4 리뷰 수정: TryConsume이 지금 성공하나 (같은 범위 — 저장 공간만, 잠긴 것 제외, 사망·레이드 종료 잠금이면 false).
        // EXT-03 대기 판정은 Contains가 아니라 이것을 쓴다 (장착·잠긴 신호탄으로 대기만 반복되지 않게).
        bool CanConsume(ItemDefinitionSO definition);

        // ── 판정 (드래그 미리보기)
        bool CanPlace(ItemInstance item, GridRef grid, Vector2Int cell, bool rotated);
        bool CanEquip(ItemInstance item, EquipSlot slot);

        // ── 명령. 성공하면 true (+ 브리지가 InventoryChanged 발행)
        bool TryMove(ItemInstance item, GridRef grid, Vector2Int cell, bool rotated);
        bool TryEquip(ItemInstance item, EquipSlot slot);       // 슬롯이 차 있으면 실패 (교환 없음, TODO(GDD-?))
        bool TryQuickMove(ItemInstance item);                   // 루트 ↔ 저장 공간 자동 배치
        bool TryDrop(ItemInstance item);                        // 바닥에 버리기 (WorldItem)
        bool TryAssignQuickSlot(int index, ItemInstance item);  // 의약품만, 저장 공간에 있는 것만
        bool TryUse(ItemInstance item);                         // 의약품 → MedicalUseRequested
        // M4: 저장 공간에서 같은 정의 하나를 없앤다 (EXT-03 신호탄 소모). 장착 슬롯·열린 루트·잠긴 아이템 제외, 사망 후 거부.
        bool TryConsume(ItemDefinitionSO definition);
        void CloseLoot();
    }
}
