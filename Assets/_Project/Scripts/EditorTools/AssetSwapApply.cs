using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using C = Ashfall.EditorTools.AssetSwapCatalog;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // 블록아웃 위에 스킨 얹기 (AssetSwap 설계 §3·§4). 대상마다 자식 "Skin"(여러 개면 "Skin_02"…) = 스킨 프리팹 인스턴스 + BlockoutSkin(숨길 렌더러).
    // 블록아웃(렌더러·머티리얼·콜라이더·GroundSurface·엄폐 지점)은 건드리지 않는다. 다시 실행하면 같은 스킨은 재사용하고 위치만 다시 맞춘다(멱등).
    // 원본 패키지가 없어 스킨 프리팹이 없는 대상은 건너뛰고, 깨진(Missing Prefab) 스킨은 지운다.
    public static class AssetSwapApply
    {
        private const string Tag = "[AssetSwapApply]";
        private const float Eps = 1e-4f;

        private static readonly Regex StructureName = new Regex(@"^(Vehicle|VehiclePile|House|Container|LowWall|HighWall)_\d+$");
        private static readonly Regex CrashedPlaneName = new Regex("^" + C.CrashedPlaneLandmark + @"_\d+$");

        public sealed class Result
        {
            public int Weapons, Enemies, EnemyGuns, Structures, Crates, Drums, Landmarks, Removed;
            public bool Changed;
            public readonly List<string> Problems = new List<string>();

            public override string ToString() =>
                $"무기 {Weapons}, 적 {Enemies}(총 {EnemyGuns}), 구조물 {Structures}, 상자 {Crates}, 드럼통 {Drums}, 랜드마크 {Landmarks}, 지운 스킨 {Removed}";
        }

        // 놓을 스킨 하나 (월드 기준). Scale = 월드 배율(부모 배율을 나눠 로컬로 바꾼다 — 부모와 축이 같은 방향일 때만 쓴다).
        private readonly struct Placement
        {
            public readonly string SkinId;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public Placement(string skinId, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                SkinId = skinId;
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }

        public static bool IsSkinName(string name) => name == C.SkinChild || name.StartsWith(C.SkinChild + "_");

        // 스킨 인스턴스인가: 이름이 "Skin"으로 시작하고, BlockoutSkin이 붙었거나 Skins 폴더 프리팹의 인스턴스이거나 원본이 사라진(Missing Prefab) 인스턴스.
        // 이름만 보지 않는다 — 깨진 인스턴스는 표시 이름이 달라질 수 있고, 사용자가 "Skin…" 이름을 다른 데 쓸 수도 있다.
        public static bool IsSkinObject(GameObject go)
        {
            if (go == null || !go.name.StartsWith(C.SkinChild))
                return false;
            if (go.GetComponent<BlockoutSkin>() != null || PrefabUtility.IsPrefabAssetMissing(go))
                return true;
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            return source != null && AssetDatabase.GetAssetPath(source).StartsWith(C.SkinFolder + "/");
        }

        // ---------- 스킨 크기 (프리팹 원점 기준 경계, 캐시) ----------

        private static readonly Dictionary<GameObject, Bounds> SizeCache = new Dictionary<GameObject, Bounds>();

        public static Bounds SkinBounds(GameObject prefab)
        {
            if (SizeCache.TryGetValue(prefab, out Bounds b))
                return b;
            // 미리보기 씬에서 잰다 (열려 있는 씬을 더럽히지 않게)
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                temp.transform.localScale = Vector3.one;
                b = AssetSwapSkins.LocalBounds(temp.transform);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
            SizeCache[prefab] = b;
            return b;
        }

        public static void ClearCache() => SizeCache.Clear();

        // ---------- 공통: 부모 아래 스킨 맞추기 ----------

        private static bool Sync(Transform parent, IReadOnlyList<Placement> wanted, Renderer[] hide, IReadOnlyDictionary<string, GameObject> skins, Result result)
        {
            var existing = new List<Transform>();
            foreach (Transform child in parent)
                if (IsSkinObject(child.gameObject))
                    existing.Add(child);

            bool changed = false;
            for (int i = 0; i < wanted.Count; i++)
            {
                if (!skins.TryGetValue(wanted[i].SkinId, out GameObject prefab))
                    return changed;   // 스킨이 없는 대상 — 위에서 걸러지지만 안전하게
                Transform skin = i < existing.Count ? existing[i] : null;
                if (skin != null && PrefabUtility.GetCorrespondingObjectFromSource(skin.gameObject) != prefab)
                {
                    Object.DestroyImmediate(skin.gameObject);
                    existing[i] = null;
                    skin = null;
                }
                if (skin == null)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
                    if (go == null)
                        go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    skin = go.transform;
                    skin.SetParent(parent, false);
                    changed = true;
                }
                string name = i == 0 ? C.SkinChild : $"{C.SkinChild}_{i + 1:00}";
                if (skin.name != name)
                {
                    skin.name = name;
                    changed = true;
                }
                changed |= SetPose(skin, wanted[i]);
                var marker = skin.GetComponent<BlockoutSkin>();
                if (marker == null)
                {
                    marker = skin.gameObject.AddComponent<BlockoutSkin>();
                    changed = true;
                }
                // 같은 블록아웃을 여러 스킨이 숨기면 하나만 꺼져도(OnDisable) 겹쳐 보인다 — 숨김 목록은 첫 스킨만 갖는다
                Renderer[] own = i == 0 ? hide : System.Array.Empty<Renderer>();
                if (!SameRenderers(marker.Hidden, own))
                {
                    marker.SetHidden(own);
                    EditorUtility.SetDirty(marker);
                    changed = true;
                }
            }
            for (int i = wanted.Count; i < existing.Count; i++)
                if (existing[i] != null)
                {
                    Object.DestroyImmediate(existing[i].gameObject);
                    result.Removed++;
                    changed = true;
                }
            return changed;
        }

        private static bool SetPose(Transform t, Placement p)
        {
            Transform parent = t.parent;
            Vector3 localPos = parent.InverseTransformPoint(p.Position);
            Quaternion localRot = Quaternion.Inverse(parent.rotation) * p.Rotation;
            Vector3 ps = parent.lossyScale;
            var localScale = new Vector3(p.Scale.x / ps.x, p.Scale.y / ps.y, p.Scale.z / ps.z);
            bool changed = false;
            if ((t.localPosition - localPos).sqrMagnitude > Eps * Eps)
            {
                t.localPosition = localPos;
                changed = true;
            }
            if (Quaternion.Angle(t.localRotation, localRot) > 0.01f)
            {
                t.localRotation = localRot;
                changed = true;
            }
            if ((t.localScale - localScale).sqrMagnitude > Eps * Eps)
            {
                t.localScale = localScale;
                changed = true;
            }
            return changed;
        }

        private static bool SameRenderers(Renderer[] a, Renderer[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        // root 아래에서 스킨 하위를 뺀 렌더러 (블록아웃)
        private static Renderer[] BlockoutRenderers(Transform root, System.Func<Renderer, bool> keep = null)
        {
            var list = new List<Renderer>();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (!UnderSkin(r.transform, root) && (keep == null || keep(r)))
                    list.Add(r);
            return list.ToArray();
        }

        // t가 root 아래 "Skin" 하위에 있는가 (root 자신이 스킨이면 false)
        public static bool UnderSkin(Transform t, Transform root = null)
        {
            for (Transform x = t; x != null && x != root; x = x.parent)
                if (IsSkinObject(x.gameObject))
                    return true;
            return false;
        }

        // ---------- 무기 뷰모델 프리팹 ----------

        public static void ApplyWeapons(IReadOnlyDictionary<string, GameObject> skins, Result result)
        {
            foreach (KeyValuePair<string, string> pair in C.WeaponSkins)
            {
                string path = M7Names.ViewModelPrefabPath(pair.Key);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    result.Problems.Add($"{path} 없음 — 먼저 Ashfall/Setup/Apply M7 Content");
                    continue;
                }
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform root = contents.transform;
                    bool changed = RemoveBroken(root, result);
                    Transform muzzle = root.Find(M7Names.MuzzleChild);
                    Renderer[] hide = BlockoutRenderers(root);
                    if (!skins.TryGetValue(pair.Value, out GameObject prefab))
                        changed |= RemoveSkins(root, result);
                    else if (muzzle == null || hide.Length == 0)
                        result.Problems.Add($"{path}: Muzzle 또는 자리표시 렌더러 없음");
                    else
                    {
                        Bounds placeholder = WorldBounds(hide);
                        float s = placeholder.size.z / Mathf.Max(SkinBounds(prefab).size.z, Eps);
                        var p = new Placement(pair.Value, muzzle.position, root.rotation, Vector3.one * s);
                        changed |= Sync(root, new[] { p }, hide, skins, result);
                        result.Weapons++;
                    }
                    // 저장 전에 검사 (실패하면 이 프리팹은 저장하지 않는다)
                    var report = new System.Text.StringBuilder();
                    if (!Validate(root, report))
                    {
                        result.Problems.Add($"{path}:\n{report}");
                        changed = false;
                    }
                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        result.Changed = true;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
        }

        // ---------- 레이드 씬 ----------

        public static bool ApplyRaid(Scene scene, IReadOnlyDictionary<string, GameObject> skins, Result result)
        {
            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
                changed |= RemoveBroken(root.transform, result);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var agent = root.GetComponent<EnemyAgent>();
                if (agent != null)
                    changed |= ApplyEnemy(agent, skins, result);
            }

            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            if (environment == null)
                result.Problems.Add($"\"{M4MapSetup.EnvironmentName}\" 없음 — 먼저 Ashfall/Setup/Create M4 Raid Scenes");
            else
            {
                foreach (Transform t in environment.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || UnderSkin(t))   // 앞에서 지운 스킨 하위
                        continue;
                    if (IsSkinObject(t.gameObject))
                        continue;
                    if (StructureName.IsMatch(t.name))
                        changed |= ApplyStructure(t, skins, result);
                    else if (CrashedPlaneName.IsMatch(t.name))
                        changed |= ApplyCrashedPlane(t, skins, result);
                    else if (t.GetComponent<LootContainer>() != null)
                        changed |= ApplyCrate(t, skins, result);
                }
            }

            GameObject markers = SetupUtil.FindRoot(scene, M7Names.ToxicMarkersObject);
            if (markers != null)
                foreach (Transform marker in markers.transform)
                    if (IsDrum(marker.name))
                        changed |= ApplyDrum(marker, skins, result);
            if (changed)
                EditorSceneManager.MarkSceneDirty(scene);   // 위치만 바뀐 경우도 저장되게
            result.Changed |= changed;
            return changed;
        }

        private static bool ApplyEnemy(EnemyAgent agent, IReadOnlyDictionary<string, GameObject> skins, Result result)
        {
            string id = agent.Archetype != null ? agent.Archetype.id : null;
            if (id == null || !C.EnemySkins.TryGetValue(id, out (string Body, string Gun) pick))
            {
                result.Problems.Add($"{agent.name}: 유형 {id ?? "(없음)"} 배정 없음");
                return false;
            }
            Transform root = agent.transform;
            bool changed = false;

            Transform body = root.Find("Body");
            var bodyRenderer = body != null ? body.GetComponent<Renderer>() : null;
            if (bodyRenderer != null && skins.TryGetValue(pick.Body, out GameObject bodySkin))
            {
                float bottom = bodyRenderer.bounds.min.y;
                float s = bodyRenderer.bounds.size.y / Mathf.Max(SkinBounds(bodySkin).size.y, Eps);   // 키 = 캡슐 키(선 자세)
                var p = new Placement(pick.Body, new Vector3(body.position.x, bottom, body.position.z), root.rotation, Vector3.one * s);
                changed |= Sync(body, new[] { p }, new[] { bodyRenderer }, skins, result);
                result.Enemies++;
            }
            else if (body != null)
                changed |= RemoveSkins(body, result);   // 원본 패키지가 빠졌으면 이전 스킨을 지운다

            Transform muzzle = root.Find(M7Names.MuzzleChild);
            Transform gun = muzzle != null ? muzzle.Find("Gun") : null;
            var gunRenderer = gun != null ? gun.GetComponent<Renderer>() : null;
            if (pick.Gun != null && gunRenderer != null && skins.TryGetValue(pick.Gun, out GameObject gunSkin))
            {
                float s = C.EnemyGunLengthM / Mathf.Max(SkinBounds(gunSkin).size.z, Eps);
                var p = new Placement(pick.Gun, muzzle.position, muzzle.rotation, Vector3.one * s);
                changed |= Sync(muzzle, new[] { p }, new[] { gunRenderer }, skins, result);
                result.EnemyGuns++;
            }
            else if (muzzle != null)
                changed |= RemoveSkins(muzzle, result);
            return changed;
        }

        private static bool ApplyStructure(Transform structure, IReadOnlyDictionary<string, GameObject> skins, Result result)
        {
            string shape = structure.name.Substring(0, structure.name.LastIndexOf('_'));
            var wanted = new List<Placement>();
            var hide = new List<Renderer>();
            float ground = structure.position.y;

            switch (shape)
            {
                case "Vehicle":
                case "VehiclePile":
                    foreach (Transform block in structure)
                    {
                        if (block.name != "Block" && block.name != "Car" && block.name != "Car_Upper")
                            continue;
                        string id = C.Pick(C.CarSkins, structure.name + block.name);
                        if (!skins.TryGetValue(id, out GameObject prefab))
                            continue;
                        float baseY = block.name == "Car_Upper" ? block.position.y - block.lossyScale.y * 0.5f : ground;
                        wanted.Add(Footprint(id, prefab, block, baseY, fill: false));
                        hide.Add(block.GetComponent<Renderer>());
                    }
                    break;
                case "House":
                case "Container":
                {
                    Transform block = structure.Find("Block");
                    string id = shape == "House" ? C.Pick(C.HouseSkins, structure.name) : C.ContainerSkin;
                    if (block == null || !skins.TryGetValue(id, out GameObject prefab))
                        break;
                    wanted.Add(Footprint(id, prefab, block, ground, fill: true));
                    hide.Add(block.GetComponent<Renderer>());
                    break;
                }
                case "LowWall":
                case "HighWall":
                {
                    Transform block = structure.Find("Block");
                    string id = shape == "LowWall" ? C.LowWallSkin : C.HighWallSkin;
                    if (block == null || Mathf.Min(block.lossyScale.x, block.lossyScale.z) > C.MaxWallThicknessM
                        || !skins.TryGetValue(id, out GameObject prefab))
                        break;
                    wanted.AddRange(Tiles(id, prefab, block, ground));
                    hide.Add(block.GetComponent<Renderer>());
                    break;
                }
            }

            hide.RemoveAll(r => r == null);
            bool changed;
            if (wanted.Count == 0)
                changed = RemoveSkins(structure, result);
            else
            {
                result.Structures++;
                changed = Sync(structure, wanted, hide.ToArray(), skins, result);
            }
            // 맵 밀도 §8.1: 구조물 컬 LODGroup이 있으면 스킨 렌더러도 같은 LOD에 넣는다(멀어지면 스킨도 같이 컬)
            return MapDensitySceneApply.IncludeRenderers(structure.GetComponent<LODGroup>()) | changed;
        }

        // 블록 발자국에 맞춘 스킨 하나. fill = 축마다 상자를 채움(건물·컨테이너), 아니면 수평 균등 비율(차량).
        private static Placement Footprint(string id, GameObject prefab, Transform block, float baseY, bool fill)
        {
            Vector3 size = SkinBounds(prefab).size;
            Vector3 scale = block.lossyScale;
            float top = block.position.y + scale.y * 0.5f;
            float height = Mathf.Max(C.MinFitHeightM, top - baseY);
            (float yaw, Vector3 target) = C.AlignLongAxis(size, scale.x, height, scale.z);
            Vector3 s = fill ? C.FillScale(size, target) : Vector3.one * C.FitInside(size, new Vector3(target.x, 0f, target.z));
            Vector3 position = new Vector3(block.position.x, baseY, block.position.z);
            return new Placement(id, position, block.rotation * Quaternion.Euler(0f, yaw, 0f), s);
        }

        // 벽: 길이 방향으로 모듈을 이어 붙이고 높이·두께를 채운다.
        private static IEnumerable<Placement> Tiles(string id, GameObject prefab, Transform block, float ground)
        {
            Vector3 size = SkinBounds(prefab).size;
            Vector3 scale = block.lossyScale;
            float top = block.position.y + scale.y * 0.5f;
            float height = Mathf.Max(C.MinFitHeightM, top - ground);
            (float yaw, Vector3 target) = C.AlignLongAxis(size, scale.x, height, scale.z);
            bool longX = size.x >= size.z;
            float module = longX ? size.x : size.z;
            float length = Mathf.Max(scale.x, scale.z);
            (int count, float stretch) = C.Tile(length, module);
            Vector3 fill = C.FillScale(size, target);
            // 길이 축은 모듈 하나 길이 × stretch
            if (longX) fill.x = stretch; else fill.z = stretch;
            Vector3 along = block.rotation * (scale.x >= scale.z ? Vector3.right : Vector3.forward);
            Quaternion rotation = block.rotation * Quaternion.Euler(0f, yaw, 0f);
            var center = new Vector3(block.position.x, ground, block.position.z);
            for (int i = 0; i < count; i++)
                yield return new Placement(id, center + along * C.TileCenter(i, count, length), rotation, fill);
        }

        private static bool ApplyCrate(Transform crate, IReadOnlyDictionary<string, GameObject> skins, Result result)
        {
            // 맵 밀도 ③ 트렁크(설계 §5.4): 렌더러 없는 차량 안 콜라이더 — 겉모습은 차량 스킨 그대로, 상자 스킨을 얹지 않는다
            var slot = crate.GetComponent<LootSpawnSlot>();
            if (slot != null && slot.Kind == LootSlotKind.Trunk)
                return RemoveSkins(crate, result);
            string id = C.Pick(C.CrateSkins, crate.name);
            var renderer = crate.GetComponent<Renderer>();
            if (renderer == null || !skins.TryGetValue(id, out GameObject prefab))
                return RemoveSkins(crate, result);
            Vector3 size = SkinBounds(prefab).size;
            float s = C.FitInside(size, crate.lossyScale);
            float bottom = crate.position.y - crate.lossyScale.y * 0.5f;
            var p = new Placement(id, new Vector3(crate.position.x, bottom, crate.position.z), crate.rotation, Vector3.one * s);
            result.Crates++;
            bool changed = Sync(crate, new[] { p }, new[] { renderer }, skins, result);
            // 맵 밀도 ③ 새 상자의 컬 LODGroup(설계 §8.1)에 스킨 렌더러도 넣는다
            return MapDensitySceneApply.IncludeRenderers(crate.GetComponent<LODGroup>()) | changed;
        }

        private static bool IsDrum(string name) =>
            name.StartsWith(M7Names.ToxicMarkerPrefix + "Drum_") || name.StartsWith(M7Names.ToxicMarkerPrefix + "Pile");

        // 드럼통 (원통 프리미티브, 높이 = 2 × scale.y): 원통 축 그대로, 밑면 = 축 방향 아래 끝
        private static bool ApplyDrum(Transform drum, IReadOnlyDictionary<string, GameObject> skins, Result result)
        {
            string id = C.Pick(C.DrumSkins, drum.name);
            var renderer = drum.GetComponent<Renderer>();
            if (renderer == null || !skins.TryGetValue(id, out GameObject prefab))
                return RemoveSkins(drum, result);
            Vector3 ds = drum.lossyScale;
            var target = new Vector3(ds.x, ds.y * 2f, ds.z);
            float s = C.FitInside(SkinBounds(prefab).size, target);
            Vector3 bottom = drum.position - drum.up * ds.y;
            var p = new Placement(id, bottom, drum.rotation, Vector3.one * s);
            result.Drums++;
            return Sync(drum, new[] { p }, new[] { renderer }, skins, result);
        }

        // 추락기 랜드마크: 래퍼 아래 "Model"(블록아웃 프리팹)을 숨기고(잔불 발광은 남김) 헬기 잔해를 발자국에 맞춘다.
        private static bool ApplyCrashedPlane(Transform wrapper, IReadOnlyDictionary<string, GameObject> skins, Result result)
        {
            Transform model = wrapper.Find("Model");
            if (model == null || !skins.TryGetValue(C.CrashedPlaneSkin, out GameObject prefab))
                return RemoveSkins(wrapper, result);
            Renderer[] hide = BlockoutRenderers(model, r => r.sharedMaterial == null || r.sharedMaterial.name != PaletteMaterialMap.Materials[PaletteMaterialMap.Key.EmberGlow].Name);
            if (hide.Length == 0)
                return false;
            Bounds local = LocalBounds(wrapper, hide);
            Vector3 size = SkinBounds(prefab).size;
            (float yaw, Vector3 target) = C.AlignLongAxis(size, local.size.x, 0f, local.size.z);
            float s = Mathf.Min(C.FitInside(size, new Vector3(target.x, 0f, target.z)), C.CrashMaxScale);
            Vector3 center = wrapper.TransformPoint(new Vector3(local.center.x, 0f, local.center.z));
            var p = new Placement(C.CrashedPlaneSkin, new Vector3(center.x, wrapper.position.y - C.CrashSinkM, center.z),
                wrapper.rotation * Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(C.CrashTiltEuler), Vector3.one * s);
            result.Landmarks++;
            return Sync(wrapper, new[] { p }, hide, skins, result);
        }

        // ---------- 제거 ----------

        // 원본 프리팹이 없는(Missing) 스킨을 지운다
        public static bool RemoveBroken(Transform root, Result result)
        {
            bool changed = false;
            var doomed = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (IsSkinObject(t.gameObject) && PrefabUtility.IsPrefabAssetMissing(t.gameObject))
                    doomed.Add(t.gameObject);
            foreach (GameObject go in doomed)
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                    result.Removed++;
                    changed = true;
                }
            return changed;
        }

        // root 아래 모든 스킨(BlockoutSkin이 붙은 "Skin*" 또는 깨진 스킨)을 지운다
        public static bool RemoveSkins(Transform root, Result result)
        {
            var doomed = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && IsSkinObject(t.gameObject))
                    doomed.Add(t.gameObject);
            bool changed = false;
            foreach (GameObject go in doomed)
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                    result.Removed++;
                    changed = true;
                }
            return changed;
        }

        // ---------- 경계 ----------

        private static Bounds WorldBounds(Renderer[] renderers)
        {
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers)
                b.Encapsulate(r.bounds);
            return b;
        }

        // 렌더러 경계(월드 AABB의 꼭짓점)를 root 로컬로
        private static Bounds LocalBounds(Transform root, Renderer[] renderers)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (Renderer r in renderers)
            {
                Bounds w = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = w.center + Vector3.Scale(w.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = root.InverseTransformPoint(corner);
                    if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                    else bounds.Encapsulate(local);
                }
            }
            return bounds;
        }

        // 적용 결과 검사: 스킨에 콜라이더 없음, BlockoutSkin 숨김 목록이 비어 있지 않다.
        public static bool Validate(Transform root, StringBuilder report)
        {
            bool ok = true;
            foreach (BlockoutSkin skin in root.GetComponentsInChildren<BlockoutSkin>(true))
            {
                if (skin.GetComponentsInChildren<Collider>(true).Length > 0)
                {
                    report.AppendLine($"  {Path(skin.transform)}: 스킨에 콜라이더");
                    ok = false;
                }
                if (skin.name == C.SkinChild && (skin.Hidden == null || skin.Hidden.Length == 0) || skin.Hidden != null && System.Array.Exists(skin.Hidden, r => r == null))
                {
                    report.AppendLine($"  {Path(skin.transform)}: 숨길 블록아웃 렌더러 없음");
                    ok = false;
                }
            }
            return ok;
        }

        private static string Path(Transform t) => t.parent != null ? Path(t.parent) + "/" + t.name : t.name;
    }
}
