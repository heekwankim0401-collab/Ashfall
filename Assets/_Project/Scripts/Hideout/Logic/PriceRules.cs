using System;
using Ashfall.Core;

namespace Ashfall.Hideout
{
    // SYS-HUB 상인 가격 규칙 (§9, M5 설계 §3.2, TODO(GDD-?) Q10).
    // 매수가 = basePrice ≤ 0이면 판매 안 함(-1), 아니면 max(1, 올림(basePrice × buyPriceMultiplier)).
    // 매도가 = max(0, 내림(basePrice × sellPriceMultiplier)), 무기는 장착 탄창의 매도가를 더한다(탄창째 팔림).
    // 탄창 남은 탄 수·의약품 남은 횟수는 가격에 반영하지 않는다 (TODO(GDD-?) Q10).
    // MVP 마무리 F3: 매도가 × 내구도 비율(IDurableItem), 수리비 RepairCost. 매수가는 새 물건(비율 1)이라 그대로.
    public static class PriceRules
    {
        // 배율이 float라 1.1f × 100 = 110.0000024 같은 오차가 생긴다. 올림·내림 전에 이만큼 정수 쪽으로 붙인다.
        private const double RoundingTolerance = 1e-3;

        // 판매 안 함(null·basePrice ≤ 0·trader null) = -1
        public static int BuyPrice(ItemDefinitionSO item, TraderConfigSO trader)
        {
            if (item == null || trader == null || item.basePrice <= 0)
                return -1;
            double raw = (double)item.basePrice * Math.Max(0f, trader.buyPriceMultiplier);
            return Math.Max(1, ToInt(Math.Ceiling(raw - RoundingTolerance)));
        }

        // 아이템 null·trader null = -1. 그 밖은 0 이상. 무기는 장착 탄창 포함.
        // MVP 마무리 F3 (MF-Q12): 본체 = 내림(basePrice × 매도 배율 × 내구도 비율) — 내구도 없는 아이템·100%는 비율 1(기존 값 그대로),
        // 0%면 0 탄피(처분 판매, M5 Q12). 장착 탄창 매도가는 비율과 무관하게 그대로 더한다.
        public static int SellPrice(ItemInstance item, TraderConfigSO trader)
        {
            if (item == null || trader == null)
                return -1;
            int price = SellPrice(item.Definition, trader, DurabilityRules.Ratio(item));
            if (item is WeaponItem weapon && weapon.LoadedMagazine != null)
                price = SafeAdd(price, SellPrice(weapon.LoadedMagazine.Definition, trader));
            return price;
        }

        // 정의 하나의 매도가 (장착 탄창 제외, 새 물건 = 비율 1). null → -1, basePrice ≤ 0 → 0.
        public static int SellPrice(ItemDefinitionSO definition, TraderConfigSO trader) => SellPrice(definition, trader, 1f);

        private static int SellPrice(ItemDefinitionSO definition, TraderConfigSO trader, float durabilityRatio)
        {
            if (definition == null || trader == null)
                return -1;
            if (definition.basePrice <= 0)
                return 0;
            double raw = (double)definition.basePrice * Math.Max(0f, trader.sellPriceMultiplier);
            if (durabilityRatio < 1f)
                raw *= Math.Max(0f, durabilityRatio);
            return Math.Max(0, ToInt(Math.Floor(raw + RoundingTolerance)));
        }

        // MVP 마무리 F3 수리비 (MF-Q13, TODO(GDD-?)): 내구도 있고 100% 미만 → max(1, 올림(basePrice × repairCostRatio × (1 − 비율))).
        // 내구도 없음·100%·null → -1(수리 불필요). 장착 탄창은 수리 대상이 아니다. basePrice ≤ 0이면 최소 1.
        // 경제: repairCostRatio ≥ sellPriceMultiplier면 수리 후 되팔기로 탄피가 늘지 않는다(TraderConfigSO.OnValidate가 지킨다).
        public static int RepairCost(ItemInstance item, TraderConfigSO trader)
        {
            if (item == null || trader == null || !DurabilityRules.HasDurability(item))
                return -1;
            float ratio = DurabilityRules.Ratio(item);
            if (ratio >= 1f)
                return -1;
            double raw = (double)Math.Max(0, item.Definition.basePrice) * Math.Max(0f, trader.repairCostRatio) * (1.0 - ratio);
            return Math.Max(1, ToInt(Math.Ceiling(raw - RoundingTolerance)));
        }

        private static int ToInt(double value) => value >= int.MaxValue ? int.MaxValue : (int)value;

        private static int SafeAdd(int a, int b) => (long)a + b >= int.MaxValue ? int.MaxValue : a + b;
    }
}
