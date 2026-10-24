using System;
using Ashfall.Core;

namespace Ashfall.AI
{
    // MVP 마무리 F4 적 방어구 (§5.3, §7 "동일한 피해 공식", MF-Q16 B). 순수 C#, 적 1명당 하나.
    // - 유형(EnemyArchetypeSO.helmet·bodyArmor, null = 없음)마다 런타임 ArmorItem 1개씩 — 내구도 시작 = maxDurability, 비관통 시 감소, 0이면 막지 못함.
    // - 판정은 플레이어와 같은 Core ArmorRules. 3부위 매핑: 머리 → covers ∋ Head, 몸통 → covers ∋ Thorax 또는 Stomach, 다리 → 없음.
    // - 난수는 호출자가 주는 방어구 전용 System.Random(기존 AI 난수 순서 불변). 확률 0·1이면 뽑지 않는다.
    // - 시체 루트에 넣지 않는다(TODO(GDD-?) MF-Q16) — 이 인스턴스는 적 안에만 있다. 할당 없음(Absorb).
    public sealed class EnemyArmorModel
    {
        public ArmorItem Helmet { get; }
        public ArmorItem BodyArmor { get; }
        public bool HasArmor => Helmet != null || BodyArmor != null;

        public EnemyArmorModel(EnemyArchetypeSO archetype)
        {
            if (archetype == null)
                throw new ArgumentNullException(nameof(archetype));
            Helmet = archetype.helmet != null ? new ArmorItem(archetype.helmet) : null;
            BodyArmor = archetype.bodyArmor != null ? new ArmorItem(archetype.bodyArmor) : null;
        }

        // 부위를 막는 방어구 (없음·내구도 0·등급 0 → null). 헬멧 슬롯 먼저.
        public ArmorItem FindCovering(EnemyHitRegion region)
        {
            switch (region)
            {
                case EnemyHitRegion.Head:
                    return ArmorRules.FindCovering(Helmet, BodyArmor, BodyPart.Head);
                case EnemyHitRegion.Body:
                    return ArmorRules.FindCovering(Helmet, BodyArmor, BodyPart.Thorax)
                           ?? ArmorRules.FindCovering(Helmet, BodyArmor, BodyPart.Stomach);
                default:
                    return null;   // 다리: 방어구 없음
            }
        }

        // 판정 + 내구도 감소. 결과 BodyDamage를 EnemyBodyModel.ApplyHit에 넘긴다. 막는 방어구가 없으면 피해 그대로(난수 없음).
        public ArmorHitResult Absorb(EnemyHitRegion region, int penetration, float damage, in ArmorRuleParams p, Random random) =>
            ArmorRules.Absorb(FindCovering(region), p, penetration, damage, random);
    }
}
