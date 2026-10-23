using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ashfall.Tests.EditMode
{
    // M7 7D-2 §6 희귀도 색 (설계 §3.11·§4.8, Q49 A): Rarity → USS 클래스, USS 테두리 색, 인벤토리·보관함·퀵슬롯·상인 칸에 붙는지.
    public class RarityStyleTests
    {
        private const string UssPath = "Assets/_Project/Art/UI/Ashfall.uss";

        [Test]
        public void ClassFor_EveryRarity_DistinctClass_InEnumOrder()
        {
            var values = (Rarity[])Enum.GetValues(typeof(Rarity));
            Assert.AreEqual(values.Length, RarityStyle.AllClasses.Length, "희귀도마다 클래스 하나");
            for (int i = 0; i < values.Length; i++)
                Assert.AreEqual(RarityStyle.AllClasses[(int)values[i]], RarityStyle.ClassFor(values[i]), values[i].ToString());
            Assert.AreEqual(values.Length, RarityStyle.AllClasses.Distinct().Count());
            Assert.AreEqual("rarity--scrap", RarityStyle.ClassFor(Rarity.Scrap));
            Assert.AreEqual("rarity--relic", RarityStyle.ClassFor(Rarity.Relic));
        }

        [Test]
        public void ClassFor_DefinitionAndInstance_NullSafe()
        {
            Assert.IsNull(RarityStyle.ClassFor((ItemDefinitionSO)null));
            Assert.IsNull(RarityStyle.ClassFor((ItemInstance)null));
            ItemDefinitionSO def = TestData.Item("T-1", "t", Vector2Int.one, 1f, Rarity.Rare);
            Assert.AreEqual(RarityStyle.RareClass, RarityStyle.ClassFor(def));
            Assert.AreEqual(RarityStyle.RareClass, RarityStyle.ClassFor(new ItemInstance(def)));
        }

        [Test]
        public void Apply_KeepsExactlyOneRarityClass_NullClears()
        {
            var e = new VisualElement();
            e.AddToClassList("other");
            RarityStyle.Apply(e, RarityStyle.CommonClass);
            RarityStyle.Apply(e, RarityStyle.RelicClass);
            Assert.IsTrue(e.ClassListContains(RarityStyle.RelicClass));
            Assert.IsTrue(e.ClassListContains(RarityStyle.BaseClass));
            Assert.AreEqual(1, RarityStyle.AllClasses.Count(e.ClassListContains), "희귀도 클래스는 하나만");
            RarityStyle.Apply(e, (string)null);
            Assert.AreEqual(0, RarityStyle.AllClasses.Count(e.ClassListContains));
            Assert.IsFalse(e.ClassListContains(RarityStyle.BaseClass));
            Assert.IsTrue(e.ClassListContains("other"), "다른 클래스는 건드리지 않는다");
            Assert.DoesNotThrow(() => RarityStyle.Apply(null, RarityStyle.RareClass));
        }

        // Q49 A: 고철 Ash_Grey, 일반 Bone, 우수 Toxic, 희귀 = 청색 상수, 유물 Ember (+ 두꺼운 테두리 = 발광 근사)
        [TestCase("rarity--scrap", "--ash-grey")]
        [TestCase("rarity--common", "--bone")]
        [TestCase("rarity--uncommon", "--toxic")]
        [TestCase("rarity--rare", "--rarity-rare-blue")]
        [TestCase("rarity--relic", "--ember")]
        public void Uss_RarityBorderColors_FollowQ49(string rarityClass, string variable)
        {
            string uss = StripComments(File.ReadAllText(Path.GetFullPath(UssPath)));
            foreach (string owner in new[] { ".inv-item", ".inv-quick", ".trader-row-name" })
            {
                string body = RuleBody(uss, owner + "." + rarityClass);
                Assert.IsNotNull(body, $"{owner}.{rarityClass} 규칙 없음");
                StringAssert.Contains($"border-color: var({variable})", body, $"{owner}.{rarityClass}");
            }
            if (rarityClass == RarityStyle.RelicClass)
                StringAssert.Contains("border-width: 2px", RuleBody(uss, ".inv-item." + rarityClass), "유물 = 두꺼운 테두리(발광 근사)");
        }

        // 7D 리뷰 낮음 8: enum 범위 밖 값은 예외 대신 null(기본 테두리) — 화면 전체가 실패하지 않게
        [Test]
        public void ClassFor_OutOfRange_ReturnsNull()
        {
            Assert.IsNull(RarityStyle.ClassFor((Rarity)99));
            Assert.IsNull(RarityStyle.ClassFor((Rarity)(-1)));
            var e = new VisualElement();
            RarityStyle.Apply(e, RarityStyle.RareClass);
            Assert.DoesNotThrow(() => RarityStyle.Apply(e, RarityStyle.ClassFor((Rarity)99)));
            Assert.AreEqual(0, RarityStyle.AllClasses.Count(e.ClassListContains), "범위 밖 = 클래스 없음");
        }

        // 7D 리뷰 중간 1: 드래그 고스트 "패널 밖 = 버리기"(M2) Ember 테두리가 희귀도 테두리 색에 덮이지 않는다.
        // UI Toolkit 우선순위 = 선택자 구체성(클래스 수), 같으면 뒤 규칙. 고스트는 .inv-item + .inv-ghost + 희귀도 클래스를 같이 단다.
        [Test]
        public void Uss_GhostOutsideEmber_WinsOverRarityBorder()
        {
            string uss = StripComments(File.ReadAllText(Path.GetFullPath(UssPath)));
            var rules = Regex.Matches(uss, @"([^{}]+)\{([^}]*)\}").Cast<Match>()
                .Select((m, index) => (index, selectors: m.Groups[1].Value.Split(',').Select(s => s.Trim()).ToArray(), body: m.Groups[2].Value))
                .ToList();

            // 고스트 요소가 실제로 다는 클래스 (패널 밖 + 희귀도 하나)
            var outsideRules = rules.Where(r => r.body.Contains("border-color: var(--ember)")
                                                && r.selectors.Any(s => s.Contains(".inv-ghost--outside"))).ToList();
            Assert.IsNotEmpty(outsideRules, ".inv-ghost--outside Ember 테두리 규칙 없음");

            foreach (string rarityClass in RarityStyle.AllClasses)
            {
                var ghostClasses = new HashSet<string> { "inv-item", "inv-ghost", "inv-ghost--outside", "rarity", rarityClass };
                // 고스트에 맞는 규칙 중 border-color를 정하는 것: 구체성 최대, 같으면 뒤 규칙이 이긴다
                var winner = rules
                    .SelectMany(r => r.selectors.Select(s => (r.index, selector: s, r.body)))
                    .Where(x => x.body.Contains("border-color:") && Matches(x.selector, ghostClasses))
                    .OrderBy(x => ClassCount(x.selector)).ThenBy(x => x.index)
                    .Last();
                StringAssert.Contains("border-color: var(--ember)", winner.body, $"{rarityClass} 고스트 패널 밖 → Ember (이긴 규칙 {winner.selector})");
                StringAssert.Contains("inv-ghost--outside", winner.selector, $"{rarityClass}: 패널 밖 규칙이 이겨야 한다 (이긴 규칙 {winner.selector})");
            }
        }

        // 단순 복합 클래스 선택자(.a.b.c)만 다룬다 — 고스트에 맞을 수 있는 규칙은 전부 이 꼴이다. 다른 꼴(자손·태그·의사 클래스)은 맞지 않는 것으로 본다.
        private static bool Matches(string selector, HashSet<string> classes)
        {
            if (!Regex.IsMatch(selector, @"^(\.[A-Za-z0-9_-]+)+$"))
                return false;
            return selector.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).All(classes.Contains);
        }

        private static int ClassCount(string selector) => selector.Count(ch => ch == '.');

        // 인벤토리 그리드·장비 슬롯·퀵슬롯·보관함(열린 루트 자리)·상인 매수/매도 줄에 희귀도 클래스, 검색 전 "?"엔 없음
        [Test]
        public void Views_ApplyRarityClasses_ToItemCells_AndTraderRows()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HideoutUxmlTests.HideoutUxml);
            Assert.IsNotNull(asset);
            VisualElement root = asset.CloneTree();

            ItemDefinitionSO relicDef = TestData.Item("ITM-REL-01", "유물", Vector2Int.one, 1f, Rarity.Relic);
            ItemDefinitionSO rareDef = TestData.Item("T-RARE", "희귀", Vector2Int.one, 1f, Rarity.Rare);
            ItemDefinitionSO uncommonDef = TestData.Item("T-UNC", "우수", Vector2Int.one, 1f, Rarity.Uncommon);
            ItemDefinitionSO commonDef = TestData.Item("T-COM", "일반", Vector2Int.one, 1f, Rarity.Common);

            // 인벤토리 (보관함 = 열린 루트 자리)
            var inv = new FakeInventoryService { Loot = new FakeLootView(new Vector2Int(4, 4), "보관함") };
            var pocket = new FakeGridView(new GridRef(GridKind.Pocket, 0), new Vector2Int(2, 2));
            inv.Grids.Add(pocket);
            var pipe = new ItemInstance(TestData.Pipe());   // Scrap
            pocket.Placed.Add(new PlacedItem(pipe, Vector2Int.zero, false));
            var relic = new ItemInstance(relicDef);
            var hidden = new ItemInstance(rareDef);
            inv.Loot.Placed.Add(new PlacedItem(relic, Vector2Int.zero, false));
            inv.Loot.Placed.Add(new PlacedItem(hidden, new Vector2Int(1, 0), false));
            inv.Loot.Revealed.Add(relic);
            var helmet = new ItemInstance(uncommonDef);
            inv.Equipped[EquipSlot.Helmet] = helmet;
            inv.QuickSlots[0] = new ItemInstance(commonDef);

            var inventory = new InventoryScreenView(root, 4f, new[] { "4", "5", "6", "7" }, () => false);
            inventory.Bind(inv, new InventoryDragModel(inv));
            inventory.SetVisible(true);
            inventory.Render();

            Label pipeLabel = ItemLabel(root.Q(InventoryScreenView.StorageName), pipe);
            Assert.IsTrue(pipeLabel.ClassListContains(RarityStyle.ScrapClass), "소지품 그리드 = 고철");
            VisualElement stash = root.Q(InventoryScreenView.LootGridName);
            Assert.IsTrue(ItemLabel(stash, relic).ClassListContains(RarityStyle.RelicClass), "보관함 = 유물");
            Label hiddenLabel = stash.Query<Label>(className: InventoryScreenView.HiddenItemClass).First();
            Assert.IsNotNull(hiddenLabel);
            Assert.AreEqual(0, RarityStyle.AllClasses.Count(hiddenLabel.ClassListContains), "검색 전 아이템은 희귀도를 드러내지 않는다");
            Assert.IsTrue(ItemLabel(root.Q(InventoryScreenView.EquipSlotsName), helmet).ClassListContains(RarityStyle.UncommonClass), "장비 슬롯 = 우수");
            VisualElement quick = root.Q(InventoryScreenView.QuickSlotsName);
            Assert.IsTrue(quick[0].ClassListContains(RarityStyle.CommonClass), "퀵슬롯 = 일반");
            Assert.AreEqual(0, RarityStyle.AllClasses.Count(quick[1].ClassListContains), "빈 퀵슬롯은 클래스 없음");

            // 상인
            var hideout = new FakeHideoutService();
            hideout.OfferList.Add(new TraderOffer(rareDef, 60));
            hideout.OfferList.Add(new TraderOffer(commonDef, 10));
            hideout.AddToStash(relicDef, 0, 0, 100);
            var rec = new FlowRecorder();
            GameFlow flow = rec.Create();
            flow.OnSceneLoaded(SceneNames.Hideout);
            var presenter = new HideoutPresenter(hideout, flow);
            var trader = new TraderView(root, _ => { }, _ => { });
            trader.Render(presenter.Trader, presenter.HubEnabled);
            VisualElement offers = root.Q(TraderView.OffersName);
            Assert.IsTrue(offers[0].Q<Label>(className: TraderView.RowNameClass).ClassListContains(RarityStyle.RareClass));
            Assert.IsTrue(offers[1].Q<Label>(className: TraderView.RowNameClass).ClassListContains(RarityStyle.CommonClass));
            Assert.IsTrue(root.Q(TraderView.SellName)[0].Q<Label>(className: TraderView.RowNameClass).ClassListContains(RarityStyle.RelicClass));
        }

        private static Label ItemLabel(VisualElement scope, ItemInstance item)
        {
            Assert.IsNotNull(scope);
            Label label = scope.Query<Label>(className: InventoryScreenView.ItemClass).ToList()
                .FirstOrDefault(l => l.userData != null && ReferenceEquals(ItemOf(l.userData), item));
            Assert.IsNotNull(label, $"{item} 칸 없음");
            return label;
        }

        // ItemTag는 뷰 내부 타입 → 필드 "Item"을 리플렉션으로 읽는다
        private static object ItemOf(object tag) =>
            tag.GetType().GetField("Item")?.GetValue(tag);

        internal static string StripComments(string uss) => Regex.Replace(uss, @"/\*.*?\*/", "", RegexOptions.Singleline);

        // selector가 들어 있는 규칙(쉼표 그룹 포함)의 본문. 없으면 null.
        private static string RuleBody(string uss, string selector)
        {
            foreach (Match m in Regex.Matches(uss, @"([^{}]+)\{([^}]*)\}"))
            {
                IEnumerable<string> selectors = m.Groups[1].Value.Split(',').Select(s => s.Trim());
                if (selectors.Contains(selector))
                    return m.Groups[2].Value;
            }
            return null;
        }
    }
}
