using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Player;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory 확장
using Is = UnityEngine.TestTools.Constraints.Is;
using AssetDatabase = UnityEditor.AssetDatabase;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ashfall.Tests.EditMode
{
    // 상시 HUD(B안, 2026-10-28 — RULE-02·04·05 개정) 순수 뷰모델·규칙.
    internal sealed class FakePlayerHudSource : IPlayerHudSource
    {
        public readonly float[] Hp = { 35f, 80f, 70f, 60f, 60f, 65f, 65f };
        public readonly float[] MaxHp = { 35f, 80f, 70f, 60f, 60f, 65f, 65f };
        // 배열만 쓴다 (할당 0 테스트에서 컬렉션 조회가 박싱하지 않게)
        public readonly bool[,] Statuses = new bool[7, 3];
        public readonly SurvivalTier[] Tiers = new SurvivalTier[3];
        public void SetStatus(BodyPart part, StatusEffect effect) => Statuses[(int)part, (int)effect] = true;
        public void SetTier(SurvivalStat stat, SurvivalTier tier) => Tiers[(int)stat] = tier;
        public float GetPartHp(BodyPart part) => Hp[(int)part];
        public float GetPartMaxHp(BodyPart part) => MaxHp[(int)part];
        public bool HasStatus(BodyPart part, StatusEffect effect) => Statuses[(int)part, (int)effect];
        public SurvivalTier GetSurvivalTier(SurvivalStat stat) => Tiers[(int)stat];
        public float Stamina { get; set; } = 100f;
        public float StaminaMax { get; set; } = 100f;
        public float Hydration { get; set; } = 100f;
        public float HydrationMax { get; set; } = 100f;
        public float Energy { get; set; } = 100f;
        public float EnergyMax { get; set; } = 100f;
        public float RadiationDose { get; set; }
        public float WeightPenaltyStartKg { get; set; } = 25f;   // §4.1
        public float SprintMaxWeightKg { get; set; } = 40f;      // §4.1
    }

    internal sealed class FakeWeaponHudSource : IWeaponHudSource
    {
        public WeaponItem CurrentWeapon { get; set; }
        public FireMode CurrentFireMode { get; set; } = FireMode.Auto;
    }

    public class HudRulesTests
    {
        [TestCase(1500f, "25:00")]
        [TestCase(65.2f, "01:06")]
        [TestCase(59.01f, "01:00")]
        [TestCase(9f, "00:09")]
        [TestCase(0f, "00:00")]
        [TestCase(-3f, "00:00")]
        [TestCase(float.NaN, "00:00")]
        public void FormatRemaining_MinutesSeconds(float sec, string expected) =>
            Assert.AreEqual(expected, HudRules.FormatRemaining(sec));

        [Test]
        public void HpTone_BoneEmberDestroyed()
        {
            Assert.AreEqual(HudHpTone.Normal, HudRules.HpTone(35f, 35f, 0.5f));
            Assert.AreEqual(HudHpTone.Normal, HudRules.HpTone(17.5f, 35f, 0.5f), "경계 값은 기본색");
            Assert.AreEqual(HudHpTone.Warn, HudRules.HpTone(17.4f, 35f, 0.5f));
            Assert.AreEqual(HudHpTone.Warn, HudRules.HpTone(0.1f, 35f, 0.5f));
            Assert.AreEqual(HudHpTone.Destroyed, HudRules.HpTone(0f, 35f, 0.5f));
            Assert.AreEqual(HudHpTone.Destroyed, HudRules.HpTone(-5f, 35f, 0.5f));
        }

        [Test]
        public void WeightTone_25And40kgExceeded()
        {
            Assert.AreEqual(HudWeightTone.Normal, HudRules.WeightTone(25f, 25f, 40f), "§4.1 25kg '초과'부터");
            Assert.AreEqual(HudWeightTone.Warn, HudRules.WeightTone(25.1f, 25f, 40f));
            Assert.AreEqual(HudWeightTone.Warn, HudRules.WeightTone(40f, 25f, 40f));
            Assert.AreEqual(HudWeightTone.Heavy, HudRules.WeightTone(40.1f, 25f, 40f));
        }

        [TestCase(0f, HudEdge.Top)]
        [TestCase(45f, HudEdge.Top)]
        [TestCase(-45f, HudEdge.Top)]
        [TestCase(46f, HudEdge.Right)]
        [TestCase(90f, HudEdge.Right)]
        [TestCase(135f, HudEdge.Right)]
        [TestCase(136f, HudEdge.Bottom)]
        [TestCase(180f, HudEdge.Bottom)]
        [TestCase(-180f, HudEdge.Bottom)]
        [TestCase(-90f, HudEdge.Left)]
        [TestCase(-135f, HudEdge.Left)]
        [TestCase(270f, HudEdge.Left)]
        public void EdgeFor_RelativeYaw(float yaw, HudEdge expected) => Assert.AreEqual(expected, HudRules.EdgeFor(yaw));

        [Test]
        public void EdgeOpacity_FadesLinearly()
        {
            Assert.AreEqual(0.6f, HudRules.EdgeOpacity(1f, 1f, 0.6f), 1e-5f);
            Assert.AreEqual(0.3f, HudRules.EdgeOpacity(0.5f, 1f, 0.6f), 1e-5f);
            Assert.AreEqual(0f, HudRules.EdgeOpacity(0f, 1f, 0.6f));
        }

        [Test]
        public void FormatWeight_OneDecimal() => Assert.AreEqual("31.5kg", HudRules.FormatWeight(31.46f));

        [Test]
        public void PlayerRelativeYaw_FromBulletTravel()
        {
            // 정면(+Z)을 보는데 오른쪽(+X)에서 쏜 탄은 -X로 날아온다 → +90 (오른쪽)
            Assert.IsTrue(PlayerHudReadout.TryRelativeYawDeg(Vector3.forward, Vector3.left, out float yaw));
            Assert.AreEqual(90f, yaw, 1e-3f);
            Assert.AreEqual(HudEdge.Right, HudRules.EdgeFor(yaw));
            // 뒤에서 (탄이 +Z로) → 아래 띠
            Assert.IsTrue(PlayerHudReadout.TryRelativeYawDeg(Vector3.forward, Vector3.forward, out yaw));
            Assert.AreEqual(HudEdge.Bottom, HudRules.EdgeFor(yaw));
            // 수직·0 방향 → 방향 없음
            Assert.IsFalse(PlayerHudReadout.TryRelativeYawDeg(Vector3.forward, Vector3.down, out _));
            Assert.IsFalse(PlayerHudReadout.TryRelativeYawDeg(Vector3.forward, Vector3.zero, out _));
        }
    }

    public class HudStatusModelTests
    {
        private HudConfigSO config;
        private HudStatusModel model;
        private FakePlayerHudSource player;
        private FakeWeaponHudSource weapon;
        private FakeInventoryService inventory;
        private UiFakeRaidInfo raid;
        private FakePlayerTarget target;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<HudConfigSO>();
            model = new HudStatusModel(config);
            player = new FakePlayerHudSource();
            weapon = new FakeWeaponHudSource();
            inventory = new FakeInventoryService();
            raid = new UiFakeRaidInfo { RemainingSec = 1500f };
            target = new FakePlayerTarget();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        private void Refresh() => model.Refresh(player, weapon, inventory, raid, target);

        [Test]
        public void NoSources_NothingVisible()
        {
            model.Refresh(null, null, null, null, null);
            Assert.IsFalse(model.PlayerVisible);
            Assert.IsFalse(model.WeaponVisible);
            Assert.IsFalse(model.QuickSlotsVisible);
            Assert.IsFalse(model.TimeVisible, "레이드 밖(IRaidInfo 없음) = 남은 시간 없음");
            Assert.IsFalse(model.PostureVisible);
        }

        [Test]
        public void Parts_HpTextAndTone()
        {
            player.Hp[(int)BodyPart.Head] = 33.2f;
            player.Hp[(int)BodyPart.Thorax] = 30f;      // 30/80 < 0.5 → 경고
            player.Hp[(int)BodyPart.LeftArm] = 0f;      // 파괴
            Refresh();
            Assert.IsTrue(model.PlayerVisible);
            Assert.AreEqual("머리 34/35", model.PartTexts[(int)BodyPart.Head], "올림 정수");
            Assert.AreEqual(HudHpTone.Normal, model.PartTones[(int)BodyPart.Head]);
            Assert.AreEqual(HudHpTone.Warn, model.PartTones[(int)BodyPart.Thorax]);
            Assert.AreEqual("왼팔 0/60", model.PartTexts[(int)BodyPart.LeftArm]);
            Assert.AreEqual(HudHpTone.Destroyed, model.PartTones[(int)BodyPart.LeftArm]);
        }

        [Test]
        public void StatusIcons_OnlyWhenAfflicted()
        {
            Refresh();
            Assert.AreEqual(string.Empty, model.PartStatusTexts[(int)BodyPart.RightLeg]);
            Assert.AreEqual(string.Empty, model.BodyEffectsText);

            player.SetStatus(BodyPart.RightLeg, StatusEffect.HeavyBleed);
            player.SetStatus(BodyPart.RightLeg, StatusEffect.Fracture);
            player.SetTier(SurvivalStat.Hydration, SurvivalTier.Critical);
            player.SetTier(SurvivalStat.Energy, SurvivalTier.Critical);
            player.SetTier(SurvivalStat.Radiation, SurvivalTier.Low);
            Refresh();
            Assert.AreEqual("중출혈 골절", model.PartStatusTexts[(int)BodyPart.RightLeg]);
            StringAssert.Contains("탈수", model.BodyEffectsText);
            StringAssert.Contains("허기", model.BodyEffectsText);
            StringAssert.Contains("방사능", model.BodyEffectsText);
        }

        [Test]
        public void Stamina_FadedWhenFull()
        {
            Refresh();
            Assert.IsTrue(model.StaminaFaded);
            Assert.AreEqual(1f, model.StaminaFill, 1e-5f);
            player.Stamina = 40f;
            Refresh();
            Assert.IsFalse(model.StaminaFaded);
            Assert.AreEqual(0.4f, model.StaminaFill, 1e-5f);
        }

        [Test]
        public void Radiation_OnlyWhenAccumulated()
        {
            Refresh();
            Assert.IsFalse(model.RadiationVisible);
            player.RadiationDose = 0.2f;
            Refresh();
            Assert.IsTrue(model.RadiationVisible);
            Assert.AreEqual("방사능 1", model.RadiationText);
            player.RadiationDose = 250f;
            Refresh();
            Assert.AreEqual("방사능 100", model.RadiationText, "0~100으로 표시");
        }

        [Test]
        public void Weapon_NameModeAmmoSpareCondition()
        {
            Refresh();
            Assert.IsFalse(model.WeaponVisible, "무기 없음 = 숨김");

            WeaponDefinitionSO def = TestData.Rifle();
            MagazineDefinitionSO magDef = TestData.Magazine30();
            var loaded = new Magazine(magDef, 27);
            var item = new WeaponItem(def, loaded);
            weapon.CurrentWeapon = item;
            var pocket = new FakeGridView(new GridRef(GridKind.Pocket, 0), new Vector2Int(1, 2));
            pocket.Placed.Add(new PlacedItem(new Magazine(magDef, 30), Vector2Int.zero, false));
            var vest = new FakeGridView(new GridRef(GridKind.TacticalVest), new Vector2Int(4, 2));
            vest.Placed.Add(new PlacedItem(new Magazine(magDef, 5), Vector2Int.zero, false));
            vest.Placed.Add(new PlacedItem(new Magazine(magDef, 0), new Vector2Int(1, 0), false));   // 빈 탄창 제외
            vest.Placed.Add(new PlacedItem(new Magazine(TestData.PistolMagazine(), 15), new Vector2Int(2, 0), false));   // 다른 탄 제외
            var backpack = new FakeGridView(new GridRef(GridKind.Backpack), new Vector2Int(4, 4));
            backpack.Placed.Add(new PlacedItem(new Magazine(magDef, 30), Vector2Int.zero, false));   // 배낭 제외
            inventory.Grids.Add(pocket);
            inventory.Grids.Add(vest);
            inventory.Grids.Add(backpack);
            Refresh();

            Assert.IsTrue(model.WeaponVisible);
            Assert.AreEqual("Rifle · 자동", model.WeaponText);
            Assert.AreEqual("27/30", model.AmmoText);
            Assert.AreEqual("예비 2", model.SpareText);
            Assert.AreEqual("양호", model.ConditionText);

            loaded.TakeRound();
            item.Durability = 30f;   // 0.3: 낡음 (0.25 ≤ 0.3 < 0.5)
            Refresh();
            Assert.AreEqual("26/30", model.AmmoText);
            Assert.AreEqual("낡음", model.ConditionText);
            Assert.AreEqual(ItemConditionTier.Worn, model.ConditionTier);

            item.Durability = 10f;
            item.LoadedMagazine = null;
            Refresh();
            Assert.AreEqual("위험", model.ConditionText);
            Assert.AreEqual(UiText.HudNoMagazine, model.AmmoText);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(magDef);
        }

        [Test]
        public void QuickSlots_PostureWeight()
        {
            var bandageDef = TestData.Bandage();
            inventory.QuickSlots[1] = new MedicalItem(bandageDef);
            inventory.WeightKg = 26f;
            target.Posture = TargetPosture.Crouching;
            Refresh();
            Assert.IsTrue(model.QuickSlotsVisible);
            Assert.AreEqual("4 —", model.QuickSlotTexts[0]);
            Assert.IsFalse(model.QuickSlotFilled[0]);
            StringAssert.StartsWith("5 " + bandageDef.displayName, model.QuickSlotTexts[1]);
            Assert.IsTrue(model.QuickSlotFilled[1]);
            Assert.AreEqual("웅크림", model.PostureText);
            Assert.AreEqual("26.0kg", model.WeightText);
            Assert.AreEqual(HudWeightTone.Warn, model.WeightTone);
            inventory.WeightKg = 41f;
            target.Posture = TargetPosture.Prone;
            Refresh();
            Assert.AreEqual(HudWeightTone.Heavy, model.WeightTone);
            Assert.AreEqual("엎드림", model.PostureText);

            // 경계는 플레이어 원천(PlayerConfigSO §4.1) 값 — HUD 쪽 복제 없음
            player.SprintMaxWeightKg = 50f;
            Refresh();
            Assert.AreEqual(HudWeightTone.Warn, model.WeightTone, "경계가 원천 값을 따른다");
            model.Refresh(null, null, inventory, null, null);
            Assert.AreEqual(HudWeightTone.Normal, model.WeightTone, "플레이어 원천 없음 = 경고 없음");
        }

        [Test]
        public void Ctor_NullConfig_Throws() =>
            Assert.Throws<System.ArgumentNullException>(() => new HudStatusModel(null));

        // 성능 목표(+0.3ms): 값이 그대로면 매 프레임 Refresh·Tick이 GC 할당 0
        [Test]
        public void RefreshAndTick_NoChange_AllocatesNothing()
        {
            WeaponDefinitionSO def = TestData.Rifle();
            MagazineDefinitionSO magDef = TestData.Magazine30();
            weapon.CurrentWeapon = new WeaponItem(def, new Magazine(magDef, 20));
            var vest = new FakeGridView(new GridRef(GridKind.TacticalVest), new Vector2Int(4, 2));
            vest.Placed.Add(new PlacedItem(new Magazine(magDef, 30), Vector2Int.zero, false));
            inventory.Grids.Add(vest);
            MedicalDefinitionSO bandage = TestData.Bandage();
            inventory.QuickSlots[0] = new MedicalItem(bandage);
            inventory.WeightKg = 30f;
            player.RadiationDose = 12f;
            var services = new ServiceRegistry();
            services.Register<IPlayerHudSource>(player);
            services.Register<IWeaponHudSource>(weapon);
            services.Register<IInventoryService>(inventory);
            services.Register<IRaidInfo>(raid);
            services.Register<IPlayerTarget>(target);
            model.NotifyHit(30f, true);
            model.Tick(0.016f, services);   // 첫 호출: 문자열 생성
            Refresh();

            Assert.That(() => Refresh(), Is.Not.AllocatingGCMemory());
            Assert.That(() => model.Tick(0.016f, services), Is.Not.AllocatingGCMemory());
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(magDef);
            Object.DestroyImmediate(bandage);
        }

        [Test]
        public void Time_FromRaidInfo()
        {
            Refresh();
            Assert.IsTrue(model.TimeVisible);
            Assert.AreEqual("25:00", model.TimeText);
            raid.RemainingSec = 61f;
            Refresh();
            Assert.AreEqual("01:01", model.TimeText);
        }

        [Test]
        public void Version_ChangesOnlyWhenDisplayChanges()
        {
            Refresh();
            int v = model.Version;
            raid.RemainingSec = 1499.5f;   // 올림 1500 = 같음
            player.Hp[0] = 34.9f;          // 올림 35 = 같음
            Refresh();
            Assert.AreEqual(v, model.Version, "표시가 같으면 다시 그리지 않는다");
            string time = model.TimeText;
            raid.RemainingSec = 1498.5f;
            Refresh();
            Assert.AreNotEqual(v, model.Version);
            Assert.AreNotSame(time, model.TimeText);
        }

        [Test]
        public void HitEdge_ShowsThenFadesWithinDuration()
        {
            config.hitEdgeDurationSec = 1f;
            config.hitEdgeMaxOpacity = 0.6f;
            Assert.AreEqual(0f, model.EdgeOpacity[(int)HudEdge.Right], "평소 없음");
            model.NotifyHit(90f, true);
            Assert.AreEqual(0.6f, model.EdgeOpacity[(int)HudEdge.Right], 1e-5f);
            Assert.AreEqual(0f, model.EdgeOpacity[(int)HudEdge.Left]);
            model.TickEdges(0.5f);
            Assert.AreEqual(0.3f, model.EdgeOpacity[(int)HudEdge.Right], 1e-4f);
            model.TickEdges(0.6f);
            Assert.AreEqual(0f, model.EdgeOpacity[(int)HudEdge.Right], "약 1초 뒤 사라짐");

            model.NotifyHit(0f, false);   // 방향 모름 = 네 띠
            for (int i = 0; i < HudRules.EdgeCount; i++)
                Assert.Greater(model.EdgeOpacity[i], 0f);
        }

        [Test]
        public void Tick_ReadsServiceRegistry()
        {
            var services = new ServiceRegistry();
            services.Register<IPlayerHudSource>(player);
            services.Register<IRaidInfo>(raid);
            model.Tick(0.016f, services);
            Assert.IsTrue(model.PlayerVisible);
            Assert.IsTrue(model.TimeVisible);
            Assert.IsFalse(model.WeaponVisible);
        }
    }

    // UXML ↔ HudStatusView 이름 계약 + 그리기
    public class HudStatusViewUxmlTests
    {
        private const string GameUxml = "Assets/_Project/Art/UI/GameUI.uxml";

        [Test]
        public void GameUxml_HudStatusView_RendersModel()
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GameUxml);
            Assert.IsNotNull(asset);
            VisualElement root = asset.CloneTree();
            var view = new HudStatusView(root);
            foreach (string edge in HudStatusView.EdgeNames)
                Assert.AreEqual(DisplayStyle.None, root.Q(edge).style.display.value, "피격 가장자리는 평소 display:none");

            var config = ScriptableObject.CreateInstance<HudConfigSO>();
            var model = new HudStatusModel(config);
            model.Refresh(new FakePlayerHudSource(), null, new FakeInventoryService(), new UiFakeRaidInfo { RemainingSec = 300f }, new FakePlayerTarget());
            model.NotifyHit(-90f, true);
            view.Render(model);

            Assert.AreEqual("05:00", root.Q<Label>(HudStatusView.TimeName).text);
            Assert.AreEqual("머리 35/35", root.Q<Label>(HudStatusView.PartPrefix + 0).text);
            Assert.AreEqual(DisplayStyle.Flex, root.Q(HudStatusView.LeftName).style.display.value);
            Assert.AreEqual(DisplayStyle.None, root.Q(HudStatusView.WeaponName).style.display.value, "무기 없음 = 숨김");
            Assert.AreEqual(DisplayStyle.None, root.Q(HudStatusView.RadiationName).style.display.value, "방사능 0 = 숨김");
            Assert.AreEqual(DisplayStyle.Flex, root.Q("hud-edge-left").style.display.value);
            Assert.AreEqual(DisplayStyle.None, root.Q("hud-edge-right").style.display.value);
            Assert.IsTrue(root.Q(HudStatusView.StaminaBarName).parent.ClassListContains(HudStatusView.FadedClass), "가득 = 흐리게");
            Object.DestroyImmediate(config);
        }
    }
}
