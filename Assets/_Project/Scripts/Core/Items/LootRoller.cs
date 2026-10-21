using System.Collections.Generic;

namespace Ashfall.Core
{
    // LootTableSO 굴림 (§14, M3 결정). 시체·상자가 같이 쓴다.
    public static class LootRoller
    {
        // rollCount.x~y(포함) 중 하나를 고르고 그 횟수만큼 가중치 비례 복원 추출한다.
        // 뽑힌 정의마다 ItemFactory.Create로 새 인스턴스를 into에 더한다. 더한 개수를 돌려준다.
        // table null·항목 없음·가중치 합 0 → 0. 정의가 null인 항목은 가중치와 상관없이 뽑히지 않는다.
        // random은 테스트에서 고정 시드.
        public static int Roll(LootTableSO table, System.Random random, List<ItemInstance> into)
        {
            if (table == null || table.entries == null || random == null || into == null)
                return 0;

            float total = 0f;
            List<LootEntry> entries = table.entries;
            for (int i = 0; i < entries.Count; i++)
                if (IsEligible(entries[i]))
                    total += entries[i].weight;
            if (total <= 0f)
                return 0;

            int min = table.rollCount.x < 0 ? 0 : table.rollCount.x;
            int max = table.rollCount.y < min ? min : table.rollCount.y;
            int rolls = random.Next(min, max + 1);

            int added = 0;
            for (int r = 0; r < rolls; r++)
            {
                ItemDefinitionSO picked = Pick(entries, total, random);
                if (picked == null)
                    continue;
                into.Add(ItemFactory.Create(picked));
                added++;
            }
            return added;
        }

        private static bool IsEligible(in LootEntry entry) => entry.item != null && entry.weight > 0f;

        private static ItemDefinitionSO Pick(List<LootEntry> entries, float total, System.Random random)
        {
            float roll = (float)(random.NextDouble() * total);
            ItemDefinitionSO last = null;
            for (int i = 0; i < entries.Count; i++)
            {
                LootEntry e = entries[i];
                if (!IsEligible(e))
                    continue;
                last = e.item;
                if (roll < e.weight)
                    return e.item;
                roll -= e.weight;
            }
            // 부동소수 오차로 끝까지 왔으면 마지막 유효 항목
            return last;
        }
    }
}
