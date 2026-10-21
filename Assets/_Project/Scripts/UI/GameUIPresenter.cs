using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.UI
{
    // 게임 UI의 상태 로직 (설계 §5.4). 버스·레지스트리를 생성자로 받아 EditMode에서 그대로 테스트한다.
    // - 기능 데이터는 Core 인터페이스(IInventoryService, IBodyStatusView)로 읽고, 변경은 이벤트로 받는다.
    // - 화면이 바뀌면 ScreenStateChanged를 발행한다(게임플레이 입력 차단은 각 기능이 한다).
    // - 그리기·입력·커서는 GameUIRoot(MonoBehaviour)와 뷰가 한다.
    public sealed class GameUIPresenter
    {
        private readonly EventBus bus;
        private readonly ServiceRegistry services;
        private readonly HashSet<ScreenKind> warnedMissing = new HashSet<ScreenKind>();
        private bool subscribed;

        public GameUIPresenter(EventBus bus, ServiceRegistry services, HudConfigSO config)
        {
            this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
            this.services = services ?? throw new ArgumentNullException(nameof(services));
            Toast = new MagazineCheckToastModel(config);
            Screens.CanOpen = ResolveServiceFor;
            Screens.Changed += OnScreenChanged;
        }

        public ScreenStateModel Screens { get; } = new ScreenStateModel();
        public MagazineCheckToastModel Toast { get; }
        public InteractionPromptModel Prompt { get; } = new InteractionPromptModel();

        // 인벤토리 화면을 열 때 레지스트리에서 얻는다. 열기 전에는 null.
        public IInventoryService InventoryService { get; private set; }
        public InventoryDragModel Drag { get; private set; }
        public IBodyStatusView BodyView { get; private set; }

        // F 프롬프트 앞에 붙는 키 이름 (입력 바인딩 표시 문자열). 루트가 채운다.
        public string InteractKeyLabel { get; set; } = string.Empty;

        public event Action<ScreenKind> ScreenChanged;
        public event Action InventoryDirty;     // 인벤토리 화면을 다시 그려야 함
        public event Action BodyStatusDirty;    // 부위 상태 화면을 다시 그려야 함

        public void Enable()
        {
            if (subscribed)
                return;
            subscribed = true;
            bus.Subscribe<InventoryChanged>(OnInventoryChanged);
            bus.Subscribe<LootSessionChanged>(OnLootSessionChanged);
            bus.Subscribe<PlayerBodyStatusChanged>(OnBodyStatusChanged);
            bus.Subscribe<MagazineCheckStarted>(OnMagazineCheckStarted);
            bus.Subscribe<MagazineCheckCompleted>(OnMagazineCheckCompleted);
            bus.Subscribe<InteractionPromptChanged>(OnInteractionPromptChanged);
            bus.Subscribe<PlayerDied>(OnPlayerDied);
        }

        // 화면이 열려 있으면 닫고(조작 차단 해제) 구독을 끊는다.
        public void Disable()
        {
            if (!subscribed)
                return;
            Screens.CloseAll();
            subscribed = false;
            bus.Unsubscribe<InventoryChanged>(OnInventoryChanged);
            bus.Unsubscribe<LootSessionChanged>(OnLootSessionChanged);
            bus.Unsubscribe<PlayerBodyStatusChanged>(OnBodyStatusChanged);
            bus.Unsubscribe<MagazineCheckStarted>(OnMagazineCheckStarted);
            bus.Unsubscribe<MagazineCheckCompleted>(OnMagazineCheckCompleted);
            bus.Unsubscribe<InteractionPromptChanged>(OnInteractionPromptChanged);
            bus.Unsubscribe<PlayerDied>(OnPlayerDied);
        }

        // ── 입력 (GameUIRoot가 호출) ─────────────────────────

        // Tab
        public bool ToggleInventory()
        {
            Drag?.Cancel();
            return Screens.ToggleInventory();
        }

        // H
        public bool ToggleBodyStatus()
        {
            Drag?.Cancel();
            return Screens.ToggleBodyStatus();
        }

        // Esc: 드래그 중이면 드래그만 취소, 아니면 화면을 모두 닫는다.
        public bool Cancel()
        {
            if (Drag != null && Drag.IsDragging)
            {
                Drag.Cancel();
                return true;
            }
            return Screens.CloseAll();
        }

        // R (인벤토리 드래그 중에만 회전)
        public bool RotateDragged()
        {
            if (Screens.Current != ScreenKind.Inventory || Drag == null || !Drag.IsDragging)
                return false;
            Drag.ToggleRotate();
            return true;
        }

        // 4~7: 인벤토리가 열려 있고 마우스 아래에 아이템이 있으면 퀵슬롯 배정 (판정은 서비스가 한다)
        public bool AssignQuickSlot(int index, ItemInstance hovered)
        {
            if (Screens.Current != ScreenKind.Inventory || InventoryService == null || hovered == null)
                return false;
            return InventoryService.TryAssignQuickSlot(index, hovered);
        }

        public void Tick(float dt) => Toast.Tick(dt);

        // ── 화면 ────────────────────────────────────────────

        // 화면을 열 때마다 레지스트리에서 다시 얻는다 (플레이어가 바뀌었을 수 있다). 없으면 열지 않는다.
        private bool ResolveServiceFor(ScreenKind kind)
        {
            switch (kind)
            {
                case ScreenKind.Inventory:
                    if (services.TryGet(out IInventoryService inventory))
                    {
                        if (!ReferenceEquals(inventory, InventoryService))
                        {
                            InventoryService = inventory;
                            Drag = new InventoryDragModel(inventory);
                        }
                        return true;
                    }
                    break;
                case ScreenKind.BodyStatus:
                    if (services.TryGet(out IBodyStatusView body))
                    {
                        BodyView = body;
                        return true;
                    }
                    break;
                default:
                    return true;
            }

            if (warnedMissing.Add(kind))
                Debug.LogWarning($"[GameUI] {kind} 화면에 필요한 서비스가 등록되지 않아 열지 않는다.");
            return false;
        }

        private void OnScreenChanged(ScreenKind screen)
        {
            if (screen != ScreenKind.Inventory)
            {
                Drag?.Cancel();
                // 루트 패널은 인벤토리 화면에서만 보인다. 다른 화면으로 바꾸거나 닫으면 루트도 닫는다(검색 정지).
                if (InventoryService != null && InventoryService.ActiveLoot != null)
                    InventoryService.CloseLoot();
            }

            bus.Publish(new ScreenStateChanged(screen));
            ScreenChanged?.Invoke(screen);
        }

        // ── 이벤트 ──────────────────────────────────────────

        private void OnInventoryChanged(InventoryChanged _)
        {
            // 드래그 중 아이템이 다른 경로로 빠졌으면 취소 (놓을 때 복제·부활 방지). 뷰는 SyncDrag에서 정리한다.
            Drag?.CancelIfGone();
            if (Screens.Current == ScreenKind.Inventory)
                InventoryDirty?.Invoke();
        }

        private void OnLootSessionChanged(LootSessionChanged evt)
        {
            if (evt.Open)
            {
                if (Screens.OpenInventory())
                    return;   // 화면이 바뀌면서 다시 그린다
                if (Screens.Current != ScreenKind.Inventory)
                {
                    // 사망·서비스 없음으로 화면을 못 열면 보이지 않는 검색이 진행되지 않게 닫는다.
                    if (services.TryGet(out IInventoryService inventory))
                        inventory.CloseLoot();
                    return;
                }
            }
            if (Screens.Current == ScreenKind.Inventory)
                InventoryDirty?.Invoke();
        }

        private void OnBodyStatusChanged(PlayerBodyStatusChanged _)
        {
            if (Screens.Current == ScreenKind.BodyStatus)
                BodyStatusDirty?.Invoke();
        }

        private void OnMagazineCheckStarted(MagazineCheckStarted _) => Toast.OnStarted();

        private void OnMagazineCheckCompleted(MagazineCheckCompleted evt) => Toast.OnCompleted(evt.Level);

        private void OnInteractionPromptChanged(InteractionPromptChanged evt) => Prompt.Apply(evt, InteractKeyLabel);

        private void OnPlayerDied(PlayerDied _)
        {
            Drag?.Cancel();
            Screens.IsDead = true;   // 열린 화면을 닫는다 → ScreenStateChanged(None)
            Prompt.Clear();
            Toast.Clear();
        }
    }
}
