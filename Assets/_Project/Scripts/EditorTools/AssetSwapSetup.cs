using System;
using System.Collections.Generic;
using System.Text;
using Ashfall.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashfall.EditorTools
{
    // 에셋 교체 메뉴 (AssetSwap 설계 §6). 에셋스토어 패키지(git 제외)로 스킨 프리팹을 만들고 무기 뷰모델·레이드 씬 블록아웃 위에 얹는다.
    // 멱등: 다시 실행하면 같은 스킨을 재사용하고 위치만 맞춘다. 패키지가 없으면 스킨을 만들지 않고 깨진 스킨만 지운다(블록아웃 모습).
    // 레이드 씬을 M4·M7·맵 개선 메뉴로 다시 만들었으면 이 메뉴를 다시 실행한다.
    // 배치모드: -executeMethod Ashfall.EditorTools.AssetSwapSetup.ApplyAll  /  RemoveAll
    public static class AssetSwapSetup
    {
        private const string Tag = "[AssetSwap]";

        [MenuItem("Ashfall/Setup/Apply Asset Swap")]
        public static void ApplyAll()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var palette = AssetDatabase.LoadAssetAtPath<PaletteSO>(M6Names.PalettePath);
            if (palette == null)
                throw new InvalidOperationException($"{Tag} {M6Names.PalettePath} 없음 — 먼저 Ashfall/Setup/Apply M6 Art & Audio");

            AssetSwapApply.ClearCache();
            var missing = new List<string>();
            Dictionary<string, GameObject> skins = AssetSwapSkins.BuildAll(palette, missing);
            if (missing.Count > 0)
                Debug.LogWarning($"{Tag} 원본 {missing.Count}개 없음(그 스킨은 건너뜀 — 블록아웃 유지):\n  " + string.Join("\n  ", missing));

            // 레이드 씬 먼저: 적용 → 검사 → 통과해야 저장. 그다음 무기 뷰모델(프리팹마다 검사 후 저장).
            var result = new AssetSwapApply.Result();
            Scene scene = EditorSceneManager.OpenScene(M4SceneSetup.RaidScenePath, OpenSceneMode.Single);
            bool changed = AssetSwapApply.ApplyRaid(scene, skins, result);

            var report = new StringBuilder();
            bool ok = result.Problems.Count == 0;
            foreach (string problem in result.Problems)
                report.AppendLine("  " + problem);
            foreach (GameObject root in scene.GetRootGameObjects())
                ok &= AssetSwapApply.Validate(root.transform, report);
            if (!ok)
                throw new InvalidOperationException($"{Tag} 확인 실패 — 씬 저장 안 함:\n{report}");
            if (changed)
                EditorSceneManager.SaveScene(scene);

            AssetSwapApply.ApplyWeapons(skins, result);
            if (result.Problems.Count > 0)
                throw new InvalidOperationException($"{Tag} 무기 뷰모델 확인 실패(그 프리팹은 저장 안 함):\n  " + string.Join("\n  ", result.Problems));
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료: 스킨 {skins.Count}/{AssetSwapCatalog.Sources.Count}종, {result}{(result.Changed ? "" : " (바뀐 것 없음)")}");
        }

        [MenuItem("Ashfall/Setup/Asset Swap/Remove Skins")]
        public static void RemoveAll()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var result = new AssetSwapApply.Result();
            foreach (string weaponId in AssetSwapCatalog.WeaponSkins.Keys)
            {
                string path = M7Names.ViewModelPrefabPath(weaponId);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    continue;
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (AssetSwapApply.RemoveSkins(contents.transform, result))
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            Scene scene = EditorSceneManager.OpenScene(M4SceneSetup.RaidScenePath, OpenSceneMode.Single);
            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
                changed |= AssetSwapApply.RemoveSkins(root.transform, result);
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 스킨 {result.Removed}개 제거 (블록아웃 모습)");
        }
    }
}
