using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Hideout;
using Ashfall.Player;
using Ashfall.UI;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // MVP 마무리 데이터 (docs/design/MVP_finish_design.md §1.7·§2). 새 SO 필드는 C# 초기값이 곧 기본값이라 기존 에셋에 필드가 없어도 동작한다 —
    // 여기서는 에셋 파일에 값을 명시 기록만 한다(M7 "명시 기록" 방식). **파일에 없는 필드만** 쓰고, 있으면 조정한 값으로 보고 보존한다.
    // 멱등: 바뀐 것이 있을 때만 SetDirty → 두 번째 실행은 파일·GUID를 바꾸지 않는다(바이트 동일).
    // 단계마다 자기 함수만 더한다: F2 ApplyHideoutConfig(stashUpgrades), F3 ApplyTraderAndHud(repairCostRatio·등급 경계),
    // F4 ApplyPlayerArmor(PlayerConfig 방어구 4필드)·ApplyEnemyArmor(AiConfig 같은 4필드 + AI-BOS·AI-BGD 방어구 참조).
    // 방어구 .asset 3개(ARM-HLM-01·ARM-BDY-01·GEAR-GSM-01)는 고치지 않는다(값이 이미 있다).
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

        // §2 TraderConfigSO.repairCostRatio (TODO(GDD-?) MF-Q13 임시값)
        public const float RepairCostRatio = 0.5f;
        // §2 HudConfigSO 내구도 등급 경계 (TODO(GDD-?) MF-Q10 임시값)
        public const float ConditionWornBelow = 0.5f;
        public const float ConditionCriticalBelow = 0.25f;

        // §2 방어구 (§5.3 0.5·0.25·0.3, 감소율 0.2 = TODO(GDD-?) MF-Q17). PlayerConfig·AiConfig에 같은 값(MF-Q16 B).
        public const float ArmorBasePenetrationChance = 0.5f;
        public const float ArmorPenetrationPerClassStep = 0.25f;
        public const float ArmorBluntDamageRatio = 0.3f;
        public const float ArmorDurabilityLossPerDamage = 0.2f;

        // 질문 결정 MF-Q16 B: AI-BOS = 헬멧 + 방탄복, AI-BGD = 방탄복(보스 거점). 나머지 유형은 방어구 없음. null 경로 = 그 슬롯 없음.
        public static readonly (string archetypePath, string helmetPath, string bodyArmorPath)[] EnemyArmor =
        {
            (M7Names.BossArchetypePath, M5DataSetup.HelmetPath, M5DataSetup.BodyArmorPath),
            (M7Names.BodyguardArchetypePath, null, M5DataSetup.BodyArmorPath),
        };

        [MenuItem("Ashfall/Setup/Apply MVP Finish Data")]
        public static void ApplyAll()
        {
            ApplyHideoutConfig();
            ApplyTraderAndHud();
            ApplyPlayerArmor();
            ApplyEnemyArmor();
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

        // ---------- F3 내구도 표시·수리 (§5.1, MF-Q10·Q13) ----------

        // TraderConfig.repairCostRatio, HudConfig.conditionWornBelow·conditionCriticalBelow — 파일에 없는 필드만
        public static void ApplyTraderAndHud()
        {
            string traderPath = M5DataSetup.TraderConfigPath;
            var trader = SetupUtil.Load<TraderConfigSO>(traderPath);
            if (trader != null && !SetupUtil.IsFieldSerialized(traderPath, nameof(TraderConfigSO.repairCostRatio)))
            {
                trader.repairCostRatio = RepairCostRatio;
                EditorUtility.SetDirty(trader);
                Debug.Log($"{Tag} TraderConfig.repairCostRatio 기록: {traderPath}");
            }

            string hudPath = M2DataSetup.HudConfigPath;
            var hud = SetupUtil.Load<HudConfigSO>(hudPath);
            if (hud == null)
                return;
            bool changed = false;
            if (!SetupUtil.IsFieldSerialized(hudPath, nameof(HudConfigSO.conditionWornBelow)))
            {
                hud.conditionWornBelow = ConditionWornBelow;
                changed = true;
            }
            if (!SetupUtil.IsFieldSerialized(hudPath, nameof(HudConfigSO.conditionCriticalBelow)))
            {
                hud.conditionCriticalBelow = ConditionCriticalBelow;
                changed = true;
            }
            if (changed)
            {
                EditorUtility.SetDirty(hud);
                Debug.Log($"{Tag} HudConfig 내구도 등급 경계 기록: {hudPath}");
            }
        }

        // ---------- F4 방어구 (§5.3, MF-Q16 B·Q17·Q22) ----------

        // PlayerConfig 방어구 4필드 — 파일에 없는 필드만
        public static void ApplyPlayerArmor()
        {
            string path = M2DataSetup.PlayerConfigPath;
            var config = SetupUtil.Load<PlayerConfigSO>(path);
            if (config == null)
                return;
            bool changed = false;
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.armorBasePenetrationChance), () => config.armorBasePenetrationChance = ArmorBasePenetrationChance);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.armorPenetrationPerClassStep), () => config.armorPenetrationPerClassStep = ArmorPenetrationPerClassStep);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.armorBluntDamageRatio), () => config.armorBluntDamageRatio = ArmorBluntDamageRatio);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.armorDurabilityLossPerDamage), () => config.armorDurabilityLossPerDamage = ArmorDurabilityLossPerDamage);
            if (changed)
            {
                EditorUtility.SetDirty(config);
                Debug.Log($"{Tag} PlayerConfig 방어구 수치 기록: {path}");
            }
        }

        // AiConfig 같은 4필드 + AI-BOS(헬멧·방탄복)·AI-BGD(방탄복) 참조 — 파일에 없는 필드만.
        // 새로 만든 BOS·BGD 에셋(필드가 null로 이미 기록됨)은 M7AiDataSetup 생성 람다가 같은 값을 넣는다.
        public static void ApplyEnemyArmor()
        {
            string aiPath = M3DataSetup.AiConfigPath;
            var ai = SetupUtil.Load<AiConfigSO>(aiPath);
            if (ai != null)
            {
                bool changed = false;
                changed |= WriteIfMissing(aiPath, nameof(AiConfigSO.armorBasePenetrationChance), () => ai.armorBasePenetrationChance = ArmorBasePenetrationChance);
                changed |= WriteIfMissing(aiPath, nameof(AiConfigSO.armorPenetrationPerClassStep), () => ai.armorPenetrationPerClassStep = ArmorPenetrationPerClassStep);
                changed |= WriteIfMissing(aiPath, nameof(AiConfigSO.armorBluntDamageRatio), () => ai.armorBluntDamageRatio = ArmorBluntDamageRatio);
                changed |= WriteIfMissing(aiPath, nameof(AiConfigSO.armorDurabilityLossPerDamage), () => ai.armorDurabilityLossPerDamage = ArmorDurabilityLossPerDamage);
                if (changed)
                {
                    EditorUtility.SetDirty(ai);
                    Debug.Log($"{Tag} AiConfig 방어구 수치 기록: {aiPath}");
                }
            }

            foreach ((string archetypePath, string helmetPath, string bodyArmorPath) in EnemyArmor)
            {
                if (AssetDatabase.LoadAssetAtPath<EnemyArchetypeSO>(archetypePath) == null)
                    continue;   // M7 AI 데이터 없음 — Create M7 AI Data가 만들면서 넣는다
                var archetype = SetupUtil.Load<EnemyArchetypeSO>(archetypePath);
                bool changed = false;
                changed |= WriteIfMissing(archetypePath, nameof(EnemyArchetypeSO.helmet), () => archetype.helmet = LoadArmor(helmetPath));
                changed |= WriteIfMissing(archetypePath, nameof(EnemyArchetypeSO.bodyArmor), () => archetype.bodyArmor = LoadArmor(bodyArmorPath));
                if (changed)
                {
                    EditorUtility.SetDirty(archetype);
                    Debug.Log($"{Tag} {archetype.id} 방어구 기록: 헬멧 {(archetype.helmet != null ? archetype.helmet.id : "없음")}, " +
                              $"방탄복 {(archetype.bodyArmor != null ? archetype.bodyArmor.id : "없음")}");
                }
            }
        }

        // M7AiDataSetup이 BOS·BGD를 새로 만들 때 쓴다 (EnemyArmor 표와 같은 값)
        public static void AssignEnemyArmor(EnemyArchetypeSO archetype, string archetypePath)
        {
            foreach ((string path, string helmetPath, string bodyArmorPath) in EnemyArmor)
            {
                if (path != archetypePath)
                    continue;
                archetype.helmet = LoadArmor(helmetPath);
                archetype.bodyArmor = LoadArmor(bodyArmorPath);
            }
        }

        private static ArmorDefinitionSO LoadArmor(string path) =>
            string.IsNullOrEmpty(path) ? null : SetupUtil.Load<ArmorDefinitionSO>(path);

        // 파일에 필드가 없을 때만 write를 부르고 true
        private static bool WriteIfMissing(string assetPath, string fieldName, System.Action write)
        {
            if (SetupUtil.IsFieldSerialized(assetPath, fieldName))
                return false;
            write();
            return true;
        }
    }
}
