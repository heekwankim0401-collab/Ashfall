using System;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Hideout;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // M5 데이터 에셋 생성 (M5 설계 §2.3, §3.2·§3.3, §4.6 "2c").
    // - 아이템 basePrice: 설계 §4.6 가격 표 (전부 TODO(GDD-?) Q10). 값이 0(미설정)일 때만 기록 — 조정한 값 보존.
    // - Data/Hideout/HideoutConfig.asset: §9 보관함 10×30, 시작 탄피 6000(Q9), 부랑자 키트 20분 + 녹슨 권총·권총 탄창 ×2·붕대(Q13~Q17)
    // - Data/Hideout/TraderConfig.asset: 상인 1명, 매수 1.0 / 매도 0.4(Q10), 고정 판매 목록(Q11)
    // - Data/Items/ItemDatabase.asset: 프로젝트(Assets/)의 모든 ItemDefinitionSO를 id 순으로. 매번 다시 채운다(GUID 유지).
    //   id 중복·빈 id면 예외(DB를 바꾸지 않는다).
    // 멱등: 이미 있는 설정 에셋은 덮어쓰지 않는다(빈 목록만 채움). 여러 번 실행해도 결과가 같다.
    // 배치모드: -executeMethod Ashfall.EditorTools.M5DataSetup.CreateAll
    public static class M5DataSetup
    {
        private const string Tag = "[M5DataSetup]";

        public const string DataFolder = M2DataSetup.DataFolder;
        public const string HideoutFolder = DataFolder + "/Hideout";
        public const string HideoutConfigPath = HideoutFolder + "/HideoutConfig.asset";
        public const string TraderConfigPath = HideoutFolder + "/TraderConfig.asset";
        public const string ItemDatabasePath = DataFolder + "/Items/ItemDatabase.asset";

        // ItemDatabase가 아이템 정의를 찾는 범위 (Packages 제외)
        public const string ItemSearchFolder = "Assets";

        // 아이템 에셋 경로 (M2~M4 메뉴가 만든 것)
        public const string RiflePath = M3DataSetup.RiflePath;
        public const string SmgPath = M3DataSetup.SmgPath;
        public const string PistolPath = M3DataSetup.PistolPath;
        public const string Mag556Path = M3DataSetup.Mag556Path;
        public const string Mag9mm30Path = M3DataSetup.Mag9mm30Path;
        public const string Mag9mm12Path = M3DataSetup.Mag9mm12Path;
        public const string MapItemPath = M4DataSetup.MapItemPath;
        public const string FlarePath = M4DataSetup.FlarePath;
        public const string BandagePath = M2DataSetup.MedicalFolder + "/MED-BND-01.asset";
        public const string TourniquetPath = M2DataSetup.MedicalFolder + "/MED-TRQ-01.asset";
        public const string SplintPath = M2DataSetup.MedicalFolder + "/MED-SPL-01.asset";
        public const string VestPath = M2DataSetup.GearFolder + "/RIG-01.asset";
        public const string BackpackPath = M2DataSetup.GearFolder + "/BAG-01.asset";
        public const string HelmetPath = M2DataSetup.ArmorFolder + "/ARM-HLM-01.asset";
        public const string BodyArmorPath = M2DataSetup.ArmorFolder + "/ARM-BDY-01.asset";
        public const string PipePath = M2DataSetup.JunkFolder + "/JNK-01.asset";
        public const string EnginePartPath = M2DataSetup.JunkFolder + "/JNK-02.asset";
        public const string WiresPath = M2DataSetup.JunkFolder + "/JNK-03.asset";

        // 설계 §4.6 가격 표 (basePrice, 탄피). 전부 TODO(GDD-?) Q10 — GDD에 가격이 없다.
        // 탄약 정의(AMMO-556/9MM)는 인벤토리 아이템이 아니라 0 그대로(표에 없음).
        private static readonly (string path, string id, int basePrice)[] PriceTable =
        {
            (RiflePath, "WPN-RIF-01", 4000),
            (SmgPath, "WPN-SMG-01", 1500),
            (PistolPath, "WPN-PST-01", 400),
            (Mag556Path, "MAG-556-30", 400),
            (Mag9mm30Path, "MAG-9MM-30", 300),
            (Mag9mm12Path, "MAG-9MM-12", 120),
            (MapItemPath, "ITM-MAP-01", 100),
            (FlarePath, "ITM-FLR-01", 500),
            (BandagePath, "MED-BND-01", 60),
            (TourniquetPath, "MED-TRQ-01", 150),
            (SplintPath, "MED-SPL-01", 120),
            (VestPath, "RIG-01", 600),
            (BackpackPath, "BAG-01", 800),
            (HelmetPath, "ARM-HLM-01", 1200),
            (BodyArmorPath, "ARM-BDY-01", 2500),
            (PipePath, "JNK-01", 40),
            (EnginePartPath, "JNK-02", 300),
            (WiresPath, "JNK-03", 80),
        };

        // §9 부랑자 키트: 녹슨 권총 + 탄창 2개 + 붕대 (같은 것은 여러 번)
        private static readonly string[] VagrantKitPaths = { PistolPath, Mag9mm12Path, Mag9mm12Path, BandagePath };

        // 상인 판매 목록 (설계 §4.6 ✔, TODO(GDD-?) Q11). 신호탄은 팔지 않는다(EXT-03 희소성). 지도는 반드시 있다(RULE-06).
        private static readonly string[] TraderCatalogPaths =
        {
            RiflePath, Mag556Path, MapItemPath, BandagePath, TourniquetPath, SplintPath,
            VestPath, BackpackPath, HelmetPath, BodyArmorPath,
        };

        [MenuItem("Ashfall/Setup/Create M5 Data")]
        public static void CreateData()
        {
            // M1~M4 데이터(아이템·탄창·지도 등)가 먼저 있어야 한다. 이미 있는 에셋은 건너뛴다.
            M4DataSetup.CreateData();

            ApplyPrices();
            CreateHideoutConfig();
            CreateTraderConfig();
            AssetDatabase.SaveAssets();
            // 가격을 저장한 뒤 DB를 채운다(아이템 정의 전부 — 이 메뉴가 새 아이템을 만들지는 않는다).
            CreateItemDatabase();
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료");
        }

        // 배치모드용 진입점. CreateData와 같다.
        public static void CreateAll() => CreateData();

        // ---------- 가격 (§4.6, TODO(GDD-?) Q10) ----------

        private static void ApplyPrices()
        {
            foreach (var (path, id, price) in PriceTable)
            {
                var item = SetupUtil.Load<ItemDefinitionSO>(path);
                if (item == null)
                    continue;
                if (item.id != id)
                {
                    Debug.LogError($"{Tag} {path}의 id가 {item.id}다 (기대 {id}). 가격을 기록하지 않는다.", item);
                    continue;
                }
                // 0 = 미설정(M2~M4 메뉴가 0으로 만들었다). 사람이 조정한 값은 보존한다.
                if (item.basePrice != 0)
                    continue;
                item.basePrice = price;
                EditorUtility.SetDirty(item);
                Debug.Log($"{Tag} {id}.basePrice = {price}");
            }
        }

        // ---------- HideoutConfig (§9, 설계 §3.1·§3.3) ----------

        private static void CreateHideoutConfig()
        {
            var config = SetupUtil.CreateAssetIfMissing<HideoutConfigSO>(HideoutConfigPath, c =>
            {
                c.stashSize = new Vector2Int(10, 30);  // §9 10×30 (W×H), 업그레이드 없음 TODO(GDD-?) Q1
                c.startingCasings = 6000;              // TODO(GDD-?) Q9 = 소총+5.56 탄창 2+지도+조끼(5500) + 여유 500
                c.vagrantKitCooldownSec = 1200f;       // §9 20분
                c.vagrantKitItems = LoadAll(VagrantKitPaths);
            }, Tag);

            // 이미 있는데 키트 목록이 비어 있으면(에디터 Create 메뉴로 만든 경우 등) 채운다. 채워져 있으면 보존.
            if (config.vagrantKitItems == null || config.vagrantKitItems.Length == 0)
            {
                config.vagrantKitItems = LoadAll(VagrantKitPaths);
                EditorUtility.SetDirty(config);
                Debug.Log($"{Tag} HideoutConfig.vagrantKitItems 채움");
            }
        }

        // ---------- TraderConfig (§9, 설계 §3.2) ----------

        private static void CreateTraderConfig()
        {
            var trader = SetupUtil.CreateAssetIfMissing<TraderConfigSO>(TraderConfigPath, t =>
            {
                t.id = "TRD-01";
                t.displayName = "고물상";        // TODO(GDD-?) 상인 이름
                t.buyPriceMultiplier = 1f;      // TODO(GDD-?) Q10
                t.sellPriceMultiplier = 0.4f;   // TODO(GDD-?) Q10 (매수 배율 이하 — 되팔기 이득 없음)
                t.catalog = LoadAll(TraderCatalogPaths);
            }, Tag);

            if (trader.catalog == null || trader.catalog.Length == 0)
            {
                trader.catalog = LoadAll(TraderCatalogPaths);
                EditorUtility.SetDirty(trader);
                Debug.Log($"{Tag} TraderConfig.catalog 채움");
            }
        }

        // ---------- ItemDatabase (설계 §2.3) ----------

        // 프로젝트의 모든 아이템 정의를 id 순(서수 비교)으로 모은다. 중복·빈 id면 예외.
        public static List<ItemDefinitionSO> CollectItemDefinitions()
        {
            var items = new List<ItemDefinitionSO>();
            var seen = new HashSet<string>();
            // 파생 형식(무기·탄약·탄창·방어구·의약품·장비)까지 빠짐없이 — ScriptableObject 전부를 보고 형식으로 거른다.
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { ItemSearchFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!seen.Add(path))
                    continue;
                // 한 파일에 서브 에셋이 있어도 모두 본다
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is ItemDefinitionSO item)
                        items.Add(item);
                }
            }

            var empty = items.Where(i => string.IsNullOrEmpty(i.id)).Select(AssetDatabase.GetAssetPath).ToArray();
            if (empty.Length > 0)
                throw new InvalidOperationException($"{Tag} id가 빈 아이템 정의: {string.Join(", ", empty)}");

            var duplicates = items.GroupBy(i => i.id, StringComparer.Ordinal).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ({string.Join(", ", g.Select(AssetDatabase.GetAssetPath))})").ToArray();
            if (duplicates.Length > 0)
                throw new InvalidOperationException($"{Tag} 아이템 id 중복: {string.Join("; ", duplicates)}");

            items.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return items;
        }

        // M7 데이터 메뉴도 새 아이템을 만든 뒤 이것을 부른다(공개만, 동작 같음). 저장(SaveAssets)은 부르는 쪽이 한다.
        public static void CreateItemDatabase()
        {
            // 검사를 먼저 끝낸다 — 실패하면 기존 DB를 건드리지 않는다.
            List<ItemDefinitionSO> items = CollectItemDefinitions();

            var database = SetupUtil.CreateAssetIfMissing<ItemDatabaseSO>(ItemDatabasePath, d => d.items = new List<ItemDefinitionSO>(items), Tag);
            if (database.items != null && database.items.SequenceEqual(items))
                return;

            database.items = items;
            EditorUtility.SetDirty(database);
            Debug.Log($"{Tag} ItemDatabase 갱신: {items.Count}개");
        }

        // ---------- 도우미 ----------

        private static ItemDefinitionSO[] LoadAll(IEnumerable<string> paths) =>
            paths.Select(SetupUtil.Load<ItemDefinitionSO>).Where(i => i != null).ToArray();
    }
}
