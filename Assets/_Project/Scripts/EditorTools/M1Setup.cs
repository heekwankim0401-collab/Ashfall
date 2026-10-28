using Ashfall.Core;
using Ashfall.Player;
using Ashfall.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // M1: 데이터 에셋(PlayerConfig, WPN-RIF-01, 5.56 탄·탄창)과 200m×200m 테스트 지형·그레이박스 씬을 만든다.
    // 이미 있는 에셋·씬은 덮어쓰지 않는다(조정한 값 보존). 배치모드: -executeMethod Ashfall.EditorTools.M1Setup.CreateAll
    public static class M1Setup
    {
        private const string DataFolder = "Assets/_Project/Data";
        private const string TestSceneFolder = "Assets/_Project/Scenes/Test";
        private const string ScenePath = TestSceneFolder + "/M1_Greybox.unity";
        private const string TerrainPath = TestSceneFolder + "/M1_TestTerrain.asset";

        [MenuItem("Ashfall/Setup/Create M1 Data")]
        public static void CreateData()
        {
            CreatePlayerConfig();
            CreateWeaponData();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Ashfall/Setup/Create M1 Greybox Scene")]
        public static void CreateAll()
        {
            CreateData();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                Debug.Log($"[M1Setup] 이미 있음, 건너뜀: {ScenePath}");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            CreateGreyboxScene();
        }

        // 생성 코드가 바뀌었을 때 기존 씬을 같은 경로(같은 GUID)로 덮어쓴다.
        [MenuItem("Ashfall/Setup/Recreate M1 Greybox Scene")]
        public static void RecreateAll()
        {
            CreateData();
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            CreateGreyboxScene();
        }

        // ---------- 데이터 (§4.1, §5.2 표 값) ----------

        private static void CreatePlayerConfig()
        {
            CreateAssetIfMissing<PlayerConfigSO>($"{DataFolder}/Player/PlayerConfig.asset", c =>
            {
                c.walkSpeed = 3.0f; c.sprintSpeed = 5.5f; c.crouchSpeed = 1.6f; c.proneSpeed = 0.8f;
                c.walkNoise = 8f; c.sprintNoise = 20f; c.crouchNoise = 3f; c.proneNoise = 1f; c.jumpNoise = 15f;
                c.jumpHeightM = 1.0f; c.jumpStaminaCost = 20f; c.leanAngleDeg = 15f;
                c.staminaMax = 100f; c.sprintStaminaPerSec = 15f;
                c.staminaRegenMovingPerSec = 10f; c.staminaRegenStationaryPerSec = 20f; c.exhaustedSprintLockSec = 3f;
                c.weightPenaltyStartKg = 25f; c.weightPenaltyPerKg = 0.02f; c.sprintMaxWeightKg = 40f;

                // TODO(GDD-?): 아래는 GDD에 없는 임시값.
                c.standHeight = 1.8f; c.crouchHeight = 1.2f; c.proneHeight = 0.6f;
                c.eyeBelowTop = 0.12f; c.stanceChangeSpeed = 3f;
                c.lookSensitivity = 0.1f; c.pitchLimitDeg = 85f;
                c.leanOffsetM = 0.3f; c.leanSpeed = 6f;
                c.groundStickSpeed = -2f;
            });
        }

        private static void CreateWeaponData()
        {
            var ammo = CreateAssetIfMissing<AmmoDefinitionSO>($"{DataFolder}/Weapons/AMMO-556.asset", a =>
            {
                a.id = "AMMO-556"; a.displayName = "5.56 탄"; a.type = AmmoType.Rifle556;
                AdsBallisticsSetup.FillNewAmmo(a);   // AB-Q3
                a.damage = 45f; // §5.2 WPN-RIF-01 피해
                a.pelletCount = 1;
                // TODO(GDD-?): 관통력·초기 속도·최대 비행 시간·무게·가격은 GDD에 없는 임시값.
                a.penetration = 3; a.velocity = 900f; a.maxFlightTimeSec = 3f;
            });

            CreateAssetIfMissing<MagazineDefinitionSO>($"{DataFolder}/Weapons/MAG-556-30.asset", m =>
            {
                m.id = "MAG-556-30"; m.displayName = "5.56 탄창 (30발)"; m.ammo = AmmoType.Rifle556;
                m.capacity = 30; // §5.2
                m.size = new Vector2Int(1, 2); // TODO(GDD-?): 칸 크기 임시값
            });

            CreateAssetIfMissing<WeaponDefinitionSO>($"{DataFolder}/Weapons/WPN-RIF-01.asset", w =>
            {
                w.id = "WPN-RIF-01"; w.displayName = "군용 소총"; w.cls = WeaponClass.AssaultRifle;
                AdsBallisticsSetup.FillNewWeapon(w);   // 정조준·탄도 AB-Q1·Q2 (id만 쓴다)
                w.ammo = AmmoType.Rifle556; w.rpm = 650; w.modes = new[] { FireMode.Auto }; // §5.2
                // TODO(GDD-?): 반동·조준 시간·재장전 시간·지향 사격 퍼짐·크기·무게는 GDD에 없는 임시값.
                w.recoilV = 0.6f; w.recoilH = 0.25f; w.adsTime = 0.25f;
                w.reloadTimeSec = 2.5f; w.fastReloadTimeSec = 1.5f; w.hipSpreadDeg = 2f;
                w.size = new Vector2Int(4, 2);
            });

            if (ammo == null)
                Debug.LogError("[M1Setup] 탄약 에셋을 만들지 못했다.");
        }

        // ---------- 씬 ----------

        private static void CreateGreyboxScene()
        {
            EnsureFolder(TestSceneFolder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Object.DestroyImmediate(GameObject.FindWithTag("MainCamera")); // 플레이어 카메라를 쓴다

            CreateTerrain();
            var projectiles = new GameObject(nameof(ProjectileSystem)).AddComponent<ProjectileSystem>();
            CreatePlayer(new Vector3(0f, 0.1f, -80f), projectiles);

            // 플레이어 정면 25/50/100/150m 표적
            foreach (int distance in new[] { 25, 50, 100, 150 })
                CreateTarget($"Target_{distance}m", new Vector3(0f, 0f, -80f + distance));

            // 이동 확인용 장애물: 점프(0.9m 상자), 웅크리기(높이 1.3m 아래 통과), 엎드리기(높이 0.7m 아래 통과)
            CreateBlock("Jump_Box_0.9m", new Vector3(-6f, 0.45f, -70f), new Vector3(2f, 0.9f, 2f));
            CreateBlock("Crouch_Bar_1.3m", new Vector3(-12f, 1.55f, -70f), new Vector3(3f, 0.5f, 0.3f));
            CreateBlock("Prone_Bar_0.7m", new Vector3(-18f, 0.95f, -70f), new Vector3(3f, 0.5f, 0.3f));

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[M1Setup] 그레이박스 씬 생성: {ScenePath}");
        }

        private static void CreateTerrain()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
            if (data == null)
            {
                data = new TerrainData { heightmapResolution = 257, size = new Vector3(200f, 20f, 200f) };
                AssetDatabase.CreateAsset(data, TerrainPath);
            }
            var terrain = Terrain.CreateTerrainGameObject(data);
            terrain.name = "TestTerrain_200m";
            terrain.transform.position = new Vector3(-100f, 0f, -100f); // 원점이 지형 중앙
        }

        private static void CreatePlayer(Vector3 position, ProjectileSystem projectiles)
        {
            var config = AssetDatabase.LoadAssetAtPath<PlayerConfigSO>($"{DataFolder}/Player/PlayerConfig.asset");

            var player = new GameObject("Player");
            player.transform.position = position;
            var controller = player.AddComponent<CharacterController>();
            controller.radius = 0.3f;
            // 엔진 기본 minMoveDistance(1mm)는 고fps + 느린 이동에서 프레임당 이동을 버려 멈추게 한다.
            controller.minMoveDistance = 0f;
            var motor = player.AddComponent<PlayerMotor>();

            var eye = new GameObject("Eye").transform;
            eye.SetParent(player.transform, false);
            eye.localPosition = new Vector3(0f, config.standHeight - config.eyeBelowTop, 0f);

            var cameraGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            cameraGo.transform.SetParent(eye, false);
            cameraGo.GetComponent<Camera>().nearClipPlane = 0.05f;

            SetFields(motor, ("config", config), ("eye", eye));

            // 총: 카메라 아래 상자 하나 + 총구
            var weaponRoot = new GameObject("Weapon_WPN-RIF-01").transform;
            weaponRoot.SetParent(cameraGo.transform, false);
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
            SetFields(weapon,
                ("weapon", AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>($"{DataFolder}/Weapons/WPN-RIF-01.asset")),
                ("ammo", AssetDatabase.LoadAssetAtPath<AmmoDefinitionSO>($"{DataFolder}/Weapons/AMMO-556.asset")),
                ("magazine", AssetDatabase.LoadAssetAtPath<MagazineDefinitionSO>($"{DataFolder}/Weapons/MAG-556-30.asset")),
                ("muzzle", muzzle), ("viewModel", model.transform), ("projectiles", projectiles));
            var so = new SerializedObject(weapon);
            so.FindProperty("startingMagazines").intValue = 4;
            so.FindProperty("hipLocalPosition").vector3Value = hip;
            so.FindProperty("adsLocalPosition").vector3Value = ads;
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
            so.FindProperty("flashSec").floatValue = 0.15f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateBlock(string name, Vector3 center, Vector3 size)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.position = center;
            block.transform.localScale = size;
        }

        // ---------- 도우미 (SetupUtil로 이동, 동작 동일) ----------

        private static T CreateAssetIfMissing<T>(string path, System.Action<T> fill) where T : ScriptableObject
            => SetupUtil.CreateAssetIfMissing(path, fill, "[M1Setup]");

        private static void EnsureFolder(string folder) => SetupUtil.EnsureFolder(folder);

        private static void SetFields(Object target, params (string name, Object value)[] fields)
            => SetupUtil.SetFields(target, fields);
    }
}
