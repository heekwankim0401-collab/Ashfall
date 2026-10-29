using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Inventory
{
    // 루트 컨테이너 (§6). 씬 상자는 고정 내용물(contents) + M4 루트 테이블 굴림(TODO(GDD-?) Q17)이고,
    // M3 시체는 AttachRuntime으로 런타임에 붙는다(내용물·모델이 바로 정해진다).
    // 처음 열 때 한 번 런타임 아이템을 만들고, 이후에는 같은 모델(검색 진행 포함)을 다시 연다.
    public sealed class LootContainer : MonoBehaviour, IInteractable
    {
        [SerializeField] private string displayName;
        [SerializeField] private Vector2Int gridSize;
        [SerializeField] private ItemDefinitionSO[] contents;
        // M4 맵 상자 (TODO(GDD-?) Q17): 처음 열 때 contents 뒤에 이 테이블 굴림 결과를 더한다. null = 고정 내용물만.
        [SerializeField] private LootTableSO lootTable;
        [Tooltip("루트 테이블 굴림 시드. 0 = 매번 무작위")]
        [SerializeField] private int seed;

        public LootContainerModel Model { get; private set; }
        // 이번 레이드에 실제로 쓸 테이블·시드 (지휘자가 정했으면 그 값, 아니면 직렬화 값)
        public LootTableSO LootTable => hasRaidOverride ? raidTable : lootTable;
        public int Seed => hasRaidOverride ? raidSeed : seed;
        // 직렬화된(씬에 저장된) 원본 — 지휘자는 이것을 바꾸지 않는다(리뷰 L1)
        public LootTableSO SerializedLootTable => lootTable;
        public string DisplayName => displayName;
        public Vector2Int GridSize => gridSize;
        public IReadOnlyList<ItemDefinitionSO> FixedContents => contents;
        // 맵 밀도 ③: 이번 레이드에 LootSpawnDirector가 비활성으로 정한 빈 컨테이너 (열 수 있고 비어 있다 — Q1 A)
        public bool IsEmptyForRaid { get; private set; }

        // 레이드 설정(런타임 전용, 직렬화 안 함 — 직렬화 필드는 원본 그대로 둔다)
        private bool hasRaidOverride;
        private bool raidSkipFixed;
        private LootTableSO raidTable;
        private int raidSeed;

        // 맵 밀도 ③ (설계 §5.3): 레이드 시작 때 지휘자가 이번 레이드의 테이블·내용물 시드를 정한다. 고정 내용물(신호탄 상자)은 그대로 쓴다
        // (앞서 ConfigureEmpty를 불렀으면 고정 내용물은 계속 건너뛴다 — JunkOnly). 처음 열기(Model 생성) 전에만 유효 — 이미 열었으면 false.
        // seed 0은 1로(0 = "매번 무작위"와 섞이지 않게).
        public bool ConfigureForRaid(LootTableSO table, int contentSeed)
        {
            if (Model != null)
                return false;
            hasRaidOverride = true;
            raidTable = table;
            raidSeed = SeedMath.NonZero(contentSeed);
            IsEmptyForRaid = false;
            return true;
        }

        // 맵 밀도 ③ 비활성 = 빈 컨테이너 (Q1 A): 이번 레이드에는 고정 내용물·테이블을 쓰지 않는다(직렬화 필드는 그대로).
        // 오브젝트·콜라이더·프롬프트는 그대로라 겉으로는 구분되지 않는다. 처음 열기 전에만 유효. 열면 빈 그리드 → 검색이 바로 끝난다.
        public bool ConfigureEmpty()
        {
            if (Model != null)
                return false;
            hasRaidOverride = true;
            raidSkipFixed = true;
            raidTable = null;
            raidSeed = 0;
            IsEmptyForRaid = true;
            return true;
        }

        public InteractionKind Kind => InteractionKind.OpenContainer;
        public string TargetName => displayName;

        public void Interact(PlayerInventoryBehaviour by)
        {
            // 플레이어 쪽 config가 비면 브리지가 꺼지고 Inventory가 null이다 (거기서 LogError).
            if (by == null || by.Inventory == null || by.Config == null)
                return;
            by.Inventory.OpenLoot(EnsureModel(by.Config.searchTimePerItemSec));
        }

        // 처음 부를 때 내용물을 만들고(고정 내용물 → 테이블 굴림 순, 이 순서로 배치·검색) 이후에는 같은 모델을 돌려준다.
        // seed 0이면 매번 다른 굴림(UnityEngine.Random에서 시드를 뽑는다).
        public LootContainerModel EnsureModel(float searchTimePerItemSec)
        {
            if (Model != null)
                return Model;
            int s = Seed;
            var random = new System.Random(s != 0 ? s : Random.Range(1, int.MaxValue));
            Model = new LootContainerModel(displayName, gridSize, CreateContents(raidSkipFixed ? null : contents, LootTable, random),
                searchTimePerItemSec);
            return Model;
        }

        // 고정 내용물(null 제외, 순서 유지) 뒤에 LootRoller.Roll(table) 결과를 붙인 새 런타임 아이템 목록.
        public static List<ItemInstance> CreateContents(IReadOnlyList<ItemDefinitionSO> fixedContents, LootTableSO table,
            System.Random random)
        {
            var items = new List<ItemInstance>();
            if (fixedContents != null)
                for (int i = 0; i < fixedContents.Count; i++)
                    if (fixedContents[i] != null)
                        items.Add(ItemFactory.Create(fixedContents[i]));
            if (table != null && random != null)
                LootRoller.Roll(table, random, items);
            return items;
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
