using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.Hideout;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M5 데이터 에셋 값 = GDD §9 + M5 설계 임시값(§4.6 가격 표, Q9~Q17 — TODO(GDD-?)).
    // 에셋은 Ashfall/Setup/Create M5 Data(M5DataSetup)가 만든다. 없으면 실패한다.
    public class M5DataAssetTests
    {
        private const string MenuPath = "Ashfall/Setup/Create M5 Data";
        private const string Data = "Assets/_Project/Data";
        private const string HideoutConfigPath = Data + "/Hideout/HideoutConfig.asset";
        private const string TraderConfigPath = Data + "/Hideout/TraderConfig.asset";
        private const string ItemDatabasePath = Data + "/Items/ItemDatabase.asset";
        private const float Eps = 1e-5f;

        // 설계 §4.6 가격 표 (id, 에셋 경로, basePrice). 전부 TODO(GDD-?) Q10.
        private static readonly (string id, string path, int price)[] PriceTable =
        {
            ("WPN-RIF-01", Data + "/Weapons/WPN-RIF-01.asset", 4000),
            ("WPN-SMG-01", Data + "/Weapons/WPN-SMG-01.asset", 1500),
            ("WPN-PST-01", Data + "/Weapons/WPN-PST-01.asset", 400),
            ("MAG-556-30", Data + "/Weapons/MAG-556-30.asset", 400),
            ("MAG-9MM-30", Data + "/Weapons/MAG-9MM-30.asset", 300),
            ("MAG-9MM-12", Data + "/Weapons/MAG-9MM-12.asset", 120),
            ("ITM-MAP-01", Data + "/Items/Raid/ITM-MAP-01.asset", 100),
            ("ITM-FLR-01", Data + "/Items/Raid/ITM-FLR-01.asset", 500),
            ("MED-BND-01", Data + "/Items/Medical/MED-BND-01.asset", 60),
            ("MED-TRQ-01", Data + "/Items/Medical/MED-TRQ-01.asset", 150),
            ("MED-SPL-01", Data + "/Items/Medical/MED-SPL-01.asset", 120),
            ("RIG-01", Data + "/Items/Gear/RIG-01.asset", 600),
            ("BAG-01", Data + "/Items/Gear/BAG-01.asset", 800),
            ("ARM-HLM-01", Data + "/Items/Armor/ARM-HLM-01.asset", 1200),
            ("ARM-BDY-01", Data + "/Items/Armor/ARM-BDY-01.asset", 2500),
            ("JNK-01", Data + "/Items/Junk/JNK-01.asset", 40),
            ("JNK-02", Data + "/Items/Junk/JNK-02.asset", 300),
            ("JNK-03", Data + "/Items/Junk/JNK-03.asset", 80),
        };

        // 탄약 정의는 인벤토리 아이템이 아니다 — 가격 없음
        private static readonly (string id, string path)[] AmmoDefinitions =
        {
            ("AMMO-556", Data + "/Weapons/AMMO-556.asset"),
            ("AMMO-9MM", Data + "/Weapons/AMMO-9MM.asset"),
        };

        // §9 부랑자 키트 (녹슨 권총 + 탄창 2개 + 붕대)
        private static readonly string[] VagrantKitIds = { "WPN-PST-01", "MAG-9MM-12", "MAG-9MM-12", "MED-BND-01" };

        // 설계 §4.6 판매 목록(✔) 순서 = M5TestData.TraderConfig
        private static readonly string[] TraderCatalogIds =
        {
            "WPN-RIF-01", "MAG-556-30", "ITM-MAP-01", "MED-BND-01", "MED-TRQ-01", "MED-SPL-01",
            "RIG-01", "BAG-01", "ARM-HLM-01", "ARM-BDY-01",
        };

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"에셋이 없다: {path}. 메뉴 {MenuPath}를 실행할 것.");
            return asset;
        }

        private static string PathOf(string id) => PriceTable.First(p => p.id == id).path;

        // 설계 §3.2 매수가 = max(1, 올림(basePrice × 배율)). PriceRules(2d)와 독립으로 계산한다.
        private static int BuyPrice(ItemDefinitionSO item, TraderConfigSO trader) =>
            Mathf.Max(1, Mathf.CeilToInt(item.basePrice * trader.buyPriceMultiplier));

        // 프로젝트(Assets/)의 모든 아이템 정의 (파생 형식 포함)
        private static List<ItemDefinitionSO> AllItemDefinitionsInProject()
        {
            var result = new List<ItemDefinitionSO>();
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }).Distinct())
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                {
                    if (asset is ItemDefinitionSO item)
                        result.Add(item);
                }
            }
            return result;
        }

        // ---------- 가격 (§4.6, TODO(GDD-?) Q10) ----------

        [Test]
        public void Prices_MatchDesignTable()
        {
            foreach (var (id, path, price) in PriceTable)
            {
                var item = Load<ItemDefinitionSO>(path);
                Assert.AreEqual(id, item.id, path);
                Assert.AreEqual(price, item.basePrice, $"{id} basePrice (설계 §4.6)");
                StringAssert.Contains($"basePrice: {price}", File.ReadAllText(Path.GetFullPath(path)), $"{id} 파일에 기록됨");
            }
        }

        [Test]
        public void AmmoDefinitions_HaveNoPrice()
        {
            foreach (var (id, path) in AmmoDefinitions)
            {
                var ammo = Load<AmmoDefinitionSO>(path);
                Assert.AreEqual(id, ammo.id);
                Assert.AreEqual(0, ammo.basePrice, $"{id}는 인벤토리 아이템이 아니다");
            }
        }

        // 규칙 테스트의 가격(M5TestData FakeCatalog.Standard)과 실제 데이터가 갈라지지 않게.
        [Test]
        public void Prices_EqualFakeCatalogStandard()
        {
            FakeCatalog catalog = FakeCatalog.Standard();
            var created = new List<ItemDefinitionSO>();
            try
            {
                foreach (var (id, path, _) in PriceTable)
                {
                    ItemDefinitionSO expected = catalog.Get(id);
                    created.Add(expected);
                    Assert.AreEqual(expected.basePrice, Load<ItemDefinitionSO>(path).basePrice, id);
                }
                foreach (var (id, path) in AmmoDefinitions)
                {
                    ItemDefinitionSO expected = catalog.Get(id);
                    created.Add(expected);
                    Assert.AreEqual(expected.basePrice, Load<ItemDefinitionSO>(path).basePrice, id);
                }
            }
            finally
            {
                foreach (ItemDefinitionSO so in created)
                    Object.DestroyImmediate(so);
            }
        }

        // 가격 표가 프로젝트의 탄약 외 아이템 정의를 전부 덮는다 (새 아이템을 추가하면 가격도 정할 것).
        [Test]
        public void Prices_CoverEveryNonAmmoItemDefinition()
        {
            var tableIds = new HashSet<string>(PriceTable.Select(p => p.id));
            var missing = AllItemDefinitionsInProject()
                .Where(i => !(i is AmmoDefinitionSO) && !tableIds.Contains(i.id))
                .Select(i => i.id).ToArray();
            CollectionAssert.IsEmpty(missing, "가격 표에 없는 아이템");
        }

        // ---------- HideoutConfig (§9, Q1·Q9·Q13~Q17) ----------

        [Test]
        public void HideoutConfig_MatchesGddAndDesign()
        {
            var c = Load<HideoutConfigSO>(HideoutConfigPath);
            Assert.AreEqual(new Vector2Int(10, 30), c.stashSize, "§9 보관함 10×30 (W×H)");
            Assert.AreEqual(6000, c.startingCasings, "TODO(GDD-?) Q9");
            Assert.AreEqual(1200f, c.vagrantKitCooldownSec, Eps, "§9 20분");
        }

        [Test]
        public void HideoutConfig_VagrantKit_IsPistolTwoMagazinesBandage()
        {
            var c = Load<HideoutConfigSO>(HideoutConfigPath);
            Assert.IsNotNull(c.vagrantKitItems);
            Assert.IsTrue(c.vagrantKitItems.All(i => i != null), "빈 항목");
            CollectionAssert.AreEqual(VagrantKitIds, c.vagrantKitItems.Select(i => i.id).ToArray());
            for (int i = 0; i < VagrantKitIds.Length; i++)
                Assert.AreSame(Load<ItemDefinitionSO>(PathOf(VagrantKitIds[i])), c.vagrantKitItems[i], $"[{i}] 실제 에셋 참조");

            Assert.IsInstanceOf<WeaponDefinitionSO>(c.vagrantKitItems[0], "녹슨 권총");
            var mag = c.vagrantKitItems[1] as MagazineDefinitionSO;
            Assert.IsNotNull(mag, "권총 탄창");
            Assert.AreEqual(((WeaponDefinitionSO)c.vagrantKitItems[0]).ammo, mag.ammo, "권총에 맞는 탄창");
            Assert.IsInstanceOf<MedicalDefinitionSO>(c.vagrantKitItems[3], "붕대");
        }

        // 규칙 테스트의 기준(M5TestData.HideoutConfig)과 같다.
        [Test]
        public void HideoutConfig_EqualsM5TestData()
        {
            var asset = Load<HideoutConfigSO>(HideoutConfigPath);
            FakeCatalog catalog = FakeCatalog.Standard();
            HideoutConfigSO expected = M5TestData.HideoutConfig(catalog);
            try
            {
                Assert.AreEqual(expected.stashSize, asset.stashSize);
                Assert.AreEqual(expected.startingCasings, asset.startingCasings);
                Assert.AreEqual(expected.vagrantKitCooldownSec, asset.vagrantKitCooldownSec, Eps);
                CollectionAssert.AreEqual(expected.vagrantKitItems.Select(i => i.id).ToArray(),
                    asset.vagrantKitItems.Select(i => i.id).ToArray());
            }
            finally
            {
                Object.DestroyImmediate(expected);
            }
        }

        // 키트가 빈 보관함에 들어간다 (Q15 — 전부 들어갈 때만 지급).
        [Test]
        public void HideoutConfig_VagrantKit_FitsEmptyStash()
        {
            var c = Load<HideoutConfigSO>(HideoutConfigPath);
            var stash = new ItemGrid(new GridRef(GridKind.Loot), c.stashSize); // 보관함 = Loot 자리 (Q28)
            foreach (ItemDefinitionSO def in c.vagrantKitItems)
            {
                ItemInstance item = ItemFactory.Create(def);
                Assert.IsTrue(stash.TryFindFreeCell(item, out Vector2Int cell, out bool rotated), def.id);
                Assert.IsTrue(stash.TryPlace(item, cell, rotated), def.id);
            }
        }

        // ---------- TraderConfig (§9, Q10·Q11) ----------

        [Test]
        public void TraderConfig_MatchesDesign()
        {
            var t = Load<TraderConfigSO>(TraderConfigPath);
            Assert.AreEqual("TRD-01", t.id);
            Assert.AreEqual("고물상", t.displayName, "TODO(GDD-?)");
            Assert.AreEqual(1f, t.buyPriceMultiplier, Eps, "TODO(GDD-?) Q10");
            Assert.AreEqual(0.4f, t.sellPriceMultiplier, Eps, "TODO(GDD-?) Q10");
            Assert.LessOrEqual(t.sellPriceMultiplier, t.buyPriceMultiplier, "되팔기 이득 없음");
            Assert.Greater(t.sellPriceMultiplier, 0f);
        }

        [Test]
        public void TraderConfig_Catalog_MatchesDesignList()
        {
            var t = Load<TraderConfigSO>(TraderConfigPath);
            Assert.IsNotNull(t.catalog);
            Assert.IsTrue(t.catalog.All(i => i != null), "빈 항목");
            CollectionAssert.AreEqual(TraderCatalogIds, t.catalog.Select(i => i.id).ToArray());
            CollectionAssert.AllItemsAreUnique(t.catalog);
            foreach (ItemDefinitionSO item in t.catalog)
                Assert.AreSame(Load<ItemDefinitionSO>(PathOf(item.id)), item, $"{item.id} 실제 에셋 참조");
        }

        [Test]
        public void TraderConfig_Catalog_AllPricedSellable_MapIncluded_NoFlare()
        {
            var t = Load<TraderConfigSO>(TraderConfigPath);
            foreach (ItemDefinitionSO item in t.catalog)
            {
                Assert.Greater(item.basePrice, 0, $"{item.id}: 가격 0이면 판매 안 함");
                Assert.IsFalse(item is AmmoDefinitionSO, $"{item.id}: 탄약 정의는 아이템이 아니다");
            }
            Assert.IsTrue(t.catalog.Any(i => i.id == "ITM-MAP-01"), "지도는 상인에서만 얻을 수 있다 (RULE-06)");
            Assert.IsFalse(t.catalog.Any(i => i.id == "ITM-FLR-01"), "신호탄은 팔지 않는다 (EXT-03 희소성)");
        }

        [Test]
        public void TraderConfig_EqualsM5TestData()
        {
            var asset = Load<TraderConfigSO>(TraderConfigPath);
            FakeCatalog catalog = FakeCatalog.Standard();
            TraderConfigSO expected = M5TestData.TraderConfig(catalog);
            try
            {
                Assert.AreEqual(expected.id, asset.id);
                Assert.AreEqual(expected.displayName, asset.displayName);
                Assert.AreEqual(expected.buyPriceMultiplier, asset.buyPriceMultiplier, Eps);
                Assert.AreEqual(expected.sellPriceMultiplier, asset.sellPriceMultiplier, Eps);
                CollectionAssert.AreEqual(expected.catalog.Select(i => i.id).ToArray(), asset.catalog.Select(i => i.id).ToArray());
            }
            finally
            {
                Object.DestroyImmediate(expected);
            }
        }

        // 설계 §3.2: 시작 탄피로 소총 + 5.56 탄창 2 + 지도 + 조끼(= M4 고정 키트 수준)를 한 번 살 수 있다.
        [Test]
        public void StartingCasings_CanBuyRifleTwoMagsMapVest()
        {
            var c = Load<HideoutConfigSO>(HideoutConfigPath);
            var t = Load<TraderConfigSO>(TraderConfigPath);
            string[] kit = { "WPN-RIF-01", "MAG-556-30", "MAG-556-30", "ITM-MAP-01", "RIG-01" };
            int total = 0;
            foreach (string id in kit)
            {
                ItemDefinitionSO item = t.catalog.FirstOrDefault(i => i.id == id);
                Assert.IsNotNull(item, $"{id}가 판매 목록에 없다");
                total += BuyPrice(item, t);
            }
            Assert.AreEqual(5500, total, "설계 §4.6 계산");
            Assert.LessOrEqual(total, c.startingCasings);
        }

        // ---------- ItemDatabase (설계 §2.3) ----------

        [Test]
        public void ItemDatabase_ContainsEveryItemDefinitionInProject()
        {
            var db = Load<ItemDatabaseSO>(ItemDatabasePath);
            Assert.IsTrue(db.items.All(i => i != null), "빈 항목");
            var inProject = AllItemDefinitionsInProject();
            CollectionAssert.AreEquivalent(inProject, db.items,
                "프로젝트의 아이템 정의와 DB가 다르다. 메뉴 " + MenuPath + "를 다시 실행할 것.");
        }

        [Test]
        public void ItemDatabase_IdsUniqueNonEmpty_SortedById()
        {
            var db = Load<ItemDatabaseSO>(ItemDatabasePath);
            string[] ids = db.items.Select(i => i.id).ToArray();
            Assert.IsTrue(ids.All(id => !string.IsNullOrEmpty(id)), "빈 id");
            CollectionAssert.AllItemsAreUnique(ids, "중복 id");
            string[] sorted = ids.ToArray();
            System.Array.Sort(sorted, string.CompareOrdinal);
            CollectionAssert.AreEqual(sorted, ids, "id 순(서수)");
        }

        [Test]
        public void ItemDatabase_ResolvesKnownIds()
        {
            var db = Load<ItemDatabaseSO>(ItemDatabasePath);
            foreach (var (id, path, _) in PriceTable)
            {
                Assert.IsTrue(db.TryGet(id, out ItemDefinitionSO def), id);
                Assert.AreSame(Load<ItemDefinitionSO>(path), def, id);
            }
            foreach (var (id, path) in AmmoDefinitions)
                Assert.IsTrue(db.TryGet(id, out _), $"{id} (탄약 정의도 들어간다 — 무해)");
            Assert.IsFalse(db.TryGet("NOPE-00", out _));
            Assert.IsFalse(db.TryGet("", out _));
        }

        // ---------- 멱등 ----------

        // 메뉴를 다시 실행해도 에셋 파일·.meta(GUID)가 바뀌지 않고 새 에셋도 생기지 않는다.
        [Test]
        public void CreateM5Data_RunAgain_DoesNotChangeAssets()
        {
            string[] paths = PriceTable.Select(p => p.path)
                .Concat(AmmoDefinitions.Select(a => a.path))
                .Concat(new[] { HideoutConfigPath, TraderConfigPath, ItemDatabasePath })
                .ToArray();
            Assume.That(paths.All(p => AssetDatabase.LoadMainAssetAtPath(p) != null),
                $"M5 에셋이 아직 없다. 메뉴 {MenuPath}를 먼저 실행할 것.");

            var before = paths.ToDictionary(p => p, p => File.ReadAllText(Path.GetFullPath(p)));
            var metaBefore = paths.ToDictionary(p => p, p => File.ReadAllText(Path.GetFullPath(p + ".meta")));
            int countBefore = AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length;

            Assert.IsTrue(EditorApplication.ExecuteMenuItem(MenuPath), $"메뉴 없음: {MenuPath}");

            foreach (var pair in before)
                Assert.AreEqual(pair.Value, File.ReadAllText(Path.GetFullPath(pair.Key)), $"재실행으로 바뀜: {pair.Key}");
            foreach (var pair in metaBefore)
                Assert.AreEqual(pair.Value, File.ReadAllText(Path.GetFullPath(pair.Key + ".meta")), $".meta 바뀜: {pair.Key}");
            Assert.AreEqual(countBefore, AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length);
        }
    }
}
