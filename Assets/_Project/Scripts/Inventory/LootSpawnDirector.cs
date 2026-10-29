using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Inventory
{
    // 레이드 루트 지점 지휘자 (docs/design/MapDensity_design.md §5.3, ③ — SYS-INV §6 루트 컨테이너).
    // OnEnable에 RaidStarted 구독 → LootSeed(0 아님)로 LootActivationPlanner 계획 → 각 LootContainer를 활성(테이블·내용물 시드) 또는 빈 컨테이너로(Q1 A).
    // 같은 시드 = 같은 활성 집합·같은 내용물(내용물 시드 = Mix(lootSeed, StableHash(slotKey))). LootSeed 0(발행자가 주지 않음)이면 아무것도 바꾸지 않는다.
    // 씬 최상위 1개, 정적 인스턴스 없음. 지휘자가 없는 씬(M2 확인 씬 등)은 LootContainer 직렬화 값 그대로(지금 동작).
    // 이미 연 컨테이너(Model 있음)는 건드리지 않고 경고. 개발 빌드·에디터는 활성 수를 한 줄 로그.
    // 세이브 영향 없음: 활성 목록·컨테이너 상태는 레이드 안에서만(RULE-03).
    [DisallowMultipleComponent]
    public sealed class LootSpawnDirector : MonoBehaviour
    {
        [SerializeField] private LootSpawnConfigSO config;

        private readonly Dictionary<string, bool> lastActive = new Dictionary<string, bool>(System.StringComparer.Ordinal);

        public LootSpawnConfigSO Config => config;

        // ── 테스트·디버그 ──
        public int LastLootSeed { get; private set; }
        public int LastSlotCount { get; private set; }
        public int LastActiveCount { get; private set; }
        public int LastSkippedOpened { get; private set; }
        public bool? WasActive(string slotKey) => lastActive.TryGetValue(slotKey ?? "", out bool a) ? a : (bool?)null;

        private void OnEnable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.Events.Subscribe<RaidStarted>(OnRaidStarted);
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.Events.Unsubscribe<RaidStarted>(OnRaidStarted);
        }

        // RaidStarted 처리 (구독 대상 — 테스트도 바로 부른다). LootSeed 0(발행자가 시드를 주지 않음)이면 아무것도 바꾸지 않는다. 적용했으면 true.
        public bool HandleRaidStarted(RaidStarted evt)
        {
            if (evt.LootSeed == 0)
                return false;
            Apply(evt.LootSeed, gameObject.scene.IsValid() ? CollectSlots() : new List<LootSpawnSlot>());
            return true;
        }

        private void OnRaidStarted(RaidStarted evt) => HandleRaidStarted(evt);

        // 이 지휘자 씬의 루트 지점 (꺼진 오브젝트 포함)
        private List<LootSpawnSlot> CollectSlots()
        {
            var slots = new List<LootSpawnSlot>();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                slots.AddRange(root.GetComponentsInChildren<LootSpawnSlot>(true));
            return slots;
        }

        // 계획·적용 (테스트는 바로 부른다). 활성 수를 돌려준다.
        public int Apply(int lootSeed, IReadOnlyList<LootSpawnSlot> slots)
        {
            lastActive.Clear();
            LastLootSeed = lootSeed;
            LastSlotCount = 0;
            LastActiveCount = 0;
            LastSkippedOpened = 0;
            if (config == null)
            {
                Debug.LogError("[LootSpawnDirector] LootSpawnConfigSO가 연결되지 않아 루트 지점을 그대로 둔다.", this);
                return 0;
            }
            if (slots == null || slots.Count == 0)
                return 0;

            var plannerSlots = new List<LootActivationPlanner.Slot>(slots.Count);
            var containers = new List<LootContainer>(slots.Count);
            foreach (LootSpawnSlot slot in slots)
            {
                if (slot == null)
                    continue;
                var container = slot.GetComponent<LootContainer>();
                if (container == null)
                {
                    Debug.LogWarning($"[LootSpawnDirector] {slot.name}: LootContainer가 없어 건너뛴다.", slot);
                    continue;
                }
                plannerSlots.Add(slot.ToPlannerSlot());
                containers.Add(container);
            }

            LootActivationPlanner.Decision[] plan = LootActivationPlanner.Plan(plannerSlots, config.RatioOf, config.minActivePerZone, lootSeed);
            for (int i = 0; i < plan.Length; i++)
            {
                LootContainer container = containers[i];
                lastActive[plan[i].Key] = plan[i].Active;
                LastSlotCount++;
                if (plan[i].Active)
                    LastActiveCount++;
                if (container.Model != null)
                {
                    LastSkippedOpened++;
                    Debug.LogWarning($"[LootSpawnDirector] {container.name}: 이미 열린 컨테이너라 이번 레이드 설정을 건너뛴다.", container);
                    continue;
                }
                LootSpawnAssignment.Result result = LootSpawnAssignment.Resolve(plannerSlots[i], plan[i], config, container.SerializedLootTable);
                LootSpawnAssignment.Apply(container, result);
            }
            if (Debug.isDebugBuild)
                Debug.Log($"[LootSpawnDirector] 루트 지점 {LastSlotCount}곳 중 활성 {LastActiveCount} (lootSeed {lootSeed})");
            return LastActiveCount;
        }
    }
}
