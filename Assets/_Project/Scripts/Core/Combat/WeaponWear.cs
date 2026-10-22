using System;

namespace Ashfall.Core
{
    // WeaponModel의 마모·고장 옵션 (M7 설계 §1.3). 플레이어 무기만 켠다 — 적 무기는 None (TODO(GDD-?) Q18).
    // 수치(임계·확률)는 WeaponHandlingConfigSO(Weapons)에서 컨트롤러가 채운다. 발당 감소는 무기 정의 durabilityLossPerShot.
    public readonly struct WeaponWear
    {
        public readonly bool Enabled;
        public readonly float Threshold;        // §5.1 50 (%)
        public readonly float ChancePerPoint;   // §5.1 0.001 (0.1%/점)
        public readonly Random Random;

        public static WeaponWear None => default;

        public WeaponWear(float threshold, float chancePerPoint, Random random)
        {
            Enabled = true;
            Threshold = threshold;
            ChancePerPoint = chancePerPoint;
            Random = random ?? throw new ArgumentNullException(nameof(random));
        }
    }
}
