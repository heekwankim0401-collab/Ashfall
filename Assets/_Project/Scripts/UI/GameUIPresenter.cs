using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.UI
{
    // 게임 UI의 상태 로직 (설계 §5.4, M4 설계 §4.5). 버스·레지스트리를 생성자로 받아 EditMode에서 그대로 테스트한다.
    // - 기능 데이터는 Core 인터페이스(IInventoryService, IBodyStatusView, IRaidInfo)로 읽고, 변경은 이벤트로 받는다.
    // - 화면이 바뀌면 ScreenStateChanged를 발행한다(게임플레이 입력 차단은 각 기능이 한다).
    // - 일시정지 화면이 열리고 닫힐 때 GameFlow.SetWorldPaused로 세계를 멈춘다(timeScale 주체는 GameFlow 하나).
    // - 그리기·입력·커서는 GameUIRoot(MonoBehaviour)와 뷰가 한다.
    public sealed class GameUIPresenter
    {
        private readonly EventBus bus;
        private readonly ServiceRegistry services;
        private readonly HudConfigSO config;
        private readonly GameFlow flow;
        private readonly HashSet<ScreenKind> warnedMissing = new HashSet<ScreenKind>();
        private bool subscribed;
        private ScreenKind lastScreen = ScreenKind.None;
        private bool abandonRequested;

        // flow: 세계 정지 대상. null이면(테스트·흐름 없는 씬) 일시정지 화면만 열고 시간은 멈추지 않는다.
        public GameUIPresenter(EventBus bus, ServiceRegistry services, HudConfigSO config, GameFlow flow = null)
        {
            this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
            this.services = services ?? throw new ArgumentNullException(nameof(services));
            Toast = new MagazineCheckToastModel(config);
            this.config = config;
            this.flow = flow;
            Screens.CanOpen = ResolveServiceFor;
            Screens.Changed += OnScreenChanged;
        }

        public ScreenStateModel Screens { get; } = new ScreenStateModel();
        public MagazineCheckToastModel Toast { get; }
        public InteractionPromptModel Prompt { get; } = new InteractionPromptModel();
        public ExtractionHudModel ExtractionHud { get; } = new ExtractionHudModel();
        public MapScreenModel Map { get; } = new MapScreenModel();

        // 인벤토리 화면을 열 때 레지스트리에서 얻는다. 열기 전에는 null.
        public IInventoryService InventoryService { get; private set; }
        public InventoryDragModel Drag { get; private set; }
        public IBodyStatusView BodyView { get; private set; }
        // 지도 화면을 열 때 얻는다.
        public IRaidInfo RaidInfo { get; private set; }

        // 레이드 종료 오버레이 한 줄 (RaidEnded 전에는 null)
        public string RaidEndText { get; private set; }

        // 일시정지 화면의 [레이드 포기] 확인 단계 (TODO(GDD-?) Q16: 확인 한 번 더)
        public bool IsConfirmingAbandon { get; private set; }

        // [레이드 포기]를 보일지: 레이드 중(IRaidInfo 등록)이고 아직 요청하지 않았을 때. 확인 씬에서는 [계속]만.
        public bool CanAbandon => !abandonRequested && services.TryGet(out IRaidInfo _);

        // F 프롬프트 앞에 붙는 키 이름 (입력 바인딩 표시 문자열). 루트가 채운다.
        public string InteractKeyLabel { get; set; } = string.Empty;

        public event Action<ScreenKind> ScreenChanged;
        public event Action InventoryDirty;     // 인벤토리 화면을 다시 그려야 함
        public event Action BodyStatusDirty;    // 부위 상태 화면을 다시 그려야 함
        public event Action PauseDirty;         // 일시정지 화면(확인 단계)을 다시 그려야 함
        public event Action MapTimeDirty;       // 지도 화면의 남은 시간 문구가 바뀜

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
            bus.Subscribe<RaidStarted>(OnRaidStarted);
            bus.Subscribe<ExtractionStatusChanged>(OnExtractionStatusChanged);
            bus.Subscribe<RaidEnded>(OnRaidEnded);
        }

        // 화면이 열려 있으면(종료 오버레이 포함) 닫고(조작 차단·일시정지 해제) 구독을 끊는다.
        public void Disable()
        {
            if (!subscribed)
                return;
            Screens.Reset();
            subscribed = false;
            bus.Unsubscribe<InventoryChanged>(OnInventoryChanged);
            bus.Unsubscribe<LootSessionChanged>(OnLootSessionChanged);
            bus.Unsubscribe<PlayerBodyStatusChanged>(OnBodyStatusChanged);
            bus.Unsubscribe<MagazineCheckStarted>(OnMagazineCheckStarted);
            bus.Unsubscribe<MagazineCheckCompleted>(OnMagazineCheckCompleted);
            bus.Unsubscribe<InteractionPromptChanged>(OnInteractionPromptChanged);
            bus.Unsubscribe<PlayerDied>(OnPlayerDied);
            bus.Unsubscribe<RaidStarted>(OnRaidStarted);
            bus.Unsubscribe<ExtractionStatusChanged>(OnExtractionStatusChanged);
            bus.Unsubscribe<RaidEnded>(OnRaidEnded);
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

        // M (RULE-06). 지도 아이템을 소지하고 레이드 정보가 있을 때만 열린다.
        public bool ToggleMap()
        {
            Drag?.Cancel();
            return Screens.ToggleMap();
        }

        // Esc: 드래그 중이면 드래그만 취소. 포기 확인 중이면 확인만 취소. 화면이 열려 있으면 닫는다.
        // 화면이 모두 닫혀 있으면 일시정지를 연다 (TODO(GDD-?) Q16). 종료 오버레이는 닫히지 않는다.
        public bool Cancel()
        {
            if (Drag != null && Drag.IsDragging)
            {
                Drag.Cancel();
                return true;
            }
            switch (Screens.Current)
            {
                case ScreenKind.None:
                    return Screens.OpenPause();
                case ScreenKind.Pause:
                    if (IsConfirmingAbandon)
                        return CancelAbandon();
                    return Screens.CloseAll();
                default:
                    return Screens.CloseAll();
            }
        }

        // 일시정지 [계속]
        public bool Resume() => Screens.Current == ScreenKind.Pause && Screens.CloseAll();

        // 일시정지 [레이드 포기] → 확인 단계
        public bool RequestAbandon()
        {
            if (Screens.Current != ScreenKind.Pause || IsConfirmingAbandon || !CanAbandon)
                return false;
            SetConfirming(true);
            return true;
        }

        // 확인 단계 [포기] → RaidAbandonRequested. Raid가 Abandoned로 끝내면 RaidEnded로 종료 오버레이가 뜬다.
        public bool ConfirmAbandon()
        {
            if (Screens.Current != ScreenKind.Pause || !IsConfirmingAbandon || !CanAbandon)
                return false;
            abandonRequested = true;
            SetConfirming(false);
            bus.Publish(new RaidAbandonRequested());
            return true;
        }

        // 확인 단계 [취소]
        public bool CancelAbandon()
        {
            if (!IsConfirmingAbandon)
                return false;
            SetConfirming(false);
            return true;
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

        public void Tick(float dt)
        {
            Toast.Tick(dt);
            // 지도가 열려 있는 동안 남은 시간 대략치(분)가 바뀌면 다시 그린다. 일시정지 중엔 지도가 열리지 않는다.
            if (Screens.Current == ScreenKind.Map && RaidInfo != null && Map.RefreshTime(RaidInfo))
                MapTimeDirty?.Invoke();
        }

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
                case ScreenKind.Map:
                    return ResolveMap();
                default:
                    return true;
            }

            WarnOnce(kind, "필요한 서비스가 등록되지 않아 열지 않는다.");
            return false;
        }

        // RULE-06: 종이 지도 아이템이 있어야 연다 (TODO(GDD-?) Q11). 레이드 정보가 없으면(확인 씬) 열지 않는다.
        private bool ResolveMap()
        {
            if (!services.TryGet(out IRaidInfo info))
            {
                WarnOnce(ScreenKind.Map, "IRaidInfo가 등록되지 않아 열지 않는다 (레이드 씬이 아님).");
                return false;
            }
            if (config.mapItem == null)
            {
                WarnOnce(ScreenKind.Map, "HudConfigSO.mapItem이 비어 있어 열지 않는다.");
                return false;
            }
            // 지도를 갖고 있지 않으면 조용히 무시 (정상 상황)
            if (!services.TryGet(out IInventoryService inventory) || !inventory.Contains(config.mapItem))
                return false;
            RaidInfo = info;
            Map.Build(info);
            return true;
        }

        private void WarnOnce(ScreenKind kind, string message)
        {
            if (warnedMissing.Add(kind))
                Debug.LogWarning($"[GameUI] {kind} 화면: {message}");
        }

        private void OnScreenChanged(ScreenKind screen)
        {
            ScreenKind previous = lastScreen;
            lastScreen = screen;

            if (screen != ScreenKind.Inventory)
            {
                Drag?.Cancel();
                // 루트 패널은 인벤토리 화면에서만 보인다. 다른 화면으로 바꾸거나 닫으면 루트도 닫는다(검색 정지).
                if (InventoryService != null && InventoryService.ActiveLoot != null)
                    InventoryService.CloseLoot();
            }
            if (screen != ScreenKind.Pause)
                IsConfirmingAbandon = false;

            // 세계 정지: 일시정지에 들어가면 멈추고, 나가면 푼다. 종료 오버레이로 넘어가면 Raid가 계속 멈춰 둔다.
            if (screen == ScreenKind.Pause)
                flow?.SetWorldPaused(true);
            else if (previous == ScreenKind.Pause && screen != ScreenKind.RaidEnd)
                flow?.SetWorldPaused(false);

            bus.Publish(new ScreenStateChanged(screen));
            ScreenChanged?.Invoke(screen);
        }

        private void SetConfirming(bool value)
        {
            if (IsConfirmingAbandon == value)
                return;
            IsConfirmingAbandon = value;
            PauseDirty?.Invoke();
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
                    // 사망·일시정지·서비스 없음으로 화면을 못 열면 보이지 않는 검색이 진행되지 않게 닫는다.
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
            Screens.IsDead = true;   // 열린 화면을 닫는다 → ScreenStateChanged(None). 종료 오버레이는 유지.
            Prompt.Clear();
            Toast.Clear();
            ExtractionHud.Clear();
        }

        private void OnRaidStarted(RaidStarted _)
        {
            ExtractionHud.Clear();
            abandonRequested = false;
        }

        private void OnExtractionStatusChanged(ExtractionStatusChanged evt)
        {
            if (Screens.IsRaidEnded || Screens.IsDead)
                return;
            ExtractionHud.Apply(evt);
        }

        // 종료 오버레이 (M4 설계 §1.3). 세계 정지·씬 전환은 Raid·GameFlow가 한다.
        private void OnRaidEnded(RaidEnded evt)
        {
            Drag?.Cancel();
            RaidEndText = RaidResultViewModel.OverlayText(evt.Outcome);
            Prompt.Clear();
            Toast.Clear();
            ExtractionHud.Clear();
            Screens.ShowRaidEnd();
        }
    }
}
