using System;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Hideout;
using Ashfall.Raid;
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
    //   7B(AI): 유형·루트 데이터 → 레이드 씬 적 배치. 7C(생존·제작): 생존 데이터·레시피·RAD-REF → 레이드 씬 방사능 구역·Toxic 표시,
    //   은신처 씬 HideoutController.crafting 연결(ApplyHideout).
    // 멱등: 각 단계가 "없을 때만 생성·빈 필드만 채움"이라 두 번째 실행에서 파일이 바뀌지 않는다. 씬은 적용으로 바뀐 경우에만 저장한다.
    // 실패는 예외(M6 리뷰 6번) — 반쯤 적용된 씬을 저장하지 않고 배치 종료 코드로 드러난다.
    // M4 레이드 씬 메뉴는 저장 직전 ApplyRaidIfAssetsExist를, M5 은신처 씬 메뉴는 ApplyHideoutIfAssetsExist를 부른다(M6 호출 다음)
    //   — 씬을 다시 만들어도 M7 구성이 빠지지 않는다.
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
            ApplyToSceneFile(M5SceneSetup.HideoutScenePath, ApplyHideout);
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
            // 7C 생존·제작(SYS-STAT·SYS-HUB): 물·음식·해독제·방독면, 레시피, 생존 수치, RAD-REF, 희귀도, 상인·루트 추가.
            //   위 CreateData 연쇄가 이미 M1~M7B를 돌렸으므로 7C 몫만 부른다(= M7SurvivalDataSetup.CreateData의 뒷부분, 결과 같음).
            M7SurvivalDataSetup.WriteM7SurvivalData();
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
            // 7C: 방사능 구역 RadiationField·Toxic 표시 54개 (실패는 예외, M_Toxic 머티리얼도 여기서)
            if (!M7HazardSceneApply.ApplyRaid(scene))
                return false;
            return true;
        }

        // 7C 은신처: HideoutController.crafting ← CraftingConfig (SYS-HUB 제작대). 바뀐 경우에만 씬을 더럽힌다.
        // 에셋은 씬을 연 뒤(이 함수 안)에 읽는다 — OpenScene(Single)보다 먼저 읽으면 언로드로 참조가 끊겨 {fileID: 0}으로 저장된다(7C-2b 메모).
        public static bool ApplyHideout(Scene scene)
        {
            if (!scene.IsValid())
                return false;
            var crafting = AssetDatabase.LoadAssetAtPath<CraftingConfigSO>(M7Names.CraftingConfigPath);
            if (crafting == null)
                throw new InvalidOperationException($"{Tag} CraftingConfig 없음: {M7Names.CraftingConfigPath} — Create M7 Survival Data 먼저");

            HideoutController controller = null;
            int found = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (HideoutController c in root.GetComponentsInChildren<HideoutController>(true))
                {
                    controller = c;
                    found++;
                }
            if (found != 1)
                throw new InvalidOperationException($"{Tag} {scene.name}: HideoutController가 {found}개 — 하나여야 한다(M5 은신처 씬 메뉴 확인)");

            var so = new SerializedObject(controller);
            SerializedProperty prop = so.FindProperty("crafting");
            if (prop == null)
                throw new InvalidOperationException($"{Tag} HideoutController에 직렬화 필드 crafting이 없다");
            if (prop.objectReferenceValue == crafting)
            {
                Debug.Log($"{Tag} {scene.name}: 제작 설정 연결 그대로 — 변경 없음");
                return true;
            }
            prop.objectReferenceValue = crafting;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{Tag} {scene.name}: HideoutController.crafting ← {M7Names.CraftingConfigPath}");
            return true;
        }

        // ---------- M4 메뉴용 (에셋이 없으면 경고 없이 건너뜀 — M4~M6만 쓰는 상황 호환) ----------

        // 에셋이 있는데 적용이 실패하면 예외 — M4·M5 메뉴가 반쯤 적용된 씬을 저장하지 않는다.
        public static void ApplyRaidIfAssetsExist(Scene scene)
        {
            if (RaidAssetsExist())
                Require(ApplyRaid(scene), scene);
        }

        public static void ApplyHideoutIfAssetsExist(Scene scene)
        {
            if (HideoutAssetsExist())
                Require(ApplyHideout(scene), scene);
        }

        // 은신처 적용에 필요한 M7 데이터 (7C 제작 설정)
        public static bool HideoutAssetsExist() => Exists<CraftingConfigSO>(M7Names.CraftingConfigPath);

        // 레이드 적용에 필요한 M7 데이터가 있는가 (M7 메뉴를 한 번이라도 돌렸는가). 하위 단계가 자기 조건을 && 로 더한다.
        public static bool RaidAssetsExist() =>
            Exists<WeaponDefinitionSO>(M7Names.ShotgunPath)
            && Exists<WeaponDefinitionSO>(M7Names.DmrPath)
            && Exists<EnemyArchetypeSO>(M7Names.SniperArchetypePath)
            && Exists<EnemyArchetypeSO>(M7Names.MutantArchetypePath)
            && Exists<EnemyArchetypeSO>(M7Names.BossArchetypePath)
            && Exists<EnemyArchetypeSO>(M7Names.BodyguardArchetypePath)
            // 7C: 생존 아이템·제작 설정·RaidConfig의 RAD-REF (구역이 없으면 방사능 적용이 예외를 낸다)
            && Exists<MedicalDefinitionSO>(M7Names.AntiRadPath)
            && Exists<ArmorDefinitionSO>(M7Names.GasMaskPath)
            && HideoutAssetsExist()
            && M7HazardSceneApply.TryFindZone(AssetDatabase.LoadAssetAtPath<RaidConfigSO>(M7Names.RaidConfigPath),
                M7Names.RefineryZoneId, out _);

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
