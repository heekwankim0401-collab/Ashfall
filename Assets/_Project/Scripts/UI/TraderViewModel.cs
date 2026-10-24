using System;
using System.Collections.Generic;
using Ashfall.Core;

namespace Ashfall.UI
{
    // 상인 판매 목록 한 줄 (이름 · 매수가 · [구매])
    public readonly struct TraderOfferRow
    {
        public readonly ItemDefinitionSO Item;
        public readonly string Name;
        public readonly string PriceText;
        public readonly bool CanBuy;   // 탄피가 충분하고 떠나는 중이 아님

        public TraderOfferRow(ItemDefinitionSO item, string name, string priceText, bool canBuy)
        {
            Item = item;
            Name = name;
            PriceText = priceText;
            CanBuy = canBuy;
        }
    }

    // 보관함 매도 한 줄 (이름 · 매도가 · [수리] · [판매]). 같은 정의도 아이템마다 한 줄 (Q12: 보관함 아이템만).
    // MVP 마무리 F3: 이름 뒤 내구도 대략 등급(MF-Q11), 수리가 필요하면 [수리 (N 탄피)](MF-Q13·Q14).
    public readonly struct TraderSellRow
    {
        public readonly ItemInstance Item;
        public readonly string Name;
        public readonly string PriceText;
        public readonly bool CanSell;
        public readonly bool NeedsRepair;   // 수리비 ≥ 0 (내구도 있고 100% 미만) — 아니면 [수리] 숨김
        public readonly string RepairText;  // [수리] 버튼 문구 (NeedsRepair일 때만)
        public readonly bool CanRepair;     // NeedsRepair이고 탄피 ≥ 수리비이고 떠나는 중이 아님

        public TraderSellRow(ItemInstance item, string name, string priceText, bool canSell)
            : this(item, name, priceText, canSell, false, string.Empty, false)
        {
        }

        public TraderSellRow(ItemInstance item, string name, string priceText, bool canSell,
            bool needsRepair, string repairText, bool canRepair)
        {
            Item = item;
            Name = name;
            PriceText = priceText;
            CanSell = canSell;
            NeedsRepair = needsRepair;
            RepairText = repairText ?? string.Empty;
            CanRepair = canRepair;
        }
    }

    // [상인] 탭 데이터 (M5 설계 §3.2, §3.4, TODO(GDD-?) Q10~Q12). 순수 C#.
    // 그리드가 아니라 목록(가장 단순). 탄창 탄 수·의약품 횟수는 이름에 붙이지 않는다(UiText.ItemLabel).
    public sealed class TraderViewModel
    {
        private TraderViewModel(string title, IReadOnlyList<TraderOfferRow> offers, IReadOnlyList<TraderSellRow> sell)
        {
            Title = title;
            Offers = offers;
            Sell = sell;
        }

        public string Title { get; }                        // 상인 이름
        public IReadOnlyList<TraderOfferRow> Offers { get; }   // IHideoutService.Offers 순
        public IReadOnlyList<TraderSellRow> Sell { get; }      // 보관함 배치 순 (위→아래, 왼→오른쪽)

        // conditionConfig = 내구도 대략 등급 경계(HudConfigSO, MVP 마무리 F3). null이면 매도 줄 이름에 등급을 붙이지 않는다.
        public static TraderViewModel Build(IHideoutService hideout, HudConfigSO conditionConfig = null)
        {
            if (hideout == null)
                throw new ArgumentNullException(nameof(hideout));
            bool leaving = hideout.IsLeaving;
            int casings = hideout.Casings;

            var offers = new List<TraderOfferRow>();
            IReadOnlyList<TraderOffer> source = hideout.Offers;
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    TraderOffer offer = source[i];
                    if (offer.Item == null)
                        continue;
                    offers.Add(new TraderOfferRow(offer.Item, RaidResultViewModel.ItemName(offer.Item),
                        UiText.Price(offer.BuyPrice), !leaving && offer.BuyPrice <= casings));
                }
            }

            var placed = new List<PlacedItem>();
            IGridView stash = hideout.Stash;
            if (stash != null && stash.Items != null)
            {
                for (int i = 0; i < stash.Items.Count; i++)
                {
                    if (stash.Items[i].Item != null)
                        placed.Add(stash.Items[i]);
                }
            }
            // 안정 정렬 (같은 칸은 없지만 원래 순서를 지킨다)
            var ordered = new List<KeyValuePair<int, PlacedItem>>(placed.Count);
            for (int i = 0; i < placed.Count; i++)
                ordered.Add(new KeyValuePair<int, PlacedItem>(i, placed[i]));
            ordered.Sort((a, b) =>
            {
                int c = a.Value.Cell.y.CompareTo(b.Value.Cell.y);
                if (c == 0)
                    c = a.Value.Cell.x.CompareTo(b.Value.Cell.x);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });

            var sell = new List<TraderSellRow>(ordered.Count);
            foreach (KeyValuePair<int, PlacedItem> pair in ordered)
            {
                ItemInstance item = pair.Value.Item;
                int price = hideout.GetSellPrice(item);
                if (price < 0)
                    continue;   // 보관함 밖으로 보고됨 (서비스 기준) — 팔 수 없다
                int repairCost = hideout.GetRepairCost(item);
                bool needsRepair = repairCost >= 0;
                sell.Add(new TraderSellRow(item, UiText.ItemLabelWithCondition(item, conditionConfig), UiText.Price(price), !leaving,
                    needsRepair, needsRepair ? UiText.RepairButton(repairCost) : string.Empty,
                    needsRepair && !leaving && repairCost <= casings));
            }

            return new TraderViewModel(hideout.TraderName ?? string.Empty, offers, sell);
        }
    }
}
