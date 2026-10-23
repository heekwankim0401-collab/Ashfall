using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.EditorTools;
using Ashfall.Hideout;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Layout = Ashfall.EditorTools.M7EnemyLayout;

namespace Ashfall.Tests.EditMode
{
    // M7 7B AI 데이터 = GDD §7(행동·무장) + M7 설계 §3.4·§3.5·§3.9 임시값(TODO(GDD-?)).
    // 에셋은 Ashfall/Setup/Create M7 AI Data(M7AiDataSetup)가 만든다. 없으면 실패한다.
    // 주의(M7 리뷰 7A 낮음 3과 같음): 멱등 테스트는 커밋된 실제 에셋에 WriteM7AiData()를 다시 돌린다(7B 몫만, 대상이 없으면 Assume으로 건너뜀).
    //   멱등이 깨지면 테스트가 커밋 에셋을 고친 채 실패한다 — 테스트 뒤 git status로 확인할 것.
    public class M7AiDataAssetTests
    {
        private const string MenuPath = "Ashfall/Setup/Create M7 AI Data";
        private const string Data = "Assets/_Project/Data";
        private const string Ai = Data + "/AI";
        private const string Loot = Data + "/Loot";
        private const string SniperPath = Ai + "/AI-SNP.asset";
        private const string MutantPath = Ai + "/AI-MUT.asset";
        private const string BossPath = Ai + "/AI-BOS.asset";
        private const string BodyguardPath = Ai + "/AI-BGD.asset";
        private const string ScvPath = Ai + "/AI-SCV.asset";
        private const string RdrPath = Ai + "/AI-RDR.asset";
        private const string AiConfigPath = Ai + "/AiConfig.asset";
        private const string LootSniperPath = Loot + "/LT-SNP.asset";
        private const string LootMutantPath = Loot + "/LT-MUT.asset";
        private const string LootBossPath = Loot + "/LT-BOS.asset";
        private const string LootRdrPath = Loot + "/LT-RDR.asset";
        private const string RelicPath = Data + "/Items/Relics/ITM-REL-01.asset";
        private const string DmrPath = Data + "/Weapons/WPN-DMR-01.asset";
        private const string LmgPath = Data + "/Weapons/WPN-LMG-01.asset";
        private const string RiflePath = Data + "/Weapons/WPN-RIF-01.asset";
        private const string Ammo762Path = Data + "/Weapons/AMMO-762.asset";
        private const string Ammo556Path = Data + "/Weapons/AMMO-556.asset";
        private const string Mag762_5Path = Data + "/Weapons/MAG-762-5.asset";
        private const string Mag762_100Path = Data + "/Weapons/MAG-762-100.asset";
        private const string Mag556Path = Data + "/Weapons/MAG-556-30.asset";
        private const string TraderConfigPath = Data + "/Hideout/TraderConfig.asset";
        private const string ItemDatabasePath = Data + "/Items/ItemDatabase.asset";
        private const string TerrainDataPath = "Assets/_Project/Scenes/Raid_CrimsonBasin/Terrain-CrimsonBasin.asset";
        private const float Eps = 1e-5f;

