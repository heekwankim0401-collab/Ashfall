using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Raid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Layout = Ashfall.EditorTools.M7HazardLayout;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // 정유소 방사능 구역을 레이드 씬에 적용 (M7 설계 §1.5·§1.7·§2.2, Q39~Q41, STAT-RAD). 7C-2c.
    // ApplyRaid(열린 레이드 씬):
    //   ① 검사 먼저(씬을 고치지 않는다): RaidConfig(M7Names.RaidConfigPath)에 RAD-REF 구역, 씬 RaidController가 같은 설정, Terrain,
    //      M7HazardLayout.Check(감쇠 띠·최대 률 영역·맵·스폰), 표시물 ↔ 씬 장애물 콜라이더 겹침 없음. 실패하면 예외.
    //   ② 머티리얼 M_Toxic(PaletteMaterialMap Toxic 키 — M6ArtSetup과 같은 생성·색 기록, GUID 유지).
    //   ③ 최상위 "RadiationField"(RadiationField 컴포넌트: config = RaidConfig, raid = 씬 RaidController) — IRadiationField 등록은 런타임 OnEnable.
    //   ④ 최상위 "M7_ToxicMarkers" 아래 M7_Toxic_* 프리미티브(콜라이더 없음 → NavMesh(PhysicsColliders 굽기)·전투·발소리 판정 무관), M_Toxic.
    // 멱등: 이름으로 찾아 위치·회전·크기·머티리얼만 값이 다를 때 다시 맞추고, 없으면 만들고, 표에 없는 자식은 지운다. 바뀐 것이 있을 때만 씬을 dirty로.
    // 씬 저장은 호출자(M7SceneSetup — 7C-3 통합) 몫, 단독 메뉴는 직접 저장.
    // 배치모드(단독 확인): -executeMethod Ashfall.EditorTools.M7HazardSceneApply.ApplyRaidSceneMenu
    public static class M7HazardSceneApply
    {
        private const string Tag = "[M7HazardSceneApply]";

        // 씬 구성 값 (에디터 배치 기하 — 게임 수치 아님, TODO(GDD-?) Q40)
        private const float ObstacleMarginM = 0.1f;     // 표시물 ↔ 장애물 상자 사이 최소 틈
        private const float GroundSkinM = 0.15f;        // 표시물 바닥 이 높이까지는 지면·바닥판과 닿아도 된다
        private const float PositionEps = 1e-4f;
        private const float RotationEpsDeg = 0.01f;
        private const float ScaleEps = 1e-5f;

        // ---------- 메뉴 (단독 확인용) ----------

        [MenuItem("Ashfall/Setup/M7/Apply Radiation Zone (Raid Scene)")]
        public static void ApplyRaidSceneMenu()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            string path = M4SceneSetup.RaidScenePath;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                throw new InvalidOperationException($"{Tag} 레이드 씬 없음: {path} — 먼저 Ashfall/Setup/Create M4 Raid Scenes");
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            ApplyRaid(scene);
            AssetDatabase.SaveAssets();
            if (!scene.isDirty)
            {
                Debug.Log($"{Tag} 변경 없음: {path}");
                return;
            }
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException($"{Tag} 씬 저장 실패: {path}");
            Debug.Log($"{Tag} 적용·저장: {path}");
        }

        // ---------- 머티리얼 ----------

        // M_Toxic: 없으면 만들고, 있으면 색·속성만 다시 기록(GUID 유지). M6ArtSetup의 팔레트 머티리얼과 같은 생성이라 M6 아트 메뉴를 나중에 돌려도 바이트가 같다.
        public static Material EnsureToxicMaterial()
        {
            PaletteSO palette = AssetDatabase.LoadAssetAtPath<PaletteSO>(M6Names.PalettePath);
            if (palette == null)
                throw new InvalidOperationException($"{Tag} 팔레트 없음: {M6Names.PalettePath} — 먼저 Ashfall/Setup/Apply M6 Art & Audio");
            PaletteMaterialMap.Spec spec = PaletteMaterialMap.Materials[PaletteMaterialMap.Key.Toxic];
            var m = AssetDatabase.LoadAssetAtPath<Material>(spec.Path);
            if (m == null)
            {
                string shaderName = spec.Unlit ? PaletteMaterialMap.UnlitShader : PaletteMaterialMap.LitShader;
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                    throw new InvalidOperationException($"{Tag} 셰이더를 찾지 못했다: {shaderName} ({spec.Path})");
                SetupUtil.EnsureFolder(Path.GetDirectoryName(spec.Path).Replace('\\', '/'));
                m = new Material(shader) { name = spec.Name };
                AssetDatabase.CreateAsset(m, spec.Path);
                Debug.Log($"{Tag} 머티리얼 생성: {spec.Path} — TODO(GDD-?) 자리표시");
            }
            M6ArtSetup.ApplySpec(m, spec, palette);
            return m;
        }

        // ---------- 레이드 씬 ----------

        // 성공하면 true. 데이터·씬 구성이 모자라거나 검사가 실패하면 예외(반쯤 적용된 씬을 저장하지 않게 — M6 리뷰 6번).
        public static bool ApplyRaid(Scene scene)
        {
            if (!scene.IsValid())
                throw new InvalidOperationException($"{Tag} 씬이 유효하지 않다");

            // ① 검사 (씬 변경 없음)
            var config = AssetDatabase.LoadAssetAtPath<RaidConfigSO>(M7Names.RaidConfigPath);
            if (config == null)
                throw new InvalidOperationException($"{Tag} 레이드 설정 없음: {M7Names.RaidConfigPath} — 먼저 Ashfall/Setup/Create M4 Raid Scenes");
            if (!TryFindZone(config, M7Names.RefineryZoneId, out RadiationZoneConfig zone))
                throw new InvalidOperationException($"{Tag} {M7Names.RaidConfigPath}에 구역 {M7Names.RefineryZoneId} 없음 — 먼저 Ashfall/Setup/Create M7 Survival Data");

            RaidController raid = FindInScene<RaidController>(scene);
            if (raid == null)
                throw new InvalidOperationException($"{Tag} {scene.name}: RaidController 없음 (레이드 씬이 아니다)");
            if (raid.Config != config)
                throw new InvalidOperationException($"{Tag} {scene.name}: RaidController 설정이 {M7Names.RaidConfigPath}가 아니다");
            Terrain terrain = FindInScene<Terrain>(scene);
            if (terrain == null)
                throw new InvalidOperationException($"{Tag} {scene.name}: Terrain 없음 (레이드 씬이 아니다)");

            List<Layout.MarkerPlan> plans = Layout.Build(zone);
            if (!Layout.Check(zone, plans, out string report))
                throw new InvalidOperationException($"{Tag} Toxic 표시 배치 검사 실패\n{report}");

            var poses = new Vector3[plans.Count];
            for (int i = 0; i < plans.Count; i++)
                poses[i] = new Vector3(plans[i].XZ.x, M4MapSetup.GroundY(terrain, plans[i].XZ.x, plans[i].XZ.y) + plans[i].CenterHeightM, plans[i].XZ.y);

            GameObject existingMarkers = SetupUtil.FindRoot(scene, M7Names.ToxicMarkersObject);
            if (!CheckObstacles(scene, existingMarkers, plans, poses, out string obstacleReport))
                throw new InvalidOperationException($"{Tag} Toxic 표시가 씬 장애물과 겹친다 (M7HazardLayout 각도·무더기 위치 확인)\n{obstacleReport}");

            // ② 머티리얼
            Material toxic = EnsureToxicMaterial();

            if (SceneManager.GetActiveScene() != scene)
                SceneManager.SetActiveScene(scene);   // 새 오브젝트는 활성 씬에 생긴다

            bool changed = false;

            // ③ RadiationField
            changed |= EnsureField(scene, config, raid, terrain, zone);

            // ④ 표시물
            int created = 0, updated = 0, removed = 0;
            GameObject markersRoot = existingMarkers;
            if (markersRoot == null)
            {
                markersRoot = SetupUtil.FindOrCreateRoot(scene, M7Names.ToxicMarkersObject);
                changed = true;
            }
            changed |= SetTransform(markersRoot.transform, Vector3.zero, Quaternion.identity, Vector3.one);
            changed |= RemoveColliders(markersRoot);

            var wanted = new HashSet<string>();
            foreach (Layout.MarkerPlan plan in plans)
                wanted.Add(plan.Name);
            for (int i = markersRoot.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = markersRoot.transform.GetChild(i);
                if (!wanted.Contains(child.name) || markersRoot.transform.Find(child.name) != child)
                {
                    Object.DestroyImmediate(child.gameObject);   // 표에 없거나 같은 이름의 두 번째
                    removed++;
                    changed = true;
                }
            }

            for (int i = 0; i < plans.Count; i++)
            {
                Layout.MarkerPlan plan = plans[i];
                Transform t = markersRoot.transform.Find(plan.Name);
                if (t != null && !IsIntact(t.gameObject, plan.Primitive))
                {
                    Object.DestroyImmediate(t.gameObject);
                    t = null;
                    removed++;
                }
                if (t == null)
                {
                    t = CreateMarker(plan, markersRoot.transform);
                    created++;
                    changed = true;
                }
                bool c = SetTransform(t, poses[i], plan.Rotation, plan.Scale);
                c |= RemoveColliders(t.gameObject);
                c |= SetRenderer(t.GetComponent<MeshRenderer>(), toxic);
                if (t.GetSiblingIndex() != i)
                {
                    t.SetSiblingIndex(i);
                    c = true;
                }
                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                    c = true;
                }
                if (c)
                {
                    updated++;
                    EditorUtility.SetDirty(t.gameObject);
                }
                changed |= c;
            }

            if (!Validate(scene, plans.Count, out string validation))
                throw new InvalidOperationException($"{Tag} 적용 결과 확인 실패\n{validation}");

            if (changed)
                EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{Tag} {scene.name}: 방사능 구역 {zone.id} + Toxic 표시 {plans.Count}개 (새로 {created}, 다시 맞춤 {updated}, 지움 {removed})" +
                      $"{(changed ? "" : " — 변경 없음")}\n{report}{obstacleReport}{validation}");
            return true;
        }

        public static bool TryFindZone(RaidConfigSO config, string id, out RadiationZoneConfig zone)
        {
            if (config != null && config.radiationZones != null)
                foreach (RadiationZoneConfig z in config.radiationZones)
                    if (z.id == id)
                    {
                        zone = z;
                        return true;
                    }
            zone = default;
            return false;
        }

        // ---------- RadiationField ----------

        private static bool EnsureField(Scene scene, RaidConfigSO config, RaidController raid, Terrain terrain, in RadiationZoneConfig zone)
        {
            bool changed = false;
            GameObject go = SetupUtil.FindRoot(scene, M7Names.RadiationFieldObject);
            if (go == null)
            {
                go = SetupUtil.FindOrCreateRoot(scene, M7Names.RadiationFieldObject);
                changed = true;
            }
            // 위치는 의미 없다(구역은 설정에서) — 에디터에서 찾기 쉽게 구역 중심 지면에 둔다
            Vector3 center = new Vector3(zone.centerXZ.x, M4MapSetup.GroundY(terrain, zone.centerXZ.x, zone.centerXZ.y), zone.centerXZ.y);
            changed |= SetTransform(go.transform, center, Quaternion.identity, Vector3.one);
            var fields = go.GetComponents<RadiationField>();
            for (int i = 1; i < fields.Length; i++)
            {
                Object.DestroyImmediate(fields[i]);
                changed = true;
            }
            RadiationField field = fields.Length > 0 ? fields[0] : null;
            if (field == null)
            {
                field = go.AddComponent<RadiationField>();
                changed = true;
            }
            if (!field.enabled)
            {
                field.enabled = true;
                changed = true;
            }
            if (!go.activeSelf)
            {
                go.SetActive(true);
                changed = true;
            }
            // 씬 전체에 RadiationField는 하나 (IRadiationField 중복 등록 방지)
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (RadiationField other in root.GetComponentsInChildren<RadiationField>(true))
                    if (other != field)
                    {
                        Object.DestroyImmediate(other);
                        changed = true;
                    }

            var so = new SerializedObject(field);
            SetRef(so, "config", config);
            SetRef(so, "raid", raid);
            changed |= so.ApplyModifiedPropertiesWithoutUndo();
            if (changed)
                EditorUtility.SetDirty(go);
            return changed;
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
                throw new InvalidOperationException($"{Tag} {so.targetObject.GetType().Name}에 직렬화 필드 '{field}'가 없다");
            if (p.objectReferenceValue != value)
                p.objectReferenceValue = value;
        }

        // ---------- 표시물 ----------

        private static Transform CreateMarker(Layout.MarkerPlan plan, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(plan.Primitive);
            go.name = plan.Name;
            foreach (Collider c in go.GetComponents<Collider>())
                Object.DestroyImmediate(c);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // 프리미티브 메시·렌더러가 그대로인가 (사용자가 바꿨거나 깨졌으면 다시 만든다)
        private static bool IsIntact(GameObject go, PrimitiveType primitive)
        {
            var filter = go.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null && filter.sharedMesh == PrimitiveMesh(primitive)
                   && go.GetComponent<MeshRenderer>() != null && NonSkinChildCount(go.transform) == 0;
        }

        // 에셋 교체 스킨 자식(AssetSwap 설계 §3)은 표시물의 일부가 아니다
        private static int NonSkinChildCount(Transform t)
        {
            int n = 0;
            foreach (Transform child in t)
                if (!AssetSwapApply.IsSkinObject(child.gameObject))
                    n++;
            return n;
        }

        private static readonly Dictionary<PrimitiveType, Mesh> PrimitiveMeshes = new Dictionary<PrimitiveType, Mesh>();

        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            if (PrimitiveMeshes.TryGetValue(type, out Mesh mesh) && mesh != null)
                return mesh;
            GameObject temp = GameObject.CreatePrimitive(type);
            mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            PrimitiveMeshes[type] = mesh;
            return mesh;
        }

        private static bool SetTransform(Transform t, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            bool changed = false;
            if (Vector3.Distance(t.position, position) > PositionEps || Quaternion.Angle(t.rotation, rotation) > RotationEpsDeg)
            {
                t.SetPositionAndRotation(position, rotation);
                changed = true;
            }
            if ((t.localScale - scale).sqrMagnitude > ScaleEps * ScaleEps)
            {
                t.localScale = scale;
                changed = true;
            }
            return changed;
        }

        private static bool RemoveColliders(GameObject go)
        {
            bool changed = false;
            foreach (Collider c in go.GetComponents<Collider>())
            {
                Object.DestroyImmediate(c);
                changed = true;
            }
            return changed;
        }

        private static bool SetRenderer(MeshRenderer renderer, Material material)
        {
            if (renderer == null)
                return false;
            bool changed = false;
            if (renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial != material)
            {
                renderer.sharedMaterials = new[] { material };
                changed = true;
            }
            if (!renderer.enabled)
            {
                renderer.enabled = true;
                changed = true;
            }
            return changed;
        }

        // ---------- 검사 ----------

        // 표시물(대략 상자) ↔ 씬의 고체 콜라이더(트리거·지형·적·플레이어·기존 표시물 제외) AABB가 겹치지 않는가.
        // 표시물 바닥 GroundSkinM은 빼고 본다(지면 위 얇은 바닥판에 서 있는 것은 괜찮다).
        private static bool CheckObstacles(Scene scene, GameObject existingMarkers, IReadOnlyList<Layout.MarkerPlan> plans, Vector3[] poses, out string report)
        {
            var obstacles = new List<(string name, Bounds bounds)>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == existingMarkers || root.name == M7Names.RadiationFieldObject)
                    continue;
                foreach (Collider c in root.GetComponentsInChildren<Collider>(false))
                {
                    if (!c.enabled || c.isTrigger || c is TerrainCollider || c is CharacterController)
                        continue;
                    if (c.GetComponentInParent<EnemyAgent>() != null)
                        continue;
                    Bounds b = c.bounds;
                    if (b.size == Vector3.zero)
                    {
                        var r = c.GetComponent<Renderer>();
                        if (r == null)
                            continue;
                        b = r.bounds;
                    }
                    obstacles.Add((c.name, b));
                }
            }

            var sb = new StringBuilder();
            bool ok = true;
            float closest = float.MaxValue;
            string closestPair = "";
            for (int i = 0; i < plans.Count; i++)
            {
                Vector3 half = plans[i].HalfExtents;
                Vector3 min = poses[i] - half;
                Vector3 max = poses[i] + half;
                min.y += GroundSkinM;
                var box = new Bounds();
                box.SetMinMax(min, max);
                foreach (var (name, bounds) in obstacles)
                {
                    float gap = Gap(box, bounds);
                    if (gap < closest)
                    {
                        closest = gap;
                        closestPair = $"{plans[i].Name} ↔ {name}";
                    }
                    if (gap < ObstacleMarginM)
                    {
                        sb.AppendLine($"  {plans[i].Name} {poses[i]}: {name} 상자와 틈 {gap:0.00}m < {ObstacleMarginM}m");
                        ok = false;
                    }
                }
            }
            sb.AppendLine($"  장애물 {obstacles.Count}개와 겹침 검사 — 가장 가까운 틈 {closest:0.00}m ({closestPair})");
            report = sb.ToString();
            return ok;
        }

        // 두 AABB 사이 틈 (겹치면 음수 — 가장 얕은 축의 침투 깊이)
        private static float Gap(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x);
            float dy = Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y);
            float dz = Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z);
            if (dx > 0f || dy > 0f || dz > 0f)
            {
                float px = Mathf.Max(0f, dx), py = Mathf.Max(0f, dy), pz = Mathf.Max(0f, dz);
                return Mathf.Sqrt(px * px + py * py + pz * pz);
            }
            return Mathf.Max(dx, Mathf.Max(dy, dz));
        }

        // 적용 결과: RadiationField 하나(최상위·config 연결), 표시물 수·최상위 묶음·콜라이더 없음·M_Toxic.
        public static bool Validate(Scene scene, int expectedMarkers, out string report)
        {
            var sb = new StringBuilder();
            bool ok = true;
            int fieldCount = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                fieldCount += root.GetComponentsInChildren<RadiationField>(true).Length;
            GameObject fieldGo = SetupUtil.FindRoot(scene, M7Names.RadiationFieldObject);
            RadiationField field = fieldGo != null ? fieldGo.GetComponent<RadiationField>() : null;
            if (fieldCount != 1 || field == null || field.Config == null)
            {
                sb.AppendLine($"  RadiationField {fieldCount}개 (최상위 \"{M7Names.RadiationFieldObject}\"에 설정 연결된 것 하나여야 한다)");
                ok = false;
            }

            GameObject markers = SetupUtil.FindRoot(scene, M7Names.ToxicMarkersObject);
            var toxic = AssetDatabase.LoadAssetAtPath<Material>(M7Names.ToxicMaterialPath);
            if (markers == null)
            {
                sb.AppendLine($"  \"{M7Names.ToxicMarkersObject}\" 없음");
                ok = false;
            }
            else
            {
                if (markers.transform.childCount != expectedMarkers)
                {
                    sb.AppendLine($"  표시 {markers.transform.childCount}/{expectedMarkers}");
                    ok = false;
                }
                if (markers.GetComponentsInChildren<Collider>(true).Length > 0)
                {
                    sb.AppendLine("  표시에 콜라이더가 있다");
                    ok = false;
                }
                foreach (MeshRenderer r in markers.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (AssetSwapApply.UnderSkin(r.transform) || AssetSwapApply.IsSkinObject(r.gameObject))
                        continue;   // 에셋 교체 스킨(깨진 것 포함)은 표시물이 아니다
                    if (r.sharedMaterial != toxic || !r.name.StartsWith(M7Names.ToxicMarkerPrefix, StringComparison.Ordinal))
                    {
                        sb.AppendLine($"  {r.name}: 머티리얼·이름이 M_Toxic·{M7Names.ToxicMarkerPrefix}가 아니다");
                        ok = false;
                    }
                }
            }
            sb.AppendLine($"  RadiationField {fieldCount}, Toxic 표시 {(markers != null ? markers.transform.childCount : 0)}개");
            report = sb.ToString();
            return ok;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
