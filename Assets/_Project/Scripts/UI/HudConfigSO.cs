using Ashfall.Core;
using UnityEngine;

namespace Ashfall.UI
{
    // HUD·화면 수치. §14에 없는 SO (설계 D6). 에셋: Data/UI/HudConfig.asset
    // 색·칸 크기 같은 모양 값은 SO가 아니라 Art/UI/Ashfall.uss 변수에 있다.
    [CreateAssetMenu(menuName = "Ashfall/HUD Config", fileName = "HudConfig")]
    public class HudConfigSO : ScriptableObject
    {
        // TODO(GDD-?): RULE-04 결과 문구 표시 시간이 문서에 없다.
        [Tooltip("탄창 확인 결과 문구 표시 시간 (초)")]
        public float magCheckResultSec = 2.0f;

        // TODO(GDD-?): 문서에 없음. 클릭(더블클릭 사용)과 드래그를 구분하는 거리.
        [Tooltip("인벤토리에서 이 거리(px) 이상 끌어야 드래그가 시작된다")]
        public float dragThresholdPx = 4f;

        // TODO(GDD-?) Q11: RULE-06 "종이 지도 아이템으로만". 이 정의를 소지(장착·소지품)해야 M 지도 화면이 열린다.
        // 비어 있으면 지도를 열 수 없다(경고 1회). 에셋 연결은 M4DataSetup(ITM-MAP-01).
        [Tooltip("M 지도 화면을 여는 데 필요한 아이템 (RULE-06)")]
        public ItemDefinitionSO mapItem;

        // MVP 마무리 F3 내구도 대략 등급 (§5.1, RULE-04 — 숫자 없음). TODO(GDD-?) MF-Q10: GDD에 경계가 없다 — 임시값.
        // 비율 ≥ conditionWornBelow = 양호, conditionCriticalBelow ≤ 비율 < conditionWornBelow = 낡음, 비율 < conditionCriticalBelow = 위험.
        [Tooltip("내구도 비율이 이 값 미만이면 \"낡음\" (0.5 = §5.1 고장 시작). TODO(GDD-?) MF-Q10")]
        [Range(0f, 1f)]
        public float conditionWornBelow = 0.5f;
        [Tooltip("내구도 비율이 이 값 미만이면 \"위험\" (낡음 경계 이하). TODO(GDD-?) MF-Q10")]
        [Range(0f, 1f)]
        public float conditionCriticalBelow = 0.25f;

        // ── 상시 HUD(B안, 2026-10-28 — RULE-02·04·05 개정) ──
        // TODO(GDD-?): 피격 방향 붉은 가장자리 시간·세기가 문서에 없다 — 임시값.
        [Tooltip("피격 방향 붉은 가장자리 표시 시간 (초). 이 시간 동안 선형으로 사라진다. TODO(GDD-?)")]
        [Min(0.05f)]
        public float hitEdgeDurationSec = 1.0f;
        [Tooltip("피격 직후 가장자리 불투명도 (0~1). TODO(GDD-?)")]
        [Range(0f, 1f)]
        public float hitEdgeMaxOpacity = 0.6f;

        // TODO(GDD-?): 부위 HP 색 경계가 문서에 없다 — 임시값. 비율이 이 값 미만이면 Ember, 0이면 파괴(어둡게).
        [Tooltip("부위 HP 비율이 이 값 미만이면 경고색(Ember). TODO(GDD-?)")]
        [Range(0f, 1f)]
        public float partHpWarnBelow = 0.5f;

        // RULE-05 개정 HUD 방사능 누적 표시 상한 (§4.3 선량 0~100)
        [Tooltip("HUD 방사능 누적 표시 상한 (선량)")]
        public float radiationDisplayMax = 100f;

        private void OnValidate()
        {
            conditionCriticalBelow = Mathf.Min(conditionCriticalBelow, conditionWornBelow);
        }
    }
}
