using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ashfall.Tests.EditMode
{
    // IHideoutService 가짜 (2b 소유 — 이 파일 안에만). 규칙은 흉내 내지 않고 정해 둔 결과를 돌려주며 호출을 기록한다.
    internal sealed class FakeHideoutService : IHideoutService
    {
        public readonly List<TraderOffer> OfferList = new List<TraderOffer>();
        public FakeGridView StashGrid { get; private set; } = new FakeGridView(new GridRef(GridKind.Loot), new Vector2Int(10, 30));
        public readonly Dictionary<ItemInstance, int> SellPrices = new Dictionary<ItemInstance, int>();
        public readonly List<string> Calls = new List<string>();

        public TradeResult BuyResult = TradeResult.Ok;
        public TradeResult SellResult = TradeResult.Ok;
        public KitClaimResult KitResult = KitClaimResult.Ok;
        public bool StartRaidResult = true;
        public bool ReturnResult = true;
        public bool SaveFailsOnStart;
        public bool SaveFailsOnReturn;

        public int Casings { get; set; } = 6000;
        public string TraderName { get; set; } = "고물상";
        public int OffersReads { get; private set; }   // M5 리뷰 10: 상인 목록을 만든 횟수
        public IReadOnlyList<TraderOffer> Offers
        {
            get
            {
                OffersReads++;
                return OfferList;
            }
        }
        public IGridView Stash => StashGrid;
        public bool IsKitAvailable { get; set; } = true;
        public float KitCooldownRemainingSec { get; set; }
        public SaveLoadReport PendingNotice { get; set; }
        public bool LastSaveFailed { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsLeaving { get; set; }

        public int GetSellPrice(ItemInstance item) =>
            item != null && SellPrices.TryGetValue(item, out int p) ? p : -1;

        public TradeResult TryBuy(ItemDefinitionSO item)
        {
            Calls.Add("Buy:" + item.id);
            return BuyResult;
        }

        public TradeResult TrySell(ItemInstance item)
        {
            Calls.Add("Sell:" + item.Definition.id);
            return SellResult;
        }

        public KitClaimResult TryClaimKit()
        {
            Calls.Add("Kit");
            return KitResult;
        }

        // M7 7C-1: 제작대. 정해 둔 레시피·결과를 돌려주고 호출을 기록한다.
        public readonly List<CraftingRecipeView> RecipeList = new List<CraftingRecipeView>();
        public CraftResult CraftResultToReturn = CraftResult.Ok;
        public IReadOnlyList<CraftingRecipeView> Recipes => RecipeList;

        public CraftResult TryCraft(string recipeId)
        {
            Calls.Add("Craft:" + recipeId);
            return CraftResultToReturn;
        }

        // MVP 마무리 F2: 보관함 업그레이드. 정해 둔 다음 단계·결과를 돌려주고 호출을 기록한다.
        // UpgradeResult = Ok면 StashGrid 크기를 NextStashUpgrade.NewSize로 바꾸고 단계 +1(탄피·다음 단계는 테스트가 정한다).
        public int StashLevel { get; set; }
        public StashUpgradeOffer NextStashUpgrade { get; set; } = new StashUpgradeOffer(new Vector2Int(10, 40), 5000);
        public StashUpgradeResult UpgradeResult = StashUpgradeResult.Ok;

        public StashUpgradeResult TryUpgradeStash()
        {
            Calls.Add("UpgradeStash");
            if (UpgradeResult == StashUpgradeResult.Ok && NextStashUpgrade.HasNext)
            {
                var grown = new FakeGridView(StashGrid.Ref, NextStashUpgrade.NewSize);
                grown.Placed.AddRange(StashGrid.Placed);
                StashGrid = grown;
                StashLevel++;
            }
            return UpgradeResult;
        }

        // MVP 마무리 F3: 상인 수리. RepairCosts에 없으면 -1(수리 불필요). RepairResultToReturn = Ok면 내구도를 최대로 되돌린다.
        public readonly Dictionary<ItemInstance, int> RepairCosts = new Dictionary<ItemInstance, int>();
        public RepairResult RepairResultToReturn = RepairResult.Ok;

        public int GetRepairCost(ItemInstance item) =>
            item != null && RepairCosts.TryGetValue(item, out int c) ? c : -1;

        public RepairResult TryRepair(ItemInstance item)
        {
            Calls.Add("Repair:" + item.Definition.id);
            if (RepairResultToReturn == RepairResult.Ok)
            {
                DurabilityRules.RestoreFull(item);
                RepairCosts.Remove(item);
            }
            return RepairResultToReturn;
        }

        public void AcknowledgeNotice()
        {
            Calls.Add("AckNotice");
            PendingNotice = null;
        }

        public bool StartRaid()
        {
            Calls.Add("StartRaid");
            LastSaveFailed = SaveFailsOnStart;
            if (StartRaidResult)
                IsLeaving = true;
            return StartRaidResult;
        }

        public bool ReturnToMainMenu()
        {
            Calls.Add("ReturnToMainMenu");
            LastSaveFailed = SaveFailsOnReturn;
            if (ReturnResult)
                IsLeaving = true;
            return ReturnResult;
        }

        public ItemInstance AddToStash(ItemDefinitionSO definition, int x, int y, int sellPrice)
        {
            var item = new ItemInstance(definition);
            StashGrid.Placed.Add(new PlacedItem(item, new Vector2Int(x, y), false));
            SellPrices[item] = sellPrice;
            return item;
        }
    }

    // M5 은신처 UI 모델 (M5 설계 §3.4, §4.5, §7 AC1 HideoutUiModelTests): 패널 순서, 탭, 키트 문구, 거래·저장 메시지,
    // 떠나는 중 비활성, 상인 줄, 알림 문구.
    public class HideoutPresenterTests
    {
        private FlowRecorder rec;
        private GameFlow flow;
        private FakeHideoutService hideout;

        [SetUp]
        public void SetUp()
        {
            rec = new FlowRecorder();
            flow = rec.Create();
            flow.OnSceneLoaded(SceneNames.Hideout);
            hideout = new FakeHideoutService();
        }

        private HideoutPresenter Create() => new HideoutPresenter(hideout, flow);

        // 레이드가 끝나 결과를 들고 은신처로 온 상태
        private void ArriveWithResult(RaidResult result)
        {
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            Assert.IsTrue(flow.CompleteRaid(result));
            flow.OnSceneLoaded(SceneNames.Hideout);
            rec.Loads.Clear();
        }

        private static SaveLoadReport CrashReport(params ItemDefinitionSO[] lost)
        {
            var report = new SaveLoadReport { Status = SaveLoadStatus.Loaded, CrashPenaltyApplied = true };
            report.CrashLost.AddRange(lost);
            return report;
        }

        [Test]
        public void Constructor_NullArgs_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new HideoutPresenter(null, flow));
            Assert.Throws<ArgumentNullException>(() => new HideoutPresenter(hideout, null));
        }

        [Test]
        public void NoNoticeNoResult_ShowsHub_LoadoutTab()
        {
            var p = Create();
            Assert.AreEqual(HideoutPanel.Hub, p.Panel);
            Assert.AreEqual(HideoutTab.Loadout, p.Tab);
            Assert.IsNull(p.Notice);
            Assert.IsNull(p.Result);
            Assert.IsTrue(p.HubEnabled);
            Assert.AreEqual("탄피 6000", p.CasingsText);
            Assert.AreEqual(string.Empty, p.Message);
        }

        // 화면 순서: 알림 → 결과 → 허브
        [Test]
        public void PanelOrder_NoticeThenResultThenHub()
        {
            ArriveWithResult(M4TestData.Result(RaidOutcome.Extracted, "EXT-01", "남쪽 도로"));
            hideout.PendingNotice = CrashReport();
            var p = Create();

            Assert.AreEqual(HideoutPanel.Notice, p.Panel);
            Assert.IsNotNull(p.Notice);
            Assert.IsFalse(p.HubEnabled);
            Assert.IsFalse(p.ConfirmResult(), "알림이 먼저");

            Assert.IsTrue(p.ConfirmNotice());
            CollectionAssert.Contains(hideout.Calls, "AckNotice");
            Assert.AreEqual(HideoutPanel.Result, p.Panel);
            Assert.AreEqual("탈출 성공 — 남쪽 도로", p.Result.Title);
            Assert.IsNotNull(flow.PendingResult);

            Assert.IsTrue(p.ConfirmResult());
            Assert.IsNull(flow.PendingResult, "결과 [확인] = AcknowledgeResult");
            Assert.AreEqual(HideoutPanel.Hub, p.Panel);
            Assert.IsFalse(p.ConfirmNotice());
            Assert.IsFalse(p.ConfirmResult());
        }

        [Test]
        public void ResultOnly_ShowsResult()
        {
            ArriveWithResult(M4TestData.Result(RaidOutcome.Killed));
            var p = Create();
            Assert.AreEqual(HideoutPanel.Result, p.Panel);
            Assert.AreEqual("사망 — 머리", p.Result.Title);
            Assert.IsTrue(p.Result.ShowLost);
        }

        // PendingNotice가 있어도 알릴 내용이 없으면(HasNotice false) 알림 패널을 건너뛴다
        [Test]
        public void EmptyNotice_Skipped()
        {
            hideout.PendingNotice = new SaveLoadReport { Status = SaveLoadStatus.Loaded };
            Assert.AreEqual(HideoutPanel.Hub, Create().Panel);
        }

        [Test]
        public void HubCommands_IgnoredWhileNoticeOrResult()
        {
            hideout.PendingNotice = CrashReport();
            var p = Create();
            Assert.IsFalse(p.Buy(TestData.Bandage()));
            Assert.IsFalse(p.ClaimKit());
            Assert.IsFalse(p.StartRaid());
            Assert.IsFalse(p.ReturnToMenu());
            Assert.IsFalse(p.SelectTab(HideoutTab.Trader));
            CollectionAssert.IsEmpty(hideout.Calls);
        }

        [Test]
        public void SelectTab_ChangesAndNotifies()
        {
            var p = Create();
            int changed = 0;
            p.Changed += () => changed++;
            Assert.IsFalse(p.SelectTab(HideoutTab.Loadout), "이미 장비 탭");
            Assert.IsTrue(p.SelectTab(HideoutTab.Trader));
            Assert.AreEqual(HideoutTab.Trader, p.Tab);
            Assert.AreEqual(1, changed);
        }

        [Test]
        public void KitStatus_ReadyOrMinutesRoundedUp()
        {
            var p = Create();
            Assert.AreEqual("받을 수 있다", p.KitStatusText);
            Assert.IsTrue(p.CanClaimKit);

            hideout.IsKitAvailable = false;
            hideout.KitCooldownRemainingSec = 1199f;
            Assert.IsTrue(p.UpdateKitStatus(), "문구가 바뀌면 true");
            Assert.AreEqual("20분 뒤", p.KitStatusText);
            Assert.IsFalse(p.CanClaimKit);
            Assert.IsFalse(p.UpdateKitStatus(), "같은 문구면 false");

            hideout.KitCooldownRemainingSec = 61f;
            p.UpdateKitStatus();
            Assert.AreEqual("2분 뒤", p.KitStatusText);
            hideout.KitCooldownRemainingSec = 60f;
            p.UpdateKitStatus();
            Assert.AreEqual("1분 뒤", p.KitStatusText);
            hideout.KitCooldownRemainingSec = 0.5f;
            p.UpdateKitStatus();
            Assert.AreEqual("1분 뒤", p.KitStatusText, "1분 미만도 1분");
        }

        [Test]
        public void KitRemainingText_RoundsUp()
        {
            Assert.AreEqual("20분 뒤", UiText.KitRemaining(1200f));
            Assert.AreEqual("20분 뒤", UiText.KitRemaining(1140.01f));
            Assert.AreEqual("19분 뒤", UiText.KitRemaining(1140f));
            Assert.AreEqual("1분 뒤", UiText.KitRemaining(0f));
        }

        [TestCase(KitClaimResult.Ok, "부랑자 키트를 보관함에 받았다.")]
        [TestCase(KitClaimResult.OnCooldown, "부랑자 키트를 아직 받을 수 없다.")]
        [TestCase(KitClaimResult.NoSpace, "보관함에 자리가 없어 부랑자 키트를 받을 수 없다.")]
        [TestCase(KitClaimResult.Unavailable, "지금은 할 수 없다.")]
        public void ClaimKit_Message(KitClaimResult result, string message)
        {
            hideout.KitResult = result;
            var p = Create();
            Assert.AreEqual(result == KitClaimResult.Ok, p.ClaimKit());
            Assert.AreEqual(message, p.Message);
            CollectionAssert.AreEqual(new[] { "Kit" }, hideout.Calls);
        }

        [TestCase(TradeResult.NotEnoughCasings, "탄피가 모자란다.")]
        [TestCase(TradeResult.NoSpace, "보관함에 자리가 없다.")]
        [TestCase(TradeResult.NotForSale, "팔지 않는 물건이다.")]
        [TestCase(TradeResult.Unavailable, "지금은 할 수 없다.")]
        public void Buy_Failure_Message(TradeResult result, string message)
        {
            hideout.BuyResult = result;
            var p = Create();
            Assert.IsFalse(p.Buy(TestData.Bandage()));
            Assert.AreEqual(message, p.Message);
        }

        [Test]
        public void Buy_Ok_MessageAndRefresh()
        {
            var p = Create();
            int changed = 0;
            p.Changed += () => changed++;
            hideout.Casings = 5940;   // 서비스가 차감했다고 치고
            Assert.IsTrue(p.Buy(TestData.Bandage()));
            Assert.AreEqual("구매했다: 붕대", p.Message);
            Assert.AreEqual("탄피 5940", p.CasingsText, "명령 뒤 다시 읽는다");
            Assert.AreEqual(1, changed);
            Assert.IsFalse(p.Buy(null));
        }

        [Test]
        public void Sell_OkAndNotInStash()
        {
            var p = Create();
            ItemInstance pipe = hideout.AddToStash(TestData.Pipe(), 0, 0, 16);
            Assert.IsTrue(p.Sell(pipe));
            Assert.AreEqual("판매했다: 녹슨 파이프", p.Message);

            hideout.SellResult = TradeResult.NotInStash;
            Assert.IsFalse(p.Sell(pipe));
            Assert.AreEqual("보관함에 있는 것만 팔 수 있다.", p.Message);
            Assert.IsFalse(p.Sell(null));
        }

        // Q30: 확인 창 없이 바로 시작. 떠나는 중이면 모든 버튼 비활성·명령 무시
        [Test]
        public void StartRaid_Ok_LeavingDisablesEverything()
        {
            ItemInstance pipe = hideout.AddToStash(TestData.Pipe(), 0, 0, 16);
            hideout.OfferList.Add(new TraderOffer(TestData.Bandage(), 60));
            var p = Create();
            Assert.IsTrue(p.StartRaid());
            CollectionAssert.AreEqual(new[] { "StartRaid" }, hideout.Calls);
            Assert.IsTrue(p.IsLeaving);
            Assert.IsFalse(p.HubEnabled);
            Assert.IsFalse(p.CanClaimKit);
            Assert.IsFalse(p.Trader.Offers[0].CanBuy);
            Assert.IsFalse(p.Trader.Sell[0].CanSell);

            Assert.IsFalse(p.StartRaid(), "연속 클릭 무시");
            Assert.IsFalse(p.ReturnToMenu());
            Assert.IsFalse(p.ClaimKit());
            Assert.IsFalse(p.Sell(pipe));
            Assert.IsFalse(p.SelectTab(HideoutTab.Trader));
            Assert.AreEqual(1, hideout.Calls.Count);
        }

        // W4: 저장 실패면 레이드를 시작하지 않는다 (RULE-03) → 메시지, 다시 누를 수 있다
        [Test]
        public void StartRaid_SaveFails_MessageAndRetry()
        {
            hideout.StartRaidResult = false;
            hideout.SaveFailsOnStart = true;
            var p = Create();
            Assert.IsFalse(p.StartRaid());
            Assert.AreEqual("저장하지 못해 레이드를 시작할 수 없다.", p.Message);
            Assert.IsTrue(p.HubEnabled);

            hideout.SaveFailsOnStart = false;
            Assert.IsFalse(p.StartRaid());
            Assert.AreEqual("레이드를 시작할 수 없다.", p.Message);

            hideout.StartRaidResult = true;
            Assert.IsTrue(p.StartRaid());
            Assert.AreEqual(string.Empty, p.Message);
        }

        // W2: 저장이 실패해도 메인 메뉴로는 간다 — 메시지만
        [Test]
        public void ReturnToMenu_SaveFails_StillLeaves_WithMessage()
        {
            hideout.SaveFailsOnReturn = true;
            var p = Create();
            Assert.IsTrue(p.ReturnToMenu());
            Assert.AreEqual("저장하지 못했다.", p.Message);
            Assert.IsTrue(p.IsLeaving);
        }

        [Test]
        public void ReturnToMenu_Rejected_Message()
        {
            hideout.ReturnResult = false;
            var p = Create();
            Assert.IsFalse(p.ReturnToMenu());
            Assert.AreEqual("메인 메뉴로 갈 수 없다.", p.Message);
            Assert.IsTrue(p.HubEnabled);
        }

        // W1 진입 저장 실패 → 진입은 계속, 첫 메시지
        [Test]
        public void EnterSaveFailed_ShowsMessage()
        {
            hideout.LastSaveFailed = true;
            var p = Create();
            Assert.AreEqual(HideoutPanel.Hub, p.Panel);
            Assert.AreEqual("저장하지 못했다.", p.Message);
        }

        // M5 리뷰 6: W2 저장 실패 → 서비스가 이동을 멈춤 → "다시 누르면 나간다" 메시지
        [Test]
        public void ReturnToMenu_SaveFailedNotLeft_RetryMessage()
        {
            hideout.ReturnResult = false;
            hideout.SaveFailsOnReturn = true;
            var p = Create();
            Assert.IsFalse(p.ReturnToMenu());
            Assert.AreEqual(UiText.MessageMainMenuSaveFailed, p.Message);
            Assert.IsTrue(p.HubEnabled, "다시 누를 수 있다");

            hideout.ReturnResult = true;
            Assert.IsTrue(p.ReturnToMenu());
            Assert.IsTrue(p.IsLeaving);
        }

        // M5 리뷰 1: 읽기 전용 → 첫 메시지·알림 패널, [레이드 시작] 거부 문구
        [Test]
        public void ReadOnly_MessagesAndNotice()
        {
            hideout.IsReadOnly = true;
            hideout.LastSaveFailed = true;
            hideout.PendingNotice = new SaveLoadReport { Status = SaveLoadStatus.ReadError };
            var p = Create();
            Assert.AreEqual(HideoutPanel.Notice, p.Panel);
            Assert.AreEqual(UiText.MessageReadOnly, p.Message);
            CollectionAssert.Contains(p.Notice.Lines, UiText.NoticeReadError);
            Assert.IsFalse(p.Notice.ShowLost);

            Assert.IsTrue(p.ConfirmNotice());
            hideout.StartRaidResult = false;
            hideout.SaveFailsOnStart = true;
            Assert.IsFalse(p.StartRaid());
            Assert.AreEqual(UiText.MessageStartRaidReadOnly, p.Message);
        }

        // M5 리뷰 10: 상인 탭 목록은 읽을 때만 만들고 Refresh가 버린다
        [Test]
        public void Trader_BuiltLazily_OnlyWhenRead()
        {
            hideout.OfferList.Add(new TraderOffer(TestData.Bandage(), 60));
            var p = Create();
            p.Refresh();
            p.Refresh();
            Assert.AreEqual(0, hideout.OffersReads, "숨은 상인 탭은 만들지 않는다");

            Assert.AreEqual(1, p.Trader.Offers.Count);
            Assert.AreSame(p.Trader, p.Trader);
            Assert.AreEqual(1, hideout.OffersReads);

            hideout.Casings = 10;
            p.Refresh();
            Assert.IsFalse(p.Trader.Offers[0].CanBuy, "Refresh 뒤 다시 만든 목록");
            Assert.AreEqual(2, hideout.OffersReads);
        }

        [Test]
        public void Refresh_PicksUpServiceChanges()
        {
            var p = Create();
            int changed = 0;
            p.Changed += () => changed++;
            hideout.Casings = 100;
            hideout.AddToStash(TestData.Wires(), 3, 0, 32);
            p.Refresh();
            Assert.AreEqual("탄피 100", p.CasingsText);
            Assert.AreEqual(1, p.Trader.Sell.Count);
            Assert.AreEqual(1, changed);
        }
    }

    public class TraderViewModelTests
    {
        [Test]
        public void Build_Null_Throws() =>
            Assert.Throws<ArgumentNullException>(() => TraderViewModel.Build(null));

        [Test]
        public void Offers_InOrder_PriceText_CanBuyByCasings()
        {
            var h = new FakeHideoutService { Casings = 500 };
            WeaponDefinitionSO rifle = TestData.Rifle();
            MedicalDefinitionSO bandage = TestData.Bandage();
            h.OfferList.Add(new TraderOffer(rifle, 4000));
            h.OfferList.Add(new TraderOffer(bandage, 60));
            h.OfferList.Add(new TraderOffer(TestData.Wires(), 500));
            h.OfferList.Add(new TraderOffer(null, 10));

            TraderViewModel vm = TraderViewModel.Build(h);
            Assert.AreEqual("고물상", vm.Title);
            Assert.AreEqual(3, vm.Offers.Count, "null 정의는 건너뛴다");
            Assert.AreSame(rifle, vm.Offers[0].Item);
            Assert.AreEqual(RaidResultViewModel.ItemName(rifle), vm.Offers[0].Name);
            Assert.AreEqual("4000 탄피", vm.Offers[0].PriceText);
            Assert.IsFalse(vm.Offers[0].CanBuy, "탄피 부족 → 비활성");
            Assert.IsTrue(vm.Offers[1].CanBuy);
            Assert.IsTrue(vm.Offers[2].CanBuy, "가격 = 탄피면 살 수 있다");
        }

        // 보관함 매도 줄: 아이템마다 한 줄, 배치 순(위→아래, 왼→오른쪽), 탄 수 없음, 가격 0도 판매(처분)
        [Test]
        public void SellRows_GridOrder_PerItem_ZeroPriceSellable()
        {
            var h = new FakeHideoutService();
            ItemDefinitionSO wires = TestData.Wires();
            ItemInstance c = h.AddToStash(wires, 5, 2, 32);
            ItemInstance a = h.AddToStash(TestData.Pipe(), 0, 0, 0);
            ItemInstance b = h.AddToStash(wires, 3, 0, 32);
            ItemInstance gone = h.AddToStash(TestData.EnginePart(), 0, 5, 120);
            h.SellPrices.Remove(gone);   // 서비스가 -1 → 줄 없음

            TraderViewModel vm = TraderViewModel.Build(h);
            CollectionAssert.AreEqual(new[] { a, b, c }, vm.Sell.Select(r => r.Item).ToList());
            Assert.AreEqual("0 탄피", vm.Sell[0].PriceText);
            Assert.IsTrue(vm.Sell[0].CanSell, "가격 0 = 처분 (Q12)");
            Assert.AreEqual("전선 뭉치", vm.Sell[1].Name);
            Assert.AreEqual("32 탄피", vm.Sell[1].PriceText);
        }

        [Test]
        public void EmptyStash_NoRows()
        {
            Assert.AreEqual(0, TraderViewModel.Build(new FakeHideoutService()).Sell.Count);
        }
    }

    public class HideoutNoticeViewModelTests
    {
        [Test]
        public void From_Null_Throws() =>
            Assert.Throws<ArgumentNullException>(() => HideoutNoticeViewModel.From(null));

        // RULE-03: 문구 + 잃은 장비 묶음 ("이름 ×n")
        [Test]
        public void Crash_TextAndGroupedLostItems()
        {
            MagazineDefinitionSO mag = TestData.Magazine30();
            WeaponDefinitionSO rifle = TestData.Rifle();
            var report = new SaveLoadReport { Status = SaveLoadStatus.Loaded, CrashPenaltyApplied = true };
            report.CrashLost.AddRange(new ItemDefinitionSO[] { rifle, mag, mag });

            HideoutNoticeViewModel vm = HideoutNoticeViewModel.From(report);
            Assert.AreEqual("알림", vm.Title);
            CollectionAssert.AreEqual(new[] { UiText.NoticeCrash }, vm.Lines);
            StringAssert.Contains("RULE-03", vm.Text);
            Assert.IsTrue(vm.ShowLost);
            CollectionAssert.AreEqual(new[]
            {
                RaidResultViewModel.ItemName(rifle),
                RaidResultViewModel.ItemName(mag) + " ×2",
            }, vm.Lost);
        }

        [Test]
        public void Corrupt_WithQuarantinePath_NoLostSection()
        {
            var report = new SaveLoadReport
            {
                Status = SaveLoadStatus.Corrupt,
                QuarantinedPath = "C:/save/save.corrupt.json",
            };
            HideoutNoticeViewModel vm = HideoutNoticeViewModel.From(report);
            CollectionAssert.AreEqual(new[]
            {
                "세이브 파일이 손상되어 새로 시작한다.",
                "이전 파일은 따로 보관했다: C:/save/save.corrupt.json",
            }, vm.Lines);
            Assert.IsFalse(vm.ShowLost);
            Assert.AreEqual(0, vm.Lost.Count);
        }

        [Test]
        public void UnsupportedVersion_UnknownAndDropped()
        {
            var report = new SaveLoadReport { Status = SaveLoadStatus.UnsupportedVersion, DroppedItemCount = 2 };
            report.UnknownItemIds.Add("WPN-XXX");
            HideoutNoticeViewModel vm = HideoutNoticeViewModel.From(report);
            CollectionAssert.AreEqual(new[]
            {
                "세이브 파일 버전을 읽을 수 없어 새로 시작한다.",
                "알 수 없는 아이템 1개를 버렸다.",
                "놓을 자리가 없는 아이템 2개를 버렸다.",
            }, vm.Lines);
        }

        // 정보 희소성: 알림에도 탄 수·HP 같은 숫자 표기를 만들지 않는다 (개수 문구 제외)
        [Test]
        public void Crash_LostLines_HaveNoRoundCounts()
        {
            var report = new SaveLoadReport { CrashPenaltyApplied = true };
            report.CrashLost.Add(TestData.Magazine30());
            HideoutNoticeViewModel vm = HideoutNoticeViewModel.From(report);
            Assert.AreEqual(RaidResultViewModel.ItemName(report.CrashLost[0]), vm.Lost[0], "이름만 (탄 수 없음)");
        }
    }

    // HideoutUI.uxml 이름 계약 (M5 설계 §4.5: 3단계는 UXML 경로와 HideoutRoot만 쓴다) + 뷰가 실제 요소에 그린다
    public class HideoutUxmlTests
    {
        public const string HideoutUxml = "Assets/_Project/Art/UI/HideoutUI.uxml";
        private const string Uss = "Assets/_Project/Art/UI/Ashfall.uss";

        private static VisualElement Clone()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HideoutUxml);
            Assert.IsNotNull(asset, HideoutUxml);
            return asset.CloneTree();
        }

        [Test]
        public void HasAllContractNames()
        {
            VisualElement root = Clone();
            string[] names =
            {
                "hideout-root", "hideout-notice-panel", "hideout-notice-text", "hideout-notice-list", "hideout-notice-confirm",
                "hideout-hub", "hideout-casings", "hideout-tab-loadout", "hideout-tab-trader", "hideout-kit-button",
                "hideout-kit-status", "hideout-start-raid", "hideout-main-menu", "hideout-message",
                "trader-panel", "trader-offers", "trader-sell",
                InventoryScreenView.ScreenName, InventoryScreenView.EquipSlotsName, InventoryScreenView.StorageName,
                InventoryScreenView.QuickSlotsName, InventoryScreenView.LootPanelName, InventoryScreenView.LootTitleName,
                InventoryScreenView.LootStatusName, InventoryScreenView.LootGridName, InventoryScreenView.DragLayerName,
                RaidResultView.PanelName, RaidResultView.TitleName, RaidResultView.DurationName, RaidResultView.KillsName,
                RaidResultView.BroughtInName, RaidResultView.CarriedOutSectionName, RaidResultView.CarriedOutName,
                RaidResultView.LostSectionName, RaidResultView.LostName, RaidResultView.ConfirmName,
            };
            foreach (string name in names)
                Assert.IsNotNull(root.Q(name), name);
            Assert.AreEqual(1, root.Query(InventoryScreenView.ScreenName).ToList().Count, "인벤토리 화면은 하나");
        }

        // 보관함 그리드 자리(inv-loot-grid-host)는 세로 ScrollView(hideout-stash-scroll) 안
        [Test]
        public void StashGridHost_IsInsideVerticalScrollView()
        {
            VisualElement root = Clone();
            var scroll = root.Q<ScrollView>(HideoutHubView.StashScrollName);
            Assert.IsNotNull(scroll);
            Assert.AreEqual(ScrollViewMode.Vertical, scroll.mode);
            VisualElement host = root.Q(InventoryScreenView.LootGridName);
            Assert.IsTrue(scroll.Contains(host), "그리드 자리가 스크롤 안에 있어야 한다");
        }

        // 뷰 5개가 같은 문서에 붙고, 허브·상인·알림·결과·인벤토리(보관함 10×30)를 그린다
        [Test]
        public void Views_BindAndRender()
        {
            VisualElement root = Clone();
            var hideout = new FakeHideoutService { Casings = 1234, IsKitAvailable = false, KitCooldownRemainingSec = 300f };
            hideout.OfferList.Add(new TraderOffer(TestData.Bandage(), 60));
            hideout.OfferList.Add(new TraderOffer(TestData.Rifle(), 4000));
            ItemInstance pipe = hideout.AddToStash(TestData.Pipe(), 0, 0, 16);
            var rec = new FlowRecorder();
            GameFlow flow = rec.Create();
            flow.OnSceneLoaded(SceneNames.Hideout);
            var presenter = new HideoutPresenter(hideout, flow);

            var buys = new List<ItemDefinitionSO>();
            var sells = new List<ItemInstance>();
            int kit = 0, start = 0, menu = 0, notice = 0, result = 0;
            HideoutTab? tab = null;
            var hub = new HideoutHubView(root, t => tab = t, () => kit++, () => start++, () => menu++);
            var noticeView = new HideoutNoticeView(root, () => notice++);
            var resultView = new RaidResultView(root, () => result++);
            var trader = new TraderView(root, buys.Add, sells.Add);
            var inventory = new InventoryScreenView(root, 4f, new[] { "4", "5", "6", "7" }, () => false);

            Assert.AreEqual(DisplayStyle.None, root.Q(HideoutHubView.HubName).style.display.value, "처음엔 숨김");
            Assert.AreEqual(DisplayStyle.None, root.Q(HideoutNoticeView.PanelName).style.display.value);
            Assert.AreEqual(DisplayStyle.None, root.Q(TraderView.PanelName).style.display.value);

            hub.SetVisible(true);
            hub.Render(presenter);
            Assert.AreEqual("탄피 1234", root.Q<Label>(HideoutHubView.CasingsName).text);
            Assert.AreEqual("5분 뒤", root.Q<Label>(HideoutHubView.KitStatusName).text);
            Assert.IsFalse(root.Q<Button>(HideoutHubView.KitButtonName).enabledSelf, "쿨다운 중 비활성");
            Assert.IsTrue(root.Q<Button>(HideoutHubView.StartRaidName).enabledSelf);
            Assert.AreEqual("레이드 시작", root.Q<Button>(HideoutHubView.StartRaidName).text);
            Assert.AreEqual("메인 메뉴", root.Q<Button>(HideoutHubView.MainMenuName).text);
            Assert.IsTrue(root.Q(HideoutHubView.TabLoadoutName).ClassListContains(HideoutHubView.TabSelectedClass));
            Assert.IsFalse(root.Q(HideoutHubView.TabTraderName).ClassListContains(HideoutHubView.TabSelectedClass));

            trader.SetVisible(true);
            trader.Render(presenter.Trader, presenter.HubEnabled);
            Assert.AreEqual("고물상", root.Q<Label>(TraderView.TitleName).text);
            VisualElement offers = root.Q(TraderView.OffersName);
            Assert.AreEqual(2, offers.childCount);
            Assert.AreEqual("60 탄피", offers[0].Q<Label>(className: TraderView.RowPriceClass).text);
            Assert.IsTrue(offers[0].Q<Button>().enabledSelf);
            Assert.IsFalse(offers[1].Q<Button>().enabledSelf, "탄피 부족 → [구매] 비활성");
            VisualElement sell = root.Q(TraderView.SellName);
            Assert.AreEqual(1, sell.childCount);
            Assert.AreEqual("녹슨 파이프", sell[0].Q<Label>(className: TraderView.RowNameClass).text);

            // 알림·결과
            var report = new SaveLoadReport { CrashPenaltyApplied = true };
            noticeView.SetVisible(true);
            noticeView.Render(HideoutNoticeViewModel.From(report));
            Assert.AreEqual(UiText.NoticeCrash, root.Q<Label>(HideoutNoticeView.TextName).text);
            Assert.AreEqual(UiText.ResultNone, root.Q(HideoutNoticeView.ListName).Q<Label>().text, "잃은 것이 없으면 '없음'");
            noticeView.Render(HideoutNoticeViewModel.From(new SaveLoadReport { Status = SaveLoadStatus.Corrupt }));
            Assert.AreEqual(DisplayStyle.None, root.Q(HideoutNoticeView.ListSectionName).style.display.value, "손상 알림엔 잃은 목록 없음");

            resultView.Render(RaidResultViewModel.From(M4TestData.Result(RaidOutcome.Extracted, "EXT-01", "남쪽 도로")));
            Assert.AreEqual("탈출 성공 — 남쪽 도로", root.Q<Label>(RaidResultView.TitleName).text);

            // [장비] 탭: 열린 루트 자리 = 보관함 10×30
            var inv = new FakeInventoryService { Loot = new FakeLootView(new Vector2Int(10, 30), "보관함") };
            inv.Grids.Add(new FakeGridView(new GridRef(GridKind.Pocket, 0), new Vector2Int(1, 1)));
            inv.Loot.Placed.Add(new PlacedItem(pipe, Vector2Int.zero, false));
            inv.Loot.Revealed.Add(pipe);
            inventory.Bind(inv, new InventoryDragModel(inv));
            inventory.SetVisible(true);
            inventory.Render();
            Assert.AreEqual(DisplayStyle.Flex, root.Q(InventoryScreenView.LootPanelName).style.display.value);
            Assert.AreEqual("보관함", root.Q<Label>(InventoryScreenView.LootTitleName).text);
            VisualElement stashGrid = root.Q(InventoryScreenView.LootGridName).Q(className: InventoryScreenView.GridClass);
            Assert.AreEqual(300, stashGrid.Query(className: InventoryScreenView.CellClass).ToList().Count, "10×30 칸");
            Assert.AreEqual(1, stashGrid.Query<Label>(className: InventoryScreenView.ItemClass).ToList().Count);
            Assert.AreEqual(7, root.Q(InventoryScreenView.EquipSlotsName).childCount, "장착 7슬롯");
            Assert.AreEqual(4, root.Q(InventoryScreenView.QuickSlotsName).childCount);

            // 떠나는 중 → 허브·상인 버튼 비활성
            presenter.StartRaid();
            hub.Render(presenter);
            trader.Render(presenter.Trader, presenter.HubEnabled);
            Assert.IsFalse(root.Q<Button>(HideoutHubView.StartRaidName).enabledSelf);
            Assert.IsFalse(root.Q<Button>(HideoutHubView.MainMenuName).enabledSelf);
            Assert.IsFalse(root.Q<Button>(HideoutHubView.TabTraderName).enabledSelf);
            Assert.IsFalse(root.Q(TraderView.OffersName)[0].Q<Button>().enabledSelf);
            Assert.IsFalse(root.Q(TraderView.SellName)[0].Q<Button>().enabledSelf);

            hub.Dispose();
            noticeView.Dispose();
            resultView.Dispose();
            inventory.Dispose();
            Assert.AreEqual(0, kit + start + menu + notice + result);
            Assert.IsNull(tab);
            CollectionAssert.IsEmpty(buys);
            CollectionAssert.IsEmpty(sells);
        }

        // §10.1: USS의 색은 팔레트 토큰(변수 정의)뿐. 변수 정의 밖에 HEX·rgb 직접 사용 금지.
        [Test]
        public void Uss_UsesOnlyPaletteColors()
        {
            string text = System.IO.File.ReadAllText(Uss);
            string[] palette = { "#0D0807", "#3A0B0B", "#6E1A14", "#B2321C", "#5A524E", "#C9BBA8", "#7A8A2A" };
            // M7 Q49: §6 희귀(청)만 문서화된 예외 (--rarity-rare-blue 정의 줄, PaletteConsistencyTests가 하나뿐임을 검사)
            foreach (Match m in Regex.Matches(text, "#[0-9A-Fa-f]{3,8}\\b"))
            {
                int lineStart = text.LastIndexOf('\n', m.Index) + 1;
                if (text.Substring(lineStart, m.Index - lineStart).Trim().StartsWith("--rarity-rare-blue:"))
                    continue;
                CollectionAssert.Contains(palette, m.Value.ToUpperInvariant(), "팔레트 밖 색: " + m.Value);
            }
            foreach (Match m in Regex.Matches(text, "rgba?\\(([^)]*)\\)"))
                StringAssert.StartsWith("13, 8, 7", m.Groups[1].Value, "반투명은 Ash_Black만: " + m.Value);
            // 변수 정의 줄 밖에서 색 리터럴을 쓰지 않는다
            foreach (string line in text.Split('\n'))
            {
                string t = line.Trim();
                if (t.StartsWith("--") || t.StartsWith("/*") || t.StartsWith("*"))
                    continue;
                Assert.IsFalse(Regex.IsMatch(t, "#[0-9A-Fa-f]{3,8}\\b|rgba?\\("), "변수 밖 색 리터럴: " + t);
            }
        }
    }

    // ── M7 제작대 UI (M7 설계 §1.5, §4.8, §6 7C-2b): [제작] 탭·버튼 활성·결과 문구 ──

    internal static class CraftingUiData
    {
        public static CraftingRecipeView Recipe(string id, string name, ItemDefinitionSO output, int outputCount,
            params (ItemDefinitionSO item, int required, int owned)[] inputs) =>
            new CraftingRecipeView(id, name, output, outputCount,
                inputs.Select(i => new CraftingInputView(i.item, i.required, i.owned)).ToList());
    }

    public class CraftingViewModelTests
    {
        [Test]
        public void Build_Null_Throws() =>
            Assert.Throws<ArgumentNullException>(() => CraftingViewModel.Build(null));

        [Test]
        public void Build_NoRecipes_Empty()
        {
            Assert.IsTrue(CraftingViewModel.Build(new FakeHideoutService()).IsEmpty);
        }

        // 재료 "가진 수/필요 수", 모두 있으면 [제작] 활성, 하나라도 모자라면 비활성
        [Test]
        public void Rows_InputCounts_CanCraftOnlyWithAllMaterials()
        {
            var h = new FakeHideoutService();
            ItemDefinitionSO pipe = TestData.Pipe();
            ItemDefinitionSO wires = TestData.Wires();
            h.RecipeList.Add(CraftingUiData.Recipe("RCP-A", "5.56 탄창", TestData.Magazine30(), 1, (pipe, 2, 3), (wires, 1, 1)));
            h.RecipeList.Add(CraftingUiData.Recipe("RCP-B", "", TestData.Bandage(), 2, (wires, 2, 1)));
            h.RecipeList.Add(CraftingUiData.Recipe(null, "id 없음", TestData.Bandage(), 1, (wires, 1, 9)));

            CraftingViewModel vm = CraftingViewModel.Build(h);

            Assert.AreEqual(2, vm.Recipes.Count, "id 없는 줄은 건너뛴다");
            CraftingRecipeRow a = vm.Recipes[0];
            Assert.AreEqual("RCP-A", a.Id);
            Assert.AreEqual("5.56 탄창", a.Name);
            CollectionAssert.AreEqual(new[] { "녹슨 파이프", "전선 뭉치" }, a.Inputs.Select(i => i.Name));
            CollectionAssert.AreEqual(new[] { "3/2", "1/1" }, a.Inputs.Select(i => i.CountText));
            Assert.IsTrue(a.Inputs.All(i => i.Satisfied));
            Assert.IsTrue(a.HasMaterials);
            Assert.IsTrue(a.CanCraft);

            CraftingRecipeRow b = vm.Recipes[1];
            Assert.AreEqual("붕대 ×2", b.Name, "이름이 없으면 산출 이름 ×n");
            Assert.AreEqual("1/2", b.Inputs[0].CountText);
            Assert.IsFalse(b.Inputs[0].Satisfied);
            Assert.IsFalse(b.HasMaterials);
            Assert.IsFalse(b.CanCraft, "재료 부족 → [제작] 비활성");
        }

        // 같은 정의가 두 줄이면 합계로 판정 (Owned는 줄마다 보관함 전체 수). 칸 표시도 버튼과 같은 합계 기준 (리뷰 7C 4)
        [Test]
        public void SameDefinitionTwoLines_SummedForCanCraft()
        {
            var h = new FakeHideoutService();
            ItemDefinitionSO pipe = TestData.Pipe();
            h.RecipeList.Add(CraftingUiData.Recipe("RCP-A", "x", TestData.Wires(), 1, (pipe, 1, 1), (pipe, 1, 1)));
            CraftingRecipeRow row = CraftingViewModel.Build(h).Recipes[0];
            CollectionAssert.AreEqual(new[] { "1/1", "1/1" }, row.Inputs.Select(i => i.CountText), "문구는 줄마다 가진 수/필요 수");
            Assert.IsTrue(row.Inputs.All(i => !i.Satisfied), "합계 2 > 가진 1 → 두 칸 모두 부족 표시");
            Assert.IsFalse(row.HasMaterials);
            Assert.IsFalse(row.CanCraft, "합계 2 > 가진 1");
        }

        [Test]
        public void SameDefinitionTwoLines_EnoughForTotal_AllSatisfied()
        {
            var h = new FakeHideoutService();
            ItemDefinitionSO pipe = TestData.Pipe();
            h.RecipeList.Add(CraftingUiData.Recipe("RCP-A", "x", TestData.Wires(), 1, (pipe, 1, 2), (pipe, 1, 2)));
            CraftingRecipeRow row = CraftingViewModel.Build(h).Recipes[0];
            Assert.IsTrue(row.Inputs.All(i => i.Satisfied), "합계 2 ≤ 가진 2");
            Assert.IsTrue(row.CanCraft);
        }

        [Test]
        public void LeavingOrReadOnly_CannotCraft()
        {
            var h = new FakeHideoutService();
            h.RecipeList.Add(CraftingUiData.Recipe("RCP-A", "x", TestData.Wires(), 1, (TestData.Pipe(), 1, 1)));
            Assert.IsTrue(CraftingViewModel.Build(h).Recipes[0].CanCraft);
            h.IsLeaving = true;
            Assert.IsFalse(CraftingViewModel.Build(h).Recipes[0].CanCraft);
            Assert.IsTrue(CraftingViewModel.Build(h).Recipes[0].HasMaterials);
            h.IsLeaving = false;
            h.IsReadOnly = true;
            Assert.IsFalse(CraftingViewModel.Build(h).Recipes[0].CanCraft, "읽기 전용 → 서비스가 Unavailable");
        }
    }

    public class HideoutCraftingPresenterTests
    {
        private FakeHideoutService hideout;
        private GameFlow flow;

        [SetUp]
        public void SetUp()
        {
            var rec = new FlowRecorder();
            flow = rec.Create();
            flow.OnSceneLoaded(SceneNames.Hideout);
            hideout = new FakeHideoutService();
            hideout.RecipeList.Add(CraftingUiData.Recipe("RCP-AMMO-556", "5.56 탄창 (30발)", TestData.Magazine30(), 1,
                (TestData.Pipe(), 2, 2), (TestData.Wires(), 1, 1)));
        }

        [Test]
        public void SelectTab_Crafting()
        {
            var p = new HideoutPresenter(hideout, flow);
            int changed = 0;
            p.Changed += () => changed++;
            Assert.IsTrue(p.SelectTab(HideoutTab.Crafting));
            Assert.AreEqual(HideoutTab.Crafting, p.Tab);
            Assert.AreEqual(1, changed);
            Assert.AreEqual(1, p.Crafting.Recipes.Count);
        }

        [Test]
        public void Craft_Ok_MessageWithRecipeName_Refreshes()
        {
            var p = new HideoutPresenter(hideout, flow);
            CraftingViewModel before = p.Crafting;
            Assert.IsTrue(p.Craft("RCP-AMMO-556"));
            CollectionAssert.AreEqual(new[] { "Craft:RCP-AMMO-556" }, hideout.Calls);
            Assert.AreEqual("만들었다: 5.56 탄창 (30발)", p.Message);
            Assert.AreNotSame(before, p.Crafting, "명령 뒤 다시 읽는다");
        }

        [TestCase(CraftResult.MissingMaterials, "재료가 모자란다.")]
        [TestCase(CraftResult.NoSpace, "보관함에 자리가 없어 만들 수 없다.")]
        [TestCase(CraftResult.UnknownRecipe, "알 수 없는 제작법이다.")]
        [TestCase(CraftResult.Unavailable, "지금은 할 수 없다.")]
        public void Craft_Failure_ResultMessage(CraftResult result, string message)
        {
            var p = new HideoutPresenter(hideout, flow);
            hideout.CraftResultToReturn = result;
            Assert.IsFalse(p.Craft("RCP-AMMO-556"));
            Assert.AreEqual(message, p.Message);
            Assert.AreEqual(message, UiText.Craft(result));
        }

        [Test]
        public void Craft_IgnoredWhenLeavingOrNotHubOrEmptyId()
        {
            var p = new HideoutPresenter(hideout, flow);
            Assert.IsFalse(p.Craft(null));
            Assert.IsFalse(p.Craft(""));
            hideout.IsLeaving = true;
            Assert.IsFalse(p.Craft("RCP-AMMO-556"));
            CollectionAssert.IsEmpty(hideout.Calls);

            hideout.IsLeaving = false;
            hideout.PendingNotice = new SaveLoadReport { CrashPenaltyApplied = true };
            p.Refresh();
            Assert.AreEqual(HideoutPanel.Notice, p.Panel);
            Assert.IsFalse(p.Craft("RCP-AMMO-556"), "알림이 떠 있으면 허브 명령 무시");
            CollectionAssert.IsEmpty(hideout.Calls);
        }
    }

    public class HideoutCraftingUxmlTests
    {
        private static VisualElement Clone()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HideoutUxmlTests.HideoutUxml);
            Assert.IsNotNull(asset);
            return asset.CloneTree();
        }

        [Test]
        public void HasCraftingNames()
        {
            VisualElement root = Clone();
            foreach (string name in new[]
                     {
                         HideoutHubView.TabCraftingName, CraftingView.PanelName, CraftingView.TitleName,
                         CraftingView.HintName, CraftingView.RecipesName,
                     })
                Assert.IsNotNull(root.Q(name), name);
            Assert.AreEqual(UiText.HideoutTabCrafting, root.Q<Button>(HideoutHubView.TabCraftingName).text, "미리보기 문구도 UiText와 같게");
        }

        // 탭 버튼·레시피 줄·[제작] 활성·클릭 콜백·빈 목록 문구
        [Test]
        public void HubTabAndCraftingView_Render()
        {
            VisualElement root = Clone();
            var hideout = new FakeHideoutService();
            hideout.RecipeList.Add(CraftingUiData.Recipe("RCP-OK", "권총 탄창 (12발)", TestData.PistolMagazine(), 1,
                (TestData.Pipe(), 1, 1), (TestData.Wires(), 1, 2)));
            hideout.RecipeList.Add(CraftingUiData.Recipe("RCP-NO", "해독제", TestData.Bandage(), 1,
                (TestData.EnginePart(), 1, 0)));
            var flow = new FlowRecorder().Create();
            flow.OnSceneLoaded(SceneNames.Hideout);
            var presenter = new HideoutPresenter(hideout, flow);

            HideoutTab? tab = null;
            var crafted = new List<string>();
            var hub = new HideoutHubView(root, t => tab = t, null, null, null);
            var view = new CraftingView(root, crafted.Add);
            Assert.AreEqual(DisplayStyle.None, root.Q(CraftingView.PanelName).style.display.value, "처음엔 숨김");
            Assert.AreEqual(UiText.CraftingTitle, root.Q<Label>(CraftingView.TitleName).text);

            presenter.SelectTab(HideoutTab.Crafting);
            hub.Render(presenter);
            Assert.IsTrue(root.Q(HideoutHubView.TabCraftingName).ClassListContains(HideoutHubView.TabSelectedClass));
            Assert.IsFalse(root.Q(HideoutHubView.TabLoadoutName).ClassListContains(HideoutHubView.TabSelectedClass));
            Assert.IsTrue(root.Q<Button>(HideoutHubView.TabCraftingName).enabledSelf);

            view.SetVisible(true);
            view.Render(presenter.Crafting, presenter.HubEnabled);
            List<VisualElement> rows = root.Q(CraftingView.RecipesName).Query(className: CraftingView.RowClass).ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("권총 탄창 (12발)", rows[0].Q<Label>(className: CraftingView.RowNameClass).text);
            CollectionAssert.AreEqual(new[] { "1/1", "2/1" },
                rows[0].Query<Label>(className: CraftingView.InputCountClass).ToList().Select(l => l.text));
            CollectionAssert.AreEqual(new[] { "녹슨 파이프", "전선 뭉치" },
                rows[0].Query<Label>(className: CraftingView.InputNameClass).ToList().Select(l => l.text));
            Assert.IsTrue(rows[0].Q<Button>(className: CraftingView.RowButtonClass).enabledSelf);
            Assert.AreEqual(UiText.CraftingButton, rows[0].Q<Button>().text);
            Assert.IsFalse(rows[1].Q<Button>(className: CraftingView.RowButtonClass).enabledSelf, "재료 부족 → 비활성");
            Assert.IsTrue(rows[1].Q(className: CraftingView.InputClass).ClassListContains(CraftingView.InputMissingClass));
            Assert.IsFalse(rows[0].Q(className: CraftingView.InputClass).ClassListContains(CraftingView.InputMissingClass));

            // 떠나는 중 → 모든 [제작] 비활성
            hideout.IsLeaving = true;
            presenter.Refresh();
            view.Render(presenter.Crafting, presenter.HubEnabled);
            Assert.IsTrue(root.Q(CraftingView.RecipesName).Query<Button>().ToList().All(b => !b.enabledSelf));

            // 레시피 없음 → 빈 문구 한 줄
            view.Render(CraftingViewModel.Build(new FakeHideoutService()), true);
            CollectionAssert.AreEqual(new[] { UiText.CraftingEmpty },
                root.Q(CraftingView.RecipesName).Query<Label>().ToList().Select(l => l.text));
            hub.Dispose();
            Assert.IsNull(tab);
        }
    }

    // MVP 마무리 F2 보관함 업그레이드 허브 줄 (§9, 설계 §3.5·§7 AC-F2, MF-Q7·Q9): 문구·활성·결과 메시지 + 실제 UXML에 그린다.
    // 실제 버튼 클릭 → TryUpgradeStash는 PlayMode StashUpgradeFlowTests.
    public class HideoutStashUpgradeUiTests
    {
        private GameFlow flow;
        private FakeHideoutService hideout;

        [SetUp]
        public void SetUp()
        {
            var rec = new FlowRecorder();
            flow = rec.Create();
            flow.OnSceneLoaded(SceneNames.Hideout);
            hideout = new FakeHideoutService { Casings = 6000 };
        }

        private HideoutPresenter Create() => new HideoutPresenter(hideout, flow);

        [Test]
        public void Texts_StatusAndButton()
        {
            Assert.AreEqual("보관함 10×30", UiText.StashStatus(new Vector2Int(10, 30)));
            Assert.AreEqual("보관함 확장 10×40 (5000 탄피)", UiText.StashUpgradeButton(new Vector2Int(10, 40), 5000));
            Assert.AreEqual("보관함 최대", UiText.StashMaxed);
            Assert.AreEqual("보관함을 10×50으로 늘렸다.", UiText.StashUpgraded(new Vector2Int(10, 50)));
        }

        [Test]
        public void Presenter_Level0_CanUpgrade()
        {
            HideoutPresenter p = Create();
            Assert.AreEqual("보관함 10×30", p.StashStatusText);
            Assert.AreEqual("보관함 확장 10×40 (5000 탄피)", p.StashUpgradeText);
            Assert.IsTrue(p.CanUpgradeStash);
        }

        [Test]
        public void Presenter_NotEnoughCasings_Disabled()
        {
            hideout.Casings = 4999;
            HideoutPresenter p = Create();
            Assert.IsFalse(p.CanUpgradeStash);
            Assert.AreEqual("보관함 확장 10×40 (5000 탄피)", p.StashUpgradeText, "문구는 그대로");
        }

        [Test]
        public void Presenter_Max_DisabledMaxText()
        {
            hideout.NextStashUpgrade = StashUpgradeOffer.None;
            HideoutPresenter p = Create();
            Assert.AreEqual("보관함 최대", p.StashUpgradeText);
            Assert.IsFalse(p.CanUpgradeStash);
        }

        [Test]
        public void Presenter_Leaving_DisabledAndIgnored()
        {
            HideoutPresenter p = Create();
            hideout.IsLeaving = true;
            p.Refresh();
            Assert.IsFalse(p.CanUpgradeStash);
            Assert.IsFalse(p.UpgradeStash());
            CollectionAssert.IsEmpty(hideout.Calls);
        }

        [Test]
        public void UpgradeStash_Ok_MessageAndStatusRefreshed()
        {
            HideoutPresenter p = Create();
            int changed = 0;
            p.Changed += () => changed++;
            hideout.NextStashUpgrade = new StashUpgradeOffer(new Vector2Int(10, 40), 5000);
            Assert.IsTrue(p.UpgradeStash());
            CollectionAssert.AreEqual(new[] { "UpgradeStash" }, hideout.Calls);
            Assert.AreEqual("보관함을 10×40으로 늘렸다.", p.Message);
            Assert.AreEqual("보관함 10×40", p.StashStatusText);
            Assert.AreEqual(1, changed);
        }

        [TestCase(StashUpgradeResult.NotEnoughCasings, "탄피가 모자란다.")]
        [TestCase(StashUpgradeResult.MaxLevel, "보관함이 이미 최대다.")]
        [TestCase(StashUpgradeResult.Unavailable, "지금은 할 수 없다.")]
        public void UpgradeStash_Failure_Message(StashUpgradeResult result, string message)
        {
            hideout.UpgradeResult = result;
            HideoutPresenter p = Create();
            Assert.IsFalse(p.UpgradeStash());
            Assert.AreEqual(message, p.Message);
            Assert.AreEqual("보관함 10×30", p.StashStatusText);
        }

        // 실제 HideoutUI.uxml: 요소 2개가 있고 뷰가 상태·버튼 문구·활성을 그린다
        [Test]
        public void HubView_RendersStashRow()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HideoutUxmlTests.HideoutUxml);
            Assert.IsNotNull(asset);
            VisualElement root = asset.CloneTree();
            Assert.IsNotNull(root.Q<Button>(HideoutHubView.StashUpgradeButtonName));
            Assert.IsNotNull(root.Q<Label>(HideoutHubView.StashStatusName));
            Assert.AreEqual("hideout-stash-upgrade-button", HideoutHubView.StashUpgradeButtonName);
            Assert.AreEqual("hideout-stash-status", HideoutHubView.StashStatusName);

            int upgrades = 0;
            var hub = new HideoutHubView(root, null, null, null, null, () => upgrades++);
            HideoutPresenter p = Create();
            hub.Render(p);
            var button = root.Q<Button>(HideoutHubView.StashUpgradeButtonName);
            var status = root.Q<Label>(HideoutHubView.StashStatusName);
            Assert.AreEqual("보관함 10×30", status.text);
            Assert.AreEqual("보관함 확장 10×40 (5000 탄피)", button.text);
            Assert.IsTrue(button.enabledSelf);

            hideout.Casings = 100;
            p.Refresh();
            hub.Render(p);
            Assert.IsFalse(button.enabledSelf, "탄피 부족 → 비활성");

            hideout.Casings = 99999;
            hideout.NextStashUpgrade = StashUpgradeOffer.None;
            p.Refresh();
            hub.Render(p);
            Assert.AreEqual("보관함 최대", button.text);
            Assert.IsFalse(button.enabledSelf, "최대 → 비활성");

            hideout.NextStashUpgrade = new StashUpgradeOffer(new Vector2Int(10, 50), 12000);
            p.Refresh();
            hub.Render(p);
            Assert.IsTrue(button.enabledSelf);
            p.StartRaid();   // 떠나는 중
            hub.Render(p);
            Assert.IsFalse(button.enabledSelf, "떠나는 중 → 비활성");

            hub.Dispose();
            Assert.AreEqual(0, upgrades);
        }
    }
}
