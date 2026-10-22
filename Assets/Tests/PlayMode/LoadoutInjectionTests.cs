#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M5 AC3 — 레이드 인벤토리의 반입 주입 (M5 설계 §4.3 PlayerInventoryBehaviour, §7 AC3 LoadoutInjectionTests, RULE-01).
    // ① 흐름 레이드 표가 없으면(확인 씬·직접 Play) 레이드 이름의 씬이라도 startingItems로 시작한다 — 코드로 만든 씬.
    // ② 흐름 레이드(GameFlow 공개 API: OnSceneLoaded(Hideout) → StartRaid(loadout))면 실제 Raid_CrimsonBasin의 플레이어가
    //    startingItems 대신 그 Loadout으로 시작한다 (위치·회전·탄 수·퀵슬롯, 같은 인스턴스).
    // 세이브는 OS 임시 폴더만 쓴다(M5 설계 §2.6 — 실제 persistentDataPath 금지). 끝나면 흐름 표를 비우고 MainMenu로 돌아간다.
    public class LoadoutInjectionTests
    {
        private const string InventoryConfigPath = "Assets/_Project/Data/Inventory/InventoryConfig.asset";
        private const float LoadTimeoutSec = 60f;

        private static readonly FieldInfo StartingItemsField =
            typeof(PlayerInventoryBehaviour).GetField("startingItems", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<string> tempDirs = new List<string>();
        private Scene codeScene;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                // 흐름 레이드 표를 비운다(다음 테스트의 레이드 씬이 이 반입을 받지 않게) + 메모리 프로필(raidInProgress)을 버린다.
                gm.Flow.TakeRaidReturn(out _);
                SaveSandbox.UseFreshStorage("loadout-injection");   // 3단계: 샌드박스 실행 폴더로 (끝나고 지워진다)
                gm.Flow.SetWorldPaused(false);
            }
            Time.timeScale = 1f;

            if (codeScene.IsValid() && codeScene.isLoaded && SceneManager.sceneCount > 1)
            {
                yield return SceneManager.UnloadSceneAsync(codeScene);
                // 코드 씬 이름이 레이드라 단계가 Raid로 바뀌었을 수 있다 → 남은 활성 씬 기준으로 맞춘다.
                if (gm != null)
                    gm.Flow.OnSceneLoaded(SceneManager.GetActiveScene().name);
            }
            codeScene = default;

            foreach (string dir in tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            tempDirs.Clear();
        }

        // ① 흐름 밖: 레이드 이름의 코드 씬이라도 startingItems
        [UnityTest]
        public IEnumerator NoFlowRaid_RaidNamedCodeScene_UsesStartingItems()
        {
            GameManager gm = GameManager.Instance;
            Assert.IsNotNull(gm, "GameManager 없음");
            Assert.IsFalse(gm.Flow.HasRaidInFlow, "앞 테스트가 흐름 레이드 표를 남겼다");
            Assert.IsFalse(gm.Flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out _));

            // 앞 테스트가 실제 레이드 씬을 남겼을 수 있다(같은 이름의 씬은 만들 수 없다) → MainMenu 하나로 정리한 뒤 만든다.
            yield return LoadMainMenu();
            codeScene = SceneManager.CreateScene(SceneNames.RaidCrimsonBasin);
            SceneManager.SetActiveScene(codeScene);
            DeactivateOtherScenes(codeScene);
            yield return null;

            var bandage = Load<ItemDefinitionSO>("Assets/_Project/Data/Items/Medical/MED-BND-01.asset");
            var wires = Load<ItemDefinitionSO>("Assets/_Project/Data/Items/Junk/JNK-03.asset");
            PlayerInventoryBehaviour behaviour = BuildPlayer(new[]
            {
                new PlayerInventoryBehaviour.StartingItem { item = wires, quickSlot = -1 },
                new PlayerInventoryBehaviour.StartingItem { item = bandage, quickSlot = 1 },
            });
            yield return null;

            Assert.AreEqual(SceneNames.RaidCrimsonBasin, behaviour.gameObject.scene.name);
            PlayerInventory inv = behaviour.Inventory;
            List<ItemInstance> items = Collect(inv);
            CollectionAssert.AreEqual(new[] { wires, bandage }, items.Select(i => i.Definition).ToArray(), "startingItems 그대로");
            Assert.IsNotNull(inv.GetQuickSlot(1));
            Assert.AreEqual(bandage, inv.GetQuickSlot(1).Definition);
            Assert.IsTrue(inv.AllowDrop, "레이드 인벤토리는 버리기 가능 (은신처만 끈다)");
        }

        // ② 흐름 레이드: 실제 레이드 씬의 플레이어가 Loadout으로 시작
        [UnityTest]
        public IEnumerator FlowRaid_RaidScene_StartsWithLoadout_IgnoresStartingItems()
        {
            GameManager gm = GameManager.Instance;
            Assert.IsNotNull(gm, "GameManager 없음");
            GameFlow flow = gm.Flow;
            Assert.IsFalse(flow.HasRaidInFlow, "앞 테스트가 흐름 레이드 표를 남겼다");

            // 세이브 샌드박스 → 새 프로필 → 은신처 단계 (모두 공개 API)
            string dir = NewTempDir();
            var storage = new FileSaveStorage(dir);
            gm.UseSaveStorage(storage);
            StringAssert.DoesNotStartWith(Path.GetFullPath(Application.persistentDataPath), Path.GetFullPath(storage.FilePath),
                "테스트가 실제 세이브 경로를 쓰면 안 된다");
            gm.Profiles.EnsureLoaded(new EmptyCatalog(), new ProfileDefaults(new Vector2Int(10, 30), 6000));
            Assert.IsTrue(gm.Profiles.IsLoaded);
            flow.OnSceneLoaded(SceneNames.Hideout);
            Assert.AreEqual(GamePhase.Hideout, flow.Phase);

            // 반입: 소총(장착 탄창 17발) Primary1, 조끼, 조끼 (1,0) 5.56 탄창 12발 회전, 주머니 2 붕대(퀵슬롯 1), 주머니 0 전선
            var rifleDef = Load<WeaponDefinitionSO>("Assets/_Project/Data/Weapons/WPN-RIF-01.asset");
            var magDef = Load<MagazineDefinitionSO>("Assets/_Project/Data/Weapons/MAG-556-30.asset");
            var rifle = new WeaponItem(rifleDef, new Magazine(magDef, 17));
            var vest = new ItemInstance(Load<ItemDefinitionSO>("Assets/_Project/Data/Items/Gear/RIG-01.asset"));
            var mag = new Magazine(magDef, 12);
            var bandage = new MedicalItem(Load<MedicalDefinitionSO>("Assets/_Project/Data/Items/Medical/MED-BND-01.asset"));
            var wires = new ItemInstance(Load<ItemDefinitionSO>("Assets/_Project/Data/Items/Junk/JNK-03.asset"));
            var magAt = ItemLocation.InGrid(new GridRef(GridKind.TacticalVest), new Vector2Int(1, 0), true);
            var bandageAt = ItemLocation.InGrid(new GridRef(GridKind.Pocket, 2), Vector2Int.zero, false);
            var wiresAt = ItemLocation.InGrid(new GridRef(GridKind.Pocket, 0), Vector2Int.zero, false);
            var loadout = new Loadout(new[]
            {
                new LoadoutEntry(rifle, ItemLocation.Equipped(EquipSlot.Primary1)),
                new LoadoutEntry(vest, ItemLocation.Equipped(EquipSlot.TacticalVest)),
                new LoadoutEntry(mag, magAt),
                new LoadoutEntry(bandage, bandageAt),
                new LoadoutEntry(wires, wiresAt),
            }, new ItemInstance[] { null, bandage, null, null });

            Assert.IsTrue(flow.StartRaid(loadout), "StartRaid 거부");
            Assert.IsTrue(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out Loadout given));
            Assert.AreSame(loadout, given);
            Assert.IsTrue(File.Exists(storage.FilePath), "레이드 진입 기록(W4)이 샌드박스에 쓰였다");
            Assert.IsTrue(JsonUtility.FromJson<SaveData>(File.ReadAllText(storage.FilePath)).raidInProgress, "raidInProgress = true");

            float deadline = Time.realtimeSinceStartup + LoadTimeoutSec;
            while (SceneManager.GetActiveScene().name != SceneNames.RaidCrimsonBasin || flow.Phase != GamePhase.Raid)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "레이드 씬 로드 시간 초과");
                yield return null;
            }
            for (int i = 0; i < 3; i++)
                yield return null;

            var behaviour = Object.FindFirstObjectByType<PlayerInventoryBehaviour>();
            Assert.IsNotNull(behaviour, "PlayerInventoryBehaviour 없음");
            var starting = (PlayerInventoryBehaviour.StartingItem[])StartingItemsField.GetValue(behaviour);
            Assert.IsNotNull(starting);
            Assert.IsNotEmpty(starting, "레이드 씬에는 startingItems가 있다 (그래도 쓰지 않아야 한다)");

            PlayerInventory inv = behaviour.Inventory;
            Assert.AreSame(rifle, inv.GetEquipped(EquipSlot.Primary1), "같은 인스턴스");
            Assert.AreEqual(17, ((WeaponItem)inv.GetEquipped(EquipSlot.Primary1)).LoadedMagazine.Rounds, "장착 탄창 탄 수");
            Assert.AreSame(vest, inv.GetEquipped(EquipSlot.TacticalVest));
            AssertAt(inv, mag, magAt, "탄창 (회전)");
            Assert.AreEqual(12, mag.Rounds);
            AssertAt(inv, bandage, bandageAt, "붕대");
            AssertAt(inv, wires, wiresAt, "전선");
            Assert.AreSame(bandage, inv.GetQuickSlot(1), "퀵슬롯");
            Assert.IsNull(inv.GetQuickSlot(0));

            List<ItemInstance> all = Collect(inv);
            CollectionAssert.AreEquivalent(new ItemInstance[] { rifle, vest, mag, bandage, wires }, all,
                "소지품 = 반입 그대로 (startingItems 없음)");

            var controller = Object.FindFirstObjectByType<RaidController>();
            Assert.IsNotNull(controller, "RaidController 없음");
            yield return M3Scene.WaitUntil(() => controller.Session != null, 10f, () => "레이드 세션 시작");
            CollectionAssert.AreEqual(all.Select(i => i.Definition).ToArray(), controller.BroughtIn.ToArray(),
                "결과 화면 반입 = 주입한 장비");

            // 정리: 흐름 표 비우고 MainMenu로 (Hideout 씬 없이도 다음 테스트가 깨끗한 흐름에서 시작)
            Assert.AreEqual(RaidReturn.Interrupted, flow.TakeRaidReturn(out _));
            yield return LoadMainMenu();
            Assert.AreEqual(GamePhase.MainMenu, flow.Phase);
        }

        // ── 도우미 ──────────────────────────────────────────

        private string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "AshfallPlayModeSaves", "LoadoutInjection_" + Guid.NewGuid().ToString("N"));
            tempDirs.Add(dir);
            return dir;
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, path);
            return asset;
        }

        private static List<ItemInstance> Collect(IInventoryService inv)
        {
            var list = new List<ItemInstance>();
            InventorySnapshot.Collect(inv, list);
            return list;
        }

        private static void AssertAt(IInventoryService inv, ItemInstance item, ItemLocation expected, string what)
        {
            Assert.IsTrue(inv.TryGetLocation(item, out ItemLocation at), what + " 없음");
            Assert.AreEqual(expected.Grid, at.Grid, what + " 그리드");
            Assert.AreEqual(expected.Cell, at.Cell, what + " 칸");
            Assert.AreEqual(expected.Rotated, at.Rotated, what + " 회전");
        }

        private static PlayerInventoryBehaviour BuildPlayer(PlayerInventoryBehaviour.StartingItem[] startingItems)
        {
            var player = new GameObject("Player");
            player.SetActive(false);
            var eye = new GameObject("Eye").transform;
            eye.SetParent(player.transform, false);
            var behaviour = player.AddComponent<PlayerInventoryBehaviour>();
            var so = new SerializedObject(behaviour);
            so.FindProperty("config").objectReferenceValue = Load<InventoryConfigSO>(InventoryConfigPath);
            so.FindProperty("viewOrigin").objectReferenceValue = eye;
            so.ApplyModifiedPropertiesWithoutUndo();
            StartingItemsField.SetValue(behaviour, startingItems);
            player.SetActive(true);   // Awake → TryGetRaidLoadout / startingItems
            return behaviour;
        }

        // 이전 테스트가 남긴 씬의 플레이어·UI가 서비스·이벤트를 쥐지 않게 끈다 (PlayerPlacementTests와 같은 방식, 러너는 그대로).
        private static void DeactivateOtherScenes(Scene keep)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene other = SceneManager.GetSceneAt(i);
                if (other == keep || !other.isLoaded)
                    continue;
                foreach (GameObject root in other.GetRootGameObjects())
                    if (root.activeSelf && !IsTestRunnerObject(root))
                        root.SetActive(false);
            }
        }

        private static bool IsTestRunnerObject(GameObject root) =>
            root.GetComponentsInChildren<MonoBehaviour>(true).Any(m =>
                m != null && m.GetType().Assembly.GetName().Name.Contains("TestRunner"));

        private static IEnumerator LoadMainMenu()
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(SceneNames.MainMenu, LoadSceneMode.Single);
            Assert.IsNotNull(op, "MainMenu 로드 실패 (빌드 설정 확인)");
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSec;
            while (!op.isDone)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "MainMenu 로드 시간 초과");
                yield return null;
            }
            yield return null;
        }

        // 새 프로필용 (파일이 없어 id를 찾지 않는다)
        private sealed class EmptyCatalog : IItemCatalog
        {
            public bool TryGet(string id, out ItemDefinitionSO definition)
            {
                definition = null;
                return false;
            }
        }
    }
}
#endif
