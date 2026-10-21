using UnityEngine;

namespace Ashfall.Core
{
    // §14 탄창 정의. 실제 남은 탄 수는 런타임 탄창 객체가 저장한다(§5.1).
    [CreateAssetMenu(menuName = "Ashfall/Magazine", fileName = "Magazine")]
    public class MagazineDefinitionSO : ItemDefinitionSO
    {
        public AmmoType ammo;
        public int capacity;
    }
}
