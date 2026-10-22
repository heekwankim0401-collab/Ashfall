using System;

namespace Ashfall.Core
{
    // §5.1 고장: 내구도 < 임계(50%)면 발당 (임계 − 내구도) × 확률/점(0.1%). 순수 규칙 (M7 설계 §1.3).
    public static class JamRules
    {
        // 0~1. 내구도가 임계 이상이면 0.
        public static float Chance(float durability, float threshold, float chancePerPoint)
        {
            if (float.IsNaN(durability) || chancePerPoint <= 0f)
                return 0f;
            float chance = Math.Max(0f, threshold - durability) * chancePerPoint;
            return Math.Min(1f, chance);
        }

        // 확률이 0 이하면 난수를 뽑지 않는다(고장 날 수 없는 무기는 난수열에 영향 없음).
        public static bool Roll(float chance, Random random)
        {
            if (chance <= 0f)
                return false;
            if (chance >= 1f)
                return true;
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            return random.NextDouble() < chance;
        }
    }
}
