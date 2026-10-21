using Ashfall.Core;

namespace Ashfall.Inventory
{
    // §6 장착 슬롯 판정 (설계 §3.5 표).
    public static class EquipRules
    {
        public static bool Accepts(EquipSlot slot, ItemDefinitionSO definition)
        {
            switch (slot)
            {
                case EquipSlot.Primary1:
                case EquipSlot.Primary2:
                    return definition is WeaponDefinitionSO primary && primary.cls != WeaponClass.Pistol;
                case EquipSlot.Secondary:
                    // TODO(GDD-?): §6 "보조무기"의 범위가 없다. 권총만 허용.
                    return definition is WeaponDefinitionSO secondary && secondary.cls == WeaponClass.Pistol;
                case EquipSlot.Helmet:
                    return definition is ArmorDefinitionSO helmet && helmet.kind == ArmorKind.Helmet;
                case EquipSlot.BodyArmor:
                    return definition is ArmorDefinitionSO armor && armor.kind == ArmorKind.BodyArmor;
                case EquipSlot.TacticalVest:
                    return definition is ContainerDefinitionSO vest && vest.kind == ContainerKind.TacticalVest;
                case EquipSlot.Backpack:
                    return definition is ContainerDefinitionSO bag && bag.kind == ContainerKind.Backpack;
                default:
                    return false;
            }
        }

        // 조끼·배낭 슬롯 → 내용물 그리드 종류
        public static bool TryGetContainerGrid(EquipSlot slot, out GridKind kind)
        {
            switch (slot)
            {
                case EquipSlot.TacticalVest:
                    kind = GridKind.TacticalVest;
                    return true;
                case EquipSlot.Backpack:
                    kind = GridKind.Backpack;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }
    }
}
