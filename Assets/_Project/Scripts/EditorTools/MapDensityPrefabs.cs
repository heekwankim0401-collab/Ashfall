using System;
using System.Collections.Generic;
using Ashfall.Audio;
using Ashfall.Raid;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // 맵 밀도 산포물 메시·프리팹 (docs/design/MapDensity_design.md §6.2·§8.1, 사용자 결정 Q3 A: 자체 생성 메시, git 포함 — 패키지 불필요).
    // 메시(Art/Meshes/Scatter/*.asset)와 프리팹(Prefabs/Scatter/SC_*.prefab)은 **없을 때만** 만든다 — 같은 경로에 모델을 덮어쓰면 유지(모델 교체).
    // LOD 거리·그림자는 MapDensityConfigSO에서 매 실행 다시 맞춘다(RefreshPrefabs, 값이 같으면 파일을 바꾸지 않는다).
    // 모양·크기 전부 TODO(GDD-?) 자리표시. 원점 = 지면 중심, 나무·통나무만 콜라이더(+ GroundSurface), 덤불·풀은 콜라이더 없음.
    public static class MapDensityPrefabs
    {
        private const string Tag = "[MapDensityPrefabs]";

        public const string MeshFolder = M6Names.ArtFolder + "/Meshes/Scatter";
        public const string PrefabFolder = M7Names.PrefabFolder + "/Scatter";
        public const string Lod0Child = "LOD0";
        public const string Lod1Child = "LOD1";

        // ── 변형 (레벨 데이터) ──
        public readonly struct TreeSpec
        {
            public readonly float HeightM;
            public readonly float TrunkRadiusM;
            public readonly int Branches;
            public readonly int Seed;
            public readonly PaletteMaterialMap.Key Material;

            public TreeSpec(float heightM, float trunkRadiusM, int branches, int seed, PaletteMaterialMap.Key material)
            {
                HeightM = heightM;
                TrunkRadiusM = trunkRadiusM;
                Branches = branches;
                Seed = seed;
                Material = material;
            }
        }

        // 죽은 나무 4종 (키 5.5~9.5m × 배율 0.8~1.3 → 약 4.4~12.4m, 설계 "키 5~12m"). 회색·검게 탄 줄기 번갈아.
        public static readonly TreeSpec[] Trees =
        {
            new TreeSpec(5.5f, 0.22f, 4, 101, PaletteMaterialMap.Key.DeadWood),
            new TreeSpec(7.0f, 0.26f, 5, 102, PaletteMaterialMap.Key.CharredWood),
            new TreeSpec(8.5f, 0.30f, 6, 103, PaletteMaterialMap.Key.DeadWood),
            new TreeSpec(9.5f, 0.34f, 3, 104, PaletteMaterialMap.Key.CharredWood),
        };

        public static float[] TreeTrunkRadii()
        {
            var radii = new float[Trees.Length];
            for (int i = 0; i < Trees.Length; i++)
                radii[i] = Trees[i].TrunkRadiusM;
            return radii;
        }

        public const int BushVariants = 2;
        public const float BushMeshHeightM = 0.85f;      // 배율 1.15에서도 1.0m 이하 (MapDensityConfigSO.bushMaxHeightM)
        public const float LogLengthM = 4.2f;
        public const float LogRadiusM = 0.28f;
        public const float LogSinkM = 0.05f;

        // 삼각형 수 상한 (설계 §6.2: 나무 LOD0 약 200·LOD1 약 60, 풀 약 12) — 테스트가 확인
        public const int TreeLod0MaxTriangles = 220;
        public const int TreeLod1MaxTriangles = 64;
        public const int BushMaxTriangles = 80;
        public const int LogMaxTriangles = 40;
        public const int GrassMaxTriangles = 12;

        public static string TreePrefabPath(int variant) => $"{PrefabFolder}/SC_DeadTree_{variant + 1:00}.prefab";
        public static string BushPrefabPath(int variant) => $"{PrefabFolder}/SC_Bush_{variant + 1:00}.prefab";
        public static string LogPrefabPath => $"{PrefabFolder}/SC_Log_01.prefab";
        public static string GrassPrefabPath(bool tall) => $"{PrefabFolder}/SC_Grass_{(tall ? "Tall" : "Short")}.prefab";

        public static IEnumerable<string> AllPrefabPaths()
        {
            for (int i = 0; i < Trees.Length; i++)
                yield return TreePrefabPath(i);
            for (int i = 0; i < BushVariants; i++)
                yield return BushPrefabPath(i);
            yield return LogPrefabPath;
            yield return GrassPrefabPath(false);
            yield return GrassPrefabPath(true);
        }

        public static bool AssetsExist()
        {
            foreach (string path in AllPrefabPaths())
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    return false;
            return true;
        }

        // ---------- LOD 거리 ↔ 화면 비율 (설계 §5.1 "SO 값 = 실제 거리") ----------

        // Unity LODGroup 화면 상대 높이 = size/2 × lodBias / (거리 × tan(fov/2)) (LODUtility와 같은 식, lodBias는 거리를 그만큼 늘린 효과).
        public static float RelativeHeight(float distanceM, float worldSizeM, float fovDeg, float lodBias)
        {
            float tanHalf = Mathf.Tan(Mathf.Clamp(fovDeg, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
            return Mathf.Clamp(worldSizeM * 0.5f * Mathf.Max(0.01f, lodBias) / (Mathf.Max(0.01f, distanceM) * tanHalf), 1e-4f, 1f);
        }

        public static float DistanceOf(float relativeHeight, float worldSizeM, float fovDeg, float lodBias)
        {
            float tanHalf = Mathf.Tan(Mathf.Clamp(fovDeg, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
            return worldSizeM * 0.5f * Mathf.Max(0.01f, lodBias) / (Mathf.Max(1e-4f, relativeHeight) * tanHalf);
        }

        // 거리 목록(오름차순) → LODGroup 전환 비율(내림차순, 엄밀히 감소)
        public static float[] Thresholds(float[] distancesM, float worldSizeM, float fovDeg, float lodBias)
        {
            var result = new float[distancesM.Length];
            float previous = 1f;
            for (int i = 0; i < distancesM.Length; i++)
            {
                float h = RelativeHeight(distancesM[i], worldSizeM, fovDeg, lodBias);
                h = Mathf.Min(h, previous * 0.999f);
                result[i] = Mathf.Round(h * 1e5f) / 1e5f;   // 직렬화 값이 실행마다 같게
                previous = result[i];
            }
            return result;
        }

        // ---------- 생성 (없을 때만) ----------

        [MenuItem("Ashfall/Setup/Map Density/Create Scatter Prefabs")]
        public static void CreatePrefabsMenu()
        {
            if (!CreatePrefabs())
                throw new InvalidOperationException($"{Tag} 실패 — 위 오류 로그 확인");
        }

        public static bool CreatePrefabs()
        {
            Dictionary<PaletteMaterialMap.Key, Material> materials = M6VisualSceneApply.LoadPaletteMaterials();
            foreach (PaletteMaterialMap.Key key in new[] { PaletteMaterialMap.Key.DeadWood, PaletteMaterialMap.Key.CharredWood,
                         PaletteMaterialMap.Key.DryGrass, PaletteMaterialMap.Key.DryGrassTall })
                if (materials[key] == null)
                {
                    Debug.LogError($"{Tag} 팔레트 머티리얼 없음: {PaletteMaterialMap.Materials[key].Path} — 먼저 Ashfall/Setup/Apply M6 Art & Audio");
                    return false;
                }
            SetupUtil.EnsureFolder(MeshFolder);
            SetupUtil.EnsureFolder(PrefabFolder);

            for (int i = 0; i < Trees.Length; i++)
            {
                if (Exists(TreePrefabPath(i)))
                    continue;
                TreeSpec spec = Trees[i];
                BuildTreeMeshes(spec, out Mesh lod0, out Mesh lod1);
                lod0 = SaveMesh(lod0, $"{MeshFolder}/SC_DeadTree_{i + 1:00}_LOD0.asset");
                lod1 = SaveMesh(lod1, $"{MeshFolder}/SC_DeadTree_{i + 1:00}_LOD1.asset");
                var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(TreePrefabPath(i)));
                Renderer r0 = AddRenderer(root, Lod0Child, lod0, materials[spec.Material]);
                Renderer r1 = AddRenderer(root, Lod1Child, lod1, materials[spec.Material]);
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.radius = spec.TrunkRadiusM;
                capsule.height = spec.HeightM * 0.85f;
                capsule.center = new Vector3(0f, spec.HeightM * 0.85f * 0.5f - 0.2f, 0f);
                SetSurface(root.AddComponent<GroundSurface>(), SurfaceKind.Debris);
                AddLodGroup(root, new[] { new LOD(0.5f, new[] { r0 }), new LOD(0.1f, new[] { r1 }) });
                Save(root, TreePrefabPath(i));
            }
            for (int i = 0; i < BushVariants; i++)
            {
                if (Exists(BushPrefabPath(i)))
                    continue;
                Mesh mesh = SaveMesh(BuildBushMesh(201 + i, i == 0 ? 14 : 18), $"{MeshFolder}/SC_Bush_{i + 1:00}.asset");
                var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(BushPrefabPath(i)));
                Renderer r = AddRenderer(root, Lod0Child, mesh, materials[i == 0 ? PaletteMaterialMap.Key.DryGrass : PaletteMaterialMap.Key.DeadWood]);
                AddLodGroup(root, new[] { new LOD(0.05f, new[] { r }) });
                Save(root, BushPrefabPath(i));
            }
            if (!Exists(LogPrefabPath))
            {
                Mesh mesh = SaveMesh(BuildLogMesh(), $"{MeshFolder}/SC_Log_01.asset");
                var root = new GameObject("SC_Log_01");
                Renderer r = AddRenderer(root, Lod0Child, mesh, materials[PaletteMaterialMap.Key.DeadWood]);
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.direction = 0;
                capsule.radius = LogRadiusM;
                capsule.height = LogLengthM;
                capsule.center = new Vector3(0f, LogRadiusM - LogSinkM, 0f);
                SetSurface(root.AddComponent<GroundSurface>(), SurfaceKind.Debris);
                AddLodGroup(root, new[] { new LOD(0.05f, new[] { r }) });
                Save(root, LogPrefabPath);
            }
            foreach (bool tall in new[] { false, true })
            {
                if (Exists(GrassPrefabPath(tall)))
                    continue;
                Mesh mesh = SaveMesh(BuildGrassMesh(tall ? 302 : 301, tall), $"{MeshFolder}/SC_Grass_{(tall ? "Tall" : "Short")}.asset");
                var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(GrassPrefabPath(tall)));
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[tall ? PaletteMaterialMap.Key.DryGrassTall : PaletteMaterialMap.Key.DryGrass];
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                Save(root, GrassPrefabPath(tall));
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        private static bool Exists(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;

        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                Object.DestroyImmediate(mesh);
                return existing;   // 메시만 남아 있으면 그대로 쓴다(모델 교체 보존)
            }
            AssetDatabase.CreateAsset(mesh, path);
            Debug.Log($"{Tag} 메시 생성: {path} (삼각형 {mesh.triangles.Length / 3})");
            return mesh;
        }

        private static Renderer AddRenderer(GameObject root, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private static void AddLodGroup(GameObject root, LOD[] lods)
        {
            var group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        private static void Save(GameObject root, string path)
        {
            try
            {
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success || saved == null)
                    throw new InvalidOperationException($"{Tag} 프리팹 저장 실패: {path}");
                Debug.Log($"{Tag} 프리팹 생성: {path}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void SetSurface(GroundSurface surface, SurfaceKind kind)
        {
            var so = new SerializedObject(surface);
            so.FindProperty("kind").intValue = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- LOD·그림자 (매 실행, SO 값) ----------

        // 나무·덤불·통나무 프리팹의 LOD 전환·그림자를 SO 값으로 맞춘다. 바꾼 프리팹 수. fov·lodBias = 플레이어 카메라·품질 단계.
        public static int RefreshPrefabs(MapDensityConfigSO cfg, float fovDeg, float lodBias)
        {
            int changed = 0;
            for (int i = 0; i < Trees.Length; i++)
                changed += RefreshPrefab(TreePrefabPath(i), new[] { cfg.treeLod1DistanceM, cfg.treeCullDistanceM },
                    new[] { true, cfg.treeLod1Shadows }, fovDeg, lodBias);
            for (int i = 0; i < BushVariants; i++)
                changed += RefreshPrefab(BushPrefabPath(i), new[] { cfg.bushCullDistanceM }, new[] { BushMeshHeightM >= cfg.shadowMinHeightM }, fovDeg, lodBias);
            changed += RefreshPrefab(LogPrefabPath, new[] { cfg.logCullDistanceM }, new[] { LogRadiusM * 2f >= cfg.shadowMinHeightM }, fovDeg, lodBias);
            return changed;
        }

        private static int RefreshPrefab(string path, float[] distances, bool[] shadows, float fovDeg, float lodBias)
        {
            if (!Exists(path))
                return 0;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!ApplyLods(contents.GetComponent<LODGroup>(), distances, shadows, fovDeg, lodBias))
                    return 0;
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                Debug.Log($"{Tag} LOD·그림자 갱신: {path}");
                return 1;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // LODGroup의 LOD마다 전환 비율·렌더러 그림자를 맞춘다(렌더러 목록은 그대로). 바뀌었으면 true.
        // 크기는 LODGroup.size(로컬) × 루트 배율 — 프리팹은 배율 1, 씬 인스턴스 배율이 다르면 전환 거리도 비례(나무 0.8~1.3배, 구현 메모).
        internal static bool ApplyLods(LODGroup group, float[] distances, bool[] shadows, float fovDeg, float lodBias, bool setShadows = true)
        {
            if (group == null)
                return false;
            LOD[] lods = group.GetLODs();
            if (lods.Length != distances.Length)
                return false;
            Vector3 s = group.transform.lossyScale;
            float worldSize = group.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
            float[] thresholds = Thresholds(distances, worldSize, fovDeg, lodBias);
            bool changed = false;
            for (int i = 0; i < lods.Length; i++)
            {
                if (!Mathf.Approximately(lods[i].screenRelativeTransitionHeight, thresholds[i]))
                {
                    lods[i].screenRelativeTransitionHeight = thresholds[i];
                    changed = true;
                }
                if (!setShadows)
                    continue;
                ShadowCastingMode mode = shadows[i] ? ShadowCastingMode.On : ShadowCastingMode.Off;
                foreach (Renderer r in lods[i].renderers)
                    if (r != null && r.shadowCastingMode != mode)
                    {
                        r.shadowCastingMode = mode;
                        EditorUtility.SetDirty(r);
                        changed = true;
                    }
            }
            if (changed)
            {
                group.SetLODs(lods);
                EditorUtility.SetDirty(group);
            }
            return changed;
        }

        // ---------- 메시 (결정적 — seed 고정) ----------

        private sealed class MeshBuilder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();

            // 각진 면(평면 셰이딩): 삼각형마다 정점을 따로 둔다
            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = Vertices.Count;
                Vertices.Add(a);
                Vertices.Add(b);
                Vertices.Add(c);
                Triangles.Add(i);
                Triangles.Add(i + 1);
                Triangles.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            // a(반지름 ra) → b(반지름 rb) 각기둥 옆면 (sides각)
            public void Frustum(Vector3 a, Vector3 b, float ra, float rb, int sides, float twistDeg = 0f)
            {
                Vector3 axis = (b - a).normalized;
                Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
                Vector3 v = Vector3.Cross(axis, u);
                for (int i = 0; i < sides; i++)
                {
                    float t0 = (i / (float)sides) * Mathf.PI * 2f + twistDeg * Mathf.Deg2Rad;
                    float t1 = ((i + 1) / (float)sides) * Mathf.PI * 2f + twistDeg * Mathf.Deg2Rad;
                    Vector3 d0 = u * Mathf.Cos(t0) + v * Mathf.Sin(t0), d1 = u * Mathf.Cos(t1) + v * Mathf.Sin(t1);
                    Quad(a + d0 * ra, b + d0 * rb, b + d1 * rb, a + d1 * ra);
                }
            }

            public Mesh ToMesh(string name, bool upNormals = false)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Vertices);
                mesh.SetTriangles(Triangles, 0);
                if (upNormals)
                {
                    var normals = new List<Vector3>(Vertices.Count);
                    for (int i = 0; i < Vertices.Count; i++)
                        normals.Add(Vector3.up);
                    mesh.SetNormals(normals);
                }
                else
                    mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private readonly struct Branch
        {
            public readonly Vector3 Start, Mid, End, TwigEnd;
            public readonly float Radius;

            public Branch(Vector3 start, Vector3 mid, Vector3 end, Vector3 twigEnd, float radius)
            {
                Start = start;
                Mid = mid;
                End = end;
                TwigEnd = twigEnd;
                Radius = radius;
            }
        }

        // 줄기(휜 3마디 6각) + 가지 n개(2마디 4각 + 잔가지). LOD1 = 줄기 2마디 4각 + 가지 앞 3개 1마디 3각.
        public static void BuildTreeMeshes(TreeSpec spec, out Mesh lod0, out Mesh lod1)
        {
            var random = new System.Random(spec.Seed);
            float h = spec.HeightM, r0 = spec.TrunkRadiusM;
            float Jit(float a) => (float)(random.NextDouble() * 2.0 - 1.0) * a;
            var trunk = new[]
            {
                new Vector3(0f, -0.3f, 0f),
                new Vector3(Jit(0.15f), h * 0.35f, Jit(0.15f)),
                new Vector3(Jit(0.35f), h * 0.7f, Jit(0.35f)),
                new Vector3(Jit(0.6f), h, Jit(0.6f)),
            };
            float[] radii = { r0, r0 * 0.7f, r0 * 0.45f, r0 * 0.12f };

            var branches = new List<Branch>();
            float azimuth = (float)(random.NextDouble() * 360.0);
            for (int i = 0; i < spec.Branches; i++)
            {
                azimuth += 360f / spec.Branches + Jit(35f);
                float at = 0.35f + 0.55f * (i + 0.5f) / spec.Branches + Jit(0.05f);
                Vector3 start = TrunkPoint(trunk, at);
                float elevation = 25f + (float)random.NextDouble() * 35f;
                float length = h * (0.25f + (float)random.NextDouble() * 0.2f) * (1f - at * 0.4f);
                Vector3 dir = Quaternion.Euler(-elevation, azimuth, 0f) * Vector3.forward;
                Vector3 bend = Quaternion.Euler(-(elevation + 15f + Jit(10f)), azimuth + Jit(25f), 0f) * Vector3.forward;
                Vector3 mid = start + dir * length * 0.5f;
                Vector3 end = mid + bend * length * 0.5f;
                Vector3 twig = mid + (Quaternion.Euler(-(elevation + 30f), azimuth + 50f * (Jit(1f) > 0 ? 1f : -1f), 0f) * Vector3.forward) * length * 0.35f;
                float radius = Mathf.Lerp(r0, r0 * 0.15f, at) * 0.55f;
                branches.Add(new Branch(start, mid, end, twig, radius));
            }

            var b0 = new MeshBuilder();
            for (int i = 0; i < 3; i++)
                b0.Frustum(trunk[i], trunk[i + 1], radii[i], radii[i + 1], 6, i * 20f);
            foreach (Branch br in branches)
            {
                b0.Frustum(br.Start, br.Mid, br.Radius, br.Radius * 0.55f, 4);
                b0.Frustum(br.Mid, br.End, br.Radius * 0.55f, 0.015f, 4);
                b0.Frustum(br.Mid, br.TwigEnd, br.Radius * 0.35f, 0.01f, 3);
            }
            lod0 = b0.ToMesh($"SC_DeadTree_{spec.Seed}_LOD0");

            var b1 = new MeshBuilder();
            b1.Frustum(trunk[0], trunk[2], radii[0], radii[2], 4);
            b1.Frustum(trunk[2], trunk[3], radii[2], radii[3], 4);
            for (int i = 0; i < Mathf.Min(3, branches.Count); i++)
                b1.Frustum(branches[i].Start, branches[i].End, branches[i].Radius, 0.02f, 3);
            lod1 = b1.ToMesh($"SC_DeadTree_{spec.Seed}_LOD1");
        }

        private static Vector3 TrunkPoint(Vector3[] trunk, float t)
        {
            float h = trunk[trunk.Length - 1].y;
            float y = Mathf.Clamp01(t) * h;
            for (int i = 0; i < trunk.Length - 1; i++)
                if (y <= trunk[i + 1].y)
                    return Vector3.Lerp(trunk[i], trunk[i + 1], Mathf.InverseLerp(trunk[i].y, trunk[i + 1].y, y));
            return trunk[trunk.Length - 1];
        }

        // 마른 덤불: 바닥에서 위로 퍼지는 가시 n개(삼각뿔, 옆면 3)
        public static Mesh BuildBushMesh(int seed, int spikes)
        {
            var random = new System.Random(seed);
            var b = new MeshBuilder();
            for (int i = 0; i < spikes; i++)
            {
                float a = (float)(random.NextDouble() * 360.0);
                Vector3 basePoint = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (float)(random.NextDouble() * 0.3);
                float tilt = 15f + (float)random.NextDouble() * 55f;
                Vector3 dir = Quaternion.Euler(-(90f - tilt), a + (float)(random.NextDouble() * 40.0 - 20.0), 0f) * Vector3.forward;
                float length = 0.45f + (float)random.NextDouble() * 0.55f;
                Vector3 tip = basePoint + dir * length;
                if (tip.y > BushMeshHeightM)
                    tip = basePoint + dir * (BushMeshHeightM / Mathf.Max(0.1f, dir.y));
                float w = 0.04f + (float)random.NextDouble() * 0.03f;
                Vector3 p0 = basePoint + new Vector3(w, -0.05f, 0f), p1 = basePoint + new Vector3(-w * 0.5f, -0.05f, w * 0.87f),
                    p2 = basePoint + new Vector3(-w * 0.5f, -0.05f, -w * 0.87f);
                b.Tri(p0, tip, p1);
                b.Tri(p1, tip, p2);
                b.Tri(p2, tip, p0);
            }
            return b.ToMesh($"SC_Bush_{seed}");
        }

        // 쓰러진 통나무: x축 8각 원통 + 양 끝 마개. 바닥이 지면에 LogSinkM 묻힌다.
        public static Mesh BuildLogMesh()
        {
            var b = new MeshBuilder();
            var a = new Vector3(-LogLengthM * 0.5f, LogRadiusM - LogSinkM, 0f);
            var c = new Vector3(LogLengthM * 0.5f, LogRadiusM - LogSinkM, 0f);
            b.Frustum(a, c, LogRadiusM, LogRadiusM * 0.85f, 8);
            for (int i = 0; i < 8; i++)
            {
                float t0 = i / 8f * Mathf.PI * 2f, t1 = (i + 1) / 8f * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(0f, Mathf.Cos(t0), Mathf.Sin(t0)), d1 = new Vector3(0f, Mathf.Cos(t1), Mathf.Sin(t1));
                b.Tri(a, a + d1 * LogRadiusM, a + d0 * LogRadiusM);
                b.Tri(c, c + d0 * LogRadiusM * 0.85f, c + d1 * LogRadiusM * 0.85f);
            }
            return b.ToMesh("SC_Log_01");
        }

        // 풀포기: 가는 잎 3장(2마디, 잎마다 삼각형 4) — 높이 1·폭 약 0.5(Terrain Detail이 min/max 높이·폭으로 늘린다), 법선 위(지면처럼 밝게)
        public static Mesh BuildGrassMesh(int seed, bool tall)
        {
            var random = new System.Random(seed);
            var b = new MeshBuilder();
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f + (float)(random.NextDouble() * 40.0 - 20.0);
                Quaternion rot = Quaternion.Euler(0f, a, 0f);
                float lean = (tall ? 0.15f : 0.3f) + (float)random.NextDouble() * 0.15f;
                float w = tall ? 0.06f : 0.08f;
                Vector3 baseCenter = rot * new Vector3(0f, 0f, 0.05f);
                Vector3 side = rot * Vector3.right * w;
                Vector3 midCenter = baseCenter + rot * new Vector3(0f, 0.55f, lean * 0.4f);
                Vector3 tip = baseCenter + rot * new Vector3(0f, 1f, lean);
                b.Quad(baseCenter - side, midCenter - side * 0.6f, midCenter + side * 0.6f, baseCenter + side);
                b.Tri(midCenter - side * 0.6f, tip, midCenter + side * 0.6f);
                b.Tri(midCenter + side * 0.6f, tip, midCenter - side * 0.6f);   // 뒷면 (양면 머티리얼이지만 삼각형 수 상한 안)
            }
            return b.ToMesh(tall ? "SC_Grass_Tall" : "SC_Grass_Short", upNormals: true);
        }
    }
}
