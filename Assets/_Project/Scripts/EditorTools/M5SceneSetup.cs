using System;
using Ashfall.Core;
using Ashfall.Hideout;
using Ashfall.Inventory;
using Ashfall.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // M5 은신처 씬 생성 (SYS-HUB, M5 설계 §1.2 씬 흐름, §4.6 "M5SceneSetup", §4.9 빌드 설정, §15 M5).
    // 1) 데이터: M5DataSetup.CreateData() (M1~M5 아이템·가격·HideoutConfig·TraderConfig·ItemDatabase)
    // 2) 공용 에셋: GamePanelSettings (없을 때만)
    // 3) Hideout 씬(메뉴형, 3D 공간 없음): 카메라 1개(검정 배경, 조명 없음)
    //    → "HideoutSystems": HideoutController(config·trader·itemDatabase, 실행 순서 -100) + HideoutInventoryBehaviour(InventoryConfig)
    //    → "HideoutUI": UIDocument(HideoutUI.uxml + GamePanelSettings) + HideoutRoot(hudConfig). EventSystem 없음(M2 방식)
    // 4) 빌드 설정: Boot → MainMenu → Hideout → Raid_CrimsonBasin (SetupUtil.ApplyBuildScenes)
    // 씬은 실행할 때마다 같은 경로에 덮어쓴다(.meta GUID 유지). 씬은 이 메뉴의 산출물이라 손으로 고치지 않는다.
    // MainMenu·Raid 씬은 다시 만들지 않는다(UXML 내용·코드만 바뀌었다 — 설계 §4.6).
    // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.M5SceneSetup.CreateAll -logFile setup.log
    public static class M5SceneSetup
    {
        private const string Tag = "[M5SceneSetup]";

        public const string HideoutScenePath = M4SceneSetup.SceneFolder + "/" + SceneNames.Hideout + ".unity";
        public const string HideoutUxmlPath = M2SceneSetup.UiFolder + "/HideoutUI.uxml";

        public const string SystemsObjectName = "HideoutSystems";
        public const string UiObjectName = "HideoutUI";

        // 은신처 배경 (M5 그레이박스 기본값. M6 에셋이 있으면 저장 전 M6SceneSetup.ApplyHideoutIfAssetsExist가 Ash_Black으로 바꾼다)
        private static readonly Color Background = Color.black;

        [MenuItem("Ashfall/Setup/Create M5 Hideout Scene")]
        public static void CreateAll()
        {
            M5DataSetup.CreateData();
            M2SceneSetup.CreatePanelSettings();
            AssetDatabase.SaveAssets();

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            CreateHideoutScene();
            SetupUtil.ApplyBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료: {HideoutScenePath}. 빌드 설정: {SetupUtil.DescribeBuildScenes()}");
        }

        // ---------- Hideout ----------

        private static void CreateHideoutScene()
        {
            SetupUtil.EnsureFolder(M4SceneSetup.SceneFolder);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                Object.DestroyImmediate(light.gameObject);
            GameObject cameraGo = GameObject.FindWithTag("MainCamera");
            if (cameraGo != null)
            {
                var camera = cameraGo.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Background;
            }

            // 에셋은 NewScene 뒤에 불러온다 (M2 함정: NewScene(Single)이 쓰지 않는 에셋을 언로드한다)
            var hideoutConfig = SetupUtil.Load<HideoutConfigSO>(M5DataSetup.HideoutConfigPath);
            var traderConfig = SetupUtil.Load<TraderConfigSO>(M5DataSetup.TraderConfigPath);
            var itemDatabase = SetupUtil.Load<ItemDatabaseSO>(M5DataSetup.ItemDatabasePath);
            var inventoryConfig = SetupUtil.Load<InventoryConfigSO>(M2DataSetup.InventoryConfigPath);
            var hudConfig = SetupUtil.Load<HudConfigSO>(M2DataSetup.HudConfigPath);
            var panelSettings = SetupUtil.Load<PanelSettings>(M2SceneSetup.PanelSettingsPath);
            var uxml = SetupUtil.Load<VisualTreeAsset>(HideoutUxmlPath);
            if (hideoutConfig == null || traderConfig == null || itemDatabase == null || inventoryConfig == null ||
                panelSettings == null || uxml == null)
                throw new InvalidOperationException($"{Tag} 필요한 에셋이 없다(위 오류 로그). 씬을 저장하지 않았다.");

            // 은신처 로직 브리지: HideoutController(-100)가 먼저 Awake해 프로필 로드·진입 저장(W1) → HideoutInventoryBehaviour가 장비를 놓는다
            var systems = new GameObject(SystemsObjectName);
            var controller = systems.AddComponent<HideoutController>();
            SetupUtil.SetFields(controller,
                ("config", hideoutConfig),
                ("trader", traderConfig),
                ("itemDatabase", itemDatabase));
            var inventory = systems.AddComponent<HideoutInventoryBehaviour>();
            SetupUtil.SetFields(inventory, ("config", inventoryConfig));

            // UI (2b 메모): UIDocument + HideoutRoot. hudConfig는 드래그 임계를 레이드 인벤토리와 맞춘다(선택)
            var ui = new GameObject(UiObjectName);
            var document = ui.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = uxml;
            var root = ui.AddComponent<HideoutRoot>();
            if (hudConfig != null)
                SetupUtil.SetFields(root, ("hudConfig", hudConfig));

            M6SceneSetup.ApplyHideoutIfAssetsExist(scene); // M6 §1.6 Q1: 배경 Ash_Black·은신처 음악 유지
            M7SceneSetup.ApplyHideoutIfAssetsExist(scene); // M7 §1.7: 다시 만들어도 제작대 연결 유지 (7C SYS-HUB)
            if (!EditorSceneManager.SaveScene(scene, HideoutScenePath))
                throw new InvalidOperationException($"{Tag} 씬 저장 실패: {HideoutScenePath}");
            Debug.Log($"{Tag} 씬 생성(덮어씀): {HideoutScenePath}");
        }
    }
}
