using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Inventory
{
    // 은신처의 얇은 인벤토리 브리지 (M5 설계 §4.3, Q27·Q28·Q29). 은신처 장비 + 보관함(열린 루트 자리)을 기존 인벤토리 화면으로 연다.
    // - Awake: 프로필(HideoutController가 -100에서 읽고 진입 저장까지 끝냄)의 은신처 장비를 PlayerInventory에 위치 그대로 놓고,
    //   놓지 못한 것은 보관함에 자동 배치(안 되면 버림 + 경고), 보관함을 전부 드러난 루트로 연다. 버리기·의약품 사용은 끈다.
    // - OnEnable/OnDisable: IInventoryService 등록·해지(IMagazineSource는 등록하지 않는다 — 은신처엔 재장전이 없다), HideoutChanged 구독.
    // - 모델 Changed → InventoryChanged, EquipmentChanged → EquipmentChanged. HideoutChanged(상인·키트가 보관함을 바꿈) → InventoryChanged.
    // - 열린 루트(보관함)가 닫히면 바로 다시 연다 (은신처 장비 화면은 항상 보관함과 함께).
    // 은신처의 InventoryChanged는 이 브리지만 발행한다. 장비 → profile.Loadout 반영(저장 때 Loadout.Capture)은 Hideout(HideoutService) 몫.
    public sealed class HideoutInventoryBehaviour : MonoBehaviour
    {
        private static readonly EquipSlot[] Slots = (EquipSlot[])Enum.GetValues(typeof(EquipSlot));

        [SerializeField] private InventoryConfigSO config;
        [Tooltip("보관함 패널 제목 (열린 루트 자리)")]
        [SerializeField] private string stashDisplayName = "보관함";

        private bool subscribed;

        public InventoryConfigSO Config => config;
        public string StashDisplayName => stashDisplayName;

        // ── 테스트·디버그 ──
        public PlayerInventory Inventory { get; private set; }
        public LootContainerModel StashLoot { get; private set; }
        // 은신처 장비에도 보관함에도 놓지 못해 버린 아이템 수 (정상 경로에선 0)
        public int DroppedCount { get; private set; }

        // 순수 준비 (EditMode 테스트용, MonoBehaviour 없이). 순서:
        // ① PlayerInventory(AllowDrop/AllowUse false, Q27) ② profile.Loadout을 위치 그대로(TryApplyLoadout)
        // ③ 거부된 것은 보관함 자동 배치(위→아래·왼→오른쪽 = Hideout StashRules와 같은 TryFindFreeCell) → 안 되면 dropped
        // ④ 보관함을 열린 루트로(ForExistingGrid) ⑤ profile.Loadout = 실제로 놓인 장비(Capture) — 보관함으로 옮긴 아이템이
        //    은신처 장비에도 남아 두 번 저장되지 않게(복제 방지).
        public static PlayerInventory CreateInventory(InventoryConfigSO config, PlayerProfile profile, string stashDisplayName,
            out LootContainerModel stashLoot, List<ItemInstance> dropped = null)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var inventory = new PlayerInventory(config) { AllowDrop = false, AllowUse = false };
            var rejected = new List<ItemInstance>();
            inventory.TryApplyLoadout(profile.Loadout, rejected);

            ItemGrid stash = profile.Stash;
            for (int i = 0; i < rejected.Count; i++)
            {
                ItemInstance item = rejected[i];
                if (stash.TryFindFreeCell(item, out Vector2Int cell, out bool rotated) && stash.TryPlace(item, cell, rotated))
                    continue;
                // TODO(GDD-?): 보관함도 가득 차면 버린다 (설계 §2.4 "안 되면 버림").
                dropped?.Add(item);
            }

            stashLoot = LootContainerModel.ForExistingGrid(stashDisplayName, stash);
            inventory.OpenLoot(stashLoot);
            profile.Loadout = Loadout.Capture(inventory);
            return inventory;
        }

        // 진입 완료 표시(ProfileService.HideoutEntryToken == 이 씬의 handle)를 확인한 뒤에만 장비를 놓는다 (M5 리뷰 5).
        // 실행 순서(-100)가 어긋나 아직 진입 전이면 Start(같은 씬의 모든 Awake 뒤)에서 한 번 더 시도한다 —
        // 결과 반영 전의 빈 장비를 놓고 다음 저장이 반출품을 지우는 일이 없게. 그래도 아니면 오류 + 비활성(장비를 건드리지 않는다).
        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[HideoutInventory] InventoryConfigSO가 연결되지 않았다.", this);
                enabled = false;
                return;
            }
            if (!IsEntryComplete(out _))
            {
                waitingForEntry = true;   // OnEnable은 Inventory가 없어 등록하지 않는다 → Start에서
                return;
            }
            Initialize();
        }

        private bool waitingForEntry;

        // 프로필이 로드됐고 이 씬의 진입 절차가 끝났나
        public static bool IsEntryComplete(ProfileService profiles, int sceneToken) =>
            profiles != null && profiles.IsLoaded && profiles.IsHideoutEntered(sceneToken);

        private bool IsEntryComplete(out ProfileService profiles)
        {
            profiles = GameManager.Instance != null ? GameManager.Instance.Profiles : null;
            return IsEntryComplete(profiles, gameObject.scene.handle);
        }

        private void Initialize()
        {
            IsEntryComplete(out ProfileService profiles);
            var dropped = new List<ItemInstance>();
            Inventory = CreateInventory(config, profiles.Profile, stashDisplayName, out LootContainerModel loot, dropped);
            StashLoot = loot;
            DroppedCount = dropped.Count;
            if (dropped.Count > 0)
                Debug.LogWarning($"[HideoutInventory] 은신처 장비 {dropped.Count}개를 장비에도 보관함에도 놓지 못해 버렸다 (첫 번째: {dropped[0]}).", this);

            Inventory.Changed += OnInventoryChanged;
            Inventory.EquipmentChanged += OnEquipmentChanged;
            Inventory.LootSessionChanged += OnLootSessionChanged;
        }

        private void OnEnable()
        {
            if (Inventory == null || GameManager.Instance == null || subscribed)
                return;
            GameManager.Instance.Services.Register<IInventoryService>(Inventory);
            GameManager.Instance.Events.Subscribe<HideoutChanged>(OnHideoutChanged);
            subscribed = true;
        }

        private void OnDisable()
        {
            if (!subscribed || GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Unregister<IInventoryService>(Inventory);
            GameManager.Instance.Events.Unsubscribe<HideoutChanged>(OnHideoutChanged);
            subscribed = false;
        }

        private void OnDestroy()
        {
            if (Inventory == null)
                return;
            Inventory.Changed -= OnInventoryChanged;
            Inventory.EquipmentChanged -= OnEquipmentChanged;
            Inventory.LootSessionChanged -= OnLootSessionChanged;
        }

        // 초기 상태를 한 번 알린다 (UI는 Start/Update에서 서비스를 찾고 다시 읽는다).
        private void Start()
        {
            if (waitingForEntry)
            {
                waitingForEntry = false;
                if (!IsEntryComplete(out _))
                {
                    Debug.LogError("[HideoutInventory] 은신처 진입 절차가 끝나지 않았다 (HideoutController 실패?). 장비를 열지 않는다.", this);
                    enabled = false;
                    return;
                }
                Initialize();
                OnEnable();
            }
            if (Inventory == null)
                return;
            foreach (EquipSlot slot in Slots)
                Publish(new EquipmentChanged(slot, Inventory.GetEquipped(slot)));
            Publish(new InventoryChanged());
        }

        private void OnInventoryChanged() => Publish(new InventoryChanged());
        private void OnEquipmentChanged(EquipSlot slot, ItemInstance item) => Publish(new EquipmentChanged(slot, item));

        // UI가 CloseLoot를 불러도 보관함은 계속 열려 있어야 한다.
        private void OnLootSessionChanged(bool open)
        {
            if (!open && StashLoot != null)
                Inventory.OpenLoot(StashLoot);
        }

        // 상인 매매·키트 지급이 보관함 그리드를 바꿨다 → 인벤토리 화면이 다시 그린다.
        private void OnHideoutChanged(HideoutChanged evt) => Publish(new InventoryChanged());

        private static void Publish<T>(in T evt) where T : struct, IGameEvent
        {
            if (GameManager.Instance != null)
                GameManager.Instance.Events.Publish(evt);
        }
    }
}
