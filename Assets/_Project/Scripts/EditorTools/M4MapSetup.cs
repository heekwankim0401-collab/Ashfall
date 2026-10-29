using System.Collections.Generic;
using System.IO;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Raid;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using L = Ashfall.EditorTools.CrimsonBasinLayout;

namespace Ashfall.EditorTools
{
    // Crimson Basin 맵 블록아웃 (§8.1·§8.2, M4 설계 §2, TODO(GDD-?) Q1 A: 1km 전체 + POI 5곳 저밀도).
    // **열린 씬**에 만든다 — 씬 생성·저장, 관리자(ProjectileSystem·AiDirector)·플레이어·UI는 3단계 M4SceneSetup 몫.
    // 만드는 것:
    // - Terrain(1000×120×1000, 해상도 513, 결정적 높이) — TerrainData는 Terrain-CrimsonBasin.asset (있으면 SetHeights로 덮어써 GUID 유지)
    // - Environment 루트: Terrain + POI 5곳·탈출구·잔해 블록아웃(내부를 채운 기본 도형) + CoverPoint + 루트 상자 → NavMeshSurface 굽기
    //   (NavMesh-Environment.asset, 있으면 CopySerialized로 덮어써 GUID 유지)
    // - Bounds 루트: 맵 경계 보이지 않는 벽 4개 (Q20, 굽기 제외)
    // - RaidController, ExtractionZone 4, PlayerSpawnPoint 3, PowerSwitch, PointOfInterest 5 (최상위)
    // 좌표·개수는 CrimsonBasinLayout(1단계), 블록아웃 도형 배치는 이 파일의 레이아웃 상수(레벨 데이터, 게임 수치 아님).
    // 주의(M2 함정): EditorSceneManager.NewScene(Single)은 쓰지 않는 에셋을 언로드한다 → MapAssets는 NewScene 뒤에 불러온다.
    public static class M4MapSetup
    {
        private const string Tag = "[M4MapSetup]";

        public const string SceneFolder = "Assets/_Project/Scenes/Raid_CrimsonBasin";
        public const string TerrainDataPath = SceneFolder + "/Terrain-CrimsonBasin.asset";
        public const string NavMeshPath = SceneFolder + "/NavMesh-Environment.asset";
        public const string ZoneMaterialPath = M3SceneSetup.MaterialFolder + "/M4_ExtractionZone.mat";

        // 2c(M4DataSetup) 데이터의 설계 경로 (M4 설계 §4.6). 3단계는 M4DataSetup 상수로 MapAssets를 채워도 된다.
        public const string DefaultRaidConfigPath = M2DataSetup.DataFolder + "/Raid/RaidConfig.asset";
        public const string DefaultCrateLootTablePath = M2DataSetup.DataFolder + "/Loot/LT-CRATE.asset";
        public const string DefaultFlarePath = M2DataSetup.DataFolder + "/Items/Raid/ITM-FLR-01.asset";

        public const string EnvironmentName = "Environment";
        public const string BoundsName = "Bounds";

        // ---------- 블록아웃 값 (레벨 데이터, TODO(GDD-?): GDD에 구조물 치수 없음, 그레이박스) ----------

        private const float SinkM = 0.5f;               // 기울어진 지형에서 틈이 없게 도형 바닥을 땅속으로 내린다
        private const float PoiPadMarginM = 40f;        // 평탄 패드 블렌딩 폭
        private const float PointPadMarginM = 60f;      // 탈출·스폰 패드 (가장자리 경사 위라 넓게 — 2d 측정: 40m면 내리막 쪽 경사 49°)
        private const float RoadPadMarginM = 30f;       // 2d 측정: 15m면 EXT-01 도로 둑 옆면 최대 59°
        // TODO(GDD-?) 설계 §2.2 "가우시안 +35m(반경 90m)" → 정상 평탄부 반경 75m + 경사 폭 90m의 smoothstep 고원.
        // 가우시안 꼭대기에 POI 패드(100×100m)를 평탄화하면 패드 가장자리 경사가 45°(NavMesh 최대 경사)를 넘는다.
        private const float HillPlateauRadiusM = 75f;
        private const float ReservoirFlatRatio = 0.5f;  // 웅덩이 바닥 평탄부 = 반경의 절반

        private const float DeckClearanceM = 6f;        // 고가 상판 아래 높이 (설계 §2.3 "높이 6m")
        private const float DeckPillarInsetM = 2f;
        private const float CoverSpacingM = 6f;         // 긴 낮은 벽의 엄폐 지점 간격
        private const float PeekBeyondEndM = 0.4f;      // M3 HighPeekSideM 1.9 = 벽 반길이 1.5 + 0.4
        internal const float LowCoverMaxHeightM = 1.3f; // 잔해·맵 개선 엄폐 상자: 이 높이 이하면 Low 엄폐 (맵 개선 리뷰 L5에서 공용 상수로)

        private static readonly Vector3 CarSize = M3SceneSetup.VehicleSize;      // 4×1.5×2
        private static readonly Vector3 LowWall = M3SceneSetup.LowWallSize;      // 4×1.1×0.4
        private static readonly Vector3 HighWall = M3SceneSetup.HighWallSize;    // 3×2.5×0.4
        private static readonly Vector3 DeckSize = new Vector3(45f, 1f, 12f);
        private static readonly Vector3 PillarSize = new Vector3(1.5f, 0f, 1.5f); // 높이는 상판까지
        private static readonly Vector3 HouseSize = new Vector3(8f, 3.5f, 6f);
        private static readonly Vector3 RubbleSize = new Vector3(4f, 1.2f, 3f);
        private static readonly Vector3 TankSize = new Vector3(14f, 10f, 14f);
        private static readonly Vector3 PipeSize = new Vector3(20f, 1f, 0.8f);
        private static readonly Vector3 LongWallSize = new Vector3(30f, 3f, 0.5f);
        private static readonly Vector3 FenceSize = new Vector3(20f, 1.2f, 0.4f);
        private static readonly Vector3 PylonSize = new Vector3(2f, 28f, 2f);
        private static readonly Vector3 DikeSize = new Vector3(55f, 3f, 4f);
        private static readonly Vector3 RailSize = new Vector3(0.3f, 0.3f, 20f);
        private static readonly Vector3 CrateSize = Vector3.one;
        private static readonly Vector2Int CrateGridSize = new Vector2Int(6, 4);  // M2 상자와 같다
        private const string CrateDisplayName = "보급 상자";
        private static readonly Vector3 SwitchSize = new Vector3(0.6f, 1.2f, 0.3f);
        private const float SwitchBottomM = 0.6f;
        private const float DefaultZoneRadiusM = 8f;    // RaidConfig가 없을 때 표시 반경
        private const float ZoneMarkerThicknessM = 0.02f;

        // ---------- 공개 API (3단계 M4SceneSetup이 부른다) ----------

        // 맵이 쓰는 데이터. NewScene 뒤에 불러야 한다(LoadDefaults). null 항목은 경고 후 빈 참조로 진행.
        internal sealed class MapAssets
        {
            public RaidConfigSO RaidConfig;          // RaidController.config, 탈출 구역 표시 반경
            public LootTableSO CrateLootTable;       // 일반 상자 LootContainer.lootTable (Q17)
            public ItemDefinitionSO Flare;           // 신호탄 상자 고정 내용물 (Q7)
            public Material ZoneMaterial;            // 탈출 구역 바닥 표시 (반투명)

