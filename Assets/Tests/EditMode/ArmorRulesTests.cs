using System.IO;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.EditorTools;
using Ashfall.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using BodyPart = Ashfall.Core.BodyPart;

namespace Ashfall.Tests.EditMode
{
    // MVP 마무리 F4 §5.3 방어구 (설계 §1.5·§7 AC-F4, MF-Q16~Q24): Core ArmorRules 관통 확률·판정·난수 미소비, 부위 선택, ArmorItem, ItemFactory.
    public class ArmorRulesTests
    {
        // §5.3 값 (PlayerConfigSO·AiConfigSO 기본값과 같음)
        private static readonly ArmorRuleParams P = new ArmorRuleParams(0.5f, 0.25f, 0.3f, 0.2f);

        private static ArmorItem Armor(ArmorDefinitionSO definition, float? durability = null)
        {
            var armor = (ArmorItem)ItemFactory.Create(definition);
            if (durability.HasValue)
                armor.Durability = durability.Value;
            return armor;
        }

        private static ArmorDefinitionSO GasMask()
        {
            ArmorDefinitionSO mask = TestData.Armor("GEAR-GSM-01", "방독면", ArmorKind.Helmet, new Vector2Int(2, 2), 0.8f, 0f);
            mask.armorClass = 0;
            mask.blocksStormRadiation = true;
            return mask;
        }

        // ── 관통 확률 ────────────────────────────────────────

        [TestCase(1, 3, 0f)]
        [TestCase(3, 3, 0.5f)]
        [TestCase(4, 3, 0.75f)]
        [TestCase(5, 3, 1f)]
        [TestCase(0, 3, 0f)]
        [TestCase(7, 3, 1f)]      // 상한 clamp
        [TestCase(0, 6, 0f)]      // 하한 clamp
        [TestCase(4, 2, 1f)]      // 7.62 대 등급 2
        [TestCase(2, 3, 0.25f)]
        public void PenetrationChance_Formula(int penetration, int armorClass, float expected)
        {
            Assert.AreEqual(expected, ArmorRules.PenetrationChance(P, penetration, armorClass), 1e-6f);
        }

        [Test]
        public void PenetrationChance_NaNParams_Zero()
        {
            var bad = new ArmorRuleParams(float.NaN, 0.25f, 0.3f, 0.2f);
            Assert.AreEqual(0f, ArmorRules.PenetrationChance(bad, 3, 3));
        }

        // ── 판정 ─────────────────────────────────────────────

        [Test]
        public void Resolve_ChanceZero_Blunt30_Loss20_NoRandomDrawn()
        {
            var used = new System.Random(1234);
            var reference = new System.Random(1234);
            ArmorHitResult r = ArmorRules.Resolve(P, 1, 3, 30f, used);   // 9mm 대 등급 3 = 0%
            Assert.IsFalse(r.Penetrated);
            Assert.AreEqual(9f, r.BodyDamage, 1e-5f, "비관통 = 피해 × 0.3");
            Assert.AreEqual(6f, r.DurabilityLoss, 1e-5f, "내구도 손실 = 원피해 × 0.2");
            Assert.AreEqual(reference.NextDouble(), used.NextDouble(), "확률 0 → 난수 미소비");
        }

        [Test]
        public void Resolve_ChanceOne_FullDamage_NoLoss_NoRandomDrawn()
        {
            var used = new System.Random(99);
            var reference = new System.Random(99);
            ArmorHitResult r = ArmorRules.Resolve(P, 4, 2, 110f, used);  // 7.62 대 등급 2 = 100%
            Assert.IsTrue(r.Penetrated);
            Assert.AreEqual(110f, r.BodyDamage, 1e-5f);
            Assert.AreEqual(0f, r.DurabilityLoss, "관통은 마모 없음 (MF-Q17)");
            Assert.AreEqual(reference.NextDouble(), used.NextDouble(), "확률 1 → 난수 미소비");
        }

        [Test]
        public void Resolve_ChanceZero_NullRandom_Ok()
        {
            Assert.IsFalse(ArmorRules.Resolve(P, 1, 3, 30f, null).Penetrated);
            Assert.IsTrue(ArmorRules.Resolve(P, 5, 3, 30f, null).Penetrated);
            Assert.Throws<System.ArgumentNullException>(() => ArmorRules.Resolve(P, 3, 3, 30f, null), "0~1 사이 확률은 난수가 필요");
        }

