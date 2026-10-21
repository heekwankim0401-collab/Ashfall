using Ashfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // MainMenu 씬 UI 진입점 (M4 설계 §1.3, §4.5). UIDocument(Art/UI/MainMenu.uxml + GamePanelSettings) 하나.
    // 상태 로직은 MainMenuPresenter(순수 C#). 여기서는 버튼 연결·패널 표시·커서만 한다.
    // 메뉴: [레이드 시작] / [종료] (TODO(GDD-?) Q22). 결과 패널: GameFlow.PendingResult가 있으면 먼저 (Q15·Q18).
    // 입력: EventSystem 없이 UI Toolkit이 Input System UI 맵으로 포인터를 받는다(M2와 같음) → UI 맵을 켠다.
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuRoot : MonoBehaviour
    {
        public const string MenuPanelName = "menu-panel";
        public const string StartName = "menu-start";
        public const string QuitName = "menu-quit";

        private UIDocument document;
        private MainMenuPresenter presenter;
        private RaidResultView resultView;
        private VisualElement menuPanel;
        private Button startButton;
        private Button quitButton;
        private bool viewsBuilt;

        // PlayMode 테스트·디버그용
        public MainMenuPresenter Presenter => presenter;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Flow == null)
            {
                Debug.LogError("[MainMenu] GameManager(GameFlow)가 없다.", this);
                enabled = false;
                return;
            }

            InputSystem.actions?.FindActionMap("UI")?.Enable();
            // 레이드에서 돌아오면 커서가 잠겨 있을 수 있다. 메뉴는 항상 커서를 보인다.
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            presenter = new MainMenuPresenter(gm.Flow, QuitApplication);
            presenter.Changed += Render;
            viewsBuilt = false;
            TryBuildViews();
        }

        private void OnDisable()
        {
            if (presenter == null)
                return;
            presenter.Changed -= Render;
            presenter = null;
            if (viewsBuilt)
            {
                startButton.clicked -= OnStartClicked;
                quitButton.clicked -= OnQuitClicked;
                resultView.Dispose();
            }
            resultView = null;
            menuPanel = null;
            startButton = null;
            quitButton = null;
            viewsBuilt = false;
        }

        private void Update()
        {
            if (presenter != null && !viewsBuilt)
                TryBuildViews();
        }

        // UIDocument가 아직 트리를 만들지 않았으면 다음 프레임에 다시 시도한다.
        private void TryBuildViews()
        {
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null || root.Q<VisualElement>(MenuPanelName) == null)
                return;

            menuPanel = RaidResultView.Find<VisualElement>(root, MenuPanelName);
            startButton = RaidResultView.Find<Button>(root, StartName);
            quitButton = RaidResultView.Find<Button>(root, QuitName);
            startButton.text = UiText.MenuStartRaid;
            quitButton.text = UiText.MenuQuit;
            startButton.clicked += OnStartClicked;
            quitButton.clicked += OnQuitClicked;
            resultView = new RaidResultView(root, OnConfirmClicked);
            viewsBuilt = true;
            Render();
        }

        private void Render()
        {
            if (!viewsBuilt || presenter == null)
                return;
            bool showResult = presenter.Panel == MainMenuPanel.Result;
            menuPanel.style.display = showResult ? DisplayStyle.None : DisplayStyle.Flex;
            resultView.SetVisible(showResult);
            if (showResult)
                resultView.Render(presenter.Result);
            startButton.SetEnabled(!presenter.IsStarting);
            quitButton.SetEnabled(!presenter.IsStarting);
        }

        private void OnStartClicked() => presenter?.StartRaid();

        private void OnQuitClicked() => presenter?.Quit();

        private void OnConfirmClicked() => presenter?.ConfirmResult();

        private static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }
    }
}
