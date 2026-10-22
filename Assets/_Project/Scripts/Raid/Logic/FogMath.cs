using UnityEngine;

namespace Ashfall.Raid
{
    // Exponential² 거리 안개 ↔ 가시거리 변환 (M6 설계 §1.3·§1.5, Q3·Q26). 순수 함수.
    // 투과율 T(d) = exp(-(density × d)²). 가시거리 V = 투과율이 threshold로 떨어지는 거리
    //   → density = √(−ln threshold) / V,  V = √(−ln threshold) / density.
    // threshold는 AtmosphereConfigSO.fogVisibilityThreshold(0.05, TODO(GDD-?)).
    public static class FogMath
    {
        // 가시거리 0 이하에서 돌려주는 밀도 상한 (계산 보호용, 게임 수치 아님 — 1m 가시거리보다 짙다)
        public const float MaxDensity = 10f;

        // threshold가 0·1 이상·NaN이면 로그가 무한·0이 된다. 그 경계를 피하는 계산 보호 범위.
        private const float MinThreshold = 1e-6f;
        private const float MaxThreshold = 0.999999f;

        // 가시거리(m) → Exp² 밀도. ∞·NaN → 0(안개 없음), ≤ 0 → MaxDensity.
        public static float DensityForVisibility(float visibilityM, float threshold)
        {
            if (float.IsNaN(visibilityM) || float.IsPositiveInfinity(visibilityM))
                return 0f;
            if (visibilityM <= 0f)
                return MaxDensity;
            return Mathf.Min(MaxDensity, Coefficient(threshold) / visibilityM);
        }

        // Exp² 밀도 → 가시거리(m). ≤ 0·NaN → ∞.
        public static float VisibilityForDensity(float density, float threshold)
        {
            if (!(density > 0f))
                return float.PositiveInfinity;
            return Coefficient(threshold) / density;
        }

        // 거리 d에서 남는 투과율 (Exp², Unity 안개 모델과 같음)
        public static float Transmittance(float distanceM, float density)
        {
            float x = density * Mathf.Max(0f, distanceM);
            return Mathf.Exp(-x * x);
        }

        private static float Coefficient(float threshold)
        {
            float t = float.IsNaN(threshold) ? MaxThreshold : Mathf.Clamp(threshold, MinThreshold, MaxThreshold);
            return Mathf.Sqrt(-Mathf.Log(t));
        }
    }
}