        [Test]
        public void Resolve_Partial_DrawsOnce_StrictlyLess()
        {
            // 확률 0.5: NextDouble 0.49 → 관통, 0.5 → 비관통
            Assert.IsTrue(ArmorRules.Resolve(P, 3, 3, 45f, new FixedRandom(0.49)).Penetrated);
            ArmorHitResult blocked = ArmorRules.Resolve(P, 3, 3, 45f, new FixedRandom(0.5));
            Assert.IsFalse(blocked.Penetrated);
            Assert.AreEqual(13.5f, blocked.BodyDamage, 1e-4f, "5.56 비관통 45 → 13.5");
            Assert.AreEqual(9f, blocked.DurabilityLoss, 1e-4f, "방탄복 −9");

            var used = new System.Random(5);
            var reference = new System.Random(5);
            ArmorRules.Resolve(P, 3, 3, 45f, used);
            reference.NextDouble();
            Assert.AreEqual(reference.NextDouble(), used.NextDouble(), "0~1 사이 확률 → 난수 정확히 1개");
        }

        [Test]
        public void Resolve_FixedSeed_IsDeterministic()
        {
            var a = new System.Random(42);
            var b = new System.Random(42);
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(ArmorRules.Resolve(P, 4, 3, 110f, a).Penetrated, ArmorRules.Resolve(P, 4, 3, 110f, b).Penetrated);
        }

        [Test]
        public void Resolve_NoDamage_NothingHappens()
        {
            var used = new System.Random(3);
            var reference = new System.Random(3);
            ArmorHitResult r = ArmorRules.Resolve(P, 3, 3, 0f, used);
            Assert.AreEqual(0f, r.BodyDamage);
            Assert.AreEqual(0f, r.DurabilityLoss);
            Assert.AreEqual(0f, ArmorRules.Resolve(P, 3, 3, float.NaN, used).BodyDamage);
            Assert.AreEqual(reference.NextDouble(), used.NextDouble(), "피해 0 → 난수 미소비");
        }

        [Test]
        public void Resolve_Melee_PenetrationZero_AlwaysBlockedByClass2Plus()
        {
            // MF-Q20: 근접(관통 0)도 같은 공식 — 등급 ≥ 2면 확률 0
            ArmorHitResult r = ArmorRules.Resolve(P, 0, 2, 14f, null);
            Assert.IsFalse(r.Penetrated);
            Assert.AreEqual(14f * 0.3f, r.BodyDamage, 1e-5f);
        }

        // ── 부위 선택 ────────────────────────────────────────

        [Test]
        public void FindCovering_HelmetHeadOnly_BodyArmorThoraxStomach_LimbsNone()
        {
            ArmorItem helmet = Armor(TestData.Helmet());
            ArmorItem body = Armor(TestData.BodyArmor());
            Assert.AreSame(helmet, ArmorRules.FindCovering(helmet, body, BodyPart.Head));
            Assert.AreSame(body, ArmorRules.FindCovering(helmet, body, BodyPart.Thorax));
            Assert.AreSame(body, ArmorRules.FindCovering(helmet, body, BodyPart.Stomach));
            foreach (BodyPart limb in new[] { BodyPart.LeftArm, BodyPart.RightArm, BodyPart.LeftLeg, BodyPart.RightLeg })
                Assert.IsNull(ArmorRules.FindCovering(helmet, body, limb), $"{limb} 방어구 없음");
            Assert.IsNull(ArmorRules.FindCovering(null, body, BodyPart.Head), "헬멧 없으면 머리는 맨몸");
            Assert.IsNull(ArmorRules.FindCovering(helmet, null, BodyPart.Thorax));
        }

        [Test]
        public void FindCovering_Excludes_GasMask_ClassZero_DurabilityZero_PlainInstance()
        {
            Assert.IsNull(ArmorRules.FindCovering(Armor(GasMask()), null, BodyPart.Head), "방독면 (등급 0·covers 빈)");

            ArmorDefinitionSO class0 = TestData.Helmet();
            class0.armorClass = 0;
            Assert.IsNull(ArmorRules.FindCovering(Armor(class0), null, BodyPart.Head), "등급 0");

            Assert.IsNull(ArmorRules.FindCovering(Armor(TestData.Helmet(), 0f), null, BodyPart.Head), "내구도 0 = 막지 못함 (MF-Q18)");
            Assert.IsNotNull(ArmorRules.FindCovering(Armor(TestData.Helmet(), 0.01f), null, BodyPart.Head), "내구도 > 0이면 등급 그대로");

            Assert.IsNull(ArmorRules.FindCovering(new ItemInstance(TestData.Helmet()), null, BodyPart.Head), "ArmorItem이 아닌 인스턴스");
            Assert.IsNull(ArmorRules.FindCovering(ItemFactory.Create(TestData.Pipe()), null, BodyPart.Head));
        }

