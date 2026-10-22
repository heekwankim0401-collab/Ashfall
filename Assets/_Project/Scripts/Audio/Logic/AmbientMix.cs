using UnityEngine;
using Random = System.Random;

namespace Ashfall.Audio
{
    // 환경음 음량·배치 (§11 환경음, 설계 §1.4). 순수.
    public static class AmbientMix
    {
        // 바람 = lerp(windVolume, windStormVolume, 폭풍 강도)
        public static float WindVolume(AudioConfigSO cfg, float stormIntensity)
        {
            if (cfg == null)
                return 0f;
            return Mathf.Lerp(cfg.windVolume, cfg.windStormVolume, Clamp01(stormIntensity));
        }

        // 폭풍 루프 = 강도 × 최대
        public static float StormVolume(AudioConfigSO cfg, float stormIntensity)
        {
            if (cfg == null)
                return 0f;
            return Clamp01(stormIntensity) * Mathf.Max(0f, cfg.stormLoopMaxVolume);
        }

        // 폭풍 루프를 틀어 둘 단계 (Rising·Peak·Falling)
        public static bool StormLoopWanted(Ashfall.Core.AshStormPhase phase) =>
            phase == Ashfall.Core.AshStormPhase.Rising || phase == Ashfall.Core.AshStormPhase.Peak ||
            phase == Ashfall.Core.AshStormPhase.Falling;

        // center에서 수평 거리 [min, max] 균등, 방향 균등인 점 (높이는 center와 같다). 금속 붕괴 위치.
        public static Vector3 RandomAround(Vector3 center, Vector2 distanceRangeM, Random random)
        {
            float a = Mathf.Max(0f, Mathf.Min(distanceRangeM.x, distanceRangeM.y));
            float b = Mathf.Max(a, Mathf.Max(distanceRangeM.x, distanceRangeM.y));
            float distance = a + (float)random.NextDouble() * (b - a);
            float angle = (float)(random.NextDouble() * 2.0 * System.Math.PI);
            return center + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
        }

        private static float Clamp01(float v) => float.IsNaN(v) ? 0f : Mathf.Clamp01(v);
    }
}