        // 설계 §3.7 가격 (id, 경로, basePrice). TODO(GDD-?). M5DataAssetTests.Prices_CoverEveryNonAmmoItemDefinition도 쓴다.
        internal static readonly (string id, string path, int price)[] PriceTable =
        {
            ("ITM-REL-01", RelicPath, 8000),
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

        // ---------- 경로 계약 (M7Names) ----------

        [Test]
        public void Paths_MatchM7Names()
        {
            Assert.AreEqual(SniperPath, M7Names.SniperArchetypePath);
            Assert.AreEqual(MutantPath, M7Names.MutantArchetypePath);
            Assert.AreEqual(BossPath, M7Names.BossArchetypePath);
            Assert.AreEqual(BodyguardPath, M7Names.BodyguardArchetypePath);
            Assert.AreEqual(LootSniperPath, M7Names.LootSniperPath);
            Assert.AreEqual(LootMutantPath, M7Names.LootMutantPath);
            Assert.AreEqual(LootBossPath, M7Names.LootBossPath);
            Assert.AreEqual(RelicPath, M7Names.RelicPath);
        }

        // ---------- 적 유형 (§7 + 설계 §3.4) ----------

        [Test]
        public void Sniper_MatchesGddAndDesign()
        {
            var a = Load<EnemyArchetypeSO>(SniperPath);
            Assert.AreEqual("AI-SNP", a.id);
            Assert.AreEqual("저격수", a.displayName, "§7 유형 이름");
            Assert.AreEqual("저격수의 시체", a.corpseDisplayName);
            Assert.AreEqual(EnemyTacticsKind.Sniper, a.tactics);
            Assert.AreEqual(EnemyTacticsKind.Sniper, EnemyBrain.ResolveTacticsKind(a));
            AssertSingleLoadout(a, DmrPath, Ammo762Path, Mag762_5Path);   // §7 볼트액션
            Assert.AreEqual(3, a.spareMagazines);
            Assert.AreSame(Load<LootTableSO>(LootSniperPath), a.loot);

            // TODO(GDD-?) 임시값
            AssertBody(a, 35f, 80f, 65f);
            AssertSenses(a, 150f, 70f, 1f);
            AssertSpeeds(a, 0f, 0f, 0f);   // 고지대 고정 (이동 없음)
            Assert.AreEqual(1.0f, a.reactionTimeSec, Eps);
            Assert.AreEqual(0.05f, a.aimErrorBaseM, Eps);
            Assert.AreEqual(0.004f, a.aimErrorPerMeterM, Eps);
            Assert.AreEqual(2.0f, a.aimSettleSec, Eps);
            Assert.AreEqual(new Vector2(2.0f, 3.5f), a.semiShotIntervalSec);
            Assert.AreEqual(200f, a.engageMaxRangeM, Eps);
            AssertFlags(a, usesCover: false, flanks: false, investigatesNoise: false);
            Assert.AreEqual(0f, a.anchorRadiusM, Eps);
            Assert.AreEqual(new Vector2(50f, 90f), a.weaponDurabilityRange);
            Assert.AreEqual(1.5f, a.telegraphSec, Eps);
            Assert.AreEqual(60f, a.scanArcDeg, Eps);
            Assert.AreEqual(0f, a.meleeDamage, Eps, "근접 없음");
            Assert.AreEqual(M7EnemyLayout.SniperSightRangeM, a.sightRange, Eps, "배치 검사의 저격수 시야 = 에셋 값");
        }

        [Test]
        public void Mutant_MatchesGddAndDesign()
        {
            var a = Load<EnemyArchetypeSO>(MutantPath);
            Assert.AreEqual("AI-MUT", a.id);
            Assert.AreEqual("변이체", a.displayName);
            Assert.AreEqual("변이체 사체", a.corpseDisplayName);
            Assert.AreEqual(EnemyTacticsKind.Mutant, a.tactics);
            Assert.AreEqual(EnemyTacticsKind.Mutant, EnemyBrain.ResolveTacticsKind(a));
            Assert.IsNotNull(a.loadouts);
            Assert.IsEmpty(a.loadouts, "§7 무장 없음");
            Assert.AreEqual(0, a.spareMagazines);
            Assert.AreSame(Load<LootTableSO>(LootMutantPath), a.loot);

            AssertBody(a, 30f, 60f, 50f);
            AssertSenses(a, 40f, 140f, 1.5f);
            AssertSpeeds(a, 1.2f, 4.0f, 5.0f);
            AssertFlags(a, usesCover: false, flanks: false, investigatesNoise: true);
            Assert.AreEqual(0f, a.anchorRadiusM, Eps);
            Assert.AreEqual(14f, a.meleeDamage, Eps);
            Assert.AreEqual(1.8f, a.meleeRangeM, Eps);
            Assert.AreEqual(1.1f, a.meleeIntervalSec, Eps);
            Assert.AreEqual(0.3f, a.meleeWindupSec, Eps);
            Assert.AreEqual(2.5f, a.surroundRadiusM, Eps);
            Assert.AreEqual(0f, a.telegraphSec, Eps);
        }

        [Test]
        public void Boss_MatchesGddAndDesign()
        {
            var a = Load<EnemyArchetypeSO>(BossPath);
            var rdr = Load<EnemyArchetypeSO>(RdrPath);
            Assert.AreEqual("AI-BOS", a.id);
            Assert.AreEqual("재의 군주", a.displayName, "§7 보스 \"재의 군주\"");
            Assert.AreEqual("재의 군주의 시체", a.corpseDisplayName);
            Assert.AreEqual(EnemyTacticsKind.Raider, a.tactics);
            Assert.AreEqual(EnemyTacticsKind.Raider, EnemyBrain.ResolveTacticsKind(a));
            AssertSingleLoadout(a, LmgPath, Ammo762Path, Mag762_100Path);   // §7 경기관총 (적 전용)
            Assert.IsTrue(a.loadouts[0].weapon.enemyOnly);
            Assert.AreEqual(2, a.spareMagazines);
            Assert.AreSame(Load<LootTableSO>(LootBossPath), a.loot);

            AssertBody(a, 50f, 160f, 110f);
            AssertSenses(a, 70f, 110f, 1f);
            AssertSpeeds(a, 1.2f, 2.5f, 3.0f);
            Assert.AreEqual(0.6f, a.reactionTimeSec, Eps);
            Assert.AreEqual(rdr.aimErrorBaseM * 1.3f, a.aimErrorBaseM, Eps, "RDR 오차 × 1.3");
            Assert.AreEqual(rdr.aimErrorPerMeterM * 1.3f, a.aimErrorPerMeterM, Eps);
            Assert.AreEqual(new Vector2Int(8, 15), a.burstRounds);
            Assert.AreEqual(new Vector2(0.6f, 1.2f), a.burstPauseSec);
            Assert.AreEqual(90f, a.engageMaxRangeM, Eps);
            AssertFlags(a, usesCover: true, flanks: false, investigatesNoise: false);
            Assert.AreEqual(25f, a.anchorRadiusM, Eps, "고정 거점 Q29");
            Assert.AreEqual(0f, a.meleeDamage, Eps);
        }

        [Test]
        public void Bodyguard_IsRaiderValuesWithAnchorAndNoFlank()
        {
            var a = Load<EnemyArchetypeSO>(BodyguardPath);
            var rdr = Load<EnemyArchetypeSO>(RdrPath);
            Assert.AreEqual("AI-BGD", a.id);
            Assert.AreEqual("호위병", a.displayName);
            Assert.AreEqual("호위병의 시체", a.corpseDisplayName);
            Assert.AreEqual(EnemyTacticsKind.Raider, a.tactics);
            Assert.AreEqual(EnemyTacticsKind.Raider, EnemyBrain.ResolveTacticsKind(a));
            AssertSingleLoadout(a, RiflePath, Ammo556Path, Mag556Path);   // 소총
            Assert.AreEqual(3, a.spareMagazines);
            Assert.AreSame(Load<LootTableSO>(LootRdrPath), a.loot, "호위는 일반 시체 (LT-RDR)");

            AssertBody(a, 35f, 80f, 65f);
            AssertSenses(a, 60f, 110f, 1f);
            // RDR 값 (Q28)
            AssertSpeeds(a, rdr.patrolSpeed, rdr.moveSpeed, rdr.sprintSpeed);
            Assert.AreEqual(rdr.reactionTimeSec, a.reactionTimeSec, Eps);
            Assert.AreEqual(rdr.aimErrorBaseM, a.aimErrorBaseM, Eps);
            Assert.AreEqual(rdr.aimErrorPerMeterM, a.aimErrorPerMeterM, Eps);
            Assert.AreEqual(rdr.aimSettleSec, a.aimSettleSec, Eps);
            Assert.AreEqual(rdr.unsettledErrorMultiplier, a.unsettledErrorMultiplier, Eps);
            Assert.AreEqual(rdr.burstRounds, a.burstRounds);
            Assert.AreEqual(rdr.burstPauseSec, a.burstPauseSec);
            Assert.AreEqual(rdr.semiShotIntervalSec, a.semiShotIntervalSec);
            Assert.AreEqual(rdr.preferredRangeM, a.preferredRangeM);
            Assert.AreEqual(rdr.engageMaxRangeM, a.engageMaxRangeM, Eps);
            Assert.AreEqual(rdr.peekSec, a.peekSec);
            Assert.AreEqual(rdr.hideSec, a.hideSec);
            Assert.AreEqual(rdr.tacticalReloadBelow, a.tacticalReloadBelow, Eps);
            AssertFlags(a, usesCover: true, flanks: false, investigatesNoise: false);
            Assert.AreEqual(25f, a.anchorRadiusM, Eps, "보스와 같은 거점");
            Assert.AreEqual(new Vector2(35f, 85f), a.weaponDurabilityRange);
        }

        [Test]
        public void AnchorRadius_Is25ForBossAndBodyguardOnly()
        {
            Assert.AreEqual(25f, Load<EnemyArchetypeSO>(BossPath).anchorRadiusM, Eps);
            Assert.AreEqual(25f, Load<EnemyArchetypeSO>(BodyguardPath).anchorRadiusM, Eps);
            foreach (string path in new[] { SniperPath, MutantPath, ScvPath, RdrPath })
                Assert.AreEqual(0f, Load<EnemyArchetypeSO>(path).anchorRadiusM, Eps, path);
        }

        // 기존 유형: 전술은 Auto 그대로(기존 규칙), 스폰 무기 내구도만 35~85 추가 (설계 §3.4)
        [Test]
        public void ExistingScavengerAndRaider_AutoTactics_Durability35To85()
        {
            var scv = Load<EnemyArchetypeSO>(ScvPath);
            var rdr = Load<EnemyArchetypeSO>(RdrPath);
            Assert.AreEqual(EnemyTacticsKind.Auto, scv.tactics);
            Assert.AreEqual(EnemyTacticsKind.Auto, rdr.tactics);
            Assert.AreEqual(EnemyTacticsKind.Scavenger, EnemyBrain.ResolveTacticsKind(scv));
            Assert.AreEqual(EnemyTacticsKind.Raider, EnemyBrain.ResolveTacticsKind(rdr));
            Assert.AreEqual(new Vector2(35f, 85f), scv.weaponDurabilityRange);
            Assert.AreEqual(new Vector2(35f, 85f), rdr.weaponDurabilityRange);
            AssertSerialized(ScvPath, nameof(EnemyArchetypeSO.weaponDurabilityRange));
            AssertSerialized(RdrPath, nameof(EnemyArchetypeSO.weaponDurabilityRange));
        }

        [Test]
        public void ExistingDurability_WriteRule_TreatsDefaultAsUnset()
        {
            Assert.IsTrue(M7AiDataSetup.ShouldWriteExistingDurability(false, new Vector2(60f, 70f)), "파일에 없음");
            Assert.IsTrue(M7AiDataSetup.ShouldWriteExistingDurability(true, new Vector2(100f, 100f)), "초기값 = 미설정");
            Assert.IsFalse(M7AiDataSetup.ShouldWriteExistingDurability(true, new Vector2(35f, 85f)));
            Assert.IsFalse(M7AiDataSetup.ShouldWriteExistingDurability(true, new Vector2(99.9f, 100f)), "조정값 보존");
        }

        [Test]
        public void AiConfig_GlintAndVocalCooldown_Serialized()
        {
            var c = Load<AiConfigSO>(AiConfigPath);
            Assert.AreEqual(0.35f, c.glintAngularSizeDeg, Eps);
            Assert.AreEqual(4f, c.vocalCooldownSec, Eps);
            AssertSerialized(AiConfigPath, nameof(AiConfigSO.glintAngularSizeDeg), nameof(AiConfigSO.vocalCooldownSec));
        }

        // 7B 리뷰 수정 (1·3·5·15): 근접 부위 가중치·높이 차 상한·둘레 비율 — 코드 초기값(임시값)이 에셋에 기록돼 있다
        [Test]
        public void AiConfig_MeleeAndSurroundFields_Serialized()
        {
            var c = Load<AiConfigSO>(AiConfigPath);
            Assert.AreEqual(1f, c.meleePartWeightThorax, Eps);
            Assert.AreEqual(1f, c.meleePartWeightStomach, Eps);
            Assert.AreEqual(0.5f, c.meleePartWeightLeftArm, Eps);
            Assert.AreEqual(0.5f, c.meleePartWeightRightArm, Eps);
            Assert.AreEqual(1.2f, c.meleeMaxHeightDiffM, Eps);
            Assert.AreEqual(0.5f, c.mutantCloseRadiusRatio, Eps);
            Assert.AreEqual(0.75f, c.mutantHoldInsideRangeRatio, Eps);
            AssertSerialized(AiConfigPath, M7AiDataSetup.AiConfigM7Fields);
        }

        // ---------- 유물·루트 (설계 §3.7·§3.9·§3.11, Q31) ----------

        [Test]
        public void Relic_IsRelicRarity_InDatabase_NotSoldByTrader()
        {
            var r = Load<ItemDefinitionSO>(RelicPath);
            Assert.AreEqual("ITM-REL-01", r.id);
            Assert.AreEqual("재의 군주 인장", r.displayName);
            Assert.AreEqual(Rarity.Relic, r.rarity, "§6 유물");
            Assert.AreEqual(typeof(ItemDefinitionSO), r.GetType(), "판매용 일반 아이템");
            Assert.AreEqual(new Vector2Int(1, 1), r.size);
            Assert.AreEqual(0.2f, r.weightKg, Eps);
            Assert.AreEqual(8000, r.basePrice);
            CollectionAssert.Contains(Load<ItemDatabaseSO>(ItemDatabasePath).items, r);
            Assert.IsFalse(Load<TraderConfigSO>(TraderConfigPath).catalog.Contains(r), "상인은 유물을 팔지 않는다");
        }

        [Test]
        public void LootSniper_MatchesDesign()
        {
            var t = Load<LootTableSO>(LootSniperPath);
            Assert.AreEqual("LT-SNP", t.id);
            Assert.AreEqual(new Vector2Int(1, 2), t.rollCount);
            Assert.IsNull(t.guaranteedFirstRoll);
            AssertEntries(t, new[] { ("MAG-762-5", 3f) }.Concat(M7SurvivalDataAssetTests.LootAdditions["LT-SNP"]).ToArray());   // 뒤 = 7C 추가분
        }

        [Test]
        public void LootMutant_MatchesDesign()
        {
            var t = Load<LootTableSO>(LootMutantPath);
            Assert.AreEqual("LT-MUT", t.id);
            Assert.AreEqual(new Vector2Int(0, 2), t.rollCount);
            Assert.IsNull(t.guaranteedFirstRoll);
            AssertEntries(t, new[] { ("JNK-01", 2f), ("JNK-03", 2f) }.Concat(M7SurvivalDataAssetTests.LootAdditions["LT-MUT"]).ToArray());   // 뒤 = 7C 추가분
        }

        [Test]
        public void LootBoss_MatchesDesign_RelicGuaranteedNotWeighted()
        {
            var t = Load<LootTableSO>(LootBossPath);
            Assert.AreEqual("LT-BOS", t.id);
            Assert.AreEqual(new Vector2Int(3, 4), t.rollCount);
            Assert.AreSame(Load<ItemDefinitionSO>(RelicPath), t.guaranteedFirstRoll, "Q31 첫 굴림 고정");
            AssertEntries(t, new[] { ("MAG-762-5", 2f) }.Concat(M7SurvivalDataAssetTests.LootAdditions["LT-BOS"]).ToArray());   // 뒤 = 7C 추가분
            Assert.IsFalse(t.entries.Any(e => e.item != null && e.item.id == "ITM-REL-01"), "유물은 가중치 목록에 없다(정확히 1개)");
        }

        // 보스 시체 굴림: 어떤 시드든 유물이 정확히 1개, 총 개수 = 굴림 3~4
        [Test]
        public void LootBoss_Roll_AlwaysExactlyOneRelic()
        {
            var t = Load<LootTableSO>(LootBossPath);
            var seenCounts = new HashSet<int>();
            for (int seed = 0; seed < 300; seed++)
            {
                var items = new List<ItemInstance>();
                int added = LootRoller.Roll(t, new System.Random(seed), items);
                Assert.AreEqual(added, items.Count);
                Assert.That(added, Is.InRange(3, 4), $"seed {seed}");
                Assert.AreEqual(1, items.Count(i => i.Definition.id == "ITM-REL-01"), $"seed {seed}");
                Assert.AreEqual("ITM-REL-01", items[0].Definition.id, "첫 굴림");
                seenCounts.Add(added);
            }
            CollectionAssert.AreEquivalent(new[] { 3, 4 }, seenCounts);
        }

        [Test]
        public void LootRoller_Guaranteed_ZeroRollsOrNoEntries_StillOne()
        {
            var relic = TestData.Item("REL", "유물", Vector2Int.one, 0.2f, Rarity.Relic);
            var zero = M3TestData.LootTable(new Vector2Int(0, 0), (TestData.Wires(), 1f));
            zero.guaranteedFirstRoll = relic;
            var items = new List<ItemInstance>();
            Assert.AreEqual(1, LootRoller.Roll(zero, new System.Random(1), items));
            Assert.AreSame(relic, items.Single().Definition);

            var empty = M3TestData.LootTable(new Vector2Int(3, 3));
            empty.guaranteedFirstRoll = relic;
            items.Clear();
            Assert.AreEqual(1, LootRoller.Roll(empty, new System.Random(1), items));
            Assert.AreSame(relic, items.Single().Definition);

            var fixedCount = M3TestData.LootTable(new Vector2Int(3, 3), (TestData.Wires(), 1f));
            fixedCount.guaranteedFirstRoll = relic;
            items.Clear();
            Assert.AreEqual(3, LootRoller.Roll(fixedCount, new System.Random(1), items), "굴림 1회를 대신한다");
            Assert.AreEqual(1, items.Count(i => i.Definition == relic));
        }

        // 보장 아이템이 없으면 기존 굴림과 같은 결과 (난수 순서 불변)
        [Test]
        public void LootRoller_NoGuaranteed_SameSequenceAsBefore()
        {
            var a = M3TestData.LootTable(new Vector2Int(1, 4), (TestData.Wires(), 1f), (TestData.Pipe(), 2f));
            Assert.IsNull(a.guaranteedFirstRoll);
            for (int seed = 0; seed < 50; seed++)
            {
                var items = new List<ItemInstance>();
                LootRoller.Roll(a, new System.Random(seed), items);
                // 기존 규칙 직접 재현: Next(min, max+1) → 매 굴림 NextDouble × 합
                var r = new System.Random(seed);
                int rolls = r.Next(1, 5);
                Assert.AreEqual(rolls, items.Count, $"seed {seed}");
                for (int k = 0; k < rolls; k++)
                {
                    double roll = r.NextDouble() * 3.0;
                    string expected = roll < 1.0 ? TestData.Wires().id : TestData.Pipe().id;
                    Assert.AreEqual(expected, items[k].Definition.id, $"seed {seed} #{k}");
                }
            }
        }

        // Q27: 적 전용 LMG·탄띠는 새 루트 테이블·상인 어디에도 없다 (기존 테이블은 M7WeaponDataAssetTests)
        [Test]
        public void EnemyOnlyLmg_NotInAnyLootOrTrader()
        {
            string[] never = { "WPN-LMG-01", "MAG-762-100" };
            List<LootTableSO> tables = AllAssetsInProject<LootTableSO>();
            foreach (string id in new[] { "LT-SNP", "LT-MUT", "LT-BOS" })
                Assert.IsTrue(tables.Any(t => t.id == id), $"{id} 없음");
            foreach (LootTableSO table in tables)
            {
                foreach (string id in never)
                {
                    Assert.IsFalse(table.entries.Any(e => e.item != null && e.item.id == id), $"{table.id}에 {id}");
                    Assert.IsFalse(table.guaranteedFirstRoll != null && table.guaranteedFirstRoll.id == id, $"{table.id} 보장에 {id}");
                }
            }
            var trader = Load<TraderConfigSO>(TraderConfigPath);
            foreach (string id in never)
                Assert.IsFalse(trader.catalog.Any(i => i != null && i.id == id), $"상인 목록에 {id}");
        }

        // ---------- 배치 (설계 §3.5, M7EnemyLayout) ----------

        [Test]
        public void Layout_Counts_14_Total34()
        {
            Assert.IsTrue(Layout.CheckCounts(out string report), report);
            Assert.AreEqual(14, Layout.TotalCount());
            Assert.AreEqual(2, Layout.CountOf(Layout.Kind.Sniper));
            Assert.AreEqual(8, Layout.CountOf(Layout.Kind.Mutant));
            Assert.AreEqual(1, Layout.CountOf(Layout.Kind.Boss));
            Assert.AreEqual(3, Layout.CountOf(Layout.Kind.Bodyguard));
            Assert.AreEqual(34, Layout.ExpectedTotalEnemyCount);
            Assert.AreEqual(CrimsonBasinLayout.ExpectedEnemyCount + Layout.Count, Layout.ExpectedTotalEnemyCount, "20 + 14");
            Assert.AreEqual(20, CrimsonBasinLayout.ExpectedEnemyCount, "M4 고정 배치는 그대로");
            // 변이체 무리 2 × 4
            int[] mutantSquads = Layout.Squads.Where(s => s.Members.All(k => k == Layout.Kind.Mutant)).Select(s => s.Count).ToArray();
            CollectionAssert.AreEqual(new[] { 4, 4 }, mutantSquads);
        }

        [Test]
        public void Layout_Names_HavePrefix_AndAreUnique()
        {
            var names = new List<string>();
            foreach (Layout.SquadPlan plan in Layout.Squads)
            {
                StringAssert.StartsWith(M7Names.M7EnemyPrefix, plan.Name);
                for (int i = 0; i < plan.Count; i++)
                    names.Add(plan.MemberName(i));
            }
            CollectionAssert.AllItemsAreUnique(names);
            foreach (CrimsonBasinLayout.SquadPlan old in CrimsonBasinLayout.Squads)
                Assert.IsFalse(old.Name.StartsWith(M7Names.M7EnemyPrefix), old.Name);
        }

        [Test]
        public void Layout_SpawnDistances_Pass()
        {
            Assert.IsTrue(Layout.CheckSpawnDistances(out string report), report);
            // 저격수 시야는 에셋 값으로도 통과 (시야 150 + 여유 50)
            float sight = Load<EnemyArchetypeSO>(SniperPath).sightRange;
            Assert.IsTrue(Layout.CheckSpawnDistances(sight, out report), report);
            Assert.GreaterOrEqual(Layout.MinDistanceFromSpawnM, 190f);
        }

        [Test]
        public void Layout_SpawnDistances_DetectsTooClose()
        {
            // 검사 함수가 실제로 걸러내는지: 시야를 아주 크게 주면 저격수가 실패한다
            Assert.IsFalse(Layout.CheckSpawnDistances(1000f, out string report));
            StringAssert.Contains("저격수", report);
        }

        [Test]
        public void Layout_InsideMap_SquadIdsUnique_BossGroupInsideAnchor()
        {
            Assert.IsTrue(Layout.CheckInsideMap(out string r1), r1);
            Assert.IsTrue(Layout.CheckSquadIds(out string r2), r2);
            float anchor = Load<EnemyArchetypeSO>(BossPath).anchorRadiusM;
            Assert.IsTrue(Layout.CheckBossGroup(anchor, out string r3), r3);
            Layout.SquadPlan boss = Layout.Squads.Single(s => s.Members.Contains(Layout.Kind.Boss));
            Assert.AreEqual(1, boss.Members.Count(k => k == Layout.Kind.Boss));
            Assert.AreEqual(Layout.Kind.Boss, boss.Members[0], "1번 = 보스");
        }

        [Test]
        public void Layout_Snipers_ArePerchesWithoutRoutes_MutantsPatrol()
        {
            foreach (Layout.SquadPlan plan in Layout.Squads)
            {
                if (plan.Members.Contains(Layout.Kind.Sniper))
                {
                    Assert.IsTrue(plan.IsPerch, plan.Name);
                    Assert.IsNull(plan.RouteXZ, $"{plan.Name}: 저격수는 이동 없음");
                    Assert.AreEqual(1, plan.Count);
                }
                if (plan.Members.Contains(Layout.Kind.Mutant))
                {
                    Assert.IsNotNull(plan.RouteXZ, $"{plan.Name}: 무리 배회");
                    Assert.GreaterOrEqual(plan.RouteXZ.Length, 2);
                }
            }
        }

        [Test]
        public void Layout_PerchHeights_PassOnRaidTerrain()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            Assume.That(data != null, $"레이드 지형이 없다: {TerrainDataPath} (Create M4 Raid Scenes)");
            var ground = Layout.GroundFromTerrainData(data);
            Assert.IsTrue(Layout.CheckPerchHeights(ground, out string report), report);
            float anchor = Load<EnemyArchetypeSO>(BossPath).anchorRadiusM;
            Assert.IsTrue(Layout.CheckAll(ground, Layout.SniperSightRangeM, anchor, out string all), all);
        }

