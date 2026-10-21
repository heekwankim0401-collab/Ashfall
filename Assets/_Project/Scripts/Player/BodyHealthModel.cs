using System;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Player
{
    // SYS-HP-01 부위별 체력(§4.2)과 STAT-BLEED / STAT-HBLEED / STAT-FRAC(§4.3).
    // 버스를 모른다. 바뀌면 C# 이벤트만 낸다 (PlayerHealth가 버스로 옮긴다).
    // 규칙 해석은 설계 §4.1 (CHANGELOG_GDD "M2 설계 결정").
    public sealed class BodyHealthModel : IBodyStatusView
    {
        private static readonly int PartCount = Enum.GetValues(typeof(BodyPart)).Length;
        private static readonly int EffectCount = Enum.GetValues(typeof(StatusEffect)).Length;

        private readonly PlayerConfigSO config;
        private readonly System.Random random;
        private readonly float[] hp;
        private readonly bool[] status;          // [part * EffectCount + effect]
        private readonly HealthTier[] lastTiers;
        private bool diedRaised;

        public bool IsDead { get; private set; }
        public BodyPart? DeathCause { get; private set; }
        public ConditionModifiers Modifiers { get; private set; } = ConditionModifiers.Healthy;

        // 등급·상태이상·사망이 바뀔 때만 (→ PlayerBodyStatusChanged). 출혈 틱마다 내지 않는다.
        public event Action StatusChanged;
        // 이동·조준 패널티가 바뀔 때 (→ PlayerConditionChanged)
        public event Action ModifiersChanged;
        // 머리·흉부가 0이 됨 (→ PlayerDied)
        public event Action<BodyPart> Died;

        public BodyHealthModel(PlayerConfigSO config, System.Random random)
        {
            this.config = config ? config : throw new ArgumentNullException(nameof(config));
            this.random = random ?? new System.Random();
            hp = new float[PartCount];
            status = new bool[PartCount * EffectCount];
            lastTiers = new HealthTier[PartCount];
            for (int i = 0; i < PartCount; i++)
                hp[i] = config.MaxHp((BodyPart)i);
        }

        public float GetHp(BodyPart part) => hp[(int)part];
        public float GetMaxHp(BodyPart part) => config.MaxHp(part);
        public bool IsDestroyed(BodyPart part) => hp[(int)part] <= 0f;

        // RULE-05: 수치 대신 등급. 경계는 HP 비율 (TODO(GDD-?) 임시값).
        public HealthTier GetTier(BodyPart part)
        {
            if (IsDestroyed(part))
                return HealthTier.Destroyed;
            float max = GetMaxHp(part);
            float ratio = max > 0f ? hp[(int)part] / max : 0f;
            if (ratio < config.tierCriticalBelow)
                return HealthTier.Critical;
            if (ratio < config.tierInjuredBelow)
                return HealthTier.Injured;
            return HealthTier.Healthy;
        }

        public bool HasStatus(BodyPart part, StatusEffect effect) => status[Index(part, effect)];

        // 피격 한 번: 복부 파괴 배율 → 피해(분산 포함) → 상태이상 굴림.
        public void ApplyHit(BodyPart part, float damage)
        {
            if (IsDead || damage <= 0f)
                return;

            float amount = IsDestroyed(BodyPart.Stomach) ? damage * config.stomachDestroyedDamageMultiplier : damage;
            bool statusChanged = DamageInternal(part, amount);
            if (!IsDead)
                statusChanged |= RollStatus(part, damage);
            Refresh(statusChanged);
        }

        // 피해만 (분산 규칙 포함, 복부 배율·상태이상 굴림 없음).
        public void ApplyDamage(BodyPart part, float damage)
        {
            if (IsDead || damage <= 0f)
                return;
            Refresh(DamageInternal(part, damage));
        }

        // 출혈·중출혈 지속 피해. 파괴 부위면 분산 규칙 적용, 복부 배율은 미적용 (TODO(GDD-?)).
        public void Tick(float dt)
        {
            if (IsDead || dt <= 0f)
                return;

            bool any = false;
            for (int i = 0; i < PartCount && !IsDead; i++)
            {
                var part = (BodyPart)i;
                float rate = 0f;
                if (HasStatus(part, StatusEffect.Bleed)) rate += config.bleedHpPerSec;
                if (HasStatus(part, StatusEffect.HeavyBleed)) rate += config.heavyBleedHpPerSec;
                if (rate > 0f)
                {
                    DamageInternal(part, rate * dt);
                    any = true;
                }
            }
            if (any)
                Refresh(false);
        }

        // 골절은 팔·다리만 (TODO(GDD-?)). 이미 있으면 그대로.
        public void AddStatus(BodyPart part, StatusEffect effect)
        {
            if (IsDead || !CanHave(part, effect) || HasStatus(part, effect))
                return;
            status[Index(part, effect)] = true;
            Refresh(true);
        }

        public bool RemoveStatus(BodyPart part, StatusEffect effect)
        {
            if (!HasStatus(part, effect))
                return false;
            status[Index(part, effect)] = false;
            Refresh(true);
            return true;
        }

        // 파괴 부위는 회복되지 않는다 (수술 없음, TODO(GDD-?)).
        public void Heal(BodyPart part, float amount)
        {
            if (IsDead || amount <= 0f || IsDestroyed(part))
                return;
            hp[(int)part] = Mathf.Min(GetMaxHp(part), hp[(int)part] + amount);
            Refresh(false);
        }

        // ── 내부 ─────────────────────────────────────────────

        // 반환: 사망으로 상태가 바뀌었는지 (등급 변화는 Refresh가 따로 본다)
        private bool DamageInternal(BodyPart part, float amount)
        {
            if (!IsDestroyed(part))
            {
                // 0 아래로 넘친 초과분은 버린다 (TODO(GDD-?) 해석).
                hp[(int)part] = Mathf.Max(0f, hp[(int)part] - amount);
                return CheckDeath(part);
            }

            // §4.2 파괴된 부위가 받은 피해의 70%를 흉부·복부 중 살아 있는 곳에 균등 분산.
            float spread = amount * config.destroyedPartRedistribution;
            bool thoraxAlive = !IsDestroyed(BodyPart.Thorax);
            bool stomachAlive = !IsDestroyed(BodyPart.Stomach);
            int targets = (thoraxAlive ? 1 : 0) + (stomachAlive ? 1 : 0);
            if (targets == 0 || spread <= 0f)
                return false;

            float each = spread / targets;
            bool died = false;
            if (stomachAlive)
            {
                hp[(int)BodyPart.Stomach] = Mathf.Max(0f, hp[(int)BodyPart.Stomach] - each);
            }
            if (thoraxAlive)
            {
                hp[(int)BodyPart.Thorax] = Mathf.Max(0f, hp[(int)BodyPart.Thorax] - each);
                died = CheckDeath(BodyPart.Thorax);
            }
            return died;
        }

        // 머리·흉부 0 → 즉사 (§4.2)
        private bool CheckDeath(BodyPart part)
        {
            if (IsDead || (part != BodyPart.Head && part != BodyPart.Thorax) || !IsDestroyed(part))
                return false;
            IsDead = true;
            DeathCause = part;
            return true;
        }

        // 상태이상 굴림 (TODO(GDD-?): §4.3에 발생 조건이 없어 임시 규칙).
        // 중출혈을 먼저 굴리고, 안 나오면 출혈. 골절은 팔·다리만 따로 굴린다.
        private bool RollStatus(BodyPart part, float damage)
        {
            bool changed = false;
            if (damage >= config.bleedMinDamage)
            {
                if (random.NextDouble() < config.heavyBleedChance)
                    changed |= SetStatus(part, StatusEffect.HeavyBleed);
                else if (random.NextDouble() < config.bleedChance)
                    changed |= SetStatus(part, StatusEffect.Bleed);
            }
            if (IsLimb(part) && damage >= config.fractureMinDamage && random.NextDouble() < config.fractureChance)
                changed |= SetStatus(part, StatusEffect.Fracture);
            return changed;
        }

        private bool SetStatus(BodyPart part, StatusEffect effect)
        {
            if (!CanHave(part, effect) || HasStatus(part, effect))
                return false;
            status[Index(part, effect)] = true;
            return true;
        }

        // 등급·패널티를 다시 계산하고 바뀐 것만 알린다.
        private void Refresh(bool statusChanged)
        {
            for (int i = 0; i < PartCount; i++)
            {
                HealthTier tier = GetTier((BodyPart)i);
                if (tier != lastTiers[i])
                {
                    lastTiers[i] = tier;
                    statusChanged = true;
                }
            }

            ConditionModifiers modifiers = ComputeModifiers();
            bool modifiersChanged = modifiers != Modifiers;
            Modifiers = modifiers;

            if (statusChanged)
                StatusChanged?.Invoke();
            if (modifiersChanged)
                ModifiersChanged?.Invoke();
            if (IsDead && DeathCause.HasValue && !diedRaised)
            {
                diedRaised = true;
                Died?.Invoke(DeathCause.Value);
            }
        }

        // 다리: 두 다리 중 가장 나쁜 값 하나 (곱하지 않음). 파괴된 다리가 있으면 질주 불가.
        // 팔: 1 + Σ(파괴 +armDestroyedAimSwayAdd, 아니면 골절 +fractureArmAimSwayAdd).
        private ConditionModifiers ComputeModifiers()
        {
            float move = Mathf.Min(LegMultiplier(BodyPart.LeftLeg), LegMultiplier(BodyPart.RightLeg));
            bool sprint = !IsDestroyed(BodyPart.LeftLeg) && !IsDestroyed(BodyPart.RightLeg);
            float sway = 1f + ArmSwayAdd(BodyPart.LeftArm) + ArmSwayAdd(BodyPart.RightArm);
            return new ConditionModifiers(move, sprint, sway);
        }

        private float LegMultiplier(BodyPart leg)
        {
            if (IsDestroyed(leg))
                return config.legDestroyedSpeedMultiplier;
            if (HasStatus(leg, StatusEffect.Fracture))
                return config.fractureLegSpeedMultiplier;
            return 1f;
        }

        private float ArmSwayAdd(BodyPart arm)
        {
            if (IsDestroyed(arm))
                return config.armDestroyedAimSwayAdd;
            if (HasStatus(arm, StatusEffect.Fracture))
                return config.fractureArmAimSwayAdd;
            return 0f;
        }

        private static bool IsLimb(BodyPart part) => part >= BodyPart.LeftArm;
        private static bool CanHave(BodyPart part, StatusEffect effect) => effect != StatusEffect.Fracture || IsLimb(part);
        private static int Index(BodyPart part, StatusEffect effect) => (int)part * EffectCount + (int)effect;
    }
}
