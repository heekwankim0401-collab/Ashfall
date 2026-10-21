using Ashfall.AI;
using Ashfall.Weapons;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Ashfall.EditorTools
{
    // M3 적 AI 확인 씬 2개 생성 (M3 설계 §8, §1.10, AI-SCV, AI-RDR).
    // - 데이터: M3DataSetup.CreateData()(M1·M2·M3 데이터, 이미 있으면 건너뜀)
    // - 공용 에셋: GamePanelSettings(M2), 적 몸 색 재질 2개 — 없을 때만 만든다
    // - 씬: 실행할 때마다 새로 만들어 덮어쓴다(.meta GUID 유지). 환경(Environment 루트)을 만든 뒤 NavMesh를 굽고
    //   NavMeshData는 씬 이름 폴더의 NavMesh-Environment.asset에 저장한다(있으면 CopySerialized로 덮어써 GUID 유지).
    // - 적·플레이어·UI·관리자는 최상위. 빌드 설정에는 넣지 않는다.
    // 배치모드: -executeMethod Ashfall.EditorTools.M3SceneSetup.CreateAll
    public static class M3SceneSetup
    {
        private const string Tag = "[M3SceneSetup]";

        public const string TestSceneFolder = M2SceneSetup.TestSceneFolder;
        public const string AiScenePath = TestSceneFolder + "/M3_AI.unity";
        public const string PerfScenePath = TestSceneFolder + "/M3_AIPerf.unity";
        public const string AiNavMeshPath = TestSceneFolder + "/M3_AI/NavMesh-Environment.asset";
        public const string PerfNavMeshPath = TestSceneFolder + "/M3_AIPerf/NavMesh-Environment.asset";

        public const string MaterialFolder = "Assets/_Project/Art/Materials";
        public const string ScvMaterialPath = MaterialFolder + "/M3_EnemySCV.mat";
        public const string RdrMaterialPath = MaterialFolder + "/M3_EnemyRDR.mat";

        // ---------- 그레이박스 배치 (씬 레이아웃 값, 게임 수치 아님. 설계 §8) ----------
        // 원점 = 바닥 중앙, 바닥 윗면 y 0.
        private const float AiFloorSizeM = 100f;
        // TODO(GDD-?): 설계 §8.2는 140m지만 오프셋 ±35에서 RDR(z 35)이 z 70 = 바닥 끝에 놓인다. 5m 여유를 둔 150m.
        private const float PerfFloorSizeM = 150f;
        private const float PerfQuadrantOffsetM = 35f;

        private static readonly Vector3 AiPlayerStart = new Vector3(0f, 0.05f, -40f);
        private static readonly Vector3 PerfPlayerStart = new Vector3(0f, 0.05f, -70f); // 중앙 남쪽, 가장 가까운 적(Q2 분대원) 약 28m

        private static readonly Vector3 LowWallSize = new Vector3(4f, 1.1f, 0.4f);
        private static readonly Vector3 HighWallSize = new Vector3(3f, 2.5f, 0.4f);
        private static readonly Vector3 VehicleSize = new Vector3(4f, 1.5f, 2f);
        // 엄폐 지점은 구조물 면에서 이만큼 떨어진다. TODO(GDD-?): 설계 "앞뒤 0.7m"를 면 기준으로 해석
        // (중심 기준이면 면에서 0.5m = NavMesh 굽기 반지름 경계라 지점이 NavMesh 밖에 걸린다).
        private const float CoverGapM = 0.7f;
        private const float HighPeekSideM = 1.9f; // 설계 §8.1 peekLocalOffset (±1.9, 0, 0): 벽 끝(1.5m) 밖

        private static readonly Vector2[] LowWalls =
        {
            new Vector2(-20f, -15f), new Vector2(-10f, 0f), new Vector2(10f, 0f), new Vector2(20f, -15f),
            new Vector2(-5f, 15f), new Vector2(5f, 15f), new Vector2(-15f, 28f), new Vector2(15f, 28f),
        };
        private static readonly Vector2[] HighWalls =
        {
            new Vector2(-25f, 10f), new Vector2(25f, 10f), new Vector2(0f, 30f), new Vector2(0f, -20f),
        };
        private static readonly Vector2[] Vehicles = { new Vector2(-30f, -25f), new Vector2(30f, -25f) };

        private static readonly Vector2[] RouteA =
            { new Vector2(-30f, -5f), new Vector2(-30f, 25f), new Vector2(-10f, 25f), new Vector2(-10f, -5f) };
        private static readonly Vector2[] RouteB =
            { new Vector2(15f, -5f), new Vector2(35f, -5f), new Vector2(35f, 20f), new Vector2(15f, 20f) };

        // 분대원 시작 위치: 리더 = 경로 첫 지점, 나머지는 뒤쪽 좌우 (로컬 x, z: 진행 방향 기준).
        // Squad.FormationSlot(formationSpacingM 2m 초기값)과 같은 자리라 시작하자마자 대형을 맞추러 움직이지 않는다.
        private static readonly Vector2[] SquadSlots = { Vector2.zero, new Vector2(-2f, -2f), new Vector2(2f, -2f) };
        private static readonly Vector2[] RaiderSpots = { new Vector2(-3f, 35f), new Vector2(3f, 35f) };

        // 적 오브젝트 (설계 §8, 2b 메모)
        private const float EnemyHeightM = 1.8f;
        private const float EnemyRadiusM = 0.35f;
        private static readonly Vector3 EyeLocal = new Vector3(0f, 1.6f, 0.1f);
        private static readonly Vector3 MuzzleLocal = new Vector3(0.2f, 1.35f, 0.5f);
        private static readonly Vector3 BodyLocal = new Vector3(0f, 0.9f, 0f);       // 캡슐 중심 피벗
        private static readonly Vector3 BodyScale = new Vector3(0.7f, 0.9f, 0.7f);   // 프리미티브 캡슐(2m×1m) → 1.8m×0.7m
        private static readonly Vector3 IndicatorLocal = new Vector3(0f, 2.15f, 0f);
        private const float IndicatorScale = 0.25f;

        // NavMeshAgent (설계 §1.10)
        private const float AgentAngularSpeed = 360f;
        private const float AgentAcceleration = 12f;
        private const float AgentStoppingDistance = 0.5f;
        private const int AvoidancePriorityMin = 40;
        private const int AvoidancePriorityRange = 21; // 40~60

        private static readonly Color ScvColor = new Color(0.45f, 0.3f, 0.15f);  // 갈색
        private static readonly Color RdrColor = new Color(0.22f, 0.22f, 0.24f); // 짙은 회색

        [MenuItem("Ashfall/Setup/Create M3 Test Scenes")]
        public static void CreateAll()
        {
            M3DataSetup.CreateData();
            M2SceneSetup.CreatePanelSettings();
            CreateColorMaterial(ScvMaterialPath, "M3_EnemySCV", ScvColor);
            CreateColorMaterial(RdrMaterialPath, "M3_EnemyRDR", RdrColor);
            AssetDatabase.SaveAssets();

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (!CreateAiScene())
                return;
            CreatePerfScene();
        }

        // ---------- 씬 ----------

        private sealed class SceneContext
        {
            public EnemyArchetypeSO Scv, Rdr;
            public Material ScvMaterial, RdrMaterial;
            public AiDirector Director;
            public int EnemyCount;
        }

        // §8.1 사람 확인 + 기능 PlayMode 테스트
        private static bool CreateAiScene()
        {
            var scene = NewScene();
            GameObject environment = new GameObject("Environment");
            CreateFloor(environment.transform, AiFloorSizeM);
            CreateStructures(environment.transform, Vector3.zero, "");
            if (!BakeNavMesh(environment, AiNavMeshPath))
                return false;

            var ctx = CreateManagers(out ProjectileSystem projectiles);
            CreatePlayerAndUi(AiPlayerStart, projectiles);

            // 분대 A: SCV 3명 (권총, 권총, SMG), 경로 A
            PatrolRoute routeA = CreateRoute("PatrolRoute_A", RouteA, Vector3.zero);
            CreateSquad(ctx, "A", ctx.Scv, 1, routeA, RouteA, Vector3.zero, new[] { 0, 0, 1 });
            // 분대 B: SCV 2명 (권총, SMG), 경로 B
            PatrolRoute routeB = CreateRoute("PatrolRoute_B", RouteB, Vector3.zero);
            CreateSquad(ctx, "B", ctx.Scv, 2, routeB, RouteB, Vector3.zero, new[] { 0, 1 });
            // 분대 C: RDR 2명 (소총), 경로 없음, 플레이어 쪽(-z)
            CreateRaiders(ctx, "C", 3, Vector3.zero);

            return Save(scene, AiScenePath);
        }

        // §8.2 성능: 구조물 2×2 반복, 적 20명 (SCV 4×3 + RDR 4×2)
        private static bool CreatePerfScene()
        {
            var scene = NewScene();
            GameObject environment = new GameObject("Environment");
            CreateFloor(environment.transform, PerfFloorSizeM);
            Vector3[] offsets = QuadrantOffsets();
            for (int q = 0; q < offsets.Length; q++)
                CreateStructures(environment.transform, offsets[q], $"Q{q + 1}_");
            if (!BakeNavMesh(environment, PerfNavMeshPath))
                return false;

            var ctx = CreateManagers(out ProjectileSystem projectiles);
            var so = new SerializedObject(projectiles);
            so.FindProperty("drawDebugTrails").boolValue = false; // 설계 §8.2 (2a 메모: private 직렬화 필드)
            so.ApplyModifiedPropertiesWithoutUndo();
            CreatePlayerAndUi(PerfPlayerStart, projectiles);

            for (int q = 0; q < offsets.Length; q++)
            {
                string squadName = $"Q{q + 1}";
                PatrolRoute route = CreateRoute($"PatrolRoute_{squadName}", RouteA, offsets[q]);
                CreateSquad(ctx, $"{squadName}S", ctx.Scv, q + 1, route, RouteA, offsets[q], new[] { 0, 0, 1 });
            }
            for (int q = 0; q < offsets.Length; q++)
                CreateRaiders(ctx, $"Q{q + 1}R", offsets.Length + q + 1, offsets[q]);

            return Save(scene, PerfScenePath);
        }

        private static Vector3[] QuadrantOffsets()
        {
            float o = PerfQuadrantOffsetM;
            return new[] { new Vector3(-o, 0f, -o), new Vector3(o, 0f, -o), new Vector3(-o, 0f, o), new Vector3(o, 0f, o) };
        }

        private static Scene NewScene()
        {
            SetupUtil.EnsureFolder(TestSceneFolder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Object.DestroyImmediate(GameObject.FindWithTag("MainCamera")); // 플레이어 카메라를 쓴다
            return scene;
        }

        private static bool Save(Scene scene, string path)
        {
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                Debug.LogError($"{Tag} 씬 저장 실패: {path}");
                return false;
            }
            Debug.Log($"{Tag} 씬 생성(덮어씀): {path}");
            return true;
        }

        // ---------- 환경 ----------

        private static void CreateFloor(Transform parent, float size)
        {
            GameObject floor = M2SceneSetup.CreateBlock($"Floor_{size:0}m", new Vector3(0f, -0.5f, 0f), new Vector3(size, 1f, size));
            floor.transform.SetParent(parent, true);
        }

        // §8.1 구조물 배치 (offset만큼 이동). 각 구조물 = 스케일 없는 루트 + 스케일한 블록 + CoverPoint 자식
        // (CoverPoint를 스케일한 오브젝트 아래 두면 peekLocalOffset이 함께 늘어난다).
        private static void CreateStructures(Transform parent, Vector3 offset, string prefix)
        {
            for (int i = 0; i < LowWalls.Length; i++)
            {
                Transform wall = CreateStructure(parent, $"{prefix}LowWall_{i + 1:00}", At(LowWalls[i], offset), LowWallSize);
                float d = LowWallSize.z * 0.5f + CoverGapM;
                CreateCoverPoint(wall, new Vector3(0f, 0f, -d), Vector3.forward, CoverHeight.Low, Vector3.zero);
                CreateCoverPoint(wall, new Vector3(0f, 0f, d), Vector3.back, CoverHeight.Low, Vector3.zero);
            }
            for (int i = 0; i < HighWalls.Length; i++)
            {
                Transform wall = CreateStructure(parent, $"{prefix}HighWall_{i + 1:00}", At(HighWalls[i], offset), HighWallSize);
                float d = HighWallSize.z * 0.5f + CoverGapM;
                // TODO(GDD-?): 설계는 (±1.9, 0, 0). 지점마다 하나라 두 지점 모두 로컬 +x(월드로는 서로 반대쪽 끝)로 둔다.
                var peek = new Vector3(HighPeekSideM, 0f, 0f);
                CreateCoverPoint(wall, new Vector3(0f, 0f, -d), Vector3.forward, CoverHeight.High, peek);
                CreateCoverPoint(wall, new Vector3(0f, 0f, d), Vector3.back, CoverHeight.High, peek);
            }
            for (int i = 0; i < Vehicles.Length; i++)
            {
                Transform vehicle = CreateStructure(parent, $"{prefix}Vehicle_{i + 1:00}", At(Vehicles[i], offset), VehicleSize);
                float dx = VehicleSize.x * 0.5f + CoverGapM;
                float dz = VehicleSize.z * 0.5f + CoverGapM;
                CreateCoverPoint(vehicle, new Vector3(-dx, 0f, 0f), Vector3.right, CoverHeight.Low, Vector3.zero);
                CreateCoverPoint(vehicle, new Vector3(dx, 0f, 0f), Vector3.left, CoverHeight.Low, Vector3.zero);
                CreateCoverPoint(vehicle, new Vector3(0f, 0f, -dz), Vector3.forward, CoverHeight.Low, Vector3.zero);
                CreateCoverPoint(vehicle, new Vector3(0f, 0f, dz), Vector3.back, CoverHeight.Low, Vector3.zero);
            }
        }

        private static Vector3 At(Vector2 xz, Vector3 offset) => new Vector3(xz.x, 0f, xz.y) + offset;

        private static Transform CreateStructure(Transform parent, string name, Vector3 groundPosition, Vector3 size)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = groundPosition;
            GameObject block = M2SceneSetup.CreateBlock("Block", groundPosition + new Vector3(0f, size.y * 0.5f, 0f), size);
            block.transform.SetParent(root, true);
            return root;
        }

        private static void CreateCoverPoint(Transform structure, Vector3 localPosition, Vector3 facing, CoverHeight height, Vector3 peek)
        {
            var go = new GameObject($"Cover_{height}_{structure.childCount}");
            go.transform.SetParent(structure, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.LookRotation(facing, Vector3.up); // forward = 구조물 쪽
            var point = go.AddComponent<CoverPoint>();
            var so = new SerializedObject(point);
            so.FindProperty("height").intValue = (int)height;
            so.FindProperty("peekLocalOffset").vector3Value = peek;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 설계 §1.10: Environment 자식의 물리 콜라이더로 굽고 NavMeshData를 씬 이름 폴더에 저장(GUID 유지).
        private static bool BakeNavMesh(GameObject environment, string assetPath)
        {
            var surface = environment.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            NavMeshData built = surface.navMeshData;
            if (built == null)
            {
                Debug.LogError($"{Tag} NavMesh 굽기 실패: {assetPath}");
                return false;
            }

            SetupUtil.EnsureFolder(System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath);
            if (existing == null)
            {
                built.name = "NavMesh-Environment";
                AssetDatabase.CreateAsset(built, assetPath);
            }
            else
            {
                // 지우고 다시 만들면 .meta GUID가 바뀐다 → 기존 에셋에 덮어쓴다.
                surface.RemoveData();
                EditorUtility.CopySerialized(built, existing);
                existing.name = "NavMesh-Environment";
                Object.DestroyImmediate(built);
                surface.navMeshData = existing;
                surface.AddData();
                EditorUtility.SetDirty(existing);
            }
            EditorUtility.SetDirty(surface);
            AssetDatabase.SaveAssets();

            // 확인: 저장된 에셋이 실제로 NavMesh를 담고 있는지 (CopySerialized 실패 감지, 설계 §1.10 "멈추고 보고")
            var saved = AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath);
            if (saved == null || surface.navMeshData != saved)
            {
                Debug.LogError($"{Tag} NavMeshData 저장 실패: {assetPath}");
                return false;
            }
            if (!NavMesh.SamplePosition(new Vector3(0f, 0f, 0f) + Vector3.up * 0.1f, out NavMeshHit _, 2f, NavMesh.AllAreas))
            {
                Debug.LogError($"{Tag} 구운 NavMesh에서 원점 샘플 실패: {assetPath}");
                return false;
            }
            Bounds b = saved.sourceBounds;
            Debug.Log($"{Tag} NavMesh 굽기: {assetPath} (sourceBounds {b.size})");
            return true;
        }

        // ---------- 관리자·플레이어 ----------

        private static SceneContext CreateManagers(out ProjectileSystem projectiles)
        {
            // 에셋은 NewScene 뒤에 불러온다 (M2 함정: 그 전에 잡은 참조는 끊긴다)
            var ctx = new SceneContext
            {
                Scv = SetupUtil.Load<EnemyArchetypeSO>(M3DataSetup.ScvPath),
                Rdr = SetupUtil.Load<EnemyArchetypeSO>(M3DataSetup.RdrPath),
                ScvMaterial = SetupUtil.Load<Material>(ScvMaterialPath),
                RdrMaterial = SetupUtil.Load<Material>(RdrMaterialPath),
            };
            projectiles = new GameObject(nameof(ProjectileSystem)).AddComponent<ProjectileSystem>();
            ctx.Director = new GameObject(nameof(AiDirector)).AddComponent<AiDirector>();
            SetupUtil.SetFields(ctx.Director, ("config", SetupUtil.Load<AiConfigSO>(M3DataSetup.AiConfigPath)));
            return ctx;
        }

        private static void CreatePlayerAndUi(Vector3 start, ProjectileSystem projectiles)
        {
            M2SceneSetup.CreatePlayer(start, projectiles); // M2와 같은 구성·시작 장비, +z
            M2SceneSetup.CreateGameUI(SetupUtil.Load<PanelSettings>(M2SceneSetup.PanelSettingsPath));
        }

        // ---------- 적 ----------

        private static PatrolRoute CreateRoute(string name, Vector2[] points, Vector3 offset)
        {
            var go = new GameObject(name);
            go.transform.position = offset;
            var waypoints = new Transform[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var wp = new GameObject($"WP_{i + 1}").transform;
                wp.SetParent(go.transform, false);
                wp.position = At(points[i], offset);
                waypoints[i] = wp;
            }
            var route = go.AddComponent<PatrolRoute>();
            var so = new SerializedObject(route);
            SerializedProperty array = so.FindProperty("waypoints");
            array.arraySize = waypoints.Length;
            for (int i = 0; i < waypoints.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = waypoints[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return route;
        }

        // 경로 첫 지점에 리더, 뒤쪽 좌우에 분대원. 첫 구간 방향을 본다.
        private static void CreateSquad(SceneContext ctx, string squadName, EnemyArchetypeSO archetype, int squadId,
            PatrolRoute route, Vector2[] routePoints, Vector3 offset, int[] loadouts)
        {
            Vector3 start = At(routePoints[0], offset);
            Vector3 heading = At(routePoints[1], offset) - start;
            heading.y = 0f;
            Quaternion facing = Quaternion.LookRotation(heading.normalized, Vector3.up);
            for (int i = 0; i < loadouts.Length; i++)
            {
                Vector2 slot = SquadSlots[i % SquadSlots.Length];
                Vector3 position = start + facing * new Vector3(slot.x, 0f, slot.y);
                CreateEnemy(ctx, $"{squadName}{i + 1}", archetype, position, facing.eulerAngles.y, route, squadId, loadouts[i]);
            }
        }

        private static void CreateRaiders(SceneContext ctx, string squadName, int squadId, Vector3 offset)
        {
            for (int i = 0; i < RaiderSpots.Length; i++)
                CreateEnemy(ctx, $"{squadName}{i + 1}", ctx.Rdr, At(RaiderSpots[i], offset), 180f, null, squadId, 0);
        }

        // 설계 §8 적 오브젝트. seed = 씬 내 순번 + 1(결정적).
        private static GameObject CreateEnemy(SceneContext ctx, string id, EnemyArchetypeSO archetype, Vector3 position,
            float yaw, PatrolRoute route, int squadId, int loadoutIndex)
        {
            int order = ctx.EnemyCount++;
            var root = new GameObject($"Enemy_{id}");
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var agent = root.AddComponent<EnemyAgent>(); // RequireComponent: NavMeshAgent, CapsuleCollider, Rigidbody

            var capsule = root.GetComponent<CapsuleCollider>();
            capsule.height = EnemyHeightM;
            capsule.radius = EnemyRadiusM;
            capsule.center = new Vector3(0f, EnemyHeightM * 0.5f, 0f);

            var body = root.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var nav = root.GetComponent<NavMeshAgent>();
            nav.agentTypeID = 0; // Humanoid
            nav.baseOffset = 0f; // 피벗 = 발
            nav.radius = EnemyRadiusM;
            nav.height = EnemyHeightM;
            nav.angularSpeed = AgentAngularSpeed;
            nav.acceleration = AgentAcceleration;
            nav.stoppingDistance = AgentStoppingDistance;
            nav.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            nav.avoidancePriority = AvoidancePriorityMin + (order * 7) % AvoidancePriorityRange;
            nav.speed = archetype != null ? archetype.patrolSpeed : nav.speed; // 실행 중에는 EnemyAgent가 덮어씀

            Material material = archetype == ctx.Rdr ? ctx.RdrMaterial : ctx.ScvMaterial;
            Transform bodyVisual = CreateVisual(PrimitiveType.Capsule, "Body", root.transform, BodyLocal, BodyScale, material);

            var eye = new GameObject("Eye").transform;
            eye.SetParent(root.transform, false);
            eye.localPosition = EyeLocal;

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = MuzzleLocal;

            // 총 모양 큐브: Muzzle 자식으로 둬 웅크림 때 총구와 함께 내려간다(설계 트리에서는 형제, 보기만 다름)
            CreateVisual(PrimitiveType.Cube, "Gun", muzzle, new Vector3(0f, 0f, -0.25f), new Vector3(0.08f, 0.1f, 0.5f), null);

            Transform indicator = CreateVisual(PrimitiveType.Sphere, "Indicator", root.transform, IndicatorLocal,
                Vector3.one * IndicatorScale, null);

            var debugView = root.AddComponent<EnemyDebugView>();
            SetupUtil.SetFields(debugView, ("indicator", indicator.GetComponent<Renderer>()));

            SetupUtil.SetFields(agent,
                ("archetype", archetype), ("director", ctx.Director), ("patrolRoute", route),
                ("eye", eye), ("muzzle", muzzle), ("bodyVisual", bodyVisual));
            var so = new SerializedObject(agent);
            so.FindProperty("squadId").intValue = squadId;
            so.FindProperty("loadoutIndex").intValue = loadoutIndex;
            so.FindProperty("seed").intValue = order + 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static Transform CreateVisual(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            if (material != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        // ---------- 공용 에셋 ----------

        // 그레이박스 색 재질 (URP Lit). 없을 때만 만든다.
        private static void CreateColorMaterial(string path, string name, Color color)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                return;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError($"{Tag} URP Lit 셰이더를 찾지 못했다.");
                return;
            }
            SetupUtil.EnsureFolder(MaterialFolder);
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.color = color;
            AssetDatabase.CreateAsset(material, path);
            Debug.Log($"{Tag} 에셋 생성: {path}");
        }
    }
}
