using System;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Raid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Ashfall.EditorTools
{
    // M6 통합 메뉴 (M6 설계 §1.6 ⟨Q1⟩, ART·AUD·SYS-MAP 재 폭풍).
    // ApplyAll: 데이터(1b) → 아트 에셋(2a) → 오디오 에셋(2b) → 게임 씬 3개를 열어(새로 만들지 않음) 적용 → 같은 경로에 저장.
    //   레이드: 비주얼(2a) → 재 폭풍(2c, AshParticles·Sun을 이름으로 찾으므로 비주얼 다음) → 오디오(2b).
    //   은신처: 카메라 배경(2a) → 음악(2b). 메인 메뉴: 카메라 배경만(음악 없음, §11).
    // 멱등: 각 Apply가 "이름으로 찾아 있으면 다시 설정, 없으면 생성"이라 두 번째 실행에서 씬·에셋이 바뀌지 않는다.
    // M4·M5 씬 생성 메뉴는 저장 직전 Apply*IfAssetsExist를 불러 M6 구성이 빠지지 않게 한다(에셋이 없으면 조용히 건너뜀).
    // 확인 씬(M1~M3)은 건드리지 않는다(Q9). 빌드 설정 변경 없음.
    // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.M6SceneSetup.ApplyAll -logFile setup.log
    public static class M6SceneSetup
    {
        private const string Tag = "[M6SceneSetup]";

        [MenuItem("Ashfall/Setup/Apply M6 Art & Audio")]
        public static void ApplyAll()
        {
            M6DataSetup.CreateData();
            if (!M6ArtSetup.CreateAssets())
                throw new InvalidOperationException($"{Tag} 아트 에셋 생성 실패 — 위 오류 로그 확인");
            M6AudioSetup.CreateAssets();
            AssetDatabase.SaveAssets();

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            ApplyToSceneFile(M4SceneSetup.RaidScenePath, ApplyRaid);
            ApplyToSceneFile(M5SceneSetup.HideoutScenePath, ApplyHideout);
            ApplyToSceneFile(M4SceneSetup.MainMenuScenePath, ApplyMainMenu);
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료");
        }

        // ---------- 씬별 적용 (저장은 호출자) ----------

        public static bool ApplyRaid(Scene scene)
        {
            if (!M6VisualSceneApply.ApplyRaid(scene))
                return false;
            if (M6StormSceneApply.Apply(scene) == null)
                return false;
            return M6AudioSceneApply.ApplyRaid(scene);
        }

        public static bool ApplyHideout(Scene scene)
        {
            if (!M6VisualSceneApply.ApplyMenuScene(scene))
                return false;
            return M6AudioSceneApply.ApplyHideout(scene);
        }

        public static bool ApplyMainMenu(Scene scene) => M6VisualSceneApply.ApplyMenuScene(scene);

        // ---------- M4·M5 메뉴용 (에셋이 없으면 경고 없이 건너뜀 — M4·M5만 쓰는 상황 호환) ----------

        // 에셋이 있는데 적용이 실패하면 예외 — M4·M5 메뉴가 반쯤 적용된 씬을 저장하지 않고, 배치 종료 코드로 드러난다(리뷰 낮음 6).
        public static void ApplyRaidIfAssetsExist(Scene scene)
        {
            if (RaidAssetsExist())
                Require(ApplyRaid(scene), scene);
        }

        public static void ApplyHideoutIfAssetsExist(Scene scene)
        {
            if (MenuAssetsExist() && Exists<AudioConfigSO>(M6Names.AudioConfigPath))
                Require(ApplyHideout(scene), scene);
        }

        public static void ApplyMainMenuIfAssetsExist(Scene scene)
        {
            if (MenuAssetsExist())
                Require(ApplyMainMenu(scene), scene);
        }

        private static void Require(bool applied, Scene scene)
        {
            if (!applied)
                throw new InvalidOperationException($"{Tag} M6 적용 실패: {scene.name} — 씬을 저장하지 않는다(위 오류 로그 확인)");
        }

        // 레이드 적용에 필요한 데이터·파생 에셋이 모두 있는가 (M6 메뉴를 한 번이라도 돌렸는가)
        public static bool RaidAssetsExist() =>
            MenuAssetsExist()
            && Exists<AtmosphereConfigSO>(M6Names.AtmosphereConfigPath)
            && Exists<AshStormConfigSO>(M6Names.AshStormConfigPath)
            && Exists<AudioConfigSO>(M6Names.AudioConfigPath)
            && Exists<VolumeProfile>(M6Names.RaidVolumeProfilePath)
            && Exists<Material>(M6Names.SkyMaterialPath)
            && Exists<Material>(M6Names.AshParticleMaterialPath)
            && Exists<TerrainLayer>(M6Names.TerrainLayerPath);

        private static bool MenuAssetsExist() => Exists<PaletteSO>(M6Names.PalettePath);

        private static bool Exists<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path) != null;

        private static void ApplyToSceneFile(string path, Func<Scene, bool> apply)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                Debug.LogWarning($"{Tag} 씬 없음(건너뜀): {path} — 먼저 M4·M5 씬 메뉴");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            if (!apply(scene))
                throw new InvalidOperationException($"{Tag} 적용 실패: {path} (위 오류 로그 확인)");
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException($"{Tag} 씬 저장 실패: {path}");
            Debug.Log($"{Tag} 적용·저장: {path}");
        }
    }
}
