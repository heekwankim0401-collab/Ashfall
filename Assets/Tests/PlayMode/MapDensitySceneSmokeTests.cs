#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.AI;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // 맵 밀도 ①② 스모크 (docs/design/MapDensity_design.md §7·§8.1·§10): Ashfall/Setup/Apply Map Density가 적용한 Raid_CrimsonBasin을 불러
    // 그룹·개수(건물·구조물·차량·나무·덤불·통나무), 풀 디테일(GPU 인스턴싱), LOD·컬·그림자 정책, NavMesh 구멍·엄폐 지점을 본다.
    // 이름은 MapDensityLayout 계약 값(테스트 asmdef는 EditorTools를 참조하지 않는다).
    public class MapDensitySceneSmokeTests
    {
        private const string ConfigPath = "Assets/_Project/Data/Raid/MapDensityConfig.asset";
        private const string EnvironmentObject = "Environment";
        private const string BuildingGroup = "MapDensity_Buildings";
        private const string StructureGroup = "MapDensity_Structures";
        private const string VegetationGroup = "MapDensity_Vegetation";
        private const string ScatterPrefabFolder = "Assets/_Project/Prefabs/Scatter/";
        private const string RaidScenePath = "Assets/_Project/Scenes/Raid_CrimsonBasin.unity";
        private const int ExpectedNewBuildings = 80;
        private const int ExpectedNewStructures = 150 + 30 + 10;   // 소형 + 승용차 + 큰 차량
        private const int MinTotalHouses = 85;                    // 설계 §10 "건물 ≥ 85" (기존 11 + 80)
        private const int MinPassengerCars = 60;                  // 기존 약 38 + 새 30 (③ 트렁크 자리)
        private const float PassengerCarMaxLengthM = 5f;
        private static readonly Regex Numbered = new Regex(@"^[A-Za-z]+_\d+$");

        private MapDensityConfigSO cfg;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Flow.SetWorldPaused(false);
                Time.timeScale = 1f;
            }
            cfg = AssetDatabase.LoadAssetAtPath<MapDensityConfigSO>(ConfigPath);
            Assert.IsNotNull(cfg, $"{ConfigPath} 없음 (Ashfall/Setup/Apply Map Density)");
            yield return null;
        }

        private static GameObject Environment() =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == EnvironmentObject);

        private static Transform Group(string name)
        {
            Transform group = Environment().transform.Find(name);
            Assert.IsNotNull(group, $"Environment/{name} 없음 (Ashfall/Setup/Apply Map Density)");
            return group;
        }

        private static bool IsSkin(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p.name.StartsWith("Skin"))
                    return true;
            return false;
        }

        [UnityTest]
        public IEnumerator RaidScene_GroupsAndCounts()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            Transform buildings = Group(BuildingGroup), structures = Group(StructureGroup), vegetation = Group(VegetationGroup);
            Assert.AreEqual(ExpectedNewBuildings, buildings.childCount, "새 건물");
            Assert.IsTrue(buildings.Cast<Transform>().All(b => Regex.IsMatch(b.name, @"^House_\d+$")), "건물 = 속 찬 House 블록아웃");
            Assert.AreEqual(ExpectedNewStructures, structures.childCount, "새 소형 구조물·차량");
            Assert.IsTrue(structures.Cast<Transform>().All(s => Numbered.IsMatch(s.name)), "구조물 이름 {Shape}_{nn}");

            Transform[] all = Environment().GetComponentsInChildren<Transform>(true).Where(t => !IsSkin(t)).ToArray();
            Assert.GreaterOrEqual(all.Count(t => Regex.IsMatch(t.name, @"^House_\d+$")), MinTotalHouses, "레이드 씬 건물 수");
            int cars = all.Count(t => Regex.IsMatch(t.name, @"^Vehicle_\d+$") && t.Find("Block") != null
                                      && Mathf.Max(t.Find("Block").lossyScale.x, t.Find("Block").lossyScale.z) <= PassengerCarMaxLengthM);
            Assert.GreaterOrEqual(cars, MinPassengerCars, "승용차(③ 트렁크 자리)");

            // 산포: 프리팹 인스턴스(씬이 Scatter 프리팹을 참조), 나무·통나무는 콜라이더 + GroundSurface, 덤불은 콜라이더 없음
            string[] dependencies = AssetDatabase.GetDependencies(RaidScenePath, false);
            Assert.IsTrue(dependencies.Any(d => d.StartsWith(ScatterPrefabFolder + "SC_DeadTree_")), "씬이 나무 프리팹을 참조");
            var trees = vegetation.Cast<Transform>().Where(t => t.name.StartsWith("SC_DeadTree_")).ToList();
            var bushes = vegetation.Cast<Transform>().Where(t => t.name.StartsWith("SC_Bush_")).ToList();
            var logs = vegetation.Cast<Transform>().Where(t => t.name.StartsWith("SC_Log_")).ToList();
            Assert.AreEqual(cfg.treeCount, trees.Count, "죽은 나무");
            Assert.AreEqual(cfg.bushCount, bushes.Count, "덤불");
            Assert.AreEqual(cfg.logCount, logs.Count, "통나무");
            Assert.AreEqual(trees.Count + bushes.Count + logs.Count, vegetation.childCount, "산포 그룹에 다른 것 없음");
            foreach (Transform t in trees.Concat(logs))
            {
                var collider = t.GetComponent<CapsuleCollider>();
                Assert.IsNotNull(collider, $"{t.name}: 줄기 콜라이더");
                Assert.IsFalse(collider.isTrigger);
                Assert.IsNotNull(t.GetComponent<GroundSurface>(), $"{t.name}: GroundSurface");
            }
            foreach (Transform b in bushes)
            {
                Assert.AreEqual(0, b.GetComponentsInChildren<Collider>(true).Length, $"{b.name}: 덤불은 콜라이더 없음(§8.4)");
                Assert.LessOrEqual(b.GetComponentInChildren<Renderer>().bounds.size.y, cfg.bushMaxHeightM + 0.1f, "덤불 키");
            }
            Terrain terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
            foreach (Transform t in trees.Take(200))
            {
                float ground = terrain.SampleHeight(t.position) + terrain.transform.position.y;
                Assert.Less(Mathf.Abs(t.position.y - ground), 0.6f, $"{t.name}: 지면에 서 있다 {t.position}");
            }
        }

        [UnityTest]
        public IEnumerator RaidScene_Grass_TerrainDetail_GpuInstanced()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            Terrain terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
            Assert.IsTrue(terrain.drawTreesAndFoliage, "drawTreesAndFoliage");
            Assert.AreEqual(cfg.grassDistanceM, terrain.detailObjectDistance, 1e-3f, "풀 보이는 거리");
            TerrainData data = terrain.terrainData;
            Assert.AreEqual(2, data.detailPrototypes.Length, "풀 2층");
            foreach (DetailPrototype p in data.detailPrototypes)
            {
                Assert.IsTrue(p.usePrototypeMesh && p.useInstancing, "메시 + GPU 인스턴싱 (§8.1)");
                Assert.IsNotNull(p.prototype);
                StringAssert.StartsWith(ScatterPrefabFolder, AssetDatabase.GetAssetPath(p.prototype), "풀 프리팹은 git 포함 자체 메시");
                Assert.LessOrEqual(p.maxHeight, 0.8f + 1e-3f, "풀 키 ≤ 0.8m (§8.4)");
                var renderer = p.prototype.GetComponent<MeshRenderer>();
                Assert.IsTrue(renderer.sharedMaterial.enableInstancing, $"{renderer.sharedMaterial.name}: 인스턴싱");
                Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode, $"{p.prototype.name}: 풀 프리팹 그림자 끔");
            }
            Assert.AreEqual(cfg.detailResolution, data.detailResolution);
            long total = 0;
            for (int layer = 0; layer < 2; layer++)
            {
                int[,] values = data.GetDetailLayer(0, 0, data.detailResolution, data.detailResolution, layer);
                foreach (int v in values)
                    total += v;
            }
            float area = data.size.x * data.size.z;
            Assert.Greater(total, (long)(area * 0.15f), "풀 포기 수 (걷는 지형 약 40% × 약 1포기/m²)");
            Assert.Less(total, (long)(area * cfg.grassMaxPerM2), "최대 밀도 이하");
        }

        [UnityTest]
        public IEnumerator RaidScene_LodCullAndShadowPolicy()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            Terrain terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
            int unshadowed = 0;
            foreach (string name in new[] { BuildingGroup, StructureGroup })
                foreach (Transform s in Group(name))
                {
                    var lod = s.GetComponent<LODGroup>();
                    Assert.IsNotNull(lod, $"{s.name}: 컬 LODGroup");
                    LOD[] lods = lod.GetLODs();
                    Assert.AreEqual(1, lods.Length, $"{s.name}: 컬 전용 LOD 하나");
                    Assert.Greater(lods[0].screenRelativeTransitionHeight, 0f);
                    Assert.Less(lods[0].screenRelativeTransitionHeight, 0.5f);
                    Renderer[] renderers = s.GetComponentsInChildren<Renderer>(true);
                    CollectionAssert.IsSubsetOf(renderers, lods[0].renderers, $"{s.name}: 렌더러가 전부 LOD에 있다(스킨 포함)");
                    float top = renderers.Where(r => !IsSkin(r.transform)).Max(r => r.bounds.max.y);
                    float height = top - (terrain.SampleHeight(s.position) + terrain.transform.position.y);
                    ShadowCastingMode expected = height >= cfg.shadowMinHeightM ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    foreach (Renderer r in renderers.Where(r => !IsSkin(r.transform)))
                        Assert.AreEqual(expected, r.shadowCastingMode, $"{s.name}/{r.name}: 높이 {height:0.0}m 그림자 정책");
                    if (expected == ShadowCastingMode.Off)
                        unshadowed++;
                }
            Assert.Greater(unshadowed, 10, "낮은 구조물(모래주머니·잔해 등) 그림자 끔");

            foreach (Transform t in Group(VegetationGroup))
            {
                var lod = t.GetComponent<LODGroup>();
                Assert.IsNotNull(lod, $"{t.name}: LODGroup");
                Assert.AreEqual(LODFadeMode.CrossFade, lod.fadeMode);
                LOD[] lods = lod.GetLODs();
                if (t.name.StartsWith("SC_DeadTree_"))
                {
                    Assert.AreEqual(2, lods.Length, "나무 LOD0·LOD1 + 컬");
                    Assert.Greater(lods[0].screenRelativeTransitionHeight, lods[1].screenRelativeTransitionHeight);
                    Assert.IsTrue(lods[0].renderers.All(r => r.shadowCastingMode == ShadowCastingMode.On), "나무 LOD0 그림자");
                    Assert.IsTrue(lods[1].renderers.All(r => r.shadowCastingMode == (cfg.treeLod1Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off)), "나무 LOD1 그림자");
                }
                else
                {
                    Assert.AreEqual(1, lods.Length);
                    Assert.IsTrue(lods[0].renderers.All(r => r.shadowCastingMode == ShadowCastingMode.Off), $"{t.name}: 작은 산포물 그림자 끔");
                }
                Assert.IsFalse(GameObjectUtility.AreStaticEditorFlagsSet(t.GetComponentInChildren<Renderer>().gameObject, StaticEditorFlags.BatchingStatic),
                    $"{t.name}: 산포물은 정적 배칭 안 함(LOD 우선)");
            }
        }

        [UnityTest]
        public IEnumerator RaidScene_NavMesh_CarvedAndCovers()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            Terrain terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
            foreach (string name in new[] { BuildingGroup, StructureGroup })
                foreach (Transform s in Group(name))
                {
                    Vector3 p = s.position;
                    var ground = new Vector3(p.x, terrain.SampleHeight(p) + terrain.transform.position.y, p.z);
                    Assert.IsFalse(NavMesh.SamplePosition(ground, out NavMeshHit hit, 0.3f, NavMesh.AllAreas),
                        $"{s.name}: 구조물 자리에 NavMesh가 있다 {hit.position} (다시 굽지 않았다)");
                }
            foreach (string name in new[] { BuildingGroup, StructureGroup, VegetationGroup })
                Assert.IsNotNull(Group(name).GetComponent<Unity.AI.Navigation.NavMeshModifier>(), $"{name}: NavMeshModifier(Not Walkable)");

            int newCovers = 0;
            foreach (string name in new[] { BuildingGroup, StructureGroup })
                foreach (CoverPoint cover in Group(name).GetComponentsInChildren<CoverPoint>())
                {
                    Assert.IsTrue(NavMesh.SamplePosition(cover.transform.position, out NavMeshHit _, 0.5f, NavMesh.AllAreas),
                        $"{cover.transform.parent.name}/{cover.name}: NavMesh 밖");
                    newCovers++;
                }
            int total = Object.FindObjectsByType<CoverPoint>(FindObjectsSortMode.None).Length;
            Assert.Greater(newCovers, 100, "새 엄폐 지점");
            Assert.LessOrEqual(newCovers, cfg.maxNewCoverPoints, "새 엄폐 상한");
            Assert.LessOrEqual(total, cfg.maxTotalCoverPoints, "전체 엄폐 상한 (CoverSelector 선형 탐색)");
            Assert.AreEqual(0, Group(VegetationGroup).GetComponentsInChildren<CoverPoint>().Length, "나무·덤불·통나무에는 엄폐 지점 없음");

            // 나무 줄기 자리는 NavMesh 구멍 (줄기 콜라이더가 구워졌다)
            int trees = 0, carvedTrees = 0;
            foreach (Transform t in Group(VegetationGroup))
            {
                if (!t.name.StartsWith("SC_DeadTree_"))
                    continue;
                trees++;
                var ground = new Vector3(t.position.x, terrain.SampleHeight(t.position) + terrain.transform.position.y, t.position.z);
                if (!NavMesh.SamplePosition(ground, out NavMeshHit _, 0.2f, NavMesh.AllAreas))
                    carvedTrees++;
            }
            Assert.Greater(carvedTrees, trees * 0.9f, $"나무 줄기 자리 NavMesh 구멍 {carvedTrees}/{trees}");
        }

        // 리뷰 M1: 적이 실제로 받는 경로는 한 번의 질의로 완성돼야 한다(NavMeshAgent는 부분 경로 끝에서 멈춘다) —
        // 순찰 구간 전부, 적 시작점 → 250m 안 다른 적·순찰 지점·상자. 맵을 가로지르는 긴 경로(스폰 → 탈출, 플레이어 몫)는 정보로만 기록하고
        // 런타임 NavMeshAgent가 같은 노드 한도를 받는지도 기록한다(설계 §14.6).
        [UnityTest]
        public IEnumerator RaidScene_AgentPaths_SingleQueryComplete()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            const float local = 250f;
            var path = new NavMeshPath();
            int segments = 0;
            foreach (PatrolRoute route in Object.FindObjectsByType<PatrolRoute>(FindObjectsSortMode.None))
            {
                var points = route.Points;
                for (int i = 0; i < points.Count && points.Count >= 2; i++)
                {
                    Assert.IsTrue(NavMesh.SamplePosition(points[i], out NavMeshHit a, 2f, NavMesh.AllAreas), $"{route.name}#{i}");
                    Assert.IsTrue(NavMesh.SamplePosition(points[(i + 1) % points.Count], out NavMeshHit b, 2f, NavMesh.AllAreas));
                    Assert.IsTrue(NavPathProbe.SingleQueryComplete(a.position, b.position, path, out _),
                        $"{route.name} 구간 {i}: 한 번의 질의로 완성되지 않는다({path.status})");
                    segments++;
                }
            }
            Assert.Greater(segments, 20, "순찰 구간 수");

            var enemies = Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None);
            var targets = enemies.Select(e => (name: e.name, position: e.transform.position))
                // 상자 = M4 기존 21개(Environment/LootCrates)만. 맵 밀도 ③ 새 상자·트렁크 182곳은 적 경로 목표가 아니다(적은 컨테이너로 가지 않는다 —
                // 트렁크 중심은 차체 안). 루트 지점 도달은 플레이어 기준으로 MapDensityLootApply가 접근점에서 확인한다(설계 §7 3).
                .Concat(Object.FindObjectsByType<Ashfall.Inventory.LootContainer>(FindObjectsSortMode.None)
                    .Where(c => c.transform.parent != null && c.transform.parent.name == "LootCrates").Select(c => (name: c.name, position: c.transform.position)))
                .Concat(Object.FindObjectsByType<PatrolRoute>(FindObjectsSortMode.None).SelectMany(r => r.Points.Select(p => (name: r.name, position: p)))).ToList();
            int checkedPaths = 0;
            float longest = 0f;
            foreach (EnemyAgent e in enemies)
            {
                Assert.IsTrue(NavMesh.SamplePosition(e.transform.position, out NavMeshHit from, 2f, NavMesh.AllAreas), e.name);
                foreach (var t in targets)
                {
                    if (t.name == e.name || Vector3.Distance(from.position, t.position) > local
                        || !NavMesh.SamplePosition(t.position, out NavMeshHit to, 2f, NavMesh.AllAreas))
                        continue;
                    Assert.IsTrue(NavPathProbe.SingleQueryComplete(from.position, to.position, path, out float length),
                        $"{e.name} → {t.name}: 한 번의 질의로 완성되지 않는다({path.status}, 직선 {Vector3.Distance(from.position, to.position):0}m)");
                    longest = Mathf.Max(longest, length);
                    checkedPaths++;
                }
            }
            Assert.Greater(checkedPaths, 200, "적 근거리 경로 수");

            // 정보: 맵 횡단 경로(한 번의 질의 / 이어 찾기)와 런타임 에이전트 결과
            var report = new System.Text.StringBuilder($"[MapDensitySceneSmokeTests] 순찰 구간 {segments}·적 근거리 {checkedPaths}(최장 {longest:0}m) 한 번에 완성 | 맵 횡단: ");
            var spawns = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
            var zones = Object.FindObjectsByType<ExtractionZone>(FindObjectsSortMode.None);
            foreach (PlayerSpawnPoint s in spawns)
                foreach (ExtractionZone z in zones)
                {
                    if (!NavMesh.SamplePosition(s.transform.position, out NavMeshHit a, 2f, NavMesh.AllAreas)
                        || !NavMesh.SamplePosition(z.transform.position, out NavMeshHit b, 2f, NavMesh.AllAreas))
                        continue;
                    bool single = NavPathProbe.SingleQueryComplete(a.position, b.position, path, out _);
                    bool connected = NavPathProbe.TryConnected(a.position, b.position, path, out float len, out int continued);
                    report.Append($"{s.Id}→{z.Id} {(single ? "한 번" : $"부분→이어 {continued}")}{(connected ? "" : " 끊김")} {len:0}m; ");
                }
            // 런타임 에이전트: SP-N 근처에 임시 NavMeshAgent를 놓고 EXT-02로 SetDestination
            PlayerSpawnPoint north = spawns.FirstOrDefault(s => s.Id == "SP-N");
            ExtractionZone ext02 = zones.FirstOrDefault(z => z.Id == "EXT-02");
            if (north != null && ext02 != null && NavMesh.SamplePosition(north.transform.position, out NavMeshHit start, 2f, NavMesh.AllAreas))
            {
                var go = new GameObject("ProbeAgent");
                go.transform.position = start.position;
                var agent = go.AddComponent<NavMeshAgent>();
                agent.SetDestination(ext02.transform.position);
                float deadline = Time.realtimeSinceStartup + 10f;
                while (agent.pathPending && Time.realtimeSinceStartup < deadline)
                    yield return null;
                report.Append($"| 런타임 NavMeshAgent SP-N→EXT-02: {agent.pathStatus}, 코너 {agent.path.corners.Length}");
                Object.Destroy(go);
            }
            Debug.Log(report.ToString());
        }
    }
}
#endif
