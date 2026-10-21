using System.Collections.Generic;
using Ashfall.Core;

namespace Ashfall.AI
{
    // 시체 아이템 목록 (§6 시체, M3 설계 §1.9). 순서 = 검색으로 드러나는 순서: 무기 → 탄창 → 루트.
    // 사망 때 한 번 불리므로 할당해도 된다.
    public static class CorpseLootBuilder
    {
        // weapon.CollectLoot + LootRoller.Roll(table). 진행 중 재장전은 먼저 취소한다(장착 탄창 그대로).
        public static ItemInstance[] Build(EnemyWeapon weapon, LootTableSO table, System.Random random)
        {
            var items = new List<ItemInstance>(8);
            if (weapon != null)
            {
                weapon.CancelReload();
                weapon.CollectLoot(items);
            }
            LootRoller.Roll(table, random, items);
            return items.ToArray();
        }
    }
}
