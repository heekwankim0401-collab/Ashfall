#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.UI;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M2 통합 테스트 공용: M2 씬 로드 + 주요 컴포넌트 참조.
    // 씬은 Ashfall/Setup/Create M2 Test Scene(M2SceneSetup)이 만든다. 빌드 설정에 없으므로 에디터 경로로 불러온다.
    internal sealed class M2Scene
    {
        public const string ScenePath = "Assets/_Project/Scenes/Test/M2_HealthInventory.unity";
        private const int WarmupFrames = 5;

        public GameManager Gm;
        public PlayerHealth Health;
        public PlayerMotor Motor;
        public PlayerInventoryBehaviour InventoryBehaviour;
        public PlayerInventory Inventory;
        public WeaponController Weapon;
        public GameUIRoot Ui;
        public UIDocument Document;
        public ProjectileSystem Projectiles;

        public BodyHealthModel Body => Health.Body;
        public EventBus Events => Gm.Events;

        public static IEnumerator Load(M2Scene into)
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < WarmupFrames; i++)
                yield return null;

            into.Gm = GameManager.Instance;
            Assert.IsNotNull(into.Gm, "GameManager 없음");
            into.Health = Object.FindFirstObjectByType<PlayerHealth>();
            into.Motor = Object.FindFirstObjectByType<PlayerMotor>();
            into.InventoryBehaviour = Object.FindFirstObjectByType<PlayerInventoryBehaviour>();
            into.Weapon = Object.FindFirstObjectByType<WeaponController>();
            into.Ui = Object.FindFirstObjectByType<GameUIRoot>();
            into.Projectiles = Object.FindFirstObjectByType<ProjectileSystem>();
            Assert.IsNotNull(into.Health, "PlayerHealth 없음");
            Assert.IsNotNull(into.Motor, "PlayerMotor 없음");
            Assert.IsNotNull(into.InventoryBehaviour, "PlayerInventoryBehaviour 없음");
            Assert.IsNotNull(into.Weapon, "WeaponController 없음");
            Assert.IsNotNull(into.Ui, "GameUIRoot 없음");
            Assert.IsNotNull(into.Projectiles, "ProjectileSystem 없음");
            into.Inventory = into.InventoryBehaviour.Inventory;
            into.Document = into.Ui.GetComponent<UIDocument>();
            Assert.IsNotNull(into.Ui.Presenter, "GameUIRoot 비활성");
        }

        public LootContainer LootBox(string objectName)
        {
            var go = GameObject.Find(objectName);
            Assert.IsNotNull(go, $"{objectName} 없음");
            return go.GetComponent<LootContainer>();
        }

        public static WorldItem[] WorldItems() => Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None);

        public static WorldItem FindWorldItem(ItemInstance item) =>
            WorldItems().FirstOrDefault(w => w != null && ReferenceEquals(w.Item, item));

        public static WorldItem FindWorldItem(string definitionId) =>
            WorldItems().FirstOrDefault(w => w != null && w.Item != null && w.Item.Definition.id == definitionId);

        public IGridView StorageGrid(GridKind kind) => Inventory.StorageGrids.First(g => g.Ref.Kind == kind);

        public ItemInstance FindStored(string definitionId) =>
            Inventory.StorageGrids.SelectMany(g => g.Items).Select(p => p.Item)
                .FirstOrDefault(i => i.Definition.id == definitionId);

        public VisualElement UiRoot => Document.rootVisualElement;

        // 요소 아래 모든 Label 텍스트
        public static IEnumerable<string> LabelTexts(VisualElement root) =>
            root.Query<Label>().ToList().Select(l => l.text ?? string.Empty);

        public static readonly Regex DigitOrPercent = new Regex("[0-9%０-９]");

        // 프레임마다 Time.deltaTime을 쌓으며 조건을 기다린다. 쌓인 시간을 돌려준다.
        public static IEnumerator WaitUntil(Func<bool> condition, float timeoutSec, Action<float> elapsedOut = null)
        {
            float elapsed = 0f;
            float deadline = Time.realtimeSinceStartup + timeoutSec;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail($"{timeoutSec}초 안에 조건이 만족되지 않았다");
                yield return null;
                elapsed += Time.deltaTime;
            }
            elapsedOut?.Invoke(elapsed);
        }

        public static IEnumerator WaitSeconds(float sec)
        {
            float elapsed = 0f;
            while (elapsed < sec)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
        }
    }

    // 버스 이벤트 기록 (구독은 Dispose에서 해지)
    internal sealed class EventLog<T> : IDisposable where T : struct, IGameEvent
    {
        private readonly EventBus bus;
        public readonly List<T> Received = new List<T>();
        public readonly List<int> Frames = new List<int>();

        public EventLog(EventBus bus)
        {
            this.bus = bus;
            bus.Subscribe<T>(OnEvent);
        }

        private void OnEvent(T evt)
        {
            Received.Add(evt);
            Frames.Add(Time.frameCount);
        }

        public int Count => Received.Count;
        public T Last => Received[Received.Count - 1];

        public void Dispose() => bus.Unsubscribe<T>(OnEvent);
    }

    // 가상 키보드. 실제 키 입력처럼 이벤트 큐로 넣어 프로젝트 입력 액션(<Keyboard>/t 등)을 그대로 거친다.
    // 배치모드에서는 게임 뷰 포커스가 없으므로 테스트 동안 현재 InputSettings의 포커스 관련 값 두 개만 바꾸고 끝나면 되돌린다.
    // (설정 객체를 갈아 끼우면 Input System이 임시 기본 설정을 파괴하므로 값만 바꾼다. 프로젝트에 설정 에셋이 없어 파일은 바뀌지 않는다.)
    internal sealed class SimKeyboard : IDisposable
    {
        private readonly Keyboard keyboard;
        private readonly InputSettings settings;
        private readonly InputSettings.BackgroundBehavior originalBackground;
        private readonly InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior;
        private readonly HashSet<Key> held = new HashSet<Key>();

        public SimKeyboard()
        {
            settings = InputSystem.settings;
            originalBackground = settings.backgroundBehavior;
            originalEditorBehavior = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>("AshfallTestKeyboard");
        }

        public void Hold(params Key[] keys)
        {
            foreach (Key k in keys) held.Add(k);
            Queue();
        }

        public void Release(params Key[] keys)
        {
            foreach (Key k in keys) held.Remove(k);
            Queue();
        }

        // 한 프레임 눌렀다 뗀다 (WasPressedThisFrame이 한 번 true)
        public IEnumerator Tap(Key key)
        {
            Hold(key);
            yield return null;
            Release(key);
            yield return null;
        }

        private void Queue() => InputSystem.QueueStateEvent(keyboard, new KeyboardState(held.ToArray()));

        public void Dispose()
        {
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            if (settings != null)
            {
                settings.backgroundBehavior = originalBackground;
                settings.editorInputBehaviorInPlayMode = originalEditorBehavior;
            }
        }
    }
}
#endif
