using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // Hideout 씬 UI 진입점 (M5 설계 §3.4, §4.5). UIDocument(Art/UI/HideoutUI.uxml + GamePanelSettings) 하나.
    // 상태 로직은 HideoutPresenter(순수 C#). 여기서는 서비스 획득, 뷰 연결, 입력 읽기, 커서, 그리기 호출만 한다.
    // - IHideoutService(Hideout의 HideoutController)·IInventoryService(Inventory의 HideoutInventoryBehaviour)는
    //   레지스트리에서 지연 획득한다(Awake 순서에 기대지 않는다). 매 프레임 같은 인스턴스인지 확인해 바뀌면 다시 묶는다.
    // - [장비] 탭 = 기존 InventoryScreenView(드래그·R 회전·Ctrl+클릭), 보관함은 열린 루트 자리(ActiveLoot, Q28·Q29).
    // - [제작] 탭 = CraftingView (M7). 결과 문구는 허브 메시지 줄. 보관함이 바뀌면 HideoutChanged로 다시 그린다.
    //   떠나는 중(IsLeaving)에는 드래그 모델을 떼어 장비·보관함을 더 바꾸지 못하게 한다(반입 Capture 뒤 변경 방지).
    // - 커서: 항상 보이고 잠금 해제. EventSystem 없음(M2 방식, Input System UI 맵).
    // - 퀵슬롯 배정(Q19): [장비] 탭에서 마우스를 아이템에 올리고 Player/QuickSlot1~4. 그 액션의 복제본만 이 컴포넌트가 켜고 끈다(Player 맵은 건드리지 않는다).
    [RequireComponent(typeof(UIDocument))]
    public sealed class HideoutRoot : MonoBehaviour
    {
        // TODO(GDD-?): HudConfigSO가 연결되지 않았을 때의 드래그 임계(px). HudConfig 기본값과 같다.
        public const float DefaultDragThresholdPx = 4f;
        // 키트 남은 시간 문구 갱신 주기(실제 초). 문구가 분 단위라 1초면 충분하다 (M5 설계 §4.5).
        public const float KitRefreshIntervalSec = 1f;

        [Tooltip("드래그 임계(px)를 레이드 인벤토리와 맞춘다. 비어 있으면 기본값")]
        [SerializeField] private HudConfigSO hudConfig;

        private UIDocument document;
        private GameManager gm;

        private IHideoutService hideout;
        private IInventoryService inventory;
        private HideoutPresenter presenter;
        private InventoryDragModel drag;

        private HideoutHubView hubView;
        private HideoutNoticeView noticeView;
        private RaidResultView resultView;
        private TraderView traderView;
        private CraftingView craftingView;
        private InventoryScreenView inventoryView;
        private bool viewsBuilt;

        private bool refreshNeeded;
        private bool viewDirty;
        private bool inventoryDirty;
        private float kitTimer;

        private InputAction cancelAction, rotateAction;
        private readonly List<InputAction> quickSlotActions = new List<InputAction>();
        private readonly List<string> quickSlotKeys = new List<string>();

        // PlayMode 테스트·디버그용
        public HideoutPresenter Presenter => presenter;
        public bool ViewsBuilt => viewsBuilt;

        private void Awake()
        {
            document = GetComponent<UIDocument>();

            InputActionAsset actions = InputSystem.actions;
            InputActionMap ui = actions != null ? actions.FindActionMap("UI") : null;
            if (ui == null)
            {
                Debug.LogError("[Hideout] 입력 액션 맵 UI가 없다.", this);
            }
            else
            {
                cancelAction = ui.FindAction("Cancel");
                rotateAction = ui.FindAction("RotateItem");
                ui.Enable();
            }

            // 퀵슬롯 액션은 복제해서 쓴다 — 켜고 끄는 것이 프로젝트 Player 맵(레이드 씬이 켬)에 닿지 않게 (M5 리뷰 11).
            InputActionMap player = actions != null ? actions.FindActionMap("Player") : null;
            if (player != null)
            {
                for (int i = 1; ; i++)
                {
                    InputAction quick = player.FindAction("QuickSlot" + i);
                    if (quick == null)
                        break;
                    quickSlotActions.Add(quick.Clone());
                    quickSlotKeys.Add(KeyLabel(quick));
                }
            }
        }

        private void OnDestroy()
        {
            foreach (InputAction quick in quickSlotActions)
                quick.Dispose();
            quickSlotActions.Clear();
        }

        private void OnEnable()
        {
            foreach (InputAction quick in quickSlotActions)
                quick.Enable();

            gm = GameManager.Instance;
            if (gm == null || gm.Flow == null)
            {
                Debug.LogError("[Hideout] GameManager(GameFlow)가 없다.", this);
                enabled = false;
                return;
            }

            InputSystem.actions?.FindActionMap("UI")?.Enable();
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            gm.Events.Subscribe<HideoutChanged>(OnHideoutChanged);
            gm.Events.Subscribe<InventoryChanged>(OnInventoryChanged);
            viewsBuilt = false;
            TryBuildViews();
            AcquireServices();
        }

        private void OnDisable()
        {
            foreach (InputAction quick in quickSlotActions)
                quick.Disable();
            if (gm == null)
                return;
            gm.Events.Unsubscribe<HideoutChanged>(OnHideoutChanged);
            gm.Events.Unsubscribe<InventoryChanged>(OnInventoryChanged);
            gm = null;

            if (presenter != null)
                presenter.Changed -= OnPresenterChanged;
            presenter = null;
            hideout = null;
            inventory = null;
            drag = null;

            if (viewsBuilt)
            {
                inventoryView.SetVisible(false);
                inventoryView.Dispose();
                hubView.Dispose();
                noticeView.Dispose();
                resultView.Dispose();
            }
            hubView = null;
            noticeView = null;
            resultView = null;
            traderView = null;
            craftingView = null;
            inventoryView = null;
            viewsBuilt = false;
        }

        private void Update()
        {
            if (gm == null)
                return;
            if (!viewsBuilt)
                TryBuildViews();
            AcquireServices();
            if (presenter == null)
                return;

            ReadInput();

            if (refreshNeeded)
            {
                refreshNeeded = false;
                presenter.Refresh();   // → Changed → viewDirty
            }

            if (!viewsBuilt)
                return;

            inventoryView.SyncDrag();
            if (viewDirty)
            {
                viewDirty = false;
                inventoryDirty = false;
                RenderAll();
            }
            else if (inventoryDirty)
            {
                inventoryDirty = false;
                RenderInventory();
            }

            kitTimer += Time.unscaledDeltaTime;
            if (kitTimer >= KitRefreshIntervalSec)
            {
                kitTimer = 0f;
                if (presenter.UpdateKitStatus())
                    hubView.RenderKit(presenter);
            }
        }

        // 서비스가 생기거나 바뀌면 다시 묶는다 (2단계: HideoutController·HideoutInventoryBehaviour가 OnEnable에서 등록).
        private void AcquireServices()
        {
            gm.Services.TryGet(out IHideoutService foundHideout);
            if (!ReferenceEquals(foundHideout, hideout))
            {
                if (presenter != null)
                    presenter.Changed -= OnPresenterChanged;
                hideout = foundHideout;
                presenter = hideout != null ? new HideoutPresenter(hideout, gm.Flow) : null;
                if (presenter != null)
                    presenter.Changed += OnPresenterChanged;
                viewDirty = true;
                if (presenter == null && viewsBuilt)
                    RenderAll();
            }

            gm.Services.TryGet(out IInventoryService foundInventory);
            if (!ReferenceEquals(foundInventory, inventory))
            {
                inventory = foundInventory;
                drag = inventory != null ? new InventoryDragModel(inventory) : null;
                viewDirty = true;
            }
        }

        private void ReadInput()
        {
            bool loadoutOpen = presenter.Panel == HideoutPanel.Hub && presenter.Tab == HideoutTab.Loadout;
            if (cancelAction != null && cancelAction.WasPressedThisFrame() && drag != null && drag.IsDragging)
                drag.Cancel();   // 뷰는 SyncDrag에서 정리
            if (rotateAction != null && rotateAction.WasPressedThisFrame() && loadoutOpen && drag != null && drag.IsDragging)
            {
                drag.ToggleRotate();
                inventoryView?.RefreshDrag();
            }

            if (!loadoutOpen || presenter.IsLeaving || inventory == null || inventoryView == null)
                return;
            for (int i = 0; i < quickSlotActions.Count; i++)
            {
                if (quickSlotActions[i].WasPressedThisFrame())
                {
                    ItemInstance item = inventoryView.ItemUnderPointer();
                    if (item != null)
                        inventory.TryAssignQuickSlot(i, item);   // 결과는 InventoryChanged로 다시 그린다
                }
            }
        }

        // UIDocument가 아직 트리를 만들지 않았으면 다음 프레임에 다시 시도한다.
        private void TryBuildViews()
        {
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null || root.Q<VisualElement>(HideoutHubView.RootName) == null)
                return;

            float threshold = hudConfig != null ? hudConfig.dragThresholdPx : DefaultDragThresholdPx;
            hubView = new HideoutHubView(root, OnTabClicked, OnKitClicked, OnStartRaidClicked, OnMainMenuClicked);
            noticeView = new HideoutNoticeView(root, OnNoticeConfirmClicked);
            resultView = new RaidResultView(root, OnResultConfirmClicked);
            traderView = new TraderView(root, OnBuyClicked, OnSellClicked);
            craftingView = new CraftingView(root, OnCraftClicked);
            inventoryView = new InventoryScreenView(root, threshold, quickSlotKeys, IsQuickMoveModifierHeld);
            viewsBuilt = true;
            viewDirty = true;
        }

        private void RenderAll()
        {
            if (!viewsBuilt)
                return;
            if (presenter == null)
            {
                hubView.SetVisible(false);
                noticeView.SetVisible(false);
                resultView.SetVisible(false);
                traderView.SetVisible(false);
                craftingView.SetVisible(false);
                inventoryView.SetVisible(false);
                return;
            }

            HideoutPanel panel = presenter.Panel;
            noticeView.SetVisible(panel == HideoutPanel.Notice);
            if (panel == HideoutPanel.Notice)
                noticeView.Render(presenter.Notice);
            resultView.SetVisible(panel == HideoutPanel.Result);
            if (panel == HideoutPanel.Result)
                resultView.Render(presenter.Result);

            bool hub = panel == HideoutPanel.Hub;
            hubView.SetVisible(hub);
            hubView.Render(presenter);

            bool trader = hub && presenter.Tab == HideoutTab.Trader;
            traderView.SetVisible(trader);
            if (trader)
                traderView.Render(presenter.Trader, presenter.HubEnabled);

            bool craftingTab = hub && presenter.Tab == HideoutTab.Crafting;
            craftingView.SetVisible(craftingTab);
            if (craftingTab)
                craftingView.Render(presenter.Crafting, presenter.HubEnabled);

            RenderInventory();
        }

        private void RenderInventory()
        {
            if (!viewsBuilt || presenter == null)
                return;
            bool visible = presenter.Panel == HideoutPanel.Hub && presenter.Tab == HideoutTab.Loadout && inventory != null;
            inventoryView.SetVisible(visible);
            if (!visible)
                return;
            // 떠나는 중이면 드래그 모델을 떼어 집기·빠른 이동·사용을 막는다 (InventoryScreenView는 drag == null이면 누름을 무시)
            inventoryView.Bind(inventory, presenter.IsLeaving ? null : drag);
            inventoryView.Render();
        }

        private void OnPresenterChanged() => viewDirty = true;

        // 탄피·보관함·키트 (Hideout) — 은신처 인벤토리 브리지가 InventoryChanged로도 다시 알린다
        private void OnHideoutChanged(HideoutChanged _)
        {
            refreshNeeded = true;
            inventoryDirty = true;
        }

        // 장비 ↔ 보관함 이동 → 인벤토리와 상인 매도 목록(보관함)을 다시 그린다
        private void OnInventoryChanged(InventoryChanged _)
        {
            // 드래그 중 아이템이 다른 경로로 빠졌으면 취소 (판매 등). 뷰는 SyncDrag에서 정리한다.
            drag?.CancelIfGone();
            refreshNeeded = true;
            inventoryDirty = true;
        }

        private void OnTabClicked(HideoutTab tab) => presenter?.SelectTab(tab);

        private void OnKitClicked() => presenter?.ClaimKit();

        private void OnStartRaidClicked()
        {
            inventoryView?.CancelPointer();
            presenter?.StartRaid();
        }

        private void OnMainMenuClicked()
        {
            inventoryView?.CancelPointer();
            presenter?.ReturnToMenu();
        }

        private void OnNoticeConfirmClicked() => presenter?.ConfirmNotice();

        private void OnResultConfirmClicked() => presenter?.ConfirmResult();

        private void OnBuyClicked(ItemDefinitionSO item) => presenter?.Buy(item);

        private void OnCraftClicked(string recipeId) => presenter?.Craft(recipeId);

        private void OnSellClicked(ItemInstance item)
        {
            if (drag != null && ReferenceEquals(drag.Dragging, item))
                drag.Cancel();
            presenter?.Sell(item);
        }

        // Ctrl+클릭 빠른 이동 (GameUIRoot와 같음)
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
