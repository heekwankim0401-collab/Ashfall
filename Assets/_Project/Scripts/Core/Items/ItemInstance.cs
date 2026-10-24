using System;
using UnityEngine;

namespace Ashfall.Core
{
    // 런타임 아이템 하나 (정의 + 상태). 정의(SO)는 여러 인스턴스가 공유한다.
    // 회전은 인스턴스가 아니라 그리드 배치(PlacedItem)의 속성이다 (설계 D4).
    public class ItemInstance
    {
        public ItemDefinitionSO Definition { get; }

        public ItemInstance(ItemDefinitionSO definition)
        {
            Definition = definition != null ? definition : throw new ArgumentNullException(nameof(definition));
        }

        public virtual float WeightKg => Definition.weightKg;

        // rotated면 W↔H
        public Vector2Int SizeFor(bool rotated) =>
            rotated ? new Vector2Int(Definition.size.y, Definition.size.x) : Definition.size;

        public override string ToString() => $"{GetType().Name}({Definition.id})";
    }

    public static class ItemFactory
    {
        // 정의 종류에 맞는 런타임 인스턴스를 만든다.
        // 탄창 → 꽉 찬 탄창(탄 상자도 가득), 무기 → 탄창 없는 무기(내장 튜브 무기는 빈 튜브, M7 Q5)·내구도 100(Q12),
        // 의약품 → 사용 횟수 가득, 방어구 → 내구도 최대 ArmorItem(MVP 마무리 F4), 그 밖 → 상태 없는 ItemInstance.
        public static ItemInstance Create(ItemDefinitionSO definition)
        {
            switch (definition)
            {
                case MagazineDefinitionSO magazine:
                    return Magazine.Full(magazine);
                case WeaponDefinitionSO weapon:
                    return new WeaponItem(weapon);
                case MedicalDefinitionSO medical:
                    return new MedicalItem(medical);
                case ArmorDefinitionSO armor:
                    // MVP 마무리 F4: 내구도 최대 (방독면처럼 최대 0이면 내구도 없음)
                    return new ArmorItem(armor);
                default:
                    return new ItemInstance(definition);
            }
        }
    }
}
