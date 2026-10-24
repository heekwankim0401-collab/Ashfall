using System;

namespace Ashfall.Core
{
    // MVP 마무리 F4 (§5.3, MF-Q16~Q24): 런타임 방어구 하나 (정의 + 내구도). 무게는 정의 그대로.
    // 내구도 = 절대값 0~maxDurability (무기의 0~100 %와 다르다). 생성 = 최대(상인·상자·시체·키트 모두 ItemFactory 경로).
    // 최대가 0 이하(방독면 GEAR-GSM-01 등)면 내구도도 0 — DurabilityRules.HasDurability = false라 표시·수리·가격에 영향 없고
    // ArmorRules.Protects도 고르지 않는다(내구도 0 = 막지 못함, MF-Q18·Q23).
    // IDurableItem(F3)이라 대략 등급 표시·상인 수리·매도가 비율이 그대로 적용된다(MF-Q15).
    public sealed class ArmorItem : ItemInstance, IDurableItem
    {
        private float durability;

        public new ArmorDefinitionSO Definition { get; }

        public ArmorItem(ArmorDefinitionSO definition) : base(definition)
        {
            Definition = definition;
            durability = MaxDurability;
        }

        // 최대가 음수·NaN이면 0으로 본다
        public float MaxDurability => Definition.maxDurability > 0f ? Definition.maxDurability : 0f;

        // 0~MaxDurability로 자른다. NaN은 최대 (WeaponItem과 같은 규칙).
        public float Durability
        {
            get => durability;
            set => durability = float.IsNaN(value) ? MaxDurability : Math.Clamp(value, 0f, MaxDurability);
        }
    }
}
