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

        // M7 방독면 GEAR-GSM-01 (§8.3, M7 설계 §1.5, Q37·Q38): 헬멧 슬롯을 공유(kind = Helmet)하고 재 폭풍 방사능만 막는다.
        // 구역 방사능(IRadiationField)은 막지 않는다. 필터 소모 없음.
        [Tooltip("착용 중이면 재 폭풍 방사능(IAshStormInfo.RadiationPerSec)을 받지 않는다. 구역 방사능은 그대로 (TODO(GDD-?) Q38)")]
        public bool blocksStormRadiation;
    }
}