        [Test]
        public void Layout_PerchHeights_FlatGroundFails()
        {
            Assert.IsFalse(Layout.CheckPerchHeights(_ => 15f, out string report), report);
            Assert.IsFalse(Layout.CheckPerchHeights(null, out _));
        }

        // ---------- 멱등 ----------

        // 7B 몫(WriteM7AiData)을 다시 실행해도 에셋 파일·GUID가 바뀌지 않고 새 에셋도 생기지 않는다.
        // 주의: 커밋된 실제 에셋에 데이터 쓰기를 다시 돌린다 — 멱등이 깨지면 테스트가 커밋 에셋을 고친 채 실패한다(테스트 뒤 git status 확인).
        // 범위를 좁히려고 이전 메뉴(M1~M7A CreateData)는 부르지 않고, 대상 에셋이 하나라도 없으면 Assume으로 건너뛴다.
        [Test]
        public void CreateM7AiData_RunAgain_DoesNotChangeAssetsOrGuids()
        {
            string[] paths =
            {
                SniperPath, MutantPath, BossPath, BodyguardPath, ScvPath, RdrPath, AiConfigPath,
                LootSniperPath, LootMutantPath, LootBossPath, RelicPath, ItemDatabasePath,
            };
            Assume.That(paths.All(p => AssetDatabase.LoadMainAssetAtPath(p) != null),
                $"M7 AI 에셋이 아직 없다. 메뉴 {MenuPath}를 먼저 실행할 것.");

            var before = paths.ToDictionary(p => p, p => File.ReadAllBytes(Path.GetFullPath(p)));
            var guids = paths.ToDictionary(p => p, p => AssetDatabase.AssetPathToGUID(p));
            int countBefore = AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length;

            var menu = (MenuItem)System.Attribute.GetCustomAttribute(
                typeof(M7AiDataSetup).GetMethod(nameof(M7AiDataSetup.CreateData)), typeof(MenuItem));
            Assert.AreEqual(MenuPath, menu?.menuItem, "메뉴 경로");

            M7AiDataSetup.WriteM7AiData();

            foreach (var pair in before)
            {
                CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(Path.GetFullPath(pair.Key)), $"재실행으로 바뀜: {pair.Key}");
                Assert.AreEqual(guids[pair.Key], AssetDatabase.AssetPathToGUID(pair.Key), $"GUID 바뀜: {pair.Key}");
            }
            Assert.AreEqual(countBefore, AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length);
        }