            internal static MapAssets LoadDefaults()
            {
                return new MapAssets
                {
                    RaidConfig = AssetDatabase.LoadAssetAtPath<RaidConfigSO>(DefaultRaidConfigPath),
                    CrateLootTable = AssetDatabase.LoadAssetAtPath<LootTableSO>(DefaultCrateLootTablePath),
                    Flare = AssetDatabase.LoadAssetAtPath<ItemDefinitionSO>(DefaultFlarePath),
                    ZoneMaterial = AssetDatabase.LoadAssetAtPath<Material>(ZoneMaterialPath),
                };
            }
        }

        // NavMesh 굽기 설정. 0 = NavMeshSurface 기본값(voxel = 에이전트 반지름/3, 타일 256 voxel).
        internal struct NavBakeSettings
        {
            public float VoxelSizeM;
            public int TileSizeVoxels;
            public float MinRegionArea;   // 0이면 기본 2
        }

        // 확정 설정 (2d 측정 근거는 M4 설계 "2d 구현 메모")
        internal static NavBakeSettings DefaultBake => new NavBakeSettings { VoxelSizeM = 0f, TileSizeVoxels = 0, MinRegionArea = 2f };

        internal sealed class MapBuildResult
        {
            public Terrain Terrain;
            public GameObject Environment;
            public GameObject Bounds;
            public RaidController Controller;
            public NavMeshSurface Surface;
            public int BlockCount;
            public int CoverPointCount;
            public int CrateCount;
            public double BakeSeconds;
            public long NavMeshBytes;
            public long TerrainBytes;
            public bool Success;
        }

        [MenuItem("Ashfall/Setup/M4/Build Crimson Basin Map (Open Scene)")]
        public static void BuildMapInOpenSceneMenu()
        {
            EnsureZoneMaterial();
            AssetDatabase.SaveAssets();
            MapBuildResult result = BuildMap(MapAssets.LoadDefaults(), DefaultBake);
            if (result.Success)
                ValidateNavigation(result.Controller != null ? result.Controller.Config : null, out _);
        }

        // 열린 씬에 맵 전체를 만든다(지형 → 블록아웃·엄폐·상자 → 경계 → NavMesh → 레이드 오브젝트).
        // 같은 씬에서 두 번 부르면 오브젝트가 두 벌 생긴다 — 새 씬에서 한 번만 부른다.
        internal static MapBuildResult BuildMap(MapAssets assets, NavBakeSettings bake)
        {
            var result = new MapBuildResult();
            if (assets == null)
                assets = new MapAssets();
            WarnMissing(assets);

            result.Environment = new GameObject(EnvironmentName);
            result.Terrain = CreateTerrain(result.Environment.transform);
            var builder = new BlockoutBuilder(result.Terrain, result.Environment.transform);
            CreateBlockout(builder);
            result.CrateCount = CreateLootCrates(builder, assets);
            result.BlockCount = builder.BlockCount;
            result.CoverPointCount = builder.CoverCount;
            result.Bounds = CreateBoundary();

            result.Success = BakeNavMesh(result.Environment, NavMeshPath, bake, out result.Surface, out result.BakeSeconds);
            result.NavMeshBytes = FileBytes(NavMeshPath);
            result.TerrainBytes = FileBytes(TerrainDataPath);

            result.Controller = CreateRaidObjects(result.Terrain, assets);
            Debug.Log($"{Tag} 맵 생성: 도형 {result.BlockCount}, 엄폐 {result.CoverPointCount}, 상자 {result.CrateCount}, " +
                      $"NavMesh 굽기 {result.BakeSeconds:0.0}s ({result.NavMeshBytes / 1024f:0} KB), Terrain {result.TerrainBytes / 1024f:0} KB");
            return result;
        }

