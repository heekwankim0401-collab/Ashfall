using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.EditorTools;
using Ashfall.Hideout;
using Ashfall.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ashfall.Tests.EditMode
{
    // MVP 마무리 F3 내구도 공통 규칙 (§5.1, 설계 §1.4·§7 AC-F3): DurabilityRules·WeaponItem : IDurableItem
    public class DurabilityRulesTests
    {
        private static WeaponItem Weapon(float durability) =>
            new WeaponItem(TestData.Rifle()) { Durability = durability };

        [Test]
        public void Ratio_Weapon_0_49_50_100()
        {
            Assert.AreEqual(0f, DurabilityRules.Ratio(Weapon(0f)), 1e-6f);
            Assert.AreEqual(0.49f, DurabilityRules.Ratio(Weapon(49f)), 1e-6f);
            Assert.AreEqual(0.5f, DurabilityRules.Ratio(Weapon(50f)), 1e-6f);
            Assert.AreEqual(1f, DurabilityRules.Ratio(Weapon(100f)), 1e-6f);
        }

        [Test]
        public void Ratio_NoDurability_IsOne()
        {
            Assert.AreEqual(1f, DurabilityRules.Ratio(new ItemInstance(TestData.Wires())));
            Assert.AreEqual(1f, DurabilityRules.Ratio(new Magazine(TestData.Magazine30(), 0)));
            Assert.AreEqual(1f, DurabilityRules.Ratio(null));
            Assert.IsFalse(DurabilityRules.HasDurability(new ItemInstance(TestData.Wires())));
            Assert.IsFalse(DurabilityRules.HasDurability(null));
            Assert.IsTrue(DurabilityRules.HasDurability(Weapon(100f)));
        }

        [Test]
        public void WeaponItem_IsDurable_MaxIs100()
        {
            IDurableItem durable = Weapon(30f);
            Assert.AreEqual(WeaponItem.MaxDurability, durable.MaxDurability);
            Assert.AreEqual(30f, durable.Durability);
            durable.Durability = 150f;
            Assert.AreEqual(100f, durable.Durability, "인터페이스로 써도 0~100으로 자른다");
        }

        [Test]
        public void RestoreFull_WeaponToMax_OtherFalse()
        {
            WeaponItem weapon = Weapon(12f);
            Assert.IsTrue(DurabilityRules.RestoreFull(weapon));
            Assert.AreEqual(100f, weapon.Durability);
            Assert.IsFalse(DurabilityRules.RestoreFull(new ItemInstance(TestData.Wires())));
            Assert.IsFalse(DurabilityRules.RestoreFull(null));
        }
    }

    // 대략 등급·문구 (MF-Q10·Q11, RULE-04 숫자 없음)
    public class ItemConditionTextTests
    {
        private HudConfigSO hud;

        [SetUp]
        public void SetUp() => hud = ScriptableObject.CreateInstance<HudConfigSO>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(hud);

        private static WeaponItem Weapon(float durability) =>
            new WeaponItem(TestData.Rifle()) { Durability = durability };

        [Test]
        public void HudConfig_Defaults_AreDesignValues()
        {
            Assert.AreEqual(0.5f, hud.conditionWornBelow);
            Assert.AreEqual(0.25f, hud.conditionCriticalBelow);
        }

        [Test]
        public void TierFor_Boundaries_BelongToUpperTier()
        {
            Assert.AreEqual(ItemConditionTier.Good, ItemConditionRules.TierFor(1f, 0.5f, 0.25f));
            Assert.AreEqual(ItemConditionTier.Good, ItemConditionRules.TierFor(0.5f, 0.5f, 0.25f), "0.5 정확히 = 양호");
            Assert.AreEqual(ItemConditionTier.Worn, ItemConditionRules.TierFor(0.49f, 0.5f, 0.25f));
            Assert.AreEqual(ItemConditionTier.Worn, ItemConditionRules.TierFor(0.25f, 0.5f, 0.25f), "0.25 정확히 = 낡음");
            Assert.AreEqual(ItemConditionTier.Critical, ItemConditionRules.TierFor(0.24f, 0.5f, 0.25f));
            Assert.AreEqual(ItemConditionTier.Critical, ItemConditionRules.TierFor(0f, 0.5f, 0.25f));
        }

        [Test]
        public void TierFor_UsesHudConfig()
        {
            hud.conditionWornBelow = 0.8f;
            hud.conditionCriticalBelow = 0.6f;
            Assert.AreEqual(ItemConditionTier.Worn, ItemConditionRules.TierFor(0.7f, hud));
            Assert.AreEqual(ItemConditionTier.Critical, ItemConditionRules.TierFor(0.5f, hud));
        }

        [Test]
        public void ConditionWord_ThreeWords()
        {
            Assert.AreEqual("양호", UiText.ConditionWord(ItemConditionTier.Good));
            Assert.AreEqual("낡음", UiText.ConditionWord(ItemConditionTier.Worn));
            Assert.AreEqual("위험", UiText.ConditionWord(ItemConditionTier.Critical));
        }

        [Test]
        public void ItemLabelWithCondition_Weapon_AppendsWord()
        {
            Assert.AreEqual("Rifle (양호)", UiText.ItemLabelWithCondition(Weapon(100f), hud));
            Assert.AreEqual("Rifle (양호)", UiText.ItemLabelWithCondition(Weapon(50f), hud));
            Assert.AreEqual("Rifle (낡음)", UiText.ItemLabelWithCondition(Weapon(49f), hud));
            Assert.AreEqual("Rifle (낡음)", UiText.ItemLabelWithCondition(Weapon(25f), hud));
            Assert.AreEqual("Rifle (위험)", UiText.ItemLabelWithCondition(Weapon(24f), hud));
            Assert.AreEqual("Rifle (위험)", UiText.ItemLabelWithCondition(Weapon(0f), hud));
        }

        // RULE-04: 내구도 숫자는 보이지 않는다 (이름에 숫자가 없는 무기로 확인)
        [Test]
        public void ItemLabelWithCondition_HasNoDigits()
        {
            foreach (float d in new[] { 0f, 3.5f, 24f, 37f, 49.9f, 50f, 77f, 100f })
            {
                string label = UiText.ItemLabelWithCondition(Weapon(d), hud);
                Assert.IsFalse(label.Any(char.IsDigit), $"숫자 없음: {label}");
            }
        }

        [Test]
        public void ItemLabelWithCondition_NonDurable_OrNoConfig_IsItemLabel()
        {
            var magazine = new Magazine(TestData.Magazine30(), 10);
            var wires = new ItemInstance(TestData.Wires());
            Assert.AreEqual(UiText.ItemLabel(magazine), UiText.ItemLabelWithCondition(magazine, hud));
            Assert.AreEqual("전선 뭉치", UiText.ItemLabelWithCondition(wires, hud));
            Assert.AreEqual("Rifle", UiText.ItemLabelWithCondition(Weapon(10f), null), "경계 없음 → 이름만");
            Assert.AreEqual(string.Empty, UiText.ItemLabelWithCondition(null, hud));
        }
    }

    // 매도가 × 비율·수리비 (MF-Q12·Q13)
    public class DurabilityPriceRulesTests
    {
        private FakeCatalog catalog;
        private TraderConfigSO trader;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
            trader = M5TestData.TraderConfig(catalog);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(trader);

        private WeaponItem Rifle(float durability, int loadedRounds = -1)
        {
            WeaponItem w = M5TestData.Rifle(catalog, loadedRounds);
            w.Durability = durability;
            return w;
        }

        [Test]
        public void TraderConfig_DefaultRepairCostRatio()
        {
            var fresh = ScriptableObject.CreateInstance<TraderConfigSO>();
            Assert.AreEqual(0.5f, fresh.repairCostRatio);
            Assert.GreaterOrEqual(fresh.repairCostRatio, fresh.sellPriceMultiplier);
            Object.DestroyImmediate(fresh);
        }

        [Test]
        public void SellPrice_ScaledByDurability_MagazineFull()
        {
            Assert.AreEqual(1600, PriceRules.SellPrice(Rifle(100f), trader), "100% = 기존 값");
            Assert.AreEqual(800, PriceRules.SellPrice(Rifle(50f), trader));                 // 4000 × 0.4 × 0.5
            Assert.AreEqual(800 + 160, PriceRules.SellPrice(Rifle(50f, 17), trader), "장착 탄창은 비율과 무관");
            Assert.AreEqual(639, PriceRules.SellPrice(Rifle(39.99f), trader));              // 639.84 → 639
            Assert.AreEqual(0, PriceRules.SellPrice(Rifle(0f), trader), "0% → 0 탄피 (처분)");
            Assert.AreEqual(160, PriceRules.SellPrice(Rifle(0f, 0), trader));
        }

        [Test]
        public void RepairCost_Formula()
        {
            Assert.AreEqual(1000, PriceRules.RepairCost(Rifle(50f), trader));    // 4000 × 0.5 × 0.5
            Assert.AreEqual(2000, PriceRules.RepairCost(Rifle(0f), trader));     // 4000 × 0.5 × 1
            Assert.AreEqual(1500, PriceRules.RepairCost(Rifle(25f, 30), trader), "장착 탄창은 수리비에 없음");
            Assert.AreEqual(41, PriceRules.RepairCost(Rifle(97.99f), trader));   // 40.2 → 41 (올림)
        }

        [Test]
        public void RepairCost_DesignExample_625()
        {
            WeaponItem w = Rifle(50f);
            w.Definition.basePrice = 2500;
            Assert.AreEqual(625, PriceRules.RepairCost(w, trader));
            Assert.AreEqual(500, PriceRules.SellPrice(w, trader));
        }

        [Test]
        public void RepairCost_MinimumOne()
        {
            Assert.AreEqual(1, PriceRules.RepairCost(Rifle(99.99f), trader));    // 0.2 → 1
            trader.repairCostRatio = 0f;
            Assert.AreEqual(1, PriceRules.RepairCost(Rifle(10f), trader));
        }

        [Test]
        public void RepairCost_NotNeeded_MinusOne()
        {
            Assert.AreEqual(-1, PriceRules.RepairCost(Rifle(100f), trader), "100% = -1");
            Assert.AreEqual(-1, PriceRules.RepairCost(M5TestData.Mag556(catalog, 10), trader), "내구도 없음");
            Assert.AreEqual(-1, PriceRules.RepairCost(M5TestData.Bandage(catalog), trader));
            Assert.AreEqual(-1, PriceRules.RepairCost(null, trader));
            Assert.AreEqual(-1, PriceRules.RepairCost(Rifle(50f), null));
        }

        // 수리 후 되팔기로 탄피가 늘지 않는다 (repairCostRatio ≥ sellPriceMultiplier)
        [Test]
        public void RepairThenSell_NeverProfits()
        {
            foreach (float d in new[] { 0f, 1f, 10f, 24.5f, 50f, 73.3f, 99f, 99.99f })
            {
                WeaponItem w = Rifle(d, 30);
                int before = PriceRules.SellPrice(w, trader);
                int cost = PriceRules.RepairCost(w, trader);
                w.Durability = WeaponItem.MaxDurability;
                int after = PriceRules.SellPrice(w, trader);
                Assert.LessOrEqual(after - before, cost, $"내구도 {d}");
            }
        }

        [Test]
        public void BuyPrice_Unchanged()
        {
            Assert.AreEqual(4000, PriceRules.BuyPrice(catalog.Get("WPN-RIF-01"), trader), "상인은 새 물건만 판다 (비율 1)");
        }
    }

    // TraderModel.TryRepair·HideoutService 위임 (MF-Q13·Q14)
    public class TraderRepairTests
    {
        private FakeCatalog catalog;
        private TraderConfigSO trader;
        private PlayerProfile profile;
        private TraderModel model;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
            trader = M5TestData.TraderConfig(catalog);
            profile = new PlayerProfile(M5TestData.StashSize, 5000);
            model = new TraderModel(profile, trader);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(trader);

        private WeaponItem StashRifle(float durability)
        {
            WeaponItem w = M5TestData.Rifle(catalog);
            w.Durability = durability;
            Assert.IsTrue(profile.Stash.TryPlace(w, new Vector2Int(2, 3), false));
            return w;
        }

        [Test]
        public void TryRepair_Ok_DeductsCasings_RestoresFull_KeepsPlace()
        {
            WeaponItem w = StashRifle(50f);
            Assert.AreEqual(1000, model.GetRepairCost(w));
            Assert.AreEqual(800, model.GetSellPrice(w));

            Assert.AreEqual(RepairResult.Ok, model.TryRepair(w));
            Assert.AreEqual(4000, profile.Casings);
            Assert.AreEqual(100f, w.Durability);
            Assert.IsTrue(profile.Stash.TryGetPlacement(w, out PlacedItem placed));
            Assert.AreEqual(new Vector2Int(2, 3), placed.Cell);
            Assert.AreEqual(1600, model.GetSellPrice(w), "수리 후 매도가 원래대로");
            Assert.AreEqual(-1, model.GetRepairCost(w));
            Assert.AreEqual(RepairResult.NotNeeded, model.TryRepair(w));
            Assert.AreEqual(4000, profile.Casings);
        }

        [Test]
        public void TryRepair_NotEnoughCasings_NoChange()
        {
            WeaponItem w = StashRifle(0f);   // 2000
            profile.Casings = 1999;
            Assert.AreEqual(RepairResult.NotEnoughCasings, model.TryRepair(w));
            Assert.AreEqual(1999, profile.Casings);
            Assert.AreEqual(0f, w.Durability);
        }

        [Test]
        public void TryRepair_NotNeeded_NonDurable()
        {
            Magazine mag = M5TestData.Mag556(catalog, 5);
            Assert.IsTrue(StashRules.TryPlaceNew(profile.Stash, mag));
            Assert.AreEqual(RepairResult.NotNeeded, model.TryRepair(mag));
            Assert.AreEqual(-1, model.GetRepairCost(mag));
            Assert.AreEqual(5000, profile.Casings);
        }

        [Test]
        public void TryRepair_NotInStash()
        {
            WeaponItem loose = M5TestData.Rifle(catalog);
            loose.Durability = 10f;
            Assert.AreEqual(RepairResult.NotInStash, model.TryRepair(loose));
            Assert.AreEqual(RepairResult.NotInStash, model.TryRepair(null));
            Assert.AreEqual(-1, model.GetRepairCost(loose), "보관함 밖 = -1");
            Assert.AreEqual(10f, loose.Durability);
            Assert.AreEqual(5000, profile.Casings);
        }
    }

    public class HideoutServiceRepairTests
    {
        private MemorySaveStorage storage;
        private FakeCatalog catalog;
        private FakeClock clock;
        private ProfileService profiles;
        private FlowRecorder rec;
        private GameFlow flow;
        private HideoutConfigSO config;
        private TraderConfigSO trader;
        private int changedCount;

        [SetUp]
        public void SetUp()
        {
            storage = new MemorySaveStorage();
            catalog = FakeCatalog.Standard();
            clock = new FakeClock();
            profiles = new ProfileService(storage, clock.Now);
            rec = new FlowRecorder();
            flow = rec.Create(profiles);
            config = M5TestData.HideoutConfig(catalog);
            trader = M5TestData.TraderConfig(catalog);
            changedCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(trader);
        }

        private HideoutService InHideout()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            Assert.IsTrue(flow.EnterHideout());
            flow.OnSceneLoaded(SceneNames.Hideout);
            HideoutService.Enter(profiles, flow, catalog, config.ToProfileDefaults());
            return new HideoutService(profiles, flow, config, trader, () => null, clock.Now, () => changedCount++);
        }

        private WeaponItem PlaceWornRifle(float durability)
        {
            WeaponItem w = M5TestData.Rifle(catalog);
            w.Durability = durability;
            Assert.IsTrue(StashRules.TryPlaceNew(profiles.Profile.Stash, w));
            return w;
        }

        [Test]
        public void TryRepair_Delegates_PublishesChanged()
        {
            HideoutService service = InHideout();
            profiles.Profile.Casings = 3000;
            WeaponItem w = PlaceWornRifle(50f);
            Assert.AreEqual(1000, service.GetRepairCost(w));
            int changed = changedCount;

            Assert.AreEqual(RepairResult.Ok, service.TryRepair(w));
            Assert.AreEqual(changed + 1, changedCount);
            Assert.AreEqual(2000, service.Casings);
            Assert.AreEqual(100f, w.Durability);

            Assert.AreEqual(RepairResult.NotNeeded, service.TryRepair(w));
            Assert.AreEqual(changed + 1, changedCount, "실패는 알리지 않는다");
        }

        [Test]
        public void TryRepair_Leaving_Unavailable()
        {
            HideoutService service = InHideout();
            profiles.Profile.Casings = 3000;
            WeaponItem w = PlaceWornRifle(50f);
            Assert.IsTrue(service.StartRaid());
            Assert.AreEqual(RepairResult.Unavailable, service.TryRepair(w));
            Assert.AreEqual(50f, w.Durability);
        }

        // 수리 내구도는 다음 저장(W2/W3/W4)에 기록된다
        [Test]
        public void Repair_SavedOnNextSave()
        {
            HideoutService service = InHideout();
            profiles.Profile.Casings = 3000;
            WeaponItem w = PlaceWornRifle(20f);
            Assert.IsTrue(service.SaveNow());
            Assert.AreEqual(20f, MinStashWeaponDurability(), 1e-3f, "수리 전 기록");

            int writes = storage.WriteCount;
            Assert.AreEqual(RepairResult.Ok, service.TryRepair(w));
            Assert.AreEqual(writes, storage.WriteCount, "수리 자체는 저장하지 않는다");
            Assert.IsTrue(service.SaveNow());
            Assert.AreEqual(100f, MinStashWeaponDurability(), 1e-3f, "수리 후 기록");
        }

        // 저장된 파일을 새로 읽어 보관함 무기 중 가장 낮은 내구도
        private float MinStashWeaponDurability()
        {
            var next = new ProfileService(storage, clock.Now);
            next.EnsureLoaded(catalog, config.ToProfileDefaults());
            return next.Profile.Stash.Items.Where(p => p.Item is WeaponItem).Min(p => ((WeaponItem)p.Item).Durability);
        }
    }

    // 상인 매도 줄 등급·[수리] (TraderViewModel·HideoutPresenter·TraderView) + 인벤토리 칸 등급 (InventoryScreenView)
    public class DurabilityUiTests
    {
        private HudConfigSO hud;
        private FakeHideoutService hideout;
        private GameFlow flow;

        [SetUp]
        public void SetUp()
        {
            hud = ScriptableObject.CreateInstance<HudConfigSO>();
            hideout = new FakeHideoutService { Casings = 1500 };
            flow = new FlowRecorder().Create();
            flow.OnSceneLoaded(SceneNames.Hideout);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(hud);

        private WeaponItem AddWeapon(float durability, int x, int y, int sellPrice, int repairCost)
        {
            var w = new WeaponItem(TestData.Rifle()) { Durability = durability };
            hideout.StashGrid.Placed.Add(new PlacedItem(w, new Vector2Int(x, y), false));
            hideout.SellPrices[w] = sellPrice;
            if (repairCost >= 0)
                hideout.RepairCosts[w] = repairCost;
            return w;
        }

        private static VisualElement Clone()
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HideoutUxmlTests.HideoutUxml);
            Assert.IsNotNull(asset);
            return asset.CloneTree();
        }

        [Test]
        public void TraderViewModel_SellRows_ConditionAndRepair()
        {
            AddWeapon(100f, 0, 0, 1600, -1);
            AddWeapon(30f, 4, 0, 480, 1400);
            AddWeapon(10f, 0, 2, 160, 1800);
            hideout.AddToStash(TestData.Pipe(), 4, 2, 16);

            TraderViewModel vm = TraderViewModel.Build(hideout, hud);
            Assert.AreEqual(4, vm.Sell.Count);
            Assert.AreEqual("Rifle (양호)", vm.Sell[0].Name);
            Assert.IsFalse(vm.Sell[0].NeedsRepair);
            Assert.IsFalse(vm.Sell[0].CanRepair);
            Assert.AreEqual("Rifle (낡음)", vm.Sell[1].Name);
            Assert.IsTrue(vm.Sell[1].NeedsRepair);
            Assert.IsTrue(vm.Sell[1].CanRepair);
            Assert.AreEqual("수리 (1400 탄피)", vm.Sell[1].RepairText);
            Assert.AreEqual("Rifle (위험)", vm.Sell[2].Name);
            Assert.IsTrue(vm.Sell[2].NeedsRepair);
            Assert.IsFalse(vm.Sell[2].CanRepair, "탄피 부족");
            Assert.AreEqual("녹슨 파이프", vm.Sell[3].Name);
            Assert.IsFalse(vm.Sell[3].NeedsRepair);

            Assert.AreEqual("Rifle", TraderViewModel.Build(hideout).Sell[0].Name, "경계 없음 → 이름만 (기존 호출)");
            hideout.IsLeaving = true;
            Assert.IsFalse(TraderViewModel.Build(hideout, hud).Sell[1].CanRepair, "떠나는 중");
        }

        [Test]
        public void Presenter_Repair_CallsService_Message()
        {
            WeaponItem w = AddWeapon(30f, 0, 0, 480, 1400);
            var p = new HideoutPresenter(hideout, flow, hud);
            Assert.AreEqual("Rifle (낡음)", p.Trader.Sell[0].Name);

            Assert.IsTrue(p.Repair(w));
            CollectionAssert.Contains(hideout.Calls, "Repair:WPN-RIF-01");
            Assert.AreEqual("수리했다: Rifle (양호)", p.Message);
            Assert.AreEqual("Rifle (양호)", p.Trader.Sell[0].Name, "Refresh 뒤 다시 만든다");
            Assert.IsFalse(p.Trader.Sell[0].NeedsRepair);

            hideout.RepairResultToReturn = RepairResult.NotEnoughCasings;
            Assert.IsFalse(p.Repair(w));
            Assert.AreEqual(UiText.MessageNotEnoughCasings, p.Message);
            hideout.RepairResultToReturn = RepairResult.NotNeeded;
            Assert.IsFalse(p.Repair(w));
            Assert.AreEqual(UiText.MessageRepairNotNeeded, p.Message);
            hideout.RepairResultToReturn = RepairResult.NotInStash;
            Assert.IsFalse(p.Repair(w));
            Assert.AreEqual(UiText.MessageRepairNotInStash, p.Message);
            Assert.IsFalse(p.Repair(null));
        }

        [Test]
        public void Presenter_Repair_Leaving_Ignored()
        {
            WeaponItem w = AddWeapon(30f, 0, 0, 480, 1400);
            var p = new HideoutPresenter(hideout, flow, hud);
            hideout.IsLeaving = true;
            Assert.IsFalse(p.Repair(w));
            CollectionAssert.DoesNotContain(hideout.Calls, "Repair:WPN-RIF-01");
        }

        [Test]
        public void TraderView_RepairButton_ShownOnlyWhenNeeded()
        {
            VisualElement root = Clone();
            WeaponItem worn = AddWeapon(30f, 0, 0, 480, 1400);
            AddWeapon(100f, 4, 0, 1600, -1);
            var p = new HideoutPresenter(hideout, flow, hud);
            var repairs = new List<ItemInstance>();
            var trader = new TraderView(root, _ => { }, _ => { }, repairs.Add);
            trader.SetVisible(true);
            trader.Render(p.Trader, p.HubEnabled);

            VisualElement sell = root.Q(TraderView.SellName);
            Assert.AreEqual(2, sell.childCount);
            Assert.AreEqual("Rifle (낡음)", sell[0].Q<Label>(className: TraderView.RowNameClass).text);
            Assert.AreEqual(UiText.TraderSell, sell[0].Q<Button>().text, "줄의 첫 버튼은 [판매]");
            Button repair = sell[0].Q<Button>(className: TraderView.RowRepairClass);
            Assert.IsNotNull(repair);
            Assert.AreEqual(DisplayStyle.Flex, repair.style.display.value);
            Assert.IsTrue(repair.enabledSelf);
            Assert.AreEqual("수리 (1400 탄피)", repair.text);
            Assert.IsTrue(repair.ClassListContains("ash-button"));

            Button hidden = sell[1].Q<Button>(className: TraderView.RowRepairClass);
            Assert.AreEqual(DisplayStyle.None, hidden.style.display.value, "100% → [수리] 숨김");
            Assert.AreEqual("Rifle (양호)", sell[1].Q<Label>(className: TraderView.RowNameClass).text);

            hideout.Casings = 100;
            p.Refresh();
            trader.Render(p.Trader, p.HubEnabled);
            Assert.IsFalse(root.Q(TraderView.SellName)[0].Q<Button>(className: TraderView.RowRepairClass).enabledSelf, "탄피 부족 → 비활성");
            CollectionAssert.IsEmpty(repairs);
            Assert.IsNotNull(worn);
        }

        [Test]
        public void InventoryView_GridAndSlotLabels_ShowCondition()
        {
            VisualElement root = Clone();
            var inventory = new InventoryScreenView(root, 4f, new[] { "4", "5", "6", "7" }, () => false, hud);
            var inv = new FakeInventoryService { Loot = new FakeLootView(new Vector2Int(10, 30), "보관함") };
            inv.Grids.Add(new FakeGridView(new GridRef(GridKind.Pocket, 0), new Vector2Int(1, 1)));
            var stashWeapon = new WeaponItem(TestData.Rifle()) { Durability = 40f };
            var pipe = new ItemInstance(TestData.Pipe());
            inv.Loot.Placed.Add(new PlacedItem(stashWeapon, Vector2Int.zero, false));
            inv.Loot.Placed.Add(new PlacedItem(pipe, new Vector2Int(0, 3), false));
            inv.Loot.Revealed.Add(stashWeapon);
            inv.Loot.Revealed.Add(pipe);
            var pistol = new WeaponItem(TestData.Pistol()) { Durability = 10f };
            inv.Equipped[EquipSlot.Secondary] = pistol;
            inventory.Bind(inv, new InventoryDragModel(inv));
            inventory.SetVisible(true);
            inventory.Render();

            List<string> stashLabels = root.Q(InventoryScreenView.LootGridName)
                .Query<Label>(className: InventoryScreenView.ItemClass).ToList().Select(l => l.text).ToList();
            CollectionAssert.AreEquivalent(new[] { "Rifle (낡음)", "녹슨 파이프" }, stashLabels);
            List<string> slotLabels = root.Q(InventoryScreenView.EquipSlotsName)
                .Query<Label>(className: InventoryScreenView.ItemClass).ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(slotLabels, "Pistol (위험)");

            // 경계 없이 만든 뷰(기존 호출) = 이름만
            inventory.ConditionConfig = null;
            inventory.Render();
            CollectionAssert.Contains(root.Q(InventoryScreenView.LootGridName)
                .Query<Label>(className: InventoryScreenView.ItemClass).ToList().Select(l => l.text).ToList(), "Rifle");
            inventory.Dispose();
        }
    }

    // 에셋 명시 기록 (MvpFinishDataSetup.ApplyTraderAndHud, 설계 §1.7) + 재실행 바이트 동일 + 경제 고정
    public class MvpFinishF3AssetTests
    {
        private const string TraderConfigPath = "Assets/_Project/Data/Hideout/TraderConfig.asset";
        private const string HudConfigPath = "Assets/_Project/Data/UI/HudConfig.asset";

        [Test]
        public void TraderConfigAsset_RepairCostRatio_AtLeastSellMultiplier()
        {
            Assert.IsTrue(SetupUtil.IsFieldSerialized(TraderConfigPath, nameof(TraderConfigSO.repairCostRatio)), "파일에 명시 기록");
            var trader = AssetDatabase.LoadAssetAtPath<TraderConfigSO>(TraderConfigPath);
            Assert.IsNotNull(trader);
            Assert.AreEqual(0.5f, trader.repairCostRatio);
            Assert.GreaterOrEqual(trader.repairCostRatio, trader.sellPriceMultiplier, "수리 후 되팔기 이득 없음");
        }

        [Test]
        public void HudConfigAsset_ConditionThresholds()
        {
            Assert.IsTrue(SetupUtil.IsFieldSerialized(HudConfigPath, nameof(HudConfigSO.conditionWornBelow)));
            Assert.IsTrue(SetupUtil.IsFieldSerialized(HudConfigPath, nameof(HudConfigSO.conditionCriticalBelow)));
            var hud = AssetDatabase.LoadAssetAtPath<HudConfigSO>(HudConfigPath);
            Assert.IsNotNull(hud);
            Assert.AreEqual(0.5f, hud.conditionWornBelow);
            Assert.AreEqual(0.25f, hud.conditionCriticalBelow);
            Assert.LessOrEqual(hud.conditionCriticalBelow, hud.conditionWornBelow);
        }

        [Test]
        public void ApplyTraderAndHud_Rerun_KeepsBytes()
        {
            string trader = Path.GetFullPath(TraderConfigPath);
            string hud = Path.GetFullPath(HudConfigPath);
            byte[] traderBefore = File.ReadAllBytes(trader);
            byte[] hudBefore = File.ReadAllBytes(hud);
            MvpFinishDataSetup.ApplyTraderAndHud();
            AssetDatabase.SaveAssets();
            CollectionAssert.AreEqual(traderBefore, File.ReadAllBytes(trader));
            CollectionAssert.AreEqual(hudBefore, File.ReadAllBytes(hud));
        }
    }
}
