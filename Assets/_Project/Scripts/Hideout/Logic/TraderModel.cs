using System;
using System.Collections.Generic;
using Ashfall.Core;

namespace Ashfall.Hideout
{
    // SYS-HUB 상인 매매 (§9, M5 설계 §3.2, Q8~Q12). 순수 C#.
    // - 매수: 판매 목록 안의 정의만. 탄피 부족 → NotEnoughCasings, 보관함 자리 없음 → NoSpace(돈을 받지 않는다).
    //   보관함 자동 배치에 성공한 뒤 차감. 탄창은 가득, 무기는 탄창 없이 (ItemFactory.Create).
    // - 매도: 보관함에 있는 아이템만 (Q12). 가격 0도 팔린다(= 처분, 은신처에는 버리기가 없다).
    // - 수리(MVP 마무리 F3): 보관함의 내구도 있는 아이템만, 탄피로 100%로 (MF-Q13·Q14).
    // 변경 알림(HideoutChanged)은 호출자(HideoutService)가 낸다.
    public sealed class TraderModel
    {
        private readonly PlayerProfile profile;
        private readonly TraderConfigSO trader;
        private readonly List<TraderOffer> offers = new List<TraderOffer>();

        public TraderModel(PlayerProfile profile, TraderConfigSO trader)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.trader = trader != null ? trader : throw new ArgumentNullException(nameof(trader));

            // catalog 순, null·가격 ≤ 0·중복 제외. 재고·시세 변동이 없어 한 번만 만든다 (Q11).
            if (trader.catalog != null)
            {
                foreach (ItemDefinitionSO item in trader.catalog)
                {
                    if (item == null || IndexOfOffer(item) >= 0)
                        continue;
                    int price = PriceRules.BuyPrice(item, trader);
                    if (price > 0)
                        offers.Add(new TraderOffer(item, price));
                }
            }
        }

        public string TraderName => trader.displayName;
        public IReadOnlyList<TraderOffer> Offers => offers;

        // 보관함 밖·null → -1
        public int GetSellPrice(ItemInstance item)
        {
            if (item == null || !profile.Stash.Contains(item))
                return -1;
            return PriceRules.SellPrice(item, trader);
        }

        // 목록 밖·null → NotForSale. 탄피 확인 → 자리(배치) → 차감 순.
        public TradeResult TryBuy(ItemDefinitionSO item)
        {
            int index = item != null ? IndexOfOffer(item) : -1;
            if (index < 0)
                return TradeResult.NotForSale;

            int price = offers[index].BuyPrice;
            if (profile.Casings < price)
                return TradeResult.NotEnoughCasings;

            ItemInstance bought = ItemFactory.Create(item);
            if (!StashRules.TryPlaceNew(profile.Stash, bought))
                return TradeResult.NoSpace;
            profile.Casings -= price;
            return TradeResult.Ok;
        }

        // 보관함 밖·null → NotInStash. 보관함에서 빼고 매도가만큼 탄피를 더한다.
        public TradeResult TrySell(ItemInstance item)
        {
            if (item == null || !profile.Stash.Contains(item))
                return TradeResult.NotInStash;

            int price = Math.Max(0, PriceRules.SellPrice(item, trader));
            if (!profile.Stash.Remove(item))
                return TradeResult.NotInStash;
            long total = (long)profile.Casings + price;
            profile.Casings = total >= int.MaxValue ? int.MaxValue : (int)total;
            return TradeResult.Ok;
        }

        // MVP 마무리 F3 (MF-Q13·Q14). 보관함 밖·null·수리 불필요 → -1
        public int GetRepairCost(ItemInstance item)
        {
            if (item == null || !profile.Stash.Contains(item))
                return -1;
            return PriceRules.RepairCost(item, trader);
        }

        // 보관함 밖 → NotInStash, 내구도 없음·100% → NotNeeded, 탄피 부족 → NotEnoughCasings(변화 없음).
        // 성공 = 탄피 차감 + 내구도 최대(최대 내구도 감소 없음). 아이템 자리는 그대로.
        public RepairResult TryRepair(ItemInstance item)
        {
            if (item == null || !profile.Stash.Contains(item))
                return RepairResult.NotInStash;
            int cost = PriceRules.RepairCost(item, trader);
            if (cost < 0)
                return RepairResult.NotNeeded;
            if (profile.Casings < cost)
                return RepairResult.NotEnoughCasings;
            if (!DurabilityRules.RestoreFull(item))
                return RepairResult.NotNeeded;
            profile.Casings -= cost;
            return RepairResult.Ok;
        }

        private int IndexOfOffer(ItemDefinitionSO item)
        {
            for (int i = 0; i < offers.Count; i++)
                if (offers[i].Item == item)
                    return i;
            return -1;
        }
    }
}
