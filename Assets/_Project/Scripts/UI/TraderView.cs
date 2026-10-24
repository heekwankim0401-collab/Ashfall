using System;
using Ashfall.Core;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // [상인] 탭 (Art/UI/HideoutUI.uxml). 왼쪽 판매 목록(이름 · 매수가 · [구매]), 오른쪽 보관함 매도(이름 · 매도가 · [판매]).
    // 데이터는 TraderViewModel. 줄은 그릴 때마다 새로 만든다(버튼 콜백은 줄과 함께 버려진다).
    public sealed class TraderView
    {
        public const string PanelName = "trader-panel";
        public const string TitleName = "trader-title";
        public const string OffersName = "trader-offers";
        public const string SellName = "trader-sell";

        public const string RowClass = "trader-row";
        public const string RowNameClass = "trader-row-name";
        public const string RowPriceClass = "trader-row-price";
        public const string RowButtonClass = "trader-row-button";
        public const string EmptyClass = "result-line--empty";
        // MVP 마무리 F3 [수리] 버튼 표시용 클래스(찾기용, 모양은 ash-button·trader-row-button 그대로 — 새 USS 규칙 없음)
        public const string RowRepairClass = "trader-row-repair";

        private readonly VisualElement panel;
        private readonly Label title;
        private readonly VisualElement offers;
        private readonly VisualElement sell;
        private readonly Action<ItemDefinitionSO> onBuy;
        private readonly Action<ItemInstance> onSell;
        private readonly Action<ItemInstance> onRepair;

        // onRepair = MVP 마무리 F3 매도 줄 [수리] (선택 — 기존 호출 그대로)
        public TraderView(VisualElement documentRoot, Action<ItemDefinitionSO> onBuy, Action<ItemInstance> onSell,
            Action<ItemInstance> onRepair = null)
        {
            panel = RaidResultView.Find<VisualElement>(documentRoot, PanelName);
            title = RaidResultView.Find<Label>(documentRoot, TitleName);
            offers = RaidResultView.Find<VisualElement>(documentRoot, OffersName);
            sell = RaidResultView.Find<VisualElement>(documentRoot, SellName);
            this.onBuy = onBuy;
            this.onSell = onSell;
            this.onRepair = onRepair;
            SetVisible(false);
        }

        public void SetVisible(bool visible) =>
            panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        // hubEnabled = false(알림·결과·떠나는 중)면 모든 버튼 비활성
        public void Render(TraderViewModel model, bool hubEnabled)
        {
            offers.Clear();
            sell.Clear();
            if (model == null)
                return;
            title.text = model.Title;

            foreach (TraderOfferRow row in model.Offers)
            {
                ItemDefinitionSO item = row.Item;
                offers.Add(Row(row.Name, row.PriceText, UiText.TraderBuy, hubEnabled && row.CanBuy, () => onBuy?.Invoke(item), RarityStyle.ClassFor(item)));
            }

            if (model.Sell.Count == 0)
            {
                var empty = new Label(UiText.TraderSellEmpty) { pickingMode = PickingMode.Ignore };
                empty.AddToClassList(RaidResultView.LineClass);
                empty.AddToClassList(EmptyClass);
                sell.Add(empty);
                return;
            }
            foreach (TraderSellRow row in model.Sell)
            {
                ItemInstance item = row.Item;
                VisualElement line = Row(row.Name, row.PriceText, UiText.TraderSell, hubEnabled && row.CanSell, () => onSell?.Invoke(item), RarityStyle.ClassFor(item));
                // [수리]는 [판매] 뒤(줄의 첫 버튼 = [판매] 유지). 수리가 필요 없으면 숨김 (MF-Q13)
                var repair = new Button(() => onRepair?.Invoke(item)) { text = row.RepairText };
                repair.AddToClassList("ash-button");
                repair.AddToClassList(RowButtonClass);
                repair.AddToClassList(RowRepairClass);
                repair.SetEnabled(hubEnabled && row.CanRepair);
                repair.style.display = row.NeedsRepair ? DisplayStyle.Flex : DisplayStyle.None;
                line.Add(repair);
                sell.Add(line);
            }
        }

        // rarityClass = 이름 칸 왼쪽 테두리 색(§6 희귀도, Q49). null이면 없음.
        private static VisualElement Row(string name, string price, string buttonText, bool enabled, Action onClick, string rarityClass)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            var nameLabel = new Label(name) { pickingMode = PickingMode.Ignore };
            nameLabel.AddToClassList(RowNameClass);
            RarityStyle.Apply(nameLabel, rarityClass);
            row.Add(nameLabel);
            var priceLabel = new Label(price) { pickingMode = PickingMode.Ignore };
            priceLabel.AddToClassList(RowPriceClass);
            row.Add(priceLabel);
            var button = new Button(onClick) { text = buttonText };
            button.AddToClassList("ash-button");
            button.AddToClassList(RowButtonClass);
            button.SetEnabled(enabled);
            row.Add(button);
            return row;
        }
    }
}
