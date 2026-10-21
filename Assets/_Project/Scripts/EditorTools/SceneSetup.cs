using Ashfall.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // M0: Boot·MainMenu 씬을 만들고 빌드 설정에 Boot → MainMenu 순서로 등록한다.
    // 이미 있는 씬은 덮어쓰지 않는다. 배치모드: -executeMethod Ashfall.EditorTools.SceneSetup.CreateM0Scenes
    public static class SceneSetup
    {
        private const string SceneFolder = "Assets/_Project/Scenes";

        private static string PathOf(string sceneName) => $"{SceneFolder}/{sceneName}.unity";

        [MenuItem("Ashfall/Setup/Create M0 Scenes (Boot, MainMenu)")]
        public static void CreateM0Scenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            CreateIfMissing(SceneNames.MainMenu, NewSceneSetup.DefaultGameObjects, null);
            CreateIfMissing(SceneNames.Boot, NewSceneSetup.EmptyScene,
                () => new GameObject(nameof(BootLoader)).AddComponent<BootLoader>());

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(PathOf(SceneNames.Boot), true),
                new EditorBuildSettingsScene(PathOf(SceneNames.MainMenu), true),
            };
            AssetDatabase.SaveAssets();

            EditorSceneManager.OpenScene(PathOf(SceneNames.Boot));
            Debug.Log("[SceneSetup] Boot, MainMenu 씬 준비 완료. 빌드 설정: Boot → MainMenu");
        }

        private static void CreateIfMissing(string sceneName, NewSceneSetup setup, System.Action populate)
        {
            string path = PathOf(sceneName);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
            {
                Debug.Log($"[SceneSetup] 이미 있음, 건너뜀: {path}");
                return;
            }

            var scene = EditorSceneManager.NewScene(setup, NewSceneMode.Single);
            populate?.Invoke();
            EditorSceneManager.SaveScene(scene, path);
        }
    }
}
