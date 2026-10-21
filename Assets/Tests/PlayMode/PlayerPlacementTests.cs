#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M4 — IPlayerPlacement(PlayerMotor)와 RaidController 스폰 배치 (M4 설계 §1.1·§4.2·§4.3, TODO(GDD-?) Q2).
    // 간단 씬을 코드로 만든다: 바닥 + 플레이어(CharacterController·PlayerMotor·PlayerInventoryBehaviour, 데이터 에셋 사용).
    // 레이드 테스트는 바닥 위에 스폰 2곳·탈출 구역 1곳·RaidController를 둔다 (흐름 밖 씬이라 씬 전환은 없다).
    public class PlayerPlacementTests
    {
        private const string PlayerConfigPath = "Assets/_Project/Data/Player/PlayerConfig.asset";
        private const string InventoryConfigPath = "Assets/_Project/Data/Inventory/InventoryConfig.asset";
        private const int WarmupFrames = 3;

        private static readonly Vector3 SpawnA = new Vector3(40f, 0f, -30f);
        private static readonly Vector3 SpawnB = new Vector3(-40f, 0f, 30f);
        private static readonly Vector3 ExtractionPos = new Vector3(0f, 0f, 60f);

        private static int sceneCounter;
        private Scene scene;
        private GameObject player;
        private PlayerMotor motor;
        private Transform eye;
        private RaidConfigSO raidConfig;

        private EventBus Events => GameManager.Instance.Events;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            // 새 씬에 만든다. 이전 테스트가 남긴 씬의 오브젝트(플레이어·UI·지형 콜라이더)는 꺼서 서비스·구독·충돌을 없앤다.
            // 다른 씬은 내리지 않는다 — 테스트 러너의 초기 씬을 내리면 러너가 멈춘다 (2a에서 확인). 러너 오브젝트는 건드리지 않는다.
            scene = SceneManager.CreateScene($"PlayerPlacementTest_{++sceneCounter}");
            SceneManager.SetActiveScene(scene);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene other = SceneManager.GetSceneAt(i);
                if (other == scene || !other.isLoaded)
                    continue;
                foreach (GameObject root in other.GetRootGameObjects())
                    if (root.activeSelf && !IsTestRunnerObject(root))
                        root.SetActive(false);
            }
            yield return null;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(300f, 1f, 300f);

            BuildPlayer(Vector3.zero);
            for (int i = 0; i < WarmupFrames; i++)
                yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            GameManager.Instance?.Flow?.SetWorldPaused(false);
            Time.timeScale = 1f;
            if (raidConfig != null)
                Object.Destroy(raidConfig);
            // 이 테스트 씬을 내린다 (플레이어·서비스 정리). SetUp이 다른 씬을 내리지 않으므로 항상 2개 이상이다.
            if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        // ── 장면 구성 ────────────────────────────────────────

        private static bool IsTestRunnerObject(GameObject root) =>
            root.GetComponentsInChildren<MonoBehaviour>(true).Any(m =>
                m != null && m.GetType().Assembly.GetName().Name.Contains("TestRunner"));

        private void BuildPlayer(Vector3 feet)
        {
            var playerConfig = AssetDatabase.LoadAssetAtPath<PlayerConfigSO>(PlayerConfigPath);
            var inventoryConfig = AssetDatabase.LoadAssetAtPath<InventoryConfigSO>(InventoryConfigPath);
            Assert.IsNotNull(playerConfig, PlayerConfigPath);
            Assert.IsNotNull(inventoryConfig, InventoryConfigPath);

            player = new GameObject("Player");
            player.SetActive(false);
            player.transform.position = feet;
            player.AddComponent<CharacterController>();
            eye = new GameObject("Eye").transform;
            eye.SetParent(player.transform, false);

            motor = player.AddComponent<PlayerMotor>();
            SetFields(motor, ("config", playerConfig), ("eye", eye));
            var inventory = player.AddComponent<PlayerInventoryBehaviour>();
            SetFields(inventory, ("config", inventoryConfig), ("viewOrigin", eye));
            player.SetActive(true);
        }

        private static void SetFields(Object target, params (string Name, Object Value)[] fields)
        {
            var so = new SerializedObject(target);
            foreach ((string name, Object value) in fields)
                so.FindProperty(name).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T AddMarker<T>(string name, Vector3 position, float yawDeg, params (string Name, string Value)[] strings)
            where T : MonoBehaviour
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDeg, 0f));
            var component = go.AddComponent<T>();
            var so = new SerializedObject(component);
            foreach ((string field, string value) in strings)
                so.FindProperty(field).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        // 스폰 2곳(SP-A·SP-B, 규칙: EXT-01만) + EXT-01 구역 + RaidController(시드 고정)
        private RaidController BuildRaid(int seed = 1, bool buildMarkers = true)
        {
            raidConfig = ScriptableObject.CreateInstance<RaidConfigSO>();
            raidConfig.timeLimitSec = 1500f;
            raidConfig.extractions = new[]
            {
                new ExtractionPointConfig { id = "EXT-01", displayName = "남쪽 도로", waitSec = 10f, radiusM = 8f, requiredSwitchId = "" },
            };
            raidConfig.spawnRules = new[]
            {
                new SpawnRule { spawnId = "SP-A", activeExtractionIds = new[] { "EXT-01" } },
                new SpawnRule { spawnId = "SP-B", activeExtractionIds = new[] { "EXT-01" } },
            };
            raidConfig.alwaysActiveIds = new[] { "EXT-01" };
            raidConfig.endDelaySec = 0.2f;
            raidConfig.zoneHeightToleranceM = 3f;

            if (buildMarkers)
            {
                AddMarker<PlayerSpawnPoint>("Spawn_A", SpawnA, 90f, ("id", "SP-A"));
                AddMarker<PlayerSpawnPoint>("Spawn_B", SpawnB, 270f, ("id", "SP-B"));
                AddMarker<ExtractionZone>("EXT-01", ExtractionPos, 0f, ("id", "EXT-01"));
                AddMarker<PointOfInterest>("POI-HWY", new Vector3(-60f, 0f, -100f), 0f, ("id", "POI-HWY"), ("displayName", "붕괴된 고속도로"));
            }

            var go = new GameObject("RaidController");
            go.SetActive(false);
            var controller = go.AddComponent<RaidController>();
            var so = new SerializedObject(controller);
            so.FindProperty("config").objectReferenceValue = raidConfig;
            so.FindProperty("seed").intValue = seed;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(true);
            return controller;
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        private static void AssertHorizontal(Vector3 expected, Vector3 actual, string message)
        {
            Assert.AreEqual(expected.x, actual.x, 0.01f, message + " x");
            Assert.AreEqual(expected.z, actual.z, 0.01f, message + " z");
        }

        // ── IPlayerPlacement ────────────────────────────────

        [Test]
        public void PlayerMotor_RegisteredAsPlacement()
        {
            Assert.IsTrue(GameManager.Instance.Services.TryGet(out IPlayerPlacement placement));
            Assert.AreSame(motor, placement);
            Assert.IsTrue(GameManager.Instance.Services.TryGet(out IPlayerTarget target));
            Assert.AreSame(motor, target, "같은 오브젝트 (IPlayerTarget 재등록 없음)");
        }

        [UnityTest]
        public IEnumerator PlaceAt_MovesFeetAndYaw_AndStays()
        {
            Vector3 destination = new Vector3(25f, 0f, -12f);
            motor.PlaceAt(destination, 135f);

            AssertHorizontal(destination, player.transform.position, "즉시");
            Assert.AreEqual(135f, player.transform.eulerAngles.y, 0.01f);

            yield return Frames(10);

            AssertHorizontal(destination, motor.Position, "CharacterController가 이전 위치로 되돌리지 않는다");
            Assert.AreEqual(destination.y, motor.Position.y, 0.2f, "바닥 위");
            Assert.AreEqual(135f, player.transform.eulerAngles.y, 0.01f);
        }

        [Test]
        public void PlaceAt_PhysicsSyncedSameFrame()
        {
            Vector3 destination = new Vector3(-30f, 0f, 20f);
            motor.PlaceAt(destination, 0f);

            // 같은 프레임 물리 쿼리가 새 위치의 캡슐을 본다 (Physics.SyncTransforms)
            Collider[] hits = Physics.OverlapSphere(destination + Vector3.up * 0.9f, 0.3f);
            Assert.IsTrue(hits.Any(h => h.transform.root == player.transform), "새 위치에서 캡슐이 잡힌다");
            Collider[] old = Physics.OverlapSphere(Vector3.up * 0.9f, 0.3f);
            Assert.IsFalse(old.Any(h => h.transform.root == player.transform), "이전 위치에는 없다");
        }

        [UnityTest]
        public IEnumerator PlaceAt_LevelsView()
        {
            motor.AddRecoil(20f, 0f);   // 위를 보게 만든다
            yield return Frames(2);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, eye.localEulerAngles.x)), Is.GreaterThan(5f), "준비: 시선이 기울어 있음");

            motor.PlaceAt(new Vector3(5f, 0f, 5f), 45f);
            yield return Frames(2);

            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, eye.localEulerAngles.x), 0.01f, "시선 pitch 0");
            Assert.AreEqual(45f, player.transform.eulerAngles.y, 0.01f);
        }

        [UnityTest]
        public IEnumerator Disable_UnregistersPlacement()
        {
            motor.enabled = false;
            yield return null;
            Assert.IsFalse(GameManager.Instance.Services.TryGet(out IPlayerPlacement _));
            motor.enabled = true;
            yield return null;
            Assert.IsTrue(GameManager.Instance.Services.TryGet(out IPlayerPlacement again));
            Assert.AreSame(motor, again);
        }

        // ── RaidController 스폰 배치·종료 ───────────────────

        [UnityTest]
        public IEnumerator Raid_PlacesPlayerAtPickedSpawn_AndRegistersInfo()
        {
            using var started = new EventLog<RaidStarted>(Events);
            RaidController controller = BuildRaid(seed: 1);
            yield return Frames(WarmupFrames);

            Assert.IsNotNull(controller.Session, "Start에서 세션 생성");
            Assert.AreEqual(1, started.Count, "RaidStarted 1회");
            Assert.AreEqual(controller.SpawnId, started.Last.SpawnId);
            Assert.AreEqual(1500f, started.Last.TimeLimitSec);

            Vector3 expected = controller.SpawnId == "SP-A" ? SpawnA : SpawnB;
            float expectedYaw = controller.SpawnId == "SP-A" ? 90f : 270f;
            Assert.IsTrue(controller.SpawnId == "SP-A" || controller.SpawnId == "SP-B");
            AssertHorizontal(expected, motor.Position, $"스폰 {controller.SpawnId}");
            Assert.AreEqual(expectedYaw, player.transform.eulerAngles.y, 0.01f);
            CollectionAssert.AreEqual(new[] { "EXT-01" }, controller.ActiveExtractionIds);

            Assert.IsTrue(GameManager.Instance.Services.TryGet(out IRaidInfo info));
            Assert.AreSame(controller, info);
            Assert.AreEqual(1, info.ActiveExtractions.Count);
            Assert.AreEqual(new Vector2(ExtractionPos.x, ExtractionPos.z), info.ActiveExtractions[0].PositionXZ);
            Assert.AreEqual("붕괴된 고속도로", info.PointsOfInterest.Single().DisplayName);
            Assert.That(info.RemainingSec, Is.InRange(1490f, 1500f));
            Assert.AreEqual(new Rect(-500f, -500f, 1000f, 1000f), info.MapAreaXZ);
        }

        [UnityTest]
        public IEnumerator Raid_SameSeed_SameSpawn()
        {
            RaidController first = BuildRaid(seed: 7);
            yield return Frames(WarmupFrames);
            string spawn = first.SpawnId;
            Object.Destroy(first.gameObject);
            Object.Destroy(raidConfig);
            yield return null;

            RaidController second = BuildRaid(seed: 7, buildMarkers: false);   // 표식은 그대로 (id 중복 방지)
            yield return Frames(WarmupFrames);
            Assert.AreEqual(spawn, second.SpawnId, "시드 고정이면 같은 스폰");
        }

        [UnityTest]
        public IEnumerator Raid_ExtractAtExt01_PublishesStatusAndEnds()
        {
            using var status = new EventLog<ExtractionStatusChanged>(Events);
            using var ended = new EventLog<RaidEnded>(Events);
            RaidController controller = BuildRaid();
            yield return Frames(WarmupFrames);

            motor.PlaceAt(ExtractionPos, 0f);
            Time.captureDeltaTime = 0.1f;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!controller.Session.IsOver && Time.realtimeSinceStartup < deadline)
                yield return null;
            Time.captureDeltaTime = 0f;

            Assert.IsTrue(controller.Session.IsOver, "10초 대기 후 탈출");
            Assert.AreEqual(RaidOutcome.Extracted, controller.LastResult.Outcome);
            Assert.AreEqual("EXT-01", controller.LastResult.ExtractionId);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual(RaidOutcome.Extracted, ended.Last.Outcome);
            Assert.AreEqual("남쪽 도로", ended.Last.ExtractionName);

            List<ExtractionStatusChanged> visible = status.Received.Where(s => s.InZone).ToList();
            Assert.That(visible.Count, Is.InRange(9, 11), "초가 바뀔 때만 발행 (10 → 1)");
            Assert.AreEqual(10, visible[0].SecondsLeft);
            Assert.AreEqual("남쪽 도로", visible[0].DisplayName);
            Assert.AreEqual(ExtractionBlock.None, visible[0].Block);
            Assert.IsFalse(status.Last.InZone, "종료 시 숨김");

            // 종료 연출: 세계 정지 → (흐름 밖 씬) endDelaySec 뒤 정지 해제
            Assert.IsTrue(GameManager.Instance.Flow.IsWorldPaused, "종료 직후 세계 정지");
            Assert.AreEqual(0f, Time.timeScale);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("흐름 밖 씬"));
            float waitUntil = Time.realtimeSinceStartup + 3f;
            while (GameManager.Instance.Flow.IsWorldPaused && Time.realtimeSinceStartup < waitUntil)
                yield return null;
            Assert.IsFalse(GameManager.Instance.Flow.IsWorldPaused);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator Raid_AbandonRequested_EndsAbandoned_LostEverything()
        {
            using var ended = new EventLog<RaidEnded>(Events);
            RaidController controller = BuildRaid();
            yield return Frames(WarmupFrames);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("흐름 밖 씬"));
            Events.Publish(new RaidAbandonRequested());

            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual(RaidOutcome.Abandoned, controller.LastResult.Outcome);
            Assert.IsEmpty(controller.LastResult.CarriedOut, "RULE-01");

            float waitUntil = Time.realtimeSinceStartup + 3f;
            while (GameManager.Instance.Flow.IsWorldPaused && Time.realtimeSinceStartup < waitUntil)
                yield return null;
            Assert.IsFalse(GameManager.Instance.Flow.IsWorldPaused);
        }

        [UnityTest]
        public IEnumerator Raid_Destroyed_UnregistersAndUnsubscribes()
        {
            int diedBefore = Events.SubscriberCount<PlayerDied>();
            RaidController controller = BuildRaid();
            yield return Frames(WarmupFrames);
            Assert.AreEqual(diedBefore + 1, Events.SubscriberCount<PlayerDied>());

            Object.Destroy(controller.gameObject);
            yield return null;

            Assert.IsFalse(GameManager.Instance.Services.TryGet(out IRaidInfo _));
            Assert.AreEqual(diedBefore, Events.SubscriberCount<PlayerDied>());
            Assert.AreEqual(0, Events.SubscriberCount<RaidAbandonRequested>());
        }
    }
}
#endif
