using System;
using System.Collections.Generic;
using System.Text;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.Raid;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // 맵 밀도 ③ 상자·트렁크·루트 지점을 레이드 씬에 적용 (docs/design/MapDensity_design.md §5.3·§5.4·§6·§7 3, SYS-INV §6 루트 컨테이너).
    // 만드는 것 (모두 있으면 개수만 확인 — 멱등):
    //   - 기존 21 상자(Environment/LootCrates)에 LootSpawnSlot(키 = 이름, 신호탄 상자 = 항상 활성) — 상자 자체는 그대로
    //   - 승용차마다 자식 트렁크 "Trunk_{zone}_{nnn}": 렌더러 없음, BoxCollider 0.6 × 0.8 × 1.6m(차체 뒷면에서 5cm 밖), LootContainer·LootSpawnSlot(Trunk)·GroundSurface(금속)
    //   - Environment/MapDensity_Loot: 새 상자 "LootCrate_{zone}_{nnn}"(1m 큐브, M_Crate, LootContainer·LootSpawnSlot(Crate), 컬 LODGroup) — 구역 목표 − 기존
    //   - 씬 최상위 LootSpawnDirector(LootSpawnConfig 연결)
    // 하나라도 빠졌으면 전부 지우고 다시(부분 상태 없음). 새 상자를 만들면 배치 확인 → NavMesh 다시 굽기 → 굽기 뒤 회귀·도달 확인(200곳: 접근점 NavMesh 1m 안 +
    // 기준 스폰에서 연결, 눈높이 레이가 interactRangeM 안에서 그 컨테이너를 처음 맞춤), 실패 = 만든 것·슬롯을 지우고 NavMesh를 되돌린 뒤 예외(씬 저장 안 함).
    // 풀 제외(상자 접근 1.5m)·컬링은 호출자(MapDensitySceneApply.ApplyRaid)가 끝에서 Refresh로 맞춘다.
    public static class MapDensityLootApply
    {
        private const string Tag = "[MapDensityLootApply]";
        public const string DirectorName = "LootSpawnDirector";
        public const string OldCrateGroup = "LootCrates";
        public const string FlarePrefix = "FlareCrate_";

        // 트렁크 (설계 §5.4) — 레벨 데이터, TODO(GDD-?) 임시값(크기·돌출은 설계값, 접근 거리·윗면/옆 여유는 설계에 없음)
        public static readonly Vector3 TrunkSizeM = new Vector3(0.6f, 0.8f, 1.6f);   // 길이 축 깊이 × 높이 × 폭
        public const float TrunkProtrudeM = 0.05f;       // 차체 뒷면 밖으로
        public const float TrunkTopInsetM = 0.05f;       // 차체 윗면 아래로
        public const float TrunkSideInsetM = 0.1f;       // 폭이 차폭보다 이만큼 작게
        public const float TrunkAccessDistanceM = 1.2f;  // 트렁크 바깥 면 → 접근점
        public const float AccessOnMeshM = 1.0f;         // 접근점 ↔ NavMesh (설계 §7 3)

        public static bool ConfigExists() => AssetDatabase.LoadAssetAtPath<LootSpawnConfigSO>(MapDensityDataSetup.LootSpawnConfigPath) != null;

        // ---------- 상태 ----------

        // 모두 있는가 (그룹·지휘자·승용차마다 트렁크·기존 상자 슬롯)
        public static bool IsComplete(Scene scene, LootSpawnConfigSO cfg, out string why)
        {
            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            if (environment == null)
            {
                why = "Environment 없음";
                return false;
            }
            if (environment.transform.Find(MapDensityLayout.LootGroup) == null)
            {
                why = $"{MapDensityLayout.LootGroup} 없음";
                return false;
            }
            GameObject director = SetupUtil.FindRoot(scene, DirectorName);
            LootSpawnDirector directorComponent = director != null ? director.GetComponent<LootSpawnDirector>() : null;
            if (directorComponent == null || directorComponent.Config == null || directorComponent.Config != cfg)   // 리뷰 L6: 같은 설정 에셋인가
            {
                why = "LootSpawnDirector 없음(또는 설정이 비었거나 LootSpawnConfig.asset이 아님)";
                return false;
            }
            foreach (MapDensitySceneApply.PassengerCar car in MapDensitySceneApply.PassengerCars(scene))
                if (FindTrunk(car.Structure) == null)
                {
                    why = $"트렁크 없는 승용차 {car.Structure.parent?.name}/{car.Structure.name}";
                    return false;
                }
            foreach (LootContainer c in environment.GetComponentsInChildren<LootContainer>(true))
                if (c.GetComponent<LootSpawnSlot>() == null)
                {
                    why = $"슬롯 없는 컨테이너 {c.name}";
                    return false;
                }
            why = null;
            return true;
        }

        internal static Transform FindTrunk(Transform carStructure)
        {
            foreach (Transform child in carStructure)
            {
                var slot = child.GetComponent<LootSpawnSlot>();
                if (slot != null && slot.Kind == LootSlotKind.Trunk)
                    return child;
            }
            return null;
        }

        // 루트 지점 전부 지우기 (Rebuild·부분 상태): 그룹, 지휘자, 트렁크, 남은 컨테이너의 슬롯. 지운 것이 있으면 true.
        public static bool Remove(Scene scene)
        {
            bool removed = false;
            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            GameObject director = SetupUtil.FindRoot(scene, DirectorName);
            if (director != null)
            {
                Object.DestroyImmediate(director);
                removed = true;
            }
            if (environment != null)
            {
                Transform group = environment.transform.Find(MapDensityLayout.LootGroup);
                if (group != null)
                {
                    Object.DestroyImmediate(group.gameObject);
                    removed = true;
                }
                foreach (LootSpawnSlot slot in environment.GetComponentsInChildren<LootSpawnSlot>(true))
                {
                    if (slot == null)
                        continue;
                    if (slot.Kind == LootSlotKind.Trunk)
                        Object.DestroyImmediate(slot.gameObject);
                    else
                        Object.DestroyImmediate(slot);
                    removed = true;
                }
            }
            if (removed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log($"{Tag} 루트 지점(그룹·트렁크·슬롯·지휘자)을 지웠다");
            }
            return removed;
        }

        // ---------- 적용 ----------

        // 설정이 없으면 건너뛴다(false). 모두 있으면 개수만 확인. 아니면 지우고 다시 만든다(실패 = 예외, 되돌림).
        public static bool Apply(Scene scene)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<LootSpawnConfigSO>(MapDensityDataSetup.LootSpawnConfigPath);
            if (cfg == null)
            {
                Debug.Log($"{Tag} {MapDensityDataSetup.LootSpawnConfigPath} 없음 — 루트 지점(③)은 건너뛴다(Ashfall/Setup/Create Map Density Data)");
                return false;
            }
            if (IsComplete(scene, cfg, out string why))
            {
                string countReport = CountReport(scene, cfg, out bool countsOk);
                if (!countsOk)
                    throw new InvalidOperationException($"{Tag} 루트 지점 개수가 LootSpawnConfig 목표와 다르다 — Ashfall/Setup/Map Density/Rebuild Map Density (Raid Scene)\n{countReport}");
                int cleared = ClearNewPointTables(scene);
                Debug.Log($"{Tag} 루트 지점 있음 — 그대로{(cleared > 0 ? $" (새 지점 직렬화 테이블 비움 {cleared})" : "")}\n{countReport}");
                return true;
            }
            Debug.Log($"{Tag} 루트 지점 만들기 ({why})");
            Remove(scene);
            BuildOrRollBack(scene, cfg);
            return true;
        }

        // 리뷰 L4: 새 루트 지점(새 상자·트렁크)의 직렬화 테이블은 비워 둔다 — 지휘자가 동작하지 못한 레이드에서 203곳이 모두 차는 대신
        // 기존 21 상자만 루트가 있는 "루트 적음" 쪽으로 실패한다(활성 지점은 지휘자가 구역 테이블을 준다). 바꾼 수(같으면 0 — 멱등).
        internal static int ClearNewPointTables(Scene scene)
        {
            int changed = 0;
            foreach (LootSpawnSlot slot in AllSlots(scene))
            {
                var c = slot.GetComponent<LootContainer>();
                if (c == null || !IsNewPoint(slot) || c.SerializedLootTable == null)
                    continue;
                var so = new SerializedObject(c);
                so.FindProperty("lootTable").objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
                changed++;
            }
            if (changed > 0)
                EditorSceneManager.MarkSceneDirty(scene);
            return changed;
        }

        internal static bool IsNewPoint(LootSpawnSlot slot) =>
            slot.Kind == LootSlotKind.Trunk || (slot.SlotKey ?? "").StartsWith(MapDensityLootLayout.NamePrefix, StringComparison.Ordinal);

        private sealed class Undo
        {
            public readonly List<GameObject> Created = new List<GameObject>();
            public readonly List<Component> Added = new List<Component>();
            // 리뷰 L5: 기존 상자 이름·그리드를 설정 값으로 맞췄으면 원래 값 (실패 시 되돌린다)
            public readonly List<(LootContainer container, string displayName, Vector2Int gridSize)> Restored = new List<(LootContainer, string, Vector2Int)>();
            public bool Rebaked;
        }

        private static void BuildOrRollBack(Scene scene, LootSpawnConfigSO cfg)
        {
            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            var surface = environment != null ? environment.GetComponent<NavMeshSurface>() : null;
            Terrain terrain = environment != null ? environment.GetComponentInChildren<Terrain>() : null;
            if (surface == null || terrain == null)
                throw new InvalidOperationException($"{Tag} {scene.name}: Environment/NavMeshSurface·Terrain 없음");
            string navPath = M4MapSetup.NavMeshPath;
            byte[] navBackup = MapPlacementChecks.ReadBytes(navPath);
            var undo = new Undo();
            try
            {
                Build(scene, environment, terrain, surface, cfg, undo);
            }
            catch
            {
                foreach (GameObject go in undo.Created)
                    if (go != null)
                        Object.DestroyImmediate(go);
                foreach (Component c in undo.Added)
                    if (c != null)
                        Object.DestroyImmediate(c);
                foreach ((LootContainer container, string displayName, Vector2Int gridSize) in undo.Restored)
                {
                    if (container == null)
                        continue;
                    var so = new SerializedObject(container);
                    so.FindProperty("displayName").stringValue = displayName;
                    so.FindProperty("gridSize").vector2IntValue = gridSize;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                if (undo.Rebaked && navBackup != null)
                    MapPlacementChecks.RestoreNavMesh(surface, navPath, navBackup);
                Debug.LogError($"{Tag} 실패 — 만든 오브젝트 {undo.Created.Count}·슬롯 {undo.Added.Count}를 지우고{(undo.Rebaked ? " NavMesh를 되돌렸다" : "")}");
                throw;
            }
        }

        private static void Build(Scene scene, GameObject environment, Terrain terrain, NavMeshSurface surface, LootSpawnConfigSO cfg, Undo undo)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ReachProbe probe = ReachProbe.Create();
            MapPlacementChecks.NavState before = MapPlacementChecks.NavState.Capture(scene, null);
            if (before.Reference == null)
                throw new InvalidOperationException($"{Tag} 기준 스폰이 NavMesh 위에 없다");
            probe.Reference = before.Reference.Value;

            // 1) 기존 상자에 슬롯
            var oldCrates = new List<LootContainer>();
            foreach (LootContainer c in environment.GetComponentsInChildren<LootContainer>(true))
                oldCrates.Add(c);
            foreach (LootContainer c in oldCrates)
            {
                bool flare = c.name.StartsWith(FlarePrefix, StringComparison.Ordinal);
                undo.Added.Add(AddSlot(c.gameObject, c.name, LootZoneRules.ZoneOf(XZ(c.transform.position)), LootSlotKind.Crate, flare));
                if (c.DisplayName != cfg.crateDisplayName || c.GridSize != cfg.crateGridSize)
                    undo.Restored.Add((c, c.DisplayName, c.GridSize));   // 리뷰 L5
                ApplyCrateConfig(c, cfg, null, keepTable: true);
            }

            // 2) 승용차마다 트렁크 (뒤 끝 우선, 닿지 않으면 앞 끝)
            var trunkCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var trunkFails = new StringBuilder();
            int flipped = 0;
            foreach (MapDensitySceneApply.PassengerCar car in MapDensitySceneApply.PassengerCars(scene))
            {
                int ordinal = (trunkCounts.TryGetValue(car.Zone, out int n) ? n : 0) + 1;
                trunkCounts[car.Zone] = ordinal;
                GameObject trunk = CreateTrunk(car, MapDensityLootLayout.TrunkName(car.Zone, ordinal), cfg, rear: true);
                undo.Created.Add(trunk);
                Physics.SyncTransforms();
                if (probe.Check(trunk.GetComponent<Collider>(), TrunkAccessPoints(trunk.transform), requireFirstHit: true, out _))
                    continue;
                Object.DestroyImmediate(trunk);
                undo.Created.RemoveAt(undo.Created.Count - 1);
                trunk = CreateTrunk(car, MapDensityLootLayout.TrunkName(car.Zone, ordinal), cfg, rear: false);
                undo.Created.Add(trunk);
                Physics.SyncTransforms();
                if (probe.Check(trunk.GetComponent<Collider>(), TrunkAccessPoints(trunk.transform), requireFirstHit: true, out string reason))
                    flipped++;
                else
                    trunkFails.AppendLine($"  {car.Structure.parent?.name}/{car.Structure.name} ({car.Structure.position.x:0}, {car.Structure.position.z:0}): {reason}");
            }
            string trunkReport = ZoneCounts("트렁크", trunkCounts, cfg, kindTrunk: true, out bool trunksOk);
            if (!trunksOk || trunkFails.Length > 0)
                throw new InvalidOperationException($"{Tag} 트렁크 확인 실패 (구역별 수 = 승용차 수가 LootSpawnConfig.trunkTargetCount와 같아야 한다)\n{trunkReport}" +
                                                    (trunkFails.Length > 0 ? $"  닿지 않는 트렁크(앞·뒤 모두):\n{trunkFails}" : ""));

            // 3) 새 상자: 구역 목표 − 기존
            Physics.SyncTransforms();
            MapDensityLayout.SiteContext ctx = MapDensitySceneApply.GatherContext(scene, terrain);
            var walls = CollectWalls(environment);
            var existing = new List<Vector2>();
            var oldCrateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (LootContainer c in environment.GetComponentsInChildren<LootContainer>(true))
            {
                existing.Add(XZ(c.transform.position));
                var slot = c.GetComponent<LootSpawnSlot>();
                if (slot.Kind == LootSlotKind.Crate)
                    oldCrateCounts[slot.ZoneId] = (oldCrateCounts.TryGetValue(slot.ZoneId, out int k) ? k : 0) + 1;
            }
            var want = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (LootZoneConfig z in cfg.zones)
                want[z.zoneId] = z.crateTargetCount - (oldCrateCounts.TryGetValue(z.zoneId, out int old) ? old : 0);
            List<MapDensityLootLayout.CratePlan> plans = MapDensityLootLayout.GenerateCrates(ctx, walls, want, existing,
                plan => probe.CheckPlan(terrain, plan), out string crateReport);
            Debug.Log($"{Tag} 입력: 벽 {walls.Count}, 기존 컨테이너 {existing.Count}(상자 {oldCrates.Count}·트렁크 {existing.Count - oldCrates.Count})\n상자 생성:\n{crateReport}");
            var made = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (MapDensityLootLayout.CratePlan p in plans)
                made[p.Zone] = (made.TryGetValue(p.Zone, out int m) ? m : 0) + 1;
            foreach (KeyValuePair<string, int> w in want)
                if ((made.TryGetValue(w.Key, out int got) ? got : 0) != Math.Max(0, w.Value))
                    throw new InvalidOperationException($"{Tag} 새 상자 자리 부족: {w.Key} {got}/{w.Value} (배치 데이터·규칙 조정 필요)\n{crateReport}");

            var group = new GameObject(MapDensityLayout.LootGroup).transform;
            group.SetParent(environment.transform, false);
            undo.Created.Add(group.gameObject);
            foreach (MapDensityLootLayout.CratePlan p in plans)
                CreateCrate(group, terrain, p, cfg);
            Dictionary<PaletteMaterialMap.Key, Material> materials = M6VisualSceneApply.LoadPaletteMaterials();
            var unknown = new List<string>();
            M6VisualSceneApply.PaintGroup(group, materials, unknown);
            if (unknown.Count > 0)
                throw new InvalidOperationException($"{Tag} 칠하지 못한 상자: {string.Join(", ", unknown)}");
            MapPlacementChecks.MarkNotWalkable(group, NavMesh.GetAreaFromName(MapPlacementChecks.NotWalkableArea));

            // 4) 배치 확인 (새 상자 콜라이더 — 겹침·지점·순찰 통로·보스 거점·저격 시선)
            Physics.SyncTransforms();
            var problems = new StringBuilder();
            var rules = new MapPlacementChecks.PlacementRules
            {
                PatrolSegmentClearM = MapDensityLayout.PatrolSegmentClearM,
                KeepOutCenter = MapDensityLayout.BossCenter(),
                KeepOutRadiusM = MapDensityLayout.BossKeepOutM,
                KeepOutName = "보스 거점",
                PointScale = MapDensityLootLayout.CratePointScale,
            };
            MapPlacementChecks.CheckPlacement(scene, new[] { group }, terrain, problems, rules);
            if (problems.Length > 0)
                throw new InvalidOperationException($"{Tag} 상자 배치 확인 실패 (씬 저장 안 함)\n{problems}");

            // 5) 지휘자
            var directorGo = new GameObject(DirectorName);
            SceneManager.MoveGameObjectToScene(directorGo, scene);
            undo.Created.Add(directorGo);
            var director = directorGo.AddComponent<LootSpawnDirector>();
            var dso = new SerializedObject(director);
            dso.FindProperty("config").objectReferenceValue = cfg;
            dso.ApplyModifiedPropertiesWithoutUndo();
            double placeSeconds = watch.Elapsed.TotalSeconds;

            // 6) 굽기 → 회귀·도달
            undo.Rebaked = true;
            if (!M4MapSetup.RebakeNavMesh(surface, M4MapSetup.NavMeshPath, out double bakeSeconds))
                throw new InvalidOperationException($"{Tag} NavMesh 다시 굽기 실패");
            MapPlacementChecks.NavState after = MapPlacementChecks.NavState.Capture(scene, before);
            var raidConfig = AssetDatabase.LoadAssetAtPath<RaidConfigSO>(M4MapSetup.DefaultRaidConfigPath);
            bool pathsOk = M4MapSetup.ValidateNavigation(raidConfig, out string navReport);
            string regressions = after.Regressions(before);
            string longer = after.PathLengthRegressions(before, 0.2f, out float maxIncrease);
            string reach = CheckReach(scene, probe, out int reachable, out int total);
            string countReport = CountReport(scene, cfg, out bool countsOk);
            if (!pathsOk || regressions.Length > 0 || longer.Length > 0 || reachable != total || !countsOk)
                throw new InvalidOperationException($"{Tag} 굽기 뒤 확인 실패 (씬 저장 안 함)\n{navReport}{regressions}{longer}{reach}{countReport}");

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{Tag} {scene.name}: 루트 지점 적용 완료 (배치 {placeSeconds:0.0}s, NavMesh 굽기 {bakeSeconds:0.0}s, 트렁크 앞 끝 {flipped})\n" +
                      $"{countReport}  도달 {reachable}/{total} (접근점 NavMesh {AccessOnMeshM}m 안 + 기준 스폰에서 연결 + 눈높이 레이 {probe.InteractRangeM}m 안 첫 충돌)\n" +
                      $"  순찰 경로 최대 증가 {maxIncrease * 100f:0.0}%\n  굽기 전: {before.Summary}\n  굽기 뒤: {after.Summary}\n{navReport}");
        }

        // ---------- 만들기 ----------

        private static LootSpawnSlot AddSlot(GameObject go, string key, string zone, LootSlotKind kind, bool always)
        {
            var slot = go.AddComponent<LootSpawnSlot>();
            var so = new SerializedObject(slot);
            so.FindProperty("slotKey").stringValue = key;
            so.FindProperty("zoneId").stringValue = zone;
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("alwaysActive").boolValue = always;
            so.ApplyModifiedPropertiesWithoutUndo();
            return slot;
        }

        // 표시 이름·그리드 = 설정(상자·트렁크), 테이블 = 구역 설정(지휘자가 없는 씬에서 쓰는 직렬화 값). keepTable = 기존 상자(테이블·고정 내용물 그대로).
        private static void ApplyCrateConfig(LootContainer container, LootSpawnConfigSO cfg, LootTableSO table, bool keepTable, bool trunk = false)
        {
            var so = new SerializedObject(container);
            SetIfDifferent(so.FindProperty("displayName"), trunk ? cfg.trunkDisplayName : cfg.crateDisplayName);
            SerializedProperty grid = so.FindProperty("gridSize");
            Vector2Int size = trunk ? cfg.trunkGridSize : cfg.crateGridSize;
            if (grid.vector2IntValue != size)
                grid.vector2IntValue = size;
            if (!keepTable)
            {
                so.FindProperty("contents").arraySize = 0;
                so.FindProperty("lootTable").objectReferenceValue = table;
                so.FindProperty("seed").intValue = 0;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetIfDifferent(SerializedProperty p, string value)
        {
            if (p.stringValue != value)
                p.stringValue = value;
        }

        private static GameObject CreateTrunk(MapDensitySceneApply.PassengerCar car, string name, LootSpawnConfigSO cfg, bool rear)
        {
            Transform body = car.Body;
            Vector3 s = body.lossyScale;
            bool alongX = s.x >= s.z;
            float length = alongX ? s.x : s.z, width = alongX ? s.z : s.x, height = s.y;
            float sign = rear ? -1f : 1f;
            // Block 로컬(단위 큐브) 좌표: 길이 축 끝 면 ± 돌출, 윗면 아래
            float along = sign * (0.5f - (TrunkSizeM.x * 0.5f - TrunkProtrudeM) / length);
            float up = 0.5f - (TrunkTopInsetM + TrunkSizeM.y * 0.5f) / height;
            Vector3 local = alongX ? new Vector3(along, up, 0f) : new Vector3(0f, up, along);
            Vector3 world = body.TransformPoint(local);
            // 트렁크 로컬 +x = 바깥쪽(차 끝 방향)
            Quaternion frame = body.rotation * (alongX ? Quaternion.identity : Quaternion.Euler(0f, -90f, 0f)) * (rear ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);

            var go = new GameObject(name);
            go.transform.SetParent(car.Structure, false);
            go.transform.SetPositionAndRotation(world, frame);
            Vector3 parentScale = car.Structure.lossyScale;
            go.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, parentScale.x), 1f / Mathf.Max(1e-4f, parentScale.y), 1f / Mathf.Max(1e-4f, parentScale.z));
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(TrunkSizeM.x, TrunkSizeM.y, Mathf.Min(TrunkSizeM.z, width - TrunkSideInsetM));
            var container = go.AddComponent<LootContainer>();
            ApplyCrateConfig(container, cfg, null, keepTable: false, trunk: true);   // 리뷰 L4: 직렬화 테이블 없음(지휘자가 못 돌면 루트 적음 쪽)
            AddSlot(go, name, car.Zone, LootSlotKind.Trunk, false);
            go.AddComponent<GroundSurface>().Kind = SurfaceKind.Metal;
            return go;
        }

        // 트렁크 접근점: 바깥 면에서 TrunkAccessDistanceM (트렁크 로컬 +x)
        internal static Vector3[] TrunkAccessPoints(Transform trunk)
        {
            Vector3 outward = trunk.right;
            outward = new Vector3(outward.x, 0f, outward.z).normalized;
            return new[] { trunk.position + outward * (TrunkSizeM.x * 0.5f + TrunkAccessDistanceM) };
        }

        // 상자 접근점: 네 면 앞 (상자 중심에서 AccessDistanceM)
        internal static Vector3[] CrateAccessPoints(Transform crate)
        {
            Vector3 r = crate.right, f = crate.forward;
            r = new Vector3(r.x, 0f, r.z).normalized;
            f = new Vector3(f.x, 0f, f.z).normalized;
            float d = MapDensityLootLayout.AccessDistanceM;
            return new[] { crate.position + r * d, crate.position - r * d, crate.position + f * d, crate.position - f * d };
        }

        private static void CreateCrate(Transform group, Terrain terrain, MapDensityLootLayout.CratePlan p, LootSpawnConfigSO cfg)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = p.Name;
            go.transform.SetParent(group, false);
            float size = MapDensityLootLayout.CrateSizeM;
            go.transform.SetPositionAndRotation(M4MapSetup.GroundPoint(terrain, p.XZ) + new Vector3(0f, size * 0.5f, 0f), Quaternion.Euler(0f, p.YawDeg, 0f));
            go.transform.localScale = Vector3.one * size;
            var container = go.AddComponent<LootContainer>();
            ApplyCrateConfig(container, cfg, null, keepTable: false);   // 리뷰 L4: 직렬화 테이블 없음(활성이면 지휘자가 구역 테이블을 준다)
            AddSlot(go, p.Name, p.Zone, LootSlotKind.Crate, false);
        }

        // 상자를 붙일 면: Environment 아래 트리거 아닌 BoxCollider(지형·산포물·루트 컨테이너·스킨 제외), 발자국 짧은 변 ≥ 0.4m·긴 변 ≥ 1.5m. 계층 순서.
        private static List<MapDensityLootLayout.Wall> CollectWalls(GameObject environment)
        {
            var walls = new List<MapDensityLootLayout.Wall>();
            Transform vegetation = environment.transform.Find(MapDensityLayout.VegetationGroup);
            foreach (BoxCollider c in environment.GetComponentsInChildren<BoxCollider>(true))
            {
                if (c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy || c.GetComponentInParent<LootContainer>() != null || AssetSwapApply.UnderSkin(c.transform)
                    || (vegetation != null && c.transform.IsChildOf(vegetation)))
                    continue;
                Footprint f = MapDensitySceneApply.FootprintOf(c);
                if (Mathf.Min(f.Half.x, f.Half.y) < 0.2f || Mathf.Max(f.Half.x, f.Half.y) < MapDensityLootLayout.MinFaceLengthM * 0.5f)
                    continue;
                walls.Add(new MapDensityLootLayout.Wall($"{c.transform.parent?.name}/{c.name}#{c.GetInstanceID()}", f));
            }
            return walls;
        }

        // ---------- 확인 ----------

        // 도달 확인 도구: 접근점 NavMesh(AccessOnMeshM) + 기준 스폰에서 연결 + 눈높이 레이가 interactRangeM 안에서 대상 콜라이더를 처음 맞춤
        internal sealed class ReachProbe
        {
            public Vector3 Reference;
            public float EyeHeightM;
            public float InteractRangeM;
            private readonly NavMeshPath path = new NavMeshPath();
            private readonly RaycastHit[] hits = new RaycastHit[16];

            public static ReachProbe Create()
            {
                var player = AssetDatabase.LoadAssetAtPath<PlayerConfigSO>(M2DataSetup.PlayerConfigPath);
                var inventory = AssetDatabase.LoadAssetAtPath<InventoryConfigSO>(M2DataSetup.InventoryConfigPath);
                if (player == null || inventory == null)
                    throw new InvalidOperationException($"{Tag} PlayerConfig·InventoryConfig 없음");
                return new ReachProbe { EyeHeightM = player.standHeight - player.eyeBelowTop, InteractRangeM = inventory.interactRangeM };
            }

            // target이 있으면: 접근점 중 하나라도 (NavMesh·연결·첫 충돌 = target, 거리 ≤ 상호작용 거리). why = 마지막 실패 이유.
            public bool Check(Collider target, Vector3[] accessPoints, bool requireFirstHit, out string why)
            {
                why = "접근점 없음";
                foreach (Vector3 access in accessPoints)
                {
                    if (!NavMesh.SamplePosition(access, out NavMeshHit hit, AccessOnMeshM + 1.5f, NavMesh.AllAreas)
                        || Vector2.Distance(XZ(hit.position), XZ(access)) > AccessOnMeshM)
                    {
                        why = $"접근점 ({access.x:0.0}, {access.z:0.0}) {AccessOnMeshM}m 안 NavMesh 없음";
                        continue;
                    }
                    if (!M4MapSetup.TryPath(Reference, hit.position, path))
                    {
                        why = $"접근점 ({access.x:0.0}, {access.z:0.0}) 기준 스폰에서 닿지 않음";
                        continue;
                    }
                    Vector3 eye = hit.position + Vector3.up * EyeHeightM;
                    Vector3 to = target.bounds.center;
                    if (!FirstHitIs(eye, to, target, out string rayWhy))
                    {
                        why = $"접근점 ({access.x:0.0}, {access.z:0.0}) 눈높이 레이: {rayWhy}";
                        continue;
                    }
                    why = null;
                    return true;
                }
                return false;
            }

            private bool FirstHitIs(Vector3 eye, Vector3 to, Collider target, out string why)
            {
                Vector3 dir = (to - eye).normalized;
                int count = Physics.RaycastNonAlloc(eye, dir, hits, InteractRangeM, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                Collider first = null;
                for (int i = 0; i < count; i++)
                    if (hits[i].distance < best && !(hits[i].collider is CharacterController))
                    {
                        best = hits[i].distance;
                        first = hits[i].collider;
                    }
                if (first == target)
                {
                    why = null;
                    return true;
                }
                why = first == null ? $"{InteractRangeM}m 안에서 아무것도 맞지 않음(거리 {Vector3.Distance(eye, to):0.00}m)" : $"{MapPlacementChecks.Owner(first)}/{first.name}가 먼저 맞음({best:0.00}m)";
                return false;
            }

            // 생성 중 후보 확인 (상자는 아직 없다): 접근점 NavMesh·연결 + 눈높이에서 상자 중심까지 아무 콜라이더도 막지 않음
            public bool CheckPlan(Terrain terrain, MapDensityLootLayout.CratePlan plan)
            {
                Vector3 access = M4MapSetup.GroundPoint(terrain, plan.AccessXZ);
                if (!NavMesh.SamplePosition(access, out NavMeshHit hit, AccessOnMeshM + 1.5f, NavMesh.AllAreas)
                    || Vector2.Distance(XZ(hit.position), plan.AccessXZ) > AccessOnMeshM * 0.5f)
                    return false;
                if (!M4MapSetup.TryPath(Reference, hit.position, path))
                    return false;
                Vector3 eye = hit.position + Vector3.up * EyeHeightM;
                Vector3 center = M4MapSetup.GroundPoint(terrain, plan.XZ) + Vector3.up * (MapDensityLootLayout.CrateSizeM * 0.5f);
                if (Vector3.Distance(eye, center) > InteractRangeM)
                    return false;
                return !Physics.Linecast(eye, center, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            }
        }

        // 모든 루트 지점 도달 (설계 §7 3). 실패 줄을 돌려준다.
        private static string CheckReach(Scene scene, ReachProbe probe, out int reachable, out int total)
        {
            var sb = new StringBuilder();
            reachable = total = 0;
            foreach (LootSpawnSlot slot in AllSlots(scene))
            {
                total++;
                var collider = slot.GetComponent<Collider>();
                Vector3[] access = slot.Kind == LootSlotKind.Trunk ? TrunkAccessPoints(slot.transform) : CrateAccessPoints(slot.transform);
                string why = null;
                if (collider != null && probe.Check(collider, access, requireFirstHit: true, out why))
                    reachable++;
                else
                    sb.AppendLine($"  닿지 않는 루트 지점 {slot.SlotKey} ({slot.transform.position.x:0.0}, {slot.transform.position.z:0.0}): {why ?? "콜라이더 없음"}");
            }
            return sb.ToString();
        }

        internal static List<LootSpawnSlot> AllSlots(Scene scene)
        {
            var slots = new List<LootSpawnSlot>();
            foreach (GameObject root in scene.GetRootGameObjects())
                slots.AddRange(root.GetComponentsInChildren<LootSpawnSlot>(true));
            return slots;
        }

        // 구역·종류별 개수 = 설정 목표인가 + 키 중복 없음 + 구역 설정 있음. 보고 줄.
        public static string CountReport(Scene scene, LootSpawnConfigSO cfg, out bool ok)
        {
            var crates = new Dictionary<string, int>(StringComparer.Ordinal);
            var trunks = new Dictionary<string, int>(StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            ok = true;
            int always = 0;
            foreach (LootSpawnSlot slot in AllSlots(scene))
            {
                Dictionary<string, int> d = slot.Kind == LootSlotKind.Trunk ? trunks : crates;
                d[slot.ZoneId ?? ""] = (d.TryGetValue(slot.ZoneId ?? "", out int n) ? n : 0) + 1;
                if (!keys.Add(slot.SlotKey ?? ""))
                {
                    sb.AppendLine($"  키 중복: {slot.SlotKey}");
                    ok = false;
                }
                if (!cfg.TryGetZone(slot.ZoneId, out _))
                {
                    sb.AppendLine($"  설정에 없는 구역: {slot.SlotKey} {slot.ZoneId}");
                    ok = false;
                }
                if (slot.AlwaysActive)
                    always++;
            }
            sb.Append(ZoneCounts("상자", crates, cfg, kindTrunk: false, out bool cratesOk));
            sb.Append(ZoneCounts("트렁크", trunks, cfg, kindTrunk: true, out bool trunksOk));
            ok &= cratesOk && trunksOk;
            int totalCrates = 0, totalTrunks = 0, expectedActive = always;
            foreach (LootZoneConfig z in cfg.zones)
            {
                int c = crates.TryGetValue(z.zoneId, out int cc) ? cc : 0, t = trunks.TryGetValue(z.zoneId, out int tt) ? tt : 0;
                totalCrates += c;
                totalTrunks += t;
            }
            expectedActive += ExpectedActive(AllSlots(scene), cfg);
            sb.AppendLine($"  합계: 상자 {totalCrates} + 트렁크 {totalTrunks} = {totalCrates + totalTrunks}, 항상 활성 {always}, 레이드당 활성 {expectedActive}");
            return sb.ToString();
        }

        // 항상 활성을 뺀 레이드당 활성 수 (구역·종류마다 ActiveCount — 표본 추출이라 레이드마다 같다)
        public static int ExpectedActive(List<LootSpawnSlot> slots, LootSpawnConfigSO cfg)
        {
            var groups = new Dictionary<(string, LootSlotKind), int>();
            foreach (LootSpawnSlot s in slots)
                if (!s.AlwaysActive)
                    groups[(s.ZoneId, s.Kind)] = (groups.TryGetValue((s.ZoneId, s.Kind), out int n) ? n : 0) + 1;
            int sum = 0;
            foreach (KeyValuePair<(string, LootSlotKind), int> g in groups)
                sum += LootActivationPlanner.ActiveCount(g.Value, cfg.RatioOf(g.Key.Item1, g.Key.Item2), cfg.minActivePerZone);
            return sum;
        }

        private static string ZoneCounts(string label, Dictionary<string, int> counts, LootSpawnConfigSO cfg, bool kindTrunk, out bool ok)
        {
            ok = true;
            var sb = new StringBuilder($"  {label}:");
            foreach (LootZoneConfig z in cfg.zones)
            {
                int have = counts.TryGetValue(z.zoneId, out int n) ? n : 0;
                int target = kindTrunk ? z.trunkTargetCount : z.crateTargetCount;
                sb.Append($" {z.zoneId} {have}/{target}");
                if (have != target)
                {
                    sb.Append("(!)");
                    ok = false;
                }
            }
            foreach (KeyValuePair<string, int> pair in counts)
                if (!cfg.TryGetZone(pair.Key, out _))
                {
                    sb.Append($" {pair.Key} {pair.Value}/설정 없음(!)");
                    ok = false;
                }
            return sb.AppendLine().ToString();
        }

        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
    }
}
