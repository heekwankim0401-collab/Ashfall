using Ashfall.Core;

namespace Ashfall.UI
{
    // MVP 마무리 F3 내구도 대략 등급 (§5.1, MF-Q10·Q11). 숫자 대신 세 단계 문구만 보인다(RULE-04).
    public enum ItemConditionTier
    {
        Good,      // 양호
        Worn,      // 낡음
        Critical,  // 위험
    }

    // 순수 C#. 경계는 HudConfigSO(conditionWornBelow·conditionCriticalBelow, TODO(GDD-?) MF-Q10).
    // 경계 값 자체는 위 등급에 속한다: 비율 == wornBelow → 양호, 비율 == criticalBelow → 낡음.
    public static class ItemConditionRules
    {
        public static ItemConditionTier TierFor(float ratio, float wornBelow, float criticalBelow)
        {
            if (ratio < criticalBelow)
                return ItemConditionTier.Critical;
            if (ratio < wornBelow)
                return ItemConditionTier.Worn;
            return ItemConditionTier.Good;
        }

        public static ItemConditionTier TierFor(float ratio, HudConfigSO config) =>
            TierFor(ratio, config.conditionWornBelow, config.conditionCriticalBelow);

        // 내구도 있는 아이템(IDurableItem, 최대 > 0)만 등급이 있다
        public static bool TryGetTier(ItemInstance item, HudConfigSO config, out ItemConditionTier tier)
        {
            tier = ItemConditionTier.Good;
            if (config == null || !DurabilityRules.HasDurability(item))
                return false;
            tier = TierFor(DurabilityRules.Ratio(item), config);
            return true;
        }
    }
}
