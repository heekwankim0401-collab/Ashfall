namespace Ashfall.Core
{
    // F 상호작용 대상 중 Inventory 밖 기능이 만드는 것 (§8.2 EXT-02 전원 스위치, M4 설계 §1.1).
    // Inventory의 F 레이캐스트(PlayerInventoryBehaviour)가 콜라이더에서 찾아 프롬프트를 띄우고 Interact를 부른다.
    public interface IWorldInteractable
    {
        InteractionKind Kind { get; }
        string TargetName { get; }
        bool IsAvailable { get; }   // false면 프롬프트 없음 (이미 켠 스위치)
        void Interact();
    }
}
