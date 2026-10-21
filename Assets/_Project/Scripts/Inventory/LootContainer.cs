using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Inventory
{
    // 루트 컨테이너 (§6). 씬 상자는 고정 내용물(TODO(M4): LootTableSO 굴림)이고,
    // M3 시체는 AttachRuntime으로 런타임에 붙는다(내용물·모델이 바로 정해진다).
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

        // 런타임 컨테이너 (M3 시체, §6). 컴포넌트를 붙이고 모델을 즉시 만든다.
        // 아이템은 주어진 순서로 배치되고 같은 순서로 검색에 드러난다. 그리드에 안 들어가면 경고 후 제외(M2 규칙).
        // host에 이미 LootContainer가 있으면 경고하고 기존 것을 그대로 돌려준다 (내용물을 덮어쓰지 않는다).
        public static LootContainer AttachRuntime(GameObject host, string displayName, Vector2Int gridSize,
            IEnumerable<ItemInstance> items, float searchTimePerItemSec)
        {
            if (host == null)
                return null;

            var existing = host.GetComponent<LootContainer>();
            if (existing != null)
            {
                Debug.LogWarning($"[LootContainer] {host.name}에 이미 루트 컨테이너가 있어 새로 붙이지 않는다.", host);
                return existing;
            }

            var container = host.AddComponent<LootContainer>();
            container.displayName = displayName;
            container.gridSize = gridSize;
            container.Model = new LootContainerModel(displayName, gridSize, items, searchTimePerItemSec);
            return container;
        }

        // EnemyKilled → 시체 루트 컨테이너 (M3 설계 §1.9). 시체가 없거나 파괴됐으면 null.
        public static LootContainer AttachCorpse(in EnemyKilled evt, InventoryConfigSO config)
        {
            if (evt.Corpse == null || config == null)
                return null;
            return AttachRuntime(evt.Corpse, evt.DisplayName, config.corpseGridSize, evt.Items,
                config.searchTimePerItemSec);
        }
    }
}
