using System;
using Ashfall.Core;

namespace Ashfall.UI
{
    public enum HideoutPanel
    {
        Notice,   // 세이브·RULE-03 알림 (IHideoutService.PendingNotice) → [확인]
        Result,   // 레이드 결과 (GameFlow.PendingResult, M4 결과 패널 이동 — Q21) → [확인]
        Hub,      // 탄피·탭·키트·레이드 시작·메인 메뉴
    }

    public enum HideoutTab
    {
        Loadout,  // [장비] 기존 인벤토리 화면 + 보관함 (Q29)
        Trader,   // [상인] 판매 목록 · 보관함 매도
        Crafting, // M7 [제작] 레시피 목록 · 보관함 재료 (§9, Q44~Q47)
    }

    // 은신처 화면 상태 (M5 설계 §3.4, §4.5). 순수 C#. HideoutRoot가 버튼·뷰를 여기로 잇는다.
    // - 패널 순서: 알림 → 결과 → 허브. 알림·결과가 떠 있으면 허브 명령은 무시한다.
    // - [레이드 시작]은 확인 창 없이 바로(Q30), 빈 장비도 허용(Q26). 떠나는 중(IsLeaving)이면 모든 명령 무시·버튼 비활성.
    // - 한 줄 메시지: 거래·키트·제작 결과, 저장 실패. 다음 명령이 덮어쓴다.
    // - 탄 수·HP·무게 숫자는 만들지 않는다. 탄피·가격·키트 남은 분·제작 재료 개수만.
    public sealed class HideoutPresenter
    {
        private readonly IHideoutService hideout;
        private readonly GameFlow flow;

        public HideoutPresenter(IHideoutService hideout, GameFlow flow)
        {
            this.hideout = hideout ?? throw new ArgumentNullException(nameof(hideout));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            // W1 진입 저장 실패 → 진입은 계속, 메시지만 (M5 설계 §1.4). 읽기 전용(세이브를 못 읽음)이면 그 문구.
            Message = hideout.IsReadOnly ? UiText.MessageReadOnly
                : hideout.LastSaveFailed ? UiText.MessageSaveFailed
                : string.Empty;
            Refresh();
        }

        private TraderViewModel trader;
        private CraftingViewModel crafting;

        public HideoutPanel Panel { get; private set; }
        public HideoutTab Tab { get; private set; } = HideoutTab.Loadout;

        // Panel == Notice일 때만 값이 있다
        public HideoutNoticeViewModel Notice { get; private set; }
        // Panel == Result일 때만 값이 있다
        public RaidResultViewModel Result { get; private set; }
        // 항상 값이 있다 (탭이 Trader일 때 뷰가 그린다). 처음 읽을 때 만들고 Refresh가 버린다 —
        // 숨은 [상인] 탭 때문에 이벤트마다 매도 목록을 다시 만들지 않게 (M5 리뷰 10).
        public TraderViewModel Trader => trader ??= TraderViewModel.Build(hideout);
        // [제작] 탭 데이터. 상인과 같이 처음 읽을 때 만들고 Refresh가 버린다 (숨은 탭 때문에 이벤트마다 다시 세지 않게).
        public CraftingViewModel Crafting => crafting ??= CraftingViewModel.Build(hideout);

        public string CasingsText { get; private set; }
        public string KitStatusText { get; private set; }
        public bool CanClaimKit { get; private set; }
        public string Message { get; private set; }
        public bool IsLeaving => hideout.IsLeaving;
        // 허브 버튼(탭·키트·레이드 시작·메인 메뉴·거래)이 눌릴 수 있나
        public bool HubEnabled => Panel == HideoutPanel.Hub && !hideout.IsLeaving;

        // 패널·탭·탄피·키트·상인 목록이 바뀜 → 뷰가 다시 그린다
        public event Action Changed;

        // 서비스·흐름 상태로 전부 다시 정한다 (생성 시, HideoutChanged·InventoryChanged 때).
        public void Refresh()
        {
            SaveLoadReport notice = hideout.PendingNotice;
            RaidResult pending = flow.PendingResult;
            if (notice != null && notice.HasNotice)
            {
                Panel = HideoutPanel.Notice;
                Notice = HideoutNoticeViewModel.From(notice);
                Result = null;
            }
            else if (pending != null)
            {
                Panel = HideoutPanel.Result;
                Notice = null;
                Result = RaidResultViewModel.From(pending);
            }
            else
            {
                Panel = HideoutPanel.Hub;
                Notice = null;
                Result = null;
            }

            CasingsText = UiText.Casings(hideout.Casings);
            trader = null;   // 다음에 읽을 때 다시 만든다
            crafting = null;
            UpdateKitStatus();
            Changed?.Invoke();
        }

