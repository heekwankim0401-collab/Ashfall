using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.EditorTools;
using Ashfall.Hideout;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7A 무기 데이터 에셋 값 = GDD §5.1·§5.2 + M7 설계 §3.1~§3.3 임시값(TODO(GDD-?)).
    // 에셋은 Ashfall/Setup/Create M7 Weapon Data(M7WeaponDataSetup)가 만든다. 없으면 실패한다.
    // 주의(M7 리뷰 7A 낮음 3): 멱등 테스트는 커밋된 실제 에셋에 WriteM7WeaponData()를 다시 돌린다(7A 몫만, 대상이 없으면 Assume으로 건너뜀).
    public class M7WeaponDataAssetTests
    {
        private const string MenuPath = "Ashfall/Setup/Create M7 Weapon Data";
        private const string Data = "Assets/_Project/Data";
        private const string Weapons = Data + "/Weapons";
        private const string ShotgunPath = Weapons + "/WPN-SHT-01.asset";
        private const string DmrPath = Weapons + "/WPN-DMR-01.asset";
        private const string LmgPath = Weapons + "/WPN-LMG-01.asset";
        private const string Ammo12GaPath = Weapons + "/AMMO-12GA.asset";
        private const string Ammo762Path = Weapons + "/AMMO-762.asset";
        private const string Mag762_5Path = Weapons + "/MAG-762-5.asset";
        private const string Mag762_100Path = Weapons + "/MAG-762-100.asset";
        private const string TubePath = Weapons + "/MAG-12GA-TUBE.asset";
        private const string Box12GaPath = Weapons + "/BOX-12GA-8.asset";
        private const string HandlingPath = Weapons + "/WeaponHandlingConfig.asset";
        private const string TraderConfigPath = Data + "/Hideout/TraderConfig.asset";
        private const string LootCratePath = Data + "/Loot/LT-CRATE.asset";
        private const string ItemDatabasePath = Data + "/Items/ItemDatabase.asset";
        private const float Eps = 1e-5f;

        // 설계 §3.1·§3.2 가격 (id, 경로, basePrice). 전부 TODO(GDD-?). 적 전용·내장 튜브는 0(팔지 않음).
        // M5DataAssetTests.Prices_CoverEveryNonAmmoItemDefinition도 이 표를 쓴다.
        internal static readonly (string id, string path, int price)[] PriceTable =
        {
            ("WPN-SHT-01", ShotgunPath, 2500),
            ("WPN-DMR-01", DmrPath, 5000),
            ("WPN-LMG-01", LmgPath, 0),
            ("MAG-762-5", Mag762_5Path, 300),
            ("MAG-762-100", Mag762_100Path, 0),
            ("MAG-12GA-TUBE", TubePath, 0),
            ("BOX-12GA-8", Box12GaPath, 200),
        };

        // 새 탄약 정의 (인벤토리 아이템 아님, 가격 0)
        internal static readonly (string id, string path)[] AmmoDefinitions =
        {
            ("AMMO-12GA", Ammo12GaPath),
            ("AMMO-762", Ammo762Path),
        };

        // Q20 상인 판매 목록 추가분 (M5 목록 뒤 이 순서). M5DataAssetTests도 쓴다.
        internal static readonly string[] TraderAdditionIds = { "WPN-SHT-01", "WPN-DMR-01", "MAG-762-5", "BOX-12GA-8" };

        // 설계 §3.9 LT-CRATE 추가분 중 7A 몫 (M4 항목 뒤 이 순서). M4DataAssetTests도 쓴다.
        internal static readonly (string id, float weight)[] CrateAdditions = { ("BOX-12GA-8", 1.5f), ("MAG-762-5", 0.5f) };

        // 적 전용·내장 정의 — 상인·루트 테이블 어디에도 없어야 한다
        private static readonly string[] NeverObtainableIds = { "WPN-LMG-01", "MAG-762-100", "MAG-12GA-TUBE" };

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
            Assert.AreEqual(ShotgunPath, Ashfall.EditorTools.M7Names.ShotgunPath);
            Assert.AreEqual(DmrPath, Ashfall.EditorTools.M7Names.DmrPath);
            Assert.AreEqual(LmgPath, Ashfall.EditorTools.M7Names.LmgPath);
            Assert.AreEqual(Ammo12GaPath, Ashfall.EditorTools.M7Names.Ammo12GaPath);
            Assert.AreEqual(Ammo762Path, Ashfall.EditorTools.M7Names.Ammo762Path);
            Assert.AreEqual(Mag762_5Path, Ashfall.EditorTools.M7Names.Mag762_5Path);
            Assert.AreEqual(Mag762_100Path, Ashfall.EditorTools.M7Names.Mag762_100Path);
            Assert.AreEqual(TubePath, Ashfall.EditorTools.M7Names.ShotgunTubePath);
            Assert.AreEqual(Box12GaPath, Ashfall.EditorTools.M7Names.Box12Ga8Path);
            Assert.AreEqual(HandlingPath, Ashfall.EditorTools.M7Names.WeaponHandlingConfigPath);
        }

        // ---------- 무기 (§5.2 GDD + 설계 §3.1) ----------

        [Test]
        public void Shotgun_MatchesGddAndDesign()
        {
            var w = Load<WeaponDefinitionSO>(ShotgunPath);
            Assert.AreEqual("WPN-SHT-01", w.id);
            Assert.AreEqual("펌프 샷건", w.displayName, "§5.2 이름");
            // GDD §5.2: 산탄총 / 12ga / 70 / 18×8 / 6 (튜브)
            Assert.AreEqual(WeaponClass.Shotgun, w.cls);
            Assert.AreEqual(AmmoType.Gauge12, w.ammo);
            Assert.AreEqual(70, w.rpm);
            Assert.AreEqual(18f, w.damage, Eps, "펠릿당 피해");
            Assert.AreSame(Load<MagazineDefinitionSO>(TubePath), w.internalMagazine, "튜브 = MAG-12GA-TUBE");
            Assert.AreEqual(6, w.internalMagazine.capacity, "§5.2 튜브 6");
            Assert.IsFalse(w.enemyOnly);

            // TODO(GDD-?) 임시값 (설계 §3.1)
            CollectionAssert.AreEqual(new[] { FireMode.Semi }, w.modes);
            Assert.AreEqual(3.5f, w.recoilV, Eps);
            Assert.AreEqual(1.0f, w.recoilH, Eps);
            Assert.AreEqual(0.30f, w.adsTime, Eps);
            Assert.AreEqual(0.5f, w.roundReloadTimeSec, Eps, "Q5 발당 장전");
            Assert.AreEqual(3.0f, w.hipSpreadDeg, Eps);
            Assert.AreEqual(2.5f, w.pelletSpreadDeg, Eps, "Q8");
            Assert.AreEqual(110f, w.shotNoiseRadiusM, Eps);
            Assert.AreEqual(new Vector2Int(4, 1), w.size);
            Assert.AreEqual(3.4f, w.weightKg, Eps);
            Assert.AreEqual(0.12f, w.durabilityLossPerShot, Eps, "Q11");
            Assert.AreEqual(Rarity.Uncommon, w.rarity, "§3.11");
        }

        [Test]
        public void Dmr_MatchesGddAndDesign()
        {
            var w = Load<WeaponDefinitionSO>(DmrPath);
            Assert.AreEqual("WPN-DMR-01", w.id);
            Assert.AreEqual("볼트액션", w.displayName, "§5.2 이름");
            // GDD §5.2: 저격 / 7.62 / 40 / 110 / 5
            Assert.AreEqual(WeaponClass.Sniper, w.cls);
            Assert.AreEqual(AmmoType.Rifle762, w.ammo);
            Assert.AreEqual(40, w.rpm);
            Assert.AreEqual(110f, w.damage, Eps);
            Assert.IsNull(w.internalMagazine, "분리형 탄창 무기");
            Assert.IsFalse(w.enemyOnly);

            // TODO(GDD-?) 임시값 (설계 §3.1)
            CollectionAssert.AreEqual(new[] { FireMode.Semi }, w.modes, "볼트 동작 = RPM 40 간격 반자동");
            Assert.AreEqual(2.8f, w.recoilV, Eps);
            Assert.AreEqual(0.4f, w.recoilH, Eps);
            Assert.AreEqual(0.40f, w.adsTime, Eps);
            Assert.AreEqual(3.2f, w.reloadTimeSec, Eps);
            Assert.AreEqual(2.2f, w.fastReloadTimeSec, Eps);
            Assert.AreEqual(4.0f, w.hipSpreadDeg, Eps);
            Assert.AreEqual(0f, w.pelletSpreadDeg, Eps);
            Assert.AreEqual(160f, w.shotNoiseRadiusM, Eps);
            Assert.AreEqual(new Vector2Int(5, 1), w.size);
            Assert.AreEqual(4.2f, w.weightKg, Eps);
            Assert.AreEqual(0.10f, w.durabilityLossPerShot, Eps);
            Assert.AreEqual(Rarity.Rare, w.rarity, "§3.11");
        }

        [Test]
        public void Lmg_IsEnemyOnlyMachineGun()
        {
            var w = Load<WeaponDefinitionSO>(LmgPath);
            Assert.AreEqual("WPN-LMG-01", w.id);
            Assert.IsTrue(w.enemyOnly, "Q27 적 전용");
            Assert.AreEqual(WeaponClass.MachineGun, w.cls);
            Assert.AreEqual(AmmoType.Rifle762, w.ammo);
            Assert.IsNull(w.internalMagazine);
            // TODO(GDD-?) 임시값 (§5.2 표 밖)
            Assert.AreEqual(600, w.rpm);
            Assert.AreEqual(40f, w.damage, Eps);
            CollectionAssert.AreEqual(new[] { FireMode.Auto }, w.modes);
            Assert.AreEqual(5.0f, w.reloadTimeSec, Eps);
            Assert.AreEqual(4.0f, w.fastReloadTimeSec, Eps);
            Assert.AreEqual(160f, w.shotNoiseRadiusM, Eps);
            Assert.AreEqual(new Vector2Int(5, 2), w.size);
            Assert.AreEqual(9.0f, w.weightKg, Eps);
            Assert.AreEqual(0, w.basePrice);
            Assert.AreEqual(0f, w.durabilityLossPerShot, Eps, "적 무기 마모 없음 (Q18)");
            Assert.AreEqual(Rarity.Scrap, w.rarity);
        }

        // 기존 3종: M7은 마모만 더하고 §5.2 값은 그대로
        [TestCase("WPN-PST-01", WeaponClass.Pistol, AmmoType.Pistol9mm, 400, 35f, 0.06f)]
        [TestCase("WPN-SMG-01", WeaponClass.Smg, AmmoType.Pistol9mm, 800, 30f, 0.04f)]
        [TestCase("WPN-RIF-01", WeaponClass.AssaultRifle, AmmoType.Rifle556, 650, 45f, 0.05f)]
        public void ExistingWeapons_GetWear_GddValuesUnchanged(string id, WeaponClass cls, AmmoType ammo, int rpm, float damage, float wear)
        {
            string path = $"{Weapons}/{id}.asset";
            var w = Load<WeaponDefinitionSO>(path);
            Assert.AreEqual(cls, w.cls);
            Assert.AreEqual(ammo, w.ammo);
            Assert.AreEqual(rpm, w.rpm);
            Assert.AreEqual(damage, w.damage, Eps);
            Assert.AreEqual(wear, w.durabilityLossPerShot, Eps, "TODO(GDD-?) Q11");
            Assert.IsNull(w.internalMagazine);
            Assert.IsFalse(w.enemyOnly);
            StringAssert.Contains("durabilityLossPerShot:", File.ReadAllText(Path.GetFullPath(path)), "파일에 기록됨");
        }

        // 프로젝트 무기 중 적 전용은 LMG 하나, 튜브 무기는 샷건 하나
        [Test]
        public void OnlyLmg_IsEnemyOnly_OnlyShotgun_HasTube()
        {
            List<WeaponDefinitionSO> weapons = AllAssetsInProject<WeaponDefinitionSO>();
            CollectionAssert.AreEquivalent(new[] { "WPN-LMG-01" }, weapons.Where(w => w.enemyOnly).Select(w => w.id).ToArray());
            CollectionAssert.AreEquivalent(new[] { "WPN-SHT-01" }, weapons.Where(w => w.internalMagazine != null).Select(w => w.id).ToArray());
            // 무기 5종 (§5.2) + 적 전용 1
            CollectionAssert.IsSupersetOf(weapons.Select(w => w.id).ToArray(),
                new[] { "WPN-PST-01", "WPN-SMG-01", "WPN-RIF-01", "WPN-SHT-01", "WPN-DMR-01", "WPN-LMG-01" });
        }

        // ---------- 탄약·탄창 (설계 §3.2) ----------

        [Test]
        public void Ammo12Ga_EightPellets_18Each()
        {
            var a = Load<AmmoDefinitionSO>(Ammo12GaPath);
            Assert.AreEqual("AMMO-12GA", a.id);
            Assert.AreEqual(AmmoType.Gauge12, a.type);
            Assert.AreEqual(8, a.pelletCount, "§5.2 18×8");
            Assert.AreEqual(18f, a.damage, Eps, "§5.2 18×8");
            Assert.AreEqual(1, a.penetration, "TODO(GDD-?)");
            Assert.AreEqual(400f, a.velocity, Eps, "TODO(GDD-?)");
            Assert.AreEqual(0.8f, a.maxFlightTimeSec, Eps, "TODO(GDD-?)");
            Assert.AreEqual(0, a.basePrice, "탄약 정의는 인벤토리 아이템이 아니다");
        }

        [Test]
        public void Ammo762_MatchesDesign()
        {
            var a = Load<AmmoDefinitionSO>(Ammo762Path);
            Assert.AreEqual("AMMO-762", a.id);
            Assert.AreEqual(AmmoType.Rifle762, a.type);
            Assert.AreEqual(1, a.pelletCount);
            Assert.AreEqual(110f, a.damage, Eps, "§5.2 DMR 피해 (기본값)");
            Assert.AreEqual(4, a.penetration, "TODO(GDD-?)");
            Assert.AreEqual(820f, a.velocity, Eps, "TODO(GDD-?)");
            Assert.AreEqual(3.0f, a.maxFlightTimeSec, Eps, "TODO(GDD-?)");
            Assert.AreEqual(0, a.basePrice);
        }

        [TestCase("MAG-762-5", AmmoType.Rifle762, 5, 1, 1, 0.15f, 300, false, Rarity.Uncommon)]
        [TestCase("MAG-762-100", AmmoType.Rifle762, 100, 2, 1, 1.5f, 0, false, Rarity.Scrap)]
        [TestCase("MAG-12GA-TUBE", AmmoType.Gauge12, 6, 1, 1, 0f, 0, false, Rarity.Scrap)]
        [TestCase("BOX-12GA-8", AmmoType.Gauge12, 8, 1, 1, 0.35f, 200, true, Rarity.Common)]
        public void Magazines_MatchDesign(string id, AmmoType ammo, int capacity, int w, int h, float kg, int price, bool box, Rarity rarity)
        {
            var m = Load<MagazineDefinitionSO>($"{Weapons}/{id}.asset");
            Assert.AreEqual(id, m.id);
            Assert.AreEqual(ammo, m.ammo);
            Assert.AreEqual(capacity, m.capacity, "MAG-762-5 = 5, 튜브 = 6 (GDD §5.2), 나머지 TODO(GDD-?)");
            Assert.AreEqual(new Vector2Int(w, h), m.size);
            Assert.AreEqual(kg, m.weightKg, Eps);
            Assert.AreEqual(price, m.basePrice);
            Assert.AreEqual(box, m.isAmmoBox);
            Assert.AreEqual(rarity, m.rarity, "§3.11");
        }

        // 탄 상자는 BOX-12GA-8 하나 (기존 탄창은 상자가 아니다)
        [Test]
        public void OnlyBox12Ga8_IsAmmoBox()
        {
            CollectionAssert.AreEquivalent(new[] { "BOX-12GA-8" },
                AllAssetsInProject<MagazineDefinitionSO>().Where(m => m.isAmmoBox).Select(m => m.id).ToArray());
        }

        // 무기마다 맞는 분리형 탄창이 하나 이상 있다 (튜브 무기는 탄 상자)
        [Test]
        public void EveryWeapon_HasCompatibleMagazineOrBox()
        {
            List<MagazineDefinitionSO> mags = AllAssetsInProject<MagazineDefinitionSO>();
            foreach (WeaponDefinitionSO w in AllAssetsInProject<WeaponDefinitionSO>())
            {
                if (w.internalMagazine != null)
                    Assert.IsTrue(mags.Any(m => m.isAmmoBox && m.ammo == w.ammo), $"{w.id}: 낱탄 상자 없음");
                else
                    Assert.IsTrue(mags.Any(m => !m.isAmmoBox && m != w.internalMagazine && m.ammo == w.ammo), $"{w.id}: 탄창 없음");
            }
        }

        // 샷건 아이템 = 빈 튜브가 꽂힌 채로 생긴다 (7A-1 ItemFactory 규칙 + 실제 데이터)
        [Test]
        public void ShotgunItem_StartsWithEmptyTube()
        {
            var item = ItemFactory.Create(Load<WeaponDefinitionSO>(ShotgunPath)) as WeaponItem;
            Assert.IsNotNull(item);
            Assert.IsTrue(item.HasInternalMagazine);
            Assert.IsNotNull(item.LoadedMagazine);
            Assert.AreSame(Load<MagazineDefinitionSO>(TubePath), item.LoadedMagazine.Definition);
            Assert.AreEqual(0, item.LoadedMagazine.Rounds);
            Assert.AreEqual(6, item.LoadedMagazine.Capacity);
            Assert.AreEqual(WeaponItem.MaxDurability, item.Durability, Eps, "상인·상자 무기는 100 (Q12)");
        }

        // ---------- WeaponHandlingConfig (설계 §3.3) ----------

        [Test]
        public void HandlingConfig_JamAndSwitch_MatchGddAndDesign()
        {
            var c = Load<WeaponHandlingConfigSO>(HandlingPath);
            Assert.AreEqual(50f, c.jamDurabilityThreshold, Eps, "GDD §5.1 50%");
            Assert.AreEqual(0.001f, c.jamChancePerDurabilityPoint, 1e-7f, "GDD §5.1 0.1%/점");
            Assert.AreEqual(1.2f, c.jamClearHoldSec, Eps, "TODO(GDD-?) Q9");
            Assert.AreEqual(0.5f, c.weaponSwitchTimeSec, Eps, "TODO(GDD-?) Q1");
            AssertSerialized(HandlingPath, "jamDurabilityThreshold", "jamChancePerDurabilityPoint", "jamClearHoldSec",
                "weaponSwitchTimeSec", "ammoDefinitions");
            // 기존 값 불변 (M2)
            Assert.AreEqual(0.8f, c.magCheckTimeSec, Eps);
            Assert.AreEqual(0.67f, c.magCheckFullAtOrAbove, Eps);
            Assert.AreEqual(0.34f, c.magCheckHalfAtOrAbove, Eps);
        }

        // §5.1 (50 − 내구도) × 0.1% — 실제 설정 값으로
        [TestCase(100f, 0f)]
        [TestCase(50f, 0f)]
        [TestCase(40f, 0.01f)]
        [TestCase(0f, 0.05f)]
        public void HandlingConfig_JamChance_FollowsGddFormula(float durability, float expected)
        {
            var c = Load<WeaponHandlingConfigSO>(HandlingPath);
            Assert.AreEqual(expected, JamRules.Chance(durability, c.jamDurabilityThreshold, c.jamChancePerDurabilityPoint), 1e-6f);
        }

        [Test]
        public void HandlingConfig_AmmoTable_FourTypes_InOrder()
        {
            var c = Load<WeaponHandlingConfigSO>(HandlingPath);
            Assert.IsNotNull(c.ammoDefinitions);
            Assert.IsTrue(c.ammoDefinitions.All(a => a != null), "빈 항목");
            CollectionAssert.AreEqual(new[] { "AMMO-9MM", "AMMO-556", "AMMO-762", "AMMO-12GA" }, c.ammoDefinitions.Select(a => a.id).ToArray());
            CollectionAssert.AllItemsAreUnique(c.ammoDefinitions.Select(a => a.type).ToArray(), "구경마다 하나 (Q3)");

            (AmmoType type, string path)[] expected =
            {
                (AmmoType.Pistol9mm, Weapons + "/AMMO-9MM.asset"), (AmmoType.Rifle556, Weapons + "/AMMO-556.asset"),
                (AmmoType.Rifle762, Ammo762Path), (AmmoType.Gauge12, Ammo12GaPath),
            };
            foreach (var (type, path) in expected)
            {
                Assert.IsTrue(c.TryGetAmmo(type, out AmmoDefinitionSO a), type.ToString());
                Assert.AreSame(Load<AmmoDefinitionSO>(path), a, type.ToString());
            }
        }

        // 프로젝트의 모든 무기가 탄약 표에서 정의를 찾는다
        [Test]
        public void HandlingConfig_AmmoTable_ResolvesEveryWeapon()
        {
            var c = Load<WeaponHandlingConfigSO>(HandlingPath);
            foreach (WeaponDefinitionSO w in AllAssetsInProject<WeaponDefinitionSO>())
                Assert.IsTrue(c.TryGetAmmo(w.ammo, out _), $"{w.id} ({w.ammo})");
        }

        // ---------- 상인·루트 (Q20, Q27, §3.9) ----------

        [Test]
        public void Trader_EndsWithM7Additions_AllPriced()
        {
            var t = Load<TraderConfigSO>(TraderConfigPath);
            Assert.IsTrue(t.catalog.All(i => i != null), "빈 항목");
            string[] ids = t.catalog.Select(i => i.id).ToArray();
            // 7A 추가분 뒤에 7C 추가분(M7SurvivalDataAssetTests.TraderAdditionIds)이 붙는다
            int tail = TraderAdditionIds.Length + M7SurvivalDataAssetTests.TraderAdditionIds.Length;
            CollectionAssert.AreEqual(TraderAdditionIds, ids.Skip(ids.Length - tail).Take(TraderAdditionIds.Length).ToArray(), "Q20 추가분이 7C 추가분 앞에 이 순서로");
            CollectionAssert.AllItemsAreUnique(t.catalog);
            foreach (string id in TraderAdditionIds)
            {
                var item = t.catalog.First(i => i.id == id);
                Assert.AreSame(Load<ItemDefinitionSO>(PriceTable.First(p => p.id == id).path), item, id);
                Assert.Greater(item.basePrice, 0, id);
            }
        }

        [Test]
        public void LootCrate_HasM7Additions()
        {
            var t = Load<LootTableSO>(LootCratePath);
            foreach (var (id, weight) in CrateAdditions)
            {
                var entries = t.entries.Where(e => e.item != null && e.item.id == id).ToArray();
                Assert.AreEqual(1, entries.Length, id);
                Assert.AreEqual(weight, entries[0].weight, Eps, $"{id} 가중치 (TODO(GDD-?))");
                Assert.AreSame(Load<ItemDefinitionSO>(PriceTable.First(p => p.id == id).path), entries[0].item, id);
            }
        }

        // LMG·LMG 탄띠·내장 튜브는 상인·어떤 루트 테이블에도 없다 (Q27, Q5)
        [Test]
        public void EnemyOnlyAndTube_NotInTraderOrAnyLootTable()
        {
            var t = Load<TraderConfigSO>(TraderConfigPath);
            foreach (string id in NeverObtainableIds)
                Assert.IsFalse(t.catalog.Any(i => i != null && i.id == id), $"상인 목록에 {id}");

            List<LootTableSO> tables = AllAssetsInProject<LootTableSO>();
            Assert.Greater(tables.Count, 0);
            foreach (LootTableSO table in tables)
                foreach (string id in NeverObtainableIds)
                    Assert.IsFalse(table.entries.Any(e => e.item != null && e.item.id == id), $"{table.id}에 {id}");

            // 적 전용 무기는 enemyOnly 플래그로도 걸러진다
            foreach (LootTableSO table in tables)
                Assert.IsFalse(table.entries.Any(e => e.item is WeaponDefinitionSO w && w.enemyOnly), table.id);
            Assert.IsFalse(t.catalog.Any(i => i is WeaponDefinitionSO w && w.enemyOnly));
        }

        // ---------- 가격 ----------

        [Test]
        public void Prices_MatchDesign()
        {
            foreach (var (id, path, price) in PriceTable)
            {
                var item = Load<ItemDefinitionSO>(path);
                Assert.AreEqual(id, item.id, path);
                Assert.AreEqual(price, item.basePrice, $"{id} basePrice (TODO(GDD-?))");
            }
            foreach (var (id, path) in AmmoDefinitions)
                Assert.AreEqual(0, Load<AmmoDefinitionSO>(path).basePrice, id);
        }

        // ---------- ItemDatabase ----------

        [Test]
        public void ItemDatabase_ResolvesNewIds()
        {
            var db = Load<ItemDatabaseSO>(ItemDatabasePath);
            foreach (var (id, path, _) in PriceTable)
            {
                Assert.IsTrue(db.TryGet(id, out ItemDefinitionSO def), id);
                Assert.AreSame(Load<ItemDefinitionSO>(path), def, id);
            }
            foreach (var (id, path) in AmmoDefinitions)
            {
                Assert.IsTrue(db.TryGet(id, out ItemDefinitionSO def), id);
                Assert.AreSame(Load<ItemDefinitionSO>(path), def, id);
            }
        }

        // M7 리뷰 7A 낮음 4: 뷰모델 메뉴가 먼저 정의를 저장해 0이 직렬화돼도 다음 데이터 메뉴가 마모를 채운다. 0보다 큰 값(조정)은 보존.
        [TestCase(false, 0f, true)]
        [TestCase(false, 0.2f, true)]
        [TestCase(true, 0f, true)]
        [TestCase(true, -1f, true)]
        [TestCase(true, float.NaN, true)]
        [TestCase(true, 0.06f, false)]
        [TestCase(true, 0.5f, false)]
        public void ExistingWeaponWear_WrittenWhenMissingOrZero(bool serialized, float current, bool expected)
        {
            Assert.AreEqual(expected, M7WeaponDataSetup.ShouldWriteExistingWear(serialized, current));
        }

        // ---------- 멱등 ----------

        // 7A 몫(WriteM7WeaponData)을 다시 실행해도 에셋 파일·GUID가 바뀌지 않고 새 에셋도 생기지 않는다.
        // 주의(M7 리뷰 7A 낮음 3, M6 리뷰 낮음 5의 연장): 커밋된 실제 에셋에 데이터 쓰기를 다시 돌린다 — 멱등이면 아무 파일도 바뀌지 않지만,
        // 멱등이 깨지면 테스트가 커밋 에셋을 고친 채 실패한다(테스트 뒤 git status로 확인). 범위를 좁히려고 이전 마일스톤 메뉴(M1~M6
        // CreateData)는 부르지 않고, 대상 에셋이 하나라도 없으면 Assume으로 건너뛴다(테스트가 에셋을 새로 만들지 않게).
        [Test]
        public void CreateM7WeaponData_RunAgain_DoesNotChangeAssetsOrGuids()
        {
            string[] paths = PriceTable.Select(p => p.path)
                .Concat(AmmoDefinitions.Select(a => a.path))
                .Concat(new[]
                {
                    HandlingPath, TraderConfigPath, LootCratePath, ItemDatabasePath,
                    Weapons + "/WPN-PST-01.asset", Weapons + "/WPN-SMG-01.asset", Weapons + "/WPN-RIF-01.asset",
                })
                .ToArray();
            Assume.That(paths.All(p => AssetDatabase.LoadMainAssetAtPath(p) != null),
                $"M7 무기 에셋이 아직 없다. 메뉴 {MenuPath}를 먼저 실행할 것.");

            var before = paths.ToDictionary(p => p, p => File.ReadAllBytes(Path.GetFullPath(p)));
            var guids = paths.ToDictionary(p => p, p => AssetDatabase.AssetPathToGUID(p));
            int countBefore = AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length;

            var menu = (MenuItem)System.Attribute.GetCustomAttribute(
                typeof(M7WeaponDataSetup).GetMethod(nameof(M7WeaponDataSetup.CreateData)), typeof(MenuItem));
            Assert.AreEqual(MenuPath, menu?.menuItem, "메뉴 경로");

            M7WeaponDataSetup.WriteM7WeaponData();

            foreach (var pair in before)
            {
                CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(Path.GetFullPath(pair.Key)), $"재실행으로 바뀜: {pair.Key}");
                Assert.AreEqual(guids[pair.Key], AssetDatabase.AssetPathToGUID(pair.Key), $"GUID 바뀜: {pair.Key}");
            }
            Assert.AreEqual(countBefore, AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length);
        }

        // ---------- 도우미 ----------

        private static void AssertSerialized(string path, params string[] fields)
        {
            string file = File.ReadAllText(Path.GetFullPath(path));
            foreach (string f in fields)
                StringAssert.Contains(f + ":", file, $"{path}에 {f}가 기록되지 않았다");
        }
    }
}
