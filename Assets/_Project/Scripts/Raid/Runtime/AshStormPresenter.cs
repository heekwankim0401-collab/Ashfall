using Ashfall.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashfall.Raid
{
    // 재 폭풍 표시 (§8.3, M6 설계 §1.5). AshStormController가 매 프레임 Apply를 부르고, 값이 바뀐 프레임에만 실제로 쓴다.
    // - 안개: 밀도 = max(씬 원래 밀도, FogMath(VisibilityM)), 색 = lerp(원래, 폭풍 안개 토큰, 강도). 폭풍 중에는 Exp²로 켠다.
    // - 하늘: 공유 머티리얼 에셋을 바꾸지 않는다. 강도 > 0이면 복제본을 RenderSettings.skybox에 넣어 바꾸고,
    //         강도 0이 되거나 파괴될 때 원본을 돌려놓고 복제본을 파괴한다.
    //         교체 강도(skyFogSwapIntensity)까지는 틴트·노출로 어두워지고, 그 뒤에는 흰 텍스처 + 틴트로 "지금 안개 색 × 밝기"를 그린다
    //         → Peak에서 하늘 전체 = 안개 색 (리뷰 중간 1, 계산은 AshStormVisualMath).
    // - 태양: 세기 × lerp(1, sunIntensityFactorAtPeak, 강도).
    // - 재 파티클(2a "AshParticles"): 방출 × lerp(1, ashEmissionMultiplier), 시뮬레이션 속도 × lerp(1, ashSpeedMultiplier),
    //         크기 × lerp(1, ashSizeMultiplier), 최대 입자 수 lerp(원래, ashMaxParticlesStorm). 없으면 건너뛴다.
    //         방출 배율은 상한이 담는 만큼으로 자른다(AshStormVisualMath.AshEmissionMultiplier, 리뷰 낮음 2).
    //         TODO(GDD-?) Falling에서 상한이 줄 때 넘치는 입자는 조금씩 정리된다 — V4·S1에서 눈으로 확인.
    // 강도 0이면 시작 때 기억한 씬 값으로 정확히 되돌린다(전환 40초 동안만 쓴다).
    public sealed class AshStormPresenter : MonoBehaviour
    {
        [Tooltip("재 파티클 (2a가 Player 아래에 만드는 \"AshParticles\"). 없으면 파티클 효과를 건너뛴다")]
        [SerializeField] private ParticleSystem ashParticles;
        [Tooltip("태양. 비면 RenderSettings.sun")]
        [SerializeField] private Light sun;
        [SerializeField] private AtmosphereConfigSO atmosphere;
        [SerializeField] private AshStormConfigSO config;

        // Panoramic(_Tint)·Procedural(_SkyTint) 하늘 셰이더 속성 (엔진 셰이더 이름)
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private bool captured;
        private bool fogEnabled;
        private FogMode fogMode;
        private float fogDensity;
        private Color fogColor;
        private Material skyOriginal;
        private Material skyCopy;
        private Light sunLight;
        private float sunIntensity;

        private bool ashCaptured;
        private float ashRate;
        private float ashSimulationSpeed;
        private float ashSize;
        private float ashLifetime;
        private int ashMaxParticles;

        private float appliedIntensity;
        private float appliedVisibility = float.PositiveInfinity;

        public ParticleSystem AshParticles => ashParticles;
        public Light Sun => sunLight != null ? sunLight : sun;
        public float AppliedIntensity => appliedIntensity;
        // 테스트·디버그: 지금 쓰는 하늘 복제본 (폭풍 아님 = null), 원래 하늘 머티리얼(공유 에셋)
        public Material SkyCopy => skyCopy;
        public Material SkyOriginal => skyOriginal;
        // 시작 때 기억한 씬 값 (복원 기준)
        public float OriginalFogDensity => fogDensity;
        public Color OriginalFogColor => fogColor;
        public float OriginalSunIntensity => sunIntensity;
        public float OriginalAshRate => ashRate;
        public float OriginalAshLifetime => ashLifetime;
        // 지금 하늘 복제본이 안개 색 단색인가 (교체 강도 초과)
        public bool SkyIsFogColor { get; private set; }

        private void Start()
        {
            CaptureIfNeeded();
            if (config == null)
                Debug.LogError("[AshStormPresenter] AshStormConfigSO가 연결되지 않아 폭풍 표시를 하지 않는다.", this);
            if (atmosphere == null)
                Debug.LogWarning("[AshStormPresenter] AtmosphereConfigSO가 없어 폭풍 안개를 바꾸지 않는다.", this);
            if (ashParticles == null)
                Debug.LogWarning("[AshStormPresenter] 재 파티클(AshParticles)이 없어 폭풍 파티클 배율을 건너뛴다.", this);
        }

        // 강도(0~1)·시야(m, 폭풍 아님 = ∞)를 화면에 반영한다.
        public void Apply(float intensity, float visibilityM)
        {
            CaptureIfNeeded();
            if (config == null)
                return;
            intensity = float.IsNaN(intensity) ? 0f : Mathf.Clamp01(intensity);
            if (Mathf.Approximately(intensity, appliedIntensity) && SameVisibility(visibilityM, appliedVisibility)
                && (intensity > 0f || skyCopy == null))
                return;
            appliedIntensity = intensity;
            appliedVisibility = visibilityM;

            if (intensity <= 0f)
            {
                RestoreAll();
                return;
            }

            ApplyFog(intensity, visibilityM);
            ApplySky(intensity);
            if (sunLight != null)
                sunLight.intensity = sunIntensity * Mathf.Lerp(1f, config.sunIntensityFactorAtPeak, intensity);
            ApplyAsh(intensity);
        }

        // 재 파티클을 바꾼다(씬 적용 뒤 생긴 파티클·테스트가 코드로 만든 파티클). 이전 것은 원래 값으로 돌리고 새 것의 원래 값을 기억한다.
        public void SetAshParticles(ParticleSystem particles)
        {
            if (particles == ashParticles)
                return;
            RestoreAsh();
            ashParticles = particles;
            ashCaptured = false;
            CaptureAsh();
            if (appliedIntensity > 0f && config != null)
                ApplyAsh(appliedIntensity);
        }

        private void OnDestroy()
        {
            // 씬 언로드 중에는 다른 씬이 활성일 수 있다 — 그 씬의 RenderSettings를 덮지 않게 이 씬이 활성일 때만 안개를 되돌린다.
            // 하늘은 지금 하늘이 우리 복제본일 때만 원본으로 돌린다. 복제본은 항상 파괴한다(공유 에셋은 처음부터 건드리지 않았다).
            if (captured && appliedIntensity > 0f && gameObject.scene == SceneManager.GetActiveScene())
                RestoreFog();
            if (sunLight != null && captured)
                sunLight.intensity = sunIntensity;
            RestoreAsh();
            ReleaseSkyCopy();
        }

        private void CaptureIfNeeded()
        {
            if (captured)
                return;
            captured = true;
            fogEnabled = RenderSettings.fog;
            fogMode = RenderSettings.fogMode;
            fogDensity = RenderSettings.fogDensity;
            fogColor = RenderSettings.fogColor;
            skyOriginal = RenderSettings.skybox;
            sunLight = sun != null ? sun : RenderSettings.sun;
            if (sunLight != null)
                sunIntensity = sunLight.intensity;
            CaptureAsh();
        }

        private void CaptureAsh()
        {
            if (ashCaptured || ashParticles == null)
                return;
            ashCaptured = true;
            ParticleSystem.MainModule main = ashParticles.main;
            ashRate = ashParticles.emission.rateOverTimeMultiplier;
            ashSimulationSpeed = main.simulationSpeed;
            ashSize = main.startSizeMultiplier;
            ashLifetime = main.startLifetimeMultiplier;
            ashMaxParticles = main.maxParticles;
        }

        private void ApplyFog(float intensity, float visibilityM)
        {
            // 가시거리 → 밀도 기준(투과율 임계)은 AtmosphereConfigSO에 있다. 없으면 안개를 건드리지 않는다.
            if (atmosphere == null)
                return;
            // 안개가 꺼져 있던(또는 Exp²가 아닌) 씬은 밀도 0에서 시작한다고 본다.
            float baseDensity = fogEnabled && fogMode == FogMode.ExponentialSquared ? fogDensity : 0f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Max(baseDensity,
                FogMath.DensityForVisibility(visibilityM, atmosphere.fogVisibilityThreshold));
            if (TryGetColor(config.stormFogToken, out Color stormColor))
                RenderSettings.fogColor = Color.Lerp(fogColor, stormColor, intensity);
        }

        private void ApplySky(float intensity)
        {
            if (skyOriginal == null)
                return;
            if (skyCopy == null)
            {
                skyCopy = new Material(skyOriginal) { name = skyOriginal.name + " (AshStorm)" };
                RenderSettings.skybox = skyCopy;
            }
            float swap = config.skyFogSwapIntensity;
            // 단색 안개 하늘은 Panoramic(_MainTex·_Tint)일 때만. 다른 하늘 셰이더는 틴트·노출만(교체 없음).
            bool canFogSky = skyOriginal.HasProperty(MainTexId) && skyOriginal.HasProperty(TintId);
            if (canFogSky && AshStormVisualMath.UsesFogSky(intensity, swap))
            {
                // ApplyFog 다음에 불린다 — RenderSettings.fogColor = 지금 안개 색 (Atmosphere가 없으면 씬 안개 색 그대로).
                // Texture2D.whiteTexture는 엔진 공용 텍스처라 읽기만 한다(수정하지 않음).
                skyCopy.SetTexture(MainTexId, Texture2D.whiteTexture);
                skyCopy.SetColor(TintId, AshStormVisualMath.PanoramicTintFor(RenderSettings.fogColor,
                    AshStormVisualMath.FogSkyBrightness(intensity, swap), QualitySettings.activeColorSpace == ColorSpace.Linear));
                if (skyOriginal.HasProperty(ExposureId))
                    skyCopy.SetFloat(ExposureId, 1f);
                SkyIsFogColor = true;
                return;
            }

            if (canFogSky && SkyIsFogColor)
                skyCopy.SetTexture(MainTexId, skyOriginal.GetTexture(MainTexId));
            SkyIsFogColor = false;
            float progress = canFogSky ? AshStormVisualMath.TexturedSkyProgress(intensity, swap) : intensity;
            if (TryGetColor(config.skyTintToken, out Color tint))
            {
                if (skyOriginal.HasProperty(TintId))
                    skyCopy.SetColor(TintId, Color.Lerp(skyOriginal.GetColor(TintId), tint, progress));
                else if (skyOriginal.HasProperty(SkyTintId))
                    skyCopy.SetColor(SkyTintId, Color.Lerp(skyOriginal.GetColor(SkyTintId), tint, progress));
            }
            if (skyOriginal.HasProperty(ExposureId))
                skyCopy.SetFloat(ExposureId, skyOriginal.GetFloat(ExposureId) * Mathf.Lerp(1f, config.skyExposureAtPeak, progress));
        }

        private void ApplyAsh(float intensity)
        {
            if (ashParticles == null || !ashCaptured)
                return;
            ParticleSystem.MainModule main = ashParticles.main;
            ParticleSystem.EmissionModule emission = ashParticles.emission;
            int stormMax = atmosphere != null ? Mathf.Max(ashMaxParticles, atmosphere.ashMaxParticlesStorm) : ashMaxParticles;
            float emissionMultiplier = AshStormVisualMath.AshEmissionMultiplier(config.ashEmissionMultiplier, ashRate, ashLifetime, stormMax);
            emission.rateOverTimeMultiplier = ashRate * Mathf.Lerp(1f, emissionMultiplier, intensity);
            // 시뮬레이션 속도: 낙하가 빨라지고 수명이 짧아진다(수명 동안 낙하 거리 유지). 정상 상태 입자 수 = 방출 × 수명이라 수는 그대로.
            main.simulationSpeed = ashSimulationSpeed * Mathf.Lerp(1f, config.ashSpeedMultiplier, intensity);
            main.startSizeMultiplier = ashSize * Mathf.Lerp(1f, config.ashSizeMultiplier, intensity);
            main.maxParticles = Mathf.RoundToInt(Mathf.Lerp(ashMaxParticles, stormMax, intensity));
        }

        private void RestoreAll()
        {
            RestoreFog();
            if (sunLight != null)
                sunLight.intensity = sunIntensity;
            RestoreAsh();
            ReleaseSkyCopy();
        }

        private void RestoreFog()
        {
            RenderSettings.fog = fogEnabled;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogColor = fogColor;
        }

        private void RestoreAsh()
        {
            if (ashParticles == null || !ashCaptured)
                return;
            ParticleSystem.MainModule main = ashParticles.main;
            ParticleSystem.EmissionModule emission = ashParticles.emission;
            emission.rateOverTimeMultiplier = ashRate;
            main.simulationSpeed = ashSimulationSpeed;
            main.startSizeMultiplier = ashSize;
            main.maxParticles = ashMaxParticles;
        }

        // 지금 하늘이 복제본이면 원본으로 돌리고 복제본을 파괴한다.
        private void ReleaseSkyCopy()
        {
            if (skyCopy == null)
                return;
            if (RenderSettings.skybox == skyCopy)
                RenderSettings.skybox = skyOriginal;
            Destroy(skyCopy);
            skyCopy = null;
            SkyIsFogColor = false;
        }

        // 폭풍 표시 색은 AshStormConfigSO.palette, 없으면 AtmosphereConfigSO.palette에서 가져온다. 둘 다 없으면 색은 그대로.
        private bool TryGetColor(PaletteToken token, out Color color)
        {
            PaletteSO palette = config.palette != null ? config.palette
                : atmosphere != null ? atmosphere.palette : null;
            color = palette != null ? palette.Get(token) : default;
            return palette != null;
        }

        private static bool SameVisibility(float a, float b) =>
            (float.IsPositiveInfinity(a) && float.IsPositiveInfinity(b)) || Mathf.Approximately(a, b);
    }
}