        // 키트 남은 시간 문구만 다시 계산한다 (HideoutRoot가 1초마다). 바뀌었으면 true — Changed는 부르지 않는다.
        public bool UpdateKitStatus()
        {
            bool available = hideout.IsKitAvailable;
            string text;
            if (available)
                text = UiText.HideoutKitReady;
            else if (hideout.KitCooldownRemainingSec > 0f)
                text = UiText.KitRemaining(hideout.KitCooldownRemainingSec);
            else
                text = UiText.HideoutKitUnavailable;
            bool canClaim = available && !hideout.IsLeaving;
            bool changed = text != KitStatusText || canClaim != CanClaimKit;
            KitStatusText = text;
            CanClaimKit = canClaim;
            return changed;
        }

        // 알림 [확인]
        public bool ConfirmNotice()
        {
            if (Panel != HideoutPanel.Notice)
                return false;
            hideout.AcknowledgeNotice();
            Refresh();
            return true;
        }

        // 결과 [확인]
        public bool ConfirmResult()
        {
            if (Panel != HideoutPanel.Result)
                return false;
            flow.AcknowledgeResult();
            Refresh();
            return true;
        }

        public bool SelectTab(HideoutTab tab)
        {
            if (!HubEnabled || Tab == tab)
                return false;
            Tab = tab;
            Changed?.Invoke();
            return true;
        }

        // [구매]. 결과 메시지를 남긴다. 성공하면 서비스가 HideoutChanged를 발행하지만 여기서도 다시 읽는다.
        public bool Buy(ItemDefinitionSO item)
        {
            if (!HubEnabled || item == null)
                return false;
            TradeResult result = hideout.TryBuy(item);
            Message = result == TradeResult.Ok ? UiText.Bought(RaidResultViewModel.ItemName(item)) : UiText.Trade(result);
            Refresh();
            return result == TradeResult.Ok;
        }

        // [판매] (보관함 아이템만 — Q12)
        public bool Sell(ItemInstance item)
        {
            if (!HubEnabled || item == null)
                return false;
            string name = UiText.ItemLabel(item);
            TradeResult result = hideout.TrySell(item);
            Message = result == TradeResult.Ok ? UiText.Sold(name) : UiText.Trade(result);
            Refresh();
            return result == TradeResult.Ok;
        }

        // [제작] (M7, Q44~Q47). 결과 문구: 성공 = "만들었다: 레시피 이름", 실패 = 재료 부족·보관함 자리 없음 등.
        public bool Craft(string recipeId)
        {
            if (!HubEnabled || string.IsNullOrEmpty(recipeId))
                return false;
            string name = Crafting.TryGetRecipe(recipeId, out CraftingRecipeRow row) ? row.Name : recipeId;
            CraftResult result = hideout.TryCraft(recipeId);
            Message = result == CraftResult.Ok ? UiText.Crafted(name) : UiText.Craft(result);
            Refresh();
            return result == CraftResult.Ok;
        }

        // [부랑자 키트 받기]
        public bool ClaimKit()
        {
            if (!HubEnabled)
                return false;
            KitClaimResult result = hideout.TryClaimKit();
            Message = UiText.Kit(result);
            Refresh();
            return result == KitClaimResult.Ok;
        }

        // [레이드 시작] (W4). 저장 실패면 시작하지 않고 메시지 (RULE-03).
        public bool StartRaid()
        {
            if (!HubEnabled)
                return false;
            if (hideout.StartRaid())
            {
                Message = string.Empty;
                Refresh();
                return true;
            }
            // 서비스가 시도 직전에 저장 실패 표시를 지운다 → LastSaveFailed는 이번 시도의 저장 실패만 (M5 리뷰 7)
            Message = hideout.IsReadOnly ? UiText.MessageStartRaidReadOnly
                : hideout.LastSaveFailed ? UiText.MessageStartRaidSaveFailed
                : UiText.MessageStartRaidFailed;
            Refresh();
            return false;
        }

        // [메인 메뉴] (W2). 저장이 실패하면 이동하지 않고 메시지 — 다시 누르면 그래도 이동한다 (M5 리뷰 6, 서비스가 판단).
        public bool ReturnToMenu()
        {
            if (!HubEnabled)
                return false;
            bool ok = hideout.ReturnToMainMenu();
            if (!ok)
                Message = hideout.LastSaveFailed && !hideout.IsReadOnly ? UiText.MessageMainMenuSaveFailed : UiText.MessageMainMenuFailed;
            else
                Message = hideout.LastSaveFailed ? UiText.MessageSaveFailed : string.Empty;
            Refresh();
            return ok;
        }
    }
}
