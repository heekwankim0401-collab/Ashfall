using System;
using System.Text;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.UI;
using Ashfall.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using L = Ashfall.EditorTools.CrimsonBasinLayout;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // M4 레이드 루프 씬 생성 (M4 설계 §1.3 씬 흐름, §4.6 "3. 통합", §15 M4).
    // 1) 데이터: M4DataSetup.CreateData() (M1~M4, 이미 있으면 건너뜀)
    // 2) 공용 에셋(NewScene 전): GamePanelSettings, 탈출 구역 재질, 적 색 재질 — 없을 때만
    // 3) Raid_CrimsonBasin 씬: 맵(M4MapSetup: 지형·블록아웃·엄폐·상자·경계·NavMesh·RaidController·탈출·스폰·스위치·POI)
    //    → ProjectileSystem → AiDirector + 적 20명(M4EnemySetup) → 플레이어(M2 CreatePlayer + 지도 아이템, Q11·Q13) → GameUI
    // 4) MainMenu 씬을 M4 UI(UIDocument MainMenu.uxml + MainMenuRoot)로 다시 만든다. Boot → MainMenu 흐름(BootLoader)은 그대로.
    // 5) 빌드 설정: SetupUtil.ApplyBuildScenes() — Boot → MainMenu → Hideout(M5, 있으면) → Raid_CrimsonBasin
    // 씬·Terrain·NavMesh는 실행할 때마다 덮어쓴다(.meta GUID 유지: 씬은 같은 경로에 저장, Terrain은 SetHeights, NavMesh는 CopySerialized).
    // 씬은 이 메뉴의 산출물이라 손으로 고치지 않는다(M6에서 손 편집을 시작하면 이 메뉴 재실행을 멈춘다).
    // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.M4SceneSetup.CreateAll -logFile setup.log
    // 확인(경로·적 배치)에 실패하면 씬은 저장하되 마지막에 예외를 던져 배치모드 종료 코드가 0이 아니게 한다.
    public static class M4SceneSetup
    {
        private const string Tag = "[M4SceneSetup]";

        public const string SceneFolder = "Assets/_Project/Scenes";
        public const string BootScenePath = SceneFolder + "/" + SceneNames.Boot + ".unity";
        public const string MainMenuScenePath = SceneFolder + "/" + SceneNames.MainMenu + ".unity";
        public const string RaidScenePath = SceneFolder + "/" + SceneNames.RaidCrimsonBasin + ".unity";
        public const string MainMenuUxmlPath = M2SceneSetup.UiFolder + "/MainMenu.uxml";

        // 플레이어 생성 위치: 첫 스폰(RaidController가 Start에서 무작위 스폰으로 PlaceAt 한다). 지면 위 여유(M2 PlayerStart와 같음)
        private const float PlayerStartLiftM = 0.05f;

        // 메인 메뉴 배경 (M4 그레이박스 기본값. M6 에셋이 있으면 저장 전 M6SceneSetup.ApplyMainMenuIfAssetsExist가 Ash_Black으로 바꾼다)
        private static readonly Color MenuBackground = Color.black;

        [MenuItem("Ashfall/Setup/Create M4 Raid Scenes")]
        public static void CreateAll()
        {
            M4DataSetup.CreateData();
            M2SceneSetup.CreatePanelSettings();
            M4MapSetup.EnsureZoneMaterial();
            M4EnemySetup.EnsureEnemyMaterials();
            AssetDatabase.SaveAssets();

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var problems = new StringBuilder();
            if (!CreateRaidScene(problems))
                throw new InvalidOperationException($"{Tag} 레이드 씬 생성 실패\n{problems}");
            CreateMainMenuScene();
            SetBuildScenes();
            AssetDatabase.SaveAssets();

            if (problems.Length > 0)
                throw new InvalidOperationException($"{Tag} 씬은 저장했지만 확인 실패\n{problems}");
            Debug.Log($"{Tag} 완료: {RaidScenePath}, {MainMenuScenePath}. 빌드 설정: {SetupUtil.DescribeBuildScenes()}");
        }

        // ---------- Raid_CrimsonBasin ----------

        // 저장까지 했으면 true. 확인 실패는 problems에 쌓는다.
        private static bool CreateRaidScene(StringBuilder problems)
        {
            SetupUtil.EnsureFolder(SceneFolder);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Object.DestroyImmediate(GameObject.FindWithTag("MainCamera")); // 플레이어 카메라를 쓴다. Directional Light는 남긴다(M6 ApplyRaid가 "Sun"으로 이름·색·방향을 정한다)

            // 에셋은 NewScene 뒤에 불러온다 (M2 함정: NewScene(Single)이 쓰지 않는 에셋을 언로드한다)
            M4MapSetup.MapAssets assets = M4MapSetup.MapAssets.LoadDefaults();
            M4MapSetup.MapBuildResult map = M4MapSetup.BuildMap(assets, M4MapSetup.DefaultBake);
            if (!map.Success)
            {
                problems.AppendLine("맵 생성(NavMesh 굽기) 실패 — 씬을 저장하지 않았다.");
                return false;
            }

            var projectiles = new GameObject(nameof(ProjectileSystem)).AddComponent<ProjectileSystem>();
            AiDirector director = M4EnemySetup.CreateDirector();
            M4EnemySetup.EnemyPlacementResult enemies = M4EnemySetup.PlaceEnemies(map.Terrain, director);

            GameObject player = CreatePlayer(map.Terrain, projectiles);
            M2SceneSetup.CreateGameUI(SetupUtil.Load<PanelSettings>(M2SceneSetup.PanelSettingsPath));

            if (!M4MapSetup.ValidateNavigation(assets.RaidConfig, out string navReport))
                problems.AppendLine("경로 확인 실패:").Append(navReport);
            if (!M4EnemySetup.ValidateEnemyStarts(enemies, out string enemyReport))
                problems.AppendLine("적 배치 확인 실패:").Append(enemyReport);
            if (player.GetComponent<PlayerInventoryBehaviour>() == null)
                problems.AppendLine("플레이어 인벤토리 없음");

            M6SceneSetup.ApplyRaidIfAssetsExist(scene); // M6 §1.6 Q1: 다시 만들어도 M6 아트·오디오·재 폭풍 유지
            M7SceneSetup.ApplyRaidIfAssetsExist(scene); // M7 §1.7: 다시 만들어도 M7 씬 구성 유지 (7B 적·7C 방사능)
            if (!EditorSceneManager.SaveScene(scene, RaidScenePath))
            {
                problems.AppendLine($"씬 저장 실패: {RaidScenePath}");
                return false;
            }
            Debug.Log($"{Tag} 씬 생성(덮어씀): {RaidScenePath} — 도형 {map.BlockCount}, 엄폐 {map.CoverPointCount}, 상자 {map.CrateCount}, " +
                      $"적 {enemies.Enemies.Count}, NavMesh 굽기 {map.BakeSeconds:0.0}s ({map.NavMeshBytes} B), Terrain {map.TerrainBytes} B");
            return true;
        }

        // M2와 같은 플레이어(구성·시작 장비) + 지도 아이템 (TODO(GDD-?) Q11·Q13: 고정 키트 = M2 시작 장비 + 지도).
        // 위치는 첫 스폰. 레이드 시작 시 RaidController가 무작위 스폰으로 옮긴다(IPlayerPlacement).
        private static GameObject CreatePlayer(Terrain terrain, ProjectileSystem projectiles)
        {
            L.Spawn first = L.Spawns[0];
            Vector3 start = M4MapSetup.GroundPoint(terrain, first.PositionXZ) + Vector3.up * PlayerStartLiftM;
            GameObject player = M2SceneSetup.CreatePlayer(start, projectiles);
            player.transform.rotation = Quaternion.Euler(0f, first.YawDeg, 0f);

            var inventory = player.GetComponent<PlayerInventoryBehaviour>();
            var map = SetupUtil.Load<ItemDefinitionSO>(M4DataSetup.MapItemPath);
            if (inventory == null || map == null)
                return player;

            var so = new SerializedObject(inventory);
            SerializedProperty array = so.FindProperty("startingItems");
            int index = array.arraySize;
            array.arraySize = index + 1; // 새 원소는 앞 원소 값을 복사하므로 모든 필드를 다시 쓴다
            SerializedProperty element = array.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("item").objectReferenceValue = map;
            element.FindPropertyRelative("equip").boolValue = false;
            element.FindPropertyRelative("slot").intValue = 0;
            element.FindPropertyRelative("loadedMagazine").objectReferenceValue = null;
            element.FindPropertyRelative("quickSlot").intValue = -1;
            so.ApplyModifiedPropertiesWithoutUndo();
            return player;
        }

        // ---------- MainMenu ----------

        // M0 SceneSetup은 "있으면 건너뜀"이라 그대로 두고 같은 경로에 덮어쓴다(.meta GUID·빌드 설정 GUID 유지).
        // 구성(2b 메모): 카메라 1개(배경) + MainMenuUI(UIDocument + MainMenuRoot). EventSystem 없음(M2와 같은 방식).
        private static void CreateMainMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                Object.DestroyImmediate(light.gameObject);
            GameObject cameraGo = GameObject.FindWithTag("MainCamera");
            if (cameraGo != null)
            {
                var camera = cameraGo.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = MenuBackground;
            }

            var ui = new GameObject("MainMenuUI");
            var document = ui.AddComponent<UIDocument>();
            document.panelSettings = SetupUtil.Load<PanelSettings>(M2SceneSetup.PanelSettingsPath);
            document.visualTreeAsset = SetupUtil.Load<VisualTreeAsset>(MainMenuUxmlPath);
            ui.AddComponent<MainMenuRoot>();

            M6SceneSetup.ApplyMainMenuIfAssetsExist(scene); // M6 §1.6 Q1: 배경 Ash_Black
            if (!EditorSceneManager.SaveScene(scene, MainMenuScenePath))
                throw new InvalidOperationException($"{Tag} 씬 저장 실패: {MainMenuScenePath}");
            Debug.Log($"{Tag} 씬 생성(덮어씀): {MainMenuScenePath}");
        }

        // ---------- 빌드 설정 ----------

        // M5: 공용 SetupUtil.ApplyBuildScenes() — Boot → MainMenu → Hideout(있으면) → Raid_CrimsonBasin.
        // 이 메뉴를 다시 돌려도 M5 Hideout 씬이 빌드 설정에서 빠지지 않는다 (M5 설계 §4.6).
        private static void SetBuildScenes() => SetupUtil.ApplyBuildScenes();
    }
}
