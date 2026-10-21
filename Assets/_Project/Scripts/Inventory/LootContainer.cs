using System.Linq;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Inventory
{
    // 씬의 루트 상자 (§6). 고정 내용물. TODO(M3/M4): LootTableSO 굴림.
    // 처음 열 때 한 번 런타임 아이템을 만들고, 이후에는 같은 모델(검색 진행 포함)을 다시 연다.
    public sealed class LootContainer : MonoBehaviour, IInteractable
    {
        [SerializeField] private string displayName;
        [SerializeField] private Vector2Int gridSize;
        [SerializeField] private ItemDefinitionSO[] contents;

        public LootContainerModel Model { get; private set; }

        public InteractionKind Kind => InteractionKind.OpenContainer;
        public string TargetName => displayName;

        public void Interact(PlayerInventoryBehaviour by)
        {
            // 플레이어 쪽 config가 비면 브리지가 꺼지고 Inventory가 null이다 (거기서 LogError).
            if (by == null || by.Inventory == null || by.Config == null)
                return;
            if (Model == null)
            {
                var items = (contents ?? new ItemDefinitionSO[0])
                    .Where(d => d != null)
                    .Select(ItemFactory.Create);
                Model = new LootContainerModel(displayName, gridSize, items, by.Config.searchTimePerItemSec);
            }
            by.Inventory.OpenLoot(Model);
        }
    }
}