        [Test]
        public void Protects_NullCovers_False()
        {
            ArmorDefinitionSO def = TestData.Helmet();
            ArmorItem armor = Armor(def);
            def.covers = null;
            Assert.IsFalse(ArmorRules.Protects(armor, BodyPart.Head));
            Assert.IsFalse(ArmorRules.Protects(null, BodyPart.Head));
        }

        // ── Absorb (판정 + 마모) ─────────────────────────────

        [Test]
        public void Absorb_BodyArmor9mm_TenShotsToZero_ThenFullDamage()
        {
            ArmorItem body = Armor(TestData.BodyArmor());   // 등급 3, 60
            for (int i = 0; i < 10; i++)
            {
                ArmorHitResult r = ArmorRules.Absorb(body, P, 1, 30f, null);
                Assert.IsFalse(r.Penetrated);
                Assert.AreEqual(9f, r.BodyDamage, 1e-4f);
                Assert.AreEqual(60f - 6f * (i + 1), body.Durability, 1e-3f);
            }
            Assert.AreEqual(0f, body.Durability, 1e-3f);
            Assert.IsNull(ArmorRules.FindCovering(null, body, BodyPart.Thorax), "내구도 0 → 고르지 않음 → 전부 관통");
        }

        [Test]
        public void Absorb_LossClampsAtZero_AndNullArmorPassesThrough()
        {
            ArmorItem helmet = Armor(TestData.Helmet(), 5f);
            ArmorRules.Absorb(helmet, P, 1, 110f, null);   // 손실 22 > 5
            Assert.AreEqual(0f, helmet.Durability);

            ArmorHitResult none = ArmorRules.Absorb(null, P, 1, 30f, null);
            Assert.IsTrue(none.Penetrated);
            Assert.AreEqual(30f, none.BodyDamage);
            Assert.AreEqual(0f, none.DurabilityLoss);
        }

        // ── ArmorItem·ItemFactory ────────────────────────────

        [Test]
        public void ArmorItem_StartsMax_ClampsAndNaN()
        {
            ArmorItem armor = Armor(TestData.BodyArmor());
            Assert.AreEqual(60f, armor.Durability);
            Assert.AreEqual(60f, armor.MaxDurability);
            armor.Durability = 75f;
            Assert.AreEqual(60f, armor.Durability, "최대로 자름");
            armor.Durability = -3f;
            Assert.AreEqual(0f, armor.Durability, "0으로 자름");
            armor.Durability = float.NaN;
            Assert.AreEqual(60f, armor.Durability, "NaN → 최대");
            Assert.AreEqual(8.0f, armor.WeightKg, "무게 불변");
        }

        [Test]
        public void ArmorItem_GasMask_NoDurability()
        {
            ArmorItem mask = Armor(GasMask());
            Assert.AreEqual(0f, mask.Durability);
            Assert.AreEqual(0f, mask.MaxDurability);
            Assert.IsFalse(DurabilityRules.HasDurability(mask), "방독면은 내구도 표시·수리 없음");
            Assert.AreEqual(1f, DurabilityRules.Ratio(mask));
        }

        [Test]
        public void ArmorItem_IsDurable_RatioAndRestore()
        {
            ArmorItem armor = Armor(TestData.BodyArmor(), 30f);
            Assert.IsTrue(DurabilityRules.HasDurability(armor));
            Assert.AreEqual(0.5f, DurabilityRules.Ratio(armor), 1e-6f);
            Assert.IsTrue(DurabilityRules.RestoreFull(armor));
            Assert.AreEqual(60f, armor.Durability);
        }

