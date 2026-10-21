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

        // 에셋을 경로로 불러온다. 없으면 에러 로그 후 null (씬 생성이 빈 참조로 조용히 진행되지 않게).
        public static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                Debug.LogError($"[Setup] 에셋이 없다: {path} ({typeof(T).Name})");
            return asset;
        }
    }
}
