using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M3 데이터 에셋 값 = GDD 표(§5.2, §7, §4.2) + M3 설계 §4 임시값(TODO(GDD-?)).
    // 에셋은 Ashfall/Setup/Create M3 Data(M3DataSetup)가 만든다. 없으면 실패한다.
    public class M3DataAssetTests
    {
        private const string MenuPath = "Ashfall/Setup/Create M3 Data";
        private const string Data = "Assets/_Project/Data";
        private const string Weapons = Data + "/Weapons";
        private const string RiflePath = Weapons + "/WPN-RIF-01.asset";
        private const string Ammo556Path = Weapons + "/AMMO-556.asset";
        private const string Mag556Path = Weapons + "/MAG-556-30.asset";
        private const string PistolPath = Weapons + "/WPN-PST-01.asset";
        private const string SmgPath = Weapons + "/WPN-SMG-01.asset";
        private const string Ammo9Path = Weapons + "/AMMO-9MM.asset";
        private const string Mag12Path = Weapons + "/MAG-9MM-12.asset";
        private const string Mag30Path = Weapons + "/MAG-9MM-30.asset";
        private const string LootScvPath = Data + "/Loot/LT-SCV.asset";
        private const string LootRdrPath = Data + "/Loot/LT-RDR.asset";
        private const string ScvPath = Data + "/AI/AI-SCV.asset";
        private const string RdrPath = Data + "/AI/AI-RDR.asset";
        private const string AiConfigPath = Data + "/AI/AiConfig.asset";
        private const string PlayerConfigPath = Data + "/Player/PlayerConfig.asset";
        private const string InventoryConfigPath = Data + "/Inventory/InventoryConfig.asset";
        private const float Eps = 1e-5f;

        // 멱등 검사 대상: M3 메뉴가 만들거나 기록하는 파일 전부
        private static readonly string[] M3AssetPaths =
        {
            RiflePath, PistolPath, SmgPath, Ammo9Path, Mag12Path, Mag30Path,
            LootScvPath, LootRdrPath, ScvPath, RdrPath, AiConfigPath,
            PlayerConfigPath, InventoryConfigPath,
        };

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"에셋이 없다: {path}. 메뉴 {MenuPath}를 실행할 것.");
            return asset;
        }

        // ---------- 무기 (§5.2) ----------

        [Test]
        public void Rifle_DamageMigrated_AndM1ValuesUnchanged()
        {
            var w = Load<WeaponDefinitionSO>(RiflePath);
            Assert.AreEqual(45f, w.damage, Eps);            // §5.2 피해 45 (M3 데이터 이전)
            Assert.AreEqual(120f, w.shotNoiseRadiusM, Eps); // TODO(GDD-?) 임시값
            // M1 값(§5.2 + M1 임시값)은 그대로
            Assert.AreEqual("WPN-RIF-01", w.id);
            Assert.AreEqual(WeaponClass.AssaultRifle, w.cls);
            Assert.AreEqual(AmmoType.Rifle556, w.ammo);
            Assert.AreEqual(650, w.rpm);
            CollectionAssert.AreEqual(new[] { FireMode.Auto }, w.modes);
            Assert.AreEqual(0.6f, w.recoilV, Eps);
            Assert.AreEqual(2.5f, w.reloadTimeSec, Eps);
            Assert.AreEqual(new Vector2Int(4, 2), w.size);
        }

        [Test]
        public void Ammo556_DamageUnchanged()
        {
            // 터렛·기존 테스트 호환: 탄약 기본 피해 45 유지 (M3 설계 §1.2)
            var a = Load<AmmoDefinitionSO>(Ammo556Path);
            Assert.AreEqual(45f, a.damage, Eps);
        }

        [Test]
        public void Pistol_MatchesGddAndDesign()
        {
            var w = Load<WeaponDefinitionSO>(PistolPath);
            AssertItem(w, "WPN-PST-01", "녹슨 권총", 2, 1, 0.9f);
            // §5.2: 권총 / 9mm / 400 RPM / 피해 35
            Assert.AreEqual(WeaponClass.Pistol, w.cls);
            Assert.AreEqual(AmmoType.Pistol9mm, w.ammo);
            Assert.AreEqual(400, w.rpm);
            Assert.AreEqual(35f, w.damage, Eps);
            // TODO(GDD-?) 임시값
            CollectionAssert.AreEqual(new[] { FireMode.Semi }, w.modes);
            Assert.AreEqual(0.9f, w.recoilV, Eps);
            Assert.AreEqual(0.3f, w.recoilH, Eps);
            Assert.AreEqual(0.2f, w.adsTime, Eps);
            Assert.AreEqual(2.0f, w.reloadTimeSec, Eps);
            Assert.AreEqual(1.2f, w.fastReloadTimeSec, Eps);
            Assert.AreEqual(2.5f, w.hipSpreadDeg, Eps);
            Assert.AreEqual(80f, w.shotNoiseRadiusM, Eps);
            Assert.AreEqual(Rarity.Common, w.rarity);
            Assert.AreEqual(400, w.basePrice, "M5 §4.6 가격 표 (2c)");
        }

        [Test]
        public void Smg_MatchesGddAndDesign()
        {
            var w = Load<WeaponDefinitionSO>(SmgPath);
            AssertItem(w, "WPN-SMG-01", "개조 SMG", 3, 2, 2.6f);
            // §5.2: 기관단총 / 9mm / 800 RPM / 피해 30
            Assert.AreEqual(WeaponClass.Smg, w.cls);
            Assert.AreEqual(AmmoType.Pistol9mm, w.ammo);
            Assert.AreEqual(800, w.rpm);
            Assert.AreEqual(30f, w.damage, Eps);
            // TODO(GDD-?) 임시값
            CollectionAssert.AreEqual(new[] { FireMode.Auto }, w.modes);
            Assert.AreEqual(0.45f, w.recoilV, Eps);
            Assert.AreEqual(0.35f, w.recoilH, Eps);
            Assert.AreEqual(0.22f, w.adsTime, Eps);
            Assert.AreEqual(2.3f, w.reloadTimeSec, Eps);
            Assert.AreEqual(1.4f, w.fastReloadTimeSec, Eps);
            Assert.AreEqual(3.0f, w.hipSpreadDeg, Eps);
            Assert.AreEqual(90f, w.shotNoiseRadiusM, Eps);
            Assert.AreEqual(Rarity.Common, w.rarity);
            Assert.AreEqual(1500, w.basePrice, "M5 §4.6 가격 표 (2c)");
        }

        [Test]
        public void Ammo9mm_MatchesDesign()
        {
            var a = Load<AmmoDefinitionSO>(Ammo9Path);
            AssertItem(a, "AMMO-9MM", "9mm 탄", 1, 1, 0.01f);
            Assert.AreEqual(AmmoType.Pistol9mm, a.type);
            Assert.AreEqual(1, a.pelletCount);
            // TODO(GDD-?) 임시값 (damage는 무기 없이 쏠 때만 쓰는 기본값)
            Assert.AreEqual(30f, a.damage, Eps);
            Assert.AreEqual(1, a.penetration);
            Assert.AreEqual(360f, a.velocity, Eps);
            Assert.AreEqual(1.5f, a.maxFlightTimeSec, Eps);
        }

        [TestCase(Mag12Path, "MAG-9MM-12", "권총 탄창 (12발)", 12, 1, 1, 0.15f)]
        [TestCase(Mag30Path, "MAG-9MM-30", "SMG 탄창 (30발)", 30, 1, 2, 0.35f)]
        public void Magazine9mm_MatchesGddAndDesign(string path, string id, string name, int capacity, int w, int h, float kg)
        {
            var m = Load<MagazineDefinitionSO>(path);
            AssertItem(m, id, name, w, h, kg);   // 크기·무게 TODO(GDD-?)
            Assert.AreEqual(AmmoType.Pistol9mm, m.ammo);
            Assert.AreEqual(capacity, m.capacity); // §5.2 탄창 12 / 30
        }

        // §5.2 피해가 DamageRules를 거쳐 그대로 나오는지 (무기 값 우선)
        [TestCase(RiflePath, Ammo556Path, 45f)]
        [TestCase(PistolPath, Ammo9Path, 35f)]
        [TestCase(SmgPath, Ammo9Path, 30f)]
        public void ProjectileDamage_EqualsGddTable(string weaponPath, string ammoPath, float expected)
        {
            var w = Load<WeaponDefinitionSO>(weaponPath);
            var a = Load<AmmoDefinitionSO>(ammoPath);
            Assert.AreEqual(expected, DamageRules.ProjectileDamage(w, a), Eps);
        }

        // ---------- 적 유형 (§7, §4.2 + 설계 §4.1) ----------

        [Test]
        public void Scavenger_MatchesGddAndDesign()
        {
            var a = Load<EnemyArchetypeSO>(ScvPath);
            Assert.AreEqual("AI-SCV", a.id);
            Assert.AreEqual("약탈자", a.displayName);
            Assert.AreEqual("약탈자 시체", a.corpseDisplayName);
            AssertBodyAndSenses(a);
            // §7 행동: 소리 추적, 엄폐·우회 없음
            Assert.IsTrue(a.investigatesNoise);
            Assert.IsFalse(a.usesCover);
            Assert.IsFalse(a.flanks);
            Assert.AreSame(Load<LootTableSO>(LootScvPath), a.loot);

            // §7 무장 권총/SMG (가중 2:1)
            Assert.AreEqual(2, a.loadouts.Count);
            AssertLoadout(a.loadouts[0], PistolPath, Ammo9Path, Mag12Path, 2f);
            AssertLoadout(a.loadouts[1], SmgPath, Ammo9Path, Mag30Path, 1f);

            // TODO(GDD-?) 임시값
            Assert.AreEqual(2, a.spareMagazines);
            Assert.AreEqual(1.5f, a.patrolSpeed, Eps);
            Assert.AreEqual(3.0f, a.moveSpeed, Eps);
            Assert.AreEqual(5.0f, a.sprintSpeed, Eps);
            Assert.AreEqual(0.6f, a.reactionTimeSec, Eps);
            Assert.AreEqual(0.3f, a.aimErrorBaseM, Eps);
            Assert.AreEqual(0.04f, a.aimErrorPerMeterM, Eps);
            Assert.AreEqual(1.5f, a.aimSettleSec, Eps);
            Assert.AreEqual(2.0f, a.unsettledErrorMultiplier, Eps);
            Assert.AreEqual(2.0f, a.selfMovingErrorMultiplier, Eps);
            Assert.AreEqual(1.5f, a.targetMovingErrorMultiplier, Eps);
            Assert.AreEqual(0.08f, a.burstErrorGrowthM, Eps);
            Assert.AreEqual(new Vector2Int(3, 6), a.burstRounds);
            AssertV2(0.6f, 1.2f, a.burstPauseSec);
            AssertV2(0.35f, 0.6f, a.semiShotIntervalSec);
            AssertV2(8f, 20f, a.preferredRangeM);
            Assert.AreEqual(50f, a.engageMaxRangeM, Eps);
            AssertV2(0f, 0f, a.peekSec);
            AssertV2(0f, 0f, a.hideSec);
            Assert.AreEqual(0f, a.tacticalReloadBelow, Eps);
            AssertV2(2f, 4f, a.strafeIntervalSec);
        }

        [Test]
        public void Raider_MatchesGddAndDesign()
        {
            var a = Load<EnemyArchetypeSO>(RdrPath);
            Assert.AreEqual("AI-RDR", a.id);
            Assert.AreEqual("레이더", a.displayName);
            Assert.AreEqual("레이더 시체", a.corpseDisplayName);
            AssertBodyAndSenses(a);
            // §7 행동: 엄폐 활용, 측면 우회
            Assert.IsTrue(a.usesCover);
            Assert.IsTrue(a.flanks);
            Assert.IsFalse(a.investigatesNoise);
            Assert.AreSame(Load<LootTableSO>(LootRdrPath), a.loot);

            // §7 무장 소총
            Assert.AreEqual(1, a.loadouts.Count);
            AssertLoadout(a.loadouts[0], RiflePath, Ammo556Path, Mag556Path, 1f);

            // TODO(GDD-?) 임시값
            Assert.AreEqual(3, a.spareMagazines);
            Assert.AreEqual(1.5f, a.patrolSpeed, Eps);
            Assert.AreEqual(3.2f, a.moveSpeed, Eps);
            Assert.AreEqual(5.5f, a.sprintSpeed, Eps);
            Assert.AreEqual(0.4f, a.reactionTimeSec, Eps);
            Assert.AreEqual(0.2f, a.aimErrorBaseM, Eps);
            Assert.AreEqual(0.025f, a.aimErrorPerMeterM, Eps);
            Assert.AreEqual(1.0f, a.aimSettleSec, Eps);
            Assert.AreEqual(2.0f, a.unsettledErrorMultiplier, Eps);
            Assert.AreEqual(2.0f, a.selfMovingErrorMultiplier, Eps);
            Assert.AreEqual(1.3f, a.targetMovingErrorMultiplier, Eps);
            Assert.AreEqual(0.05f, a.burstErrorGrowthM, Eps);
            Assert.AreEqual(new Vector2Int(3, 5), a.burstRounds);
            AssertV2(0.5f, 1.0f, a.burstPauseSec);
            AssertV2(0.35f, 0.6f, a.semiShotIntervalSec);
            AssertV2(15f, 40f, a.preferredRangeM);
            Assert.AreEqual(60f, a.engageMaxRangeM, Eps);
            AssertV2(1.5f, 3f, a.peekSec);
            AssertV2(1f, 2.5f, a.hideSec);
            Assert.AreEqual(0.3f, a.tacticalReloadBelow, Eps);
            AssertV2(0f, 0f, a.strafeIntervalSec);
        }

        // 무장의 탄·탄창 종류가 무기와 맞는지 (M1 호환 규칙: 탄 종류)
        [TestCase(ScvPath)]
        [TestCase(RdrPath)]
        public void Loadouts_AmmoAndMagazineMatchWeapon(string path)
        {
            var a = Load<EnemyArchetypeSO>(path);
            Assert.IsNotEmpty(a.loadouts);
            foreach (EnemyLoadout l in a.loadouts)
            {
                Assert.IsNotNull(l.weapon, $"{a.id} 무기 없음");
                Assert.IsNotNull(l.ammo, $"{a.id} 탄 없음");
                Assert.IsNotNull(l.magazine, $"{a.id} 탄창 없음");
                Assert.AreEqual(l.weapon.ammo, l.ammo.type, $"{a.id} {l.weapon.id} 탄");
                Assert.AreEqual(l.weapon.ammo, l.magazine.ammo, $"{a.id} {l.weapon.id} 탄창");
                Assert.Greater(l.weight, 0f);
            }
        }

        // ---------- AiConfig (설계 §4.2) ----------

        [Test]
        public void AiConfig_MatchesGddAndDesign()
        {
            var c = Load<AiConfigSO>(AiConfigPath);
            // §7 자세 배율
            Assert.AreEqual(0.6f, c.crouchSightMultiplier, Eps);
            Assert.AreEqual(0.3f, c.proneSightMultiplier, Eps);
            // §4.2 피해 공식 (3부위 축약)
            Assert.AreEqual(0.7f, c.destroyedPartRedistribution, Eps);
            Assert.AreEqual(0.6f, c.legsDestroyedSpeedMultiplier, Eps);

            // TODO(GDD-?) 임시값: 인지 게이지
            Assert.AreEqual(0.3f, c.suspiciousAt, Eps);
            Assert.AreEqual(2.5f, c.sightGainNearPerSec, Eps);
            Assert.AreEqual(0.35f, c.sightGainFarPerSec, Eps);
            Assert.AreEqual(4f, c.instantDetectRangeM, Eps);
            Assert.AreEqual(0.15f, c.awarenessDecayPerSec, Eps);
            Assert.AreEqual(0.25f, c.footstepAwarenessGain, Eps);
            Assert.AreEqual(0.4f, c.jumpAwarenessGain, Eps);
            // 타이머
            Assert.AreEqual(2f, c.idleBeforePatrolSec, Eps);
            AssertV2(2f, 4f, c.patrolWaitSec);
            Assert.AreEqual(5f, c.suspiciousLookSec, Eps);
            Assert.AreEqual(12f, c.alertTimeoutSec, Eps);
            Assert.AreEqual(5f, c.loseSightSec, Eps);
            Assert.AreEqual(25f, c.searchDurationSec, Eps);
            Assert.AreEqual(12f, c.searchRadiusM, Eps);
            Assert.AreEqual(3, c.searchPointCount);
            Assert.AreEqual(2f, c.searchLookSec, Eps);
            // 부위 판정
            Assert.AreEqual(0.85f, c.headFrom, Eps);
            Assert.AreEqual(0.45f, c.bodyFrom, Eps);
            // 몸
            Assert.AreEqual(1.8f, c.standHeightM, Eps);
            Assert.AreEqual(1.2f, c.crouchHeightM, Eps);
            Assert.AreEqual(1.6f, c.standEyeHeightM, Eps);
            Assert.AreEqual(1.0f, c.crouchEyeHeightM, Eps);
            Assert.AreEqual(360f, c.turnSpeedDegPerSec, Eps);
            Assert.AreEqual(10f, c.fireMaxYawErrorDeg, Eps);
            // 분대
            Assert.AreEqual(50f, c.squadShareRangeM, Eps);
            Assert.AreEqual(0.5f, c.squadReportMinIntervalSec, Eps);
            Assert.AreEqual(2f, c.formationSpacingM, Eps);
            Assert.AreEqual(0.5f, c.formationUpdateSec, Eps);
            Assert.AreEqual(8f, c.flankCooldownSec, Eps);
            // 엄폐
            Assert.AreEqual(25f, c.coverSearchRadiusM, Eps);
            Assert.AreEqual(70f, c.coverProtectionHalfAngleDeg, Eps);
            Assert.AreEqual(1f, c.coverDistanceWeight, Eps);
            Assert.AreEqual(0.5f, c.coverRangeWeight, Eps);
            Assert.AreEqual(5f, c.coverAdvancePenalty, Eps);
            Assert.AreEqual(3, c.coverLosCandidates);
            Assert.AreEqual(0.6f, c.coverArriveDistanceM, Eps);
            // 측면 우회
            Assert.AreEqual(6f, c.flankAfterNoSightSec, Eps);
            Assert.AreEqual(45f, c.flankMinAngleDeg, Eps);
            Assert.AreEqual(120f, c.flankMaxAngleDeg, Eps);
            Assert.AreEqual(30f, c.flankSearchRadiusM, Eps);
            // 성능
            Assert.AreEqual(0.2f, c.relaxedSensePeriodSec, Eps);
            Assert.AreEqual(0.1f, c.engagedSensePeriodSec, Eps);
            Assert.AreEqual(8, c.maxSenseUpdatesPerFrame);
            Assert.AreEqual(3, c.maxPathRequestsPerFrame);
            Assert.AreEqual(0.5f, c.repathMinIntervalSec, Eps);
            Assert.AreEqual(1.0f, c.repathDistanceM, Eps);
            Assert.AreEqual(1, c.maxCoverQueriesPerFrame);
            Assert.AreEqual(1f, c.targetResolveRetrySec, Eps);
            // 리뷰 수정 추가 (TODO(GDD-?))
            Assert.AreEqual(10f, c.lookAheadDistanceM, Eps);
            Assert.AreEqual(5f, c.coverInvalidatedExcludeSec, Eps);
            string file = File.ReadAllText(Path.GetFullPath(AiConfigPath));
            StringAssert.Contains("lookAheadDistanceM:", file, "파일에 기록됨 (메뉴가 기존 에셋에도 기록)");
            StringAssert.Contains("coverInvalidatedExcludeSec:", file, "파일에 기록됨 (메뉴가 기존 에셋에도 기록)");
            // 디버그
            Assert.IsTrue(c.showStateIndicator);
        }

        // ---------- 루트 테이블 (설계 §4.4, 전부 TODO(GDD-?)) ----------

        [Test]
        public void LootScv_MatchesDesign()
        {
            var t = Load<LootTableSO>(LootScvPath);
            Assert.AreEqual("LT-SCV", t.id);
            Assert.AreEqual(new Vector2Int(1, 2), t.rollCount);
            AssertEntries(t, ("MED-BND-01", 3f), ("JNK-03", 3f), ("JNK-01", 2f), ("MED-TRQ-01", 1f), ("MAG-9MM-12", 1f));
        }

        [Test]
        public void LootRdr_MatchesDesign()
        {
            var t = Load<LootTableSO>(LootRdrPath);
            Assert.AreEqual("LT-RDR", t.id);
            Assert.AreEqual(new Vector2Int(1, 3), t.rollCount);
            AssertEntries(t, ("MED-BND-01", 2f), ("MED-TRQ-01", 2f), ("MED-SPL-01", 1f), ("MAG-556-30", 2f), ("JNK-03", 1f));
        }

        // 루트 테이블 항목이 실제 에셋을 가리키고 LootRoller로 굴릴 수 있다
        [TestCase(LootScvPath)]
        [TestCase(LootRdrPath)]
        public void LootTable_RollsWithinRollCount(string path)
        {
            var t = Load<LootTableSO>(path);
            var random = new System.Random(1234);
            var items = new List<ItemInstance>();
            for (int i = 0; i < 50; i++)
            {
                items.Clear();
                int n = LootRoller.Roll(t, random, items);
                Assert.That(n, Is.InRange(t.rollCount.x, t.rollCount.y), $"{t.id} 굴림 수");
                Assert.AreEqual(n, items.Count);
                foreach (ItemInstance item in items)
                    Assert.IsTrue(t.entries.Any(e => e.item == item.Definition), $"{t.id}에 없는 아이템");
            }
        }

        // ---------- §4.5 기존 SO 새 필드 ----------

        [Test]
        public void ConfigNewFields_MatchDesign_AndAreWrittenToFile()
        {
            var p = Load<PlayerConfigSO>(PlayerConfigPath);
            var i = Load<InventoryConfigSO>(InventoryConfigPath);
            Assert.AreEqual(0.5f, p.noisePulseIntervalSec, Eps);       // TODO(GDD-?)
            Assert.AreEqual(new Vector2Int(6, 5), i.corpseGridSize);   // TODO(GDD-?)
            StringAssert.Contains("noisePulseIntervalSec:", File.ReadAllText(Path.GetFullPath(PlayerConfigPath)));
            StringAssert.Contains("corpseGridSize:", File.ReadAllText(Path.GetFullPath(InventoryConfigPath)));
        }

        // ---------- 멱등 ----------

        // 메뉴를 다시 실행해도 에셋 파일이 바뀌지 않고 새 에셋도 생기지 않는다 (조정한 값 보존).
        [Test]
        public void CreateM3Data_RunAgain_DoesNotChangeAssets()
        {
            Assume.That(M3AssetPaths.All(p => AssetDatabase.LoadMainAssetAtPath(p) != null),
                $"M3 에셋이 아직 없다. 메뉴 {MenuPath}를 먼저 실행할 것.");

            var before = M3AssetPaths.ToDictionary(p => p, p => File.ReadAllText(Path.GetFullPath(p)));
            int countBefore = AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length;

            Assert.IsTrue(EditorApplication.ExecuteMenuItem(MenuPath), $"메뉴 없음: {MenuPath}");

            foreach (var pair in before)
                Assert.AreEqual(pair.Value, File.ReadAllText(Path.GetFullPath(pair.Key)), $"재실행으로 바뀜: {pair.Key}");
            Assert.AreEqual(countBefore, AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length);
        }

        // ---------- 도우미 ----------

        private static void AssertItem(ItemDefinitionSO item, string id, string name, int w, int h, float kg)
        {
            Assert.AreEqual(id, item.id);
            Assert.AreEqual(name, item.displayName);
            Assert.AreEqual(new Vector2Int(w, h), item.size, $"{id} 크기");
            Assert.AreEqual(kg, item.weightKg, Eps, $"{id} 무게");
        }

        private static void AssertBodyAndSenses(EnemyArchetypeSO a)
        {
            // §4.2 축약: 몸통(흉부) 80 / 머리 35 / 다리 65
            Assert.AreEqual(80f, a.hp, Eps, $"{a.id} 몸통");
            Assert.AreEqual(35f, a.headHp, Eps, $"{a.id} 머리");
            Assert.AreEqual(65f, a.legsHp, Eps, $"{a.id} 다리");
            // §7 시야 60m, 콘 110°
            Assert.AreEqual(60f, a.sightRange, Eps, $"{a.id} 시야 거리");
            Assert.AreEqual(110f, a.sightAngle, Eps, $"{a.id} 시야각");
            Assert.AreEqual(1f, a.hearingMultiplier, Eps, $"{a.id} 청각 배율");
        }

        private static void AssertLoadout(EnemyLoadout l, string weaponPath, string ammoPath, string magPath, float weight)
        {
            Assert.AreSame(Load<WeaponDefinitionSO>(weaponPath), l.weapon, weaponPath);
            Assert.AreSame(Load<AmmoDefinitionSO>(ammoPath), l.ammo, ammoPath);
            Assert.AreSame(Load<MagazineDefinitionSO>(magPath), l.magazine, magPath);
            Assert.AreEqual(weight, l.weight, Eps);
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

        private static void AssertV2(float x, float y, Vector2 actual)
        {
            Assert.AreEqual(x, actual.x, Eps);
            Assert.AreEqual(y, actual.y, Eps);
        }
    }
}
