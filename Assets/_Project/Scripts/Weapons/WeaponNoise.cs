using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Weapons
{
    // 사격 소음 (M3, §7 청각). 반경은 무기 SO shotNoiseRadiusM (TODO(GDD-?): GDD에 없는 임시값).
    // 펠릿 수와 상관없이 발당 1회. 반경 0 이하면 소음 없음.
    public static class WeaponNoise
    {
        public static bool TryCreateShot(WeaponDefinitionSO weapon, Vector3 position, out NoiseEmitted noise)
        {
            if (weapon == null || weapon.shotNoiseRadiusM <= 0f)
            {
                noise = default;
                return false;
            }
            noise = new NoiseEmitted(position, weapon.shotNoiseRadiusM, NoiseKind.Gunshot);
            return true;
        }
    }
}
