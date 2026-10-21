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
    }
}
