using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Hideout
{
    // SYS-HUB 상인 1명 (§9, M5 설계 §3.2). §14 스키마에 없는 추가. 에셋 Data/Hideout/TraderConfig.asset.
    // 가격 계산은 PriceRules. 재고·평판·시세 변동 없음 (Q11).
    [CreateAssetMenu(menuName = "Ashfall/Trader Config", fileName = "TraderConfig")]
    public class TraderConfigSO : ScriptableObject
    {
        [Tooltip("상인 id (TRD-01). 세이브에는 쓰지 않는다")]
        public string id = "TRD-01";
        [Tooltip("상인 이름 (은신처 상인 탭 제목). TODO(GDD-?)")]
        public string displayName = "고물상";
        [Tooltip("매수가(플레이어가 삼) = 올림(basePrice × 이 값), 최소 1. basePrice ≤ 0이면 팔지 않는다. 0 이상. TODO(GDD-?) Q10")]
        [Min(0f)]
        public float buyPriceMultiplier = 1f;
        [Tooltip("매도가(플레이어가 팜) = 내림(basePrice × 이 값), 무기는 장착 탄창 매도가 포함. " +
                 "0 이상, 매수 배율 이하(되팔기 이득 없음 — 넘으면 매수 배율로 자른다). TODO(GDD-?) Q10")]
        [Min(0f)]
        public float sellPriceMultiplier = 0.4f;
        // MVP 마무리 F3 상인 수리 (§5.1, MF-Q13). TODO(GDD-?) MF-Q13: GDD에 수리비가 없다 — 임시값 0.5.
        [Tooltip("수리비 = max(1, 올림(basePrice × 이 값 × 잃은 내구도 비율)), 100%로 수리. " +
                 "매도 배율 이상(수리 후 되팔기 이득 없음 — 작으면 매도 배율로 올린다). TODO(GDD-?) MF-Q13")]
        [Min(0f)]
        public float repairCostRatio = 0.5f;
        [Tooltip("판매 목록 (이 순서로 표시, 재고 무제한). null·가격 0 이하·중복은 목록에서 빠진다. TODO(GDD-?) Q11")]
        public ItemDefinitionSO[] catalog;

        private void OnValidate()
        {
            buyPriceMultiplier = Mathf.Max(0f, buyPriceMultiplier);
            sellPriceMultiplier = Mathf.Clamp(sellPriceMultiplier, 0f, buyPriceMultiplier);
            repairCostRatio = Mathf.Max(repairCostRatio, sellPriceMultiplier);
        }
    }
}
