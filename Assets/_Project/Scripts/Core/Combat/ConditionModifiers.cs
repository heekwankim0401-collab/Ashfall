using System;

namespace Ashfall.Core
{
    // 체력 상태(§4.2 파괴, §4.3 골절)가 이동·조준에 주는 패널티.
    public readonly struct ConditionModifiers : IEquatable<ConditionModifiers>
    {
        public readonly float MoveSpeedMultiplier;   // 1 = 정상
        public readonly bool SprintAllowed;
        public readonly float AimSwayMultiplier;     // 1 = 정상, 팔 파괴 1개당 +1 (§4.2 +100%)

        public ConditionModifiers(float move, bool sprint, float sway)
        {
            MoveSpeedMultiplier = move;
            SprintAllowed = sprint;
            AimSwayMultiplier = sway;
        }

        public static ConditionModifiers Healthy => new ConditionModifiers(1f, true, 1f);

        // 변경 감지용 정확 비교 (같은 계산에서 나온 값끼리 비교한다)
        public bool Equals(ConditionModifiers other) =>
            MoveSpeedMultiplier.Equals(other.MoveSpeedMultiplier)
            && SprintAllowed == other.SprintAllowed
            && AimSwayMultiplier.Equals(other.AimSwayMultiplier);

        public override bool Equals(object obj) => obj is ConditionModifiers other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(MoveSpeedMultiplier, SprintAllowed, AimSwayMultiplier);
        public static bool operator ==(ConditionModifiers a, ConditionModifiers b) => a.Equals(b);
        public static bool operator !=(ConditionModifiers a, ConditionModifiers b) => !a.Equals(b);

        public override string ToString() =>
            $"(move ×{MoveSpeedMultiplier}, sprint {SprintAllowed}, sway ×{AimSwayMultiplier})";
    }
}
