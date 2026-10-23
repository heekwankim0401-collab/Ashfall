using UnityEngine;

namespace Ashfall.Core
{
    // §14 의약품 정의. §4.3 상태이상 해제(붕대·지혈대·부목)에 쓴다.
    [CreateAssetMenu(menuName = "Ashfall/Medical", fileName = "Medical")]
    public class MedicalDefinitionSO : ItemDefinitionSO
    {
        [Tooltip("사용하면 해제되는 상태이상")]
        public StatusEffect[] cures = new StatusEffect[0];
        [Tooltip("대상 부위 회복량 (HP). 파괴 부위는 회복되지 않는다")]
        public float healAmount;
        [Tooltip("사용 시간 (초)")]
        public float useTime;
        [Tooltip("사용 가능 횟수")]
        public int uses = 1;

        // M7 전신 효과 (§4.3 STAT-DEHY·HUNG·RAD, M7 설계 §1.5, Q42). 물·음식·해독제도 의약품 흐름(퀵슬롯·횟수·세이브)을 그대로 쓴다.
        // 부위 대상이 없고 전신 효과가 필요하면 사용된다(대상 표시 = 흉부). 0/false = 효과 없음(기존 의약품).
        [Header("M7 전신 효과 (TODO(GDD-?) Q42)")]
        [Tooltip("사용 완료 시 수분 회복량 (최대에서 자름). 0 = 없음")]
        [Min(0f)]
        public float hydrationRestore;
        [Tooltip("사용 완료 시 에너지 회복량 (최대에서 자름). 0 = 없음")]
        [Min(0f)]
        public float energyRestore;
        [Tooltip("사용 완료 시 방사능 선량 0 + 최대 HP 원래대로 (HP 회복 없음). 선량 > 0일 때만 사용된다 (Q36)")]
        public bool curesRadiation;
    }
}
