using UnityEngine;

namespace Ashfall.Core
{
    // §14 탄약 정의. 탄도(§5.1)는 velocity와 Physics.gravity로 계산한다.
    [CreateAssetMenu(menuName = "Ashfall/Ammo", fileName = "Ammo")]
    public class AmmoDefinitionSO : ItemDefinitionSO
    {
        public AmmoType type;
        public float damage;
        public int penetration;
        [Tooltip("초기 속도 (m/s)")]
        public float velocity;
        public int pelletCount = 1;

        // §14에 없는 필드 (CHANGELOG_GDD 2026-10-21). 이 시간이 지나도 맞지 않은 탄은 사라진다.
        [Tooltip("최대 비행 시간 (초)")]
        public float maxFlightTimeSec;
    }
}
