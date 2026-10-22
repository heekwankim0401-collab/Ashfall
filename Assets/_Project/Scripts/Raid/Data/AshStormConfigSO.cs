using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // 재 폭풍 수치 (§8.3, M6 설계 §3.3, Q23·Q24). 에셋: Data/Raid/AshStormConfig.asset (M6 데이터 메뉴가 만든다).
    // 필드 초기값 = 설계값. §8.3에 없는 수치는 TODO(GDD-?) 임시값.
    [CreateAssetMenu(menuName = "Ashfall/Ash Storm Config", fileName = "AshStormConfig")]
    public class AshStormConfigSO : ScriptableObject
    {
        [Header("일정 (§8.3 레이드 중 1회 랜덤)")]
        [Tooltip("시작 시각 범위 (레이드 경과 초, 포함 범위, 균등 무작위). TODO(GDD-?) Q23")]
        public Vector2 startWindowSec = new Vector2(180f, 1200f);
        [Tooltip("강도 0 → 1 전환 (초). TODO(GDD-?) Q24")]
        public float riseSec = 20f;
        [Tooltip("§8.3 \"3분\" = 최대 강도 유지 (초). Q24")]
        public float peakSec = 180f;
        [Tooltip("강도 1 → 0 전환 (초). TODO(GDD-?) Q24")]
        public float fallSec = 20f;

        [Header("효과 (§8.3)")]
        [Tooltip("§8.3 최대 강도의 시야 (m). 렌더 안개 + AI 시야 상한 (Q26)")]
        public float stormVisibilityM = 30f;
        [Tooltip("§8.3 최대 강도의 방사능 (/s). 강도에 비례 (Q27)")]
        public float radiationPerSec = 1f;

        [Header("표시 (TODO(GDD-?))")]
        public PaletteSO palette;
        [Tooltip("폭풍 안개 색 (재 색)")]
        public PaletteToken stormFogToken = PaletteToken.AshGrey;
        [Tooltip("생성 하늘이 어두워지며 향하는 틴트 (교체 강도에서 도달, 교체가 없으면 최대 강도에서)")]
        public PaletteToken skyTintToken = PaletteToken.AshGrey;
        [Tooltip("생성 하늘 노출 배율 (교체 강도에서 도달, 교체가 없으면 최대 강도에서)")]
        [Range(0f, 1f)] public float skyExposureAtPeak = 0.35f;
        [Tooltip("이 강도를 넘으면 하늘을 폭풍 안개 색 단색으로 바꾼다(밝기 0 → 최대 강도에서 안개 색). " +
                 "Peak에서 하늘 = 안개 색이라 30m 밖 윤곽이 하늘에 비치지 않는다(Q26, M6 리뷰 중간 1). 1이면 교체 없음. TODO(GDD-?)")]
        [Range(0f, 1f)] public float skyFogSwapIntensity = 0.5f;
        [Tooltip("최대 강도에서 태양 세기 배율")]
        [Range(0f, 1f)] public float sunIntensityFactorAtPeak = 0.25f;

        [Header("재 파티클 배율 (최대 강도, TODO(GDD-?))")]
        [Tooltip("방출 배율. 폭풍 상한(AtmosphereConfig.ashMaxParticlesStorm)이 담는 만큼으로 잘린다 — " +
                 "평시 600/s × 10s = 6000, 상한 12000이면 실제 ×2 (M6 리뷰 낮음 2)")]
        public float ashEmissionMultiplier = 4f;
        [Tooltip("시뮬레이션 속도 배율 (낙하·흔들림이 빨라지고 수명은 짧아진다 — 입자 수는 그대로)")]
        public float ashSpeedMultiplier = 3f;
        public float ashSizeMultiplier = 1.5f;
    }
}
