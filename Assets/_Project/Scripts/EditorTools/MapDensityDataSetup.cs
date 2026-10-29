using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Inventory;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // 맵 밀도 ③ 루트 데이터 (docs/design/MapDensity_design.md §5.1·§5.2·§9, SYS-INV §6 루트 컨테이너).
    // LootSpawnConfig.asset·LT-CRATE-LOW·LT-TRUNK를 **파일이 없을 때만** 만든다(MVP 마무리 방식 — 기획 조정 보존, 두 번째 실행은 바이트 동일).
    // 값은 전부 TODO(GDD-?) 임시값 = "맵 밀도 기본값(플레이 검증 전)". 경제 시뮬레이션(LootEconomySimulator, Q2 A: 지금 맵의 1.3~1.5배)으로 정했다.
    // 값을 바꾸려면 에셋을 고치거나(보존된다) 에셋을 지우고 이 메뉴를 다시 실행한다. 목표 수(crate/trunkTargetCount)를 바꾸면 Rebuild 메뉴.
    // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.MapDensitySetup.CreateData -logFile setup.log
    public static class MapDensityDataSetup
    {
        private const string Tag = "[MapDensityDataSetup]";

        public const string LootSpawnConfigPath = M2DataSetup.DataFolder + "/Inventory/LootSpawnConfig.asset";
        public const string CrateLowTablePath = M3DataSetup.LootFolder + "/LT-CRATE-LOW.asset";
        public const string TrunkTablePath = M3DataSetup.LootFolder + "/LT-TRUNK.asset";
        public const string CrateLowId = "LT-CRATE-LOW";
        public const string TrunkId = "LT-TRUNK";

        private const string JunkFolder = M2DataSetup.JunkFolder;
        private const string MedicalFolder = M2DataSetup.MedicalFolder;
        private const string PipePath = JunkFolder + "/JNK-01.asset";
        private const string EnginePartPath = JunkFolder + "/JNK-02.asset";
        private const string WiresPath = JunkFolder + "/JNK-03.asset";
        private const string BandagePath = MedicalFolder + "/MED-BND-01.asset";
        private const string SplintPath = MedicalFolder + "/MED-SPL-01.asset";
        private const string TourniquetPath = MedicalFolder + "/MED-TRQ-01.asset";
        private const string WaterPath = MedicalFolder + "/ITM-WTR-01.asset";
        private const string FoodPath = MedicalFolder + "/ITM-FOD-01.asset";
        private const string Box12Path = M3DataSetup.WeaponsFolder + "/BOX-12GA-8.asset";

        // 구역별 (zoneId, 상자 목표(기존 포함), 트렁크 목표, 상자 활성 비율, 트렁크 활성 비율). TODO(GDD-?) 전부 임시값.
        // 상자 목표 = 설계 §3.2. 트렁크 목표 = 승용차마다 1개(실제 씬의 구역별 승용차 수 — 배치가 확인한다).
        // 비율 = 경제 시뮬레이션으로 맞춘 값(설계 §14.8 표). 설계와 다름(리뷰 L11): POI 트렁크 0.50(초안 0.40), 트렁크 목표 = 실제 승용차 70(초안 67·구역 분포도 다름).
        public static readonly (string Zone, int Crates, int Trunks, float CrateRatio, float TrunkRatio)[] Zones =
        {
            ("POI-HWY", 14, 7, 0.40f, 0.50f),
            ("POI-REF", 16, 4, 0.40f, 0.50f),
            ("POI-VIL", 16, 4, 0.40f, 0.50f),
            ("POI-PYL", 12, 2, 0.40f, 0.50f),
            ("POI-RES", 13, 4, 0.40f, 0.50f),
            (LootZoneRules.Route, 36, 35, 0.30f, 0.30f),
            (LootZoneRules.Outskirt, 26, 14, 0.30f, 0.30f),
        };

        // LT-CRATE-LOW (길목·외곽 상자, 고철 위주). (경로, 가중치) — TODO(GDD-?) 임시값
        public static readonly (string Path, float Weight)[] CrateLowEntries =
        {
            (PipePath, 4f), (WiresPath, 4f), (EnginePartPath, 1f), (BandagePath, 3f), (WaterPath, 2f), (FoodPath, 2f),
            (M3DataSetup.Mag9mm12Path, 1f), (Box12Path, 1f), (SplintPath, 1f),
        };
        public static readonly Vector2Int CrateLowRolls = new Vector2Int(1, 2);

        // LT-TRUNK (승용차 트렁크). 신호탄 없음(EXT-03 희소성 — 신호탄은 LT-CRATE에만). TODO(GDD-?) 임시값
        public static readonly (string Path, float Weight)[] TrunkEntries =
        {
            (PipePath, 3f), (WiresPath, 3f), (EnginePartPath, 1f), (WaterPath, 2f), (FoodPath, 2f), (BandagePath, 2f),
            (Box12Path, 1f), (M3DataSetup.Mag9mm12Path, 1f), (TourniquetPath, 1f),
        };
        public static readonly Vector2Int TrunkRolls = new Vector2Int(0, 2);

        // 없을 때만 (메뉴 = Ashfall/Setup/Create Map Density Data → MapDensitySetup.CreateData). 테이블 → 설정 순서(설정이 테이블을 참조한다).
        public static void CreateLootData()
        {
            CreateTable(CrateLowTablePath, CrateLowId, CrateLowEntries, CrateLowRolls);
            CreateTable(TrunkTablePath, TrunkId, TrunkEntries, TrunkRolls);
            var crate = LoadOrThrow<LootTableSO>(M4DataSetup.LootCratePath);
            var low = AssetDatabase.LoadAssetAtPath<LootTableSO>(CrateLowTablePath);
            var trunk = AssetDatabase.LoadAssetAtPath<LootTableSO>(TrunkTablePath);
            SetupUtil.CreateAssetIfMissing<LootSpawnConfigSO>(LootSpawnConfigPath, cfg =>
            {
                var zones = new List<LootZoneConfig>();
                foreach (var z in Zones)
                    zones.Add(new LootZoneConfig
                    {
                        zoneId = z.Zone,
                        crateTargetCount = z.Crates,
                        trunkTargetCount = z.Trunks,
                        crateActiveRatio = z.CrateRatio,
                        trunkActiveRatio = z.TrunkRatio,
                        crateTable = LootZoneRules.IsPoi(z.Zone) ? crate : low,
                        trunkTable = trunk,
                    });
                cfg.zones = zones.ToArray();
                // 나머지(최소 활성 1, 비활성 = 빈 컨테이너(Q1 A), 그리드 6×4·5×3, 이름)는 SO 초기값 그대로
            }, Tag);
        }

        private static void CreateTable(string path, string id, (string Path, float Weight)[] entries, Vector2Int rolls)
        {
            SetupUtil.CreateAssetIfMissing<LootTableSO>(path, t =>
            {
                t.id = id;
                t.rollCount = rolls;
                t.entries = new List<LootEntry>();
                foreach (var e in entries)
                {
                    var item = LoadOrThrow<ItemDefinitionSO>(e.Path);
                    // 리뷰 L9: 없는 아이템을 조용히 빼지 않는다(LoadOrThrow가 던진다)
                    t.entries.Add(new LootEntry { item = item, weight = e.Weight });
                }
            }, Tag);
        }

        // 리뷰 L9: 데이터 원천이 없으면 반쯤 빈 테이블·설정을 만들지 않고 예외(SetupUtil.Load는 오류 로그 뒤 null을 돌려준다)
        private static T LoadOrThrow<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new System.InvalidOperationException($"{Tag} 에셋이 없다: {path} ({typeof(T).Name}) — 먼저 M2~M5 데이터 메뉴");
            return asset;
        }
    }
}
