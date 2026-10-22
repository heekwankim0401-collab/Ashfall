using UnityEngine;

namespace Ashfall.Core
{
    // §14 탄창 정의. 실제 남은 탄 수는 런타임 탄창 객체가 저장한다(§5.1).
    [CreateAssetMenu(menuName = "Ashfall/Magazine", fileName = "Magazine")]
    public class MagazineDefinitionSO : ItemDefinitionSO
    {
        public AmmoType ammo;
        public int capacity;

        // §14에 없는 필드 (M7 설계 §1.3, TODO(GDD-?) Q5·Q6).
        [Tooltip("탄 상자(낱탄). 무기에 넣을 수 없고 튜브 장전(ILooseAmmoSource)에서 한 발씩 꺼낸다. 빈 상자는 0발 아이템으로 남는다")]
        public bool isAmmoBox;
    }
}
