using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Inventory
{
    // 바닥에 놓인 아이템 하나 (그레이박스 상자 + Rigidbody). F로 줍는다.
    // 씬 배치: definition을 넣으면 시작 시 런타임 아이템을 만든다. 버린 아이템: Spawn이 기존 인스턴스(탄 수 등)를 그대로 넣는다.
    public sealed class WorldItem : MonoBehaviour, IInteractable
    {
        [SerializeField] private ItemDefinitionSO definition;

        public ItemInstance Item { get; private set; }

        public InteractionKind Kind => InteractionKind.PickUp;
        public string TargetName => Item != null ? Item.Definition.displayName : string.Empty;

        private void Awake()
        {
            if (Item == null && definition != null)
                Item = ItemFactory.Create(definition);
        }

        // 공간이 없으면 그대로 둔다. TODO(GDD-?): "공간 없음" 표시 없음.
        public void Interact(PlayerInventoryBehaviour by)
        {
            if (Item == null || by == null || by.Inventory == null)
                return;
            if (by.Inventory.TryAutoStore(Item))
            {
                Item = null;
                Destroy(gameObject);
            }
        }

        // 바닥 아이템 생성. 크기 = 칸 수 × worldItemCellM (그레이박스 표현).
        public static WorldItem Spawn(ItemInstance item, Vector3 position, Vector3 velocity, InventoryConfigSO config)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"WorldItem_{item.Definition.id}";
            Vector2Int size = item.Definition.size;
            float cell = config.worldItemCellM;
            go.transform.position = position;
            go.transform.localScale = new Vector3(size.x * cell, cell, size.y * cell);

            var body = go.AddComponent<Rigidbody>();
            body.linearVelocity = velocity;

            var worldItem = go.AddComponent<WorldItem>();
            worldItem.Item = item;
            return worldItem;
        }
    }
}
