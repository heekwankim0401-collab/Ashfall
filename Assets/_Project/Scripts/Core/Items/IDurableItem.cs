using System;

namespace Ashfall.Core
{
    // MVP 마무리 F3 (§5.1, MF-Q10~Q15): 내구도가 있는 런타임 아이템 공통 접근. WeaponItem(F3)·ArmorItem(F4)이 구현한다.
    // UI(대략 등급)·Hideout(매도가·수리)이 구체 타입을 몰라도 같은 규칙을 쓴다. 숫자는 UI에 보이지 않는다(RULE-04).
    public interface IDurableItem
    {
        // 0~MaxDurability. 구현이 범위로 자른다.
        float Durability { get; set; }
        // 0 이하 = 내구도 없음으로 본다 (DurabilityRules.HasDurability)
        float MaxDurability { get; }
    }

    // 내구도 비율·수리 공통 규칙. 순수 C#.
    public static class DurabilityRules
    {
        // IDurableItem이고 최대 > 0
        public static bool HasDurability(ItemInstance item) =>
            item is IDurableItem durable && durable.MaxDurability > 0f;

        // 내구도 있음 → clamp01(현재 / 최대), 없음·null → 1 (가격·표시에 영향 없음). NaN → 1.
        public static float Ratio(ItemInstance item)
        {
            if (!(item is IDurableItem durable) || !(durable.MaxDurability > 0f))
                return 1f;
            float ratio = durable.Durability / durable.MaxDurability;
            return float.IsNaN(ratio) ? 1f : Math.Clamp(ratio, 0f, 1f);
        }

        // 최대로 되돌린다(수리, MF-Q13 — 최대 내구도 감소 없음). 내구도 없는 아이템이면 false.
        public static bool RestoreFull(ItemInstance item)
        {
            if (!HasDurability(item))
                return false;
            var durable = (IDurableItem)item;
            durable.Durability = durable.MaxDurability;
            return true;
        }
    }
}
