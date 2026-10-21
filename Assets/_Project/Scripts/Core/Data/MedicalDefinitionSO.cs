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
    }
}
