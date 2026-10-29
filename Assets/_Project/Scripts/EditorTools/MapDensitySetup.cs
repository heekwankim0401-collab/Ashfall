using System;
using Ashfall.Raid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashfall.EditorTools
{
    // 맵 밀도 메뉴 (docs/design/MapDensity_design.md §6.3·§6.4). MapPolishSetup과 같은 방식.
    // 순서: 맵 개선이 없으면 MapPolishSetup.ApplyAll → 데이터(MapDensityConfig, 없을 때만) → M6 아트 파생물(새 팔레트 머티리얼)
    //   → 산포 메시·프리팹(없을 때만) → 레이드 씬 열기 → MapDensitySceneApply.ApplyRaid → 바뀌었으면 저장.
    // 멱등: "없을 때만"·"그룹이 없을 때만"·"값이 다를 때만"이라 두 번째 실행은 파일을 바꾸지 않는다.
    // 실패는 예외 — 반쯤 적용된 씬을 저장하지 않고 배치 종료 코드로 드러난다.
    // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.MapDensitySetup.ApplyAll -logFile setup.log
    //   (RebuildAll / RefreshAll / CreateData 도 같은 방식)
    // ③ 상자·트렁크·루트(LootSpawnConfig·LT-CRATE-LOW·LT-TRUNK)는 다음 단계 — 이 메뉴는 아직 만들지 않는다.
    public static class MapDensitySetup
    {
        private const string Tag = "[MapDensitySetup]";

        [MenuItem("Ashfall/Setup/Apply Map Density")]
        public static void ApplyAll() => Run(rebuild: false);

        // 산포 프리팹·목표 수·배치 데이터를 바꾼 뒤: 그룹을 지우고 다시 배치·확인·굽기
        [MenuItem("Ashfall/Setup/Map Density/Rebuild Map Density (Raid Scene)")]
        public static void RebuildAll() => Run(rebuild: true);

        // SO 조정용: 풀·LOD 거리·그림자만 (NavMesh·배치는 그대로)
        [MenuItem("Ashfall/Setup/Map Density/Refresh Grass & Culling")]
        public static void RefreshAll()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<MapDensityConfigSO>(MapDensitySceneApply.ConfigPath);
            if (cfg == null)
                throw new InvalidOperationException($"{Tag} {MapDensitySceneApply.ConfigPath} 없음 — 먼저 Ashfall/Setup/Apply Map Density");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Scene scene = OpenRaidScene();
            int changed = MapDensitySceneApply.Refresh(scene, cfg, out string report);
            SaveIfDirty(scene);
            Debug.Log($"{Tag} 풀·컬링 갱신 {changed}\n{report}");
        }

        // MapDensityConfig (없을 때만 — 기획 조정 보존). 값 = SO 필드 초기값(설계 §5.1).
        [MenuItem("Ashfall/Setup/Create Map Density Data")]
        public static void CreateData()
        {
            SetupUtil.CreateAssetIfMissing<MapDensityConfigSO>(MapDensitySceneApply.ConfigPath, _ => { }, Tag);
            AssetDatabase.SaveAssets();
        }

        private static void Run(bool rebuild)
        {
            if (!MapPolishSceneApply.AssetsExist())
            {
                Debug.Log($"{Tag} 맵 개선 에셋 없음 — Apply Map Polish 먼저 실행");
                MapPolishSetup.ApplyAll();
            }
            CreateData();
            if (!M6ArtSetup.CreateAssets())
                throw new InvalidOperationException($"{Tag} M6 아트 파생물(팔레트 머티리얼) 생성 실패 — 위 오류 로그 확인");
            if (!MapDensityPrefabs.CreatePrefabs())
                throw new InvalidOperationException($"{Tag} 산포 프리팹 생성 실패 — 위 오류 로그 확인");
            AssetDatabase.SaveAssets();

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Scene scene = OpenRaidScene();
            if (!HasMapPolishGroups(scene))
            {
                Debug.Log($"{Tag} 레이드 씬에 맵 개선 그룹 없음 — Apply Map Polish 먼저 실행");
                MapPolishSetup.ApplyAll();
                scene = OpenRaidScene();
            }
            if (rebuild)
                MapDensitySceneApply.RemoveGroups(scene);
            if (!MapDensitySceneApply.ApplyRaid(scene))
                throw new InvalidOperationException($"{Tag} 맵 밀도 적용 실패: {scene.path}");
            SaveIfDirty(scene);
            Debug.Log($"{Tag} 완료{(rebuild ? "(다시 만들기)" : "")}: {scene.path} — 패키지가 있는 PC면 Ashfall/Setup/Apply Asset Swap을 이어서 실행(새 건물·차량·벽 스킨)");
        }

        private static Scene OpenRaidScene()
        {
            string path = M4SceneSetup.RaidScenePath;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                throw new InvalidOperationException($"{Tag} 레이드 씬 없음: {path} — 먼저 Create M4 Raid Scenes");
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static bool HasMapPolishGroups(Scene scene)
        {
            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            return environment != null && environment.transform.Find(MapPolishLayout.StructureGroup) != null
                                       && environment.transform.Find(MapPolishLayout.LandmarkGroup) != null;
        }

        private static void SaveIfDirty(Scene scene)
        {
            if (scene.isDirty && !EditorSceneManager.SaveScene(scene, scene.path))
                throw new InvalidOperationException($"{Tag} 씬 저장 실패: {scene.path}");
            AssetDatabase.SaveAssets();
        }
    }
}
