using System;
using Ashfall.Core;
using Ashfall.Raid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashfall.EditorTools
{
    // 맵 개선 통합 메뉴 (docs/design/MapPolish_design.md §5). M7SceneSetup과 같은 방식.
    // 순서: M7 에셋이 없으면 M7 ApplyAll(M6 포함) → 밝기 개정·새 필드 기록(ApplyAtmosphere) → M6 아트 파생물(볼륨 노출·하늘·발광 머티리얼)
    //   → 랜드마크·소품 프리팹(없을 때만) → 레이드 씬을 열어 M6 비주얼(밝기) → 맵 개선 배치·광원·NavMesh → 바뀌었으면 저장.
    // 멱등: 개정 번호·"파일에 없는 필드만"·"그룹이 없을 때만"이라 두 번째 실행은 파일을 바꾸지 않는다(씬은 같은 내용으로 다시 저장될 수 있다).
    // 실패는 예외 — 반쯤 적용된 씬을 저장하지 않고 배치 종료 코드로 드러난다.
    // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.MapPolishSetup.ApplyAll -logFile setup.log
    public static class MapPolishSetup
    {
        private const string Tag = "[MapPolishSetup]";

        // 밝기 개정 1 (§2, TODO(GDD-?) — §10에 수치 없음. 미리보기 렌더 3안 중 고른 값, 설계 §8 구현 메모). AtmosphereConfigSO 필드 초기값과 같다(테스트가 확인).
        public const int LightingRevision = 1;
        public const float SunLightIntensity = 2.4f;
        public const float SunLightBoneMix = 0.4f;
        public const float SunElevationDeg = 12f;
        public const float AmbientIntensity = 3.5f;
        public const PaletteToken AmbientGroundToken = PaletteToken.DriedBlood;
        public const float PostExposureEv = 0.8f;
        public const float ClearVisibilityM = 600f;

        [MenuItem("Ashfall/Setup/Apply Map Polish")]
        public static void ApplyAll() => Run(rebuild: false);

        // 랜드마크·소품 프리팹을 같은 경로의 모델로 바꾼 뒤 등: 맵 개선 그룹을 지우고 다시 배치·확인·굽기 (리뷰 M4, 설계 §4.1 교체 절차).
        [MenuItem("Ashfall/Setup/Map Polish/Rebuild Map Polish (Raid Scene)")]
        public static void RebuildAll() => Run(rebuild: true);

        private static void Run(bool rebuild)
        {
            if (!M7SceneSetup.RaidAssetsExist())
            {
                Debug.Log($"{Tag} M7 에셋 없음 — Apply M7 Content 먼저 실행");
                M7SceneSetup.ApplyAll();
            }

            // 에셋(밝기 개정·볼륨·발광 머티리얼·프리팹)은 씬보다 먼저 저장된다 — 씬 단계가 실패해도 다시 실행하면 씬이 따라온다(리뷰 L8).
            ApplyAtmosphere();
            AssetDatabase.SaveAssets();
            if (!M6ArtSetup.CreateAssets())
                throw new InvalidOperationException($"{Tag} M6 아트 파생물 생성 실패 — 위 오류 로그 확인");
            if (!MapPolishPrefabs.CreatePrefabs())
                throw new InvalidOperationException($"{Tag} 프리팹 생성 실패 — 위 오류 로그 확인");
            AssetDatabase.SaveAssets();

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string path = M4SceneSetup.RaidScenePath;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                throw new InvalidOperationException($"{Tag} 레이드 씬 없음: {path} — 먼저 Create M4 Raid Scenes");
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            if (!M6VisualSceneApply.ApplyRaid(scene))
                throw new InvalidOperationException($"{Tag} M6 비주얼 적용 실패: {path}");
            if (rebuild)
                MapPolishSceneApply.RemoveGroups(scene);
            if (!MapPolishSceneApply.ApplyRaid(scene))
                throw new InvalidOperationException($"{Tag} 맵 개선 적용 실패: {path}");
            if (scene.isDirty && !EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException($"{Tag} 씬 저장 실패: {path}");
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료{(rebuild ? "(다시 만들기)" : "")}: {path}");
        }

        // AtmosphereConfig: 새 필드(점광원 모양·상한)는 파일에 없을 때만 명시 기록, 밝기 값은 개정 번호가 낮을 때만 덮어쓴다(조정값 보존).
        public static void ApplyAtmosphere()
        {
            string path = M6Names.AtmosphereConfigPath;
            var cfg = SetupUtil.Load<AtmosphereConfigSO>(path);
            if (cfg == null)
                throw new InvalidOperationException($"{Tag} {path} 없음 — 먼저 Create M6 Data");

            bool dirty = false;
            // 새 필드: C# 초기값이 곧 기본값 — 파일에 명시만 한다
            foreach (string field in new[] { nameof(AtmosphereConfigSO.fireLight), nameof(AtmosphereConfigSO.lampLight), nameof(AtmosphereConfigSO.maxMapLights) })
                dirty |= !SetupUtil.IsFieldSerialized(path, field);

            if (cfg.lightingRevision < LightingRevision)
            {
                WriteLightingRevision(cfg);
                dirty = true;
                Debug.Log($"{Tag} AtmosphereConfig 밝기 개정 {LightingRevision} 기록: 태양 {SunLightIntensity}(Bone {SunLightBoneMix}, 고도 {SunElevationDeg}°), " +
                          $"환경광 ×{AmbientIntensity}(지면 {AmbientGroundToken}), 노출 +{PostExposureEv}EV, 맑을 때 가시거리 {ClearVisibilityM}m");
            }
            if (!dirty)
                return;
            EditorUtility.SetDirty(cfg);
        }

        // 밝기 개정 1 값 (기존 에셋 이전용 — 새 에셋은 SO 필드 초기값이 같은 값이고, M6 데이터 메뉴도 이것을 부른다)
        internal static void WriteLightingRevision(AtmosphereConfigSO cfg)
        {
            cfg.sunLightIntensity = SunLightIntensity;
            cfg.sunLightBoneMix = SunLightBoneMix;
            cfg.sunElevationDeg = SunElevationDeg;
            cfg.ambientIntensity = AmbientIntensity;
            cfg.ambientGroundToken = AmbientGroundToken;
            cfg.postExposureEv = PostExposureEv;
            cfg.clearVisibilityM = ClearVisibilityM;
            cfg.lightingRevision = LightingRevision;
        }
    }
}
