using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Ashfall.AI;
using Ashfall.Inventory;
using Ashfall.Raid;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using L = Ashfall.EditorTools.CrimsonBasinLayout;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // 맵 밀도를 레이드 씬에 적용 (docs/design/MapDensity_design.md §6.2·§6.3·§7·§8.1, ① 구조물·건물 + ② 산포). 저장은 호출자(MapDensitySetup·M4SceneSetup) 몫.
    // Environment 아래 세 그룹을 **모두 없을 때만** 만든다(하나라도 빠졌으면 셋 다 지우고 다시):
    //   MapDensity_Buildings: 속 찬 건물 "House_{nn}" (M4 블록아웃 생성기 — M6 칠하기·AssetSwap 스킨이 이름으로 붙는다) + 엄폐 지점
    //   MapDensity_Structures: 소형 구조물·차량 "{Shape}_{nn}" + 엄폐 지점
    //   MapDensity_Vegetation: 죽은 나무·덤불·통나무 프리팹 인스턴스(Prefabs/Scatter/SC_*)
    //   (MapDensity_Loot = ③ 상자·트렁크, 이 파일은 만들지 않는다)
    // 만들면: 배치 확인(겹침·회피·순찰 통로·보스 거점·저격 시선 3D) → NavMesh 다시 굽기 → 경로·엄폐·적·순찰(길이 +20% 이내)·상자·구멍·엄폐 상한 확인.
    // 실패 = 예외: 만든 그룹을 지우고 NavMesh·TerrainData를 적용 전 바이트로 되돌린 뒤 던진다(씬 저장 안 함).
    // 그룹이 이미 있으면 구조물 자리 NavMesh 구멍만 확인(아니면 예외 — Rebuild 메뉴).
    // 풀(TerrainData 디테일)·지형 설정·LOD 거리·그림자는 **매 실행** MapDensityConfigSO에서 다시 맞춘다(Refresh — 같으면 바꾸지 않는다).
    public static class MapDensitySceneApply
    {
        private const string Tag = "[MapDensitySceneApply]";

        public const string ConfigPath = M6Names.RaidDataFolder + "/MapDensityConfig.asset";
        private const float MaxPatrolPathIncrease = 0.2f;     // 설계 §7 2
        private const float DefaultFovDeg = 60f;
        private const float GrassObstacleMarginM = 1f;        // 건물 발자국 + 1m (설계 §3.1)
        private const float GrassCrateMarginM = 1.5f;         // 상자 접근 1.5m
        private const float GrassRoadMarginM = 1f;
        private const float TreeSinkM = 0.05f;

        private static readonly Regex VehicleName = new Regex(@"^Vehicle_\d+$");

        // M4 메뉴용: 맵 밀도 에셋(설정·산포 프리팹)이 있으면 적용, 없으면 조용히 건너뛴다(맵 개선까지만 쓰는 상황 호환).
        public static void ApplyRaidIfAssetsExist(Scene scene)
        {
            if (!AssetsExist())
                return;
            if (!ApplyRaid(scene))
                throw new InvalidOperationException($"{Tag} 맵 밀도 적용 실패: {scene.name} — 씬을 저장하지 않는다(위 오류 로그 확인)");
        }

        public static bool AssetsExist() =>
            AssetDatabase.LoadAssetAtPath<MapDensityConfigSO>(ConfigPath) != null && MapDensityPrefabs.AssetsExist();

        public static IEnumerable<string> GroupNames()
        {
            yield return MapDensityLayout.BuildingGroup;
            yield return MapDensityLayout.StructureGroup;
            yield return MapDensityLayout.VegetationGroup;
        }

        // Rebuild 메뉴: 세 그룹을 지운다(다음 ApplyRaid가 다시 만들고 다시 굽는다).
        public static void RemoveGroups(Scene scene)
        {
            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            if (environment == null)
                return;
            foreach (string name in GroupNames())
            {
                Transform group = environment.transform.Find(name);
                if (group == null)
                    continue;
                Object.DestroyImmediate(group.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log($"{Tag} 다시 만들기: {name} 지움");
            }
        }

        public static bool ApplyRaid(Scene scene)
        {
            if (!scene.IsValid())
                return false;
            var cfg = AssetDatabase.LoadAssetAtPath<MapDensityConfigSO>(ConfigPath);
            if (cfg == null)
                throw new InvalidOperationException($"{Tag} {ConfigPath} 없음 — 먼저 Ashfall/Setup/Apply Map Density");
            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            if (environment == null)
                throw new InvalidOperationException($"{Tag} {scene.name}: {M4MapSetup.EnvironmentName} 루트 없음 — 먼저 Create M4 Raid Scenes");
            Terrain terrain = environment.GetComponentInChildren<Terrain>();
            var surface = environment.GetComponent<NavMeshSurface>();
            if (terrain == null || surface == null)
                throw new InvalidOperationException($"{Tag} {scene.name}: Terrain 또는 NavMeshSurface 없음");

            var groups = new List<Transform>();
            foreach (string name in GroupNames())
                groups.Add(environment.transform.Find(name));
            if (groups.Contains(null))
            {
                foreach (Transform partial in groups)
                    if (partial != null)
                    {
                        Debug.LogWarning($"{Tag} 일부 그룹만 있다 — 지우고 다시 만든다: {partial.name}");
                        Object.DestroyImmediate(partial.gameObject);
                    }
                BuildOrRollBack(scene, environment, terrain, surface, cfg);
                return true;
            }

            // 그룹은 있는데 NavMesh가 옛것(구조물 자리에 구멍 없음)이면 굳은 상태 — 다시 만들기를 안내한다(맵 개선 리뷰 H2와 같다).
            var carvedReport = new StringBuilder();
            bool carved = true;
            foreach (Transform group in new[] { groups[0], groups[1] })
            {
                carved &= MapPlacementChecks.CheckCarved(group, terrain, out string report);
                carvedReport.Append(report);
            }
            if (!carved)
                throw new InvalidOperationException($"{Tag} {scene.name}: 맵 밀도 그룹은 있는데 NavMesh에 구조물이 반영되지 않았다 — " +
                                                    $"Ashfall/Setup/Map Density/Rebuild Map Density (Raid Scene)\n{carvedReport}");
            int changed = Refresh(scene, cfg, out string refreshReport);
            Debug.Log($"{Tag} {scene.name}: 맵 밀도 그룹 있음 — 배치·NavMesh 그대로 ({carvedReport.ToString().Trim()}), 풀·컬링 갱신 {changed}\n{refreshReport}");
            return true;
        }

        // ---------- 생성 ----------

        // 실패하면 만든 그룹을 지우고 NavMesh·TerrainData를 적용 전 바이트로 되돌린 뒤 다시 던진다 (설계 §7 5).
        private static void BuildOrRollBack(Scene scene, GameObject environment, Terrain terrain, NavMeshSurface surface, MapDensityConfigSO cfg)
        {
            string navPath = M4MapSetup.NavMeshPath;
            byte[] navBackup = MapPlacementChecks.ReadBytes(navPath);
            var created = new List<GameObject>();
            bool rebaked = false;
            try
            {
                Build(scene, environment, terrain, surface, cfg, created, ref rebaked);
            }
            catch
            {
                foreach (GameObject go in created)
                    if (go != null)
                        Object.DestroyImmediate(go);
                if (rebaked && navBackup != null)
                    MapPlacementChecks.RestoreNavMesh(surface, navPath, navBackup);
                Debug.LogError($"{Tag} 실패 — 만든 그룹 {created.Count}개를 지우고{(rebaked ? " NavMesh를 되돌렸다" : "")}");
                throw;
            }
            // 풀(TerrainData)·LOD·그림자는 배치·확인이 모두 성공한 뒤에만 맞춘다(리뷰 M3) — Build는 TerrainData를 건드리지 않으므로 되돌릴 TerrainData가 없다.
            // Refresh가 도중에 던지면 씬은 저장되지 않고(호출자), 메모리의 TerrainData는 저장되지 않은 채 배치 프로세스와 함께 버려진다.
            // 산포 프리팹 LOD·그림자 갱신은 SO에서 다시 계산되는 파생값이라 되돌리지 않는다(다음 실행이 같은 값으로 맞춘다).
            int changed = Refresh(scene, cfg, out string refreshReport);
            Debug.Log($"{Tag} 풀·컬링·그림자 맞춤 {changed}\n{refreshReport}");
        }

        private static void Build(Scene scene, GameObject environment, Terrain terrain, NavMeshSurface surface, MapDensityConfigSO cfg,
            List<GameObject> created, ref bool rebaked)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            MapPlacementChecks.NavState before = MapPlacementChecks.NavState.Capture(scene, null);
            Physics.SyncTransforms();

            // ① 구조물·건물
            MapDensityLayout.SiteContext ctx = GatherContext(scene, terrain);
            List<MapDensityLayout.StructurePlan> plans = MapDensityLayout.GenerateStructures(ctx, out string structureReport);
            Debug.Log($"{Tag} 입력: 콜라이더 발자국 {ctx.ObstacleCount}, 지점 {ctx.PointCount}, 순찰 구간 {ctx.PatrolSegments.Count}, 경로 통로 선분 {ctx.CorridorCount}\n구조물 생성:\n{structureReport}");
            CheckCounts(plans, structureReport);
            if (!MapDensityLayout.CheckStructures(plans, ctx, out string planReport))
                throw new InvalidOperationException($"{Tag} 구조물 배치 데이터 검사 실패\n{planReport}");

            var builder = new M4MapSetup.BlockoutBuilder(terrain, environment.transform);
            builder.Group(MapDensityLayout.BuildingGroup, MapDensityLayout.PiecesOf(plans, buildings: true));
            Transform buildingGroup = environment.transform.Find(MapDensityLayout.BuildingGroup);
            created.Add(buildingGroup.gameObject);
            builder.Group(MapDensityLayout.StructureGroup, MapDensityLayout.PiecesOf(plans, buildings: false));
            Transform structureGroup = environment.transform.Find(MapDensityLayout.StructureGroup);
            created.Add(structureGroup.gameObject);
            var unknown = new List<string>();
            Dictionary<PaletteMaterialMap.Key, Material> materials = M6VisualSceneApply.LoadPaletteMaterials();
            M6VisualSceneApply.PaintGroup(buildingGroup, materials, unknown);
            M6VisualSceneApply.PaintGroup(structureGroup, materials, unknown);
            if (unknown.Count > 0)
                throw new InvalidOperationException($"{Tag} 칠하지 못한 구조물(PaletteMaterialMap 규칙 없음): {string.Join(", ", unknown)}");

            Physics.SyncTransforms();
            var newCovers = new List<CoverPoint>();
            newCovers.AddRange(buildingGroup.GetComponentsInChildren<CoverPoint>(true));
            newCovers.AddRange(structureGroup.GetComponentsInChildren<CoverPoint>(true));
            int generatedCovers = newCovers.Count;
            int removedInside = MapPlacementChecks.RemoveCoversInsideColliders(newCovers, Tag);
            int removedSpacing = ThinCovers(scene, newCovers, new[] { buildingGroup, structureGroup }, cfg.newCoverMinSpacingM);

            // ② 산포 (새 구조물 발자국·새 엄폐 지점을 피한다)
            foreach (MapDensityLayout.StructurePlan p in plans)
                ctx.AddObstacle(p.Footprint);
            foreach (CoverPoint cover in newCovers)
                ctx.AddPoint($"엄폐 {cover.transform.parent.name}/{cover.name}", XZ(cover.transform.position), MapPolishLayout.MinDistanceFromPointsM);
            var settings = new MapDensityLayout.ScatterSettings(cfg.treeCount, cfg.bushCount, cfg.logCount, cfg.treeMinSpacingM, cfg.treeGroveNoiseScaleM,
                cfg.treeGroveThreshold, cfg.treeScatterShare, cfg.treeMaxSlopeDeg, cfg.treeScaleRange, MapDensityPrefabs.TreeTrunkRadii(), MapDensityPrefabs.LogLengthM);
            List<MapDensityLayout.ScatterPlan> scatter = MapDensityLayout.GenerateScatter(ctx, settings, plans, out string scatterReport);
            Debug.Log($"{Tag} 산포 생성:\n{scatterReport}");
            int trees = Count(scatter, MapDensityLayout.ScatterKind.Tree), bushes = Count(scatter, MapDensityLayout.ScatterKind.Bush),
                logs = Count(scatter, MapDensityLayout.ScatterKind.Log);
            if (trees != cfg.treeCount || bushes != cfg.bushCount || logs != cfg.logCount)
                throw new InvalidOperationException($"{Tag} 산포 개수 부족 (나무 {trees}/{cfg.treeCount}, 덤불 {bushes}/{cfg.bushCount}, 통나무 {logs}/{cfg.logCount})\n{scatterReport}");
            var vegetation = new GameObject(MapDensityLayout.VegetationGroup).transform;
            vegetation.SetParent(environment.transform, false);
            created.Add(vegetation.gameObject);
            PlaceScatter(vegetation, terrain, scatter);

            // NavMesh: 그룹 루트 Not Walkable(윗면 섬 없음) + 속 찬 상자 내부 막기
            var newRoots = new[] { buildingGroup, structureGroup, vegetation };
            int notWalkable = NavMesh.GetAreaFromName(MapPlacementChecks.NotWalkableArea);
            foreach (Transform root in newRoots)
                MapPlacementChecks.MarkNotWalkable(root, notWalkable);
            int navBlocks = MapPlacementChecks.AddNavBlocks(new[] { buildingGroup, structureGroup }, notWalkable);

            Physics.SyncTransforms();
            var problems = new StringBuilder();
            var rules = new MapPlacementChecks.PlacementRules
            {
                PatrolSegmentClearM = MapDensityLayout.PatrolSegmentClearM,
                KeepOutCenter = MapDensityLayout.BossCenter(),
                KeepOutRadiusM = MapDensityLayout.BossKeepOutM,
                KeepOutName = "보스 거점",
            };
            MapPlacementChecks.CheckPlacement(scene, newRoots, terrain, problems, rules);
            CheckCoverCaps(before.CoverTotal, newCovers.Count, cfg, problems);
            if (problems.Length > 0)
                throw new InvalidOperationException($"{Tag} 배치 확인 실패 (씬 저장 안 함)\n{problems}");
            double placeSeconds = watch.Elapsed.TotalSeconds;

            rebaked = true;
            if (!M4MapSetup.RebakeNavMesh(surface, M4MapSetup.NavMeshPath, out double bakeSeconds))
                throw new InvalidOperationException($"{Tag} NavMesh 다시 굽기 실패");

            int removedOffMesh = MapPlacementChecks.RemoveCoversOffMesh(newCovers, before.Reference, Tag);
            MapPlacementChecks.NavState after = MapPlacementChecks.NavState.Capture(scene, before);
            var raidConfig = AssetDatabase.LoadAssetAtPath<RaidConfigSO>(M4MapSetup.DefaultRaidConfigPath);
            bool pathsOk = M4MapSetup.ValidateNavigation(raidConfig, out string navReport);
            string regressions = after.Regressions(before);
            string longer = after.PathLengthRegressions(before, MaxPatrolPathIncrease, out float maxIncrease);
            var carvedReport = new StringBuilder();
            bool carved = true;
            foreach (Transform group in new[] { buildingGroup, structureGroup })
            {
                carved &= MapPlacementChecks.CheckCarved(group, terrain, out string report);
                carvedReport.Append(report);
            }
            var caps = new StringBuilder();
            CheckCoverCaps(before.CoverTotal, newCovers.Count, cfg, caps);
            if (!pathsOk || regressions.Length > 0 || longer.Length > 0 || !carved || caps.Length > 0)
                throw new InvalidOperationException($"{Tag} 굽기 뒤 확인 실패 (씬 저장 안 함)\n{navReport}{regressions}{longer}{(carved ? "" : carvedReport.ToString())}{caps}" +
                                                    DescribeBlockers(scene, newRoots) + DescribeCuts(newRoots));

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{Tag} {scene.name}: 적용 완료 (배치 {placeSeconds:0.0}s, NavMesh 굽기 {bakeSeconds:0.0}s)\n" +
                      $"{structureReport}{scatterReport}" +
                      $"  새 엄폐 지점 {newCovers.Count}(생성 {generatedCovers} − 콜라이더 안 {removedInside} − 간격 {cfg.newCoverMinSpacingM}m {removedSpacing} − NavMesh 밖·도달 불가 {removedOffMesh}), " +
                      $"전체 {before.CoverTotal + newCovers.Count}/{cfg.maxTotalCoverPoints}, 내부 막기 볼륨 {navBlocks}, 순찰 경로 최대 증가 {maxIncrease * 100f:0.0}%\n" +
                      $"  굽기 전: {before.Summary}\n  굽기 뒤: {after.Summary}\n{carvedReport}{navReport}");
        }

        // 실패 진단: 끊긴 경로(스폰 → 탈출, 기준 스폰 → 적·상자)의 끝 지점과 그 근처 새 콜라이더
        private static string DescribeBlockers(Scene scene, Transform[] newRoots)
        {
            var sb = new StringBuilder("  끊긴 경로 진단:\n");
            var spawns = new List<Vector3>();
            var targets = new List<(string name, Vector3 p)>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PlayerSpawnPoint s in root.GetComponentsInChildren<PlayerSpawnPoint>(true))
                    spawns.Add(s.transform.position);
                foreach (ExtractionZone z in root.GetComponentsInChildren<ExtractionZone>(true))
                    targets.Add(($"탈출 {z.Id}", z.transform.position));
                foreach (EnemyAgent e in root.GetComponentsInChildren<EnemyAgent>(true))
                    targets.Add((e.name, e.transform.position));
            }
            var colliders = new List<Collider>();
            foreach (Transform r in newRoots)
                colliders.AddRange(r.GetComponentsInChildren<Collider>(true));
            var path = new NavMeshPath();
            var seen = new HashSet<Vector2Int>();
            foreach (Vector3 spawn in spawns)
                foreach (var t in targets)
                {
                    if (!NavMesh.SamplePosition(spawn, out NavMeshHit a, 2f, NavMesh.AllAreas) || !NavMesh.SamplePosition(t.p, out NavMeshHit b, 2f, NavMesh.AllAreas))
                        continue;
                    if (M4MapSetup.TryPath(a.position, b.position, path) || path.corners.Length == 0)
                        continue;
                    Vector3 end = path.corners[path.corners.Length - 1];
                    if (!seen.Add(new Vector2Int(Mathf.RoundToInt(end.x / 10f), Mathf.RoundToInt(end.z / 10f))))
                        continue;
                    var near = new List<string>();
                    foreach (Collider c in colliders)
                        if (Vector2.Distance(XZ(c.bounds.ClosestPoint(end)), XZ(end)) < 12f)
                            near.Add($"{MapPlacementChecks.Owner(c)}({c.transform.position.x:0},{c.transform.position.z:0})");
                    sb.AppendLine($"    {spawn.x:0},{spawn.z:0} → {t.name}: 끝 ({end.x:0.0}, {end.y:0.0}, {end.z:0.0}) 근처 새 콜라이더 {near.Count}: {string.Join(", ", near.GetRange(0, Mathf.Min(12, near.Count)))}");
                }
            return sb.ToString();
        }

        private static void CheckCounts(List<MapDensityLayout.StructurePlan> plans, string report)
        {
            int b = 0, s = 0, car = 0, big = 0;
            foreach (MapDensityLayout.StructurePlan p in plans)
                switch (p.Category)
                {
                    case MapDensityLayout.Category.Building: b++; break;
                    case MapDensityLayout.Category.SmallStructure: s++; break;
                    case MapDensityLayout.Category.Car: car++; break;
                    default: big++; break;
                }
            if (b != MapDensityLayout.BuildingTarget || s != MapDensityLayout.SmallStructureTarget || car != MapDensityLayout.CarTarget
                || big != MapDensityLayout.LargeVehicleTarget)
                throw new InvalidOperationException($"{Tag} 구조물 개수가 목표와 다르다 — 자리 부족(배치 데이터 조정 필요)\n{report}");
        }

        private static void CheckCoverCaps(int existing, int added, MapDensityConfigSO cfg, StringBuilder problems)
        {
            if (added > cfg.maxNewCoverPoints)
                problems.AppendLine($"  새 엄폐 지점 {added} > 상한 {cfg.maxNewCoverPoints}");
            if (existing + added > cfg.maxTotalCoverPoints)
                problems.AppendLine($"  전체 엄폐 지점 {existing + added} > 상한 {cfg.maxTotalCoverPoints} (CoverSelector 선형 탐색 — 설계 §7 4)");
        }

        private static int Count(List<MapDensityLayout.ScatterPlan> plans, MapDensityLayout.ScatterKind kind)
        {
            int n = 0;
            foreach (MapDensityLayout.ScatterPlan p in plans)
                if (p.Kind == kind)
                    n++;
            return n;
        }

        // 새 엄폐 지점끼리·기존 지점과 spacing보다 가까우면 뺀다(계층 순서 — 결정적). 뺀 수.
        private static int ThinCovers(Scene scene, List<CoverPoint> covers, Transform[] newRoots, float spacing)
        {
            if (spacing <= 0f)
                return 0;
            var kept = new List<Vector3>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (CoverPoint cover in root.GetComponentsInChildren<CoverPoint>(true))
                    if (!MapPlacementChecks.IsUnder(cover.transform, newRoots))
                        kept.Add(cover.transform.position);
            int removed = 0;
            for (int i = 0; i < covers.Count; i++)
            {
                Vector3 p = covers[i].transform.position;
                bool close = false;
                foreach (Vector3 k in kept)
                    if ((XZ(k) - XZ(p)).sqrMagnitude < spacing * spacing)
                    {
                        close = true;
                        break;
                    }
                if (!close)
                {
                    kept.Add(p);
                    continue;
                }
                Object.DestroyImmediate(covers[i].gameObject);
                covers.RemoveAt(i);
                i--;
                removed++;
            }
            return removed;
        }

        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);

        // 산포 프리팹 인스턴스 (그룹 바로 아래, 이름 = 프리팹 이름 그대로 — 씬 파일 변경 최소)
        private static void PlaceScatter(Transform group, Terrain terrain, List<MapDensityLayout.ScatterPlan> plans)
        {
            var prefabs = new Dictionary<string, GameObject>();
            GameObject Prefab(string path)
            {
                if (!prefabs.TryGetValue(path, out GameObject prefab))
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                        throw new InvalidOperationException($"{Tag} 산포 프리팹 없음: {path} — 먼저 Ashfall/Setup/Apply Map Density");
                    prefabs[path] = prefab;
                }
                return prefab;
            }
            foreach (MapDensityLayout.ScatterPlan p in plans)
            {
                string path = p.Kind == MapDensityLayout.ScatterKind.Tree ? MapDensityPrefabs.TreePrefabPath(p.Variant)
                    : p.Kind == MapDensityLayout.ScatterKind.Bush ? MapDensityPrefabs.BushPrefabPath(p.Variant) : MapDensityPrefabs.LogPrefabPath;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(path), group.gameObject.scene);
                go.transform.SetParent(group, false);
                Quaternion yaw = Quaternion.Euler(0f, p.YawDeg, 0f);
                if (p.Kind == MapDensityLayout.ScatterKind.Log)
                {
                    // 양 끝 지면에 맞춰 기울인다 (통나무 축 = 로컬 x)
                    Vector3 axis = yaw * Vector3.right * (MapDensityPrefabs.LogLengthM * 0.5f);
                    float y1 = M4MapSetup.GroundY(terrain, p.XZ.x + axis.x, p.XZ.y + axis.z);
                    float y0 = M4MapSetup.GroundY(terrain, p.XZ.x - axis.x, p.XZ.y - axis.z);
                    float pitch = Mathf.Atan2(y1 - y0, MapDensityPrefabs.LogLengthM) * Mathf.Rad2Deg;
                    go.transform.SetPositionAndRotation(new Vector3(p.XZ.x, (y0 + y1) * 0.5f, p.XZ.y), yaw * Quaternion.Euler(0f, 0f, pitch));
                }
                else
                {
                    // 줄기·밑동 둘레의 가장 낮은 지면 — 경사 쪽 틈이 없게
                    float r = p.Kind == MapDensityLayout.ScatterKind.Tree ? MapDensityPrefabs.Trees[p.Variant].TrunkRadiusM * p.Scale * 1.5f : 0.3f;
                    float y = M4MapSetup.GroundY(terrain, p.XZ.x, p.XZ.y);
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 o = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * r;
                        y = Mathf.Min(y, M4MapSetup.GroundY(terrain, p.XZ.x + o.x, p.XZ.y + o.z));
                    }
                    go.transform.SetPositionAndRotation(new Vector3(p.XZ.x, y - TreeSinkM, p.XZ.y), yaw);
                    go.transform.localScale = Vector3.one * p.Scale;
                }
            }
        }

        // ---------- 입력 수집 ----------

        // 씬의 기존 콜라이더 발자국·지점·순찰 구간·저격 시선·보스 거점 + 지형 샘플러
        internal static MapDensityLayout.SiteContext GatherContext(Scene scene, Terrain terrain)
        {
            var ctx = new MapDensityLayout.SiteContext
            {
                GroundY = xz => M4MapSetup.GroundY(terrain, xz.x, xz.y),
                SlopeDeg = xz => Steepness(terrain, xz),
                BossCenter = MapDensityLayout.BossCenter(),
            };
            ctx.SightLines.AddRange(MapPolishLayout.SniperSightLines());
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                    if (!c.isTrigger && !(c is TerrainCollider))
                        ctx.AddObstacle(FootprintOf(c));
                foreach (CoverPoint cover in root.GetComponentsInChildren<CoverPoint>(true))
                    ctx.AddPoint($"엄폐 {cover.transform.parent?.name}/{cover.name}", XZ(cover.transform.position), MapPolishLayout.MinDistanceFromPointsM);
                foreach (LootContainer crate in root.GetComponentsInChildren<LootContainer>(true))
                    ctx.AddPoint($"상자 {crate.name}", XZ(crate.transform.position), MapPolishLayout.MinDistanceFromPointsM);
                foreach (EnemyAgent enemy in root.GetComponentsInChildren<EnemyAgent>(true))
                    ctx.AddPoint($"적 {enemy.name}", XZ(enemy.transform.position), MapPolishLayout.MinDistanceFromPointsM);
                foreach (PatrolRoute route in root.GetComponentsInChildren<PatrolRoute>(true))
                {
                    IReadOnlyList<Vector3> points = route.Points;
                    foreach (Vector3 p in points)
                        ctx.AddPoint($"순찰 {route.name}", XZ(p), MapPolishLayout.MinDistanceFromPointsM);
                    if (points.Count >= 2)
                        for (int i = 0; i < points.Count; i++)
                            ctx.PatrolSegments.Add((XZ(points[i]), XZ(points[(i + 1) % points.Count])));
                }
                foreach (PlayerSpawnPoint spawn in root.GetComponentsInChildren<PlayerSpawnPoint>(true))
                    ctx.AddPoint($"스폰 {spawn.Id}", XZ(spawn.transform.position), MapPolishLayout.MinDistanceFromSpawnExtM);
                foreach (ExtractionZone zone in root.GetComponentsInChildren<ExtractionZone>(true))
                    ctx.AddPoint($"탈출 {zone.Id}", XZ(zone.transform.position), MapPolishLayout.MinDistanceFromSpawnExtM);
                foreach (PowerSwitch powerSwitch in root.GetComponentsInChildren<PowerSwitch>(true))
                    ctx.AddPoint("전원 스위치", XZ(powerSwitch.transform.position), MapPolishLayout.MinDistanceFromSwitchM);
            }
            LastCorridorPaths.Clear();
            LastGridCoverage = GridCoverage() + $" / 삼각형 {NavMesh.CalculateTriangulation().indices.Length / 3}";
            AddPathCorridors(scene, ctx);
            return ctx;
        }

        // 지금 NavMesh의 경로(스폰마다 → 모든 탈출구, 기준 스폰 → 적·상자, 순찰 지점 사이)를 통로로 보존한다. 굽기 전에 부른다.
        private static void AddPathCorridors(Scene scene, MapDensityLayout.SiteContext ctx)
        {
            var spawns = new List<Vector3>();
            var targets = new List<Vector3>();
            var exits = new List<Vector3>();
            var legs = new List<(Vector3 a, Vector3 b)>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PlayerSpawnPoint spawn in root.GetComponentsInChildren<PlayerSpawnPoint>(true))
                    spawns.Add(spawn.transform.position);
                foreach (ExtractionZone zone in root.GetComponentsInChildren<ExtractionZone>(true))
                    exits.Add(zone.transform.position);
                foreach (EnemyAgent enemy in root.GetComponentsInChildren<EnemyAgent>(true))
                    targets.Add(enemy.transform.position);
                foreach (LootContainer crate in root.GetComponentsInChildren<LootContainer>(true))
                    targets.Add(crate.transform.position);
                foreach (PowerSwitch powerSwitch in root.GetComponentsInChildren<PowerSwitch>(true))
                    targets.Add(powerSwitch.transform.position);
                foreach (PatrolRoute route in root.GetComponentsInChildren<PatrolRoute>(true))
                {
                    IReadOnlyList<Vector3> points = route.Points;
                    if (points.Count >= 2)
                        for (int i = 0; i < points.Count; i++)
                            legs.Add((points[i], points[(i + 1) % points.Count]));
                }
            }
            foreach (Vector3 spawn in spawns)
                foreach (Vector3 exit in exits)
                    legs.Add((spawn, exit));
            if (spawns.Count > 0)
                foreach (Vector3 target in targets)
                    legs.Add((spawns[0], target));
            var path = new NavMeshPath();
            foreach ((Vector3 a, Vector3 b) in legs)
            {
                if (!NavMesh.SamplePosition(a, out NavMeshHit ha, MapPlacementChecks.AgentOnMeshM, NavMesh.AllAreas)
                    || !NavMesh.SamplePosition(b, out NavMeshHit hb, MapPlacementChecks.AgentOnMeshM, NavMesh.AllAreas))
                    continue;
                var list = new List<Vector3>();
                bool complete = M4MapSetup.TryPath(ha.position, hb.position, path, out _, out _, list);
                for (int i = 1; i < list.Count; i++)
                    ctx.AddCorridor(XZ(list[i - 1]), XZ(list[i]));
                if (complete && list.Count > 0)
                    LastCorridorPaths.Add(list.ToArray());
            }
        }

        // 굽기 전 경로 (실패 진단용 — 새 NavMesh에서 어디서 끊겼는지 찾는다)
        private static readonly List<Vector3[]> LastCorridorPaths = new List<Vector3[]>();
        private static string LastGridCoverage = "";

        // 25m 격자 지면 점 중 NavMesh 1.5m 안에 있는 수 (진단)
        private static string GridCoverage()
        {
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();
            int total = 0, on = 0;
            for (float x = -450f; x <= 450f; x += 25f)
            for (float z = -450f; z <= 450f; z += 25f)
            {
                total++;
                if (NavMesh.SamplePosition(M4MapSetup.GroundPoint(terrain, new Vector2(x, z)), out NavMeshHit _, 1.5f, NavMesh.AllAreas))
                    on++;
            }
            return $"{on}/{total}";
        }

        // 굽기 전 경로를 새 NavMesh 위에서 2m 간격으로 따라가며 시작점에서 처음 닿지 않는 자리와 그 근처 새 콜라이더를 보고한다.
        private static string DescribeCuts(Transform[] newRoots)
        {
            var sb = new StringBuilder($"  NavMesh 격자 표본(25m): 굽기 전 {LastGridCoverage}, 뒤 {GridCoverage()} / 삼각형 {NavMesh.CalculateTriangulation().indices.Length / 3}\n");
            sb.AppendLine("  끊긴 자리(굽기 전 경로 기준):");
            var colliders = new List<Collider>();
            foreach (Transform r in newRoots)
                colliders.AddRange(r.GetComponentsInChildren<Collider>(true));
            var path = new NavMeshPath();
            var reported = new HashSet<Vector2Int>();
            foreach (Vector3[] corners in LastCorridorPaths)
            {
                if (!NavMesh.SamplePosition(corners[0], out NavMeshHit start, 2f, NavMesh.AllAreas))
                    continue;
                NavMesh.SamplePosition(corners[corners.Length - 1], out NavMeshHit goal, 2f, NavMesh.AllAreas);
                if (M4MapSetup.TryPath(start.position, goal.position, path))
                    continue;
                bool found = false;
                for (int i = 1; i < corners.Length && !found; i++)
                {
                    float length = Vector3.Distance(corners[i - 1], corners[i]);
                    for (float d = 0f; d <= length && !found; d += 2f)
                    {
                        Vector3 p = Vector3.Lerp(corners[i - 1], corners[i], length > 0f ? d / length : 0f);
                        bool ok = NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)
                                  && M4MapSetup.TryPath(start.position, hit.position, path);
                        if (ok)
                            continue;
                        found = true;
                        if (!reported.Add(new Vector2Int(Mathf.RoundToInt(p.x / 4f), Mathf.RoundToInt(p.z / 4f))))
                            break;
                        var near = new List<string>();
                        foreach (Collider c in colliders)
                        {
                            float dist = Vector2.Distance(XZ(c.bounds.ClosestPoint(p)), XZ(p));
                            if (dist < 6f)
                                near.Add($"{MapPlacementChecks.Owner(c)}({c.transform.position.x:0.0},{c.transform.position.z:0.0}) {dist:0.0}m");
                        }
                        sb.AppendLine($"    ({p.x:0.0}, {p.y:0.0}, {p.z:0.0}) NavMesh {(NavMesh.SamplePosition(p, out NavMeshHit _, 0.8f, NavMesh.AllAreas) ? "있음(도달 불가)" : "없음")}, 6m 안 새 콜라이더: {string.Join(", ", near)}");
                    }
                }
            }
            return sb.ToString();
        }

        // 콜라이더의 수평 발자국 (회전 상자를 수평으로 투영 — 기울어진 상자는 투영을 감싼다, 보수적)
        internal static Footprint FootprintOf(Collider c)
        {
            MapPlacementChecks.Obb obb = MapPlacementChecks.Obb.Of(c);
            Vector3 right = obb.Rotation * Vector3.right;
            var dir = new Vector2(right.x, right.z);
            if (dir.sqrMagnitude < 0.04f)
            {
                Vector3 fwd = obb.Rotation * Vector3.forward;
                dir = new Vector2(fwd.z, -fwd.x);   // 앞 방향을 90° 돌려 x축으로
            }
            float yaw = dir.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
            var frame = new Footprint(XZ(obb.Center), Vector2.zero, yaw);
            Vector2 half = Vector2.zero;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                Vector3 w = obb.Center + obb.Rotation * Vector3.Scale(corner, obb.HalfExtents);
                Vector2 local = frame.ToLocal(XZ(w));
                half = new Vector2(Mathf.Max(half.x, Mathf.Abs(local.x)), Mathf.Max(half.y, Mathf.Abs(local.y)));
            }
            return new Footprint(frame.Center, half, yaw);
        }

        private static float Steepness(Terrain terrain, Vector2 xz)
        {
            Vector3 local = new Vector3(xz.x, 0f, xz.y) - terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            return terrain.terrainData.GetSteepness(local.x / size.x, local.z / size.z);
        }

        // ---------- 풀·컬링·그림자 (매 실행, SO 값 — Refresh 메뉴) ----------

        // 바꾼 항목 수. 씬 오브젝트가 바뀌면 씬을 더럽힌다(저장은 호출자).
        public static int Refresh(Scene scene, MapDensityConfigSO cfg, out string report)
        {
            var sb = new StringBuilder();
            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            Terrain terrain = environment != null ? environment.GetComponentInChildren<Terrain>() : null;
            if (terrain == null)
                throw new InvalidOperationException($"{Tag} {scene.name}: Environment/Terrain 없음");
            float fov = PlayerFov(scene);
            float bias = QualitySettings.lodBias;

            int prefabChanges = MapDensityPrefabs.RefreshPrefabs(cfg, fov, bias);
            sb.AppendLine($"  LOD 기준: 카메라 FOV {fov:0.#}°, lodBias {bias:0.##} (품질 단계 {QualitySettings.names[QualitySettings.GetQualityLevel()]}), 산포 프리팹 갱신 {prefabChanges}");

            int sceneChanges = 0;
            int culled = 0, unshadowed = 0;
            foreach (string name in new[] { MapDensityLayout.BuildingGroup, MapDensityLayout.StructureGroup })
            {
                Transform group = environment.transform.Find(name);
                if (group == null)
                    continue;
                foreach (Transform structure in group)
                {
                    float distance = CullDistanceOf(structure, name == MapDensityLayout.BuildingGroup, cfg);
                    float height = HeightAboveGround(structure, terrain);
                    bool shadows = height >= cfg.shadowMinHeightM;
                    if (!shadows)
                        unshadowed++;
                    sceneChanges += EnsureCullLod(structure, distance, shadows, fov, bias);
                    culled++;
                }
            }
            sb.AppendLine($"  구조물 컬 LODGroup {culled}(그림자 끔 {unshadowed}, 높이 < {cfg.shadowMinHeightM}m), 갱신 {sceneChanges}");

            int terrainChanges = RefreshGrass(scene, environment, terrain, cfg, sb);
            if (sceneChanges > 0 || terrainChanges > 0)
                EditorSceneManager.MarkSceneDirty(scene);
            report = sb.ToString();
            return prefabChanges + sceneChanges + terrainChanges;
        }

        // 플레이어 카메라(MainCamera 태그) FOV, 없으면 60°
        private static float PlayerFov(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                    if (camera.CompareTag("MainCamera"))
                        return camera.fieldOfView;
            return DefaultFovDeg;
        }

        private static float CullDistanceOf(Transform structure, bool building, MapDensityConfigSO cfg)
        {
            if (building)
                return cfg.buildingCullDistanceM;
            if (VehicleName.IsMatch(structure.name))
            {
                Transform block = structure.Find("Block");
                bool car = block != null && Mathf.Max(block.lossyScale.x, block.lossyScale.z) <= MapDensityLayout.PassengerCarMaxLengthM;
                return car ? cfg.carCullDistanceM : cfg.buildingCullDistanceM;   // 큰 차량(버스·트럭·전차 잔해)은 건물 거리
            }
            return cfg.smallStructureCullDistanceM;
        }

        // 구조물 높이 = 블록아웃 윗면 − 중심 지면 (스킨 제외)
        private static float HeightAboveGround(Transform structure, Terrain terrain)
        {
            float top = float.MinValue;
            foreach (Renderer r in structure.GetComponentsInChildren<Renderer>(true))
                if (!AssetSwapApply.UnderSkin(r.transform, structure))
                    top = Mathf.Max(top, r.bounds.max.y);
            return top == float.MinValue ? 0f : top - M4MapSetup.GroundY(terrain, structure.position.x, structure.position.z);
        }

        // 구조물 래퍼의 컬 전용 LODGroup (LOD 하나 = 블록아웃 + 스킨 렌더러 전부, 거리 = 실제 m). 블록아웃 그림자는 높이 정책.
        // 바뀐 수 (0/1). AssetSwap이 스킨을 얹은 뒤에도 같은 함수(IncludeRenderers)가 스킨 렌더러를 넣는다.
        internal static int EnsureCullLod(Transform structure, float distanceM, bool shadows, float fovDeg, float lodBias)
        {
            bool changed = false;
            ShadowCastingMode mode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            foreach (Renderer r in structure.GetComponentsInChildren<Renderer>(true))
                if (!AssetSwapApply.UnderSkin(r.transform, structure) && r.shadowCastingMode != mode)
                {
                    r.shadowCastingMode = mode;
                    EditorUtility.SetDirty(r);
                    changed = true;
                }
            var group = structure.GetComponent<LODGroup>();
            if (group == null)
            {
                group = structure.gameObject.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.CrossFade;
                group.animateCrossFading = true;
                group.SetLODs(new[] { new LOD(0.01f, structure.GetComponentsInChildren<Renderer>(true)) });
                group.RecalculateBounds();
                changed = true;
            }
            changed |= IncludeRenderers(group);
            changed |= MapDensityPrefabs.ApplyLods(group, new[] { distanceM }, new[] { shadows }, fovDeg, lodBias, setShadows: false);
            return changed ? 1 : 0;
        }

        // LOD 하나짜리 컬 LODGroup의 렌더러 = 그 아래 렌더러 전부 (스킨 포함). 바뀌었으면 true. AssetSwapApply가 스킨을 얹은 뒤 부른다.
        public static bool IncludeRenderers(LODGroup group)
        {
            if (group == null)
                return false;
            LOD[] lods = group.GetLODs();
            if (lods.Length != 1)
                return false;
            Renderer[] wanted = group.GetComponentsInChildren<Renderer>(true);
            Renderer[] have = lods[0].renderers ?? Array.Empty<Renderer>();
            bool same = wanted.Length == have.Length;
            for (int i = 0; same && i < wanted.Length; i++)
                same = wanted[i] == have[i];
            if (same)
                return false;
            lods[0].renderers = wanted;
            group.SetLODs(lods);   // 크기(size)는 만들 때의 블록아웃 기준 그대로 — 스킨을 얹어도 전환 거리가 흔들리지 않게(메뉴 순서와 무관하게 멱등)
            EditorUtility.SetDirty(group);
            return true;
        }

        // ---------- 풀 (Terrain Detail, GPU 인스턴싱) ----------

        private static int RefreshGrass(Scene scene, GameObject environment, Terrain terrain, MapDensityConfigSO cfg, StringBuilder sb)
        {
            TerrainData data = terrain.terrainData;
            int changes = 0;
            // 지형 컴포넌트 (씬)
            if (!terrain.drawTreesAndFoliage || !Mathf.Approximately(terrain.detailObjectDistance, cfg.grassDistanceM)
                || !Mathf.Approximately(terrain.detailObjectDensity, 1f))
            {
                terrain.drawTreesAndFoliage = true;
                terrain.detailObjectDistance = cfg.grassDistanceM;
                terrain.detailObjectDensity = 1f;
                EditorUtility.SetDirty(terrain);
                changes++;
            }

            // TerrainData (에셋 — 계산한 값이 파일과 다를 때만 쓴다)
            int res = Mathf.Max(32, cfg.detailResolution);
            int perPatch = Mathf.Clamp(cfg.detailResolutionPerPatch, 8, 128);
            bool dataChanged = false;
            if (data.detailScatterMode != DetailScatterMode.InstanceCountMode)
            {
                data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
                dataChanged = true;
            }
            if (data.detailResolution != res || data.detailResolutionPerPatch != perPatch)
            {
                data.SetDetailResolution(res, perPatch);
                dataChanged = true;
            }
            DetailPrototype[] wanted = GrassPrototypes(cfg);
            DetailPrototype[] have = data.detailPrototypes;
            bool sameProtos = have.Length == wanted.Length;
            for (int i = 0; sameProtos && i < wanted.Length; i++)
                sameProtos = SamePrototype(have[i], wanted[i]);
            if (!sameProtos)
            {
                data.detailPrototypes = wanted;
                dataChanged = true;
            }

            bool[,] excluded = GrassExclusion(scene, environment, terrain, res);
            Vector3 size = data.size;
            float cellArea = size.x / res * (size.z / res);
            GrassLayers(excluded, cellArea, cfg.grassCoverage, cfg.grassMaxPerM2, size.x / res, cfg.grassNoiseScaleM, MapDensityLayout.GrassSeed,
                out int[,] shortLayer, out int[,] tallLayer, out int covered, out long instances);
            int[][,] layers = { shortLayer, tallLayer };
            int layerWrites = 0;
            for (int layer = 0; layer < layers.Length; layer++)
            {
                int[,] existing = data.GetDetailLayer(0, 0, res, res, layer);
                if (SameLayer(existing, layers[layer]))
                    continue;
                data.SetDetailLayer(0, 0, layer, layers[layer]);
                dataChanged = true;
                layerWrites++;
            }
            if (dataChanged)
            {
                EditorUtility.SetDirty(data);
                changes++;
            }
            int walkable = 0;
            foreach (bool e in excluded)
                if (!e)
                    walkable++;
            sb.AppendLine($"  풀: 디테일 {res}×{res}(패치 {perPatch}), 층 2, 덮은 셀 {covered}/{walkable}(걷는 지형의 {(walkable > 0 ? covered * 100f / walkable : 0f):0.0}%), " +
                          $"포기 {instances}, 보이는 거리 {cfg.grassDistanceM}m, 층 다시 씀 {layerWrites}{(dataChanged ? " — TerrainData 변경" : "")}");
            return changes;
        }

        // 두 층: 짧은 풀(Rust_Red) / 긴 풀(Ash_Grey). 키 범위를 반으로 나눈다. 메시 프리팹 + VertexLit + GPU 인스턴싱(설계 §8.1).
        internal static DetailPrototype[] GrassPrototypes(MapDensityConfigSO cfg)
        {
            float lo = cfg.grassHeightRange.x, hi = Mathf.Max(lo, cfg.grassHeightRange.y), mid = (lo + hi) * 0.5f;
            DetailPrototype Make(bool tall) => new DetailPrototype
            {
                prototype = AssetDatabase.LoadAssetAtPath<GameObject>(MapDensityPrefabs.GrassPrefabPath(tall)),
                usePrototypeMesh = true,
                renderMode = DetailRenderMode.VertexLit,
                useInstancing = true,
                minHeight = tall ? mid : lo,
                maxHeight = tall ? hi : mid,
                minWidth = 0.7f,
                maxWidth = 1.2f,
                noiseSpread = 0.3f,
                healthyColor = Color.white,
                dryColor = Color.white,
            };
            return new[] { Make(false), Make(true) };
        }

        private static bool SamePrototype(DetailPrototype a, DetailPrototype b) =>
            a.prototype == b.prototype && a.usePrototypeMesh == b.usePrototypeMesh && a.renderMode == b.renderMode && a.useInstancing == b.useInstancing
            && Mathf.Approximately(a.minHeight, b.minHeight) && Mathf.Approximately(a.maxHeight, b.maxHeight)
            && Mathf.Approximately(a.minWidth, b.minWidth) && Mathf.Approximately(a.maxWidth, b.maxWidth)
            && Mathf.Approximately(a.noiseSpread, b.noiseSpread) && a.healthyColor == b.healthyColor && a.dryColor == b.dryColor;

        private static bool SameLayer(int[,] a, int[,] b)
        {
            if (a == null || b == null || a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1))
                return false;
            for (int z = 0; z < a.GetLength(0); z++)
            for (int x = 0; x < a.GetLength(1); x++)
                if (a[z, x] != b[z, x])
                    return false;
            return true;
        }

        // 풀 제외 마스크 [z, x]: 콜라이더 발자국 + 1m(산포 나무·덤불·통나무 제외 — 그 밑은 풀이 자연스럽다), 상자 접근 1.5m, 도로 띠 + 1m,
        // 정유소 패드(POI-REF 발자국), 탈출 패드.
        private static bool[,] GrassExclusion(Scene scene, GameObject environment, Terrain terrain, int res)
        {
            var mask = new bool[res, res];
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            float cell = size.x / res;
            Transform vegetation = environment.transform.Find(MapDensityLayout.VegetationGroup);

            void Stamp(Footprint f, float margin)
            {
                f.Aabb(out Vector2 min, out Vector2 max);
                int x0 = Mathf.Clamp(Mathf.FloorToInt((min.x - margin - origin.x) / cell), 0, res - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((max.x + margin - origin.x) / cell), 0, res - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((min.y - margin - origin.z) / cell), 0, res - 1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt((max.y + margin - origin.z) / cell), 0, res - 1);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (!mask[z, x] && f.Distance(new Vector2(origin.x + (x + 0.5f) * cell, origin.z + (z + 0.5f) * cell)) < margin)
                        mask[z, x] = true;
            }
            void StampSegment(Vector2 a, Vector2 b, float halfWidth)
            {
                Vector2 d = b - a;
                float length = d.magnitude;
                if (length < 1e-3f)
                    return;
                float yaw = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;
                Stamp(new Footprint((a + b) * 0.5f, new Vector2(length * 0.5f, halfWidth), yaw), 0.01f);
            }

            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger || c is TerrainCollider || (vegetation != null && c.transform.IsChildOf(vegetation)))
                        continue;
                    bool crate = c.GetComponent<LootContainer>() != null;
                    Stamp(FootprintOf(c), crate ? GrassCrateMarginM : GrassObstacleMarginM);
                }
            foreach (L.RoadSegment road in L.Roads)
                StampSegment(road.FromXZ, road.ToXZ, road.WidthM * 0.5f + GrassRoadMarginM);
            Stamp(new Footprint(L.Refinery.CenterXZ, L.Refinery.FootprintXZ * 0.5f, 0f), 0.01f);
            foreach (L.Extraction ext in L.Extractions)
                Stamp(new Footprint(ext.CenterXZ, ext.PadSizeXZ * 0.5f, 0f), 2f);
            return mask;
        }

        // 순수: 제외되지 않은 셀 중 coverage 비율(노이즈 상위)을 덮는다. 셀당 기대 포기 = maxPerM2 × 셀 넓이 × (0.5 ~ 1, 노이즈가 높을수록 빽빽),
        // 정수 = 내림 + 소수부 확률(고정 seed). 긴 풀 = 두 번째 노이즈 상위 40% 자리. 결과 [z, x].
        public static void GrassLayers(bool[,] excluded, float cellAreaM2, float coverage, float maxPerM2, float cellM, float noiseScaleM, int seed,
            out int[,] shortLayer, out int[,] tallLayer, out int covered, out long instances)
        {
            int rz = excluded.GetLength(0), rx = excluded.GetLength(1);
            shortLayer = new int[rz, rx];
            tallLayer = new int[rz, rx];
            covered = 0;
            instances = 0;
            Vector2 o1 = MapDensityLayout.NoiseOffset(seed), o2 = MapDensityLayout.NoiseOffset(seed + 1);
            float scale = Mathf.Max(1f, noiseScaleM);
            var values = new List<float>(rz * rx);
            var noise = new float[rz, rx];
            for (int z = 0; z < rz; z++)
            for (int x = 0; x < rx; x++)
            {
                if (excluded[z, x])
                    continue;
                float n = Mathf.PerlinNoise(o1.x + x * cellM / scale, o1.y + z * cellM / scale) * 0.75f
                          + Mathf.PerlinNoise(o1.x * 0.5f + x * cellM / (scale * 0.3f), o1.y * 0.5f + z * cellM / (scale * 0.3f)) * 0.25f;
                noise[z, x] = n;
                values.Add(n);
            }
            if (values.Count == 0 || coverage <= 0f)
                return;
            values.Sort();
            int cut = Mathf.Clamp(Mathf.RoundToInt(values.Count * (1f - Mathf.Clamp01(coverage))), 0, values.Count - 1);
            float threshold = values[cut];
            float top = values[values.Count - 1];
            var random = new System.Random(seed);
            for (int z = 0; z < rz; z++)
            for (int x = 0; x < rx; x++)
            {
                double roll = random.NextDouble();   // 셀마다 한 번 (제외 셀도 — 순서가 마스크에 덜 흔들리게)
                if (excluded[z, x] || noise[z, x] < threshold)
                    continue;
                covered++;
                float t = top > threshold ? (noise[z, x] - threshold) / (top - threshold) : 1f;
                float expected = maxPerM2 * cellAreaM2 * Mathf.Lerp(0.5f, 1f, t);
                int count = Mathf.FloorToInt(expected) + (roll < expected - Mathf.Floor(expected) ? 1 : 0);
                if (count <= 0)
                    continue;
                bool tall = Mathf.PerlinNoise(o2.x + x * cellM / (scale * 0.6f), o2.y + z * cellM / (scale * 0.6f)) >= 0.55f;
                if (tall)
                    tallLayer[z, x] = count;
                else
                    shortLayer[z, x] = count;
                instances += count;
            }
        }

        // ---------- ③ 계약: 승용차(트렁크 자리) ----------

        // 승용차 하나 (③이 트렁크를 붙인다: GDD §6, 설계 §5.4 — 차체 뒷면에서 5cm 밖, 0.6 × 0.8 × 1.6m)
        public readonly struct PassengerCar
        {
            public readonly Transform Structure;   // "Vehicle_{nn}" 래퍼 (Environment/{그룹}/)
            public readonly Transform Body;        // 블록아웃 "Block" (BoxCollider·렌더러)
            public readonly Vector3 LengthAxis;    // 차 길이 방향(월드, 수평 단위 벡터) = Block 로컬 x
            public readonly float LengthM, WidthM, TopY, GroundY;
            public readonly string Zone;           // LootZoneRules.ZoneOf
            public readonly bool IsNew;            // 맵 밀도가 더한 차량

            public PassengerCar(Transform structure, Transform body, Vector3 lengthAxis, float lengthM, float widthM, float topY, float groundY, string zone, bool isNew)
            {
                Structure = structure;
                Body = body;
                LengthAxis = lengthAxis;
                LengthM = lengthM;
                WidthM = widthM;
                TopY = topY;
                GroundY = groundY;
                Zone = zone;
                IsNew = isNew;
            }

            // 뒤 끝 중심 (지면 높이) — "뒤"는 길이 축 음의 끝으로 정한다(블록아웃 차량은 앞뒤 구분이 없다)
            public Vector3 RearGroundPoint => new Vector3(Body.position.x, GroundY, Body.position.z) - LengthAxis * (LengthM * 0.5f);
        }

        // 씬의 모든 승용차 = Environment 아래 "Vehicle_{nn}"(차량 무더기 VehiclePile 제외) 중 Block 길이 ≤ PassengerCarMaxLengthM. 계층 순서.
        public static List<PassengerCar> PassengerCars(Scene scene)
        {
            var cars = new List<PassengerCar>();
            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            if (environment == null)
                return cars;
            Terrain terrain = environment.GetComponentInChildren<Terrain>();
            Transform mine = environment.transform.Find(MapDensityLayout.StructureGroup);
            foreach (Transform t in environment.GetComponentsInChildren<Transform>(true))
            {
                if (!VehicleName.IsMatch(t.name) || AssetSwapApply.UnderSkin(t))
                    continue;
                Transform body = t.Find("Block");
                if (body == null)
                    continue;
                Vector3 s = body.lossyScale;
                if (Mathf.Max(s.x, s.z) > MapDensityLayout.PassengerCarMaxLengthM)
                    continue;
                bool alongX = s.x >= s.z;
                Vector3 axis = body.rotation * (alongX ? Vector3.right : Vector3.forward);
                axis = new Vector3(axis.x, 0f, axis.z).normalized;
                float ground = M4MapSetup.GroundY(terrain, t.position.x, t.position.z);
                cars.Add(new PassengerCar(t, body, axis, Mathf.Max(s.x, s.z), Mathf.Min(s.x, s.z), body.position.y + s.y * 0.5f, ground,
                    LootZoneRules.ZoneOf(XZ(t.position)), mine != null && t.IsChildOf(mine)));
            }
            return cars;
        }
    }
}
