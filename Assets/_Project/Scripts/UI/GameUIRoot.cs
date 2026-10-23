using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // 게임 UI 진입점 (설계 §5.4, M4 설계 §4.5). UIDocument(Art/UI/GameUI.uxml) 하나에
    // HUD·인벤토리·부위 상태·지도·일시정지·레이드 종료 뷰를 붙인다.
    // 상태 로직은 GameUIPresenter(순수 C#). 여기서는 입력 읽기, 커서, 그리기 호출만 한다.
    // 커서는 UI만 바꾼다: 화면이 모두 닫히면 잠금·숨김, 열리면(지도·일시정지·종료 오버레이 포함) 해제·표시 (설계 D9).
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameUIRoot : MonoBehaviour
    {
        [SerializeField] private HudConfigSO config;

        private UIDocument document;
        private GameUIPresenter presenter;

        private HudView hud;
        private InventoryScreenView inventoryView;
        private BodyStatusScreenView bodyView;
        private MapScreenView mapView;
        private PauseScreenView pauseView;
        private RaidEndOverlayView raidEndView;
        private bool viewsBuilt;
        private bool inventoryDirty;
        private bool bodyDirty;
        private bool pauseDirty;
        private bool mapTimeDirty;

        private InputAction inventoryAction, bodyStatusAction, mapAction, interactAction, cancelAction, rotateAction;
        private readonly List<InputAction> quickSlotActions = new List<InputAction>();
        private readonly List<string> quickSlotKeys = new List<string>();

        // PlayMode 테스트·디버그용
        public GameUIPresenter Presenter => presenter;

        private void Awake()
        {
            document = GetComponent<UIDocument>();

            var player = InputSystem.actions.FindActionMap("Player", throwIfNotFound: true);
            inventoryAction = player.FindAction("Inventory", true);
            bodyStatusAction = player.FindAction("BodyStatus", true);
            interactAction = player.FindAction("Interact", true);
            // M4 RULE-06 지도 (M). 액션이 없으면 지도 키만 동작하지 않는다.
            mapAction = player.FindAction("Map");
            if (mapAction == null)
                Debug.LogWarning("[GameUI] 입력 액션 Player/Map이 없다. 지도 화면을 열 수 없다.", this);
            for (int i = 1; ; i++)
            {
                InputAction quick = player.FindAction("QuickSlot" + i);
                if (quick == null)
                    break;
                quickSlotActions.Add(quick);
                quickSlotKeys.Add(KeyLabel(quick));
            }

            var ui = InputSystem.actions.FindActionMap("UI", throwIfNotFound: true);
            cancelAction = ui.FindAction("Cancel", true);
            rotateAction = ui.FindAction("RotateItem", true);

            // Player 맵은 PlayerMotor가 켠다. 플레이어 없는 씬에서도 화면 키가 동작하도록 쓰는 액션만 켠다.
            inventoryAction.Enable();
            bodyStatusAction.Enable();
            mapAction?.Enable();
            foreach (InputAction quick in quickSlotActions)
                quick.Enable();
            ui.Enable();
        }

        private void OnEnable()
        {
            if (config == null)
            {
                Debug.LogError("[GameUI] HudConfigSO가 연결되지 않았다.", this);
                enabled = false;
                return;
            }
            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                Debug.LogError("[GameUI] GameManager가 없다.", this);
                enabled = false;
                return;
            }

            // gm.Flow: 일시정지 화면이 세계를 멈추는 데 쓴다 (timeScale 주체는 GameFlow 하나)
            presenter = new GameUIPresenter(gm.Events, gm.Services, config, gm.Flow)
            {
                InteractKeyLabel = KeyLabel(interactAction),
            };
            presenter.ScreenChanged += OnScreenChanged;
            presenter.InventoryDirty += OnInventoryDirty;
            presenter.BodyStatusDirty += OnBodyStatusDirty;
            presenter.PauseDirty += OnPauseDirty;
            presenter.MapTimeDirty += OnMapTimeDirty;
            presenter.Enable();

            viewsBuilt = false;
            TryBuildViews();
            ApplyCursor(ScreenKind.None);
        }

        private void OnDisable()
        {
            if (presenter == null)
                return;
            // 커서는 건드리지 않고(씬 언로드 중일 수 있음) 화면만 닫아 조작 차단·일시정지를 푼다.
            presenter.ScreenChanged -= OnScreenChanged;
            presenter.InventoryDirty -= OnInventoryDirty;
            presenter.BodyStatusDirty -= OnBodyStatusDirty;
            presenter.PauseDirty -= OnPauseDirty;
            presenter.MapTimeDirty -= OnMapTimeDirty;
            if (GameManager.Instance != null)
                presenter.Disable();
            presenter = null;
            if (viewsBuilt)
            {
                hud.Set(false, null, null, null);
                inventoryView.SetVisible(false);
                bodyView.SetVisible(false);
                mapView.SetVisible(false);
                pauseView.SetVisible(false);
                raidEndView.SetVisible(false);
                inventoryView.Dispose();
                pauseView.Dispose();
            }
            hud = null;
            inventoryView = null;
            bodyView = null;
            mapView = null;
            pauseView = null;
            raidEndView = null;
            viewsBuilt = false;
        }

        private void Update()
        {
            if (presenter == null)
                return;
            if (!viewsBuilt)
                TryBuildViews();

            ReadInput();
            presenter.Tick(Time.deltaTime);

            if (!viewsBuilt)
                return;

            inventoryView.SyncDrag();
            if (inventoryDirty)
            {
                inventoryDirty = false;
                RenderInventory();
            }
            if (bodyDirty)
            {
                bodyDirty = false;
                RenderBodyStatus();
            }
            if (pauseDirty)
            {
                pauseDirty = false;
                RenderPause();
            }
            if (mapTimeDirty)
            {
                mapTimeDirty = false;
                if (presenter.Screens.Current == ScreenKind.Map)
                    mapView.SetTime(presenter.Map.RemainingText);
            }
            hud.Set(presenter.Screens.HudVisible, presenter.Toast.Text, presenter.Prompt.Text, presenter.ExtractionHud.Text);
        }

        private void ReadInput()
        {
            if (inventoryAction.WasPressedThisFrame())
                presenter.ToggleInventory();
            if (bodyStatusAction.WasPressedThisFrame())
                presenter.ToggleBodyStatus();
            if (mapAction != null && mapAction.WasPressedThisFrame())
                presenter.ToggleMap();
            if (cancelAction.WasPressedThisFrame())
                presenter.Cancel();
            if (rotateAction.WasPressedThisFrame() && presenter.RotateDragged())
                inventoryView?.RefreshDrag();

            if (presenter.Screens.Current != ScreenKind.Inventory || inventoryView == null)
                return;
            for (int i = 0; i < quickSlotActions.Count; i++)
            {
                if (quickSlotActions[i].WasPressedThisFrame())
                    presenter.AssignQuickSlot(i, inventoryView.ItemUnderPointer());
            }
        }

        // UIDocument가 아직 트리를 만들지 않았으면 다음 프레임에 다시 시도한다.
        private void TryBuildViews()
        {
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null || root.Q<VisualElement>(HudView.RootName) == null)
                return;

            hud = new HudView(root);
            inventoryView = new InventoryScreenView(root, config.dragThresholdPx, quickSlotKeys, IsQuickMoveModifierHeld);
            bodyView = new BodyStatusScreenView(root);
            mapView = new MapScreenView(root);
            pauseView = new PauseScreenView(root, OnResumeClicked, OnAbandonClicked, OnConfirmAbandonClicked, OnCancelAbandonClicked);
            raidEndView = new RaidEndOverlayView(root);
            viewsBuilt = true;
            OnScreenChanged(presenter.Screens.Current);
        }

        private void OnScreenChanged(ScreenKind screen)
        {
            ApplyCursor(screen);
            if (!viewsBuilt)
                return;

            inventoryView.SetVisible(screen == ScreenKind.Inventory);
            bodyView.SetVisible(screen == ScreenKind.BodyStatus);
            mapView.SetVisible(screen == ScreenKind.Map);
            pauseView.SetVisible(screen == ScreenKind.Pause);
            raidEndView.SetVisible(screen == ScreenKind.RaidEnd);
            switch (screen)
            {
                case ScreenKind.Inventory:
                    RenderInventory();
                    break;
                case ScreenKind.BodyStatus:
                    RenderBodyStatus();
                    break;
                case ScreenKind.Map:
                    mapView.Render(presenter.Map.Markers, presenter.Map.RemainingText);
                    break;
                case ScreenKind.Pause:
                    RenderPause();
                    break;
                case ScreenKind.RaidEnd:
                    raidEndView.Render(presenter.RaidEndText);
                    break;
            }
        }

        private void OnInventoryDirty() => inventoryDirty = true;

        private void OnBodyStatusDirty() => bodyDirty = true;

        private void OnPauseDirty() => pauseDirty = true;

        private void OnMapTimeDirty() => mapTimeDirty = true;

        // 일시정지 버튼 (UI Toolkit clicked). 세계가 멈춰 있어도(timeScale 0) UI 이벤트는 처리된다.
        private void OnResumeClicked() => presenter?.Resume();

        private void OnAbandonClicked() => presenter?.RequestAbandon();

        private void OnConfirmAbandonClicked() => presenter?.ConfirmAbandon();

        private void OnCancelAbandonClicked() => presenter?.CancelAbandon();

        private void RenderInventory()
        {
            if (!viewsBuilt || presenter.Screens.Current != ScreenKind.Inventory)
                return;
            inventoryView.Bind(presenter.InventoryService, presenter.Drag);
            inventoryView.Render();
        }

        private void RenderBodyStatus()
        {
            if (!viewsBuilt || presenter.Screens.Current != ScreenKind.BodyStatus || presenter.BodyView == null)
                return;
            bodyView.Render(BodyStatusPresenter.Build(presenter.BodyView), BodyStatusPresenter.BuildSurvival(presenter.BodyView));
        }

        private void RenderPause()
        {
            if (!viewsBuilt || presenter.Screens.Current != ScreenKind.Pause)
                return;
            pauseView.Render(presenter.IsConfirmingAbandon, presenter.CanAbandon);
        }

        private static void ApplyCursor(ScreenKind screen)
        {
            bool open = screen != ScreenKind.None;
            UnityEngine.Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            UnityEngine.Cursor.visible = open;
        }

        // Ctrl+클릭 빠른 이동. 포인터 이벤트의 ctrlKey가 런타임에서 비어 있을 때를 대비해 장치 상태도 본다.
        private static bool IsQuickMoveModifierHeld()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.ctrlKey.isPressed;
        }

        private static string KeyLabel(InputAction action)
        {
            string label = action.GetBindingDisplayString();
            return string.IsNullOrEmpty(label) ? action.name : label;
        }
    }
}
