using System;
using System.Collections.Generic;

namespace Ashfall.Core
{
    // 장비 하나의 자리 (M5 설계 §4.1): Equipped(slot) 또는 InGrid(Pocket[i] / TacticalVest / Backpack, cell, rotated).
    // Loot 자리는 없다 (Loadout 생성자가 버린다).
    public readonly struct LoadoutEntry
    {
        public readonly ItemInstance Item;
        public readonly ItemLocation Location;

        public LoadoutEntry(ItemInstance item, ItemLocation location)
        {
            Item = item;
            Location = location;
        }
    }

    // 반입(§18 Loadout) = 장착 슬롯 + 저장 공간 배치 + 퀵슬롯 (M5 설계 §1.3, Q3·Q19).
    // 은신처 장비 ↔ 레이드 소지품을 오가는 묶음. 만든 뒤 바뀌지 않는다(목록은 생성 시 복사).
    // 아이템 인스턴스 자체는 공유한다 — 같은 인스턴스가 동시에 두 인벤토리에 놓이지 않게 하는 것은 호출자(흐름) 몫.
    public sealed class Loadout
    {
        public const int QuickSlotCount = 4;   // §12.1 키 4~7

        private static readonly EquipSlot[] SlotOrder = (EquipSlot[])Enum.GetValues(typeof(EquipSlot));

        public static readonly Loadout Empty = new Loadout(null, null);

        private readonly LoadoutEntry[] entries;
        private readonly ItemInstance[] quickSlots;

        // 복사. null → 빈 것. null 아이템·Loot 자리 항목은 버리고, 같은 인스턴스가 여러 번이면 첫 번째만 남긴다.
        // 퀵슬롯은 길이 4로 맞추고(넘치면 자름), Entries 밖 아이템은 null로 바꾼다.
        public Loadout(IReadOnlyList<LoadoutEntry> entries, IReadOnlyList<ItemInstance> quickSlots)
        {
            var kept = new List<LoadoutEntry>(entries?.Count ?? 0);
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    LoadoutEntry entry = entries[i];
                    if (entry.Item == null)
                        continue;
                    if (!entry.Location.IsEquipped && entry.Location.Grid.Kind == GridKind.Loot)
                        continue;
                    if (IndexOf(kept, entry.Item) >= 0)
                        continue;
                    kept.Add(entry);
                }
            }
            this.entries = kept.ToArray();

            this.quickSlots = new ItemInstance[QuickSlotCount];
            if (quickSlots != null)
            {
                for (int i = 0; i < QuickSlotCount && i < quickSlots.Count; i++)
                {
                    ItemInstance item = quickSlots[i];
                    this.quickSlots[i] = item != null && IndexOf(kept, item) >= 0 ? item : null;
                }
            }
        }

        public IReadOnlyList<LoadoutEntry> Entries => entries;
        public IReadOnlyList<ItemInstance> QuickSlots => quickSlots;   // 길이 4, 빈 칸 null
        public bool IsEmpty => entries.Length == 0;

        // Entries 순서로 아이템을 into에 더한다 (Capture로 만든 것이면 = InventorySnapshot.Collect 순서). 더한 수.
        // 무기의 장착 탄창은 무기에 포함(따로 넣지 않음). into null → 0.
        public int CollectItems(List<ItemInstance> into)
        {
            if (into == null)
                return 0;
            for (int i = 0; i < entries.Length; i++)
                into.Add(entries[i].Item);
            return entries.Length;
        }

        // 인벤토리의 현재 장비를 위치까지 기록한다 (IInventoryService 읽기 API만).
        // 장착 7슬롯(EquipSlot 순) → StorageGrids(순서·배치 순, Loot 종류는 건너뜀) → 퀵슬롯 0~3. inventory null → Empty.
        // 재장전은 완료 순간 탄창을 바로 바꾼다(WeaponModel.CompleteReload) — 인벤토리 밖 "손에 든 탄창"은 없다.
        // 빠른 재장전으로 떨군 탄창은 월드 아이템이라 들어가지 않는다 (InventorySnapshot과 같음).
        public static Loadout Capture(IInventoryService inventory)
        {
            if (inventory == null)
                return Empty;

            var list = new List<LoadoutEntry>();
            for (int i = 0; i < SlotOrder.Length; i++)
            {
                ItemInstance item = inventory.GetEquipped(SlotOrder[i]);
                if (item != null)
                    list.Add(new LoadoutEntry(item, ItemLocation.Equipped(SlotOrder[i])));
            }

            IReadOnlyList<IGridView> grids = inventory.StorageGrids;
            if (grids != null)
            {
                for (int g = 0; g < grids.Count; g++)
                {
                    IGridView grid = grids[g];
                    if (grid == null || grid.Ref.Kind == GridKind.Loot || grid.Items == null)
                        continue;
                    IReadOnlyList<PlacedItem> items = grid.Items;
                    for (int i = 0; i < items.Count; i++)
                    {
                        PlacedItem placed = items[i];
                        if (placed.Item != null)
                            list.Add(new LoadoutEntry(placed.Item, ItemLocation.InGrid(grid.Ref, placed.Cell, placed.Rotated)));
                    }
                }
            }

            var quick = new ItemInstance[QuickSlotCount];
            for (int i = 0; i < QuickSlotCount; i++)
                quick[i] = inventory.GetQuickSlot(i);

            return new Loadout(list, quick);
        }

        private static int IndexOf(List<LoadoutEntry> list, ItemInstance item)
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i].Item, item))
                    return i;
            return -1;
        }
    }
}
