using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Source = Ashfall.EditorTools.AssetSwapCatalog.Source;

namespace Ashfall.EditorTools
{
    // 스킨 프리팹·머티리얼·합친 메시 생성 (AssetSwap 설계 §3·§5). 출력 = Assets/_Project/Skins/ (git 제외, 매번 다시 씀 — GUID 유지).
    // 스킨 = 원본 복제(프리팹 연결 끊음) → 콜라이더·스크립트·Rigidbody 제거 → (사람) Idle 첫 프레임 자세 → 머티리얼을 팔레트 틴트 사본으로
    // → 부품을 머티리얼별 서브메시 하나로 합친 렌더러 1개(스키닝·Animator 없음) → 앵커가 원점에 오도록 감싼다 (성능, 설계 §5.1).
    // 원본이 없으면 그 스킨은 만들지 않고, 예전에 만든 그 스킨 프리팹도 지운다(적용 쪽이 그 스킨을 씬에서 뺀다).
    // 이번 실행에 쓰지 않은 머티리얼 사본·메시도 지운다. 원본 패키지가 하나도 없으면 아무것도 만들지 않는다.
    public static class AssetSwapSkins
    {
        private const string Tag = "[AssetSwapSkins]";

        private static readonly HashSet<string> Written = new HashSet<string>();

        // 만든 스킨 id → 프리팹. 원본이 없어 못 만든 id는 빠진다.
        public static Dictionary<string, GameObject> BuildAll(PaletteSO palette, List<string> missing)
        {
            var result = new Dictionary<string, GameObject>();
            if (!AssetSwapCatalog.Sources.Any(s => AssetDatabase.LoadAssetAtPath<GameObject>(s.Path) != null))
            {
                missing.AddRange(AssetSwapCatalog.Sources.Select(s => s.Path));
                return result;   // 패키지 없는 clone — 스킨 폴더를 만들지 않는다
            }
            SetupUtil.EnsureFolder(AssetSwapCatalog.SkinFolder);
            SetupUtil.EnsureFolder(AssetSwapCatalog.SkinMaterialFolder);
            SetupUtil.EnsureFolder(AssetSwapCatalog.SkinMeshFolder);
            AssetDatabase.DeleteAsset(AssetSwapCatalog.LegacyIdleControllerPath);   // 예전 버전의 Idle 컨트롤러(자세를 구운 뒤로 안 씀)
            Written.Clear();
            var materialCache = new Dictionary<(Material, PaletteToken), Material>();
            AnimationClip idle = null;
            foreach (Source source in AssetSwapCatalog.Sources)
            {
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(source.Path);
                if (original == null)
                {
                    missing.Add(source.Path);
                    AssetDatabase.DeleteAsset(AssetSwapCatalog.SkinPrefabPath(source.Id));   // 없으면 아무 일도 없다
                    continue;
                }
                if (source.Character && idle == null)
                    idle = LoadIdleClip();
                GameObject prefab = Build(source, original, palette, materialCache, idle);
                if (prefab != null)
                    result[source.Id] = prefab;
            }
            // 이번에 쓰지 않은 머티리얼 사본·합친 메시(원본이 빠졌거나 이름 규칙이 바뀐 것) 정리
            foreach (string guid in AssetDatabase.FindAssets("t:Material t:Mesh", new[] { AssetSwapCatalog.SkinMaterialFolder, AssetSwapCatalog.SkinMeshFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Written.Contains(path))
                    AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        private static AnimationClip LoadIdleClip()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetSwapCatalog.IdleClipPath);
            if (clip == null)
                Debug.LogWarning($"{Tag} Idle 클립 {AssetSwapCatalog.IdleClipPath} 없음 — 사람 스킨을 바인드 포즈로 굽는다.");
            return clip;
        }

        private static GameObject Build(Source source, GameObject original, PaletteSO palette,
            Dictionary<(Material, PaletteToken), Material> materialCache, AnimationClip idle)
        {
            string path = AssetSwapCatalog.SkinPrefabPath(source.Id);
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                GameObject model = Object.Instantiate(original);
                model.name = original.name;
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(source.Euler);
                foreach (string extraPath in source.Extras)
                {
                    var extraAsset = AssetDatabase.LoadAssetAtPath<GameObject>(extraPath);
                    if (extraAsset == null)
                    {
                        Debug.LogWarning($"{Tag} {source.Id}: 함께 놓을 원본 {extraPath} 없음 — 빼고 만든다");
                        continue;
                    }
                    GameObject extra = Object.Instantiate(extraAsset, model.transform, false);
                    extra.name = extraAsset.name;
                }
                Strip(model);

                // 사람: Idle 첫 프레임 자세로 세운 뒤 정적 메시로 굽는다(스키닝·Animator 없음 — 성능, 설계 §5.1)
                if (source.Character)
                    Pose(model, idle, source.Id);

                bool weapon = source.Anchor == AssetSwapCatalog.Anchor.FrontTip;
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null)
                            mats[i] = Convert(mats[i], source.Tint, palette, materialCache);
                    r.sharedMaterials = mats;
                }

