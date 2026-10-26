using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
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
    // 맵 개선을 레이드 씬에 적용 (docs/design/MapPolish_design.md §5·§6). 저장은 호출자(MapPolishSetup·M4SceneSetup) 몫.
    // Environment 아래 세 그룹을 **모두 없을 때만** 만든다(하나라도 빠졌으면 셋 다 지우고 다시):
    //   MapPolish_Landmarks: 래퍼 LM_{종류}_{nn} → 프리팹 인스턴스 "Model" + 엄폐 지점 + (있으면) 점광원 "Light_{종류}"
    //   MapPolish_Structures: M4 블록아웃 생성기 도형 "{Shape}_{nn}" + 엄폐 지점 (M6 표로 칠함)
    //   MapPolish_Lights: 래퍼 LP_{종류}_{nn} → 프리팹 인스턴스 "Model" + 점광원
    // 그룹 루트마다 NavMeshModifier(Not Walkable) — 구조물 윗면에 닿을 수 없는 NavMesh 섬을 만들지 않는다(리뷰 M1).
    // 만들었으면 겹침·회피 확인 → NavMesh 다시 굽기(같은 에셋, GUID 유지) → 경로·엄폐·적·순찰·상자·도달성 확인.
    // 실패 = 예외: 만든 그룹을 지우고 NavMesh 에셋을 굽기 전 바이트로 되돌린 뒤 던진다(리뷰 H1·H2 — 반쯤 적용된 상태가 남지 않게).
    // 그룹이 이미 있으면 구조물 자리가 NavMesh에 구멍으로 반영됐는지만 보고(아니면 예외 — Rebuild 메뉴), 배치·NavMesh는 그대로.
    // 점광원 값(색·세기·범위·그림자 없음)은 매 실행 AtmosphereConfigSO에서 다시 맞춘다 — 값이 같으면 바꾸지 않는다(멱등).
    public static class MapPolishSceneApply
    {
        private const string Tag = "[MapPolishSceneApply]";

        private const float CoverOnMeshM = 0.5f;          // 엄폐 지점 ↔ NavMesh 허용 거리
        private const float AgentOnMeshM = 2f;            // 적 시작·순찰 지점·상자 ↔ NavMesh
        private const float CarvedProbeM = 0.3f;          // 구조물 중심 지면에서 이 안에 NavMesh가 있으면 "구멍 없음"(옛 NavMesh)
        private const float CoverProbeRadiusM = 0.3f;     // 새 엄폐 지점·peek 자리가 다른 콜라이더 안인지 (캡슐)
        private const int GroundSamples = 5;              // 랜드마크 발자국 지면 샘플 (한 변)
        private const int OverlapBufferSize = 256;
        private const string NotWalkableArea = "Not Walkable";
        private const float NavBlockMinSizeM = 1f;        // 세 변이 모두 이 이상인 콜라이더만 내부 막기 볼륨
        private const float NavBlockShrink = 0.98f;       // 볼륨을 콜라이더보다 살짝 작게 — 바깥 지면을 건드리지 않게

        // M4 메뉴용: 맵 개선 에셋(프리팹·발광 머티리얼)이 있으면 적용, 없으면 조용히 건너뛴다(M4~M7만 쓰는 상황 호환).
        public static void ApplyRaidIfAssetsExist(Scene scene)
        {
            if (!AssetsExist())
                return;
            if (!ApplyRaid(scene))
                throw new InvalidOperationException($"{Tag} 맵 개선 적용 실패: {scene.name} — 씬을 저장하지 않는다(위 오류 로그 확인)");
        }

        public static bool AssetsExist()
        {
            foreach (MapPolishPrefabs.Spec spec in MapPolishPrefabs.AllSpecs())
                if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.Path) == null)
                    return false;
            return AssetDatabase.LoadAssetAtPath<AtmosphereConfigSO>(M6Names.AtmosphereConfigPath) != null;
        }

        // Rebuild 메뉴: 세 그룹을 지운다(다음 ApplyRaid가 다시 만들고 다시 굽는다).
        public static void RemoveGroups(Scene scene)
        {
            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            if (environment == null)
                return;
            foreach (string name in new[] { MapPolishLayout.LandmarkGroup, MapPolishLayout.StructureGroup, MapPolishLayout.LightGroup })
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
            var cfg = AssetDatabase.LoadAssetAtPath<AtmosphereConfigSO>(M6Names.AtmosphereConfigPath);
            PaletteSO palette = cfg != null && cfg.palette != null ? cfg.palette : AssetDatabase.LoadAssetAtPath<PaletteSO>(M6Names.PalettePath);
            if (cfg == null || palette == null)
                throw new InvalidOperationException($"{Tag} AtmosphereConfig·Palette 없음 — 먼저 Apply M6 Art & Audio");
            if (!MapPolishLayout.CheckLayout(cfg.maxMapLights, out string layoutReport))
                throw new InvalidOperationException($"{Tag} 배치 데이터 검사 실패\n{layoutReport}");

            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            if (environment == null)
                throw new InvalidOperationException($"{Tag} {scene.name}: {M4MapSetup.EnvironmentName} 루트 없음 — 먼저 Create M4 Raid Scenes");
            Terrain terrain = environment.GetComponentInChildren<Terrain>();
            var surface = environment.GetComponent<NavMeshSurface>();
            if (terrain == null || surface == null)
                throw new InvalidOperationException($"{Tag} {scene.name}: Terrain 또는 NavMeshSurface 없음");

            Transform landmarks = environment.transform.Find(MapPolishLayout.LandmarkGroup);
            Transform structures = environment.transform.Find(MapPolishLayout.StructureGroup);
            Transform lights = environment.transform.Find(MapPolishLayout.LightGroup);
            if (landmarks == null || structures == null || lights == null)
            {
                foreach (Transform partial in new[] { landmarks, structures, lights })
                    if (partial != null)
                    {
                        Debug.LogWarning($"{Tag} 일부 그룹만 있다 — 지우고 다시 만든다: {partial.name}");
                        Object.DestroyImmediate(partial.gameObject);
                    }
                BuildOrRollBack(scene, environment, terrain, surface);
            }
            else
            {
                // 그룹은 있는데 NavMesh가 옛것(구조물 자리에 구멍 없음)이면 확인 없이 굳은 상태 — 다시 만들기를 안내한다(리뷰 H2).
                if (!CheckCarved(structures, terrain, out string carvedReport))
                    throw new InvalidOperationException($"{Tag} {scene.name}: 맵 개선 그룹은 있는데 NavMesh에 구조물이 반영되지 않았다 — " +
                                                        $"Ashfall/Setup/Map Polish/Rebuild Map Polish (Raid Scene)\n{carvedReport}");
                Debug.Log($"{Tag} {scene.name}: 맵 개선 그룹 있음 — 배치·NavMesh 그대로, 광원 값만 확인 ({carvedReport.Trim()})");
            }

            int changed = RefreshLights(environment, cfg, palette);
            if (changed > 0)
                EditorSceneManager.MarkSceneDirty(scene);
            if (!CheckLights(scene, cfg, out string lightReport))
                throw new InvalidOperationException($"{Tag} 광원 확인 실패\n{lightReport}");
            Debug.Log($"{Tag} {scene.name}: 광원 {lightReport.Trim()} (값 갱신 {changed})");
            return true;
        }

        // ---------- 생성 ----------

        // 실패하면 만든 그룹을 지우고 NavMesh 에셋을 굽기 전 바이트로 되돌린 뒤 다시 던진다 (리뷰 H1·H2).
        private static void BuildOrRollBack(Scene scene, GameObject environment, Terrain terrain, NavMeshSurface surface)
        {
            string navPath = M4MapSetup.NavMeshPath;
            string navFile = Path.GetFullPath(navPath);
            byte[] navBackup = File.Exists(navFile) ? File.ReadAllBytes(navFile) : null;
            var created = new List<GameObject>();
            bool rebaked = false;
            try
            {
                Build(scene, environment, terrain, surface, created, ref rebaked);
            }
            catch
            {
                foreach (GameObject go in created)
                    if (go != null)
                        Object.DestroyImmediate(go);
                if (rebaked && navBackup != null)
                    RestoreNavMesh(surface, navPath, navFile, navBackup);
                Debug.LogError($"{Tag} 실패 — 만든 그룹 {created.Count}개를 지우고{(rebaked ? " NavMesh를 되돌렸다" : "")}");
                throw;
            }
        }

        private static void RestoreNavMesh(NavMeshSurface surface, string navPath, string navFile, byte[] backup)
        {
            surface.RemoveData();
            File.WriteAllBytes(navFile, backup);
            AssetDatabase.ImportAsset(navPath, ImportAssetOptions.ForceUpdate);
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
            surface.AddData();
            EditorUtility.SetDirty(surface);
        }

        private static void Build(Scene scene, GameObject environment, Terrain terrain, NavMeshSurface surface,
            List<GameObject> created, ref bool rebaked)
        {
            // 굽기 전 상태 (회귀만 실패로 본다 — 원래부터 NavMesh 밖이던 것은 정보로만)
            NavState before = NavState.Capture(scene, null);

            var builder = new M4MapSetup.BlockoutBuilder(terrain, environment.transform);
            var problems = new StringBuilder();
            int coverAdjusted = 0;

            Transform landmarkGroup = builder.NewGroup(MapPolishLayout.LandmarkGroup);
            created.Add(landmarkGroup.gameObject);
            for (int i = 0; i < MapPolishLayout.Landmarks.Length; i++)
            {
                MapPolishLayout.LandmarkPlan plan = MapPolishLayout.Landmarks[i];
                MapPolishPrefabs.Spec spec = MapPolishPrefabs.Landmarks[plan.Kind];
                Transform wrapper = PlaceWrapper(landmarkGroup, $"LM_{plan.Kind}_{i + 1:00}", terrain, plan.XZ, plan.YawDeg, spec, problems);
                InstantiateModel(wrapper, spec);
                for (int c = 0; c < spec.Covers.Length; c++)
                    coverAdjusted += AddCovers(builder, wrapper, spec.Covers[c], c);
                AddLights(wrapper, spec, terrain);
            }

            int coversBefore = builder.CoverCount;
            builder.Group(MapPolishLayout.StructureGroup, MapPolishLayout.Structures);
            int structureCovers = builder.CoverCount - coversBefore;
            Transform structureGroup = environment.transform.Find(MapPolishLayout.StructureGroup);
            created.Add(structureGroup.gameObject);

            Transform lightGroup = builder.NewGroup(MapPolishLayout.LightGroup);
            created.Add(lightGroup.gameObject);
            for (int i = 0; i < MapPolishLayout.Props.Length; i++)
            {
                MapPolishLayout.PropPlan plan = MapPolishLayout.Props[i];
                MapPolishPrefabs.Spec spec = MapPolishPrefabs.Props[plan.Kind];
                Transform wrapper = PlaceWrapper(lightGroup, $"LP_{plan.Kind}_{i + 1:00}", terrain, plan.XZ, plan.YawDeg, spec, problems);
                InstantiateModel(wrapper, spec);
                AddLights(wrapper, spec, terrain);
            }

            // 중간 구조물 칠하기 (M6 표 — M6 적용이 이미 지나간 M4 메뉴 경로에서도 칠해지게)
            var unknown = new List<string>();
            M6VisualSceneApply.PaintGroup(structureGroup, M6VisualSceneApply.LoadPaletteMaterials(), unknown);
            if (unknown.Count > 0)
                problems.AppendLine($"  칠하지 못한 구조물: {string.Join(", ", unknown)}");

            var newRoots = new[] { landmarkGroup, structureGroup, lightGroup };
            int notWalkable = NavMesh.GetAreaFromName(NotWalkableArea);
            foreach (Transform root in newRoots)
            {
                var modifier = root.gameObject.AddComponent<NavMeshModifier>();
                modifier.overrideArea = true;
                modifier.area = notWalkable;
            }
            int navBlocks = AddNavBlocks(newRoots, notWalkable);
            var newCovers = new List<CoverPoint>();
            foreach (Transform root in newRoots)
                newCovers.AddRange(root.GetComponentsInChildren<CoverPoint>(true));

            Physics.SyncTransforms();
            CheckPlacement(scene, newRoots, terrain, problems);
            int removedInside = RemoveCoversInsideColliders(newCovers);
            if (problems.Length > 0)
                throw new InvalidOperationException($"{Tag} 배치 확인 실패 (씬 저장 안 함)\n{problems}");

            rebaked = true;
            if (!M4MapSetup.RebakeNavMesh(surface, M4MapSetup.NavMeshPath, out double bakeSeconds))
                throw new InvalidOperationException($"{Tag} NavMesh 다시 굽기 실패");

            int removedOffMesh = RemoveCoversOffMesh(newCovers, before.Reference);
            NavState after = NavState.Capture(scene, before);
            var raidConfig = AssetDatabase.LoadAssetAtPath<RaidConfigSO>(M4MapSetup.DefaultRaidConfigPath);
            bool pathsOk = M4MapSetup.ValidateNavigation(raidConfig, out string navReport);
            string regressions = after.Regressions(before);
            bool carved = CheckCarved(structureGroup, terrain, out string carvedReport);
            if (!pathsOk || regressions.Length > 0 || !carved)
                throw new InvalidOperationException($"{Tag} 굽기 뒤 확인 실패 (씬 저장 안 함)\n{navReport}{regressions}{(carved ? "" : carvedReport)}");

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{Tag} {scene.name}: 랜드마크 {MapPolishLayout.Landmarks.Length}, 구조물 {MapPolishLayout.StructureCount}" +
                      $"(엄폐 {structureCovers}), 광원 소품 {MapPolishLayout.Props.Length}, 새 엄폐 지점 {newCovers.Count}" +
                      $"(콜라이더 안 {removedInside}·NavMesh 밖/도달 불가 {removedOffMesh} 뺌, 드러난 높이로 조정 {coverAdjusted}), " +
                      $"내부 막기 볼륨 {navBlocks}, NavMesh 굽기 {bakeSeconds:0.0}s\n" +
                      $"  굽기 전: {before.Summary}\n  굽기 뒤: {after.Summary}\n  {carvedReport.Trim()}\n{navReport}");
        }

        // Recast는 닫힌 메시 속을 채우지 않아, 사람 키보다 높은 속 찬 상자(컨테이너·창고·버스·교각·동체) 안쪽 지면에 닿을 수 없는 NavMesh 섬이 생긴다.
        // 세 변이 모두 NavBlockMinSizeM 이상인 콜라이더마다 같은 자리·회전의 Not Walkable 볼륨을 둔다(프리팹 밖 형제 오브젝트 — 프리팹을 바꿔도 남지 않게
        // Rebuild가 다시 만든다). 얇은 벽·막대는 가장자리 침식으로 이미 구멍이 난다. 만든 수.
        private static int AddNavBlocks(Transform[] newRoots, int notWalkable)
        {
            int count = 0;
            var colliders = new List<Collider>();
            foreach (Transform root in newRoots)
                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                    if (!c.isTrigger)
                        colliders.Add(c);
            foreach (Collider c in colliders)
            {
                Obb obb = Obb.Of(c);
                Vector3 size = obb.HalfExtents * 2f;
                if (Mathf.Min(size.x, Mathf.Min(size.y, size.z)) < NavBlockMinSizeM)
                    continue;
                Transform owner = c.transform.parent;
                while (owner != null && PrefabUtility.IsPartOfPrefabInstance(owner.gameObject))
                    owner = owner.parent;   // 프리팹 인스턴스 밖(래퍼)에 둔다 — 인스턴스에 덧붙임(override)을 만들지 않게
                var go = new GameObject($"NavBlock_{c.name}");
                go.transform.SetParent(owner, false);
                go.transform.SetPositionAndRotation(obb.Center, obb.Rotation);
                var volume = go.AddComponent<NavMeshModifierVolume>();
                volume.center = Vector3.zero;
                volume.size = size * NavBlockShrink;
                volume.area = notWalkable;
                count++;
            }
            return count;
        }

        // 래퍼를 발자국의 가장 낮은 지면에 놓는다(기초가 높은 쪽을 묻는다). 지면 높이 차가 크면 problems.
        private static Transform PlaceWrapper(Transform group, string name, Terrain terrain, Vector2 xz, float yawDeg,
            MapPolishPrefabs.Spec spec, StringBuilder problems)
        {
            Quaternion yaw = Quaternion.Euler(0f, yawDeg, 0f);
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int ix = 0; ix < GroundSamples; ix++)
            for (int iz = 0; iz < GroundSamples; iz++)
            {
                float u = ix / (GroundSamples - 1f) - 0.5f, v = iz / (GroundSamples - 1f) - 0.5f;
                Vector3 offset = yaw * new Vector3(u * spec.FootprintXZ.x, 0f, v * spec.FootprintXZ.y);
                float y = M4MapSetup.GroundY(terrain, xz.x + offset.x, xz.y + offset.z);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            if (maxY - minY > MapPolishLayout.MaxGroundSpanM)
                problems.AppendLine($"  {name}: 발자국 지면 높이 차 {maxY - minY:0.0}m > {MapPolishLayout.MaxGroundSpanM}m (경사가 크다)");
            var wrapper = new GameObject(name).transform;
            wrapper.SetParent(group, false);
            wrapper.SetPositionAndRotation(new Vector3(xz.x, minY, xz.y), yaw);
            return wrapper;
        }

        private static void InstantiateModel(Transform wrapper, MapPolishPrefabs.Spec spec)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Path);
            if (prefab == null)
                throw new InvalidOperationException($"{Tag} 프리팹 없음: {spec.Path} — 먼저 Create Landmark Prefabs");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, wrapper.gameObject.scene);
            model.name = MapPolishLayout.ModelChild;
            model.transform.SetParent(wrapper, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
        }

        // 엄폐 상자마다 앵커를 두고 M4 규칙으로 엄폐 지점을 만든다(지면에 붙음). 높이 ≤ 1.3m면 Low.
        // 래퍼가 발자국 최저 지면에 있어 오르막 쪽은 묻힌다 — 엄폐 지점마다 드러난 높이(상자 윗면 − 그 지점 지면)로
        // High → Low로 내리거나(≤ 1.3m) 뺀다(< 0.9m) (리뷰 M6). 조정·뺀 수를 돌려준다.
        private static int AddCovers(M4MapSetup.BlockoutBuilder builder, Transform wrapper, MapPolishPrefabs.CoverBox box, int index)
        {
            var anchor = new GameObject($"CoverAnchor_{index + 1:00}").transform;
            anchor.SetParent(wrapper, false);
            anchor.localPosition = new Vector3(box.CenterXZ.x, 0f, box.CenterXZ.y);
            anchor.localRotation = Quaternion.Euler(0f, box.YawDeg, 0f);
            if (box.Around)
                builder.CoversAround(anchor, box.Size);
            else
                builder.CoversFrontBack(anchor, box.Size, box.Size.y <= M4MapSetup.LowCoverMaxHeightM ? CoverHeight.Low : CoverHeight.High);

            int adjusted = 0;
            float top = wrapper.position.y + box.Size.y;
            foreach (CoverPoint cover in anchor.GetComponentsInChildren<CoverPoint>(true))
            {
                float exposed = top - cover.transform.position.y;
                if (exposed < MapPolishLayout.MinCoverExposedM)
                {
                    Debug.Log($"{Tag} 엄폐 지점 뺌(드러난 높이 {exposed:0.00}m): {wrapper.name}/{cover.name}");
                    Object.DestroyImmediate(cover.gameObject);
                    adjusted++;
                    continue;
                }
                if (exposed > M4MapSetup.LowCoverMaxHeightM)
                    continue;
                var so = new SerializedObject(cover);
                SerializedProperty height = so.FindProperty("height");
                if (height.intValue == (int)CoverHeight.Low)
                    continue;
                height.intValue = (int)CoverHeight.Low;
                so.FindProperty("peekLocalOffset").vector3Value = Vector3.zero;
                so.ApplyModifiedPropertiesWithoutUndo();
                adjusted++;
            }
            return adjusted;
        }

        // 점광원. 그 자리 지면보다 MinLightAboveGroundM 아래면(경사로 묻힌 쪽) 지면 위로 올린다.
        private static void AddLights(Transform wrapper, MapPolishPrefabs.Spec spec, Terrain terrain)
        {
            for (int i = 0; i < spec.Lights.Length; i++)
            {
                MapPolishPrefabs.LightMount mount = spec.Lights[i];
                var go = new GameObject(LightName(mount.Kind, i));
                go.transform.SetParent(wrapper, false);
                go.transform.localPosition = mount.Local;
                Vector3 p = go.transform.position;
                float minY = M4MapSetup.GroundY(terrain, p.x, p.z) + MapPolishLayout.MinLightAboveGroundM;
                if (p.y < minY)
                    go.transform.position = new Vector3(p.x, minY, p.z);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.lightmapBakeType = LightmapBakeType.Realtime;
            }
        }

        private static string LightName(MapPolishPrefabs.LightKind kind, int index) =>
            index == 0 ? $"{MapPolishLayout.LightChild}_{kind}" : $"{MapPolishLayout.LightChild}_{kind}_{index}";

        // ---------- 광원 값 (매 실행) ----------

        // 맵 개선 그룹의 점광원을 SO 값으로 맞춘다. 바꾼 광원 수.
        internal static int RefreshLights(GameObject environment, AtmosphereConfigSO cfg, PaletteSO palette)
        {
            int changed = 0;
            foreach (string groupName in new[] { MapPolishLayout.LandmarkGroup, MapPolishLayout.LightGroup })
            {
                Transform group = environment.transform.Find(groupName);
                if (group == null)
                    continue;
                foreach (Light light in group.GetComponentsInChildren<Light>(true))
                {
                    if (!TryKindOf(light.name, out MapPolishPrefabs.LightKind kind))
                        continue;
                    MapLightStyle style = kind == MapPolishPrefabs.LightKind.Fire ? cfg.fireLight : cfg.lampLight;
                    Color color = Color.Lerp(palette.Get(style.token), palette.Get(PaletteToken.Bone), Mathf.Clamp01(style.boneMix));
                    color.a = 1f;
                    bool same = light.type == LightType.Point && light.shadows == LightShadows.None
                                && light.lightmapBakeType == LightmapBakeType.Realtime
                                && light.color == color && Mathf.Approximately(light.intensity, style.intensity)
                                && Mathf.Approximately(light.range, style.rangeM);
                    if (same)
                        continue;
                    light.type = LightType.Point;
                    light.shadows = LightShadows.None;
                    light.lightmapBakeType = LightmapBakeType.Realtime;
                    light.color = color;
                    light.intensity = style.intensity;
                    light.range = style.rangeM;
                    EditorUtility.SetDirty(light);
                    changed++;
                }
            }
            return changed;
        }

        public static bool TryKindOf(string lightName, out MapPolishPrefabs.LightKind kind)
        {
            kind = default;
            string prefix = MapPolishLayout.LightChild + "_";
            if (!lightName.StartsWith(prefix))
                return false;
            string rest = lightName.Substring(prefix.Length);
            int underscore = rest.IndexOf('_');
            return Enum.TryParse(underscore < 0 ? rest : rest.Substring(0, underscore), out kind);
        }

        // 씬 점광원 수 ≤ 상한, 전부 그림자 없음 (방향광 "Sun"은 제외). 성능 목적이라 씬 전체를 센다.
        internal static bool CheckLights(Scene scene, AtmosphereConfigSO cfg, out string report)
        {
            int point = 0, shadowed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Directional)
                    continue;
                point++;
                if (light.shadows != LightShadows.None)
                    shadowed++;
            }
            report = $"점광원 {point}/{cfg.maxMapLights}, 그림자 있는 점광원 {shadowed}";
            return point <= cfg.maxMapLights && shadowed == 0;
        }

        // 구조물(속을 채운 도형) 중심의 지면 점 근처에 NavMesh가 없어야 한다 = 구조물이 NavMesh에 구멍으로 반영됐다 (리뷰 H2·M2).
        internal static bool CheckCarved(Transform structureGroup, Terrain terrain, out string report)
        {
            int carved = 0, total = 0;
            var stale = new List<string>();
            foreach (Transform structure in structureGroup)
            {
                total++;
                Vector3 ground = M4MapSetup.GroundPoint(terrain, new Vector2(structure.position.x, structure.position.z));
                if (NavMesh.SamplePosition(ground, out NavMeshHit _, CarvedProbeM, NavMesh.AllAreas))
                    stale.Add(structure.name);
                else
                    carved++;
            }
            report = $"  구조물 자리 NavMesh 구멍 {carved}/{total}" + (stale.Count > 0 ? $" — 구멍 없음: {string.Join(", ", stale)}\n" : "\n");
            return total > 0 && carved == total;
        }

        // ---------- 배치 확인 (§6 1) ----------

        private static bool IsUnder(Transform t, Transform[] roots)
        {
            foreach (Transform root in roots)
                if (t.IsChildOf(root))
                    return true;
            return false;
        }

        // 새 콜라이더(OBB)가 기존 콜라이더(지형·트리거 제외)와 겹치지 않는가, 기존 지점들에서 떨어져 있는가, 저격 시선을 막지 않는가.
        private static void CheckPlacement(Scene scene, Transform[] newRoots, Terrain terrain, StringBuilder problems)
        {
            var newColliders = new List<Collider>();
            foreach (Transform root in newRoots)
                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                    if (!c.isTrigger)
                        newColliders.Add(c);

            // 기존 지점 (이름, 위치, 최소 거리)
            var points = new List<(string name, Vector3 position, float minDistance)>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (CoverPoint cover in root.GetComponentsInChildren<CoverPoint>(true))
                    if (!IsUnder(cover.transform, newRoots))
                        points.Add(($"엄폐 {cover.transform.parent?.name}/{cover.name}", cover.transform.position, MapPolishLayout.MinDistanceFromPointsM));
                foreach (LootContainer crate in root.GetComponentsInChildren<LootContainer>(true))
                    points.Add(($"상자 {crate.name}", crate.transform.position, MapPolishLayout.MinDistanceFromPointsM));
                foreach (EnemyAgent enemy in root.GetComponentsInChildren<EnemyAgent>(true))
                    points.Add(($"적 {enemy.name}", enemy.transform.position, MapPolishLayout.MinDistanceFromPointsM));
                foreach (PatrolRoute route in root.GetComponentsInChildren<PatrolRoute>(true))
                    foreach (Vector3 p in route.Points)
                        points.Add(($"순찰 {route.name}", p, MapPolishLayout.MinDistanceFromPointsM));
                foreach (PlayerSpawnPoint spawn in root.GetComponentsInChildren<PlayerSpawnPoint>(true))
                    points.Add(($"스폰 {spawn.Id}", spawn.transform.position, MapPolishLayout.MinDistanceFromSpawnExtM));
                foreach (ExtractionZone zone in root.GetComponentsInChildren<ExtractionZone>(true))
                    points.Add(($"탈출 {zone.Id}", zone.transform.position, MapPolishLayout.MinDistanceFromSpawnExtM));
                foreach (PowerSwitch powerSwitch in root.GetComponentsInChildren<PowerSwitch>(true))
                    points.Add(("전원 스위치", powerSwitch.transform.position, MapPolishLayout.MinDistanceFromSwitchM));
            }

            var hits = new Collider[OverlapBufferSize];
            var reported = new HashSet<string>();
            foreach (Collider c in newColliders)
            {
                Obb obb = Obb.Of(c);
                int count = Physics.OverlapBoxNonAlloc(obb.Center, obb.HalfExtents, hits, obb.Rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                if (count >= hits.Length)
                    problems.AppendLine($"  겹침 검사 버퍼 가득 참({hits.Length}): {Owner(c)} — 결과가 잘렸을 수 있다");
                for (int i = 0; i < count; i++)
                {
                    Collider other = hits[i];
                    if (other is TerrainCollider || IsUnder(other.transform, newRoots))
                        continue;
                    string key = $"{Owner(c)} ↔ {Owner(other)}";
                    if (reported.Add(key))
                        problems.AppendLine($"  겹침: {key}");
                }
                foreach (var point in points)
                {
                    // 지면 높이 지점이 OBB 아래로 빠지지 않게 수평 거리로 본다 (구조물 바닥이 지면 아래로 묻혀 있다)
                    float d = obb.HorizontalDistance(point.position);
                    if (d < point.minDistance)
                    {
                        string key = $"{Owner(c)} ↔ {point.name}";
                        if (reported.Add(key))
                            problems.AppendLine($"  가까움: {key} {d:0.0}m < {point.minDistance}m (지점 {point.position.x:0.0}, {point.position.z:0.0})");
                    }
                }
            }

            // 저격 시선 (M7 퍼치 → 감시 지점)을 새 콜라이더가 막지 않는다
            foreach ((Vector2 from, Vector2 to) in MapPolishLayout.SniperSightLines())
            {
                Vector3 eye = M4MapSetup.GroundPoint(terrain, from) + Vector3.up * MapPolishLayout.SniperEyeHeightM;
                Vector3 target = M4MapSetup.GroundPoint(terrain, to) + Vector3.up * MapPolishLayout.SniperTargetHeightM;
                foreach (RaycastHit hit in Physics.RaycastAll(eye, (target - eye).normalized, Vector3.Distance(eye, target),
                             Physics.AllLayers, QueryTriggerInteraction.Ignore))
                    if (IsUnder(hit.collider.transform, newRoots))
                        problems.AppendLine($"  저격 시선 {from}→{to}을 {Owner(hit.collider)}가 막는다");
            }
        }

        // 구조물 이름 (그룹 바로 아래 조상)
        private static string Owner(Collider c)
        {
            Transform t = c.transform;
            while (t.parent != null && t.parent.parent != null && t.parent.parent.name != M4MapSetup.EnvironmentName)
                t = t.parent;
            return t.parent != null ? $"{t.parent.name}/{t.name}" : t.name;
        }

        // 새 엄폐 지점 자리 또는 peek 자리가 다른 콜라이더 안이면 뺀다(서 있을 수 없는 자리, 리뷰 L2). 뺀 수.
        private static int RemoveCoversInsideColliders(List<CoverPoint> covers)
        {
            int removed = 0;
            var hits = new Collider[OverlapBufferSize];
            for (int i = covers.Count - 1; i >= 0; i--)
            {
                CoverPoint cover = covers[i];
                Vector3 p = cover.transform.position;
                Vector3 peekLocal = new SerializedObject(cover).FindProperty("peekLocalOffset").vector3Value;
                bool blocked = Blocked(p, hits) || (peekLocal != Vector3.zero && Blocked(cover.transform.TransformPoint(peekLocal), hits));
                if (!blocked)
                    continue;
                Debug.Log($"{Tag} 엄폐 지점 뺌(콜라이더 안): {cover.transform.parent?.name}/{cover.name} {p}");
                Object.DestroyImmediate(cover.gameObject);
                covers.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static bool Blocked(Vector3 groundPoint, Collider[] hits)
        {
            int count = Physics.OverlapCapsuleNonAlloc(groundPoint + Vector3.up * (CoverProbeRadiusM + 0.15f), groundPoint + Vector3.up * 1.4f,
                CoverProbeRadiusM, hits, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < count; h++)
                if (!(hits[h] is TerrainCollider))
                    return true;
            return false;
        }

        // 굽기 뒤 NavMesh 밖이거나 기준 스폰에서 닿을 수 없는 새 엄폐 지점을 뺀다(리뷰 M1). 뺀 수.
        private static int RemoveCoversOffMesh(List<CoverPoint> covers, Vector3? reference)
        {
            int removed = 0;
            var path = new NavMeshPath();
            for (int i = covers.Count - 1; i >= 0; i--)
            {
                Vector3 p = covers[i].transform.position;
                bool ok = NavMesh.SamplePosition(p, out NavMeshHit hit, CoverOnMeshM, NavMesh.AllAreas)
                          && (reference == null || (NavMesh.CalculatePath(reference.Value, hit.position, NavMesh.AllAreas, path)
                                                    && path.status == NavMeshPathStatus.PathComplete));
                if (ok)
                    continue;
                Debug.Log($"{Tag} 엄폐 지점 뺌(NavMesh 밖·도달 불가): {covers[i].transform.parent?.name}/{covers[i].name} {p}");
                Object.DestroyImmediate(covers[i].gameObject);
                covers.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        // 회전한 상자 (BoxCollider = 그 상자, MeshCollider(원통) = 메시 경계 상자)
        private readonly struct Obb
        {
            public readonly Vector3 Center;
            public readonly Vector3 HalfExtents;
            public readonly Quaternion Rotation;

            private Obb(Vector3 center, Vector3 halfExtents, Quaternion rotation)
            {
                Center = center;
                HalfExtents = halfExtents;
                Rotation = rotation;
            }

            public static Obb Of(Collider c)
            {
                Transform t = c.transform;
                Vector3 scale = t.lossyScale;
                Vector3 localCenter, localSize;
                if (c is BoxCollider box)
                {
                    localCenter = box.center;
                    localSize = box.size;
                }
                else if (c is MeshCollider mesh && mesh.sharedMesh != null)
                {
                    localCenter = mesh.sharedMesh.bounds.center;
                    localSize = mesh.sharedMesh.bounds.size;
                }
                else
                {
                    return new Obb(c.bounds.center, c.bounds.extents, Quaternion.identity);
                }
                Vector3 half = new Vector3(Mathf.Abs(localSize.x * scale.x), Mathf.Abs(localSize.y * scale.y), Mathf.Abs(localSize.z * scale.z)) * 0.5f;
                return new Obb(t.TransformPoint(localCenter), half, t.rotation);
            }

            // 점에서 상자까지의 수평 거리 (상자의 수평 투영을 상자 높이 전체로 본다)
            public float HorizontalDistance(Vector3 point)
            {
                Vector3 local = Quaternion.Inverse(Rotation) * (point - Center);
                Vector3 clamped = new Vector3(
                    Mathf.Clamp(local.x, -HalfExtents.x, HalfExtents.x),
                    Mathf.Clamp(local.y, -HalfExtents.y, HalfExtents.y),
                    Mathf.Clamp(local.z, -HalfExtents.z, HalfExtents.z));
                Vector3 nearest = Center + Rotation * clamped;
                return Vector2.Distance(new Vector2(point.x, point.z), new Vector2(nearest.x, nearest.z));
            }
        }

        // ---------- NavMesh 상태 (§6 2) ----------

        // 키는 인스턴스 ID (리뷰 L9 — 이름 중복에 영향받지 않게). 도달성 = 기준 스폰(첫 PlayerSpawnPoint)에서 경로 완성(리뷰 M1).
        private sealed class NavState
        {
            private readonly HashSet<int> allCovers = new HashSet<int>();
            private readonly Dictionary<int, Component> objects = new Dictionary<int, Component>();   // 실패 줄의 이름·위치용
            private readonly HashSet<int> coversOnMesh = new HashSet<int>();     // 굽기 전부터 있던 엄폐 지점 중 NavMesh 위
            private readonly HashSet<int> agentsOk = new HashSet<int>();
            private readonly HashSet<int> agentsReachable = new HashSet<int>();
            private readonly HashSet<string> segmentsOk = new HashSet<string>();
            private readonly HashSet<int> cratesOk = new HashSet<int>();
            private readonly HashSet<int> cratesReachable = new HashSet<int>();
            private int coverTotal, agentTotal, segmentTotal, crateTotal;

            public Vector3? Reference { get; private set; }

            public string Summary =>
                $"엄폐 {coversOnMesh.Count}/{coverTotal} NavMesh 위, 적 {agentsOk.Count}/{agentTotal}(스폰에서 도달 {agentsReachable.Count}), " +
                $"순찰 구간 {segmentsOk.Count}/{segmentTotal} 경로 완성, 상자 {cratesOk.Count}/{crateTotal}(스폰에서 도달 {cratesReachable.Count})";

            // previous가 있으면 엄폐 지점은 그때 있던 것만 센다(새 엄폐 지점은 따로 정리했다).
            public static NavState Capture(Scene scene, NavState previous)
            {
                var state = new NavState();
                var path = new NavMeshPath();
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (PlayerSpawnPoint spawn in root.GetComponentsInChildren<PlayerSpawnPoint>(true))
                        if (state.Reference == null && NavMesh.SamplePosition(spawn.transform.position, out NavMeshHit sh, AgentOnMeshM, NavMesh.AllAreas))
                            state.Reference = sh.position;

                bool Reachable(Vector3 to) => state.Reference != null
                                              && NavMesh.CalculatePath(state.Reference.Value, to, NavMesh.AllAreas, path)
                                              && path.status == NavMeshPathStatus.PathComplete;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (CoverPoint cover in root.GetComponentsInChildren<CoverPoint>(true))
                    {
                        int id = cover.GetInstanceID();
                        if (previous != null && !previous.allCovers.Contains(id))
                            continue;
                        state.allCovers.Add(id);
                        state.objects[id] = cover;
                        state.coverTotal++;
                        if (NavMesh.SamplePosition(cover.transform.position, out NavMeshHit _, CoverOnMeshM, NavMesh.AllAreas))
                            state.coversOnMesh.Add(id);
                    }
                    foreach (EnemyAgent enemy in root.GetComponentsInChildren<EnemyAgent>(true))
                    {
                        state.agentTotal++;
                        state.objects[enemy.GetInstanceID()] = enemy;
                        if (!NavMesh.SamplePosition(enemy.transform.position, out NavMeshHit eh, AgentOnMeshM, NavMesh.AllAreas))
                            continue;
                        state.agentsOk.Add(enemy.GetInstanceID());
                        if (Reachable(eh.position))
                            state.agentsReachable.Add(enemy.GetInstanceID());
                    }
                    foreach (PatrolRoute route in root.GetComponentsInChildren<PatrolRoute>(true))
                    {
                        state.objects[route.GetInstanceID()] = route;
                        IReadOnlyList<Vector3> points = route.Points;
                        for (int i = 0; i < points.Count && points.Count >= 2; i++)
                        {
                            state.segmentTotal++;
                            Vector3 a = points[i], b = points[(i + 1) % points.Count];
                            if (NavMesh.SamplePosition(a, out NavMeshHit ha, AgentOnMeshM, NavMesh.AllAreas)
                                && NavMesh.SamplePosition(b, out NavMeshHit hb, AgentOnMeshM, NavMesh.AllAreas)
                                && NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path)
                                && path.status == NavMeshPathStatus.PathComplete)
                                state.segmentsOk.Add($"{route.GetInstanceID()}#{i}");
                        }
                    }
                    foreach (LootContainer crate in root.GetComponentsInChildren<LootContainer>(true))
                    {
                        state.crateTotal++;
                        state.objects[crate.GetInstanceID()] = crate;
                        if (!NavMesh.SamplePosition(crate.transform.position, out NavMeshHit ch, AgentOnMeshM, NavMesh.AllAreas))
                            continue;
                        state.cratesOk.Add(crate.GetInstanceID());
                        if (Reachable(ch.position))
                            state.cratesReachable.Add(crate.GetInstanceID());
                    }
                }
                return state;
            }

            // before에서 되던 것이 지금 안 되면 실패 줄
            public string Regressions(NavState before)
            {
                var sb = new StringBuilder();
                foreach (int id in before.coversOnMesh)
                    if (!coversOnMesh.Contains(id))
                        sb.AppendLine($"  엄폐 지점이 NavMesh에서 떨어졌다: {Describe(id)}");
                Lost(sb, before.agentsOk, agentsOk, "적 시작점이 NavMesh 밖");
                Lost(sb, before.agentsReachable, agentsReachable, "적 시작점에 스폰에서 닿을 수 없다");
                foreach (string key in before.segmentsOk)
                    if (!segmentsOk.Contains(key))
                        sb.AppendLine($"  순찰 구간 경로 끊김: {Describe(int.Parse(key.Substring(0, key.IndexOf('#'))))} 구간 {key.Substring(key.IndexOf('#') + 1)}");
                Lost(sb, before.cratesOk, cratesOk, "상자 옆 NavMesh 없음");
                Lost(sb, before.cratesReachable, cratesReachable, "상자에 스폰에서 닿을 수 없다");
                return sb.ToString();
            }

            private void Lost(StringBuilder sb, HashSet<int> before, HashSet<int> now, string message)
            {
                foreach (int id in before)
                    if (!now.Contains(id))
                        sb.AppendLine($"  {message}: {Describe(id)}");
            }

            private string Describe(int instanceId)
            {
                objects.TryGetValue(instanceId, out Component c);
                return c != null ? $"{(c.transform.parent != null ? c.transform.parent.name + "/" : "")}{c.name} {c.transform.position}" : instanceId.ToString();
            }
        }
    }
}