        [Test]
        public void ItemFactory_Armor_IsArmorItem_VestStaysPlain()
        {
            Assert.IsInstanceOf<ArmorItem>(ItemFactory.Create(TestData.Helmet()));
            Assert.IsInstanceOf<ArmorItem>(ItemFactory.Create(TestData.BodyArmor()));
            Assert.IsInstanceOf<ArmorItem>(ItemFactory.Create(GasMask()));
            Assert.AreEqual(typeof(ItemInstance), ItemFactory.Create(TestData.Vest()).GetType());
            ItemDefinitionSO def = TestData.Helmet();
            Assert.AreSame(def, ((ArmorItem)ItemFactory.Create(def)).Definition);
        }

        // ── 설정 SO 기본값 ───────────────────────────────────

        [Test]
        public void ConfigDefaults_PlayerAndAi_Match_GddValues()
        {
            var player = ScriptableObject.CreateInstance<PlayerConfigSO>();
            var ai = ScriptableObject.CreateInstance<AiConfigSO>();
            try
            {
                AssertParams(P, player.ArmorRuleParams, "PlayerConfigSO 기본값");
                AssertParams(P, ai.ArmorRuleParams, "AiConfigSO 기본값");
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(ai);
            }
        }

        internal static void AssertParams(in ArmorRuleParams expected, in ArmorRuleParams actual, string label)
        {
            Assert.AreEqual(expected.BasePenetrationChance, actual.BasePenetrationChance, 1e-6f, label + " 기본 확률");
            Assert.AreEqual(expected.PenetrationPerClassStep, actual.PenetrationPerClassStep, 1e-6f, label + " 등급당");
            Assert.AreEqual(expected.BluntDamageRatio, actual.BluntDamageRatio, 1e-6f, label + " 둔탁");
            Assert.AreEqual(expected.DurabilityLossPerDamage, actual.DurabilityLossPerDamage, 1e-6f, label + " 감소율");
        }
    }

    // 세이브: 방어구 내구도 ItemRecord.durability 재사용 (설계 §1.6, MF-Q21). 버전은 3 그대로.
    public class ArmorSaveTests
    {
        private FakeCatalog catalog;
        private SaveLoadReport report;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
            report = new SaveLoadReport();
        }

