using System.IO;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.UI;
using Ashfall.Weapons;
using UnityEditor;
using UnityEngine;
using BodyPart = Ashfall.Core.BodyPart;

namespace Ashfall.EditorTools
{
    // M2 데이터 에셋 생성 (M2 설계 §4, §5.6).
    // - §4.5 아이템 10종 (의약품 3, 조끼·배낭, 헬멧·방탄복, 잡동사니 3)
    // - 설정 SO 3개 (WeaponHandlingConfig, InventoryConfig, HudConfig)
    // - 기존 PlayerConfig.asset에 §4.2/§4.3 필드 기록
    // 멱등: 이미 있는 에셋은 덮어쓰지 않는다(조정한 값 보존). 여러 번 실행해도 결과가 같다.
    // M1 에셋(WPN-RIF-01, MAG-556-30, AMMO-556)은 만들기만 하고(없을 때) 값은 건드리지 않는다.
    // 배치모드: -executeMethod Ashfall.EditorTools.M2DataSetup.CreateAll
    public static class M2DataSetup
    {
        private const string Tag = "[M2DataSetup]";

        public const string DataFolder = "Assets/_Project/Data";
        public const string PlayerConfigPath = DataFolder + "/Player/PlayerConfig.asset";
        public const string WeaponHandlingConfigPath = DataFolder + "/Weapons/WeaponHandlingConfig.asset";
        public const string InventoryConfigPath = DataFolder + "/Inventory/InventoryConfig.asset";
        public const string HudConfigPath = DataFolder + "/UI/HudConfig.asset";

        public const string MedicalFolder = DataFolder + "/Items/Medical";
        public const string GearFolder = DataFolder + "/Items/Gear";
        public const string ArmorFolder = DataFolder + "/Items/Armor";
        public const string JunkFolder = DataFolder + "/Items/Junk";

        [MenuItem("Ashfall/Setup/Create M2 Data")]
        public static void CreateData()
        {
            // PlayerConfig 등 M1 데이터가 먼저 있어야 한다. M1Setup도 이미 있는 에셋은 건너뛴다.
            M1Setup.CreateData();

            FillPlayerConfigM2();
            CreateConfigs();
            CreateMedical();
            CreateGear();
            CreateArmor();
            CreateJunk();
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료");
        }

        // 배치모드용 진입점. CreateData와 같다.
        public static void CreateAll() => CreateData();

        // ---------- PlayerConfig §4.2 / §4.3 ----------

        // 기존 PlayerConfig.asset은 M2 필드가 YAML에 없어도 로드하면 필드 초기값(= 설계값)으로 보인다(1단계 메모 5).
        // 그래서 "값이 0이면 채우기"는 동작하지 않는다. 파일에 headMaxHp가 아직 기록되지 않았을 때만 값을 넣고
        // SetDirty → SaveAssets로 파일에 기록한다. 이미 기록돼 있으면(조정했을 수 있음) 건드리지 않는다.
        private static void FillPlayerConfigM2()
        {
            var c = AssetDatabase.LoadAssetAtPath<PlayerConfigSO>(PlayerConfigPath);
            if (c == null)
            {
                Debug.LogError($"{Tag} PlayerConfig가 없다: {PlayerConfigPath}");
                return;
            }

            if (IsFieldSerialized(PlayerConfigPath, "headMaxHp"))
                return;

            // §4.2 부위 체력
            c.headMaxHp = 35f; c.thoraxMaxHp = 80f; c.stomachMaxHp = 70f; c.armMaxHp = 60f; c.legMaxHp = 65f;
            c.destroyedPartRedistribution = 0.7f;      // 파괴 부위 추가 피해 70% 분산
            c.stomachDestroyedDamageMultiplier = 1.5f; // 복부 0 → 이후 피해 1.5배
            c.armDestroyedAimSwayAdd = 1.0f;           // 팔 0 → 조준 흔들림 +100%
            c.legDestroyedSpeedMultiplier = 0.6f;      // 다리 0 → 이동 -40%, 질주 불가

            // §4.3 출혈
            c.bleedHpPerSec = 1f; c.heavyBleedHpPerSec = 3f;

            // TODO(GDD-?): 아래는 GDD에 없는 임시값 (M2 설계 §4.1).
            c.bleedMinDamage = 10f; c.heavyBleedChance = 0.15f; c.bleedChance = 0.35f;
            c.fractureMinDamage = 20f; c.fractureChance = 0.2f;
            c.fractureLegSpeedMultiplier = 0.8f; c.fractureArmAimSwayAdd = 0.5f;
            c.tierInjuredBelow = 0.75f; c.tierCriticalBelow = 0.35f;
            c.headZoneFrom = 0.87f; c.thoraxZoneFrom = 0.62f; c.stomachZoneFrom = 0.45f; c.armZoneSideRatio = 0.6f;

            EditorUtility.SetDirty(c);
            Debug.Log($"{Tag} PlayerConfig에 §4.2/§4.3 필드 기록: {PlayerConfigPath}");
        }

        // ---------- 설정 SO ----------

