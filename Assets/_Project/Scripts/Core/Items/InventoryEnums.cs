namespace Ashfall.Core
{
    // §6 장착 슬롯 7개
    public enum EquipSlot
    {
        Primary1,
        Primary2,
        Secondary,
        Helmet,
        BodyArmor,
        TacticalVest,
        Backpack,
    }

    // 그리드 종류. 주머니는 GridRef.Index 0~3으로 구분한다.
    public enum GridKind
    {
        Pocket,
        TacticalVest,
        Backpack,
        Loot,
    }

    // ContainerDefinitionSO 종류 (장착 슬롯 판정)
    public enum ContainerKind
    {
        TacticalVest,
        Backpack,
    }

    // ArmorDefinitionSO 종류 (장착 슬롯 판정). §14에 없는 추가 (설계 D6).
    public enum ArmorKind
    {
        Helmet,
        BodyArmor,
    }

    // F 상호작용 종류 (HUD 프롬프트 문구)
    public enum InteractionKind
    {
        OpenContainer,
        PickUp,
    }
}
