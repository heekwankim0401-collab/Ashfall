#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.Raid;
using Ashfall.UI;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M4 통합 테스트 공용 (M4 설계 §6 "4. 검증", §7): Raid_CrimsonBasin 로드(스폰 고정 가능) + 주요 참조,
    // 탈출 구역 순간이동(IPlayerPlacement), 적 제거, 머리 피격 사망, 메인 메뉴 UI 참조, UI Toolkit 버튼 클릭.
    // 두 씬 모두 빌드 설정에 있어 이름으로 불러온다 → GameFlow.Phase가 실제 흐름과 같다(종료 시 MainMenu로 실제 전환).
    internal sealed class M4Raid
    {
        public const float LoadTimeoutSec = 60f;
        // 종료 오버레이(endDelaySec 3초, 실시간) + MainMenu 로드 여유
        public const float EndToMenuTimeoutSec = 20f;

        public GameManager Gm;
        public RaidController Controller;
        public PlayerMotor Motor;
        public PlayerHealth Health;
        public PlayerInventoryBehaviour InventoryBehaviour;
        public PlayerInventory Inventory;
        public GameUIRoot Ui;
        public UIDocument Document;
        public AiDirector Director;
        public ProjectileSystem Projectiles;
        public readonly List<EnemyAgent> Enemies = new List<EnemyAgent>();

        public EventBus Events => Gm.Events;
        public GameFlow Flow => Gm.Flow;
        public RaidSession Session => Controller.Session;
        public RaidConfigSO Config => Controller.Config;
        public IPlayerPlacement Placement => Motor;
        public VisualElement UiRoot => Document.rootVisualElement;
        public GameUIPresenter Presenter => Ui.Presenter;

        private static readonly FieldInfo SeedField =
            typeof(RaidController).GetField("seed", BindingFlags.Instance | BindingFlags.NonPublic);

        // 레이드 씬을 이름으로 불러온다. spawnId를 주면 RaidController.seed를 그 스폰이 뽑히는 값으로 바꾼다
        // (sceneLoaded는 Awake·OnEnable 뒤, Start 전에 온다 — Start가 시드로 스폰·활성 탈출구를 고른다).
        public static IEnumerator Load(M4Raid into, string spawnId = null)
        {
            string injectedFor = null;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> inject = (scene, mode) =>
            {
                if (spawnId == null || scene.name != SceneNames.RaidCrimsonBasin)
                    return;
                foreach (RaidController c in Object.FindObjectsByType<RaidController>(FindObjectsSortMode.None))
                {
                    SeedField.SetValue(c, SeedForSpawn(spawnId));
                    injectedFor = spawnId;
                }
            };
            SceneManager.sceneLoaded += inject;
            try
            {
                yield return LoadScene(SceneNames.RaidCrimsonBasin);
            }
            finally
            {
                SceneManager.sceneLoaded -= inject;
            }
            if (spawnId != null)
                Assert.AreEqual(spawnId, injectedFor, "시드 주입 실패 (sceneLoaded가 Start 전에 오지 않았다)");
            yield return Attach(into);
            if (spawnId != null)
                Assert.AreEqual(spawnId, into.Controller.SpawnId, "고정한 스폰이 뽑히지 않았다");
        }

        // 이미 불러온(또는 메뉴에서 시작한) 레이드 씬의 참조를 잡고 세션·UI 준비를 기다린다.
        public static IEnumerator Attach(M4Raid into)
        {
            Assert.AreEqual(SceneNames.RaidCrimsonBasin, SceneManager.GetActiveScene().name, "레이드 씬이 아니다");
            into.Gm = GameManager.Instance;
            Assert.IsNotNull(into.Gm, "GameManager 없음");
            into.Controller = Object.FindFirstObjectByType<RaidController>();
            into.Motor = Object.FindFirstObjectByType<PlayerMotor>();
            into.Health = Object.FindFirstObjectByType<PlayerHealth>();
            into.InventoryBehaviour = Object.FindFirstObjectByType<PlayerInventoryBehaviour>();
            into.Ui = Object.FindFirstObjectByType<GameUIRoot>();
            into.Director = Object.FindFirstObjectByType<AiDirector>();
            into.Projectiles = Object.FindFirstObjectByType<ProjectileSystem>();
            Assert.IsNotNull(into.Controller, "RaidController 없음");
            Assert.IsNotNull(into.Motor, "PlayerMotor 없음");
            Assert.IsNotNull(into.Health, "PlayerHealth 없음");
            Assert.IsNotNull(into.InventoryBehaviour, "PlayerInventoryBehaviour 없음");
            Assert.IsNotNull(into.Ui, "GameUIRoot 없음");
            Assert.IsNotNull(into.Director, "AiDirector 없음");
            Assert.IsNotNull(into.Projectiles, "ProjectileSystem 없음");
            into.Inventory = into.InventoryBehaviour.Inventory;
            into.Document = into.Ui.GetComponent<UIDocument>();
            into.Enemies.Clear();
            into.Enemies.AddRange(Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.InstanceID));

            M4Raid raid = into;
            yield return M3Scene.WaitUntil(() =>
                    raid.Controller.Session != null && raid.Ui.Presenter != null &&
                    raid.UiRoot != null && raid.UiRoot.Q<Button>(PauseScreenView.ResumeName) != null &&
                    raid.UiRoot.Q<Button>(PauseScreenView.ResumeName).text == UiText.PauseResume,
                10f, () => "레이드 세션·게임 UI 준비");
        }

        public static IEnumerator LoadScene(string sceneName)
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            Assert.IsNotNull(op, $"{sceneName} 로드 실패 (빌드 설정 확인)");
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSec;
            while (!op.isDone)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"{sceneName} 로드 시간 초과");
                yield return null;
            }
            for (int i = 0; i < 3; i++)
                yield return null;
            Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name);
        }

        // RaidController와 같은 규칙(스폰 id 서수 정렬, new System.Random(seed) → PickSpawnIndex)으로 spawnId가 뽑히는 시드를 찾는다.
        public static int SeedForSpawn(string spawnId)
        {
            List<string> ids = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Select(s => s.Id).ToList();
            ids.Sort(string.CompareOrdinal);
            int index = ids.IndexOf(spawnId);
            Assert.GreaterOrEqual(index, 0, $"스폰 {spawnId}가 씬에 없다");
            for (int seed = 1; seed < 10000; seed++)
                if (ActiveExtractionPicker.PickSpawnIndex(ids.Count, new System.Random(seed)) == index)
                    return seed;
            Assert.Fail($"스폰 {spawnId}를 뽑는 시드를 찾지 못했다");
            return 0;
        }

        // ── 위치 ──

        public ExtractionZone Zone(string id)
        {
            ExtractionZone zone = Object.FindObjectsByType<ExtractionZone>(FindObjectsSortMode.None).FirstOrDefault(z => z.Id == id);
            Assert.IsNotNull(zone, $"ExtractionZone {id} 없음");
            return zone;
        }

        public ExtractionModel Model(string id)
        {
            ExtractionModel model = Session.Extractions.FirstOrDefault(m => m.Id == id);
            Assert.IsNotNull(model, $"탈출 모델 {id} 없음");
            return model;
        }

        public PlayerSpawnPoint ChosenSpawn =>
            Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None).First(s => s.Id == Controller.SpawnId);

        // 탈출 구역 중심으로 순간이동 (구역은 위치 판정이라 콜라이더 없음)
        public IEnumerator EnterZone(string id)
        {
            Placement.PlaceAt(Zone(id).transform.position + Vector3.up * 0.05f, 0f);
            yield return null;
            yield return null;
        }

        // 스폰 지점으로 돌아간다 (모든 탈출 구역에서 수백 m 밖)
        public IEnumerator LeaveZone()
        {
            PlayerSpawnPoint spawn = ChosenSpawn;
            Placement.PlaceAt(spawn.FeetPosition + Vector3.up * 0.05f, spawn.YawDeg);
            yield return null;
            yield return null;
        }

        // 시선 상하 (도, + = 아래). PlaceAt 직후 pitch 0 기준. PlayerMotor.AddRecoil(위로 차는 양)로 맞춘다.
        public void LookDown(float pitchDeg) => Motor.AddRecoil(-pitchDeg, 0f);

        // ── 적 ──

        // 탈출·UI 흐름 테스트에서 교전 변수를 없앤다 (적은 스폰에서 150m 밖이지만 순찰·청각으로 다가올 수 있다).
        public IEnumerator RemoveEnemies()
        {
            foreach (EnemyAgent e in Enemies)
                if (e != null)
                    Object.Destroy(e.gameObject);
            Enemies.Clear();
            yield return null;
            Assert.AreEqual(0, Director.Agents.Count, "AiDirector 등록 남음");
        }

        // ── 플레이어 ──

        // 머리 높이 지점에 IDamageable 피해 (PlayerHealth의 부위 판정을 그대로 거친다)
        public void ShootHead(float damage = 1000f)
        {
            var cc = Motor.GetComponent<CharacterController>();
            // PlayerHealth: local = (p - center) / 0.8, local.y += height / 2 → 캡슐 꼭대기 바로 아래(높이의 97%)
            float localY = cc.center.y + 0.8f * (cc.height * 0.5f - cc.height * 0.03f);
            Vector3 point = Motor.transform.TransformPoint(new Vector3(0f, localY, 0f));
            ((IDamageable)Health).ApplyDamage(new DamageInfo(damage, 0, point, Motor.transform.forward, 10f));
        }

        public List<ItemInstance> Carried()
        {
            var list = new List<ItemInstance>();
            InventorySnapshot.Collect(Inventory, list);
            return list;
        }

        public int CountCarried(ItemDefinitionSO definition) => Carried().Count(i => i.Definition == definition);

        // 결과 RaidResult + 종료 오버레이 → MainMenu 결과 패널까지 기다린다.
        public static IEnumerator WaitForMainMenu(M4Menu menu, float timeoutSec = EndToMenuTimeoutSec)
        {
            yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu,
                timeoutSec, () => $"MainMenu로 전환 (현재 {SceneManager.GetActiveScene().name})");
            yield return M4Menu.Attach(menu);
        }
    }

    // MainMenu 씬 UI 참조 (M4 설계 §1.3·§3.4)
    internal sealed class M4Menu
    {
        public MainMenuRoot Root;
        public VisualElement Ui;
        public Button Start;
        public Button Quit;
        public Button Confirm;
        public VisualElement MenuPanel;
        public VisualElement ResultPanel;

        public MainMenuPresenter Presenter => Root.Presenter;

        public static IEnumerator Attach(M4Menu into)
        {
            MainMenuRoot root = null;
            yield return M3Scene.WaitUntil(() =>
            {
                root = Object.FindFirstObjectByType<MainMenuRoot>();
                if (root == null || root.Presenter == null)
                    return false;
                VisualElement ui = root.GetComponent<UIDocument>().rootVisualElement;
                Button start = ui?.Q<Button>(MainMenuRoot.StartName);
                return start != null && start.text == UiText.MenuStartRaid;   // 뷰가 만들어지면 UiText로 덮어쓴다
            }, 10f, () => "MainMenuRoot 준비");
            into.Root = root;
            into.Ui = root.GetComponent<UIDocument>().rootVisualElement;
            into.Start = into.Ui.Q<Button>(MainMenuRoot.StartName);
            into.Quit = into.Ui.Q<Button>(MainMenuRoot.QuitName);
            into.Confirm = into.Ui.Q<Button>(RaidResultView.ConfirmName);
            into.MenuPanel = into.Ui.Q<VisualElement>(MainMenuRoot.MenuPanelName);
            into.ResultPanel = into.Ui.Q<VisualElement>(RaidResultView.PanelName);
            yield return null;   // 레이아웃 한 번
        }

        public string Text(string name) => Ui.Q<Label>(name)?.text;

        public List<string> Lines(string listName) =>
            Ui.Q<VisualElement>(listName).Query<Label>().ToList().Select(l => l.text).ToList();
    }

    internal static class UiQuery
    {
        // 인라인 display(뷰가 SetVisible로 바꾸는 값)를 조상까지 본다
        public static bool IsShown(VisualElement e)
        {
            for (VisualElement v = e; v != null; v = v.parent)
                if (v.style.display.keyword == StyleKeyword.Undefined && v.style.display.value == DisplayStyle.None)
                    return false;
            return true;
        }
    }

    public enum UiClickPath
    {
        None,
        RealPointer,             // 가상 마우스(Input System 장치) → InputForUI → UI Toolkit (EventSystem 없음, 실제 게임과 같은 경로)
        SyntheticPointerEvents,  // PointerDown/PointerUp 이벤트를 버튼에 직접 보냄 (Clickable 조작기 경로)
        NavigationSubmit,        // NavigationSubmitEvent (Button의 제출 처리)
    }

    // UI Toolkit 버튼 클릭 (M4 2b "확신 없는 API": EventSystem 없이·timeScale 0에서 Button.clicked).
    // 먼저 실제 입력 경로(가상 마우스 장치)로 누르고, clicked가 오지 않으면 그 사실을 기록한 뒤 합성 이벤트로 누른다.
    // 기록(Attempts)은 테스트가 보고·단정한다. 배치모드 포커스 대응은 SimMouse와 같다(Dispose에서 되돌림).
    internal sealed class UiPointer : IDisposable
    {
        public static readonly List<string> Attempts = new List<string>();

        private readonly Mouse mouse;
        private readonly InputSettings settings;
        private readonly InputSettings.BackgroundBehavior originalBackground;
        private readonly InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior;

        public UiClickPath LastPath { get; private set; }

        public UiPointer()
        {
            settings = InputSystem.settings;
            originalBackground = settings.backgroundBehavior;
            originalEditorBehavior = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>("AshfallTestUiPointer");
        }

        // allowFallback false면 실제 경로만 시도한다.
        public IEnumerator Click(Button button, bool allowFallback = true)
        {
            Assert.IsNotNull(button, "버튼 없음");
            Assert.IsTrue(UiQuery.IsShown(button), $"{button.name}: 보이지 않는 버튼");
            Assert.IsTrue(button.enabledInHierarchy, $"{button.name}: 비활성 버튼");
            bool fired = false;
            Action probe = () => fired = true;
            button.clicked += probe;
            string name = button.name;
            float timeScale = Time.timeScale;
            try
            {
                LastPath = UiClickPath.None;

                // 1) 실제 입력 경로
                Vector2 panelPos = button.worldBound.center;
                Vector2 screenBottomLeft = PanelToScreenBottomLeft(button.panel, panelPos);
                Queue(screenBottomLeft, false);
                yield return null;
                yield return null;
                Queue(screenBottomLeft, true);
                yield return null;
                yield return null;
                Queue(screenBottomLeft, false);
                yield return null;
                yield return null;
                // 다른 버튼을 늦게 누르지 않게 포인터를 치운다
                Queue(Vector2.zero, false);
                yield return null;
                if (fired)
                {
                    LastPath = UiClickPath.RealPointer;
                    Record(name, timeScale, panelPos, screenBottomLeft);
                    yield break;
                }
                if (!allowFallback)
                {
                    Record(name, timeScale, panelPos, screenBottomLeft);
                    yield break;
                }

                // 2) 합성 포인터 이벤트 (Clickable: 누름 → 버튼 안에서 뗌 → clicked)
                if (button.panel != null)
                {
                    var down = new Event { type = EventType.MouseDown, mousePosition = panelPos, button = 0, clickCount = 1 };
                    using (PointerDownEvent e = PointerDownEvent.GetPooled(down))
                        button.SendEvent(e);
                    var up = new Event { type = EventType.MouseUp, mousePosition = panelPos, button = 0, clickCount = 1 };
                    using (PointerUpEvent e = PointerUpEvent.GetPooled(up))
                        button.SendEvent(e);
                    yield return null;
                }
                if (fired)
                {
                    LastPath = UiClickPath.SyntheticPointerEvents;
                    Record(name, timeScale, panelPos, screenBottomLeft);
                    yield break;
                }

                // 3) 제출 이벤트
                if (button.panel != null)
                {
                    using (NavigationSubmitEvent e = NavigationSubmitEvent.GetPooled())
                        button.SendEvent(e);
                    yield return null;
                }
                if (fired)
                    LastPath = UiClickPath.NavigationSubmit;
                Record(name, timeScale, panelPos, screenBottomLeft);
            }
            finally
            {
                button.clicked -= probe;
            }
        }

        private void Record(string name, float timeScale, Vector2 panelPos, Vector2 screen)
        {
            string line = $"{name}: {LastPath} (timeScale {timeScale}, 패널 {panelPos}, 화면 {screen}, Screen {Screen.width}x{Screen.height}, " +
                          $"isFocused {Application.isFocused})";
            Attempts.Add(line);
            Debug.Log("[UiPointer] " + line);
        }

        // 패널 좌표 → Input System 화면 좌표(왼쪽 아래 원점). RuntimePanelUtils.ScreenToPanel(왼쪽 위 원점)이 선형이므로
        // 두 점으로 역변환한다. InputSystemProvider가 y를 뒤집어 패널로 보낸다(Screen.height - y).
        public static Vector2 PanelToScreenBottomLeft(IPanel panel, Vector2 panelPos)
        {
            Vector2 o = RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero);
            Vector2 u = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(100f, 100f)) - o;
            float sx = Mathf.Abs(u.x) > 1e-4f ? (panelPos.x - o.x) * 100f / u.x : panelPos.x;
            float sy = Mathf.Abs(u.y) > 1e-4f ? (panelPos.y - o.y) * 100f / u.y : panelPos.y;
            return new Vector2(sx, Screen.height - sy);
        }

        private void Queue(Vector2 position, bool leftDown)
        {
            var state = new MouseState { position = position }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, leftDown);
            InputSystem.QueueStateEvent(mouse, state);
        }

        public void Dispose()
        {
            if (mouse != null && mouse.added)
                InputSystem.RemoveDevice(mouse);
            if (settings != null)
            {
                settings.backgroundBehavior = originalBackground;
                settings.editorInputBehaviorInPlayMode = originalEditorBehavior;
            }
        }
    }
}
#endif
