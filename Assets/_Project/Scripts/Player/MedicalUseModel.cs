using System;
using Ashfall.Core;

namespace Ashfall.Player
{
    public enum MedicalStartResult
    {
        Started,
        NotNeeded,
        Busy,
        Dead,
        Depleted,   // 남은 사용 횟수 0
    }

    // §4.3 의약품 사용 (붕대·지혈대·부목). 사용 시간이 끝나면 효과를 적용한다.
    // 대상 부위는 자동 선택 (설계 D8). 사용 중 이동·사격 제한·취소 없음 (TODO(GDD-?)).
    // M7(7C-2a, Q42): 물·음식·해독제도 같은 흐름. 부위 대상이 없고 전신 효과가 필요하면 시작하고 대상 = 흉부(전신 표시).
    // 완료 시 수분·에너지 회복(SurvivalModel.Restore), 해독제 = 선량 0 + 최대 HP 배율 1(HP 회복 없음, Q36).
    // 생존 상태는 body.AttachSurvival로 붙인 것을 쓴다(없으면 전신 효과 불필요 = 기존 동작).
    public sealed class MedicalUseModel
    {
        // 전신 효과 아이템의 대상 표시 (MedicalUseStarted/Completed.Target, M7 설계 §1.5)
        public const BodyPart SystemicTarget = BodyPart.Thorax;

        private readonly BodyHealthModel body;

        public bool IsUsing => Current != null;
        public MedicalItem Current { get; private set; }
        public BodyPart Target { get; private set; }
        public float RemainingSec { get; private set; }

        public MedicalUseModel(BodyHealthModel body)
        {
            this.body = body ?? throw new ArgumentNullException(nameof(body));
        }

        public MedicalStartResult TryStart(MedicalItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (body.IsDead)
                return MedicalStartResult.Dead;
            if (IsUsing)
                return MedicalStartResult.Busy;
            if (item.UsesLeft <= 0)
                return MedicalStartResult.Depleted;

            BodyPart? target = FindTarget(body, item.Definition);
            if (!target.HasValue)
                return MedicalStartResult.NotNeeded;

            Current = item;
            Target = target.Value;
            RemainingSec = item.Definition.useTime;
            return MedicalStartResult.Started;
        }

        // 이번 Tick에 끝났으면 true. 효과: 대상 부위의 cures 제거 + healAmount 회복 + 전신 효과(M7).
        // 사용 중 사망하면 효과 없이 끝난다 (false).
        public bool Tick(float dt)
        {
            if (!IsUsing)
                return false;
            if (body.IsDead)
            {
                Clear();
                return false;
            }

            RemainingSec -= dt;
            if (RemainingSec > 0f)
                return false;

            MedicalDefinitionSO definition = Current.Definition;
            if (definition.cures != null)
                foreach (StatusEffect effect in definition.cures)
                    body.RemoveStatus(Target, effect);
            if (definition.healAmount > 0f)
                body.Heal(Target, definition.healAmount);
            ApplySystemic(body, definition);
            Clear();
            return true;
        }

        // 진행 중 사용을 효과 없이 버린다 (M4 리뷰 수정: 레이드 종료 시). 사용 중이 아니면 아무것도 안 한다. 아이템은 그대로.
        public void Cancel() => Clear();

        // cures 중 하나를 가진 부위를 BodyPart 순서로 첫 번째.
        // 없고 healAmount > 0이면 HP 비율이 가장 낮은 비파괴·비만충 부위.
        // M7: 그래도 없고 전신 효과가 필요하면(NeedsSystemic) 흉부(전신 표시). 없으면 null.
        public static BodyPart? FindTarget(BodyHealthModel body, MedicalDefinitionSO definition)
        {
            BodyPart? part = FindPartTarget(body, definition);
            if (part.HasValue)
                return part;
            return NeedsSystemic(body, definition) ? SystemicTarget : (BodyPart?)null;
        }

        // M7 전신 효과가 지금 필요한가 (Q42·Q36): 수분 회복 && 수분 < 최대, 에너지 회복 && 에너지 < 최대, 해독제 && 선량 > 0.
        public static bool NeedsSystemic(BodyHealthModel body, MedicalDefinitionSO definition)
        {
            SurvivalModel survival = body.Survival;
            if (survival != null)
            {
                if (definition.hydrationRestore > 0f && survival.Hydration < survival.HydrationMax)
                    return true;
                if (definition.energyRestore > 0f && survival.Energy < survival.EnergyMax)
                    return true;
            }
            return definition.curesRadiation && body.Radiation != null && body.Radiation.Dose > 0f;
        }

        // 완료 시 전신 효과. 생존 상태가 없으면 아무것도 안 한다.
        private static void ApplySystemic(BodyHealthModel body, MedicalDefinitionSO definition)
        {
            if (body.Survival != null && (definition.hydrationRestore > 0f || definition.energyRestore > 0f))
                body.Survival.Restore(definition.hydrationRestore, definition.energyRestore);
            if (definition.curesRadiation && body.Radiation != null)
            {
                body.Radiation.Reset();
                body.SyncRadiation();   // 최대 HP 배율 1, 등급 갱신
            }
        }

        private static BodyPart? FindPartTarget(BodyHealthModel body, MedicalDefinitionSO definition)
        {
            int partCount = Enum.GetValues(typeof(BodyPart)).Length;
            if (definition.cures != null)
            {
                for (int i = 0; i < partCount; i++)
                    foreach (StatusEffect effect in definition.cures)
                        if (body.HasStatus((BodyPart)i, effect))
                            return (BodyPart)i;
            }

            if (definition.healAmount <= 0f)
                return null;

            BodyPart? best = null;
            float bestRatio = float.MaxValue;
            for (int i = 0; i < partCount; i++)
            {
                var part = (BodyPart)i;
                float max = body.GetMaxHp(part);
                if (body.IsDestroyed(part) || max <= 0f || body.GetHp(part) >= max)
                    continue;
                float ratio = body.GetHp(part) / max;
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    best = part;
                }
            }
            return best;
        }

        private void Clear()
        {
            Current = null;
            RemainingSec = 0f;
        }
    }
}
