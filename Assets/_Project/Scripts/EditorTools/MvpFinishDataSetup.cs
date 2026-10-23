using Ashfall.Hideout;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // MVP 마무리 데이터 (docs/design/MVP_finish_design.md §1.7·§2). 새 SO 필드는 C# 초기값이 곧 기본값이라 기존 에셋에 필드가 없어도 동작한다 —
    // 여기서는 에셋 파일에 값을 명시 기록만 한다(M7 "명시 기록" 방식). **파일에 없는 필드만** 쓰고, 있으면 조정한 값으로 보고 보존한다.
    // 멱등: 바뀐 것이 있을 때만 SetDirty → 두 번째 실행은 파일·GUID를 바꾸지 않는다(바이트 동일).
    // 단계마다 자기 함수만 더한다: F2 ApplyHideoutConfig(stashUpgrades). (F3·F4가 TraderConfig·HudConfig·PlayerConfig 함수를 추가)
    // M7SceneSetup.ApplyAll 끝에서도 부른다(M4·M5 씬 메뉴·Apply M7 Content를 다시 돌려도 빠지지 않게).
    // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.MvpFinishDataSetup.ApplyAll -logFile setup.log
    public static class MvpFinishDataSetup
    {
        private const string Tag = "[MvpFinishDataSetup]";

        // §2 HideoutConfigSO.stashUpgrades (TODO(GDD-?) MF-Q5 임시값): 10×40 5000 탄피, 10×50 12000 탄피
        public static readonly StashUpgradeStep[] StashUpgrades =
        {
            new StashUpgradeStep(40, 5000),
            new StashUpgradeStep(50, 12000),
        };

        [MenuItem("Ashfall/Setup/Apply MVP Finish Data")]
        public static void ApplyAll()
        {
            ApplyHideoutConfig();
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료");
        }

        // ---------- F2 보관함 업그레이드 (§9, MF-Q5) ----------

        public static void ApplyHideoutConfig()
        {
            string path = M5DataSetup.HideoutConfigPath;
            var config = SetupUtil.Load<HideoutConfigSO>(path);
            if (config == null)
                return;   // M5 데이터 없음(Create M5 Data 먼저) — Load가 오류를 남긴다

            if (SetupUtil.IsFieldSerialized(path, nameof(HideoutConfigSO.stashUpgrades)))
                return;
            config.stashUpgrades = (StashUpgradeStep[])StashUpgrades.Clone();
            EditorUtility.SetDirty(config);
            Debug.Log($"{Tag} HideoutConfig.stashUpgrades 기록: {path}");
        }
    }
}
