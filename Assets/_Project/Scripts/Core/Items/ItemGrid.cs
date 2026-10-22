using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Core
{
    // SYS-INV 그리드 하나 (W×H 칸). 아이템 배치·회전·충돌 판정 (§6).
    // M5: Inventory → Core로 이동 (M5 설계 Q2 — 은신처 보관함·상인·키트(Hideout)가 같은 배치 판정을 쓴다).
    // 칸 좌표: x 오른쪽, y 아래쪽. 회전은 배치(PlacedItem)에 저장한다 (설계 D4).
    public sealed class ItemGrid : IGridView
    {
        private readonly List<PlacedItem> items = new List<PlacedItem>();

        public GridRef Ref { get; }
        public Vector2Int Size { get; }
        public IReadOnlyList<PlacedItem> Items => items;
        public bool IsEmpty => items.Count == 0;

        // 들어 있는 아이템 무게 합 (할당 없음)
        public float TotalWeightKg
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < items.Count; i++)
                    total += items[i].Item.WeightKg;
                return total;
            }
        }

        public ItemGrid(GridRef id, Vector2Int size)
        {
            Ref = id;
            Size = size;
        }

        // 아이템 자신과 ignore는 겹침 판정에서 뺀다 (같은 그리드 안에서 옮길 때).
        public bool CanPlace(ItemInstance item, Vector2Int cell, bool rotated, ItemInstance ignore = null)
        {
            if (item == null)
                return false;
            Vector2Int size = item.SizeFor(rotated);
            if (cell.x < 0 || cell.y < 0 || cell.x + size.x > Size.x || cell.y + size.y > Size.y)
                return false;

            var rect = new RectInt(cell, size);
            foreach (PlacedItem placed in items)
            {
                if (ReferenceEquals(placed.Item, item) || ReferenceEquals(placed.Item, ignore))
                    continue;
                if (rect.Overlaps(new RectInt(placed.Cell, placed.Size)))
                    return false;
            }
            return true;
        }

        // 이미 이 그리드에 있으면 false (옮길 때는 Remove 후 TryPlace).
        public bool TryPlace(ItemInstance item, Vector2Int cell, bool rotated)
        {
            if (item == null || Contains(item) || !CanPlace(item, cell, rotated))
                return false;
            items.Add(new PlacedItem(item, cell, rotated));
            return true;
        }

        public bool Remove(ItemInstance item)
        {
            int index = IndexOf(item);
            if (index < 0)
                return false;
            items.RemoveAt(index);
            return true;
        }

        public bool Contains(ItemInstance item) => IndexOf(item) >= 0;

        public bool TryGetPlacement(ItemInstance item, out PlacedItem placed)
        {
            int index = IndexOf(item);
            placed = index >= 0 ? items[index] : default;
            return index >= 0;
        }

        // 위→아래, 왼→오른쪽. 같은 칸에서 회전 안 한 것 먼저. 정사각형은 회전을 시도하지 않는다.
        public bool TryFindFreeCell(ItemInstance item, out Vector2Int cell, out bool rotated)
        {
            if (item != null)
            {
                bool square = item.Definition.size.x == item.Definition.size.y;
                for (int y = 0; y < Size.y; y++)
                {
                    for (int x = 0; x < Size.x; x++)
                    {
                        var c = new Vector2Int(x, y);
                        if (CanPlace(item, c, false))
                        {
                            cell = c;
                            rotated = false;
                            return true;
                        }
                        if (!square && CanPlace(item, c, true))
                        {
                            cell = c;
                            rotated = true;
                            return true;
                        }
                    }
                }
            }
            cell = default;
            rotated = false;
            return false;
        }

        private int IndexOf(ItemInstance item)
        {
            if (item == null)
                return -1;
            for (int i = 0; i < items.Count; i++)
                if (ReferenceEquals(items[i].Item, item))
                    return i;
            return -1;
        }
    }
}
