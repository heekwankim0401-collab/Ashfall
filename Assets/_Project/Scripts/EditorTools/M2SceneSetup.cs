using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.UI;
using Ashfall.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ashfall.EditorTools
{
    // M2 체력·인벤토리 확인 씬 생성 (M2 설계 §5.6, SYS-HP-01, SYS-INV, RULE-04, RULE-05).
    // - 데이터: M2DataSetup.CreateData()(M1·M2 데이터 모두, 이미 있으면 건너뜀)
    // - 공용 에셋: GamePanelSettings(UI), 터렛 구역 표시 재질 — 없을 때만 만든다
    // - 씬: 실행할 때마다 새로 만들어 덮어쓴다(씬은 이 메뉴의 산출물이라 손으로 고치지 않는다).
    // 빌드 설정에는 넣지 않는다(M1 그레이박스와 같음).
    // EventSystem은 넣지 않는다: Unity 6(2023.2+)의 UI Toolkit 런타임은 InputForUI + Input System의 InputSystemProvider로
    // 프로젝트 입력 액션의 UI 맵(Point/Click/...)을 직접 받는다(Input System 문서 understand-ui-compatibility.md: "UI Toolkit (2023.2+) — UI Input Module Not required").
    // 배치모드: -executeMethod Ashfall.EditorTools.M2SceneSetup.CreateAll
    public static class M2SceneSetup
    {
        private const string Tag = "[M2SceneSetup]";

        public const string TestSceneFolder = "Assets/_Project/Scenes/Test";
        public const string ScenePath = TestSceneFolder + "/M2_HealthInventory.unity";

        public const string UiFolder = "Assets/_Project/Art/UI";
        public const string PanelSettingsPath = UiFolder + "/GamePanelSettings.asset";
        public const string ThemePath = UiFolder + "/AshfallTheme.tss";
        public const string GameUxmlPath = UiFolder + "/GameUI.uxml";
        public const string ZoneMaterialPath = "Assets/_Project/Art/Materials/M2_TurretZone.mat";

        private const string WeaponFolder = M2DataSetup.DataFolder + "/Weapons";
        private const string RiflePath = WeaponFolder + "/WPN-RIF-01.asset";
        private const string AmmoPath = WeaponFolder + "/AMMO-556.asset";
        private const string MagazinePath = WeaponFolder + "/MAG-556-30.asset";

        // ---------- 그레이박스 배치 (씬 레이아웃 값, 게임 수치 아님) ----------
        // 원점 = 바닥 중앙, 바닥 윗면 y 0. 플레이어는 +z를 본다.
        private static readonly Vector3 FloorSize = new Vector3(60f, 1f, 60f);         // 설계 §5.6: 60m×60m
        private static readonly Vector3 PlayerStart = new Vector3(0f, 0.05f, -20f);
        private static readonly Vector3 TurretZoneCenter = new Vector3(15f, 0f, -20f);  // 시작점에서 15m 오른쪽 (설계 §5.6)
        private static readonly Vector3 TurretPosition = new Vector3(15f, 0f, -8f);     // 구역 중심에서 12m 앞
        private const float TurretHeightM = 1.5f;

        // TestTurret 값 (2a 메모: zoneRadiusM 4, intervalSec 2, aimHeightRangeM 0.3~1.75)
        private const float TurretZoneRadiusM = 4f;
        private const float TurretIntervalSec = 2f;
        private static readonly Vector2 TurretAimHeightRangeM = new Vector2(0.3f, 1.75f);

        // 루트 상자 그리드 (TODO(GDD-?): 상자 그리드 크기 GDD에 없음. 내용물이 모두 들어가는 크기)
        private static readonly Vector2Int LootGridSize = new Vector2Int(6, 4);

        // PanelSettings (2b 메모, TODO(GDD-?) 그레이박스 값)
        private static readonly Vector2Int ReferenceResolution = new Vector2Int(1920, 1080);
        private const float ScreenMatch = 0.5f;

        [MenuItem("Ashfall/Setup/Create M2 Test Scene")]
        public static void CreateAll()
        {
            M2DataSetup.CreateData();
            CreatePanelSettings();
            CreateZoneMaterial();
            AssetDatabase.SaveAssets();

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            CreateScene();
        }

        // ---------- 공용 에셋 ----------

        private static PanelSettings CreatePanelSettings()
        {
            return SetupUtil.CreateAssetIfMissing<PanelSettings>(PanelSettingsPath, ps =>
            {
                ps.themeStyleSheet = SetupUtil.Load<ThemeStyleSheet>(ThemePath);
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = ReferenceResolution;
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                ps.match = ScreenMatch;
            }, Tag);
        }

        // 터렛 구역 바닥 표시용 붉은 재질 (URP Lit). 없을 때만 만든다.
        private static Material CreateZoneMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(ZoneMaterialPath);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError($"{Tag} URP Lit 셰이더를 찾지 못했다.");
                return null;
            }
            SetupUtil.EnsureFolder(System.IO.Path.GetDirectoryName(ZoneMaterialPath).Replace('\\', '/'));
            var material = new Material(shader) { name = "M2_TurretZone" };
            var red = new Color(0.8f, 0.1f, 0.08f); // 그레이박스 표시색
            material.SetColor("_BaseColor", red);
            material.color = red;
            AssetDatabase.CreateAsset(material, ZoneMaterialPath);
            Debug.Log($"{Tag} 에셋 생성: {ZoneMaterialPath}");
            return material;
        }

        // ---------- 씬 ----------

        private static void CreateScene()
        {
            SetupUtil.EnsureFolder(TestSceneFolder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            // 에셋은 NewScene 뒤에 불러온다. NewScene(Single)이 사용되지 않는 에셋을 언로드해서
            // 그 전에 잡아 둔 참조(PanelSettings·재질)는 끊긴 객체가 되고 씬에 {fileID: 0}/기본 재질로 저장된다.
            var panelSettings = SetupUtil.Load<PanelSettings>(PanelSettingsPath);
            var zoneMaterial = SetupUtil.Load<Material>(ZoneMaterialPath);
            Object.DestroyImmediate(GameObject.FindWithTag("MainCamera")); // 플레이어 카메라를 쓴다

            CreateBlock("Floor_60m", new Vector3(0f, -FloorSize.y * 0.5f, 0f), FloorSize);
            var projectiles = new GameObject(nameof(ProjectileSystem)).AddComponent<ProjectileSystem>();
            var player = CreatePlayer(PlayerStart, projectiles);

            CreateLootContainers();
            CreateWorldItems();
            CreateTurret(player.transform, projectiles, zoneMaterial);

            // 사격 확인용 표적 (플레이어 정면 10/25/45m)
            foreach (int distance in new[] { 10, 25, 45 })
                CreateTarget($"Target_{distance}m", PlayerStart + new Vector3(0f, -PlayerStart.y, distance));

            CreateGameUI(panelSettings);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"{Tag} 씬 생성(덮어씀): {ScenePath}");
        }

        private static GameObject CreatePlayer(Vector3 position, ProjectileSystem projectiles)
        {
            var config = SetupUtil.Load<PlayerConfigSO>(M2DataSetup.PlayerConfigPath);

            var player = new GameObject("Player");
            player.transform.position = position;
            var controller = player.AddComponent<CharacterController>();
            controller.radius = 0.3f; // M1과 같은 그레이박스 값
            // 엔진 기본 minMoveDistance(1mm)는 고fps + 느린 이동(엎드림 + 무게 패널티)에서 프레임당 이동을 버려 멈추게 한다.
            controller.minMoveDistance = 0f;
            var motor = player.AddComponent<PlayerMotor>();
            var health = player.AddComponent<PlayerHealth>();

            var eye = new GameObject("Eye").transform;
            eye.SetParent(player.transform, false);
            eye.localPosition = new Vector3(0f, config.standHeight - config.eyeBelowTop, 0f);

            var cameraGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            cameraGo.transform.SetParent(eye, false);
            cameraGo.GetComponent<Camera>().nearClipPlane = 0.05f;

            SetupUtil.SetFields(motor, ("config", config), ("eye", eye));
            SetupUtil.SetFields(health, ("config", config));

            CreateWeapon(cameraGo.transform, projectiles);
            CreateInventory(player, cameraGo.transform);
            return player;
        }

        // M1과 같은 뷰모델 + handling(WeaponHandlingConfig). 장착 무기·탄창은 인벤토리(Primary1)에서 온다.
        private static void CreateWeapon(Transform camera, ProjectileSystem projectiles)
        {
            var weaponRoot = new GameObject("Weapon_WPN-RIF-01").transform;
            weaponRoot.SetParent(camera, false);
            var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            model.name = "ViewModel";
            Object.DestroyImmediate(model.GetComponent<Collider>());
            model.transform.SetParent(weaponRoot, false);
            model.transform.localScale = new Vector3(0.06f, 0.08f, 0.6f);
            var hip = new Vector3(0.2f, -0.18f, 0.45f);
            var ads = new Vector3(0f, -0.09f, 0.35f);
            model.transform.localPosition = hip;
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(model.transform, false);
            muzzle.localPosition = new Vector3(0f, 0f, 0.5f);

            var weapon = weaponRoot.gameObject.AddComponent<WeaponController>();
            SetupUtil.SetFields(weapon,
                ("weapon", SetupUtil.Load<WeaponDefinitionSO>(RiflePath)),
                ("ammo", SetupUtil.Load<AmmoDefinitionSO>(AmmoPath)),
                ("magazine", SetupUtil.Load<MagazineDefinitionSO>(MagazinePath)),
                ("handling", SetupUtil.Load<WeaponHandlingConfigSO>(M2DataSetup.WeaponHandlingConfigPath)),
                ("muzzle", muzzle), ("viewModel", model.transform), ("projectiles", projectiles));
            var so = new SerializedObject(weapon);
            // 인벤토리가 있으면 쓰지 않는다. 인벤토리를 빼고 시험할 때 M1과 같은 지급이 되게 둔다.
            so.FindProperty("startingMagazines").intValue = 4;
            so.FindProperty("hipLocalPosition").vector3Value = hip;
            so.FindProperty("adsLocalPosition").vector3Value = ads;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 시작 장비 (설계 §5.6). 순서가 중요하다: 조끼·배낭을 먼저 장착해야 뒤의 아이템이 들어간다.
        // 자동 배치(조끼 → 주머니 → 배낭): 탄창 3 + 붕대·지혈대 = 조끼(4×2), 부목(1×2) = 배낭.
        private static void CreateInventory(GameObject player, Transform camera)
        {
            var inventory = player.AddComponent<PlayerInventoryBehaviour>();
            SetupUtil.SetFields(inventory,
                ("config", SetupUtil.Load<InventoryConfigSO>(M2DataSetup.InventoryConfigPath)),
                ("viewOrigin", camera));

            var magazine = SetupUtil.Load<MagazineDefinitionSO>(MagazinePath);
            var entries = new (string path, bool equip, EquipSlot slot, MagazineDefinitionSO loaded, int quick)[]
            {
                (RiflePath, true, EquipSlot.Primary1, magazine, -1),
                ($"{M2DataSetup.GearFolder}/RIG-01.asset", true, EquipSlot.TacticalVest, null, -1),
                ($"{M2DataSetup.GearFolder}/BAG-01.asset", true, EquipSlot.Backpack, null, -1),
                (MagazinePath, false, default, null, -1),
                (MagazinePath, false, default, null, -1),
                (MagazinePath, false, default, null, -1),
                ($"{M2DataSetup.MedicalFolder}/MED-BND-01.asset", false, default, null, 0), // 키 4
                ($"{M2DataSetup.MedicalFolder}/MED-TRQ-01.asset", false, default, null, 1), // 키 5
                ($"{M2DataSetup.MedicalFolder}/MED-SPL-01.asset", false, default, null, 2), // 키 6
            };

            var so = new SerializedObject(inventory);
            SerializedProperty array = so.FindProperty("startingItems");
            array.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("item").objectReferenceValue = SetupUtil.Load<ItemDefinitionSO>(e.path);
                element.FindPropertyRelative("equip").boolValue = e.equip;
                element.FindPropertyRelative("slot").intValue = (int)e.slot;
                element.FindPropertyRelative("loadedMagazine").objectReferenceValue = e.loaded;
                element.FindPropertyRelative("quickSlot").intValue = e.quick;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 루트 상자 3개 (설계 §5.6): A 2개(1초), B 6개(3초), C 엔진 부품 2개(무게 패널티 확인)
        private static void CreateLootContainers()
        {
            string medical = M2DataSetup.MedicalFolder, junk = M2DataSetup.JunkFolder;
            CreateLootContainer("LootBox_A", "상자 A", PlayerStart + new Vector3(-4f, 0f, 3f),
                $"{M2DataSetup.ArmorFolder}/ARM-HLM-01.asset", $"{junk}/JNK-03.asset");
            CreateLootContainer("LootBox_B", "상자 B", PlayerStart + new Vector3(-4f, 0f, 6f),
                $"{medical}/MED-BND-01.asset", $"{medical}/MED-TRQ-01.asset", $"{medical}/MED-SPL-01.asset",
                $"{junk}/JNK-01.asset", MagazinePath, $"{junk}/JNK-03.asset");
            CreateLootContainer("LootBox_C", "상자 C", PlayerStart + new Vector3(-4f, 0f, 9f),
                $"{junk}/JNK-02.asset", $"{junk}/JNK-02.asset");
        }

        private static void CreateLootContainer(string name, string displayName, Vector3 groundPosition, params string[] contentPaths)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = groundPosition + new Vector3(0f, 0.5f, 0f); // 1m 큐브
            var container = box.AddComponent<LootContainer>();

            var so = new SerializedObject(container);
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("gridSize").vector2IntValue = LootGridSize;
            SerializedProperty contents = so.FindProperty("contents");
            contents.arraySize = contentPaths.Length;
            for (int i = 0; i < contentPaths.Length; i++)
                contents.GetArrayElementAtIndex(i).objectReferenceValue = SetupUtil.Load<ItemDefinitionSO>(contentPaths[i]);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 바닥 아이템 (설계 §5.6): 방탄복, 탄창. 크기 = 칸 수 × worldItemCellM (WorldItem.Spawn과 같은 표현)
        private static void CreateWorldItems()
        {
            var config = SetupUtil.Load<InventoryConfigSO>(M2DataSetup.InventoryConfigPath);
            CreateWorldItem(SetupUtil.Load<ItemDefinitionSO>($"{M2DataSetup.ArmorFolder}/ARM-BDY-01.asset"),
                PlayerStart + new Vector3(3f, 0f, 3f), config);
            CreateWorldItem(SetupUtil.Load<ItemDefinitionSO>(MagazinePath),
                PlayerStart + new Vector3(3f, 0f, 5f), config);
        }

        private static void CreateWorldItem(ItemDefinitionSO definition, Vector3 groundPosition, InventoryConfigSO config)
        {
            if (definition == null || config == null)
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"WorldItem_{definition.id}";
            float cell = config.worldItemCellM;
            go.transform.position = groundPosition + new Vector3(0f, cell * 0.5f, 0f);
            go.transform.localScale = new Vector3(definition.size.x * cell, cell, definition.size.y * cell);
            go.AddComponent<Rigidbody>();
            var worldItem = go.AddComponent<WorldItem>();
            SetupUtil.SetFields(worldItem, ("definition", definition));
        }

        // 피격 확인용 터렛 (설계 §5.6, 2a 메모). 붉은 바닥 구역 안에 서 있으면 2초마다 5.56 1발.
        private static void CreateTurret(Transform target, ProjectileSystem projectiles, Material zoneMaterial)
        {
            // 구역 표시: 반경 4m를 덮는 붉은 사각형(충돌체 없음)
            var zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            zone.name = "TurretZone";
            Object.DestroyImmediate(zone.GetComponent<Collider>());
            zone.transform.position = TurretZoneCenter + new Vector3(0f, 0.01f, 0f);
            zone.transform.localScale = new Vector3(TurretZoneRadiusM * 2f, 0.02f, TurretZoneRadiusM * 2f);
            if (zoneMaterial != null)
                zone.GetComponent<MeshRenderer>().sharedMaterial = zoneMaterial;

            var turret = new GameObject("TestTurret");
            turret.transform.position = TurretPosition;
            Vector3 toZone = TurretZoneCenter - TurretPosition;
            toZone.y = 0f;
            turret.transform.rotation = Quaternion.LookRotation(toZone.normalized, Vector3.up);

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pole.name = "Pole";
            pole.transform.SetParent(turret.transform, false);
            pole.transform.localPosition = new Vector3(0f, TurretHeightM * 0.5f, 0f);
            pole.transform.localScale = new Vector3(0.2f, TurretHeightM, 0.2f);

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            head.transform.SetParent(turret.transform, false);
            head.transform.localPosition = new Vector3(0f, TurretHeightM, 0f);
            head.transform.localScale = new Vector3(0.4f, 0.3f, 0.6f);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(turret.transform, false);
            muzzle.localPosition = new Vector3(0f, TurretHeightM, 0.5f); // 머리 앞

            var component = turret.AddComponent<TestTurret>();
            SetupUtil.SetFields(component,
                ("projectiles", projectiles),
                ("ammo", SetupUtil.Load<AmmoDefinitionSO>(AmmoPath)),
                ("target", target),
                ("zoneCenter", zone.transform),
                ("muzzle", muzzle));
            var so = new SerializedObject(component);
            so.FindProperty("zoneRadiusM").floatValue = TurretZoneRadiusM;
            so.FindProperty("intervalSec").floatValue = TurretIntervalSec;
            so.FindProperty("aimHeightRangeM").vector2Value = TurretAimHeightRangeM;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateTarget(string name, Vector3 groundPosition)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = name;
            target.transform.position = groundPosition + new Vector3(0f, 1f, 0f);
            target.transform.localScale = new Vector3(1f, 2f, 0.2f);
            var hit = target.AddComponent<HitTarget>();
            var so = new SerializedObject(hit);
            so.FindProperty("flashSec").floatValue = 0.15f; // M1과 같은 그레이박스 값
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateBlock(string name, Vector3 center, Vector3 size)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.position = center;
            block.transform.localScale = size;
        }

        // UI (2b 메모): UIDocument(GameUI.uxml + GamePanelSettings) + GameUIRoot(config = HudConfig)
        private static void CreateGameUI(PanelSettings panelSettings)
        {
            var go = new GameObject("GameUI");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = SetupUtil.Load<VisualTreeAsset>(GameUxmlPath);
            var root = go.AddComponent<GameUIRoot>();
            SetupUtil.SetFields(root, ("config", SetupUtil.Load<HudConfigSO>(M2DataSetup.HudConfigPath)));
        }
    }
}
