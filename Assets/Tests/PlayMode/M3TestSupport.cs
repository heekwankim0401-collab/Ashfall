#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.UI;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M3 통합 테스트 공용 (M3 설계 §9, 4단계): M3 씬 로드 + 주요 참조, 적 정리·배치, 플레이어 순간이동·조준.
    // 씬은 Ashfall/Setup/Create M3 Test Scenes(M3SceneSetup)가 만든다. 빌드 설정에 없어 에디터 경로로 불러온다.
    // 씬 오브젝트 이름은 3단계 메모: Enemy_A1~A3(SCV 분대 1, 무장 권총·권총·SMG), Enemy_B1~B2(SCV 분대 2, 권총·SMG),
    // Enemy_C1~C2(RDR 분대 3, 소총, 경로 없음). 적 seed는 씬 순번 + 1로 고정.
    internal sealed class M3Scene
    {
        public const string AiScenePath = "Assets/_Project/Scenes/Test/M3_AI.unity";
        public const string PerfScenePath = "Assets/_Project/Scenes/Test/M3_AIPerf.unity";

        // 시야가 트인 빈 통로 (M3_AI: x 36~50에는 구조물·남은 경로가 없다. 분대 B를 지우면 비어 있다)
        public const float OpenLaneX = 42f;
        // 어느 적에게서도 60m 넘게 떨어진 구석 (시야 밖으로 치울 때)
        public static readonly Vector3 FarCorner = new Vector3(-45f, 0.05f, -45f);

        private const float LoadTimeoutSec = 5f;

        public GameManager Gm;
        public AiDirector Director;
        public PlayerMotor Motor;
        public PlayerHealth Health;
        public PlayerInventoryBehaviour InventoryBehaviour;
        public PlayerInventory Inventory;
        public WeaponController Weapon;
        public ProjectileSystem Projectiles;
        public GameUIRoot Ui;
        public readonly List<EnemyAgent> Enemies = new List<EnemyAgent>();

        public EventBus Events => Gm.Events;
        public BodyHealthModel PlayerBody => Health.Body;
        public Transform PlayerEye => Motor.transform.Find("Eye");
        public Transform PlayerCamera => PlayerEye.GetChild(0);
        // 든 무기의 총구 (WeaponController.MuzzlePosition — 프리팹 뷰모델이면 그 총구, 아니면 직렬화 총구). M7 리뷰 7A 낮음 7: 계층 순서 검색을 쓰지 않는다.
        public Vector3 PlayerMuzzlePosition => Weapon.MuzzlePosition;

        // 씬을 불러오고 모든 적이 등록(Brain)·무장(Weapon)되고 director가 서비스를 잡을 때까지 기다린다.
        public static IEnumerator Load(M3Scene into, string path = AiScenePath)
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            into.Gm = GameManager.Instance;
            Assert.IsNotNull(into.Gm, "GameManager 없음");
            into.Director = Object.FindFirstObjectByType<AiDirector>();
            into.Motor = Object.FindFirstObjectByType<PlayerMotor>();
            into.Health = Object.FindFirstObjectByType<PlayerHealth>();
            into.InventoryBehaviour = Object.FindFirstObjectByType<PlayerInventoryBehaviour>();
            into.Weapon = Object.FindFirstObjectByType<WeaponController>();
            into.Projectiles = Object.FindFirstObjectByType<ProjectileSystem>();
            into.Ui = Object.FindFirstObjectByType<GameUIRoot>();
            Assert.IsNotNull(into.Director, "AiDirector 없음");
            Assert.IsNotNull(into.Motor, "PlayerMotor 없음");
            Assert.IsNotNull(into.Health, "PlayerHealth 없음");
            Assert.IsNotNull(into.InventoryBehaviour, "PlayerInventoryBehaviour 없음");
            Assert.IsNotNull(into.Weapon, "WeaponController 없음");
            Assert.IsNotNull(into.Projectiles, "ProjectileSystem 없음");
            into.Inventory = into.InventoryBehaviour.Inventory;
            into.Enemies.Clear();
            into.Enemies.AddRange(Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.InstanceID));
            Assert.Greater(into.Enemies.Count, 0, "적 없음");

            M3Scene scene = into;
            yield return M2Scene.WaitUntil(() =>
                scene.Director.Target != null && scene.Director.Projectiles != null &&
                scene.Enemies.All(e => e.Brain != null && e.Weapon != null) && scene.Weapon.IsArmed,
                LoadTimeoutSec);
        }

        public EnemyAgent Enemy(string id)
        {
            EnemyAgent agent = Enemies.FirstOrDefault(e => e != null && e.name == "Enemy_" + id);
            Assert.IsNotNull(agent, $"Enemy_{id} 없음");
            return agent;
        }

        // 지정한 적만 남기고 나머지는 파괴한다. 호출 뒤 한 프레임 넘겨야 실제로 사라진다.
        // 살아 있는 적이 파괴되면 EnemyAgent가 스스로 AI에서 빠진다(director 등록 해제 + 엄폐·우회권 반납 + 분대 탈퇴 →
        // 남은 분대원 중 순서가 앞선 쪽이 리더, 리뷰 5). 테스트는 그 정리를 여기서 확인한다(직접 Squad.Leave 하지 않음).
        public IEnumerator KeepOnly(params string[] ids)
        {
            var keep = new HashSet<string>(ids.Select(i => "Enemy_" + i));
            var removedBrains = new List<EnemyBrain>();
            foreach (EnemyAgent e in Enemies.ToArray())
            {
                if (e != null && !keep.Contains(e.name))
                {
                    if (e.Brain != null)
                        removedBrains.Add(e.Brain);
                    Object.Destroy(e.gameObject);
                    Enemies.Remove(e);
                }
            }
            Assert.AreEqual(ids.Length, Enemies.Count, "남긴 적 수");
            yield return null;
            Assert.AreEqual(ids.Length, Director.Agents.Count, "director 등록 수");
            foreach (EnemyBrain b in removedBrains)
            {
                Assert.IsTrue(b.IsDetached, $"파괴된 적 {b.AgentId}: AI에서 빠짐");
                if (b.Squad != null)
                    Assert.AreEqual(-1, b.Squad.IndexOf(b.AgentId), $"파괴된 적 {b.AgentId}: 분대 탈퇴");
                Assert.AreEqual(-1, Director.Covers.ReservedBy(b.AgentId), $"파괴된 적 {b.AgentId}: 엄폐 예약 해제");
            }
            foreach (EnemyAgent e in Enemies)
            {
                if (e.Brain.Squad == null)
                    continue;
                int sameSquad = Enemies.Count(o => o.SquadId == e.SquadId);
                Assert.AreEqual(sameSquad, e.Brain.Squad.Count, $"{e.name}: 분대 인원 = 남은 같은 분대 적 수");
            }
        }

        // NavMeshAgent.Warp로 옮기고 yaw(도)를 바라보게 한다.
        public static void WarpEnemy(EnemyAgent enemy, Vector3 position, float yawDeg)
        {
            var nav = enemy.GetComponent<NavMeshAgent>();
            Assert.IsTrue(nav.Warp(position), $"{enemy.name}: Warp 실패 {position}");
            nav.ResetPath();   // 이전 목적지로 계속 걷지 않게 (AI가 다음 의도에서 다시 정한다)
            enemy.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            // autoSyncTransforms가 꺼져 있어 다음 물리 스텝 전까지 콜라이더가 옛 자리에 남는다 → 바로 맞힐 수 있게 동기화
            Physics.SyncTransforms();
        }

        // 조건을 기다린다. 실패 메시지에 상황 설명(what)을 붙인다.
        public static IEnumerator WaitUntil(Func<bool> condition, float timeoutSec, Func<string> what)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSec;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail($"{timeoutSec}초 안에 조건이 만족되지 않았다: {what()}");
                yield return null;
            }
        }

        public static Transform EnemyEye(EnemyAgent enemy) => enemy.transform.Find("Eye");

        // 플레이어 순간이동 (CharacterController를 잠깐 끄고 옮긴다). lookAt이 있으면 그쪽 수평 방향을 본다. 시선 상하는 0.
        public void TeleportPlayer(Vector3 position, Vector3? lookAt = null)
        {
            Transform t = Motor.transform;
            var cc = t.GetComponent<CharacterController>();
            cc.enabled = false;
            t.position = position;
            if (lookAt.HasValue)
            {
                Vector3 flat = lookAt.Value - position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 1e-6f)
                    t.rotation = Quaternion.LookRotation(flat);
            }
            cc.enabled = true;
            Physics.SyncTransforms();
            SetPitch(0f);
        }

        public float CurrentPitch
        {
            get
            {
                float x = PlayerEye.localEulerAngles.x;
                return x > 180f ? x - 360f : x;
            }
        }

        // 시선 상하(도, + = 아래). PlayerMotor는 pitch를 숨기므로 공개 API AddRecoil(위로 차는 양)로 맞춘다. 다음 Update에 반영.
        public void SetPitch(float pitchDeg) => Motor.AddRecoil(CurrentPitch - pitchDeg, 0f);

        // 기준점(muzzle 또는 카메라)에서 point로 향하도록 플레이어 몸(yaw)과 시선(pitch)을 돌린다.
        // 기준점이 회전과 함께 움직이므로 몇 프레임 반복해 수렴시킨다.
        public IEnumerator AimAt(Vector3 point, bool fromMuzzle, int iterations = 4) =>
            AimAt(() => point, fromMuzzle, iterations);

        public IEnumerator AimAt(Func<Vector3> pointOf, bool fromMuzzle, int iterations = 4)
        {
            for (int i = 0; i < iterations; i++)
            {
                Vector3 point = pointOf();
                Vector3 origin = fromMuzzle ? PlayerMuzzlePosition : PlayerCamera.position;
                Vector3 d = (point - origin).normalized;
                Vector3 flat = new Vector3(d.x, 0f, d.z);
                if (flat.sqrMagnitude > 1e-6f)
                    Motor.transform.rotation = Quaternion.LookRotation(flat);
                SetPitch(Mathf.Asin(Mathf.Clamp(-d.y, -1f, 1f)) * Mathf.Rad2Deg);
                yield return null;
                yield return null;
            }
        }

        // 플레이어 7부위 HP 합
        public float PlayerTotalHp()
        {
            float sum = 0f;
            foreach (BodyPart p in Enum.GetValues(typeof(BodyPart)))
                sum += PlayerBody.GetHp(p);
            return sum;
        }

        // from→to 사이에 ignore 루트 소속이 아닌 콜라이더가 있으면 true (트리거 무시)
        public static bool IsBlocked(Vector3 from, Vector3 to, params Transform[] ignoreRoots)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 1e-4f)
                return false;
            RaycastHit[] hits = Physics.RaycastAll(from, d / length, length, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                Transform root = h.collider.transform.root;
                if (!ignoreRoots.Any(r => r != null && r == root))
                    return true;
            }
            return false;
        }

        // 기록용: 상태 전이 (이전, 다음, 시각)
        public static List<(EnemyState from, EnemyState to, float time)> RecordStates(EnemyAgent enemy)
        {
            var list = new List<(EnemyState, EnemyState, float)>();
            enemy.Brain.StateChanged += (a, b) => list.Add((a, b, Time.time));
            return list;
        }

        public static string Describe(IEnumerable<(EnemyState from, EnemyState to, float time)> log) =>
            string.Join(", ", log.Select(l => $"{l.from}→{l.to}@{l.time:0.00}"));

        // 플레이어 대신 무적 표적을 AI 표적으로 등록 (성능·우회 테스트). 플레이어 등록은 해제하고 구석으로 치운다.
        // TestTarget은 AI가 돌아가는 채로 지워도 된다(EnemyAgent가 파괴된 표적 참조를 놓는다, 리뷰 8).
        public IEnumerator SwapTargetTo(TestTarget target, Vector3 parkPlayerAt)
        {
            Gm.Services.Unregister<IPlayerTarget>(Motor);
            TeleportPlayer(parkPlayerAt);
            target.Register();
            AiDirector director = Director;
            // director는 targetResolveRetrySec(1초)마다 다시 조회한다
            yield return M2Scene.WaitUntil(() => ReferenceEquals(director.Target, target), 3f);
        }
    }

    // 테스트용 무적 표적 (M3 설계 §1.8 성능 측정): IPlayerTarget + IDamageable(피해 무시, 기록만). 루트 = 자기 자신.
    internal sealed class TestTarget : MonoBehaviour, IPlayerTarget, IDamageable
    {
        private const float StandHeight = 1.8f;
        private const float CrouchHeight = 1.2f;
        private const float EyeBelowTop = 0.12f;   // 플레이어와 같은 눈 높이 규칙

        private CapsuleCollider capsule;
        private bool registered;

        public int HitCount { get; private set; }
        public float DamageTaken { get; private set; }

        public Transform Root => transform.root;
        public Vector3 Position => transform.position;
        public Vector3 EyePosition => transform.position + Vector3.up * (capsule.height - EyeBelowTop);
        public Vector3 CenterPosition => transform.position + Vector3.up * (capsule.height * 0.5f);
        public Vector3 Velocity => Vector3.zero;
        public TargetPosture Posture { get; private set; } = TargetPosture.Standing;
        public bool IsDead => false;

        public static TestTarget Create(Vector3 position)
        {
            var go = new GameObject("TestTarget");
            go.transform.position = position;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.3f;
            capsule.height = StandHeight;
            capsule.center = new Vector3(0f, StandHeight * 0.5f, 0f);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var target = go.AddComponent<TestTarget>();
            target.capsule = capsule;
            return target;
        }

        public void SetPosture(TargetPosture posture)
        {
            Posture = posture;
            float h = posture == TargetPosture.Standing ? StandHeight : CrouchHeight;
            capsule.height = h;
            capsule.center = new Vector3(0f, h * 0.5f, 0f);
            Physics.SyncTransforms();
        }

        public void MoveTo(Vector3 position)
        {
            transform.position = position;
            Physics.SyncTransforms();
        }

        public void Register()
        {
            if (GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Register<IPlayerTarget>(this);
            registered = true;
        }

        public void ApplyDamage(in DamageInfo info)
        {
            HitCount++;
            DamageTaken += info.Amount;
        }

        private void OnDisable()
        {
            if (registered && GameManager.Instance != null)
                GameManager.Instance.Services.Unregister<IPlayerTarget>(this);
            registered = false;
        }
    }

    // 가상 마우스 (LMB 사격·RMB 조준). SimKeyboard와 같은 방식으로 이벤트 큐에 넣어 프로젝트 입력 액션을 거친다.
    // 포커스 설정은 생성 시 값을 저장했다가 Dispose에서 되돌린다 → SimKeyboard와 같이 쓰면 만든 역순으로 Dispose할 것(SimInput).
    internal sealed class SimMouse : IDisposable
    {
        private readonly Mouse mouse;
        private readonly InputSettings settings;
        private readonly InputSettings.BackgroundBehavior originalBackground;
        private readonly InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior;
        private bool left, right;

        public SimMouse()
        {
            settings = InputSystem.settings;
            originalBackground = settings.backgroundBehavior;
            originalEditorBehavior = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>("AshfallTestMouse");
        }

        public void SetLeft(bool pressed)
        {
            left = pressed;
            Queue();
        }

        public void SetRight(bool pressed)
        {
            right = pressed;
            Queue();
        }

        // LMB를 한 프레임 눌렀다 뗀다
        public IEnumerator ClickLeft()
        {
            SetLeft(true);
            yield return null;
            SetLeft(false);
            yield return null;
        }

        private void Queue()
        {
            var state = new MouseState().WithButton(MouseButton.Left, left).WithButton(MouseButton.Right, right);
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

    // 키보드 + 마우스 (만든 역순으로 해제해 입력 설정을 원래대로 되돌린다)
    internal sealed class SimInput : IDisposable
    {
        public readonly SimKeyboard Keyboard;
        public readonly SimMouse Mouse;

        public SimInput()
        {
            Keyboard = new SimKeyboard();
            Mouse = new SimMouse();
        }

        public void Dispose()
        {
            Mouse.Dispose();
            Keyboard.Dispose();
        }
    }
}
#endif
