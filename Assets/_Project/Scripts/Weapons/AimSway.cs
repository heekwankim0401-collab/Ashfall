using UnityEngine;

namespace Ashfall.Weapons
{
    // 조준 흔들림 (설계 D12). 정상 상태 기본값 × 체력 배율(§4.2 팔 파괴 +100%, §4.3 골절).
    public static class AimSway
    {
        // 펄린 노이즈 축마다 다른 줄에서 읽는다 (시드별 다른 무늬). 값 자체는 튜닝 수치가 아니다.
        private const float SeedStride = 17.31f;
        private const float AxisOffset = 101.7f;

        // (yawDeg, pitchDeg). |값| ≤ baseDeg × multiplier
        public static Vector2 Offset(float time, float baseDeg, float multiplier, float frequencyHz, int seed)
        {
            float amplitude = baseDeg * multiplier;
            if (amplitude <= 0f)
                return Vector2.zero;

            float t = time * frequencyHz;
            float row = seed * SeedStride;
            float yaw = Mathf.Clamp(Mathf.PerlinNoise(t, row) * 2f - 1f, -1f, 1f);
            float pitch = Mathf.Clamp(Mathf.PerlinNoise(row + AxisOffset, t) * 2f - 1f, -1f, 1f);
            return new Vector2(yaw * amplitude, pitch * amplitude);
        }
    }
}