        private PlayerProfile Load(string json)
        {
            Assert.IsTrue(SaveSerializer.TryParse(json, out SaveData data, out SaveLoadStatus status, out string error), error);
            Assert.AreEqual(SaveLoadStatus.Loaded, status);
            return SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);
        }

        private static ItemRecord Record(string id, float durability) =>
            new ItemRecord { id = id, container = ItemRecord.StashContainer, slot = string.Empty, loadedMagazineId = string.Empty, durability = durability };

        [Test]
        public void ToRecord_ArmorWritesDurability_GasMaskMinusOne()
        {
            var body = (ArmorItem)ItemFactory.Create(catalog.Get("ARM-BDY-01"));
            body.Durability = 42.5f;
            Assert.AreEqual(42.5f, SaveSerializer.ToRecord(body).durability);

            ArmorDefinitionSO mask = TestData.Armor("GEAR-GSM-01", "방독면", ArmorKind.Helmet, new Vector2Int(2, 2), 0.8f, 0f);
            mask.armorClass = 0;
            Assert.AreEqual(-1f, SaveSerializer.ToRecord(ItemFactory.Create(mask)).durability, "최대 0 = 내구도 없음 −1");
        }

        [Test]
        public void RoundTrip_ArmorDurability_StaysVersion3()
        {
            var profile = new PlayerProfile(M5TestData.StashSize, 0);
            var body = (ArmorItem)ItemFactory.Create(catalog.Get("ARM-BDY-01"));
            body.Durability = 17f;
            var helmet = (ArmorItem)ItemFactory.Create(catalog.Get("ARM-HLM-01"));
            Assert.IsTrue(profile.Stash.TryPlace(body, new Vector2Int(0, 0), false));
            Assert.IsTrue(profile.Stash.TryPlace(helmet, new Vector2Int(4, 0), false));

            string json = SaveSerializer.ToJson(profile, M5TestData.Epoch);
            StringAssert.Contains("\"version\": 3", json, "F4는 버전을 올리지 않는다");
            PlayerProfile loaded = Load(json);

            Assert.AreEqual(2, loaded.Stash.Items.Count);
            var lBody = (ArmorItem)loaded.Stash.Items[0].Item;
            var lHelmet = (ArmorItem)loaded.Stash.Items[1].Item;
            Assert.AreEqual(17f, lBody.Durability);
            Assert.AreEqual(40f, lHelmet.Durability);
        }

        [TestCase(-1f, 3, 60f)]           // F2 빌드가 쓴 v3 방어구(−1) → 최대
        [TestCase(float.NaN, 3, 60f)]
        [TestCase(25f, 2, 60f)]           // v < 3 → 최대
        [TestCase(25f, 1, 60f)]
        [TestCase(80f, 3, 60f)]           // 최대 초과 → 최대
        [TestCase(0f, 3, 0f)]
        [TestCase(25f, 3, 25f)]
        public void ReadArmorDurability_Rules(float recorded, int version, float expected)
        {
            Assert.AreEqual(expected, SaveSerializer.ReadArmorDurability(recorded, 60f, version));
        }

        [Test]
        public void ReadArmorDurability_MaxZero_Zero()
        {
            Assert.AreEqual(0f, SaveSerializer.ReadArmorDurability(-1f, 0f, 3));
            Assert.AreEqual(0f, SaveSerializer.ReadArmorDurability(5f, 0f, 3));
        }

        [Test]
        public void FromRecord_V2Armor_IsFullArmorItem()
        {
            ItemInstance item = SaveSerializer.FromRecord(Record("ARM-BDY-01", 10f), catalog, report, 2);
            Assert.IsInstanceOf<ArmorItem>(item);
            Assert.AreEqual(60f, ((ArmorItem)item).Durability);
            ItemInstance v3 = SaveSerializer.FromRecord(Record("ARM-BDY-01", 10f), catalog, report, 3);
            Assert.AreEqual(10f, ((ArmorItem)v3).Durability);
        }
    }

    // 적 방어구 (MF-Q16 B): EnemyArmorModel 3부위 매핑·런타임 내구도·EnemyBodyModel 연결, 데이터·설정 에셋.
    public class EnemyArmorTests
    {
        private static readonly ArmorRuleParams P = new ArmorRuleParams(0.5f, 0.25f, 0.3f, 0.2f);
        private EnemyArchetypeSO archetype;
        private AiConfigSO config;

        [SetUp]
        public void SetUp()
        {
            archetype = M3TestData.Rdr();   // 머리 35·몸통 80·다리 65
            config = M3TestData.AiConfig();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(archetype);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void NoArmor_Default_PassesThrough()
        {
            var armor = new EnemyArmorModel(archetype);
            Assert.IsFalse(armor.HasArmor, "기존 유형 = 방어구 없음");
            foreach (EnemyHitRegion region in new[] { EnemyHitRegion.Head, EnemyHitRegion.Body, EnemyHitRegion.Legs })
            {
                Assert.IsNull(armor.FindCovering(region));
                ArmorHitResult r = armor.Absorb(region, 1, 30f, P, null);
                Assert.AreEqual(30f, r.BodyDamage);
            }
        }

        [Test]
        public void Mapping_Head_Helmet_Torso_BodyArmor_Legs_None()
        {
            archetype.helmet = TestData.Helmet();
            archetype.bodyArmor = TestData.BodyArmor();
            var armor = new EnemyArmorModel(archetype);
            Assert.IsTrue(armor.HasArmor);
            Assert.AreSame(armor.Helmet, armor.FindCovering(EnemyHitRegion.Head));
            Assert.AreSame(armor.BodyArmor, armor.FindCovering(EnemyHitRegion.Body));
            Assert.IsNull(armor.FindCovering(EnemyHitRegion.Legs), "다리 = 방어구 없음");
            Assert.AreEqual(40f, armor.Helmet.Durability, "시작 = maxDurability");
            Assert.AreEqual(60f, armor.BodyArmor.Durability);
        }

        [Test]
        public void Mapping_BodyArmorOnly_HeadUnprotected()
        {
            archetype.bodyArmor = TestData.BodyArmor();   // AI-BGD
            var armor = new EnemyArmorModel(archetype);
            Assert.IsNull(armor.Helmet);
            Assert.IsNull(armor.FindCovering(EnemyHitRegion.Head));
            Assert.IsNotNull(armor.FindCovering(EnemyHitRegion.Body));
        }

        [Test]
        public void Mapping_TorsoCoveredByStomachOnly()
        {
            ArmorDefinitionSO belly = TestData.Armor("TEST-BELLY", "복대", ArmorKind.BodyArmor, new Vector2Int(2, 2), 1f, 20f, BodyPart.Stomach);
            archetype.bodyArmor = belly;
            Assert.IsNotNull(new EnemyArmorModel(archetype).FindCovering(EnemyHitRegion.Body), "몸통 = Thorax 또는 Stomach");
        }

        [Test]
        public void Torso9mm_Blunt_DamagesBodyAndWearsArmor_ToZeroThenFull()
        {
            archetype.bodyArmor = TestData.BodyArmor();
            var armor = new EnemyArmorModel(archetype);
            var body = new EnemyBodyModel(archetype, config);

            ArmorHitResult r = armor.Absorb(EnemyHitRegion.Body, 1, 30f, P, null);   // 9mm 대 등급 3 = 0%
            body.ApplyHit(EnemyHitRegion.Body, r.BodyDamage);
            Assert.IsFalse(r.Penetrated);
            Assert.AreEqual(80f - 9f, body.GetHp(EnemyHitRegion.Body), 1e-4f);
            Assert.AreEqual(54f, armor.BodyArmor.Durability, 1e-4f);

            armor.BodyArmor.Durability = 0f;
            Assert.IsNull(armor.FindCovering(EnemyHitRegion.Body), "내구도 0 = 막지 못함");
            ArmorHitResult full = armor.Absorb(EnemyHitRegion.Body, 1, 30f, P, null);
            Assert.IsTrue(full.Penetrated);
            Assert.AreEqual(30f, full.BodyDamage);
        }

        [Test]
        public void Head762_VsClass2_AlwaysPenetrates_NoWear_NoRandom()
        {
            ArmorDefinitionSO weak = TestData.Helmet();
            weak.armorClass = 2;
            archetype.helmet = weak;
            var armor = new EnemyArmorModel(archetype);
            var body = new EnemyBodyModel(archetype, config);
            var used = new System.Random(8);
            var reference = new System.Random(8);

            ArmorHitResult r = armor.Absorb(EnemyHitRegion.Head, 4, 110f, P, used);
            body.ApplyHit(EnemyHitRegion.Head, r.BodyDamage);
            Assert.IsTrue(r.Penetrated);
            Assert.IsTrue(body.IsDead, "머리 관통 → 사망");
            Assert.AreEqual(40f, armor.Helmet.Durability, "관통은 마모 없음");
            Assert.AreEqual(reference.NextDouble(), used.NextDouble(), "확률 1 → 난수 미소비");
        }

        [Test]
        public void Legs_IgnoreArmor_EvenWithFullSet()
        {
            archetype.helmet = TestData.Helmet();
            archetype.bodyArmor = TestData.BodyArmor();
            var armor = new EnemyArmorModel(archetype);
            ArmorHitResult r = armor.Absorb(EnemyHitRegion.Legs, 1, 30f, P, null);
            Assert.AreEqual(30f, r.BodyDamage);
            Assert.AreEqual(40f, armor.Helmet.Durability);
            Assert.AreEqual(60f, armor.BodyArmor.Durability);
        }

        [Test]
        public void EachEnemy_HasOwnDurability()
        {
            archetype.bodyArmor = TestData.BodyArmor();
            var a = new EnemyArmorModel(archetype);
            var b = new EnemyArmorModel(archetype);
            a.Absorb(EnemyHitRegion.Body, 1, 30f, P, null);
            Assert.AreEqual(54f, a.BodyArmor.Durability, 1e-4f);
            Assert.AreEqual(60f, b.BodyArmor.Durability, "같은 유형이어도 적마다 따로");
        }

        [Test]
        public void NullArchetype_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new EnemyArmorModel(null));
        }
    }

    // 에셋: PlayerConfig·AiConfig 방어구 4필드 명시 기록·값 일치, AI-BOS·AI-BGD 방어구 데이터, 재실행 바이트 동일 (MvpFinishDataSetup F4).
    public class ArmorDataAssetTests
    {
        private static readonly ArmorRuleParams Gdd = new ArmorRuleParams(0.5f, 0.25f, 0.3f, 0.2f);

        private static readonly string[] ArmorFields =
        {
            "armorBasePenetrationChance", "armorPenetrationPerClassStep", "armorBluntDamageRatio", "armorDurabilityLossPerDamage",
        };

        [Test]
        public void PlayerAndAiConfigAssets_ArmorFieldsSerialized_AndEqual()
        {
            foreach (string field in ArmorFields)
            {
                Assert.IsTrue(SetupUtil.IsFieldSerialized(M2DataSetup.PlayerConfigPath, field), $"PlayerConfig.{field} 명시 기록");
                Assert.IsTrue(SetupUtil.IsFieldSerialized(M3DataSetup.AiConfigPath, field), $"AiConfig.{field} 명시 기록");
            }
            var player = AssetDatabase.LoadAssetAtPath<PlayerConfigSO>(M2DataSetup.PlayerConfigPath);
            var ai = AssetDatabase.LoadAssetAtPath<AiConfigSO>(M3DataSetup.AiConfigPath);
            Assert.IsNotNull(player);
            Assert.IsNotNull(ai);
            ArmorRulesTests.AssertParams(player.ArmorRuleParams, ai.ArmorRuleParams, "AiConfig = PlayerConfig (§7 동일한 피해 공식)");
            ArmorRulesTests.AssertParams(Gdd, player.ArmorRuleParams, "PlayerConfig = §5.3");
        }

        [Test]
        public void EnemyArchetypes_BossHelmetAndBody_GuardBody_OthersNone()
        {
            var helmet = AssetDatabase.LoadAssetAtPath<ArmorDefinitionSO>(M5DataSetup.HelmetPath);
            var bodyArmor = AssetDatabase.LoadAssetAtPath<ArmorDefinitionSO>(M5DataSetup.BodyArmorPath);
            Assert.AreEqual("ARM-HLM-01", helmet.id);
            Assert.AreEqual("ARM-BDY-01", bodyArmor.id);

            var boss = AssetDatabase.LoadAssetAtPath<EnemyArchetypeSO>(M7Names.BossArchetypePath);
            var guard = AssetDatabase.LoadAssetAtPath<EnemyArchetypeSO>(M7Names.BodyguardArchetypePath);
            Assert.AreSame(helmet, boss.helmet, "AI-BOS 헬멧");
            Assert.AreSame(bodyArmor, boss.bodyArmor, "AI-BOS 방탄복");
            Assert.IsNull(guard.helmet, "AI-BGD 헬멧 없음");
            Assert.AreSame(bodyArmor, guard.bodyArmor, "AI-BGD 방탄복");

            foreach (string path in new[] { M3DataSetup.ScvPath, M3DataSetup.RdrPath, M7Names.SniperArchetypePath, M7Names.MutantArchetypePath })
            {
                var a = AssetDatabase.LoadAssetAtPath<EnemyArchetypeSO>(path);
                Assert.IsNotNull(a, path);
                Assert.IsNull(a.helmet, $"{a.id} 헬멧 없음");
                Assert.IsNull(a.bodyArmor, $"{a.id} 방탄복 없음");
            }
        }

        [Test]
        public void ArmorAssets_Unchanged_Values()
        {
            var helmet = AssetDatabase.LoadAssetAtPath<ArmorDefinitionSO>(M5DataSetup.HelmetPath);
            var bodyArmor = AssetDatabase.LoadAssetAtPath<ArmorDefinitionSO>(M5DataSetup.BodyArmorPath);
            Assert.AreEqual(3, helmet.armorClass);
            Assert.AreEqual(40f, helmet.maxDurability);
            CollectionAssert.AreEqual(new[] { BodyPart.Head }, helmet.covers);
            Assert.AreEqual(3, bodyArmor.armorClass);
            Assert.AreEqual(60f, bodyArmor.maxDurability);
            CollectionAssert.AreEqual(new[] { BodyPart.Thorax, BodyPart.Stomach }, bodyArmor.covers);
        }

        [Test]
        public void ApplyPlayerAndEnemyArmor_Rerun_KeepsBytes()
        {
            string[] paths =
            {
                M2DataSetup.PlayerConfigPath, M3DataSetup.AiConfigPath, M7Names.BossArchetypePath, M7Names.BodyguardArchetypePath,
                M5DataSetup.HelmetPath, M5DataSetup.BodyArmorPath,
            };
            var before = new byte[paths.Length][];
            for (int i = 0; i < paths.Length; i++)
                before[i] = File.ReadAllBytes(Path.GetFullPath(paths[i]));
            MvpFinishDataSetup.ApplyPlayerArmor();
            MvpFinishDataSetup.ApplyEnemyArmor();
            AssetDatabase.SaveAssets();
            for (int i = 0; i < paths.Length; i++)
                CollectionAssert.AreEqual(before[i], File.ReadAllBytes(Path.GetFullPath(paths[i])), paths[i]);
        }
    }
}
