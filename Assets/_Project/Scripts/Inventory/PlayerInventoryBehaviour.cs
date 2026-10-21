using System;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashfall.Inventory
{
    // SYS-INV 플레이어 인벤토리의 얇은 브리지 (플레이어 루트에 붙는다). 규칙은 PlayerInventory에 있다.
    // - IInventoryService·IMagazineSource 등록, 모델 변화 → 버스 이벤트
    // - F 상호작용(레이캐스트), 4~7 의약품 퀵슬롯, 열린 루트 검색 진행
    //   M4: 대상에 Core IWorldInteractable(다른 기능의 장치, EXT-02 전원 스위치)도 포함 (InteractionTargets)
    // - ItemDropRequested·버리기 → WorldItem 생성, 의약품 사용 시작/완료 → 잠금/차감, 사망 → 잠금
    // - M3: EnemyKilled → 시체에 LootContainer를 붙인다 (§6 시체)
    // - M4: RaidEnded → 인벤토리 잠금(IsRaidEndLocked, 사망 잠금과 같은 범위)
    public sealed class PlayerInventoryBehaviour : MonoBehaviour
    {
        [Serializable]
        public struct StartingItem
        {
            public ItemDefinitionSO item;
            public bool equip;
            public EquipSlot slot;
            [Tooltip("무기일 때 꽉 찬 상태로 장착할 탄창")]
            public MagazineDefinitionSO loadedMagazine;
            [Tooltip("의약품 퀵슬롯 0~3 (키 4~7), -1 = 없음")]
            public int quickSlot;
        }

        [SerializeField] private InventoryConfigSO config;
        [Tooltip("상호작용 레이캐스트 시작점·방향 (카메라)")]
        [SerializeField] private Transform viewOrigin;
        [SerializeField] private StartingItem[] startingItems;

        private InputAction interact;
        private readonly InputAction[] quickSlotActions = new InputAction[PlayerInventory.QuickSlotCount];
        private bool screenOpen;
        private bool dead;
        private Component currentTarget;
        private bool promptVisible;
        private float publishedWeightKg = float.NaN;
        // 상호작용 레이캐스트 캐시: 같은 Collider면 GetComponentInParent를 다시 부르지 않는다.
        private Collider lastHitCollider;
        private Component lastHitInteractable;
        private readonly RaycastHit[] interactHits = new RaycastHit[8];

        public PlayerInventory Inventory { get; private set; }
        public InventoryConfigSO Config => config;

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[PlayerInventory] InventoryConfigSO가 연결되지 않았다.", this);
                enabled = false;
                return;
            }
            Inventory = new PlayerInventory(config);
            ApplyStartingItems();

            Inventory.Changed += OnInventoryChanged;
            Inventory.EquipmentChanged += OnEquipmentChanged;
            Inventory.Dropped += OnDropped;
            Inventory.UseRequested += OnUseRequested;
            Inventory.LootSessionChanged += OnLootSessionChanged;

            var map = InputSystem.actions.FindActionMap("Player", throwIfNotFound: true);
            interact = map.FindAction("Interact", true);
            for (int i = 0; i < quickSlotActions.Length; i++)
                quickSlotActions[i] = map.FindAction($"QuickSlot{i + 1}", true);
            map.Enable();
        }

        private void OnEnable()
        {
            if (Inventory == null || GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Register<IInventoryService>(Inventory);
            GameManager.Instance.Services.Register<IMagazineSource>(Inventory);
            var events = GameManager.Instance.Events;
            events.Subscribe<ItemDropRequested>(OnItemDropRequested);
            events.Subscribe<MedicalUseStarted>(OnMedicalUseStarted);
            events.Subscribe<MedicalUseCompleted>(OnMedicalUseCompleted);
            events.Subscribe<PlayerDied>(OnPlayerDied);
            events.Subscribe<RaidEnded>(OnRaidEnded);
            events.Subscribe<ScreenStateChanged>(OnScreenStateChanged);
            events.Subscribe<EnemyKilled>(OnEnemyKilled);
        }

        private void OnDisable()
        {
            if (Inventory == null || GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Unregister<IInventoryService>(Inventory);
            GameManager.Instance.Services.Unregister<IMagazineSource>(Inventory);
            var events = GameManager.Instance.Events;
            events.Unsubscribe<ItemDropRequested>(OnItemDropRequested);
            events.Unsubscribe<MedicalUseStarted>(OnMedicalUseStarted);
            events.Unsubscribe<MedicalUseCompleted>(OnMedicalUseCompleted);
            events.Unsubscribe<PlayerDied>(OnPlayerDied);
            events.Unsubscribe<RaidEnded>(OnRaidEnded);
            events.Unsubscribe<ScreenStateChanged>(OnScreenStateChanged);
            events.Unsubscribe<EnemyKilled>(OnEnemyKilled);
        }

        // 초기 상태를 한 번 알린다 (소비자는 OnEnable에서 이미 구독했다).
        private void Start()
        {
            PublishWeightIfChanged();
            foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
                Publish(new EquipmentChanged(slot, Inventory.GetEquipped(slot)));
            Publish(new InventoryChanged());
        }

        // 순서대로: equip이면 장착(무기는 꽉 찬 탄창 장착), 아니면 자동 배치(조끼 → 주머니 → 배낭).
        private void ApplyStartingItems()
        {
            if (startingItems == null)
                return;
            foreach (StartingItem entry in startingItems)
            {
                if (entry.item == null)
                    continue;
                ItemInstance instance = ItemFactory.Create(entry.item);
                if (instance is WeaponItem weapon && entry.loadedMagazine != null)
                    weapon.LoadedMagazine = Magazine.Full(entry.loadedMagazine);

                bool ok = entry.equip ? Inventory.TryEquipNew(instance, entry.slot) : Inventory.TryAutoStore(instance);
                if (!ok)
                {
                    Debug.LogWarning($"[PlayerInventory] 시작 장비 {instance}를 {(entry.equip ? entry.slot.ToString() : "저장 공간")}에 넣지 못했다.", this);
                    continue;
                }
                // 퀵슬롯은 의약품만 (직렬화 기본값 0이 다른 아이템에 붙지 않게)
                if (instance is MedicalItem && entry.quickSlot >= 0 && !Inventory.TryAssignQuickSlot(entry.quickSlot, instance))
                    Debug.LogWarning($"[PlayerInventory] 시작 장비 {instance}를 퀵슬롯 {entry.quickSlot}에 넣지 못했다.", this);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // 루트 검색은 열려 있는 동안 진행 (화면이 열려 있어도)
            LootContainerModel loot = Inventory.ActiveLootModel;
            if (loot != null && loot.Tick(dt))
                Publish(new InventoryChanged());

            if (screenOpen || dead || Inventory.IsRaidEndLocked)
            {
                SetTarget(null);
                return;
            }

            UpdateInteractionTarget();
            if (currentTarget != null && interact.WasPressedThisFrame() && InteractionTargets.Interact(currentTarget, this))
            {
                // M4: 대상 상태가 바뀔 수 있다(켠 스위치는 IsAvailable=false) → 캐시를 비우고 다음 프레임에 다시 찾는다.
                lastHitCollider = null;
                lastHitInteractable = null;
            }

            for (int i = 0; i < quickSlotActions.Length; i++)
            {
                if (!quickSlotActions[i].WasPressedThisFrame())
                    continue;
                ItemInstance item = Inventory.GetQuickSlot(i);
                if (item != null)
                    Inventory.TryUse(item);
            }
        }

        // 무기 재장전처럼 인벤토리 밖에서 무게가 바뀌는 경우까지 매 프레임 확인한다.
        // TotalWeightKg는 할당 없이 계산한다 (그리드 배열 캐시 + 인덱스 순회).
        private void LateUpdate() => PublishWeightIfChanged();

        private void UpdateInteractionTarget()
        {
            Component found = null;
            if (viewOrigin != null && TryRaycastIgnoringSelf(out RaycastHit hit))
            {
                Collider hitCollider = hit.collider;
                if (hitCollider != lastHitCollider)
                {
                    lastHitCollider = hitCollider;
                    // M4: Inventory IInteractable 우선, 없으면 Core IWorldInteractable (EXT-02 전원 스위치)
                    lastHitInteractable = InteractionTargets.Find(hitCollider);
                }
                // 파괴된 대상(주운 WorldItem)은 Unity null 비교로, 이미 켠 스위치는 IsAvailable로 매 프레임 걸러진다.
                if (InteractionTargets.IsUsable(lastHitInteractable))
                    found = lastHitInteractable;
            }
            SetTarget(found);
        }

        // 자기 몸(플레이어 캡슐)을 건너뛴 가장 가까운 충돌. 할당 없음(고정 버퍼).
        // 눈(카메라)이 PhysX 캐릭터 캡슐(컨트롤러의 0.8배) 꼭대기보다 높아서, 아래를 보면 레이가 자기 캡슐을 먼저 맞혀
        // 발밑의 시체·아이템을 F로 열 수 없었다 (M3 4단계 CorpseLootFlowTests에서 발견).
        private bool TryRaycastIgnoringSelf(out RaycastHit nearest)
        {
            int count = Physics.RaycastNonAlloc(viewOrigin.position, viewOrigin.forward, interactHits,
                config.interactRangeM, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Transform self = transform.root;
            nearest = default;
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = interactHits[i];
                if (h.collider.transform.root == self || (any && h.distance >= nearest.distance))
                    continue;
                nearest = h;
                any = true;
            }
            return any;
        }

        // 대상이 바뀔 때만 프롬프트 이벤트
        private void SetTarget(Component target)
        {
            if (target == currentTarget && promptVisible == (target != null))
                return;

            currentTarget = target;
            promptVisible = target != null;
            if (InteractionTargets.TryDescribe(target, out InteractionKind kind, out string targetName))
                Publish(new InteractionPromptChanged(true, kind, targetName));
            else
                Publish(new InteractionPromptChanged(false, default, null));
        }

        private void PublishWeightIfChanged()
        {
            float weight = Inventory.TotalWeightKg;
            if (weight.Equals(publishedWeightKg))
                return;
            publishedWeightKg = weight;
            Publish(new CarriedWeightChanged(weight));
        }

        // ── 모델 → 버스 ───────────────────────────────────────

        private void OnInventoryChanged()
        {
            Publish(new InventoryChanged());
            PublishWeightIfChanged();
        }

        private void OnEquipmentChanged(EquipSlot slot, ItemInstance item) => Publish(new EquipmentChanged(slot, item));
        private void OnUseRequested(MedicalItem item) => Publish(new MedicalUseRequested(item));
        private void OnLootSessionChanged(bool open) => Publish(new LootSessionChanged(open));

        // 플레이어가 버린 아이템: 시선 앞에 떨어뜨린다.
        private void OnDropped(ItemInstance item)
        {
            Transform origin = viewOrigin != null ? viewOrigin : transform;
            WorldItem.Spawn(item, origin.position + origin.forward * config.dropForwardM,
                origin.forward * config.dropSpeed, config);
        }

        // ── 버스 → 모델 ───────────────────────────────────────

        private void OnItemDropRequested(ItemDropRequested evt)
        {
            if (evt.Item != null)
                WorldItem.Spawn(evt.Item, evt.Position, evt.Velocity, config);
        }

        // 적 사망 → 시체 루트 컨테이너 (M3 설계 §1.9). 시체가 없거나 파괴됐으면 무시.
        // 상호작용 캐시를 비운다: 쏘는 동안 보던 적 콜라이더가 "상호작용 없음"으로 캐시돼 있으면 시체가 잡히지 않는다.
        private void OnEnemyKilled(EnemyKilled evt)
        {
            if (LootContainer.AttachCorpse(evt, config) == null)
                return;
            lastHitCollider = null;
            lastHitInteractable = null;
        }

        private void OnMedicalUseStarted(MedicalUseStarted evt)
        {
            Inventory.SetItemLocked(evt.Item, true);
            Publish(new InventoryChanged());
        }

        private void OnMedicalUseCompleted(MedicalUseCompleted evt)
        {
            Inventory.SetItemLocked(evt.Item, false);
            Inventory.ConsumeMedicalUse(evt.Item);
        }

        private void OnPlayerDied(PlayerDied evt)
        {
            dead = true;
            Inventory.CloseLoot();
            Inventory.IsDeadLocked = true;
            SetTarget(null);
        }

        // M4 리뷰 수정(RULE-01): 결과 스냅샷(RaidEnded 직전) 이후 소지품이 바뀌지 않게 사망과 같은 잠금을 건다.
        // 재장전 탄창 교환(TakeFullest/TryStore)·의약품 사용(TryUse)·사용 완료 차감·신호탄 소모·줍기 모두 거부된다.
        // 열린 루트는 닫지 않는다 (LootSessionChanged가 종료 오버레이와 엉키지 않게, 루트는 소지품이 아니다).
        private void OnRaidEnded(RaidEnded evt)
        {
            Inventory.IsRaidEndLocked = true;
            SetTarget(null);
        }

        // UI가 화면을 모두 닫으면 열린 루트도 닫는다.
        private void OnScreenStateChanged(ScreenStateChanged evt)
        {
            screenOpen = evt.Screen != ScreenKind.None;
            if (!screenOpen)
                Inventory.CloseLoot();
        }

        private static void Publish<T>(in T evt) where T : struct, IGameEvent
        {
            if (GameManager.Instance != null)
                GameManager.Instance.Events.Publish(evt);
        }
    }
}