                // 부품을 머티리얼별 서브메시 하나로 합친다 → 스킨마다 렌더러 1개 (드로콜·컬링 비용, 설계 §5.1)
                GameObject merged = Merge(root.transform, model, source.Id);
                Object.DestroyImmediate(model);
                var renderer = merged.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = weapon ? ShadowCastingMode.Off : ShadowCastingMode.On;
                // 정적 배칭은 쓰지 않는다 — 배칭된 메시는 Mesh LOD를 고르지 못한다(렌더러 1개라 SRP Batcher로 충분)

                // 앵커를 원점으로
                Bounds bounds = LocalBounds(merged.transform.parent, merged.transform);
                Vector3 offset = -AssetSwapCatalog.AnchorPoint(bounds, source.Anchor);
                merged.transform.localPosition = offset;

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
                if (!ok || saved == null)
                {
                    Debug.LogError($"{Tag} 스킨 저장 실패: {path}");
                    return null;
                }
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 렌더러·메시·Animator만 남긴다 (게임 판정·스크립트가 섞이지 않게). Animator는 사람 자세를 잡는 데만 쓰고 합칠 때 사라진다.
        private static void Strip(GameObject model)
        {
            // 스크립트를 먼저 (RequireComponent로 묶인 CharacterController 등은 스크립트가 남아 있으면 지워지지 않는다).
            // 스크립트끼리 RequireComponent가 걸릴 수 있어 몇 번 돈다. 스크립트 파일이 없는(Missing Script) 컴포넌트도 지운다.
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            for (int pass = 0; pass < 4; pass++)
                foreach (MonoBehaviour mb in model.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb != null)
                        Object.DestroyImmediate(mb);
            foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
            foreach (Rigidbody rb in model.GetComponentsInChildren<Rigidbody>(true))
                Object.DestroyImmediate(rb);
            if (model.GetComponentsInChildren<Component>(true).Any(c => c is MonoBehaviour || c is Collider || c is Rigidbody))
                Debug.LogError($"{Tag} {model.name}: 스크립트·콜라이더를 다 지우지 못했다");
        }