        // 탈출 구역 바닥 표시 재질 (URP Unlit 반투명). 없을 때만 만든다. 색은 M6 아트 메뉴(PaletteMaterialMap.LegacyMaterials)가 팔레트 Bone α0.25로 다시 칠한다(GUID 유지).
        internal static Material EnsureZoneMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(ZoneMaterialPath);
            if (existing != null)
                return existing;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError($"{Tag} URP Unlit 셰이더를 찾지 못했다.");
                return null;
            }
            SetupUtil.EnsureFolder(M3SceneSetup.MaterialFolder);
            var material = new Material(shader) { name = "M4_ExtractionZone" };
            var color = new Color(0.95f, 0.75f, 0.2f, 0.3f);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f); // Transparent
            material.SetFloat("_Blend", 0f);   // Alpha
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(material, ZoneMaterialPath);
            Debug.Log($"{Tag} 에셋 생성: {ZoneMaterialPath}");
            return material;
        }

        // 지면 높이 (월드 y). Terrain이 없으면 0.
        internal static float GroundY(Terrain terrain, float x, float z)
        {
            if (terrain == null)
                return 0f;
            return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        }

        internal static Vector3 GroundPoint(Terrain terrain, Vector2 xz) => new Vector3(xz.x, GroundY(terrain, xz.x, xz.y), xz.y);

        // 지면 점을 NavMesh 위로 붙인다(굽기 뒤). 실패하면 지면 점 그대로.
        internal static Vector3 SnapToNavMesh(Terrain terrain, Vector2 xz, float maxDistanceM = 3f)
        {
            Vector3 ground = GroundPoint(terrain, xz);
            return NavMesh.SamplePosition(ground, out NavMeshHit hit, maxDistanceM, NavMesh.AllAreas) ? hit.position : ground;
        }

        // 스폰·탈출 구역이 NavMesh 위인지, 스폰마다 활성 탈출구까지 경로가 있는지(설계 §2.4).
        // config가 없거나 spawnRules가 비면 모든 스폰 → 모든 탈출구(상위 집합)를 본다. 씬의 컴포넌트를 찾는다(에디터 전용 확인).
        internal static bool ValidateNavigation(RaidConfigSO config, out string report)
        {
            var sb = new System.Text.StringBuilder();
            bool ok = true;
            var spawns = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.InstanceID);
            var zones = Object.FindObjectsByType<ExtractionZone>(FindObjectsSortMode.InstanceID);
            var path = new NavMeshPath();

            foreach (PlayerSpawnPoint spawn in spawns)
            {
                if (!Sample(spawn.transform.position, out Vector3 from))
                {
                    sb.AppendLine($"  스폰 {spawn.Id}: NavMesh 샘플 실패 {spawn.transform.position}");
                    ok = false;
                    continue;
                }
                foreach (ExtractionZone zone in zones)
                {
                    if (!IsActiveFor(config, spawn.Id, zone.Id))
                        continue;
                    if (!Sample(zone.transform.position, out Vector3 to))
                    {
                        sb.AppendLine($"  탈출 {zone.Id}: NavMesh 샘플 실패 {zone.transform.position}");
                        ok = false;
                        continue;
                    }
                    var corners = new List<Vector3>();
                    bool complete = TryPath(from, to, path, out float length, out int continued, corners);
                    // 이어 찾았으면 상태·코너 수는 이어 붙인 전체 경로 기준 (마지막 조각만 보이지 않게)
                    string status = complete ? (continued > 0 ? $"Complete(연결 — 한 번 질의는 {NavMeshPathStatus.PathPartial}, 이어 찾기 {continued})" : "Complete")
                        : $"{path.status}(이어 찾기 {continued} 뒤에도 끊김)";
                    sb.AppendLine($"  {spawn.Id} → {zone.Id}: {status}, " +
                                  $"코너 {corners.Count}, 길이 {length:0}m (직선 {Vector3.Distance(from, to):0}m)");
                    ok &= complete;
                }
            }
            if (spawns.Length != L.Spawns.Length || zones.Length != L.Extractions.Length)
            {
                sb.AppendLine($"  개수 불일치: 스폰 {spawns.Length}/{L.Spawns.Length}, 탈출 {zones.Length}/{L.Extractions.Length}");
                ok = false;
            }
            report = sb.ToString();
            if (ok)
                Debug.Log($"{Tag} 경로 확인 통과\n{report}");
            else
                Debug.LogError($"{Tag} 경로 확인 실패\n{report}");
            return ok;
        }

        private static bool IsActiveFor(RaidConfigSO config, string spawnId, string extractionId)
        {
            if (config == null || config.spawnRules == null || config.spawnRules.Length == 0)
                return true;
            foreach (SpawnRule rule in config.spawnRules)
            {
                if (rule.spawnId != spawnId || rule.activeExtractionIds == null)
                    continue;
                foreach (string id in rule.activeExtractionIds)
                    if (id == extractionId)
                        return true;
                return false;
            }
            return true; // 규칙 없는 스폰 = 무작위 → 전부 확인
        }

        private static bool Sample(Vector3 position, out Vector3 onMesh)
        {
            bool found = NavMesh.SamplePosition(position, out NavMeshHit hit, 2f, NavMesh.AllAreas);
            onMesh = found ? hit.position : position;
            return found;
        }

        // 연결성 확인 (플레이어가 걸어서 닿는가). 구현은 Ashfall.AI.NavPathProbe(테스트와 같은 규칙) — 적 경로는 한 번의 질의로 확인한다.
        public static bool TryPath(Vector3 from, Vector3 to, NavMeshPath path, out float length, out int continued, List<Vector3> corners = null) =>
            NavPathProbe.TryConnected(from, to, path, out length, out continued, corners);

        public static bool TryPath(Vector3 from, Vector3 to, NavMeshPath path) => NavPathProbe.TryConnected(from, to, path);

        private static void WarnMissing(MapAssets assets)
        {
            if (assets.RaidConfig == null)
                Debug.LogWarning($"{Tag} RaidConfig 없음 — RaidController.config가 비고 탈출 구역 표시 반경은 {DefaultZoneRadiusM}m.");
            if (assets.CrateLootTable == null)
                Debug.LogWarning($"{Tag} 상자 루트 테이블 없음 — 일반 상자가 빈 채로 놓인다.");
            if (assets.Flare == null)
                Debug.LogWarning($"{Tag} 신호탄 아이템 없음 — 신호탄 상자가 빈 채로 놓인다.");
            if (assets.ZoneMaterial == null)
                Debug.LogWarning($"{Tag} 탈출 구역 재질 없음 — 기본 재질로 표시.");
        }

        private static long FileBytes(string assetPath)
        {
            string full = Path.GetFullPath(assetPath);
            return File.Exists(full) ? new FileInfo(full).Length : 0;
        }

        // ---------- 지형 (설계 §2.2) ----------

        internal static Terrain CreateTerrain(Transform parent)
        {
            float[,] heights = ComputeHeights();
            SetupUtil.EnsureFolder(SceneFolder);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null)
            {
                // 해상도를 먼저 정해야 size가 유지된다(M1 순서)
                data = new TerrainData { heightmapResolution = L.HeightmapResolution, size = L.TerrainSize };
                data.SetHeights(0, 0, heights);
                data.name = "Terrain-CrimsonBasin";
                AssetDatabase.CreateAsset(data, TerrainDataPath);
                Debug.Log($"{Tag} 에셋 생성: {TerrainDataPath}");
            }
            else
            {
                // 지우고 다시 만들면 .meta GUID가 바뀐다 → 기존 에셋에 덮어쓴다.
                data.heightmapResolution = L.HeightmapResolution;
                data.size = L.TerrainSize;
                data.SetHeights(0, 0, heights);
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain_CrimsonBasin";
            go.transform.SetParent(parent, false);
            go.transform.position = L.TerrainPosition;
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 5f;
            terrain.basemapDistance = 300f;
            terrain.drawInstanced = true;
            terrain.drawTreesAndFoliage = false;
            terrain.treeDistance = 0f;
            terrain.detailObjectDistance = 0f;
            return terrain;
        }

        // 결정적 높이맵 [z, x] (0~1 정규화). 같은 입력이면 같은 값(Mathf.PerlinNoise + 고정 시드 오프셋).
        internal static float[,] ComputeHeights()
        {
            int res = L.HeightmapResolution;
            var heights = new float[res, res];
            Vector2 noiseOffset = NoiseOffset();
            List<Pad> pads = BuildPads(noiseOffset);
            float step = L.TerrainSize.x / (res - 1);
            for (int zi = 0; zi < res; zi++)
            {
                float z = L.TerrainPosition.z + zi * step;
                for (int xi = 0; xi < res; xi++)
                {
                    float x = L.TerrainPosition.x + xi * step;
                    float h = RawHeightM(x, z, noiseOffset);
                    for (int p = 0; p < pads.Count; p++)
                        h = pads[p].Apply(x, z, h);
                    heights[zi, xi] = Mathf.Clamp01((h - L.TerrainPosition.y) / L.TerrainSize.y);
                }
            }
            return heights;
        }

        internal static Vector2 NoiseOffset()
        {
            var random = new System.Random(L.NoiseSeed);
            return new Vector2((float)(random.NextDouble() * 1000.0), (float)(random.NextDouble() * 1000.0));
        }

        // 패드 적용 전 높이 (m)
        internal static float RawHeightM(float x, float z, Vector2 noiseOffset)
        {
            float h = L.BasinFloorM;
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            h += L.EdgeRiseM * Smooth(L.EdgeRiseStartM, L.EdgeRiseEndM, edge);
            float n = Mathf.PerlinNoise(noiseOffset.x + x / L.NoiseWavelengthM, noiseOffset.y + z / L.NoiseWavelengthM);
            h += (n * 2f - 1f) * L.NoiseAmplitudeM;
            float hill = Vector2.Distance(new Vector2(x, z), L.HillCenterXZ);
            h += L.HillHeightM * (1f - Smooth(HillPlateauRadiusM, HillPlateauRadiusM + L.HillRadiusM, hill));
            float res = Vector2.Distance(new Vector2(x, z), L.ReservoirCenterXZ);
            h -= L.ReservoirDepthM * (1f - Smooth(L.ReservoirRadiusM * ReservoirFlatRatio, L.ReservoirRadiusM, res));
            return h;
        }

        private static float Smooth(float from, float to, float value)
        {
            float t = Mathf.Clamp01((value - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        // 평탄 패드: 사각형(중심·반크기) 또는 도로 띠(선분·반폭). 목표 높이로 블렌딩.
        private readonly struct Pad
        {
            private readonly bool isRoad;
            private readonly Vector2 a, b, half;
            private readonly float targetA, targetB, margin;

            private Pad(bool isRoad, Vector2 a, Vector2 b, Vector2 half, float targetA, float targetB, float margin)
            {
                this.isRoad = isRoad;
                this.a = a;
                this.b = b;
                this.half = half;
                this.targetA = targetA;
                this.targetB = targetB;
                this.margin = margin;
            }

            public static Pad Rect(Vector2 center, Vector2 size, float target, float margin) =>
                new Pad(false, center, center, size * 0.5f, target, target, margin);

            public static Pad Road(Vector2 from, Vector2 to, float width, float targetFrom, float targetTo, float margin) =>
                new Pad(true, from, to, new Vector2(width * 0.5f, 0f), targetFrom, targetTo, margin);

            public float Apply(float x, float z, float h)
            {
                float distance, target;
                if (isRoad)
                {
                    Vector2 ab = b - a;
                    var p = new Vector2(x, z);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    distance = Mathf.Max(Vector2.Distance(p, a + ab * t) - half.x, 0f);
                    target = Mathf.Lerp(targetA, targetB, t);
                }
                else
                {
                    float dx = Mathf.Max(Mathf.Abs(x - a.x) - half.x, 0f);
                    float dz = Mathf.Max(Mathf.Abs(z - a.y) - half.y, 0f);
                    distance = Mathf.Sqrt(dx * dx + dz * dz);
                    target = targetA;
                }
                if (distance >= margin)
                    return h;
                float w = 1f - Smooth(0f, margin, distance);
                return Mathf.Lerp(h, target, w);
            }
        }

        // 순서 = 적용 순서(뒤가 앞을 덮는다): 도로 → POI → 탈출 → 스폰.
        // 송전탑 언덕·저수지는 지형 함수 자체가 POI 모양이라 POI 패드를 두지 않는다(언덕은 고원, 저수지는 웅덩이).
        private static List<Pad> BuildPads(Vector2 noise)
        {
            var pads = new List<Pad>();
            foreach (L.RoadSegment road in L.Roads)
                pads.Add(Pad.Road(road.FromXZ, road.ToXZ, road.WidthM,
                    RawHeightM(road.FromXZ.x, road.FromXZ.y, noise), RawHeightM(road.ToXZ.x, road.ToXZ.y, noise), RoadPadMarginM));
            foreach (L.Poi poi in L.Pois)
            {
                if (poi.Id == L.Reservoir.Id)
                    continue;
                pads.Add(Pad.Rect(poi.CenterXZ, poi.FootprintXZ, RawHeightM(poi.CenterXZ.x, poi.CenterXZ.y, noise), PoiPadMarginM));
            }
            foreach (L.Extraction ext in L.Extractions)
                pads.Add(Pad.Rect(ext.CenterXZ, ext.PadSizeXZ, RawHeightM(ext.CenterXZ.x, ext.CenterXZ.y, noise), PointPadMarginM));
            foreach (L.Spawn spawn in L.Spawns)
                pads.Add(Pad.Rect(spawn.PositionXZ, Vector2.one * L.SpawnPadSizeM,
                    RawHeightM(spawn.PositionXZ.x, spawn.PositionXZ.y, noise), PointPadMarginM));
            return pads;
        }

        // ---------- 블록아웃 (설계 §2.3) ----------

        public enum Shape
        {
            LowWall,      // Low 엄폐 앞뒤 (긴 벽은 CoverSpacingM 간격)
            HighWall,     // High 엄폐 앞뒤 양 끝 + peek (M3 규칙)
            Vehicle,      // 차량 1대, Low 엄폐 4방향
            VehiclePile,  // 차량 무더기: 차량 + 위에 비스듬히 얹은 차량, 엄폐는 아래 차량 기준
            House,        // 반쯤 묻힌 집(안을 채운 박스), 긴 면 양 끝 High
            Rubble,       // 잔해 덩어리, 높이 ≤ 1.3m면 Low 앞뒤, 아니면 HighWall 규칙
            Deck,         // 고가 상판 + 기둥 2개, 엄폐 없음
            Ramp,         // 무너진 경사 상판 (한쪽 끝 지면, 반대쪽 DeckClearanceM), 엄폐 없음
            Tank,         // 원통 탱크 (MeshCollider), 엄폐 없음
            Post,         // 송전탑·배수구 기둥 (가는 높은 박스), 엄폐 없음
            Flat,         // 낮은 띠(철로·착륙장 테두리), 엄폐 없음
            Container,    // 맵 개선 §4.2: 컨테이너(녹슨 금속 박스), High 엄폐 앞뒤
            Sandbag,      // 맵 밀도 §6.2: 모래주머니 벽, Low 엄폐 앞뒤
            Fence,        // 맵 밀도 §6.2: 철망·판자 울타리(얇은 박스), 엄폐 없음(보이는 틈)
            Pole,         // 맵 밀도 §6.2: 전봇대(가는 높은 박스), 엄폐 없음
            Debris,       // 맵 밀도 §6.2: 고철·잡동사니 더미, Rubble 규칙(≤ 1.3m Low)
        }

        public readonly struct Piece
        {
            public readonly Shape Shape;
            public readonly Vector2 XZ;
            public readonly Vector3 Size;
            public readonly float YawDeg;

            public Piece(Shape shape, float x, float z, Vector3 size, float yawDeg = 0f)
            {
                Shape = shape;
                XZ = new Vector2(x, z);
                Size = size;
                YawDeg = yawDeg;
            }
        }

        private static Piece P(Shape shape, float x, float z, Vector3 size, float yaw = 0f) => new Piece(shape, x, z, size, yaw);

        // TODO(GDD-?) Q1: POI 블록아웃 배치(레벨 데이터). CrimsonBasinLayout의 적 위치·순찰 경로·상자·스위치를 막지 않게 골랐다.
        private static Piece[] HighwayPieces() => new[]
        {
            // 동서 고가 상판 조각 4 (z -275, 도로 북쪽) + 가운데 무너진 경사 상판
            P(Shape.Deck, -180f, -275f, DeckSize), P(Shape.Deck, -120f, -275f, DeckSize),
            P(Shape.Deck, -10f, -275f, DeckSize), P(Shape.Deck, 60f, -275f, DeckSize),
            P(Shape.Ramp, -65f, -275f, new Vector3(30f, 1f, 12f)),
            // 차량 무더기 8
            P(Shape.VehiclePile, -170f, -296f, CarSize, 15f), P(Shape.VehiclePile, -110f, -302f, CarSize, 80f),
            P(Shape.VehiclePile, -90f, -290f, CarSize), P(Shape.VehiclePile, -70f, -305f, CarSize, 30f),
            P(Shape.VehiclePile, -20f, -296f, CarSize, 100f), P(Shape.VehiclePile, 30f, -305f, CarSize, 10f),
            P(Shape.VehiclePile, 70f, -298f, CarSize, 60f), P(Shape.VehiclePile, -150f, -325f, CarSize, 45f),
            // 방호벽·콘크리트 판·잔해
            P(Shape.LowWall, -160f, -288f, LowWall), P(Shape.LowWall, -45f, -290f, LowWall),
            P(Shape.LowWall, 10f, -288f, LowWall), P(Shape.LowWall, 60f, -312f, LowWall),
            P(Shape.LowWall, -100f, -312f, LowWall), P(Shape.LowWall, 90f, -288f, LowWall),
            P(Shape.HighWall, -200f, -320f, HighWall, 20f), P(Shape.HighWall, 85f, -325f, HighWall, -15f),
            P(Shape.Rubble, -35f, -340f, RubbleSize, 10f), P(Shape.Rubble, 45f, -345f, RubbleSize),
        };

        private static Piece[] RefineryPieces() => new[]
        {
            // 원통 탱크 6 (진입 불가)
            P(Shape.Tank, 250f, -50f, TankSize), P(Shape.Tank, 350f, -55f, TankSize), P(Shape.Tank, 350f, -95f, TankSize),
            P(Shape.Tank, 245f, -100f, TankSize), P(Shape.Tank, 300f, -75f, TankSize), P(Shape.Tank, 305f, -20f, TankSize),
            // 배관 (지면 위 낮은 띠 = Low 엄폐)
            P(Shape.LowWall, 275f, -60f, PipeSize), P(Shape.LowWall, 325f, -40f, PipeSize, 90f),
            P(Shape.LowWall, 270f, -110f, PipeSize), P(Shape.LowWall, 325f, -75f, PipeSize, 90f),
            // 외벽 150×120m (변마다 30m 구간 + 틈)
            P(Shape.HighWall, 240f, 0f, LongWallSize), P(Shape.HighWall, 300f, 0f, LongWallSize), P(Shape.HighWall, 360f, 0f, LongWallSize),
            P(Shape.HighWall, 240f, -120f, LongWallSize), P(Shape.HighWall, 300f, -120f, LongWallSize), P(Shape.HighWall, 360f, -120f, LongWallSize),
            P(Shape.HighWall, 225f, -15f, LongWallSize, 90f), P(Shape.HighWall, 225f, -60f, LongWallSize, 90f), P(Shape.HighWall, 225f, -105f, LongWallSize, 90f),
            P(Shape.HighWall, 375f, -15f, LongWallSize, 90f), P(Shape.HighWall, 375f, -60f, LongWallSize, 90f), P(Shape.HighWall, 375f, -105f, LongWallSize, 90f),
            P(Shape.Vehicle, 260f, -15f, CarSize, 30f), P(Shape.Vehicle, 340f, -110f, CarSize, 80f),
        };

        private static Piece[] VillagePieces() => new[]
        {
            // 반쯤 묻힌 집 7 (안을 채운 박스)
            P(Shape.House, -322f, 70f, HouseSize), P(Shape.House, -322f, 100f, HouseSize, 90f), P(Shape.House, -315f, 45f, HouseSize),
            P(Shape.House, -280f, 100f, HouseSize), P(Shape.House, -350f, 135f, HouseSize), P(Shape.House, -262f, 132f, HouseSize, 90f),
            P(Shape.House, -270f, 75f, HouseSize),
            // 담장 (120×120m 둘레, 틈 있음)
            P(Shape.LowWall, -345f, 140f, FenceSize), P(Shape.LowWall, -300f, 140f, FenceSize), P(Shape.LowWall, -255f, 140f, FenceSize),
            P(Shape.LowWall, -345f, 20f, FenceSize), P(Shape.LowWall, -300f, 20f, FenceSize), P(Shape.LowWall, -255f, 20f, FenceSize),
            P(Shape.LowWall, -360f, 45f, FenceSize, 90f), P(Shape.LowWall, -360f, 115f, FenceSize, 90f),
            P(Shape.LowWall, -240f, 45f, FenceSize, 90f), P(Shape.LowWall, -240f, 115f, FenceSize, 90f),
            P(Shape.Rubble, -290f, 130f, RubbleSize, 20f), P(Shape.Rubble, -335f, 30f, RubbleSize),
            P(Shape.Vehicle, -250f, 100f, CarSize, 90f),
        };

        private static Piece[] PylonPieces() => new[]
        {
            // 송전탑 3 + 변전 설비 2
            P(Shape.Post, 200f, 340f, PylonSize), P(Shape.Post, 265f, 340f, PylonSize), P(Shape.Post, 190f, 262f, PylonSize),
            P(Shape.Rubble, 235f, 306f, new Vector3(8f, 3f, 5f)), P(Shape.Rubble, 252f, 312f, new Vector3(4f, 2.5f, 4f)),
            P(Shape.LowWall, 218f, 284f, LowWall), P(Shape.LowWall, 228f, 284f, LowWall), P(Shape.LowWall, 205f, 300f, LowWall, 90f),
            P(Shape.LowWall, 260f, 295f, LowWall, 90f), P(Shape.LowWall, 240f, 330f, LowWall), P(Shape.LowWall, 215f, 345f, LowWall),
            P(Shape.HighWall, 195f, 320f, HighWall, 30f), P(Shape.HighWall, 270f, 270f, HighWall, 60f),
            P(Shape.Vehicle, 250f, 262f, CarSize, 20f),
            P(Shape.Rubble, 185f, 300f, RubbleSize), P(Shape.Rubble, 275f, 320f, RubbleSize),
        };

        private static Piece[] ReservoirPieces() => new[]
        {
            // 남쪽 둑 (가운데 틈) + 좌초한 배 2 + 잔해
            P(Shape.HighWall, -242.5f, 256f, DikeSize), P(Shape.HighWall, -157.5f, 256f, DikeSize),
            P(Shape.Rubble, -190f, 345f, new Vector3(12f, 3f, 4f), 30f), P(Shape.Rubble, -165f, 370f, new Vector3(9f, 2.5f, 3f), -20f),
            P(Shape.Vehicle, -160f, 350f, CarSize, 40f), P(Shape.Vehicle, -245f, 345f, CarSize, 10f),
            P(Shape.LowWall, -140f, 330f, LowWall, 90f), P(Shape.LowWall, -260f, 290f, LowWall),
            P(Shape.LowWall, -120f, 390f, LowWall), P(Shape.LowWall, -215f, 400f, LowWall),
            P(Shape.Rubble, -185f, 300f, RubbleSize),
        };

        private static Piece[] ExtractionPieces()
        {
            var list = new List<Piece>
            {
                // EXT-01 남쪽 도로 끝 바리케이드
                P(Shape.LowWall, -8f, -482f, LowWall), P(Shape.LowWall, 0f, -483f, LowWall), P(Shape.LowWall, 8f, -482f, LowWall),
                P(Shape.Vehicle, -14f, -465f, CarSize, 80f),
                // EXT-02 열차 신호소 외벽
                P(Shape.House, 482f, -262f, new Vector3(6f, 4f, 5f)), P(Shape.HighWall, 462f, -240f, HighWall, 90f),
                // EXT-03 헬기 착륙장 테두리 20×20m
                P(Shape.Flat, 60f, 470f, new Vector3(20f, 0.3f, 0.5f)), P(Shape.Flat, 60f, 450f, new Vector3(20f, 0.3f, 0.5f)),
                P(Shape.Flat, 50f, 460f, new Vector3(20f, 0.3f, 0.5f), 90f), P(Shape.Flat, 70f, 460f, new Vector3(20f, 0.3f, 0.5f), 90f),
                // EXT-04 하수 배출구: 큰 박스 + 입구 기둥 2
                P(Shape.Rubble, -486f, 200f, new Vector3(6f, 5f, 10f)),
                P(Shape.Post, -481f, 195.5f, new Vector3(2f, 4f, 1f)), P(Shape.Post, -481f, 204.5f, new Vector3(2f, 4f, 1f)),
            };
            // EXT-02 철로 2줄 (신호소 앞 140m, 20m 구간 — 구간마다 지면에 맞춘다)
            for (float z = -310f; z <= -190f; z += RailSize.z)
            {
                list.Add(P(Shape.Flat, 467f, z, RailSize));
                list.Add(P(Shape.Flat, 473f, z, RailSize));
            }
            return list.ToArray();
        }

        // 지형과 무관한 고정 블록아웃 조각 전부 (POI 5 + 탈출) — 맵 밀도 EditMode 테스트가 씬 없이 장애물로 쓴다(흩어진 잔해는 지형 경사에 달려 빠진다).
        internal static List<Piece> FixedPieces()
        {
            var list = new List<Piece>();
            list.AddRange(HighwayPieces());
            list.AddRange(RefineryPieces());
            list.AddRange(VillagePieces());
            list.AddRange(PylonPieces());
            list.AddRange(ReservoirPieces());
            list.AddRange(ExtractionPieces());
            return list;
        }

        private static void CreateBlockout(BlockoutBuilder b)
        {
            b.Group("POI-HWY", HighwayPieces());
            b.Group("POI-REF", RefineryPieces());
            b.Group("POI-VIL", VillagePieces());
            b.Group("POI-PYL", PylonPieces());
            b.Group("POI-RES", ReservoirPieces());
            b.Group("Extractions", ExtractionPieces());
            b.Group("Scatter", ScatterPieces(b.Terrain));
        }

        // 흩어진 잔해·차량 (POI 사이 길목). 고정 시드. POI·탈출·스폰·도로를 피하고 경사 25° 이하에만.
        private static Piece[] ScatterPieces(Terrain terrain)
        {
            const float limit = 460f, keepOffPointsM = 40f, roadClearM = 6f, maxSlopeDeg = 25f;
            const int maxAttempts = 5000;
            var random = new System.Random(L.ScatterDebrisSeed);
            var list = new List<Piece>(L.ScatterDebrisCount);
            for (int attempt = 0; attempt < maxAttempts && list.Count < L.ScatterDebrisCount; attempt++)
            {
                float x = (float)(random.NextDouble() * 2.0 - 1.0) * limit;
                float z = (float)(random.NextDouble() * 2.0 - 1.0) * limit;
                double kind = random.NextDouble();
                float yaw = (float)(random.NextDouble() * 360.0);
                var p = new Vector2(x, z);
                if (!IsScatterFree(p, keepOffPointsM, roadClearM))
                    continue;
                if (terrain != null && Steepness(terrain, p) > maxSlopeDeg)
                    continue;
                bool tooClose = false;
                foreach (Piece other in list)
                    tooClose |= Vector2.Distance(other.XZ, p) < 15f;
                if (tooClose)
                    continue;
                if (kind < 0.4)
                    list.Add(P(Shape.Vehicle, x, z, CarSize, yaw));
                else if (kind < 0.7)
                    list.Add(P(Shape.LowWall, x, z, LowWall, yaw));
                else
                    list.Add(P(Shape.HighWall, x, z, HighWall, yaw));
            }
            if (list.Count < L.ScatterDebrisCount)
                Debug.LogWarning($"{Tag} 잔해 {list.Count}/{L.ScatterDebrisCount}개만 배치(자리 부족).");
            return list.ToArray();
        }

        private static bool IsScatterFree(Vector2 p, float keepOff, float roadClear)
        {
            foreach (L.Poi poi in L.Pois)
            {
                Vector2 half = poi.FootprintXZ * 0.5f + Vector2.one * L.ScatterMinDistanceFromPoiM;
                if (Mathf.Abs(p.x - poi.CenterXZ.x) < half.x && Mathf.Abs(p.y - poi.CenterXZ.y) < half.y)
                    return false;
            }
            foreach (L.Extraction ext in L.Extractions)
                if (Vector2.Distance(p, ext.CenterXZ) < keepOff)
                    return false;
            foreach (L.Spawn spawn in L.Spawns)
                if (Vector2.Distance(p, spawn.PositionXZ) < keepOff)
                    return false;
            foreach (L.RoadSegment road in L.Roads)
            {
                Vector2 ab = road.ToXZ - road.FromXZ;
                float t = Mathf.Clamp01(Vector2.Dot(p - road.FromXZ, ab) / ab.sqrMagnitude);
                if (Vector2.Distance(p, road.FromXZ + ab * t) < road.WidthM * 0.5f + roadClear)
                    return false;
            }
            return Vector2.Distance(p, L.PowerSwitchXZ) >= keepOff;
        }

        private static float Steepness(Terrain terrain, Vector2 xz)
        {
            Vector3 local = new Vector3(xz.x, 0f, xz.y) - terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            return terrain.terrainData.GetSteepness(local.x / size.x, local.z / size.z);
        }

        // 도형·엄폐 지점 생성기. 모든 도형은 Environment 아래, 정적 배칭.
        internal sealed class BlockoutBuilder
        {
            public readonly Terrain Terrain;
            private readonly Transform root;
            private Transform group;
            public int BlockCount;
            public int CoverCount;

            public BlockoutBuilder(Terrain terrain, Transform root)
            {
                Terrain = terrain;
                this.root = root;
            }

            public Transform NewGroup(string name)
            {
                group = new GameObject(name).transform;
                group.SetParent(root, false);
                return group;
            }

            public void Group(string name, Piece[] pieces)
            {
                NewGroup(name);
                for (int i = 0; i < pieces.Length; i++)
                    Build(pieces[i], $"{pieces[i].Shape}_{i + 1:00}");
            }

            private void Build(Piece piece, string name)
            {
                Quaternion yaw = Quaternion.Euler(0f, piece.YawDeg, 0f);
                GroundSpan(piece.XZ, piece.Size, yaw, out float minY, out float maxY);
                var structure = new GameObject(name).transform;
                structure.SetParent(group, false);
                structure.SetPositionAndRotation(new Vector3(piece.XZ.x, GroundY(Terrain, piece.XZ.x, piece.XZ.y), piece.XZ.y), yaw);

                Vector3 size = piece.Size;
                switch (piece.Shape)
                {
                    case Shape.Deck:
                    {
                        float deckBottom = maxY + DeckClearanceM;
                        Block(structure, "Deck", PrimitiveType.Cube, piece.XZ, deckBottom, deckBottom + size.y, size, yaw);
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 offset = yaw * new Vector3(side * (size.x * 0.5f - DeckPillarInsetM), 0f, 0f);
                            var at = new Vector2(piece.XZ.x + offset.x, piece.XZ.y + offset.z);
                            GroundSpan(at, PillarSize, yaw, out float pMin, out _);
                            Block(structure, "Pillar", PrimitiveType.Cube, at, pMin - SinkM, deckBottom, PillarSize, yaw);
                        }
                        return;
                    }
                    case Shape.Ramp:
                    {
                        // 한쪽 끝(-x)이 지면, 반대쪽(+x)이 상판 높이. 로컬 x 방향 경사.
                        float angle = Mathf.Atan2(DeckClearanceM, size.x) * Mathf.Rad2Deg;
                        float length = Mathf.Sqrt(size.x * size.x + DeckClearanceM * DeckClearanceM);
                        var go = Primitive(PrimitiveType.Cube, "Slab", structure);
                        go.transform.SetPositionAndRotation(
                            new Vector3(piece.XZ.x, minY + DeckClearanceM * 0.5f, piece.XZ.y),
                            Quaternion.Euler(0f, piece.YawDeg, angle));
                        go.transform.localScale = new Vector3(length, size.y, size.z);
                        return;
                    }
                    case Shape.VehiclePile:
                    {
                        float top = maxY + size.y;
                        Block(structure, "Car", PrimitiveType.Cube, piece.XZ, minY - SinkM, top, size, yaw);
                        Quaternion upperYaw = Quaternion.Euler(0f, piece.YawDeg + 25f, 0f);
                        Vector3 shift = yaw * new Vector3(0.3f, 0f, 0.2f);
                        Block(structure, "Car_Upper", PrimitiveType.Cube, piece.XZ + new Vector2(shift.x, shift.z),
                            top, top + size.y, size, upperYaw);
                        CoversAround(structure, size);
                        return;
                    }
                    case Shape.Tank:
                        Block(structure, "Tank", PrimitiveType.Cylinder, piece.XZ, minY - SinkM, maxY + size.y, size, yaw);
                        return;
                    case Shape.Post:
                    case Shape.Flat:
                    case Shape.Pole:
                    case Shape.Fence:
                        Block(structure, "Block", PrimitiveType.Cube, piece.XZ, minY - SinkM, maxY + size.y, size, yaw);
                        return;
                }

                Block(structure, "Block", PrimitiveType.Cube, piece.XZ, minY - SinkM, maxY + size.y, size, yaw);
                switch (piece.Shape)
                {
                    case Shape.LowWall:
                        CoversFrontBack(structure, size, CoverHeight.Low);
                        break;
                    case Shape.HighWall:
                    case Shape.House:
                    case Shape.Container:
                        CoversFrontBack(structure, size, CoverHeight.High);
                        break;
                    case Shape.Rubble:
                    case Shape.Debris:
                        CoversFrontBack(structure, size, size.y <= LowCoverMaxHeightM ? CoverHeight.Low : CoverHeight.High);
                        break;
                    case Shape.Sandbag:
                        CoversFrontBack(structure, size, CoverHeight.Low);
                        break;
                    case Shape.Vehicle:
                        CoversAround(structure, size);
                        break;
                }
            }

            // 발자국(회전한 사각형) 네 모서리 + 중심의 지면 높이 범위
            private void GroundSpan(Vector2 xz, Vector3 size, Quaternion yaw, out float minY, out float maxY)
            {
                minY = maxY = GroundY(Terrain, xz.x, xz.y);
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 c = yaw * new Vector3(sx * size.x * 0.5f, 0f, sz * size.z * 0.5f);
                    float y = GroundY(Terrain, xz.x + c.x, xz.y + c.z);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            // 바닥 bottomY ~ 윗면 topY인 도형. 크기 x·z는 size.
            private void Block(Transform parent, string name, PrimitiveType type, Vector2 xz, float bottomY, float topY, Vector3 size, Quaternion yaw)
            {
                var go = Primitive(type, name, parent);
                go.transform.SetPositionAndRotation(new Vector3(xz.x, (bottomY + topY) * 0.5f, xz.y), yaw);
                float height = topY - bottomY;
                // 프리미티브 원통은 높이 2m → y 스케일 절반. 캡슐 콜라이더 대신 메시 콜라이더(바닥까지 막히게).
                if (type == PrimitiveType.Cylinder)
                {
                    go.transform.localScale = new Vector3(size.x, height * 0.5f, size.z);
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                }
                else
                {
                    go.transform.localScale = new Vector3(size.x, height, size.z);
                }
            }

            private GameObject Primitive(PrimitiveType type, string name, Transform parent)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.SetParent(parent, true);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                BlockCount++;
                return go;
            }

            // M3 규칙: 구조물 면에서 CoverGapM 떨어진 앞(-z)·뒤(+z). Low = 가운데(긴 벽은 간격마다), High = 양 끝 + peek(끝 밖).
            internal void CoversFrontBack(Transform structure, Vector3 size, CoverHeight height)
            {
                float d = size.z * 0.5f + M3SceneSetup.CoverGapM;
                if (height == CoverHeight.Low)
                {
                    int count = Mathf.Max(1, Mathf.RoundToInt(size.x / CoverSpacingM));
                    for (int i = 0; i < count; i++)
                    {
                        float x = (i + 0.5f) / count * size.x - size.x * 0.5f;
                        Cover(structure, new Vector3(x, 0f, -d), Vector3.forward, height, null);
                        Cover(structure, new Vector3(x, 0f, d), Vector3.back, height, null);
                    }
                    return;
                }
                float peekX = size.x * 0.5f + PeekBeyondEndM;
                if (size.x <= 2f * M3SceneSetup.HighPeekSideM)
                {
                    // 짧은 벽 (M3와 같음): 앞뒤 가운데, peek는 서로 반대쪽 끝
                    Cover(structure, new Vector3(0f, 0f, -d), Vector3.forward, height, new Vector3(peekX, 0f, -d));
                    Cover(structure, new Vector3(0f, 0f, d), Vector3.back, height, new Vector3(-peekX, 0f, d));
                    return;
                }
                float endX = size.x * 0.5f - 1f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Cover(structure, new Vector3(side * endX, 0f, -d), Vector3.forward, height, new Vector3(side * peekX, 0f, -d));
                    Cover(structure, new Vector3(side * endX, 0f, d), Vector3.back, height, new Vector3(side * peekX, 0f, d));
                }
            }

            internal void CoversAround(Transform structure, Vector3 size)
            {
                float dx = size.x * 0.5f + M3SceneSetup.CoverGapM;
                float dz = size.z * 0.5f + M3SceneSetup.CoverGapM;
                Cover(structure, new Vector3(-dx, 0f, 0f), Vector3.right, CoverHeight.Low, null);
                Cover(structure, new Vector3(dx, 0f, 0f), Vector3.left, CoverHeight.Low, null);
                Cover(structure, new Vector3(0f, 0f, -dz), Vector3.forward, CoverHeight.Low, null);
                Cover(structure, new Vector3(0f, 0f, dz), Vector3.back, CoverHeight.Low, null);
            }

            // M3SceneSetup.CreateCoverPoint로 만들고 지면에 붙인다. peek은 구조물 로컬 → 엄폐 지점 로컬(지면 높이 반영).
            private void Cover(Transform structure, Vector3 local, Vector3 facing, CoverHeight height, Vector3? peekInStructure)
            {
                M3SceneSetup.CreateCoverPoint(structure, local, facing, height, Vector3.zero);
                Transform cover = structure.GetChild(structure.childCount - 1);
                Vector3 p = cover.position;
                cover.position = new Vector3(p.x, GroundY(Terrain, p.x, p.z), p.z);
                CoverCount++;
                if (peekInStructure == null)
                    return;
                Vector3 peek = structure.TransformPoint(peekInStructure.Value);
                peek.y = GroundY(Terrain, peek.x, peek.z);
                var so = new SerializedObject(cover.GetComponent<CoverPoint>());
                so.FindProperty("peekLocalOffset").vector3Value = cover.InverseTransformPoint(peek);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // 상자 (정적 배칭 안 함: LootContainer 상태를 가진다). Environment 아래라 NavMesh에서 장애물로 구워진다.
            public GameObject Crate(string name, Vector2 xz)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(group, false);
                go.transform.position = GroundPoint(Terrain, xz) + new Vector3(0f, CrateSize.y * 0.5f, 0f);
                go.transform.localScale = CrateSize;
                return go;
            }
        }

        // ---------- 루트 상자 (Q7·Q17) ----------

        private static int CreateLootCrates(BlockoutBuilder b, MapAssets assets)
        {
            b.NewGroup("LootCrates");
            int count = 0;
            for (int i = 0; i < L.LootCrates.Length; i++)
            {
                L.Crate crate = L.LootCrates[i];
                // TODO(GDD-?) Q17: 시드 0 = 열 때마다 무작위
                SetupCrate(b.Crate($"Crate_{crate.PoiId}_{i + 1:00}", crate.PositionXZ), assets.CrateLootTable, null);
                count++;
            }
            for (int i = 0; i < L.FlareCrates.Length; i++)
            {
                L.Crate crate = L.FlareCrates[i];
                // TODO(GDD-?) Q7: 신호탄 고정 상자 = 신호탄 1개만(테이블 굴림 없음). 겉모습·이름은 일반 상자와 같다.
                SetupCrate(b.Crate($"FlareCrate_{crate.PoiId}_{i + 1:00}", crate.PositionXZ), null, assets.Flare);
                count++;
            }
            return count;
        }

        private static void SetupCrate(GameObject box, LootTableSO table, ItemDefinitionSO fixedItem)
        {
            var container = box.AddComponent<LootContainer>();
            var so = new SerializedObject(container);
            so.FindProperty("displayName").stringValue = CrateDisplayName;
            so.FindProperty("gridSize").vector2IntValue = CrateGridSize;
            SerializedProperty contents = so.FindProperty("contents");
            contents.arraySize = fixedItem != null ? 1 : 0;
            if (fixedItem != null)
                contents.GetArrayElementAtIndex(0).objectReferenceValue = fixedItem;
            so.FindProperty("lootTable").objectReferenceValue = table;
            so.FindProperty("seed").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- 경계 (Q20) ----------

        internal static GameObject CreateBoundary()
        {
            var root = new GameObject(BoundsName);
            float inset = L.BoundaryInsetM, t = L.BoundaryThicknessM, h = L.BoundaryHeightM;
            float span = inset * 2f + t * 2f;
            float c = inset + t * 0.5f;
            AddWall(root.transform, "Wall_E", new Vector3(c, h * 0.5f, 0f), new Vector3(t, h, span));
            AddWall(root.transform, "Wall_W", new Vector3(-c, h * 0.5f, 0f), new Vector3(t, h, span));
            AddWall(root.transform, "Wall_N", new Vector3(0f, h * 0.5f, c), new Vector3(span, h, t));
            AddWall(root.transform, "Wall_S", new Vector3(0f, h * 0.5f, -c), new Vector3(span, h, t));
            return root;
        }

        private static void AddWall(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        // ---------- NavMesh (설계 §2.4) ----------

        // M3 BakeNavMesh와 같은 방식(Environment 자식의 물리 콜라이더, CopySerialized로 GUID 유지) + 굽기 시간 측정·설정 덮어쓰기.
        internal static bool BakeNavMesh(GameObject environment, string assetPath, NavBakeSettings settings,
            out NavMeshSurface surface, out double seconds)
        {
            surface = environment.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            if (settings.VoxelSizeM > 0f)
            {
                surface.overrideVoxelSize = true;
                surface.voxelSize = settings.VoxelSizeM;
            }
            if (settings.TileSizeVoxels > 0)
            {
                surface.overrideTileSize = true;
                surface.tileSize = settings.TileSizeVoxels;
            }
            if (settings.MinRegionArea > 0f)
                surface.minRegionArea = settings.MinRegionArea;
            return RebakeNavMesh(surface, assetPath, out seconds);
        }

        // 이미 설정된 NavMeshSurface로 다시 굽고 같은 에셋에 덮어쓴다(GUID 유지). 맵 개선 메뉴가 구조물을 더한 뒤에도 부른다.
        internal static bool RebakeNavMesh(NavMeshSurface surface, string assetPath, out double seconds)
        {
            GameObject environment = surface.gameObject;
            Physics.SyncTransforms();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            surface.BuildNavMesh();
            watch.Stop();
            seconds = watch.Elapsed.TotalSeconds;
            NavMeshData built = surface.navMeshData;
            if (built == null)
            {
                Debug.LogError($"{Tag} NavMesh 굽기 실패: {assetPath}");
                return false;
            }

            SetupUtil.EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath);
            if (existing == null)
            {
                built.name = "NavMesh-Environment";
                AssetDatabase.CreateAsset(built, assetPath);
            }
            else
            {
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

            var saved = AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath);
            if (saved == null || surface.navMeshData != saved)
            {
                Debug.LogError($"{Tag} NavMeshData 저장 실패: {assetPath}");
                return false;
            }
            Vector2 probe = L.Spawns[0].PositionXZ;
            Terrain terrain = environment.GetComponentInChildren<Terrain>();
            if (!NavMesh.SamplePosition(GroundPoint(terrain, probe), out NavMeshHit _, 2f, NavMesh.AllAreas))
            {
                Debug.LogError($"{Tag} 구운 NavMesh에서 스폰 {L.Spawns[0].Id} 샘플 실패: {assetPath}");
                return false;
            }
            Debug.Log($"{Tag} NavMesh 굽기 {seconds:0.0}s: {assetPath} (sourceBounds {saved.sourceBounds.size}, " +
                      $"voxel {(surface.overrideVoxelSize ? surface.voxelSize.ToString("0.###") : "기본")}, " +
                      $"tile {(surface.overrideTileSize ? surface.tileSize.ToString() : "기본")})");
            return true;
        }

        // ---------- 레이드 오브젝트 (최상위) ----------

        internal static RaidController CreateRaidObjects(Terrain terrain, MapAssets assets)
        {
            var controllerGo = new GameObject(nameof(RaidController));
            var controller = controllerGo.AddComponent<RaidController>();
            SetupUtil.SetFields(controller, ("config", assets.RaidConfig));
            var so = new SerializedObject(controller);
            so.FindProperty("seed").intValue = 0; // 0 = 매번 무작위 (설계 §3.2)
            so.ApplyModifiedPropertiesWithoutUndo();

            var extractionRoot = new GameObject("ExtractionZones").transform;
            foreach (L.Extraction ext in L.Extractions)
            {
                var go = new GameObject($"Extraction_{ext.Id}");
                go.transform.SetParent(extractionRoot, false);
                go.transform.position = GroundPoint(terrain, ext.CenterXZ);
                SetString(go.AddComponent<ExtractionZone>(), ("id", ext.Id));
                CreateZoneMarker(go.transform, ZoneRadius(assets.RaidConfig, ext.Id), assets.ZoneMaterial);
            }

            var spawnRoot = new GameObject("PlayerSpawns").transform;
            foreach (L.Spawn spawn in L.Spawns)
            {
                var go = new GameObject($"Spawn_{spawn.Id}");
                go.transform.SetParent(spawnRoot, false);
                go.transform.SetPositionAndRotation(GroundPoint(terrain, spawn.PositionXZ), Quaternion.Euler(0f, spawn.YawDeg, 0f));
                SetString(go.AddComponent<PlayerSpawnPoint>(), ("id", spawn.Id));
            }

            var poiRoot = new GameObject("PointsOfInterest").transform;
            foreach (L.Poi poi in L.Pois)
            {
                var go = new GameObject($"POI_{poi.Id}");
                go.transform.SetParent(poiRoot, false);
                go.transform.position = GroundPoint(terrain, poi.CenterXZ);
                SetString(go.AddComponent<PointOfInterest>(), ("id", poi.Id), ("displayName", poi.DisplayName));
            }

            CreatePowerSwitch(terrain);
            return controller;
        }

        // EXT-02 전원 스위치 (Q5): 상자 모양 패널(BoxCollider = F 레이캐스트 대상). 시선 yaw 쪽이 앞면.
        private static void CreatePowerSwitch(Terrain terrain)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PowerSwitch_" + L.PowerSwitchId;
            go.transform.SetPositionAndRotation(
                GroundPoint(terrain, L.PowerSwitchXZ) + new Vector3(0f, SwitchBottomM + SwitchSize.y * 0.5f, 0f),
                Quaternion.Euler(0f, L.PowerSwitchYawDeg, 0f));
            go.transform.localScale = SwitchSize;
            SetString(go.AddComponent<PowerSwitch>(), ("switchId", L.PowerSwitchId), ("displayName", L.PowerSwitchDisplayName));
        }

        private static float ZoneRadius(RaidConfigSO config, string id)
        {
            if (config != null && config.extractions != null)
                foreach (ExtractionPointConfig ext in config.extractions)
                    if (ext.id == id && ext.radiusM > 0f)
                        return ext.radiusM;
            return DefaultZoneRadiusM;
        }

        // 바닥 원판 표시 (콜라이더 없음). TODO(GDD-?): 반경은 생성 시점 config 값 — config를 바꾸면 메뉴 재실행.
        private static void CreateZoneMarker(Transform zone, float radius, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "ZoneMarker";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(zone, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, ZoneMarkerThicknessM * 0.5f, radius * 2f);
            var renderer = go.GetComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void SetString(Object target, params (string name, string value)[] fields)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in fields)
            {
                SerializedProperty property = so.FindProperty(name);
                if (property == null)
                {
                    Debug.LogError($"{Tag} {target.GetType().Name}에 직렬화 필드 '{name}'이 없다.", target);
                    continue;
                }
                property.stringValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
