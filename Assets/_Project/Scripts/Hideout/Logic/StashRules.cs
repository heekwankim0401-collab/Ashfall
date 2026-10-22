using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Hideout
{
    // 보관함 자동 배치 (M5 설계 §3.2 매수, §3.3 키트). 순수 정적.
    // 배치 순서는 ItemGrid.TryFindFreeCell (위→아래·왼→오른쪽, 같은 칸에서 회전 안 한 것 먼저).
    public static class StashRules
    {
        // 빈 자리를 찾아 놓는다. grid·item null, 이미 들어 있음, 자리 없음 → false.
        public static bool TryPlaceNew(ItemGrid grid, ItemInstance item)
        {
            if (grid == null || item == null || grid.Contains(item))
                return false;
            return grid.TryFindFreeCell(item, out Vector2Int cell, out bool rotated) && grid.TryPlace(item, cell, rotated);
        }

        // 전부 놓거나 아무것도 놓지 않는다 (Q15). 앞에서부터 하나씩 자동 배치하다가 실패하면 이번에 놓은 것을 되돌린다.
        // null 항목은 건너뛴다. 빈 목록·null 목록 → true. grid null → false.
        // TODO(GDD-?) Q15: 순서대로 놓는 탐욕 배치라 다른 배치로는 들어갈 수 있는 경우에도 실패할 수 있다(가장 단순).
        public static bool TryPlaceAll(ItemGrid grid, IReadOnlyList<ItemInstance> items)
        {
            if (grid == null)
                return false;
            if (items == null)
                return true;

            var placed = new List<ItemInstance>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                ItemInstance item = items[i];
                if (item == null)
                    continue;
                if (!TryPlaceNew(grid, item))
                {
                    for (int p = 0; p < placed.Count; p++)
                        grid.Remove(placed[p]);
                    return false;
                }
                placed.Add(item);
            }
            return true;
        }
    }
}
