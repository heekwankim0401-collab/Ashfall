using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // 게임 UI 진입점 (설계 §5.4). UIDocument(Art/UI/GameUI.uxml) 하나에 HUD·인벤토리·부위 상태 뷰를 붙인다.
    // 상태 로직은 GameUIPresenter(순수 C#). 여기서는 입력 읽기, 커서, 그리기 호출만 한다.
    // 커서는 UI만 바꾼다: 화면이 모두 닫히면 잠금·숨김, 열리면 해제·표시 (설계 D9).
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameUIRoot : MonoBehaviour
    {
        [SerializeField] private HudConfigSO config;

        private UIDocument document;
        private GameUIPresenter presenter;

        private HudView hud;
        private InventoryScreenView inventoryView;
        private BodyStatusScreenView bodyView;
        private bool viewsBuilt;
        private bool inventoryDirty;
        private bool bodyDirty;

        private InputAction inventoryAction, bodyStatusAction, interactAction, cancelAction, rotateAction;
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

            presenter = new GameUIPresenter(gm.Events, gm.Services, config)
            {
                InteractKeyLabel = KeyLabel(interactAction),
            };
            presenter.ScreenChanged += OnScreenChanged;
            presenter.InventoryDirty += OnInventoryDirty;
            presenter.BodyStatusDirty += OnBodyStatusDirty;
            presenter.Enable();

            viewsBuilt = false;
            TryBuildViews();
            ApplyCursor(ScreenKind.None);
        }

        private void OnDisable()
        {
            if (presenter == null)
                return;
            // 커서는 건드리지 않고(씬 언로드 중일 수 있음) 화면만 닫아 조작 차단을 푼다.
            presenter.ScreenChanged -= OnScreenChanged;
            presenter.InventoryDirty -= OnInventoryDirty;
            presenter.BodyStatusDirty -= OnBodyStatusDirty;
            if (GameManager.Instance != null)
                presenter.Disable();
            presenter = null;
            if (viewsBuilt)
            {
                hud.Set(false, null, null);
                inventoryView.SetVisible(false);
                bodyView.SetVisible(false);
                inventoryView.Dispose();
            }
            hud = null;
            inventoryView = null;
            bodyView = null;
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
            hud.Set(presenter.Screens.HudVisible, presenter.Toast.Text, presenter.Prompt.Text);
        }

        private void ReadInput()
        {
            if (inventoryAction.WasPressedThisFrame())
                presenter.ToggleInventory();
            if (bodyStatusAction.WasPressedThisFrame())
                presenter.ToggleBodyStatus();
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
            if (screen == ScreenKind.Inventory)
                RenderInventory();
            else if (screen == ScreenKind.BodyStatus)
                RenderBodyStatus();
        }

        private void OnInventoryDirty() => inventoryDirty = true;

        private void OnBodyStatusDirty() => bodyDirty = true;

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
            bodyView.Render(BodyStatusPresenter.Build(presenter.BodyView));
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
