using System;
using System.Collections.Generic;

namespace Ashfall.Core
{
    // 인벤토리 소지품 목록 (RULE-01 반입·반출·소실, M4 설계 §1.5·§1.6). Raid와 M5 Hideout이 같이 쓴다.
    // IInventoryService의 읽기 API(GetEquipped·StorageGrids)만으로 계산한다.
    public static class InventorySnapshot
    {
        private static readonly EquipSlot[] SlotOrder = (EquipSlot[])Enum.GetValues(typeof(EquipSlot));

        // 장착 7슬롯(EquipSlot 순) → StorageGrids(순서·배치 순)의 아이템을 into에 더한다. 더한 수를 돌려준다.
        // 열린 루트(ActiveLoot)는 제외. 같은 인스턴스는 한 번만. 무기의 장착 탄창은 무기에 포함(따로 넣지 않음).
        // 인자 null → 0. TODO(GDD-?): 재장전 중 손에 든 탄창(인벤토리 밖)은 빠진다.
        public static int Collect(IInventoryService inventory, List<ItemInstance> into)
        {
            if (inventory == null || into == null)
                return 0;

            int start = into.Count;
            for (int i = 0; i < SlotOrder.Length; i++)
                AddOnce(inventory.GetEquipped(SlotOrder[i]), into, start);

            IReadOnlyList<IGridView> grids = inventory.StorageGrids;
            if (grids != null)
            {
                for (int g = 0; g < grids.Count; g++)
                {
                    IGridView grid = grids[g];
                    // StorageGrids에는 루트가 없지만 구현이 잘못 넣어도 소지품으로 세지 않는다.
                    if (grid == null || grid.Ref.Kind == GridKind.Loot || grid.Items == null)
                        continue;
                    IReadOnlyList<PlacedItem> items = grid.Items;
                    for (int i = 0; i < items.Count; i++)
                        AddOnce(items[i].Item, into, start);
                }
            }
            return into.Count - start;
        }

        // 이번 호출에서 더한 범위 안에서만 중복을 본다.
        private static void AddOnce(ItemInstance item, List<ItemInstance> into, int start)
        {
            if (item == null)
                return;
            for (int i = start; i < into.Count; i++)
                if (ReferenceEquals(into[i], item))
                    return;
            into.Add(item);
        }
    }
}
