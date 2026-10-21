using Ashfall.Core;

namespace Ashfall.Inventory
{
    // F 상호작용 대상 (상자 열기, 바닥 아이템 줍기). Inventory 어셈블리 안에서만 쓴다.
    public interface IInteractable
    {
        InteractionKind Kind { get; }
        string TargetName { get; }
        void Interact(PlayerInventoryBehaviour by);
    }
}
