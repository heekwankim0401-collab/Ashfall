using UnityEngine;

namespace Ashfall.Raid
{
    // 재 폭풍 표시 순수 계산 (§8.3, M6 설계 §1.5, M6 리뷰 중간 1·낮음 2). AshStormPresenter가 쓴다.
    //
    // 하늘 (리뷰 중간 1 — Peak에서 30m 밖 윤곽이 하늘을 배경으로 보이지 않게, Q26):
    //   안개(Exp²)는 지오메트리에만 걸리고 하늘에는 안 걸린다. 그래서 강도에 따라 하늘을 폭풍 안개 색으로 수렴시킨다.
    //   - 강도 0 ~ 교체 강도(skyFogSwapIntensity): 생성 하늘(파노라마)이 틴트 → skyTintToken, 노출 → skyExposureAtPeak로 어두워진다.
    //   - 교체 강도 초과: 하늘 복제본의 텍스처를 흰색으로 바꾸고 틴트로 "지금 안개 색 × 밝기"를 그린다(밝기 0 → 1).
    //     교체 순간 단색 하늘은 검정(밝기 0)이고 생성 하늘은 거의 검정(노출 배율)이라 이음매가 작다. 최대 강도에서 하늘 전체 = 안개 색.
    //   - 교체 강도 ≥ 1이면 교체하지 않는다(M6 2c 동작: 틴트·노출만).
    // 재 파티클 (리뷰 낮음 2):
    //   정상 상태 입자 수 = 초당 방출 × 수명 이다(시뮬레이션 속도 배율은 방출·수명을 같이 바꿔 수에 영향이 없다).
    //   설정한 방출 배율이 폭풍 상한(ashMaxParticlesStorm)이 담을 수 있는 것보다 크면 상한에 맞춰 자른다 —
    //   넘치는 방출은 상한에 막혀 버려질 뿐이고, Falling에서 상한을 줄일 때 정리되는 입자만 늘린다.
    public static class AshStormVisualMath
    {
        // 내장 Skybox/Panoramic 셰이더의 unity_ColorSpaceDouble (엔진 상수 — 튜닝 수치 아님).
        // 셰이더 식: 색 = 텍스처 × _Tint × unity_ColorSpaceDouble × _Exposure
        public const float ColorSpaceDoubleLinear = 4.59479380f;
        public const float ColorSpaceDoubleGamma = 2f;

        // 이 강도에서 하늘이 안개 색 단색인가. 교체 강도 ≥ 1이면 항상 false.
        public static bool UsesFogSky(float intensity, float swapIntensity)
        {
            if (float.IsNaN(intensity) || float.IsNaN(swapIntensity) || swapIntensity >= 1f)
                return false;
            return intensity > Mathf.Max(0f, swapIntensity);
        }

        // 생성 하늘 단계의 진행도 0 → 1 (강도 0 → 교체 강도). 교체가 없으면 강도 그대로.
        public static float TexturedSkyProgress(float intensity, float swapIntensity)
        {
            intensity = Sanitize01(intensity);
            if (float.IsNaN(swapIntensity) || swapIntensity >= 1f)
                return intensity;
            if (swapIntensity <= 0f)
                return 1f;
            return Mathf.Clamp01(intensity / swapIntensity);
        }

        // 단색 안개 하늘의 밝기 0 → 1 (교체 강도 → 최대 강도). 교체가 없으면 0.
        public static float FogSkyBrightness(float intensity, float swapIntensity)
        {
            intensity = Sanitize01(intensity);
            if (float.IsNaN(swapIntensity) || swapIntensity >= 1f)
                return 0f;
            float swap = Mathf.Max(0f, swapIntensity);
            return Mathf.Clamp01((intensity - swap) / (1f - swap));
        }

        // 흰 텍스처 + 노출 1인 Panoramic 하늘이 "목표 색 × 밝기"로 그려지게 하는 _Tint (sRGB — Material.SetColor에 넣는 값).
        // Linear 프로젝트: SetColor가 sRGB → 선형으로 바꾸므로 선형 목표 / ColorSpaceDouble을 다시 sRGB로 돌려 넣는다(밝기는 선형 배율).
        // Gamma 프로젝트: 변환 없음, 목표 × 밝기 / 2.
        public static Color PanoramicTintFor(Color targetSrgb, float brightness, bool linearColorSpace)
        {
            brightness = Sanitize01(brightness);
            if (linearColorSpace)
            {
                Color lin = targetSrgb.linear;
                float k = brightness / ColorSpaceDoubleLinear;
                return new Color(lin.r * k, lin.g * k, lin.b * k, 1f).gamma;
            }
            float g = brightness / ColorSpaceDoubleGamma;
            return new Color(targetSrgb.r * g, targetSrgb.g * g, targetSrgb.b * g, 1f);
        }

        // Panoramic 하늘이 셰이더 작업 공간(Linear 프로젝트 = 선형, Gamma = 감마)에서 내는 색. 텍스처 값도 같은 공간.
        // 검증용: 안개 색(Linear면 fogColor.linear)과 비교한다. _Exposure는 [Gamma] 속성이라 Linear에서 선형으로 바뀐다.
        public static Color PanoramicOutput(Color tintSrgb, float exposure, Color texture, bool linearColorSpace)
        {
            Color t;
            float k;
            if (linearColorSpace)
            {
                t = tintSrgb.linear;
                k = ColorSpaceDoubleLinear * Mathf.GammaToLinearSpace(Mathf.Max(0f, exposure));
            }
            else
            {
                t = tintSrgb;
                k = ColorSpaceDoubleGamma * Mathf.Max(0f, exposure);
            }
            return new Color(texture.r * t.r * k, texture.g * t.g * k, texture.b * t.b * k, 1f);
        }

        // 실제로 쓰는 재 방출 배율 = min(설정, max(1, 폭풍 상한 / (평시 초당 방출 × 수명))).
        // 설정이 1 이하이거나 평시 값이 없으면 설정 그대로.
        public static float AshEmissionMultiplier(float configured, float baseRatePerSec, float lifetimeSec, int stormMaxParticles)
        {
            if (float.IsNaN(configured))
                return 1f;
            float steady = baseRatePerSec * lifetimeSec;
            if (configured <= 1f || !(steady > 0f) || float.IsInfinity(steady) || stormMaxParticles <= 0)
                return configured;
            float capacity = stormMaxParticles / steady;
            return Mathf.Min(configured, Mathf.Max(1f, capacity));
        }

        private static float Sanitize01(float v) => float.IsNaN(v) ? 0f : Mathf.Clamp01(v);
    }
}
