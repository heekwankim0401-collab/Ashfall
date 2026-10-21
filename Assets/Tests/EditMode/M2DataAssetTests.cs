using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.UI;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using BodyPart = Ashfall.Core.BodyPart;

namespace Ashfall.Tests.EditMode
{
    // M2 데이터 에셋 값 = GDD 표 (§4.2, §4.3, §6, RULE-04) + M2 설계 §4 임시값(TODO(GDD-?)).
    // 에셋은 Ashfall/Setup/Create M2 Data(M2DataSetup)가 만든다. 없으면 실패한다.
    public class M2DataAssetTests
    {
        private const string MenuPath = "Ashfall/Setup/Create M2 Data";
        private const string Data = "Assets/_Project/Data";
        private const string PlayerConfigPath = Data + "/Player/PlayerConfig.asset";
        private const string WeaponHandlingPath = Data + "/Weapons/WeaponHandlingConfig.asset";
        private const string InventoryConfigPath = Data + "/Inventory/InventoryConfig.asset";
        private const string HudConfigPath = Data + "/UI/HudConfig.asset";
        private const float Eps = 1e-5f;

        private static readonly string[] M2AssetPaths =
        {
            PlayerConfigPath, WeaponHandlingPath, InventoryConfigPath, HudConfigPath,
            Data + "/Items/Medical/MED-BND-01.asset",
            Data + "/Items/Medical/MED-TRQ-01.asset",
            Data + "/Items/Medical/MED-SPL-01.asset",
            Data + "/Items/Gear/RIG-01.asset",
            Data + "/Items/Gear/BAG-01.asset",
            Data + "/Items/Armor/ARM-HLM-01.asset",
            Data + "/Items/Armor/ARM-BDY-01.asset",
            Data + "/Items/Junk/JNK-01.asset",
            Data + "/Items/Junk/JNK-02.asset",
            Data + "/Items/Junk/JNK-03.asset",
        };

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"에셋이 없다: {path}. 메뉴 {MenuPath}를 실행할 것.");
            return asset;
        }

        // ---------- PlayerConfig §4.2 / §4.3 ----------

        [Test]
        public void PlayerConfig_BodyPartHp_MatchesGdd()
        {
            var c = Load<PlayerConfigSO>(PlayerConfigPath);
            // §4.2 표: 머리 35 / 흉부 80 / 복부 70 / 팔 60 / 다리 65
            Assert.AreEqual(35f, c.headMaxHp, Eps);
            Assert.AreEqual(80f, c.thoraxMaxHp, Eps);
            Assert.AreEqual(70f, c.stomachMaxHp, Eps);
            Assert.AreEqual(60f, c.armMaxHp, Eps);
            Assert.AreEqual(65f, c.legMaxHp, Eps);
            Assert.AreEqual(35f, c.MaxHp(BodyPart.Head), Eps);
            Assert.AreEqual(60f, c.MaxHp(BodyPart.RightArm), Eps);
            Assert.AreEqual(65f, c.MaxHp(BodyPart.LeftLeg), Eps);
        }

        [Test]
        public void PlayerConfig_DestroyedEffects_MatchGdd()
        {
            var c = Load<PlayerConfigSO>(PlayerConfigPath);
            Assert.AreEqual(0.7f, c.destroyedPartRedistribution, Eps);      // 70% 분산
            Assert.AreEqual(1.5f, c.stomachDestroyedDamageMultiplier, Eps); // 복부 1.5배
            Assert.AreEqual(1.0f, c.armDestroyedAimSwayAdd, Eps);           // 팔 +100%
            Assert.AreEqual(0.6f, c.legDestroyedSpeedMultiplier, Eps);      // 다리 -40%
        }

        [Test]
        public void PlayerConfig_Bleed_MatchesGdd()
        {
            var c = Load<PlayerConfigSO>(PlayerConfigPath);
            Assert.AreEqual(1f, c.bleedHpPerSec, Eps);      // STAT-BLEED -1 HP/s
            Assert.AreEqual(3f, c.heavyBleedHpPerSec, Eps); // STAT-HBLEED -3 HP/s
        }

        // TODO(GDD-?) 임시값이 설계 §4.1과 같은지 (문서가 정해지면 바뀐다)
        [Test]
        public void PlayerConfig_TemporaryValues_MatchDesign()
        {
            var c = Load<PlayerConfigSO>(PlayerConfigPath);
            Assert.AreEqual(10f, c.bleedMinDamage, Eps);
            Assert.AreEqual(0.15f, c.heavyBleedChance, Eps);
            Assert.AreEqual(0.35f, c.bleedChance, Eps);
            Assert.AreEqual(20f, c.fractureMinDamage, Eps);
            Assert.AreEqual(0.2f, c.fractureChance, Eps);
            Assert.AreEqual(0.8f, c.fractureLegSpeedMultiplier, Eps);
            Assert.AreEqual(0.5f, c.fractureArmAimSwayAdd, Eps);
            Assert.AreEqual(0.75f, c.tierInjuredBelow, Eps);
            Assert.AreEqual(0.35f, c.tierCriticalBelow, Eps);
            Assert.AreEqual(0.87f, c.headZoneFrom, Eps);
            Assert.AreEqual(0.62f, c.thoraxZoneFrom, Eps);
            Assert.AreEqual(0.45f, c.stomachZoneFrom, Eps);
            Assert.AreEqual(0.6f, c.armZoneSideRatio, Eps);
        }

        [Test]
        public void PlayerConfig_M1Values_Unchanged()
        {
            var c = Load<PlayerConfigSO>(PlayerConfigPath);
            // §4.1 표 (M2 메뉴가 M1 값을 건드리지 않았는지)
            Assert.AreEqual(3.0f, c.walkSpeed, Eps);
            Assert.AreEqual(5.5f, c.sprintSpeed, Eps);
            Assert.AreEqual(25f, c.weightPenaltyStartKg, Eps);
            Assert.AreEqual(0.02f, c.weightPenaltyPerKg, Eps);
            Assert.AreEqual(40f, c.sprintMaxWeightKg, Eps);
        }

        // 1단계 메모 5: 필드 초기값만으로 맞아 보이는 것이 아니라 파일에 실제로 기록돼 있어야 한다.
        [Test]
        public void PlayerConfig_M2Fields_AreWrittenToFile()
        {
            Load<PlayerConfigSO>(PlayerConfigPath);
            string text = File.ReadAllText(Path.GetFullPath(PlayerConfigPath));
            foreach (string field in new[] { "headMaxHp", "legMaxHp", "bleedHpPerSec", "heavyBleedHpPerSec", "armZoneSideRatio" })
                StringAssert.Contains(field + ":", text, $"PlayerConfig.asset에 {field}가 기록되지 않았다.");
        }

        // ---------- 설정 SO ----------

        [Test]
        public void WeaponHandlingConfig_MatchesGddAndDesign()
        {
            var h = Load<WeaponHandlingConfigSO>(WeaponHandlingPath);
            Assert.AreEqual(0.8f, h.magCheckTimeSec, Eps); // RULE-04 0.8초
            // TODO(GDD-?) 임시값
            Assert.AreEqual(0.67f, h.magCheckFullAtOrAbove, Eps);
            Assert.AreEqual(0.34f, h.magCheckHalfAtOrAbove, Eps);
            Assert.AreEqual(0.15f, h.aimSwayBaseDeg, Eps);
            Assert.AreEqual(0.5f, h.aimSwayFrequencyHz, Eps);
        }

        [Test]
        public void InventoryConfig_MatchesGddAndDesign()
        {
            var i = Load<InventoryConfigSO>(InventoryConfigPath);
            // §6 주머니 1×1×4, 아이템 1개당 0.5초 검색
            Assert.AreEqual(4, i.pocketCount);
            Assert.AreEqual(new Vector2Int(1, 1), i.pocketSize);
            Assert.AreEqual(0.5f, i.searchTimePerItemSec, Eps);
            // TODO(GDD-?) 임시값
            Assert.AreEqual(2.0f, i.interactRangeM, Eps);
            Assert.IsFalse(i.reloadFromBackpack);
            Assert.AreEqual(0.6f, i.dropForwardM, Eps);
            Assert.AreEqual(1.5f, i.dropSpeed, Eps);
            Assert.AreEqual(0.1f, i.worldItemCellM, Eps);
        }

        [Test]
        public void HudConfig_MatchesDesign()
        {
            var h = Load<HudConfigSO>(HudConfigPath);
            Assert.AreEqual(2.0f, h.magCheckResultSec, Eps); // TODO(GDD-?) 임시값
        }

        // ---------- 아이템 (설계 §4.5, 전부 TODO(GDD-?)) ----------

        [TestCase("MED-BND-01", "붕대", 1, 1, 0.05f, StatusEffect.Bleed, 3f)]
        [TestCase("MED-TRQ-01", "지혈대", 1, 1, 0.1f, StatusEffect.HeavyBleed, 4f)]
        [TestCase("MED-SPL-01", "부목", 1, 2, 0.2f, StatusEffect.Fracture, 5f)]
        public void Medical_MatchesDesign(string id, string name, int w, int h, float kg, StatusEffect cure, float useTime)
        {
            var m = Load<MedicalDefinitionSO>($"{Data}/Items/Medical/{id}.asset");
            AssertItem(m, id, name, w, h, kg);
            // §4.3 해제 수단: 붕대 → 출혈, 지혈대 → 중출혈, 부목 → 골절 (하나만)
            CollectionAssert.AreEqual(new[] { cure }, m.cures);
            Assert.AreEqual(0f, m.healAmount, Eps);
            Assert.AreEqual(useTime, m.useTime, Eps);
            Assert.AreEqual(1, m.uses);
        }

        [TestCase("RIG-01", "전술조끼", ContainerKind.TacticalVest, 2, 2, 1.2f, 4, 2)]
        [TestCase("BAG-01", "배낭", ContainerKind.Backpack, 3, 3, 1.5f, 4, 5)]
        public void Container_MatchesDesign(string id, string name, ContainerKind kind, int w, int h, float kg, int gridW, int gridH)
        {
            var c = Load<ContainerDefinitionSO>($"{Data}/Items/Gear/{id}.asset");
            AssertItem(c, id, name, w, h, kg);
            Assert.AreEqual(kind, c.kind);
            Assert.AreEqual(new Vector2Int(gridW, gridH), c.gridSize);
        }

        [Test]
        public void Vest_HoldsFourMagazines()
        {
            // 설계 §4.5: 조끼 4×2 = 1×2 탄창 4개
            var vest = Load<ContainerDefinitionSO>($"{Data}/Items/Gear/RIG-01.asset");
            var mag = Load<MagazineDefinitionSO>($"{Data}/Weapons/MAG-556-30.asset");
            int fit = (vest.gridSize.x / mag.size.x) * (vest.gridSize.y / mag.size.y);
            Assert.AreEqual(4, fit);
        }

        [Test]
        public void Helmet_MatchesDesign()
        {
            var a = Load<ArmorDefinitionSO>($"{Data}/Items/Armor/ARM-HLM-01.asset");
            AssertItem(a, "ARM-HLM-01", "헬멧", 2, 2, 1.4f);
            Assert.AreEqual(ArmorKind.Helmet, a.kind);
            Assert.AreEqual(3, a.armorClass);
            Assert.AreEqual(40f, a.maxDurability, Eps);
            CollectionAssert.AreEqual(new[] { BodyPart.Head }, a.covers);
        }

        [Test]
        public void BodyArmor_MatchesDesign()
        {
            var a = Load<ArmorDefinitionSO>($"{Data}/Items/Armor/ARM-BDY-01.asset");
            AssertItem(a, "ARM-BDY-01", "방탄복", 3, 3, 8.0f);
            Assert.AreEqual(ArmorKind.BodyArmor, a.kind);
            Assert.AreEqual(3, a.armorClass);
            Assert.AreEqual(60f, a.maxDurability, Eps);
            CollectionAssert.AreEqual(new[] { BodyPart.Thorax, BodyPart.Stomach }, a.covers);
        }

        [Test]
        public void ArmorClass_IsWithinGddRange()
        {
            // §5.3 방어 등급 1~5
            foreach (string id in new[] { "ARM-HLM-01", "ARM-BDY-01" })
            {
                var a = Load<ArmorDefinitionSO>($"{Data}/Items/Armor/{id}.asset");
                Assert.That(a.armorClass, Is.InRange(1, 5), id);
            }
        }

        [TestCase("JNK-01", "녹슨 파이프", 1, 3, 1.0f)]
        [TestCase("JNK-02", "엔진 부품", 3, 2, 20.0f)]
        [TestCase("JNK-03", "전선 뭉치", 1, 1, 0.3f)]
        public void Junk_MatchesDesign(string id, string name, int w, int h, float kg)
        {
            var i = Load<ItemDefinitionSO>($"{Data}/Items/Junk/{id}.asset");
            Assert.AreEqual(typeof(ItemDefinitionSO), i.GetType(), "잡동사니는 일반 ItemDefinitionSO여야 한다.");
            AssertItem(i, id, name, w, h, kg);
            Assert.AreEqual(Rarity.Scrap, i.rarity);
        }

        [Test]
        public void ItemIds_AreUniqueAndMatchFileNames()
        {
            var guids = AssetDatabase.FindAssets("t:ItemDefinitionSO", new[] { Data });
            var ids = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinitionSO>(path);
                Assert.IsFalse(string.IsNullOrEmpty(item.id), $"id 비어 있음: {path}");
                Assert.AreEqual(Path.GetFileNameWithoutExtension(path), item.id, $"파일 이름 ≠ id: {path}");
                ids.Add(item.id);
            }
            CollectionAssert.AllItemsAreUnique(ids);
        }

        // ---------- 멱등 ----------

        // 메뉴를 다시 실행해도 에셋 파일이 바뀌지 않는다 (조정한 값 보존).
        [Test]
        public void CreateM2Data_RunAgain_DoesNotChangeAssets()
        {
            Assume.That(M2AssetPaths.All(p => AssetDatabase.LoadMainAssetAtPath(p) != null),
                $"M2 에셋이 아직 없다. 메뉴 {MenuPath}를 먼저 실행할 것.");

            var before = M2AssetPaths.ToDictionary(p => p, p => File.ReadAllText(Path.GetFullPath(p)));
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
    }
}
