using System;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // 은신처 허브 (Art/UI/HideoutUI.uxml). 데이터·판정은 HideoutPresenter. 여기서는 문구·활성·버튼 연결만.
    // 탭 내용: [장비] = InventoryScreenView(inventory-screen), [상인] = TraderView(trader-panel). 둘의 표시는 HideoutRoot가 정한다.
    public sealed class HideoutHubView
    {
        public const string RootName = "hideout-root";
        public const string HubName = "hideout-hub";
        public const string CasingsName = "hideout-casings";
        public const string TabLoadoutName = "hideout-tab-loadout";
        public const string TabTraderName = "hideout-tab-trader";
        public const string KitButtonName = "hideout-kit-button";
        public const string KitStatusName = "hideout-kit-status";
        public const string StartRaidName = "hideout-start-raid";
        public const string MainMenuName = "hideout-main-menu";
        public const string MessageName = "hideout-message";
        // [장비] 탭 보관함 스크롤 (inv-loot-grid-host를 감싼다, 세로 스크롤 — 설계 §4.5)
        public const string StashScrollName = "hideout-stash-scroll";

        public const string TabSelectedClass = "hideout-tab--selected";

        private readonly VisualElement hub;
        private readonly Label casings;
        private readonly Button tabLoadout;
        private readonly Button tabTrader;
        private readonly Button kitButton;
        private readonly Label kitStatus;
        private readonly Button startRaid;
        private readonly Button mainMenu;
        private readonly Label message;

        private readonly Action onTabLoadout;
        private readonly Action onTabTrader;
        private readonly Action onKit;
        private readonly Action onStartRaid;
        private readonly Action onMainMenu;

        public HideoutHubView(VisualElement documentRoot, Action<HideoutTab> onTab, Action onKit, Action onStartRaid, Action onMainMenu)
        {
            hub = RaidResultView.Find<VisualElement>(documentRoot, HubName);
            casings = RaidResultView.Find<Label>(documentRoot, CasingsName);
            tabLoadout = RaidResultView.Find<Button>(documentRoot, TabLoadoutName);
            tabTrader = RaidResultView.Find<Button>(documentRoot, TabTraderName);
            kitButton = RaidResultView.Find<Button>(documentRoot, KitButtonName);
            kitStatus = RaidResultView.Find<Label>(documentRoot, KitStatusName);
            startRaid = RaidResultView.Find<Button>(documentRoot, StartRaidName);
            mainMenu = RaidResultView.Find<Button>(documentRoot, MainMenuName);
            message = RaidResultView.Find<Label>(documentRoot, MessageName);

            tabLoadout.text = UiText.HideoutTabLoadout;
            tabTrader.text = UiText.HideoutTabTrader;
            kitButton.text = UiText.HideoutKitButton;
            startRaid.text = UiText.HideoutStartRaid;
            mainMenu.text = UiText.HideoutMainMenu;

            onTabLoadout = () => onTab?.Invoke(HideoutTab.Loadout);
            onTabTrader = () => onTab?.Invoke(HideoutTab.Trader);
            this.onKit = onKit ?? (() => { });
            this.onStartRaid = onStartRaid ?? (() => { });
            this.onMainMenu = onMainMenu ?? (() => { });
            tabLoadout.clicked += onTabLoadout;
            tabTrader.clicked += onTabTrader;
            kitButton.clicked += this.onKit;
            startRaid.clicked += this.onStartRaid;
            mainMenu.clicked += this.onMainMenu;
            SetVisible(false);
        }

        public void SetVisible(bool visible) =>
            hub.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void Render(HideoutPresenter presenter)
        {
            if (presenter == null)
                return;
            bool enabled = presenter.HubEnabled;
            casings.text = presenter.CasingsText;
            message.text = presenter.Message ?? string.Empty;
            tabLoadout.EnableInClassList(TabSelectedClass, presenter.Tab == HideoutTab.Loadout);
            tabTrader.EnableInClassList(TabSelectedClass, presenter.Tab == HideoutTab.Trader);
            tabLoadout.SetEnabled(enabled);
            tabTrader.SetEnabled(enabled);
            startRaid.SetEnabled(enabled);
            mainMenu.SetEnabled(enabled);
            RenderKit(presenter);
        }

        // 1초마다 (키트 남은 분)
        public void RenderKit(HideoutPresenter presenter)
        {
            if (presenter == null)
                return;
            kitStatus.text = presenter.KitStatusText;
            kitButton.SetEnabled(presenter.HubEnabled && presenter.CanClaimKit);
        }

        public void Dispose()
        {
            tabLoadout.clicked -= onTabLoadout;
            tabTrader.clicked -= onTabTrader;
            kitButton.clicked -= onKit;
            startRaid.clicked -= onStartRaid;
            mainMenu.clicked -= onMainMenu;
        }
    }
}
