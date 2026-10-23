using System;
using Ashfall.AI;
using Ashfall.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashfall.EditorTools
{
    // M7 통합 메뉴 (M7 설계 §1.7, SYS-WPN·SYS-AI·SYS-SRV). M6SceneSetup과 같은 방식.
    // ApplyAll: (M6 에셋이 없으면 M6 ApplyAll 먼저 — 팔레트 머티리얼·AudioConfig) → 데이터·자리표시 에셋(CreateAssets)
    //   → 레이드 씬을 열어(새로 만들지 않음) 적용(ApplyRaid) → 바뀌었으면 같은 경로에 저장.
    // 하위 단계는 자기 줄만 더한다: 에셋은 CreateAssets, 레이드 씬은 ApplyRaid, 레이드 적용 조건은 RaidAssetsExist.
    //   7A(무기): 데이터 → 뷰모델 프리팹 → 오디오 WAV·AudioConfig. 씬 연결 없음(컨트롤러가 무기 정의 viewModel·handling 탄약 표를 런타임에 읽는다).
    //   7B(AI)·7C(생존·제작)가 이 아래에 한 줄씩 더한다. 은신처 씬 적용이 필요하면 7C가 ApplyHideout을 같은 모양으로 더한다.
    // 멱등: 각 단계가 "없을 때만 생성·빈 필드만 채움"이라 두 번째 실행에서 파일이 바뀌지 않는다. 씬은 적용으로 바뀐 경우에만 저장한다.
    // 실패는 예외(M6 리뷰 6번) — 반쯤 적용된 씬을 저장하지 않고 배치 종료 코드로 드러난다.
    // M4 레이드 씬 메뉴는 저장 직전 ApplyRaidIfAssetsExist를 부른다(M6 호출 다음) — 씬을 다시 만들어도 M7 구성이 빠지지 않는다.
    // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.M7SceneSetup.ApplyAll -logFile setup.log
    public static class M7SceneSetup
    {
        private const string Tag = "[M7SceneSetup]";

        [MenuItem("Ashfall/Setup/Apply M7 Content")]
        public static void ApplyAll()
        {
            // 뷰모델 프리팹은 M6 팔레트 머티리얼을, 오디오는 M6 AudioConfig를 쓴다 — 없으면 M6 전체를 먼저(씬 적용 포함, 멱등).
            if (!M6SceneSetup.RaidAssetsExist())
            {
                Debug.Log($"{Tag} M6 에셋 없음 — Apply M6 Art & Audio 먼저 실행");
                M6SceneSetup.ApplyAll();
            }

            CreateAssets();

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            ApplyToSceneFile(M4SceneSetup.RaidScenePath, ApplyRaid);
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료");
        }

        // ---------- 데이터·자리표시 에셋 (씬 밖) ----------

        public static void CreateAssets()
        {
            // 7A 무기: 데이터(M1~M6 데이터 포함) → 뷰모델 프리팹(무기 정의 viewModel 빈 칸 채움) → 새 WAV·AudioConfig 세트·클릭 클립
            M7WeaponDataSetup.CreateData();
            if (!M7WeaponAssets.CreateViewModels())
                throw new InvalidOperationException($"{Tag} 무기 뷰모델 생성 실패 — 위 오류 로그 확인");
            M6AudioSetup.CreateAssets();

            // 7B AI: 유형 4개·루트 표·유물(SYS-AI). LMG 세트·울음 WAV는 위 M6AudioSetup이 만든다.
            M7AiDataSetup.CreateData();
            // 7C 생존·제작: (7C가 여기 한 줄)
            AssetDatabase.SaveAssets();
        }

        // ---------- 씬별 적용 (저장은 호출자) ----------

        public static bool ApplyRaid(Scene scene)
        {
            if (!scene.IsValid())
                return false;
            // 7A: 씬 적용 없음 (위 주석)
            // 7B: 적 14명·순찰 경로·몸 머티리얼·저격수 섬광 (실패는 예외)
            if (!M7EnemySceneApply.ApplyRaid(scene))
                return false;
            // 7C: if (!M7HazardSceneApply.ApplyRaid(scene)) return false;
            return true;
        }

        // ---------- M4 메뉴용 (에셋이 없으면 경고 없이 건너뜀 — M4~M6만 쓰는 상황 호환) ----------

        // 에셋이 있는데 적용이 실패하면 예외 — M4 메뉴가 반쯤 적용된 씬을 저장하지 않는다.
        public static void ApplyRaidIfAssetsExist(Scene scene)
        {
            if (RaidAssetsExist())
                Require(ApplyRaid(scene), scene);
        }

        // 레이드 적용에 필요한 M7 데이터가 있는가 (M7 메뉴를 한 번이라도 돌렸는가). 하위 단계가 자기 조건을 && 로 더한다.
        public static bool RaidAssetsExist() =>
            Exists<WeaponDefinitionSO>(M7Names.ShotgunPath)
            && Exists<WeaponDefinitionSO>(M7Names.DmrPath)
            && Exists<EnemyArchetypeSO>(M7Names.SniperArchetypePath)
            && Exists<EnemyArchetypeSO>(M7Names.MutantArchetypePath)
            && Exists<EnemyArchetypeSO>(M7Names.BossArchetypePath)
            && Exists<EnemyArchetypeSO>(M7Names.BodyguardArchetypePath);

        private static void Require(bool applied, Scene scene)
        {
            if (!applied)
                throw new InvalidOperationException($"{Tag} M7 적용 실패: {scene.name} — 씬을 저장하지 않는다(위 오류 로그 확인)");
        }

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
            // 적용이 아무것도 바꾸지 않았으면 저장하지 않는다 (재실행 바이트 동일)
            if (!scene.isDirty)
            {
                Debug.Log($"{Tag} 변경 없음: {path}");
                return;
            }
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException($"{Tag} 씬 저장 실패: {path}");
            Debug.Log($"{Tag} 적용·저장: {path}");
        }
    }
}