        private static void CreateConfigs()
        {
            CreateAssetIfMissing<WeaponHandlingConfigSO>(WeaponHandlingConfigPath, h =>
            {
                h.magCheckTimeSec = 0.8f; // RULE-04
                // TODO(GDD-?): 구간 경계·조준 흔들림은 GDD에 없는 임시값 (M2 설계 §4.2).
                h.magCheckFullAtOrAbove = 0.67f; h.magCheckHalfAtOrAbove = 0.34f;
                h.aimSwayBaseDeg = 0.15f; h.aimSwayFrequencyHz = 0.5f;
            });

            CreateAssetIfMissing<InventoryConfigSO>(InventoryConfigPath, i =>
            {
                i.pocketCount = 4; i.pocketSize = Vector2Int.one; // §6 주머니 1×1×4
                i.searchTimePerItemSec = 0.5f;                     // §6 아이템 1개당 0.5초
                // TODO(GDD-?): 아래는 GDD에 없는 임시값 (M2 설계 §4.3).
                i.interactRangeM = 2.0f;
                i.reloadFromBackpack = false;
                i.dropForwardM = 0.6f; i.dropSpeed = 1.5f;
                i.worldItemCellM = 0.1f;
            });

            CreateAssetIfMissing<HudConfigSO>(HudConfigPath, h =>
            {
                h.magCheckResultSec = 2.0f; // TODO(GDD-?): 결과 문구 표시 시간 임시값 (M2 설계 §4.4)
            });
        }

        // ---------- 아이템 (M2 설계 §4.5) ----------
        // TODO(GDD-?): 아이템 ID·크기·무게·사용 시간·등급·내구도는 전부 GDD에 없는 임시값.
        // TODO(GDD-?): 희귀도·기본 가격도 없다. 희귀도는 기본값(Scrap), 가격은 0으로 둔다.

        private static void CreateMedical()
        {
            CreateMedical("MED-BND-01", "붕대", new Vector2Int(1, 1), 0.05f, StatusEffect.Bleed, 3f);      // §4.3 출혈 해제
            CreateMedical("MED-TRQ-01", "지혈대", new Vector2Int(1, 1), 0.1f, StatusEffect.HeavyBleed, 4f); // §4.3 중출혈 해제
            CreateMedical("MED-SPL-01", "부목", new Vector2Int(1, 2), 0.2f, StatusEffect.Fracture, 5f);    // §4.3 골절 해제
        }

        private static void CreateMedical(string id, string name, Vector2Int size, float kg, StatusEffect cure, float useTime)
        {
            CreateAssetIfMissing<MedicalDefinitionSO>($"{MedicalFolder}/{id}.asset", m =>
            {
                m.id = id; m.displayName = name; m.size = size; m.weightKg = kg;
                m.cures = new[] { cure }; m.healAmount = 0f; m.useTime = useTime; m.uses = 1;
            });
        }

        private static void CreateGear()
        {
            // 조끼 그리드 4×2 = 1×2 탄창 4개
            CreateContainer("RIG-01", "전술조끼", ContainerKind.TacticalVest, new Vector2Int(2, 2), 1.2f, new Vector2Int(4, 2));
            CreateContainer("BAG-01", "배낭", ContainerKind.Backpack, new Vector2Int(3, 3), 1.5f, new Vector2Int(4, 5));
        }

        private static void CreateContainer(string id, string name, ContainerKind kind, Vector2Int size, float kg, Vector2Int grid)
        {
            CreateAssetIfMissing<ContainerDefinitionSO>($"{GearFolder}/{id}.asset", c =>
            {
                c.id = id; c.displayName = name; c.size = size; c.weightKg = kg;
                c.kind = kind; c.gridSize = grid;
            });
        }

        private static void CreateArmor()
        {
            CreateArmor("ARM-HLM-01", "헬멧", ArmorKind.Helmet, new Vector2Int(2, 2), 1.4f, 40f, BodyPart.Head);
            CreateArmor("ARM-BDY-01", "방탄복", ArmorKind.BodyArmor, new Vector2Int(3, 3), 8.0f, 60f, BodyPart.Thorax, BodyPart.Stomach);
        }

        private static void CreateArmor(string id, string name, ArmorKind kind, Vector2Int size, float kg, float durability,
            params BodyPart[] covers)
        {
            CreateAssetIfMissing<ArmorDefinitionSO>($"{ArmorFolder}/{id}.asset", a =>
            {
                a.id = id; a.displayName = name; a.size = size; a.weightKg = kg;
                a.armorClass = 3; // §5.3 범위(1~5) 안의 임시값
                a.maxDurability = durability; a.covers = covers; a.kind = kind;
            });
        }

        private static void CreateJunk()
        {
            CreateItem("JNK-01", "녹슨 파이프", new Vector2Int(1, 3), 1.0f);
            CreateItem("JNK-02", "엔진 부품", new Vector2Int(3, 2), 20.0f); // 무게 패널티(§4.1) 확인용
            CreateItem("JNK-03", "전선 뭉치", new Vector2Int(1, 1), 0.3f);
        }

        private static void CreateItem(string id, string name, Vector2Int size, float kg)
        {
            CreateAssetIfMissing<ItemDefinitionSO>($"{JunkFolder}/{id}.asset", i =>
            {
                i.id = id; i.displayName = name; i.size = size; i.weightKg = kg; i.rarity = Rarity.Scrap;
            });
        }

        // ---------- 도우미 ----------

        private static T CreateAssetIfMissing<T>(string path, System.Action<T> fill) where T : ScriptableObject
            => SetupUtil.CreateAssetIfMissing(path, fill, Tag);

        // 에셋 파일(텍스트 직렬화)에 필드가 실제로 기록돼 있는지. 읽기만 한다.
        private static bool IsFieldSerialized(string assetPath, string fieldName)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return false;
            foreach (string line in File.ReadLines(fullPath))
            {
                if (line.TrimStart().StartsWith(fieldName + ":"))
                    return true;
            }
            return false;
        }
    }
}
