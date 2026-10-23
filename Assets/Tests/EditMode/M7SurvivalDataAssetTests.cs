using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.EditorTools;
using Ashfall.Hideout;
using Ashfall.Player;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7C 생존·제작 데이터 = GDD §4.3(생존 수치) + M7 설계 §3.6~§3.11 임시값(TODO(GDD-?)).
    // 에셋은 Ashfall/Setup/Create M7 Survival Data(M7SurvivalDataSetup)가 만든다. 없으면 실패한다.
    // 주의(M7 리뷰 7A 낮음 3과 같음): 멱등 테스트는 커밋된 실제 에셋에 WriteM7SurvivalData()를 다시 돌린다(7C 몫만, 대상이 없으면 Assume으로 건너뜀).
    //   멱등이 깨지면 테스트가 커밋 에셋(아이템·PlayerConfig·RaidConfig·상인·루트 테이블·DB)을 고친 채 실패한다 — 테스트 뒤 git status로 확인할 것.
    public class M7SurvivalDataAssetTests
    {
        private const string MenuPath = "Ashfall/Setup/Create M7 Survival Data";
        private const string Data = "Assets/_Project/Data";
        private const string Medical = Data + "/Items/Medical";
        private const string WaterPath = Medical + "/ITM-WTR-01.asset";
        private const string FoodPath = Medical + "/ITM-FOD-01.asset";
        private const string AntiRadPath = Medical + "/MED-ANT-01.asset";
        private const string GasMaskPath = Data + "/Items/Armor/GEAR-GSM-01.asset";
        private const string CraftingConfigPath = Data + "/Hideout/CraftingConfig.asset";
        private const string TraderConfigPath = Data + "/Hideout/TraderConfig.asset";
        private const string PlayerConfigPath = Data + "/Player/PlayerConfig.asset";
        private const string RaidConfigPath = Data + "/Raid/RaidConfig.asset";
        private const string ItemDatabasePath = Data + "/Items/ItemDatabase.asset";
        private const string Loot = Data + "/Loot";
        private const float Eps = 1e-5f;

        // 설계 §3.7 가격 (id, 경로, basePrice). TODO(GDD-?). M5DataAssetTests.Prices_CoverEveryNonAmmoItemDefinition도 쓴다.
        internal static readonly (string id, string path, int price)[] PriceTable =
        {
            ("ITM-WTR-01", WaterPath, 150),
            ("ITM-FOD-01", FoodPath, 200),
            ("MED-ANT-01", AntiRadPath, 600),
            ("GEAR-GSM-01", GasMaskPath, 1500),
        };

        // 상인 판매 목록 추가분 (M5 + 7A 목록 뒤 이 순서). M5DataAssetTests·M7WeaponDataAssetTests도 쓴다.
        internal static readonly string[] TraderAdditionIds = { "ITM-WTR-01", "ITM-FOD-01", "MED-ANT-01", "GEAR-GSM-01" };

        // 설계 §3.9 루트 테이블 추가분 (표마다 앞 단계 항목 뒤 이 순서). M3·M4·M7AiDataAssetTests도 쓴다.
        // LT-CRATE 방독면은 설계 0.3 → 0.5 (M4 Q7-A: 신호탄 0.5가 최저 가중치 — 7C-1b 구현 메모).
        internal static readonly Dictionary<string, (string id, float weight)[]> LootAdditions = new Dictionary<string, (string id, float weight)[]>
        {
            ["LT-CRATE"] = new[] { ("ITM-WTR-01", 2f), ("ITM-FOD-01", 2f), ("MED-ANT-01", 0.5f), ("GEAR-GSM-01", 0.5f), ("JNK-02", 0.5f) },
            ["LT-SCV"] = new[] { ("ITM-WTR-01", 1f), ("ITM-FOD-01", 1f), ("BOX-12GA-8", 0.5f) },
            ["LT-RDR"] = new[] { ("ITM-WTR-01", 1f), ("ITM-FOD-01", 1f) },
            ["LT-SNP"] = new[] { ("MED-ANT-01", 1f), ("ITM-WTR-01", 1f) },
            ["LT-MUT"] = new[] { ("MED-ANT-01", 0.3f) },
            ["LT-BOS"] = new[] { ("GEAR-GSM-01", 1f), ("MED-ANT-01", 1f) },
        };

        // 실제 희귀도 = 설계 §3.11 (TODO(GDD-?) Q48). 단 WPN-PST-01·WPN-SMG-01·ITM-FLR-01은 M3·M4가 Common으로 만들어
        // "Scrap = 미설정" 규칙에 따라 보존된다(§3.11은 우수) — 7C-1b 구현 메모. 탄약 정의·내장 튜브·적 전용 = Scrap.
        private static readonly (string id, Rarity rarity)[] ExpectedRarities =
        {
            ("JNK-01", Rarity.Scrap), ("JNK-02", Rarity.Scrap), ("JNK-03", Rarity.Scrap),
            ("AMMO-9MM", Rarity.Scrap), ("AMMO-556", Rarity.Scrap), ("AMMO-762", Rarity.Scrap), ("AMMO-12GA", Rarity.Scrap),
            ("MAG-12GA-TUBE", Rarity.Scrap), ("MAG-762-100", Rarity.Scrap), ("WPN-LMG-01", Rarity.Scrap),
            ("MAG-9MM-12", Rarity.Common), ("MAG-9MM-30", Rarity.Common), ("BOX-12GA-8", Rarity.Common), ("MED-BND-01", Rarity.Common),
            ("ITM-WTR-01", Rarity.Common), ("ITM-FOD-01", Rarity.Common), ("ITM-MAP-01", Rarity.Common), ("RIG-01", Rarity.Common),
            ("WPN-PST-01", Rarity.Common), ("WPN-SMG-01", Rarity.Common), ("ITM-FLR-01", Rarity.Common),   // 기존 값 보존 (§3.11 우수)
            ("WPN-SHT-01", Rarity.Uncommon), ("MAG-556-30", Rarity.Uncommon), ("MAG-762-5", Rarity.Uncommon),
            ("MED-TRQ-01", Rarity.Uncommon), ("MED-SPL-01", Rarity.Uncommon), ("BAG-01", Rarity.Uncommon), ("ARM-HLM-01", Rarity.Uncommon),
            ("MED-ANT-01", Rarity.Uncommon), ("GEAR-GSM-01", Rarity.Uncommon),
            ("WPN-RIF-01", Rarity.Rare), ("WPN-DMR-01", Rarity.Rare), ("ARM-BDY-01", Rarity.Rare),
            ("ITM-REL-01", Rarity.Relic),
        };

        // 설계 §3.8 레시피 (id, 산출 id, 산출 수, 재료). RCP-AMMO-12GA·RCP-MED-SPL은 설계 JNK-01 × 1 → × 2 (가치 확인, 7C-1b 구현 메모).
        private static readonly (string id, string output, int count, (string id, int count)[] inputs)[] ExpectedRecipes =
        {
            ("RCP-AMMO-9MM12", "MAG-9MM-12", 1, new[] { ("JNK-01", 1), ("JNK-03", 1) }),
            ("RCP-AMMO-556", "MAG-556-30", 1, new[] { ("JNK-01", 2), ("JNK-03", 1) }),
            ("RCP-AMMO-762", "MAG-762-5", 1, new[] { ("JNK-01", 1), ("JNK-02", 1) }),
            ("RCP-AMMO-12GA", "BOX-12GA-8", 1, new[] { ("JNK-01", 2) }),
            ("RCP-MED-BND", "MED-BND-01", 2, new[] { ("JNK-03", 1) }),
            ("RCP-MED-TRQ", "MED-TRQ-01", 1, new[] { ("JNK-03", 2) }),
            ("RCP-MED-SPL", "MED-SPL-01", 1, new[] { ("JNK-01", 2) }),
            ("RCP-MED-ANT", "MED-ANT-01", 1, new[] { ("JNK-02", 1), ("JNK-03", 1) }),
        };

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"에셋이 없다: {path}. 메뉴 {MenuPath}를 실행할 것.");
            return asset;
        }

        private static List<T> AllAssetsInProject<T>() where T : Object
        {
            var result = new List<T>();
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }).Distinct())
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                    if (asset is T t)
                        result.Add(t);
            return result;
        }

        private static ItemDefinitionSO ById(string id)
        {
            var db = Load<ItemDatabaseSO>(ItemDatabasePath);
            Assert.IsTrue(db.TryGet(id, out ItemDefinitionSO def), $"ItemDatabase에 {id}가 없다");
            return def;
        }

        // ---------- 경로 계약 (M7Names) ----------

        [Test]
        public void Paths_MatchM7Names()
        {
            Assert.AreEqual(WaterPath, M7Names.WaterPath);
            Assert.AreEqual(FoodPath, M7Names.FoodPath);
            Assert.AreEqual(AntiRadPath, M7Names.AntiRadPath);
            Assert.AreEqual(GasMaskPath, M7Names.GasMaskPath);
            Assert.AreEqual(CraftingConfigPath, M7Names.CraftingConfigPath);
            Assert.AreEqual(RaidConfigPath, M7Names.RaidConfigPath);
            var menu = (MenuItem)System.Attribute.GetCustomAttribute(
                typeof(M7SurvivalDataSetup).GetMethod(nameof(M7SurvivalDataSetup.CreateData)), typeof(MenuItem));
            Assert.AreEqual(MenuPath, menu?.menuItem, "메뉴 경로");
        }

        // ---------- ① 새 아이템 (§3.7, Q42) ----------

        [TestCase("ITM-WTR-01", WaterPath, "물병", 1, 2, 0.6f, 50f, 0f, false, 3f, 2)]
        [TestCase("ITM-FOD-01", FoodPath, "통조림", 1, 1, 0.4f, 0f, 60f, false, 4f, 1)]
        [TestCase("MED-ANT-01", AntiRadPath, "해독제", 1, 1, 0.1f, 0f, 0f, true, 3f, 1)]
        public void SurvivalMedicals_MatchDesign(string id, string path, string name, int w, int h, float kg,
            float hydration, float energy, bool curesRadiation, float useTime, int uses)
        {
            var m = Load<MedicalDefinitionSO>(path);
            Assert.AreEqual(id, m.id);
            Assert.AreEqual(name, m.displayName);
            Assert.AreEqual(new Vector2Int(w, h), m.size);
            Assert.AreEqual(kg, m.weightKg, Eps);
            Assert.AreEqual(hydration, m.hydrationRestore, Eps, "수분 회복");
            Assert.AreEqual(energy, m.energyRestore, Eps, "에너지 회복");
            Assert.AreEqual(curesRadiation, m.curesRadiation, "방사능 해제 (Q36)");
            CollectionAssert.IsEmpty(m.cures, "부위 상태이상은 해제하지 않는다");
            Assert.AreEqual(0f, m.healAmount, Eps, "HP 회복 없음");
            Assert.AreEqual(useTime, m.useTime, Eps);
            Assert.AreEqual(uses, m.uses);
        }

        [Test]
        public void GasMask_IsHelmetSlot_ClassZero_NoCover_BlocksStormRadiation()
        {
            var a = Load<ArmorDefinitionSO>(GasMaskPath);
            Assert.AreEqual(typeof(ArmorDefinitionSO), a.GetType());
            Assert.AreEqual("GEAR-GSM-01", a.id);
            Assert.AreEqual("방독면", a.displayName);
            Assert.AreEqual(ArmorKind.Helmet, a.kind, "헬멧 슬롯 공유 (Q37)");
            Assert.AreEqual(0, a.armorClass, "방어 기능 없음");
            CollectionAssert.IsEmpty(a.covers);
            Assert.IsTrue(a.blocksStormRadiation, "§8.3 방독면으로 무효 (Q38)");
            Assert.AreEqual(new Vector2Int(2, 2), a.size);
            Assert.AreEqual(0.8f, a.weightKg, Eps);
            // 다른 방어구는 폭풍을 막지 않는다
            foreach (ArmorDefinitionSO other in AllAssetsInProject<ArmorDefinitionSO>().Where(x => x != a))
                Assert.IsFalse(other.blocksStormRadiation, other.id);
        }

        [Test]
        public void Prices_MatchDesign()
        {
            foreach (var (id, path, price) in PriceTable)
            {
                var item = Load<ItemDefinitionSO>(path);
                Assert.AreEqual(id, item.id, path);
                Assert.AreEqual(price, item.basePrice, $"{id} basePrice (TODO(GDD-?))");
            }
        }

        [Test]
        public void ItemDatabase_ResolvesNewIds()
        {
            var db = Load<ItemDatabaseSO>(ItemDatabasePath);
            foreach (var (id, path, _) in PriceTable)
            {
                Assert.IsTrue(db.TryGet(id, out ItemDefinitionSO def), id);
                Assert.AreSame(Load<ItemDefinitionSO>(path), def, id);
            }
        }

        // ---------- ② 희귀도 (§3.11, Q48) ----------

        [Test]
        public void Rarity_MatchesTable_ForEveryItemDefinition()
        {
            var expected = ExpectedRarities.ToDictionary(e => e.id, e => e.rarity);
            List<ItemDefinitionSO> all = AllAssetsInProject<ItemDefinitionSO>();
            var missing = all.Where(i => !expected.ContainsKey(i.id)).Select(i => i.id).ToArray();
            CollectionAssert.IsEmpty(missing, "희귀도 표에 없는 아이템 (새 아이템이면 표에 추가)");
            foreach (ItemDefinitionSO item in all)
                Assert.AreEqual(expected[item.id], item.rarity, $"{item.id} 희귀도");
        }

        [Test]
        public void Rarity_ScrapItemsAreJunkOrNonInventory()
        {
            // 고철 = JNK-01~03(Q48). 그 밖의 Scrap은 상인·루트에 나오지 않는 정의(탄약·튜브·적 전용)뿐.
            string[] scrapSellable = AllAssetsInProject<ItemDefinitionSO>()
                .Where(i => i.rarity == Rarity.Scrap && i.basePrice > 0).Select(i => i.id).OrderBy(i => i, System.StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(new[] { "JNK-01", "JNK-02", "JNK-03" }, scrapSellable);
        }

        [TestCase(Rarity.Scrap, Rarity.Rare, true)]
        [TestCase(Rarity.Scrap, Rarity.Scrap, false)]
        [TestCase(Rarity.Common, Rarity.Uncommon, false)]
        [TestCase(Rarity.Rare, Rarity.Common, false)]
        public void Rarity_WriteRule_OnlyFromScrap(Rarity current, Rarity target, bool expected)
        {
            Assert.AreEqual(expected, M7SurvivalDataSetup.ShouldWriteRarity(current, target));
        }

        // ---------- ③ PlayerConfig (§3.6, GDD §4.3) ----------

        [Test]
        public void PlayerConfig_SurvivalFields_GddValues_WrittenToFile()
        {
            var c = Load<PlayerConfigSO>(PlayerConfigPath);
            // GDD §4.3
            Assert.AreEqual(100f, c.hydrationMax, Eps, "수분 100");
            Assert.AreEqual(0.1f, c.hydrationDecayPerSec, Eps, "−0.1/s");
            Assert.AreEqual(0.5f, c.dehydratedHpPerSec, Eps, "수분 0 → −0.5 HP/s");
            Assert.AreEqual(100f, c.energyMax, Eps, "에너지 100");
            Assert.AreEqual(0.07f, c.energyDecayPerSec, Eps, "−0.07/s");
            Assert.AreEqual(0.5f, c.starvingStaminaRegenMultiplier, Eps, "스태미나 재생 −50%");
            Assert.AreEqual(100f, c.radiationSicknessDose, Eps, "누적 100");
            Assert.AreEqual(0.8f, c.radiationSicknessMaxHpMultiplier, Eps, "최대 HP −20%");
            // TODO(GDD-?) 설계 임시값
            Assert.AreEqual(2f, c.stomachDestroyedHydrationMultiplier, Eps, "Q34");
            Assert.AreEqual(50f, c.radiationWarnDose, Eps, "Q43");
            Assert.AreEqual(0.25f, c.survivalLowRatio, Eps, "Q43");

            string[] fields =
            {
                nameof(PlayerConfigSO.hydrationMax), nameof(PlayerConfigSO.hydrationDecayPerSec), nameof(PlayerConfigSO.dehydratedHpPerSec),
                nameof(PlayerConfigSO.stomachDestroyedHydrationMultiplier), nameof(PlayerConfigSO.energyMax), nameof(PlayerConfigSO.energyDecayPerSec),
                nameof(PlayerConfigSO.starvingStaminaRegenMultiplier), nameof(PlayerConfigSO.radiationSicknessDose),
                nameof(PlayerConfigSO.radiationSicknessMaxHpMultiplier), nameof(PlayerConfigSO.radiationWarnDose), nameof(PlayerConfigSO.survivalLowRatio),
            };
            foreach (string field in fields)
                Assert.IsTrue(SetupUtil.IsFieldSerialized(PlayerConfigPath, field), $"{field}가 파일에 기록되지 않음");
        }

        // ---------- ④ 방사능 구역 (§3.10, Q39) ----------

        [Test]
        public void RaidConfig_RefineryZone_MatchesDesign()
        {
            var raid = Load<RaidConfigSO>(RaidConfigPath);
            Assert.IsNotNull(raid.radiationZones);
            Assert.AreEqual(1, raid.radiationZones.Count(z => z.id == "RAD-REF"), "RAD-REF 하나");
            RadiationZoneConfig zone = raid.radiationZones.First(z => z.id == "RAD-REF");
            Assert.AreEqual(new Vector2(300f, -60f), zone.centerXZ, "POI-REF 중심");
            Assert.AreEqual(CrimsonBasinLayout.Refinery.CenterXZ, zone.centerXZ);
            Assert.AreEqual(70f, zone.radiusM, Eps);
            Assert.AreEqual(0.5f, zone.ratePerSec, Eps);
            Assert.AreEqual(15f, zone.edgeFalloffM, Eps);
            Assert.IsTrue(SetupUtil.IsFieldSerialized(RaidConfigPath, nameof(RaidConfigSO.radiationZones)));

            // 중심 = 최대, 반경 밖 = 0, 가장자리 중간 = 절반
            Assert.AreEqual(0.5f, RadiationZoneMath.RateAt(raid.radiationZones, new Vector3(300f, 12f, -60f)), Eps);
            Assert.AreEqual(0f, RadiationZoneMath.RateAt(raid.radiationZones, new Vector3(300f + 71f, 0f, -60f)), Eps);
            Assert.AreEqual(0.25f, RadiationZoneMath.RateAt(raid.radiationZones, new Vector3(300f, 0f, -60f + 62.5f)), Eps);
        }

        // 구역은 스폰 지점과 겹치지 않는다 (레이드 시작부터 피폭되지 않게)
        [Test]
        public void RefineryZone_DoesNotCoverSpawns()
        {
            var raid = Load<RaidConfigSO>(RaidConfigPath);
            foreach (Vector2 spawn in new[] { new Vector2(-460f, -150f), new Vector2(460f, 120f), new Vector2(-80f, 470f) })
                Assert.AreEqual(0f, RadiationZoneMath.RateAt(raid.radiationZones, new Vector3(spawn.x, 0f, spawn.y)), Eps, spawn.ToString());
        }

        // ---------- ⑤ 제작 (§3.8, Q46·Q47) ----------

        [Test]
        public void CraftingConfig_Recipes_MatchDesign()
        {
            var c = Load<CraftingConfigSO>(CraftingConfigPath);
            Assert.IsNotNull(c.recipes);
            CollectionAssert.AreEqual(ExpectedRecipes.Select(r => r.id).ToArray(), c.recipes.Select(r => r.id).ToArray(), "레시피 순서");
            foreach (var (id, output, count, inputs) in ExpectedRecipes)
            {
                CraftingRecipe r = c.recipes.First(x => x.id == id);
                Assert.IsFalse(string.IsNullOrEmpty(r.displayName), $"{id} 이름");
                Assert.IsNotNull(r.output, $"{id} 산출");
                Assert.AreEqual(output, r.output.id, $"{id} 산출");
                Assert.AreSame(ById(output), r.output, $"{id} 산출 실제 에셋");
                Assert.AreEqual(count, r.outputCount, $"{id} 산출 수");
                Assert.AreEqual(inputs.Length, r.inputs.Length, $"{id} 재료 수");
                for (int i = 0; i < inputs.Length; i++)
                {
                    Assert.IsNotNull(r.inputs[i].item, $"{id} 재료 {i}");
                    Assert.AreEqual(inputs[i].id, r.inputs[i].item.id, $"{id} 재료 {i}");
                    Assert.AreSame(ById(inputs[i].id), r.inputs[i].item, $"{id} 재료 {i} 실제 에셋");
                    Assert.AreEqual(inputs[i].count, r.inputs[i].count, $"{id} 재료 {i} 개수");
                }
            }
        }

        // 재료 = 고철(JNK-*)만, 산출 = 탄창·탄 상자·의약품(Q46 — 무기 없음, 내장 튜브·적 전용 없음)
        [Test]
        public void CraftingConfig_InputsAreScrap_OutputsAreAmmoOrMedical()
        {
            var c = Load<CraftingConfigSO>(CraftingConfigPath);
            foreach (CraftingRecipe r in c.recipes)
            {
                foreach (CraftingIngredient input in r.inputs)
                {
                    Assert.AreEqual(Rarity.Scrap, input.item.rarity, $"{r.id}: {input.item.id}");
                    StringAssert.StartsWith("JNK-", input.item.id, r.id);
                    Assert.GreaterOrEqual(input.count, 1, r.id);
                }
                Assert.IsTrue(r.output is MagazineDefinitionSO || r.output is MedicalDefinitionSO, $"{r.id}: {r.output.id}");
                Assert.IsFalse(r.output is WeaponDefinitionSO, r.id);
                Assert.Greater(r.output.basePrice, 0, $"{r.id}: 팔 수 있는 산출 (적 전용·튜브 아님)");
                Assert.GreaterOrEqual(r.outputCount, 1, r.id);
            }
        }

        // 설계 §3.8 가치 확인: 산출 매도 합 ≤ 재료 매수 합 (제작이 탄피 생성기가 되지 않게)
        [Test]
        public void CraftingConfig_OutputSellValue_AtMostInputBuyValue()
        {
            var c = Load<CraftingConfigSO>(CraftingConfigPath);
            var trader = Load<TraderConfigSO>(TraderConfigPath);
            foreach (CraftingRecipe r in c.recipes)
            {
                int sell = PriceRules.SellPrice(r.output, trader) * r.outputCount;
                int buy = 0;
                foreach (CraftingIngredient input in r.inputs)
                {
                    int price = PriceRules.BuyPrice(input.item, trader);
                    Assert.Greater(price, 0, $"{r.id}: {input.item.id} 매수가");
                    buy += price * input.count;
                }
                Assert.LessOrEqual(sell, buy, $"{r.id}: 산출 매도 {sell} > 재료 매수 {buy}");
            }
        }

        // ---------- ⑥ 상인 ----------

        [Test]
        public void Trader_HasSurvivalAdditions_AtEnd_AndAmmoBox()
        {
            var t = Load<TraderConfigSO>(TraderConfigPath);
            Assert.IsTrue(t.catalog.All(i => i != null), "빈 항목");
            string[] ids = t.catalog.Select(i => i.id).ToArray();
            CollectionAssert.AreEqual(TraderAdditionIds, ids.Skip(ids.Length - TraderAdditionIds.Length).ToArray(), "7C 추가분이 끝에 이 순서로");
            CollectionAssert.AllItemsAreUnique(t.catalog);
            foreach (var (id, path, _) in PriceTable)
            {
                Assert.AreSame(Load<ItemDefinitionSO>(path), t.catalog.First(i => i.id == id), id);
                Assert.Greater(PriceRules.BuyPrice(t.catalog.First(i => i.id == id), t), 0, id);
            }
            Assert.AreEqual(1, ids.Count(i => i == "BOX-12GA-8"), "12ga 탄 상자 (7A Q20)");
        }

        // ---------- ⑦ 루트 테이블 (§3.9) ----------

        [Test]
        public void LootTables_EndWithSurvivalAdditions()
        {
            foreach (var pair in LootAdditions)
            {
                var t = Load<LootTableSO>($"{Loot}/{pair.Key}.asset");
                Assert.AreEqual(pair.Key, t.id);
                Assert.IsTrue(t.entries.All(e => e.item != null), $"{t.id} 빈 항목");
                var tail = t.entries.Skip(t.entries.Count - pair.Value.Length).ToArray();
                Assert.AreEqual(pair.Value.Length, tail.Length, t.id);
                for (int i = 0; i < pair.Value.Length; i++)
                {
                    Assert.AreEqual(pair.Value[i].id, tail[i].item.id, $"{t.id} 끝 {i}");
                    Assert.AreEqual(pair.Value[i].weight, tail[i].weight, Eps, $"{t.id} {pair.Value[i].id} 가중치 (TODO(GDD-?))");
                    Assert.AreSame(ById(pair.Value[i].id), tail[i].item, $"{t.id} {pair.Value[i].id} 실제 에셋");
                    Assert.AreEqual(1, t.entries.Count(e => e.item == tail[i].item), $"{t.id}에 {pair.Value[i].id} 하나");
                }
            }
        }

        // 보스 루트: 방독면·해독제가 붙어도 유물은 여전히 가중치 목록 밖(첫 굴림 고정, Q31)
        [Test]
        public void LootBoss_RelicStillGuaranteedOnly()
        {
            var t = Load<LootTableSO>($"{Loot}/LT-BOS.asset");
            Assert.AreSame(ById("ITM-REL-01"), t.guaranteedFirstRoll);
            Assert.IsFalse(t.entries.Any(e => e.item != null && e.item.id == "ITM-REL-01"));
        }

        // ---------- 멱등 ----------

        // 7C 몫(WriteM7SurvivalData)을 다시 실행해도 에셋 파일·GUID가 바뀌지 않고 새 에셋도 생기지 않는다.
        // 주의: 커밋된 실제 에셋에 데이터 쓰기를 다시 돌린다 — 멱등이 깨지면 테스트가 커밋 에셋을 고친 채 실패한다(테스트 뒤 git status 확인).
        // 범위를 좁히려고 이전 메뉴(M1~M7B CreateData)는 부르지 않고, 대상 에셋이 하나라도 없으면 Assume으로 건너뛴다.
        [Test]
        public void CreateM7SurvivalData_RunAgain_DoesNotChangeAssetsOrGuids()
        {
            string[] paths = PriceTable.Select(p => p.path)
                .Concat(M7SurvivalDataSetup.RarityTable.Select(r => r.path))
                .Concat(LootAdditions.Keys.Select(id => $"{Loot}/{id}.asset"))
                .Concat(new[] { CraftingConfigPath, TraderConfigPath, PlayerConfigPath, RaidConfigPath, ItemDatabasePath })
                .Distinct()
                .ToArray();
            Assume.That(paths.All(p => AssetDatabase.LoadMainAssetAtPath(p) != null),
                $"M7 생존 에셋이 아직 없다. 메뉴 {MenuPath}를 먼저 실행할 것.");

            var before = paths.ToDictionary(p => p, p => File.ReadAllBytes(Path.GetFullPath(p)));
            var guids = paths.ToDictionary(p => p, p => AssetDatabase.AssetPathToGUID(p));
            int countBefore = AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length;

            M7SurvivalDataSetup.WriteM7SurvivalData();

            foreach (var pair in before)
            {
                CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(Path.GetFullPath(pair.Key)), $"재실행으로 바뀜: {pair.Key}");
                Assert.AreEqual(guids[pair.Key], AssetDatabase.AssetPathToGUID(pair.Key), $"GUID 바뀜: {pair.Key}");
            }
            Assert.AreEqual(countBefore, AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length);
        }
    }
}
