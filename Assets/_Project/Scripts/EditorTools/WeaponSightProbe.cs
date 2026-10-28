using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashfall.EditorTools
{
    // 정조준 가늠자 기준점(SightAnchor)을 정하는 데 쓰는 스킨 형상 조사 (AdsBallistics 설계 §2).
    // VM 프리팹의 Skin(합친 메시, 부품 이름 없음)과 같은 자리에 원본 패키지 프리팹을 놓아 부품 이름별 정점을 VM 루트 로컬 좌표로 돌려준다.
    // 스킨 프리팹 구조(AssetSwapSkins.Build): SK 루트 → 원본(회전 Euler, 위치 0)을 합친 자식(localPosition = -앵커) — 그래서
    // 원본 → Skin 좌표 = T(합친 자식 localPosition) · R(Euler).
    public static class WeaponSightProbe
    {
        // 부품 하나의 정점 (VM 루트 로컬)
        public sealed class PartVertices
        {
            public readonly string Name;
            public readonly List<Vector3> Points = new List<Vector3>();        // VM 루트 로컬
            public readonly List<Vector3> SourcePoints = new List<Vector3>();  // 원본 프리팹 루트 로컬 (스킨 맞춤 배율과 무관)
            public Bounds Bounds;
            public Bounds SourceBounds;
            public Matrix4x4 SourceToRoot;

            public PartVertices(string name) => Name = name;
        }

        // VM 프리팹 내용(root)과 그 무기의 스킨 원본에서 부품별 정점을 모은다. 스킨이 없거나 원본 패키지가 없으면 null.
        // 원본 프리팹 루트 로컬 → VM 루트 로컬 행렬. VM에 정상 스킨(깨지지 않은 SK 인스턴스, 합친 자식 있음)이 없으면 false.
        // 원본 패키지는 필요 없다(스킨 프리팹 구조만 쓴다).
        public static bool TrySkinSourceToRoot(Transform vmRoot, string weaponId, out Matrix4x4 sourceToRoot)
        {
            sourceToRoot = Matrix4x4.identity;
            if (!AssetSwapCatalog.WeaponSkins.TryGetValue(weaponId, out string skinId))
                return false;
            AssetSwapCatalog.Source source = AssetSwapCatalog.Find(skinId);
            Transform skin = vmRoot.Find(AssetSwapCatalog.SkinChild);
            if (source == null || skin == null || !AssetSwapApply.IsSkinObject(skin.gameObject)
                || PrefabUtility.IsPrefabAssetMissing(skin.gameObject) || skin.childCount == 0)
                return false;
            Transform merged = skin.GetChild(0);
            if (merged.GetComponent<MeshFilter>() == null)
                return false;
            sourceToRoot = vmRoot.worldToLocalMatrix * skin.localToWorldMatrix
                           * Matrix4x4.Translate(merged.localPosition) * Matrix4x4.Rotate(Quaternion.Euler(source.Euler));
            return true;
        }

        public static List<PartVertices> CollectSkinParts(Transform vmRoot, string weaponId)
        {
            if (!TrySkinSourceToRoot(vmRoot, weaponId, out Matrix4x4 sourceToRoot))
                return null;
            AssetSwapCatalog.Source source = AssetSwapCatalog.Find(AssetSwapCatalog.WeaponSkins[weaponId]);
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(source.Path);
            if (original == null)
                return null;

            var parts = new List<PartVertices>();
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var temp = Object.Instantiate(original);
                SceneManager.MoveGameObjectToScene(temp, preview);
                temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                temp.transform.localScale = Vector3.one;
                Matrix4x4 worldToSource = temp.transform.worldToLocalMatrix;
                foreach (MeshFilter filter in temp.GetComponentsInChildren<MeshFilter>(true))
                {
                    var r = filter.GetComponent<Renderer>();
                    if (filter.sharedMesh == null || r == null || !r.enabled || !filter.gameObject.activeInHierarchy)
                        continue;
                    var part = new PartVertices(filter.gameObject.name) { SourceToRoot = sourceToRoot };
                    Matrix4x4 toSource = worldToSource * filter.transform.localToWorldMatrix;
                    Vector3[] vertices = ReadableVertices(filter.sharedMesh);
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 src = toSource.MultiplyPoint3x4(vertices[i]);
                        part.SourcePoints.Add(src);
                        part.Points.Add(sourceToRoot.MultiplyPoint3x4(src));
                    }
                    if (part.Points.Count == 0)
                        continue;
                    part.Bounds = new Bounds(part.Points[0], Vector3.zero);
                    part.SourceBounds = new Bounds(part.SourcePoints[0], Vector3.zero);
                    for (int i = 0; i < part.Points.Count; i++)
                    {
                        part.Bounds.Encapsulate(part.Points[i]);
                        part.SourceBounds.Encapsulate(part.SourcePoints[i]);
                    }
                    parts.Add(part);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
            return parts;
        }

        // 읽기 불가 메시(isReadable false)도 에디터에서는 정점을 읽을 수 있다(MeshData). 할당이 있어도 에디터 도구라 괜찮다.
        private static Vector3[] ReadableVertices(Mesh mesh)
        {
            if (mesh.isReadable)
                return mesh.vertices;
            using (Mesh.MeshDataArray data = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var buffer = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
                data[0].GetVertices(buffer);
                Vector3[] result = buffer.ToArray();
                buffer.Dispose();
                return result;
            }
        }

        // ---------- 조사 보고 (수동 보정표를 정할 때 쓴다) ----------

        // 배치모드: -executeMethod Ashfall.EditorTools.WeaponSightProbe.ReportAll
        [MenuItem("Ashfall/Setup/Weapons/Report Sight Geometry")]
        public static void ReportAll()
        {
            var sb = new StringBuilder();
            foreach (string weaponId in AssetSwapCatalog.WeaponSkins.Keys)
            {
                string path = M7Names.ViewModelPrefabPath(weaponId);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    sb.AppendLine($"=== {weaponId}: {path} 없음");
                    continue;
                }
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform root = contents.transform;
                    Transform muzzle = root.Find(M7Names.MuzzleChild);
                    sb.AppendLine($"=== {weaponId} muzzle {F(muzzle != null ? muzzle.localPosition : Vector3.zero)}");
                    Transform skin = root.Find(AssetSwapCatalog.SkinChild);
                    if (skin != null)
                    {
                        sb.AppendLine($"  skin pos {F(skin.localPosition)} scale {F(skin.localScale)}");
                        foreach (Renderer r in skin.GetComponentsInChildren<Renderer>())
                        {
                            Bounds b = r.bounds;
                            sb.AppendLine($"  merged world bounds min {F(root.InverseTransformPoint(b.min))} max {F(root.InverseTransformPoint(b.max))}");
                        }
                    }
                    List<PartVertices> parts = CollectSkinParts(root, weaponId);
                    if (parts == null)
                    {
                        sb.AppendLine("  (스킨 또는 원본 없음)");
                        continue;
                    }
                    foreach (PartVertices part in parts)
                    {
                        sb.AppendLine($"  part {part.Name} n={part.Points.Count} min {F(part.Bounds.min)} max {F(part.Bounds.max)}");
                        // 원본 좌표 앞뒤(z) 24칸마다 정점 y 최대와 그 높이(1mm 안) 정점의 x 범위 — 가늠자·가늠쇠 윤곽
                        Bounds src = part.SourceBounds;
                        sb.AppendLine($"     source min {F(src.min)} max {F(src.max)}");
                        const int slices = 24;
                        for (int s = 0; s < slices; s++)
                        {
                            float z0 = src.min.z + src.size.z * s / slices, z1 = src.min.z + src.size.z * (s + 1) / slices;
                            float hi = float.MinValue;
                            foreach (Vector3 p in part.SourcePoints)
                                if (p.z >= z0 && p.z <= z1)
                                    hi = Mathf.Max(hi, p.y);
                            if (hi == float.MinValue)
                                continue;
                            float xlo = float.MaxValue, xhi = float.MinValue;
                            foreach (Vector3 p in part.SourcePoints)
                                if (p.z >= z0 && p.z <= z1 && p.y >= hi - 0.001f)
                                {
                                    xlo = Mathf.Min(xlo, p.x);
                                    xhi = Mathf.Max(xhi, p.x);
                                }
                            Vector3 vm = part.SourceToRoot.MultiplyPoint3x4(new Vector3((xlo + xhi) * 0.5f, hi, (z0 + z1) * 0.5f));
                            sb.AppendLine($"     src z[{z0:0.0000},{z1:0.0000}] ymax {hi:0.0000} x {xlo:0.0000}..{xhi:0.0000} -> vm {F(vm)}");
                        }
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            Debug.Log("[WeaponSightProbe]\n" + sb);
        }

        // 한 부품의 정점(원본 좌표)을 높은 순으로 (중복 제거). 가늠쇠 끝·가늠자 구멍 높이를 고를 때 쓴다.
        // 배치모드: -executeMethod Ashfall.EditorTools.WeaponSightProbe.ReportVertices -ashfallSightQuery "WPN-RIF-01|AR_A_Barrel|0.49|0.56|0.004"
        //   (무기|부품|원본 z 최소|최대|원본 x 반폭) — 세미콜론으로 여러 개
        public static void ReportVertices()
        {
            var sb = new StringBuilder();
            foreach (string query in (ArgValue("-ashfallSightQuery") ?? "").Split(';'))
            {
                string[] q = query.Split('|');
                if (q.Length < 5)
                    continue;
                float zMin = float.Parse(q[2], CultureInfo.InvariantCulture), zMax = float.Parse(q[3], CultureInfo.InvariantCulture);
                float xHalf = float.Parse(q[4], CultureInfo.InvariantCulture);
                string vmPath = M7Names.ViewModelPrefabPath(q[0]);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(vmPath) == null)
                {
                    sb.AppendLine($"== {query}: {vmPath} 없음");
                    continue;
                }
                GameObject contents = PrefabUtility.LoadPrefabContents(vmPath);
                try
                {
                    List<PartVertices> parts = CollectSkinParts(contents.transform, q[0]);
                    PartVertices part = parts?.Find(p => p.Name == q[1]);
                    if (part == null)
                    {
                        sb.AppendLine($"== {query}: 부품 없음");
                        continue;
                    }
                    var seen = new HashSet<string>();
                    var list = new List<Vector3>();
                    foreach (Vector3 p in part.SourcePoints)
                        if (p.z >= zMin && p.z <= zMax && Mathf.Abs(p.x) <= xHalf && seen.Add(F(p)))
                            list.Add(p);
                    list.Sort((a, b) => b.y.CompareTo(a.y));
                    sb.AppendLine($"== {query}: {list.Count}개");
                    for (int i = 0; i < list.Count && i < 60; i++)
                        sb.AppendLine($"   src {F(list[i])} -> vm {F(part.SourceToRoot.MultiplyPoint3x4(list[i]))}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            Debug.Log("[WeaponSightProbe] 정점\n" + sb);
        }

        // ---------- 옆·뒤 직교 렌더 (눈으로 확인) ----------

        // 배치모드: -executeMethod Ashfall.EditorTools.WeaponSightProbe.RenderViews -ashfallSightOut <폴더>
        // VM 프리팹마다 옆(+x에서)·뒤(-z에서) 직교 PNG. 빨간 십자 = 현재 SightAnchor(있으면).
        public static void RenderViews()
        {
            string outDir = ArgValue("-ashfallSightOut");
            if (string.IsNullOrEmpty(outDir))
                throw new System.ArgumentException("-ashfallSightOut <폴더> 필요");
            System.IO.Directory.CreateDirectory(outDir);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("Light", typeof(Light));
            lightGo.GetComponent<Light>().type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(30f, -60f, 0f);
            var camGo = new GameObject("Cam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.85f, 0.85f, 0.85f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            var rt = new RenderTexture(1600, 800, 24);
            cam.targetTexture = rt;
            foreach (string weaponId in AssetSwapCatalog.WeaponSkins.Keys)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(M7Names.ViewModelPrefabPath(weaponId));
                if (prefab == null)
                    continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                // 블록아웃 숨김 (BlockoutSkin은 실행 중에만 숨긴다)
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                    if (!AssetSwapApply.UnderSkin(r.transform, go.transform))
                        r.enabled = false;
                Transform anchor = go.transform.Find(WeaponSightLayout.AnchorChild);
                GameObject marker = null;
                Material markerMaterial = null;
                if (anchor != null)
                {
                    marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    marker.transform.SetParent(anchor, false);
                    marker.transform.localScale = new Vector3(0.002f, 0.002f, 0.6f);
                    markerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    markerMaterial.color = Color.red;
                    marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                }
                Bounds b = new Bounds(go.transform.position, Vector3.zero);
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
                    if (r.enabled) b.Encapsulate(r.bounds);
                // 옆
                cam.transform.SetPositionAndRotation(b.center + Vector3.right * 2f, Quaternion.LookRotation(Vector3.left));
                cam.orthographicSize = b.extents.z * 0.55f;
                Save(cam, rt, System.IO.Path.Combine(outDir, weaponId + "_side.png"));
                // 윗부분 확대 (가늠자 쪽)
                cam.transform.SetPositionAndRotation(new Vector3(2f, b.max.y - 0.03f, b.center.z), Quaternion.LookRotation(Vector3.left));
                cam.orthographicSize = 0.06f;
                Save(cam, rt, System.IO.Path.Combine(outDir, weaponId + "_side_top.png"));
                // 뒤 (눈 쪽에서 앞으로)
                cam.transform.SetPositionAndRotation(new Vector3(0f, b.max.y - 0.03f, b.min.z - 1f), Quaternion.identity);
                cam.orthographicSize = 0.05f;
                Save(cam, rt, System.IO.Path.Combine(outDir, weaponId + "_rear.png"));
                Object.DestroyImmediate(go);
                if (markerMaterial != null)
                    Object.DestroyImmediate(markerMaterial);
            }
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log($"[WeaponSightProbe] 렌더 저장: {outDir}");
        }

        private static void Save(Camera cam, RenderTexture rt, string path)
        {
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        public static string ArgValue(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }

        private static string F(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.0000}, {1:0.0000}, {2:0.0000})", v.x, v.y, v.z);
    }
}
