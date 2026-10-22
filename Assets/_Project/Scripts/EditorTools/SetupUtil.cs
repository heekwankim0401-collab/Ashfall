using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // 셋업 메뉴(M1Setup, M2DataSetup, M2SceneSetup) 공용 도우미 (M2 설계 §5.6 SetupUtil).
    // M1Setup에 있던 CreateAssetIfMissing·EnsureFolder·SetFields를 옮겼다. 동작은 같다.
    public static class SetupUtil
    {
        // path에 에셋이 없을 때만 만들고 fill로 값을 넣는다. 이미 있으면 그대로 돌려준다(조정한 값 보존).
        public static T CreateAssetIfMissing<T>(string path, System.Action<T> fill, string logTag = "[Setup]")
            where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
                return existing;

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var asset = ScriptableObject.CreateInstance<T>();
            fill(asset);
            AssetDatabase.CreateAsset(asset, path);
            Debug.Log($"{logTag} 에셋 생성: {path}");
            return asset;
        }

        // "Assets/a/b/c" 폴더를 위에서부터 차례로 만든다.
        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // private [SerializeField] 오브젝트 참조 필드를 이름으로 채운다. 없는 필드면 에러 로그.
        public static void SetFields(Object target, params (string name, Object value)[] fields)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in fields)
            {
                SerializedProperty property = so.FindProperty(name);
                if (property == null)
                {
                    Debug.LogError($"[Setup] {target.GetType().Name}에 직렬화 필드 '{name}'이 없다.", target);
                    continue;
                }
                property.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 에셋 파일(텍스트 직렬화)에 필드가 실제로 기록돼 있는지. 읽기만 한다.
        // 새 필드는 기존 에셋을 로드하면 초기값으로 보이지만 파일에는 SetDirty → SaveAssets 전까지 없다(M2 1단계 메모 5).
        // M2DataSetup에 같은 private 함수가 있다(M3에서 공용으로 추가, 동작 같음).
        public static bool IsFieldSerialized(string assetPath, string fieldName)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return false;
            foreach (string line in File.ReadLines(fullPath))
            {
                if (line.TrimStart().StartsWith(fieldName + ":"))
                    return true;
            }
            return false;
        }

        // 빌드 설정 (M5 설계 §4.9): Boot → MainMenu → Hideout(있으면) → Raid_CrimsonBasin(있으면).
        // M4·M5 씬 메뉴가 모두 이것을 불러, 어느 메뉴를 다시 돌려도 다른 씬이 빠지지 않는다.
        // Boot·MainMenu가 없으면 예외(M0 씬 메뉴를 먼저 실행해야 한다).
        public static void ApplyBuildScenes()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            foreach (string path in new[] { M4SceneSetup.BootScenePath, M4SceneSetup.MainMenuScenePath })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                    throw new System.InvalidOperationException($"[Setup] 씬이 없다: {path}. 먼저 Ashfall/Setup/Create M0 Scenes.");
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            foreach (string path in new[] { M5SceneSetup.HideoutScenePath, M4SceneSetup.RaidScenePath })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                else
                    Debug.LogWarning($"[Setup] 빌드 설정에서 뺀다(씬 없음): {path}");
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // 로그용: 현재 빌드 설정의 활성 씬 이름 "A → B → C"
        public static string DescribeBuildScenes()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.enabled)
                    names.Add(Path.GetFileNameWithoutExtension(s.path));
            return string.Join(" → ", names);
        }

        // 에셋을 경로로 불러온다. 없으면 에러 로그 후 null (씬 생성이 빈 참조로 조용히 진행되지 않게).
        public static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                Debug.LogError($"[Setup] 에셋이 없다: {path} ({typeof(T).Name})");
            return asset;
        }

        // ---------- 씬 오브젝트 (M6 리뷰 낮음 9: M6 씬 적용 3파일의 같은 도우미를 모았다. 동작은 같다) ----------

        // 씬 최상위에서 이름으로 찾는다. 없으면 null.
        public static GameObject FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name)
                    return root;
            return null;
        }

        // 씬 최상위에서 이름으로 찾고, 없으면 그 씬에 만든다.
        public static GameObject FindOrCreateRoot(UnityEngine.SceneManagement.Scene scene, string name)
        {
            GameObject go = FindRoot(scene, name);
            if (go != null)
                return go;
            go = new GameObject(name);
            if (go.scene != scene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        // 컴포넌트가 있으면 그것, 없으면 추가. 에디터의 GetComponent는 없을 때 가짜 null을 돌려줄 수 있어 ?? 대신 == null로 본다.
        public static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }
    }
}
