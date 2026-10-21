using UnityEngine;

namespace Ashfall.Core
{
    // §14 방어구 정의. §13.2는 Weapons 폴더지만 Inventory도 쓰므로 Core에 둔다 (설계 D6, M1 아이템 SO와 같은 사유).
    // M2는 정의·장착·무게만. 관통·둔탁 피해·내구도 감소(§5.3)는 M3 이후.
    [CreateAssetMenu(menuName = "Ashfall/Armor", fileName = "Armor")]
    public class ArmorDefinitionSO : ItemDefinitionSO
    {
        [Tooltip("방어 등급 1~6 (§5.3)")]
        public int armorClass;
        public float maxDurability;
        public BodyPart[] covers = new BodyPart[0];

        // §14에 없는 필드 (설계 D6). 헬멧/방탄복 슬롯 판정용 — covers로 추론하면 모호하다.
        public ArmorKind kind;
    }
}