        // ---------- 도우미 ----------

        private static void AssertSingleLoadout(EnemyArchetypeSO a, string weaponPath, string ammoPath, string magPath)
        {
            Assert.AreEqual(1, a.loadouts.Count, $"{a.id} 무장 하나");
            EnemyLoadout l = a.loadouts[0];
            Assert.AreSame(Load<WeaponDefinitionSO>(weaponPath), l.weapon);
            Assert.AreSame(Load<AmmoDefinitionSO>(ammoPath), l.ammo);
            Assert.AreSame(Load<MagazineDefinitionSO>(magPath), l.magazine);
            Assert.AreEqual(l.weapon.ammo, l.ammo.type, "탄 종류 일치");
            Assert.AreEqual(l.weapon.ammo, l.magazine.ammo, "탄창 종류 일치");
            Assert.AreEqual(1f, l.weight, Eps);
        }

        private static void AssertBody(EnemyArchetypeSO a, float head, float body, float legs)
        {
            Assert.AreEqual(head, a.headHp, Eps, $"{a.id} 머리");
            Assert.AreEqual(body, a.hp, Eps, $"{a.id} 몸통");
            Assert.AreEqual(legs, a.legsHp, Eps, $"{a.id} 다리");
        }

        private static void AssertSenses(EnemyArchetypeSO a, float range, float angle, float hearing)
        {
            Assert.AreEqual(range, a.sightRange, Eps, $"{a.id} 시야 거리");
            Assert.AreEqual(angle, a.sightAngle, Eps, $"{a.id} 시야 각");
            Assert.AreEqual(hearing, a.hearingMultiplier, Eps, $"{a.id} 청각 배율");
        }

