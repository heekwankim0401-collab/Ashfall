using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Inventory
{
    // F 상호작용 대상 판정 (§6 상자·바닥 아이템 + M4 Core IWorldInteractable — EXT-02 전원 스위치, M4 설계 §1.1).
    // 대상은 Component 하나로 다룬다: Inventory의 IInteractable(우선) 또는 다른 기능이 만든 IWorldInteractable.
    // F 판정·프롬프트는 PlayerInventoryBehaviour 한 곳에서만 한다.
    public static class InteractionTargets
    {
        // 콜라이더(자신 또는 부모)에서 상호작용 대상을 찾는다. 없으면 null. 사용 가능 여부는 보지 않는다(IsUsable).
        public static Component Find(Collider collider)
        {
            if (collider == null)
                return null;
            if (collider.GetComponentInParent<IInteractable>() is Component inventoryTarget)
                return inventoryTarget;
            return collider.GetComponentInParent<IWorldInteractable>() as Component;
        }

        // 지금 프롬프트를 띄울 수 있나. IWorldInteractable은 IsAvailable(이미 켠 스위치는 false). 파괴된 대상은 false.
        public static bool IsUsable(Component target)
        {
            if (target == null)
                return false;
            if (target is IInteractable)
                return true;
            return target is IWorldInteractable world && world.IsAvailable;
        }

        public static bool TryDescribe(Component target, out InteractionKind kind, out string targetName)
        {
            switch (target)
            {
                case IInteractable inventoryTarget when target != null:
                    kind = inventoryTarget.Kind;
                    targetName = inventoryTarget.TargetName;
                    return true;
                case IWorldInteractable world when target != null:
                    kind = world.Kind;
                    targetName = world.TargetName;
                    return true;
                default:
                    kind = default;
                    targetName = null;
                    return false;
            }
        }

        // F 한 번. IInteractable이면 by를 넘기고, IWorldInteractable이면 사용 가능할 때만 Interact. 실행했으면 true.
        public static bool Interact(Component target, PlayerInventoryBehaviour by)
        {
            if (!IsUsable(target))
                return false;
            if (target is IInteractable inventoryTarget)
            {
                inventoryTarget.Interact(by);
                return true;
            }
            ((IWorldInteractable)target).Interact();
            return true;
        }
    }
}
