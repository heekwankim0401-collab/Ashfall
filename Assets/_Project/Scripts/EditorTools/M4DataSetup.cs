using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Raid;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // M4 데이터 에셋 생성 (M4 설계 §4.6 "2c").
    // - RaidConfig: RULE-02 1500초, §8.2 EXT-01~04(소요·조건) + 반경 임시값, 스폰별 활성 탈출구(Q2·Q3)
    // - 아이템: ITM-FLR-01 신호탄(Q6·Q7, EXT-03 소모), ITM-MAP-01 종이 지도(Q11, RULE-06)
    // - 상자 루트 테이블 LT-CRATE (Q17, 신호탄 낮은 가중치 = Q7-A)
    // - HudConfig.mapItem = 지도 (Q11, 필드가 있고 비어 있을 때만)
    // 멱등: 이미 있는 에셋은 덮어쓰지 않는다(조정한 값 보존). 여러 번 실행해도 결과가 같다.
    // 배치모드: -executeMethod Ashfall.EditorTools.M4DataSetup.CreateAll
    public static class M4DataSetup
    {
        private const string Tag = "[M4DataSetup]";

        public const string DataFolder = M2DataSetup.DataFolder;
        public const string RaidFolder = DataFolder + "/Raid";
        public const string RaidItemsFolder = DataFolder + "/Items/Raid";

        public const string RaidConfigPath = RaidFolder + "/RaidConfig.asset";
        public const string FlarePath = RaidItemsFolder + "/ITM-FLR-01.asset";
        public const string MapItemPath = RaidItemsFolder + "/ITM-MAP-01.asset";
        public const string LootCratePath = M3DataSetup.LootFolder + "/LT-CRATE.asset";

        // 탈출·스폰·스위치 id (CrimsonBasinLayout·씬 컴포넌트 id와 같아야 한다)
        public const string Ext01 = "EXT-01";
        public const string Ext02 = "EXT-02";
        public const string Ext03 = "EXT-03";
        public const string Ext04 = "EXT-04";
        public const string SpawnWest = "SP-W";
        public const string SpawnEast = "SP-E";
        public const string SpawnNorth = "SP-N";

        // HudConfigSO.mapItem (2b가 추가하는 필드). 이름으로만 쓴다 — UI 코드와 컴파일 의존 없음.
        private const string HudMapItemField = "mapItem";

        // 상자 테이블이 참조하는 기존 아이템
        private const string BandagePath = M2DataSetup.MedicalFolder + "/MED-BND-01.asset";
        private const string TourniquetPath = M2DataSetup.MedicalFolder + "/MED-TRQ-01.asset";
        private const string SplintPath = M2DataSetup.MedicalFolder + "/MED-SPL-01.asset";
        private const string PipePath = M2DataSetup.JunkFolder + "/JNK-01.asset";
        private const string WiresPath = M2DataSetup.JunkFolder + "/JNK-03.asset";
        private const string HelmetPath = M2DataSetup.ArmorFolder + "/ARM-HLM-01.asset";

        [MenuItem("Ashfall/Setup/Create M4 Data")]
        public static void CreateData()
        {
            // M1~M3 데이터(소총·탄창·의약품·잡동사니·HudConfig 등)가 먼저 있어야 한다. 이미 있는 에셋은 건너뛴다.
            M3DataSetup.CreateData();

            CreateItems();
            CreateRaidConfig();
            CreateCrateLootTable();
            AssignHudMapItem();
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료");
        }

        // 배치모드용 진입점. CreateData와 같다.
        public static void CreateAll() => CreateData();

        // ---------- 아이템 (Q7 신호탄, Q11 지도) ----------
        // TODO(GDD-?): 두 아이템 모두 GDD 아이템 표에 없다. id·이름·크기·무게·희귀도·가격은 M4 설계 임시값.

        private static void CreateItems()
        {
            SetupUtil.CreateAssetIfMissing<ItemDefinitionSO>(FlarePath, i =>
            {
                i.id = "ITM-FLR-01"; i.displayName = "신호탄";        // §8.2 EXT-03 "신호탄 소모"
                i.size = new Vector2Int(1, 2); i.weightKg = 0.3f;    // TODO(GDD-?) Q7
                i.rarity = Rarity.Common; i.basePrice = 0;           // TODO(GDD-?) 가격은 M5 상인에서
            }, Tag);

            SetupUtil.CreateAssetIfMissing<ItemDefinitionSO>(MapItemPath, i =>
            {
                i.id = "ITM-MAP-01"; i.displayName = "종이 지도";     // RULE-06 종이 지도 아이템
                i.size = new Vector2Int(1, 1); i.weightKg = 0.1f;    // TODO(GDD-?) Q11
                i.rarity = Rarity.Common; i.basePrice = 0;           // TODO(GDD-?)
            }, Tag);
        }

        // ---------- RaidConfig (§14, §8.2, RULE-02) ----------

        private static void CreateRaidConfig()
        {
            var flare = SetupUtil.Load<ItemDefinitionSO>(FlarePath);

            SetupUtil.CreateAssetIfMissing<RaidConfigSO>(RaidConfigPath, c =>
            {
                c.timeLimitSec = 1500f; // RULE-02 25분 (§14 기본값)

                // §8.2 소요(waitSec)·조건. 반경(radiusM)은 TODO(GDD-?) Q8 임시값 8/8/10/5m.
                c.extractions = new[]
                {
                    // EXT-01 항상 열림, 10초 대기
                    Extraction(Ext01, "남쪽 도로", 10f, 8f),
                    // EXT-02 전원 스위치 작동 필요, 15초. 스위치 id = CrimsonBasinLayout.PowerSwitchId. TODO(GDD-?) Q5
                    Extraction(Ext02, "열차 신호소", 15f, 8f, requiredSwitchId: CrimsonBasinLayout.PowerSwitchId),
                    // EXT-03 레이드 15분 이후만(경과 ≥ 900초), 20초, 신호탄 소모(완료 순간 1개, Q6)
                    Extraction(Ext03, "헬기 착륙장", 20f, 10f, availableAfterSec: 900f, consumedItem: flare),
                    // EXT-04 배낭 미착용 시만, 7초 (조끼는 허용)
                    Extraction(Ext04, "하수 배출구", 7f, 5f, forbidsBackpack: true),
                };

                // TODO(GDD-?) Q2·Q3: 스폰별 고정 활성 세트, EXT-01은 "항상 열림"이라 모든 스폰에 포함.
                // 설계 §2.3 표의 "(+EXT-03)"을 포함으로 읽음 (M4TestData와 같다).
                c.spawnRules = new[]
                {
                    SpawnRule(SpawnWest, Ext01, Ext02, Ext03),
                    SpawnRule(SpawnEast, Ext01, Ext04, Ext03),
                    SpawnRule(SpawnNorth, Ext01, Ext02, Ext04),
                };
                c.randomActiveCount = new Vector2Int(2, 3); // §8.2 "2~3개만 활성" (spawnRules가 없을 때)
                c.alwaysActiveIds = new[] { Ext01 };        // TODO(GDD-?) Q3
                c.endDelaySec = 3f;                         // TODO(GDD-?) Q15 종료 오버레이
                c.zoneHeightToleranceM = 3f;                // TODO(GDD-?) Q8 구역 높이 허용
            }, Tag);
        }

        private static ExtractionPointConfig Extraction(string id, string displayName, float waitSec, float radiusM,
            string requiredSwitchId = "", float availableAfterSec = 0f, ItemDefinitionSO consumedItem = null,
            bool forbidsBackpack = false) =>
            new ExtractionPointConfig
            {
                id = id,
                displayName = displayName,
                waitSec = waitSec,
                radiusM = radiusM,
                requiredSwitchId = requiredSwitchId,
                availableAfterSec = availableAfterSec,
                consumedItem = consumedItem,
                forbidsBackpack = forbidsBackpack,
            };

        private static SpawnRule SpawnRule(string spawnId, params string[] activeExtractionIds) =>
            new SpawnRule { spawnId = spawnId, activeExtractionIds = activeExtractionIds };

        // ---------- 상자 루트 테이블 (Q17, 전부 TODO(GDD-?)) ----------
        // 일반 상자 1종. 의약품·탄창·잡동사니 위주, 헬멧·신호탄은 낮은 가중치(Q7-A: 신호탄은 고정 상자 2곳 + 이 테이블).
        // 기대값: 상자 19개 × 평균 2회 굴림 × 신호탄 확률 0.5/15 ≈ 맵 전체 1.3개.

        private static void CreateCrateLootTable()
        {
            SetupUtil.CreateAssetIfMissing<LootTableSO>(LootCratePath, t =>
            {
                t.id = "LT-CRATE";
                t.rollCount = new Vector2Int(1, 3); // TODO(GDD-?): 굴림 횟수 임시값
                t.entries = new List<LootEntry>();
                AddEntry(t, BandagePath, 3f);       // TODO(GDD-?): 가중치 전부 임시값
                AddEntry(t, WiresPath, 3f);
                AddEntry(t, TourniquetPath, 2f);
                AddEntry(t, M3DataSetup.Mag556Path, 2f);
                AddEntry(t, PipePath, 2f);
                AddEntry(t, SplintPath, 1f);
                AddEntry(t, M3DataSetup.Mag9mm12Path, 1f);
                AddEntry(t, HelmetPath, 0.5f);
                AddEntry(t, FlarePath, 0.5f);
            }, Tag);
        }

        private static void AddEntry(LootTableSO table, string itemPath, float weight)
        {
            var item = SetupUtil.Load<ItemDefinitionSO>(itemPath);
            if (item != null)
                table.entries.Add(new LootEntry { item = item, weight = weight });
        }

        // ---------- HudConfig.mapItem (Q11) ----------
        // 지도 화면은 지도 아이템을 가지고 있을 때만 열린다. 필드가 비어 있을 때만 채운다(다른 값으로 바꿨으면 보존).

        private static void AssignHudMapItem()
        {
            var hud = AssetDatabase.LoadAssetAtPath<ScriptableObject>(M2DataSetup.HudConfigPath);
            if (hud == null)
            {
                Debug.LogError($"{Tag} HudConfig가 없다: {M2DataSetup.HudConfigPath}");
                return;
            }

            var so = new SerializedObject(hud);
            SerializedProperty property = so.FindProperty(HudMapItemField);
            if (property == null)
            {
                Debug.LogWarning($"{Tag} HudConfigSO에 '{HudMapItemField}' 필드가 아직 없다(UI 단계). 지도 아이템 연결을 건너뛴다.");
                return;
            }
            if (property.objectReferenceValue != null)
                return;

            property.objectReferenceValue = SetupUtil.Load<ItemDefinitionSO>(MapItemPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hud);
            Debug.Log($"{Tag} HudConfig.{HudMapItemField} = ITM-MAP-01");
        }
    }
}
