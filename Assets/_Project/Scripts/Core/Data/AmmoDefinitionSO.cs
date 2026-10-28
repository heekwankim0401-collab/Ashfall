using UnityEngine;

namespace Ashfall.Core
{
    // §14 탄약 정의. 탄도(§5.1)는 velocity와 Physics.gravity로 계산한다.
    [CreateAssetMenu(menuName = "Ashfall/Ammo", fileName = "Ammo")]
    public class AmmoDefinitionSO : ItemDefinitionSO
    {
        public AmmoType type;
        [Tooltip("무기 damage가 0일 때 쓰는 기본 피해 (무기 없이 쏘는 터렛·테스트). M3 결정, DamageRules")]
        public float damage;
        public int penetration;
        [Tooltip("초기 속도 (m/s)")]
        public float velocity;
        public int pelletCount = 1;

        // §14에 없는 필드 (CHANGELOG_GDD 2026-10-21). 이 시간이 지나도 맞지 않은 탄은 사라진다.
        [Tooltip("최대 비행 시간 (초)")]
        public float maxFlightTimeSec;

        // 정조준·탄도 필드 (GDD §14에 반영, CHANGELOG_GDD 2026-10-28 AB-Q3). 이차 공기 저항: 가속도 a = g - dragPerM·|v|·v.
        // 0이면 저항 없음(이전 동작과 같다). 거리 s를 날아간 뒤 탄속 ≈ velocity·e^(-dragPerM·s) (낙차 무시).
        [Tooltip("공기 저항 계수 (1/m). a = -k·|v|·v. 0 = 저항 없음. TODO(GDD-?) AB-Q3 임시값")]
        public float dragPerM;
    }
}
