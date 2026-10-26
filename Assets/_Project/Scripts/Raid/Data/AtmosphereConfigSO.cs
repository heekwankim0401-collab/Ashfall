using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // 톤 매핑 선택 (§10.2). Raid가 URP 형식을 참조하지 않게 따로 둔다 — EditorTools가 URP TonemappingMode로 바꾼다.
    public enum TonemapChoice
    {
        None,
        Neutral,
        Aces,
    }

    // 필름 그레인 종류 (§10.2). URP FilmGrainLookup의 내장 값과 같은 이름·순서(Custom 제외) — EditorTools가 이름으로 바꾼다.
    public enum FilmGrainChoice
    {
        Thin1,
        Thin2,
        Medium1,
        Medium2,
        Medium3,
        Medium4,
        Medium5,
        Medium6,
        Large01,
        Large02,
    }

    // 맵 점광원 한 종류의 모양 (맵 개선 설계 §3). 색 = lerp(token, Bone, boneMix). 그림자 없음은 메뉴가 고정한다.
    [System.Serializable]
    public struct MapLightStyle
    {
        public PaletteToken token;
        [Range(0f, 1f)] public float boneMix;
        public float intensity;
        public float rangeM;

        public MapLightStyle(PaletteToken token, float boneMix, float intensity, float rangeM)
        {
            this.token = token;
            this.boneMix = boneMix;
            this.intensity = intensity;
            this.rangeM = rangeM;
        }
    }

    // 레이드 대기 연출 수치 (§10.2, M6 설계 §3.2, Q3·Q4·Q30). 에셋: Data/Raid/AtmosphereConfig.asset (M6 데이터 메뉴가 만든다).
    // 수치의 원본은 이 SO다. 볼륨 프로필(VP_RaidAtmosphere)·하늘 PNG·머티리얼은 메뉴가 여기서 다시 만드는 파생물(인스펙터 직접 수정은 덮인다).
    // 필드 초기값 = 설계값. §10.2에 없는 수치는 전부 TODO(GDD-?) 임시값.
    [CreateAssetMenu(menuName = "Ashfall/Atmosphere Config", fileName = "AtmosphereConfig")]
    public class AtmosphereConfigSO : ScriptableObject
    {
        [Header("팔레트 (§10.1)")]
        public PaletteSO palette;

        [Header("안개 (§10.2 Height Fog → Exp² 거리 안개, Q3)")]
        [Tooltip("§10.2 안개 색 토큰")]
        public PaletteToken fogToken = PaletteToken.DriedBlood;
        [Tooltip("맑을 때 가시거리 (m). 폭풍이 없을 때의 안개 밀도를 정한다. TODO(GDD-?)")]
        public float clearVisibilityM = 600f;   // 맵 개선 밝기 개정 1 (M6 450)
        [Tooltip("가시거리에서 남는 투과율 (Exp² 밀도 계산 기준). TODO(GDD-?)")]
        public float fogVisibilityThreshold = 0.05f;

        [Header("하늘 (§10.2 그라데이션 Ash_Black → Rust_Red, Q4)")]
        public PaletteToken zenithToken = PaletteToken.AshBlack;
        public PaletteToken horizonToken = PaletteToken.RustRed;
        [Tooltip("지평선 아래 = 안개색으로 원경 지형과 잇는다. TODO(GDD-?)")]
        public PaletteToken belowHorizonToken = PaletteToken.DriedBlood;
        [Tooltip("지평선 띠 두께 (도). TODO(GDD-?)")]
        public float horizonBandDeg = 10f;
        [Tooltip("생성 파노라마 크기 (equirect 2:1). TODO(GDD-?)")]
        public Vector2Int skyTextureSize = new Vector2Int(1024, 512);
        [Tooltip("실제 하늘 그림 (있으면 메뉴가 생성 PNG를 쓰지 않는다)")]
        public Texture2D skyTextureOverride;
        [Tooltip("지평선 → 천정 그라데이션 곡선 지수 (1 = 선형, 작을수록 지평선 색이 빨리 어두워진다). TODO(GDD-?) Q4")]
        public float skyGradientExponent = 0.6f;

        [Header("태양 (§10.2 낮게 걸린 흐린 태양, TODO(GDD-?))")]
        [Tooltip("방위 (도, +Z = 0, 시계 방향)")]
        public float sunAzimuthDeg = 245f;
        [Tooltip("고도 (도)")]
        public float sunElevationDeg = 12f;   // 맵 개선 밝기 개정 1 (M6 8)
        [Tooltip("태양 원반 반지름 (도)")]
        public float sunDiskDeg = 2.5f;
        [Tooltip("태양 주변 번짐 반지름 (도)")]
        public float sunGlowDeg = 18f;
        [Tooltip("흐림 (0 = 또렷, 1 = 안 보임)")]
        [Range(0f, 1f)] public float sunHaze = 0.6f;
        [Tooltip("흐림 0일 때 태양 번짐 최대 섞임 비율. TODO(GDD-?) Q4")]
        [Range(0f, 1f)] public float sunGlowStrength = 0.8f;
        public PaletteToken sunCoreToken = PaletteToken.Bone;
        public PaletteToken sunGlowToken = PaletteToken.Ember;
        [Tooltip("Directional Light \"Sun\" 색")]
        public PaletteToken sunLightToken = PaletteToken.Ember;
        public float sunLightIntensity = 2.4f;   // 맵 개선 밝기 개정 1 (M6 0.9)
        [Tooltip("태양 라이트 색 = lerp(sunLightToken, Bone, 이 값). 0 = 토큰 그대로(M6). TODO(GDD-?) 맵 개선 §2")]
        [Range(0f, 1f)] public float sunLightBoneMix = 0.4f;

        [Header("환경광 Trilight (TODO(GDD-?))")]
        public PaletteToken ambientSkyToken = PaletteToken.RustRed;
        public PaletteToken ambientEquatorToken = PaletteToken.DriedBlood;
        public PaletteToken ambientGroundToken = PaletteToken.DriedBlood;   // 맵 개선 밝기 개정 1 (M6 Ash_Black)
        public float ambientIntensity = 3.5f;   // 맵 개선 밝기 개정 1 (M6 1.0)

        [Header("포스트 프로세싱 (§10.2)")]
        [Tooltip("§10.2 채도 -30")]
        public float saturation = -30f;
        [Tooltip("붉은 틴트 토큰. TODO(GDD-?)")]
        public PaletteToken tintToken = PaletteToken.Ember;
        [Tooltip("colorFilter = lerp(흰색, 틴트, 이 값). TODO(GDD-?)")]
        [Range(0f, 1f)] public float tintStrength = 0.15f;
        [Tooltip("대비. TODO(GDD-?)")]
        public float contrast = 10f;
        [Tooltip("Color Adjustments Post Exposure (EV). 0 = M6. TODO(GDD-?) 맵 개선 §2")]
        public float postExposureEv = 0.8f;
        [Tooltip("§10.2 Film Grain 0.3")]
        [Range(0f, 1f)] public float filmGrainIntensity = 0.3f;
        [Tooltip("그레인 종류. TODO(GDD-?)")]
        public FilmGrainChoice filmGrainType = FilmGrainChoice.Medium1;
        [Tooltip("§10.2 Vignette 0.35")]
        [Range(0f, 1f)] public float vignetteIntensity = 0.35f;
        [Tooltip("TODO(GDD-?)")]
        [Range(0f, 1f)] public float vignetteSmoothness = 0.4f;
        public PaletteToken vignetteToken = PaletteToken.AshBlack;
        [Tooltip("§10.2 Bloom 낮음. 수치 TODO(GDD-?)")]
        public float bloomIntensity = 0.15f;
        [Tooltip("TODO(GDD-?)")]
        public float bloomThreshold = 1.1f;
        [Tooltip("TODO(GDD-?)")]
        public TonemapChoice tonemapping = TonemapChoice.Neutral;

        [Header("재 파티클 (§10.2 반경 30m, Q2 Particle System)")]
        [Tooltip("§10.2 플레이어 주변 반경 (m)")]
        public float ashRadiusM = 30f;
        [Tooltip("방출 상자 높이 (m). TODO(GDD-?)")]
        public float ashHeightM = 20f;
        [Tooltip("초당 방출 수. TODO(GDD-?)")]
        public float ashRatePerSec = 600f;
        [Tooltip("수명 (초). TODO(GDD-?)")]
        public float ashLifetimeSec = 10f;
        [Tooltip("평시 최대 입자 수. TODO(GDD-?)")]
        public int ashMaxParticles = 6000;
        [Tooltip("재 폭풍 중 최대 입자 수. TODO(GDD-?)")]
        public int ashMaxParticlesStorm = 12000;
        [Tooltip("크기 최소~최대 (m). TODO(GDD-?)")]
        public Vector2 ashSizeM = new Vector2(0.03f, 0.08f);
        [Tooltip("낙하 속도 최소~최대 (m/s). TODO(GDD-?)")]
        public Vector2 ashFallSpeedMps = new Vector2(0.3f, 0.8f);
        [Tooltip("노이즈 흔들림 세기. TODO(GDD-?)")]
        public float ashNoiseStrength = 0.3f;
        [Tooltip("노이즈 주파수. TODO(GDD-?)")]
        public float ashNoiseFrequency = 0.4f;
        [Tooltip("방출 상자 중심 높이 = 플레이어 + 상자 높이 × 이 비율. TODO(GDD-?)")]
        public float ashBoxCenterRatio = 0.3f;
        [Tooltip("입자 최대 화면 크기 (화면 비율) — 카메라 바로 앞 재가 화면을 덮지 않게 (V4). TODO(GDD-?)")]
        public float ashMaxScreenSize = 0.01f;
        public PaletteToken ashToken = PaletteToken.AshGrey;
        [Range(0f, 1f)] public float ashAlpha = 0.7f;
        [Tooltip("실제 재 텍스처 (있으면 메뉴가 생성 PNG를 쓰지 않는다)")]
        public Texture2D ashTextureOverride;

        [Header("지형 레이어 TL_AshSoil (자리표시, TODO(GDD-?))")]
        [Tooltip("노이즈 회색 하한 (0~1, × Dried_Blood). 생성 GroundNoise.png에 반영")]
        [Range(0f, 1f)] public float groundNoiseMin = 0.7f;
        [Tooltip("지형 텍스처 타일 크기 (m)")]
        public float groundTileM = 12f;

        [Header("맵 점광원 (맵 개선 설계 §3, TODO(GDD-?))")]
        [Tooltip("불타는 드럼통·잔불")]
        public MapLightStyle fireLight = new MapLightStyle(PaletteToken.Ember, 0.15f, 10f, 12f);
        [Tooltip("깨진 가로등")]
        public MapLightStyle lampLight = new MapLightStyle(PaletteToken.Bone, 0f, 8f, 16f);
        [Tooltip("레이드 씬 점광원 상한 (맵 개선 메뉴·테스트가 확인, 성능 — 그림자 없는 실시간 광원)")]
        public int maxMapLights = 16;

        [Header("개정")]
        [Tooltip("밝기 개정 번호. 맵 개선 메뉴는 이 값이 1 미만일 때만 밝기 개정 1 값을 쓰고 1로 올린다(이후 조정값 보존). 필드 초기값은 이미 개정 1 값 — 기존 에셋 이전용")]
        public int lightingRevision = 0;
    }
}
