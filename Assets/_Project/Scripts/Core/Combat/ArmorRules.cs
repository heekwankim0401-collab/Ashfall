using System;

namespace Ashfall.Core
{
    // MVP 마무리 F4 §5.3 방어구 수치 묶음. 값은 SO가 만든다 — 플레이어 PlayerConfigSO, 적 AiConfigSO(같은 4필드, MF-Q16 B·Q22).
    public readonly struct ArmorRuleParams
    {
        // §5.3 관통확률 = clamp01(BasePenetrationChance + (탄관통력 − 방어등급) × PenetrationPerClassStep)
        public readonly float BasePenetrationChance;
        public readonly float PenetrationPerClassStep;
        // §5.3 비관통 시 몸이 받는 피해 비율 (둔탁 피해 30%)
        public readonly float BluntDamageRatio;
        // 비관통 시 방어구 내구도 감소 = 원피해 × 이 값 (TODO(GDD-?) MF-Q17)
        public readonly float DurabilityLossPerDamage;

        public ArmorRuleParams(float basePenetrationChance, float penetrationPerClassStep, float bluntDamageRatio, float durabilityLossPerDamage)
        {
            BasePenetrationChance = basePenetrationChance;
            PenetrationPerClassStep = penetrationPerClassStep;
            BluntDamageRatio = bluntDamageRatio;
            DurabilityLossPerDamage = durabilityLossPerDamage;
        }
    }

    // 방어구 한 번의 판정 결과. 관통 = 피해 그대로·마모 없음(MF-Q17), 비관통 = 피해 × 둔탁 비율·마모.
    public readonly struct ArmorHitResult
    {
        public readonly bool Penetrated;
        public readonly float BodyDamage;
        public readonly float DurabilityLoss;

        public ArmorHitResult(bool penetrated, float bodyDamage, float durabilityLoss)
        {
            Penetrated = penetrated;
            BodyDamage = bodyDamage;
            DurabilityLoss = durabilityLoss;
        }
    }

    // §5.3 방어구 관통·둔탁 피해 순수 판정 (MF-Q16~Q24). 플레이어(PlayerHealth)와 적(EnemyArmorModel)이 같은 함수를 쓴다(§7 "동일한 피해 공식").
    // 난수는 호출자가 주입한다(방어구 전용 System.Random — 상태이상·AI 난수 순서를 밀지 않게).
    // 확률이 정확히 0 또는 1이면 난수를 뽑지 않는다(결정적 경우 난수 순서 불변). 할당 없음.
    public static class ArmorRules
    {
        // clamp01. NaN(설정 오류)은 0 = 관통 안 됨으로 본다.
        public static float PenetrationChance(in ArmorRuleParams p, int penetration, int armorClass)
        {
            float chance = p.BasePenetrationChance + (penetration - armorClass) * p.PenetrationPerClassStep;
            if (float.IsNaN(chance))
                return 0f;
            return Math.Clamp(chance, 0f, 1f);
        }

        // 이 방어구가 부위를 막는가 (MF-Q18·Q23): 등급 ≥ 1, covers ∋ part, 내구도 > 0. 방독면(등급 0·covers 빈)·null은 false.
        public static bool Protects(ArmorItem armor, BodyPart part)
        {
            if (armor == null || armor.Definition.armorClass < 1 || !(armor.Durability > 0f))
                return false;
            BodyPart[] covers = armor.Definition.covers;
            if (covers == null)
                return false;
            for (int i = 0; i < covers.Length; i++)
            {
                if (covers[i] == part)
                    return true;
            }
            return false;
        }

        // 헬멧 슬롯 → 방탄복 슬롯 순서로 부위를 막는 방어구. ArmorItem이 아닌 인스턴스(구 경로 new ItemInstance(armorDef))는 고르지 않는다.
        public static ArmorItem FindCovering(ItemInstance helmet, ItemInstance bodyArmor, BodyPart part)
        {
            if (helmet is ArmorItem h && Protects(h, part))
                return h;
            if (bodyArmor is ArmorItem b && Protects(b, part))
                return b;
            return null;
        }

        // 한 번의 피격 판정. 피해가 0 이하·NaN이면 비관통·피해 0·마모 0(난수 없음).
        // 관통 여부 = 확률 1 → 관통, 0 → 비관통(둘 다 난수 미소비), 그 사이 → random.NextDouble() < 확률.
        public static ArmorHitResult Resolve(in ArmorRuleParams p, int penetration, int armorClass, float damage, Random random)
        {
            if (!(damage > 0f))
                return new ArmorHitResult(false, 0f, 0f);

            float chance = PenetrationChance(p, penetration, armorClass);
            bool penetrated;
            if (chance >= 1f)
                penetrated = true;
            else if (chance <= 0f)
                penetrated = false;
            else
            {
                if (random == null)
                    throw new ArgumentNullException(nameof(random));
                penetrated = random.NextDouble() < chance;
            }

            if (penetrated)
                return new ArmorHitResult(true, damage, 0f);
            float blunt = Math.Max(0f, damage * p.BluntDamageRatio);
            float loss = Math.Max(0f, damage * p.DurabilityLossPerDamage);
            return new ArmorHitResult(false, blunt, loss);
        }

        // 고른 방어구로 판정하고 내구도를 깎는다(비관통만, 0 아래로 내려가지 않음). 방어구 null = 막는 것 없음 → 관통·피해 그대로(난수 없음).
        // 플레이어(PlayerHealth)·적(EnemyArmorModel)이 FindCovering 다음에 부른다.
        public static ArmorHitResult Absorb(ArmorItem armor, in ArmorRuleParams p, int penetration, float damage, Random random)
        {
            if (armor == null)
                return new ArmorHitResult(true, damage, 0f);
            ArmorHitResult result = Resolve(p, penetration, armor.Definition.armorClass, damage, random);
            if (result.DurabilityLoss > 0f)
                armor.Durability -= result.DurabilityLoss;
            return result;
        }
    }
}
