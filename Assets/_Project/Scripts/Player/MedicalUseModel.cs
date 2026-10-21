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
    public sealed class MedicalUseModel
    {
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

        // 이번 Tick에 끝났으면 true. 효과: 대상 부위의 cures 제거 + healAmount 회복.
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
            foreach (StatusEffect effect in definition.cures)
                body.RemoveStatus(Target, effect);
            if (definition.healAmount > 0f)
                body.Heal(Target, definition.healAmount);
            Clear();
            return true;
        }

        // 진행 중 사용을 효과 없이 버린다 (M4 리뷰 수정: 레이드 종료 시). 사용 중이 아니면 아무것도 안 한다. 아이템은 그대로.
        public void Cancel() => Clear();

        // cures 중 하나를 가진 부위를 BodyPart 순서로 첫 번째.
        // 없고 healAmount > 0이면 HP 비율이 가장 낮은 비파괴·비만충 부위. 없으면 null.
        public static BodyPart? FindTarget(BodyHealthModel body, MedicalDefinitionSO definition)
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