        private static void AssertSpeeds(EnemyArchetypeSO a, float patrol, float move, float sprint)
        {
            Assert.AreEqual(patrol, a.patrolSpeed, Eps, $"{a.id} 순찰");
            Assert.AreEqual(move, a.moveSpeed, Eps, $"{a.id} 이동");
            Assert.AreEqual(sprint, a.sprintSpeed, Eps, $"{a.id} 질주");
        }

        private static void AssertFlags(EnemyArchetypeSO a, bool usesCover, bool flanks, bool investigatesNoise)
        {
            Assert.AreEqual(usesCover, a.usesCover, $"{a.id} usesCover");
            Assert.AreEqual(flanks, a.flanks, $"{a.id} flanks");
            Assert.AreEqual(investigatesNoise, a.investigatesNoise, $"{a.id} investigatesNoise");
        }

        private static void AssertEntries(LootTableSO t, params (string id, float weight)[] expected)
        {
            Assert.AreEqual(expected.Length, t.entries.Count, $"{t.id} 항목 수");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.IsNotNull(t.entries[i].item, $"{t.id}[{i}]");
                Assert.AreEqual(expected[i].id, t.entries[i].item.id, $"{t.id}[{i}]");
                Assert.AreEqual(expected[i].weight, t.entries[i].weight, Eps, $"{t.id}[{i}] 가중치");
            }
        }

        private static void AssertSerialized(string path, params string[] fields)
        {
            string file = File.ReadAllText(Path.GetFullPath(path));
            foreach (string f in fields)
                StringAssert.Contains(f + ":", file, $"{path}에 {f}가 기록되지 않았다");
        }
    }
}