        // Idle 클립 첫 프레임을 휴머노이드 아바타로 평가해 뼈를 그 자세로 둔다. 편집 모드 샘플링(AnimationMode)은 끝나면 되돌려지므로
        // 샘플링 중의 뼈 로컬 변환을 기록해 두었다가 다시 쓴다. 손이 움직이지 않았으면(자세 실패) 경고.
        private static void Pose(GameObject model, AnimationClip idle, string id)
        {
            Animator animator = model.GetComponentInChildren<Animator>(true);
            if (idle == null || animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogWarning($"{Tag} {id}: Idle 클립 또는 휴머노이드 아바타가 없어 바인드 포즈로 굽는다.");
                return;
            }
            animator.applyRootMotion = false;
            Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Vector3 handBefore = hand != null ? hand.position : Vector3.zero;
            Transform[] bones = animator.GetComponentsInChildren<Transform>(true);
            var posed = new (Vector3 p, Quaternion r, Vector3 s)[bones.Length];
            bool started = !AnimationMode.InAnimationMode();
            if (started)
                AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animator.gameObject, idle, 0f);
                AnimationMode.EndSampling();
                for (int i = 0; i < bones.Length; i++)
                    posed[i] = (bones[i].localPosition, bones[i].localRotation, bones[i].localScale);
            }
            finally
            {
                if (started)
                    AnimationMode.StopAnimationMode();
            }
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == animator.transform)
                    continue;   // 루트는 그대로(자리 이동 없음)
                bones[i].localPosition = posed[i].p;
                bones[i].localRotation = posed[i].r;
                bones[i].localScale = posed[i].s;
            }
            float moved = hand != null ? Vector3.Distance(handBefore, hand.position) : 0f;
            if (moved < 0.01f)
                Debug.LogWarning($"{Tag} {id}: Idle 자세가 적용되지 않았다(손 이동 {moved:0.000}m) — 바인드 포즈로 굽는다.");
            else
                Debug.Log($"{Tag} {id}: Idle 자세 적용(손 이동 {moved:0.00}m)");
        }

        // model 아래 켜진 렌더러(사람은 현재 자세로 구운 SkinnedMesh)를 root 공간 메시 하나로 합친다. 머티리얼마다 서브메시 하나.
        // 메시는 Skins/Meshes/{id}.asset (있으면 내용만 바꿔 GUID 유지).
        private static GameObject Merge(Transform root, GameObject model, string id)
        {
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var order = new List<Material>();
            var temp = new List<Mesh>();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                Mesh mesh;
                Matrix4x4 matrix;
                if (r is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null)
                        continue;
                    mesh = new Mesh();
                    skinned.BakeMesh(mesh, true);   // 현재 자세, 렌더러 스케일 포함(위치·회전만 행렬로)
                    temp.Add(mesh);
                    matrix = toRoot * Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                }
                else
                {
                    var filter = r.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                        continue;
                    mesh = filter.sharedMesh;
                    matrix = toRoot * r.transform.localToWorldMatrix;
                }
                Material[] mats = r.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material m = mats.Length == 0 ? null : mats[Mathf.Min(sub, mats.Length - 1)];
                    if (m == null)
                        continue;
                    if (!byMaterial.TryGetValue(m, out List<CombineInstance> list))
                    {
                        byMaterial[m] = list = new List<CombineInstance>();
                        order.Add(m);
                    }
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = matrix });
                }
            }

            // 머티리얼별로 먼저 합치고(서브메시 하나), 그것들을 서브메시를 나눈 채 다시 합친다
            var parts = new List<CombineInstance>();
            int vertices = 0;
            foreach (Material m in order)
            {
                var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                part.CombineMeshes(byMaterial[m].ToArray(), true, true);
                vertices += part.vertexCount;
                temp.Add(part);
                parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
            }
            var combined = new Mesh
            {
                name = id,
                indexFormat = vertices > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            combined.CombineMeshes(parts.ToArray(), false, false);
            combined.RecalculateBounds();
            // 자동 Mesh LOD — 화면에서 작아지면 단순한 단계로 (가까이는 원본 그대로, 그림자 패스도 같은 단계). 성능, 설계 §5.1
            MeshLodUtility.GenerateMeshLods(combined, AssetSwapCatalog.MaxMeshLods);
            foreach (Mesh m in temp)
                Object.DestroyImmediate(m);

            string meshPath = $"{AssetSwapCatalog.SkinMeshFolder}/{id}.asset";
            Written.Add(meshPath);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(combined, existing);
                existing.name = id;
                Object.DestroyImmediate(combined);
                combined = existing;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(combined, meshPath);
            }

            var go = new GameObject("Model");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = combined;
            go.AddComponent<MeshRenderer>().sharedMaterials = order.ToArray();
            return go;
        }

        // root 로컬 공간의 렌더러 경계 (root는 원점·단위 변환). within이 있으면 그 아래 렌더러만
        public static Bounds LocalBounds(Transform root, Transform within = null)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (Renderer r in (within != null ? within : root).GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = r is SkinnedMeshRenderer s && s.sharedMesh != null ? SkinnedWorldBounds(s) : r.bounds;
                Vector3 min = root.InverseTransformPoint(b.min), max = root.InverseTransformPoint(b.max);
                var local = new Bounds((min + max) * 0.5f, Vector3.zero);
                local.Encapsulate(min);
                local.Encapsulate(max);
                if (!any) { bounds = local; any = true; }
                else bounds.Encapsulate(local);
            }
            return bounds;
        }

        // 바인드 포즈 메시를 구워 월드 경계 (SkinnedMeshRenderer.bounds는 에디터 비실행 중 갱신이 늦을 수 있다)
        private static Bounds SkinnedWorldBounds(SkinnedMeshRenderer s)
        {
            var mesh = new Mesh();
            try
            {
                s.BakeMesh(mesh, true);
                Bounds local = mesh.bounds;
                Transform t = s.transform;
                var world = new Bounds(t.TransformPoint(local.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    world.Encapsulate(t.TransformPoint(corner));
                }
                return world;
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        // ---------- 머티리얼 (설계 §5) ----------

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Glossiness = Shader.PropertyToID("_Glossiness");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");

        private static Material Convert(Material source, PaletteToken token, PaletteSO palette,
            Dictionary<(Material, PaletteToken), Material> cache)
        {
            if (cache.TryGetValue((source, token), out Material done))
                return done;

            Color tokenColor = palette != null ? palette.Get(token) : Color.white;
            string shaderName = source.shader != null ? source.shader.name : "";
            // URP Lit·URP용 패키지 셰이더만 복사, 나머지(Built-in Standard, URP 대상이 없는 셰이더 그래프 등 — 분홍색)는 URP Lit으로 변환
            bool keep = shaderName.StartsWith("Universal Render Pipeline/") || shaderName.StartsWith("Ithappy/");
            Material made;
            if (!keep)
            {
                made = new Material(Shader.Find(PaletteMaterialMap.LitShader));
                Texture albedo = source.HasProperty(MainTex) ? source.GetTexture(MainTex) : null;
                if (albedo == null && source.HasProperty(BaseMap))
                    albedo = source.GetTexture(BaseMap);
                if (albedo == null)
                    albedo = FindTexture(source, "basecolor", "albedo", "diffuse", "_color", "_d.");
                Color color = source.HasProperty(ColorId) ? source.GetColor(ColorId) : Color.white;
                if (color.a <= 0f)
                    color = Color.white;   // 셰이더 그래프 등 _Color가 없는 경우
                made.SetTexture(BaseMap, albedo);
                made.SetColor(BaseColor, Tint(color, albedo != null, tokenColor));
                Texture normal = source.HasProperty(BumpMap) ? source.GetTexture(BumpMap) : null;
                if (normal == null)
                    normal = FindTexture(source, "normal", "_n.");
                if (normal != null)
                {
                    made.SetTexture(BumpMap, normal);
                    made.EnableKeyword("_NORMALMAP");
                }
                float gloss = source.HasProperty(Glossiness) ? source.GetFloat(Glossiness) : AssetSwapCatalog.DefaultSmoothness;
                made.SetFloat(Smoothness, Mathf.Min(gloss, AssetSwapCatalog.MaxSmoothness));
                made.SetFloat(Metallic, source.HasProperty(Metallic) ? source.GetFloat(Metallic) : 0f);
                // Built-in Fade/Transparent(유리·렌즈)는 URP에서도 알파 블렌드로
                if (source.renderQueue >= (int)RenderQueue.Transparent || (source.HasProperty("_Mode") && source.GetFloat("_Mode") >= 2f))
                    MakeTransparent(made);
            }
            else
            {
                // 이미 URP용(URP Lit·셰이더 그래프·패키지 전용 URP 셰이더): 사본 + 기본색만 틴트
                made = new Material(source);
                if (made.HasProperty(BaseColor))
                {
                    Texture albedo = made.HasProperty(BaseMap) ? made.GetTexture(BaseMap) : null;
                    made.SetColor(BaseColor, Tint(made.GetColor(BaseColor), albedo != null, tokenColor));
                }
                if (made.HasProperty(Smoothness))
                    made.SetFloat(Smoothness, Mathf.Min(made.GetFloat(Smoothness), AssetSwapCatalog.MaxSmoothness));
            }
            made.name = $"{source.name}_{token}";
            made.enableInstancing = true;

            // 경로 = 원본 이름 + 토큰 + 원본 에셋 GUID 앞 8자 + 파일 안 id 끝 4자리(fbx 내장 머티리얼 구분).
            // 다른 패키지의 같은 이름 머티리얼(Material·lambert1 등)이 서로 덮어쓰지 않게 한다 (리뷰 높음 3).
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string sourceGuid, out long sourceLocalId);
            string path = $"{AssetSwapCatalog.SkinMaterialFolder}/{Sanitize(made.name)}_{(sourceGuid ?? "").PadRight(8, '0').Substring(0, 8)}{(ulong)sourceLocalId % 0x10000:x4}.mat";
            Written.Add(path);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material result;
            if (existing != null)
            {
                existing.shader = made.shader;
                existing.CopyPropertiesFromMaterial(made);
                existing.shaderKeywords = made.shaderKeywords;
                existing.renderQueue = made.renderQueue;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(made);
                result = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(made, path);
                result = made;
            }
            cache[(source, token)] = result;
            return result;
        }

        // URP Lit 알파 블렌드 (M6ArtSetup 투명 팔레트 머티리얼과 같은 설정)
        private static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
        }

        // 텍스처 속성 중 파일 이름에 단서가 있는 것 (셰이더 그래프의 자동 이름 속성 등)
        private static Texture FindTexture(Material source, params string[] hints)
        {
            foreach (string hint in hints)
                foreach (string property in source.GetTexturePropertyNames())
                {
                    Texture t = source.GetTexture(property);
                    if (t == null)
                        continue;
                    string file = Path.GetFileName(AssetDatabase.GetAssetPath(t)).ToLowerInvariant();
                    if (file.Contains(hint))
                        return t;
                }
            return null;
        }

        // 텍스처가 있으면 곱하기 틴트(무늬 유지), 단색이면 팔레트 쪽으로 크게 당긴다(파란 컨테이너 등)
        private static Color Tint(Color source, bool textured, Color token) =>
            textured
                ? AssetSwapCatalog.Tinted(source, token)
                : new Color(Mathf.Lerp(source.r, token.r, AssetSwapCatalog.SolidTintStrength), Mathf.Lerp(source.g, token.g, AssetSwapCatalog.SolidTintStrength), Mathf.Lerp(source.b, token.b, AssetSwapCatalog.SolidTintStrength), source.a);

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace('(', '_').Replace(')', '_');
        }
    }
}
