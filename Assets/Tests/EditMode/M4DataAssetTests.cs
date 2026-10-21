using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M4 데이터 에셋 값 = GDD(RULE-02, §8.2) + M4 설계 임시값(TODO(GDD-?), M4TestData와 같은 값).
    // 에셋은 Ashfall/Setup/Create M4 Data(M4DataSetup)가 만든다. 없으면 실패한다.
    public class M4DataAssetTests
    {
        private const string MenuPath = "Ashfall/Setup/Create M4 Data";
        private const string Data = "Assets/_Project/Data";
        private const string RaidConfigPath = Data + "/Raid/RaidConfig.asset";
        private const string FlarePath = Data + "/Items/Raid/ITM-FLR-01.asset";
        private const string MapItemPath = Data + "/Items/Raid/ITM-MAP-01.asset";
        private const string LootCratePath = Data + "/Loot/LT-CRATE.asset";
        private const string HudConfigPath = Data + "/UI/HudConfig.asset";
        private const string InventoryConfigPath = Data + "/Inventory/InventoryConfig.asset";
        private const string RiflePath = Data + "/Weapons/WPN-RIF-01.asset";
        private const string Mag556Path = Data + "/Weapons/MAG-556-30.asset";
        private const string VestPath = Data + "/Items/Gear/RIG-01.asset";
        private const string BackpackPath = Data + "/Items/Gear/BAG-01.asset";
        private const string BandagePath = Data + "/Items/Medical/MED-BND-01.asset";
        private const string TourniquetPath = Data + "/Items/Medical/MED-TRQ-01.asset";
        private const string SplintPath = Data + "/Items/Medical/MED-SPL-01.asset";
        private const float Eps = 1e-5f;

        // 멱등 검사 대상: M4 메뉴가 만들거나 기록하는 파일 전부
        private static readonly string[] M4AssetPaths =
        {
            RaidConfigPath, FlarePath, MapItemPath, LootCratePath, HudConfigPath,
        };

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"에셋이 없다: {path}. 메뉴 {MenuPath}를 실행할 것.");
            return asset;
        }

        private static ExtractionPointConfig FindExtraction(RaidConfigSO config, string id)
        {
            Assert.IsNotNull(config.extractions, "extractions 없음");
            var matches = config.extractions.Where(e => e.id == id).ToArray();
            Assert.AreEqual(1, matches.Length, $"{id}가 정확히 하나여야 한다");
            return matches[0];
        }

        // ---------- RaidConfig (RULE-02, §8.2) ----------

        [Test]
        public void RaidConfig_TimeLimitIs25Minutes()
        {
            var c = Load<RaidConfigSO>(RaidConfigPath);
            Assert.AreEqual(1500f, c.timeLimitSec, Eps); // RULE-02 25분 = 1500초 (§14 기본값)
        }

        [Test]
        public void RaidConfig_HasExtractions01To04_InOrder()
        {
            var c = Load<RaidConfigSO>(RaidConfigPath);
            CollectionAssert.AreEqual(
                new[] { M4TestData.Ext01, M4TestData.Ext02, M4TestData.Ext03, M4TestData.Ext04 },
                c.extractions.Select(e => e.id).ToArray());
        }

        // §8.2 표: 이름·소요·조건. 반경은 TODO(GDD-?) 임시값(8/8/10/5m).
        [TestCase("EXT-01", "남쪽 도로", 10f, 8f, "", 0f, false, false)]      // 항상 열림, 10초
        [TestCase("EXT-02", "열차 신호소", 15f, 8f, "SW-PWR", 0f, false, false)] // 전원 스위치, 15초
        [TestCase("EXT-03", "헬기 착륙장", 20f, 10f, "", 900f, true, false)]   // 15분 이후, 20초, 신호탄 소모
        [TestCase("EXT-04", "하수 배출구", 7f, 5f, "", 0f, false, true)]       // 배낭 미착용, 7초
        public void RaidConfig_Extraction_MatchesGdd(string id, string name, float waitSec, float radiusM,
            string switchId, float availableAfterSec, bool consumesFlare, bool forbidsBackpack)
        {
            var c = Load<RaidConfigSO>(RaidConfigPath);
            ExtractionPointConfig e = FindExtraction(c, id);
            Assert.AreEqual(name, e.displayName, $"{id} 이름");
            Assert.AreEqual(waitSec, e.waitSec, Eps, $"{id} 소요 (§8.2)");
            Assert.AreEqual(radiusM, e.radiusM, Eps, $"{id} 반경 (TODO(GDD-?))");
            Assert.AreEqual(switchId, e.requiredSwitchId ?? "", $"{id} 스위치");
            Assert.AreEqual(availableAfterSec, e.availableAfterSec, Eps, $"{id} 시간 제한");
            Assert.AreEqual(forbidsBackpack, e.forbidsBackpack, $"{id} 배낭 금지");
            if (consumesFlare)
                Assert.AreSame(Load<ItemDefinitionSO>(FlarePath), e.consumedItem, $"{id} 소모 아이템 = 신호탄");
            else
                Assert.IsNull(e.consumedItem, $"{id} 소모 아이템 없음");
        }

        // 에셋 값이 규칙 테스트의 기준(M4TestData.RaidConfig)과 같다 — 테스트와 실제 데이터가 갈라지지 않게.
        [Test]
        public void RaidConfig_EqualsM4TestData()
        {
            var asset = Load<RaidConfigSO>(RaidConfigPath);
            var flare = Load<ItemDefinitionSO>(FlarePath);
            var expected = M4TestData.RaidConfig(flare);
            try
            {
                Assert.AreEqual(expected.timeLimitSec, asset.timeLimitSec, Eps);
                Assert.AreEqual(expected.extractions.Length, asset.extractions.Length);
                for (int i = 0; i < expected.extractions.Length; i++)
                {
                    ExtractionPointConfig x = expected.extractions[i], a = asset.extractions[i];
                    Assert.AreEqual(x.id, a.id);
                    Assert.AreEqual(x.displayName, a.displayName, x.id);
                    Assert.AreEqual(x.waitSec, a.waitSec, Eps, x.id);
                    Assert.AreEqual(x.radiusM, a.radiusM, Eps, x.id);
                    Assert.AreEqual(x.requiredSwitchId ?? "", a.requiredSwitchId ?? "", x.id);
                    Assert.AreEqual(x.availableAfterSec, a.availableAfterSec, Eps, x.id);
                    Assert.AreSame(x.consumedItem, a.consumedItem, x.id);
                    Assert.AreEqual(x.forbidsBackpack, a.forbidsBackpack, x.id);
                }
                Assert.AreEqual(expected.spawnRules.Length, asset.spawnRules.Length);
                for (int i = 0; i < expected.spawnRules.Length; i++)
                {
                    Assert.AreEqual(expected.spawnRules[i].spawnId, asset.spawnRules[i].spawnId);
                    CollectionAssert.AreEqual(expected.spawnRules[i].activeExtractionIds,
                        asset.spawnRules[i].activeExtractionIds, expected.spawnRules[i].spawnId);
                }
                Assert.AreEqual(expected.randomActiveCount, asset.randomActiveCount);
                CollectionAssert.AreEqual(expected.alwaysActiveIds, asset.alwaysActiveIds);
                Assert.AreEqual(expected.endDelaySec, asset.endDelaySec, Eps);
                Assert.AreEqual(expected.zoneHeightToleranceM, asset.zoneHeightToleranceM, Eps);
            }
            finally
            {
                Object.DestroyImmediate(expected);
            }
        }

        // Q2·Q3: 스폰 3곳, 스폰별 고정 세트 SP-W {01,02,03}, SP-E {01,04,03}, SP-N {01,02,04}
        [TestCase("SP-W", new[] { "EXT-01", "EXT-02", "EXT-03" })]
        [TestCase("SP-E", new[] { "EXT-01", "EXT-04", "EXT-03" })]
        [TestCase("SP-N", new[] { "EXT-01", "EXT-02", "EXT-04" })]
        public void RaidConfig_SpawnRule_MatchesDesign(string spawnId, string[] expected)
        {
            var c = Load<RaidConfigSO>(RaidConfigPath);
            var rules = c.spawnRules.Where(r => r.spawnId == spawnId).ToArray();
            Assert.AreEqual(1, rules.Length, $"{spawnId} 규칙이 정확히 하나여야 한다");
            CollectionAssert.AreEqual(expected, rules[0].activeExtractionIds);
        }

        // §8.2 "2~3개만 활성" + EXT-01 "항상 열림"(Q3) + 모든 id가 실제 탈출구
        [Test]
        public void RaidConfig_SpawnRules_Have2To3ExistingIds_IncludingAlwaysActive()
        {
            var c = Load<RaidConfigSO>(RaidConfigPath);
            Assert.AreEqual(3, c.spawnRules.Length, "스폰 3곳");
            var ids = new HashSet<string>(c.extractions.Select(e => e.id));
            foreach (SpawnRule rule in c.spawnRules)
            {
                Assert.That(rule.activeExtractionIds.Length, Is.InRange(2, 3), rule.spawnId);
                CollectionAssert.AllItemsAreUnique(rule.activeExtractionIds, rule.spawnId);
                foreach (string id in rule.activeExtractionIds)
                    Assert.IsTrue(ids.Contains(id), $"{rule.spawnId}: 없는 탈출구 {id}");
                foreach (string always in c.alwaysActiveIds)
                    CollectionAssert.Contains(rule.activeExtractionIds, always, rule.spawnId);
            }
            CollectionAssert.AllItemsAreUnique(c.spawnRules.Select(r => r.spawnId).ToArray());
        }

        [Test]
        public void RaidConfig_OtherFields_MatchDesign()
        {
            var c = Load<RaidConfigSO>(RaidConfigPath);
            Assert.AreEqual(new Vector2Int(2, 3), c.randomActiveCount); // §8.2 2~3
            CollectionAssert.AreEqual(new[] { "EXT-01" }, c.alwaysActiveIds); // TODO(GDD-?) Q3
            Assert.AreEqual(3f, c.endDelaySec, Eps);                     // TODO(GDD-?) Q15
            Assert.AreEqual(3f, c.zoneHeightToleranceM, Eps);            // TODO(GDD-?) Q8
        }

        // ---------- 아이템 (Q7 신호탄, Q11 지도, 전부 TODO(GDD-?)) ----------

        [Test]
        public void Flare_MatchesDesign()
        {
            var i = Load<ItemDefinitionSO>(FlarePath);
            Assert.AreEqual(typeof(ItemDefinitionSO), i.GetType(), "상태 없는 일반 아이템");
            AssertItem(i, M4TestData.Flare());
            Assert.AreEqual(0, i.basePrice);
        }

        [Test]
        public void MapItem_MatchesDesign()
        {
            var i = Load<ItemDefinitionSO>(MapItemPath);
            Assert.AreEqual(typeof(ItemDefinitionSO), i.GetType(), "상태 없는 일반 아이템");
            AssertItem(i, M4TestData.MapItem());
            Assert.AreEqual(0, i.basePrice);
        }

        // Q11: 지도 화면은 HudConfig.mapItem을 소지했을 때만 열린다.
        [Test]
        public void HudConfig_MapItem_IsMapAsset()
        {
            var hud = Load<ScriptableObject>(HudConfigPath);
            SerializedProperty property = new SerializedObject(hud).FindProperty("mapItem");
            Assert.IsNotNull(property, "HudConfigSO에 mapItem 필드가 없다 (M4 설계 §4.5).");
            Assert.AreSame(Load<ItemDefinitionSO>(MapItemPath), property.objectReferenceValue);
            StringAssert.Contains("mapItem:", File.ReadAllText(Path.GetFullPath(HudConfigPath)), "파일에 기록됨");
        }

        // ---------- 상자 루트 테이블 (Q17, 전부 TODO(GDD-?)) ----------

        [Test]
        public void LootCrate_MatchesDesign()
        {
            var t = Load<LootTableSO>(LootCratePath);
            Assert.AreEqual("LT-CRATE", t.id);
            Assert.AreEqual(new Vector2Int(1, 3), t.rollCount);
            AssertEntries(t, ("MED-BND-01", 3f), ("JNK-03", 3f), ("MED-TRQ-01", 2f), ("MAG-556-30", 2f),
                ("JNK-01", 2f), ("MED-SPL-01", 1f), ("MAG-9MM-12", 1f), ("ARM-HLM-01", 0.5f), ("ITM-FLR-01", 0.5f));
        }

        // Q7-A: 신호탄은 상자 테이블에 낮은 가중치로 들어 있다 (가장 낮은 가중치 이하).
        [Test]
        public void LootCrate_ContainsFlare_WithLowestWeight()
        {
            var t = Load<LootTableSO>(LootCratePath);
            var flare = Load<ItemDefinitionSO>(FlarePath);
            var flareEntries = t.entries.Where(e => e.item == flare).ToArray();
            Assert.AreEqual(1, flareEntries.Length, "신호탄 항목 1개");
            Assert.Greater(flareEntries[0].weight, 0f);
            Assert.LessOrEqual(flareEntries[0].weight, t.entries.Min(e => e.weight));
            Assert.IsFalse(t.entries.Any(e => e.item == Load<ItemDefinitionSO>(MapItemPath)), "지도는 상자에서 나오지 않는다");
        }

        [Test]
        public void LootCrate_RollsWithinRollCount()
        {
            var t = Load<LootTableSO>(LootCratePath);
            Assert.IsTrue(t.entries.All(e => e.item != null), "빈 항목");
            var random = new System.Random(4242);
            var items = new List<ItemInstance>();
            for (int i = 0; i < 100; i++)
            {
                items.Clear();
                int n = LootRoller.Roll(t, random, items);
                Assert.That(n, Is.InRange(t.rollCount.x, t.rollCount.y), "굴림 수");
                Assert.AreEqual(n, items.Count);
                foreach (ItemInstance item in items)
                    Assert.IsTrue(t.entries.Any(e => e.item == item.Definition), "테이블에 없는 아이템");
            }
        }

        // ---------- 시작 키트 (Q13: M2 시작 장비 + 지도) ----------

        // 고정 키트(M2SceneSetup.CreateInventory와 같은 순서)에 지도를 더해도 전부 들어가고,
        // 남은 공간에 신호탄 하나를 주울 수 있다 (EXT-03 경로).
        [Test]
        public void StartKit_WithMap_FitsInventory_AndLeavesRoomForFlare()
        {
            var inventory = new PlayerInventory(Load<InventoryConfigSO>(InventoryConfigPath));

            Assert.IsTrue(inventory.TryEquipNew(ItemFactory.Create(Load<ItemDefinitionSO>(RiflePath)), EquipSlot.Primary1), "소총");
            Assert.IsTrue(inventory.TryEquipNew(ItemFactory.Create(Load<ItemDefinitionSO>(VestPath)), EquipSlot.TacticalVest), "조끼");
            Assert.IsTrue(inventory.TryEquipNew(ItemFactory.Create(Load<ItemDefinitionSO>(BackpackPath)), EquipSlot.Backpack), "배낭");
            foreach (string path in new[] { Mag556Path, Mag556Path, Mag556Path, BandagePath, TourniquetPath, SplintPath, MapItemPath })
                Assert.IsTrue(inventory.TryAutoStore(ItemFactory.Create(Load<ItemDefinitionSO>(path))), $"자동 배치 실패: {path}");

            var map = Load<ItemDefinitionSO>(MapItemPath);
            var flare = Load<ItemDefinitionSO>(FlarePath);
            Assert.IsTrue(inventory.Contains(map), "지도 소지");
            Assert.IsFalse(inventory.Contains(flare), "시작 키트에 신호탄 없음 (Q7)");
            Assert.IsTrue(inventory.TryAutoStore(ItemFactory.Create(flare)), "신호탄 주울 자리");
            Assert.IsTrue(inventory.TryConsume(flare), "신호탄 소모 가능 (EXT-03)");
        }

        // ---------- 멱등 ----------

        // 메뉴를 다시 실행해도 에셋 파일이 바뀌지 않고 새 에셋도 생기지 않는다 (조정한 값 보존).
        [Test]
        public void CreateM4Data_RunAgain_DoesNotChangeAssets()
        {
            Assume.That(M4AssetPaths.All(p => AssetDatabase.LoadMainAssetAtPath(p) != null),
                $"M4 에셋이 아직 없다. 메뉴 {MenuPath}를 먼저 실행할 것.");

            var before = M4AssetPaths.ToDictionary(p => p, p => File.ReadAllText(Path.GetFullPath(p)));
            int countBefore = AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length;

            Assert.IsTrue(EditorApplication.ExecuteMenuItem(MenuPath), $"메뉴 없음: {MenuPath}");

            foreach (var pair in before)
                Assert.AreEqual(pair.Value, File.ReadAllText(Path.GetFullPath(pair.Key)), $"재실행으로 바뀜: {pair.Key}");
            Assert.AreEqual(countBefore, AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length);
        }

        // ---------- 도우미 ----------

        private static void AssertItem(ItemDefinitionSO actual, ItemDefinitionSO expected)
        {
            try
            {
                Assert.AreEqual(expected.id, actual.id);
                Assert.AreEqual(expected.displayName, actual.displayName, expected.id);
                Assert.AreEqual(expected.size, actual.size, $"{expected.id} 크기");
                Assert.AreEqual(expected.weightKg, actual.weightKg, Eps, $"{expected.id} 무게");
                Assert.AreEqual(expected.rarity, actual.rarity, $"{expected.id} 희귀도");
            }
            finally
            {
                Object.DestroyImmediate(expected);
            }
        }

        private static void AssertEntries(LootTableSO t, params (string id, float weight)[] expected)
        {
            Assert.AreEqual(expected.Length, t.entries.Count, $"{t.id} 항목 수");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.IsNotNull(t.entries[i].item, $"{t.id}[{i}] 아이템 없음");
                Assert.AreEqual(expected[i].id, t.entries[i].item.id, $"{t.id}[{i}]");
                Assert.AreEqual(expected[i].weight, t.entries[i].weight, Eps, $"{t.id}[{i}] 가중치");
            }
        }
    }
}
