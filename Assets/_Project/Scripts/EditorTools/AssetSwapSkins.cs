using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Source = Ashfall.EditorTools.AssetSwapCatalog.Source;

namespace Ashfall.EditorTools
{
    // 스킨 프리팹·머티리얼·Idle 컨트롤러 생성 (AssetSwap 설계 §3·§5). 출력 = Assets/_Project/Skins/ (git 제외, 매번 다시 씀 — GUID 유지).
    // 스킨 = 원본 복제(프리팹 연결 끊음) → 콜라이더·스크립트·Rigidbody 제거 → 머티리얼을 팔레트 틴트 사본으로 → 앵커가 원점에 오도록 감싼다.
    // 원본이 없으면 그 스킨은 만들지 않고, 예전에 만든 그 스킨 프리팹도 지운다(적용 쪽이 그 스킨을 씬에서 뺀다).
    // 이번 실행에 쓰지 않은 머티리얼 사본도 지운다. 원본 패키지가 하나도 없으면 아무것도 만들지 않는다.
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
            Written.Clear();
            var materialCache = new Dictionary<(Material, PaletteToken), Material>();
            RuntimeAnimatorController idle = null;
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
                    idle = EnsureIdleController();
                GameObject prefab = Build(source, original, palette, materialCache, idle);
                if (prefab != null)
                    result[source.Id] = prefab;
            }
            // 이번에 쓰지 않은 머티리얼 사본(원본이 빠졌거나 이름 규칙이 바뀐 것) 정리
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { AssetSwapCatalog.SkinMaterialFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Written.Contains(path))
                    AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        private static GameObject Build(Source source, GameObject original, PaletteSO palette,
            Dictionary<(Material, PaletteToken), Material> materialCache, RuntimeAnimatorController idle)
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
                Strip(model, source.Character);

                bool weapon = source.Anchor == AssetSwapCatalog.Anchor.FrontTip;
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = weapon ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null)
                            mats[i] = Convert(mats[i], source.Tint, palette, materialCache);
                    r.sharedMaterials = mats;
                    if (r is SkinnedMeshRenderer skinned)
                        skinned.updateWhenOffscreen = false;
                }

                if (source.Character)
                {
                    Animator animator = model.GetComponentInChildren<Animator>(true);
                    if (animator == null)
                        animator = model.AddComponent<Animator>();
                    if (animator.avatar == null || !animator.avatar.isHuman)
                        Debug.LogWarning($"{Tag} {source.Id}: 휴머노이드 아바타가 없어 Idle 클립이 재생되지 않는다(바인드 포즈로 선다).");
                    animator.runtimeAnimatorController = idle;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.CullCompletely;
                }
                else if (!weapon)
                {
                    foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
                }

                // 앵커를 원점으로 (경계는 바인드 포즈 기준 — 사람은 Idle에서 거의 같다)
                Bounds bounds = LocalBounds(root.transform);
                model.transform.localPosition = -AssetSwapCatalog.AnchorPoint(bounds, source.Anchor);

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

        // 렌더러·메시·Animator·LODGroup만 남긴다 (게임 판정·스크립트가 섞이지 않게)
        private static void Strip(GameObject model, bool character)
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
            if (!character)
                foreach (Animator a in model.GetComponentsInChildren<Animator>(true))
                    Object.DestroyImmediate(a);
        }

        // root 로컬 공간의 렌더러 경계 (root는 원점·단위 변환)
        public static Bounds LocalBounds(Transform root)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
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

        // ---------- Idle 컨트롤러 ----------

        private static RuntimeAnimatorController EnsureIdleController()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetSwapCatalog.IdleClipPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetSwapCatalog.IdleControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(AssetSwapCatalog.IdleControllerPath);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            AnimatorState state = sm.defaultState;
            if (state == null)
            {
                state = sm.AddState("Idle");
                sm.defaultState = state;
            }
            if (state.motion != clip)
            {
                state.motion = clip;
                EditorUtility.SetDirty(controller);
            }
            if (clip == null)
                Debug.LogWarning($"{Tag} Idle 클립 {AssetSwapCatalog.IdleClipPath} 없음 — 사람 스킨이 바인드 포즈로 선다.");
            return controller;
        }
    }
}
