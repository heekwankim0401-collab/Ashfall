using System.Collections.Generic;

namespace Ashfall.Core
{
    // 상인 거래 결과 (§9, M5 설계 §3.2)
    public enum TradeResult
    {
        Ok,
        NotEnoughCasings,
        NoSpace,          // 보관함 자리 없음 (돈을 받지 않는다)
        NotForSale,       // 판매 목록 밖
        NotInStash,       // 매도는 보관함 아이템만 (Q12)
        Unavailable,      // 떠나는 중·프로필 없음 등
    }

    // 부랑자 키트 수령 결과 (§9, M5 설계 §3.3)
    public enum KitClaimResult
    {
        Ok,
        OnCooldown,
        NoSpace,          // 보관함에 전부 들어가지 않음 → 지급 없음, 쿨다운 소모 없음 (Q15)
        Unavailable,
    }

    // 상인 판매 목록 한 줄
    public readonly struct TraderOffer
    {
        public readonly ItemDefinitionSO Item;
        public readonly int BuyPrice;

        public TraderOffer(ItemDefinitionSO item, int buyPrice)
        {
            Item = item;
            BuyPrice = buyPrice;
        }
    }

    // SYS-HUB 은신처 조회·명령 (M5 설계 §1.1, §4.1). Hideout(HideoutController)이 등록하고 은신처 UI가 쓴다.
    // 결과(성공·이유)가 동기로 필요해 이벤트가 아니라 서비스. 변경 알림은 HideoutChanged.
    public interface IHideoutService
    {
        int Casings { get; }
        string TraderName { get; }
        IReadOnlyList<TraderOffer> Offers { get; }
        IGridView Stash { get; }
        int GetSellPrice(ItemInstance item);          // 보관함에 없으면 -1
        TradeResult TryBuy(ItemDefinitionSO item);
        TradeResult TrySell(ItemInstance item);
        bool IsKitAvailable { get; }
        float KitCooldownRemainingSec { get; }        // 0 = 받을 수 있음
        KitClaimResult TryClaimKit();
        SaveLoadReport PendingNotice { get; }         // ProfileService.PendingNotice
        bool LastSaveFailed { get; }
        bool IsReadOnly { get; }                      // 세이브를 읽지 못한 실행 — 저장 막음, 레이드 시작 불가 (ProfileService.IsReadOnly)
        void AcknowledgeNotice();
        bool IsLeaving { get; }                       // 레이드 시작·메뉴 복귀 요청 후 (버튼 비활성)
        bool StartRaid();                             // 현재 은신처 장비 = 반입
        // W2. 저장 실패면 이동하지 않고 false(LastSaveFailed) — 다음 호출은 저장이 또 실패해도 이동한다. 읽기 전용이면 바로 이동.
        bool ReturnToMainMenu();
    }
}
