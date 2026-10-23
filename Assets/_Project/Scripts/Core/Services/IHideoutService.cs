using System.Collections.Generic;
using UnityEngine;

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

    // MVP 마무리 F2 보관함 업그레이드 결과 (§9, MF-Q5~Q9)
    public enum StashUpgradeResult
    {
        Ok,
        NotEnoughCasings, // 탄피 부족 → 변화 없음
        MaxLevel,         // 이미 최대 단계
        Unavailable,      // 떠나는 중·프로필 없음 등
    }

    // 다음 보관함 단계. HasNext = false면 최대 단계(NewSize·Cost = 0)
    public readonly struct StashUpgradeOffer
    {
        public readonly bool HasNext;
        public readonly Vector2Int NewSize;
        public readonly int Cost;   // 탄피

        public StashUpgradeOffer(Vector2Int newSize, int cost)
        {
            HasNext = true;
            NewSize = newSize;
            Cost = cost;
        }

        public static StashUpgradeOffer None => default;
    }

    // M7 제작대 결과 (§9, M7 설계 §1.5, Q44~Q47)
    public enum CraftResult
    {
        Ok,
        MissingMaterials, // 보관함 재료가 모자람 (Q44 — 보관함만)
        NoSpace,          // 산출물이 보관함에 다 들어가지 않음 → 재료를 원래 자리로 되돌림
        UnknownRecipe,    // 레시피 id 없음
        Unavailable,      // 떠나는 중·프로필 없음 등
    }

    // 레시피 재료 한 줄. Owned = 지금 보관함에 있는 같은 정의 인스턴스 수 (UI "가진 수/필요 수" — 재료 개수만, RULE-04·05 수치 아님)
    public readonly struct CraftingInputView
    {
        public readonly ItemDefinitionSO Item;
        public readonly int Required;
        public readonly int Owned;

        public CraftingInputView(ItemDefinitionSO item, int required, int owned)
        {
            Item = item;
            Required = required;
            Owned = owned;
        }

        // 이 줄만 본 충족. 같은 정의가 여러 줄인 레시피는 정의 합계로 판정해야 한다(CraftingModel.HasMaterials·UI CraftingViewModel이 합계 기준, 리뷰 7C 4).
        public bool IsSatisfied => Owned >= Required;
    }

    // 레시피 한 개 (M7 설계 §3.8). 산출 = Output 정의 × OutputCount개 (탄창은 가득 찬 상태, Q46)
    public readonly struct CraftingRecipeView
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly ItemDefinitionSO Output;
        public readonly int OutputCount;
        public readonly IReadOnlyList<CraftingInputView> Inputs;

        public CraftingRecipeView(string id, string displayName, ItemDefinitionSO output, int outputCount,
            IReadOnlyList<CraftingInputView> inputs)
        {
            Id = id;
            DisplayName = displayName;
            Output = output;
            OutputCount = outputCount;
            Inputs = inputs;
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
        // M7 제작대 (§9, Q44~Q47): 재료 = 보관함만, 즉시 완료, 탄피 비용 없음. 성공하면 메모리에서만 바뀌고 W2/W3/W4에서 저장(상인과 같음),
        // HideoutChanged 발행. Recipes는 읽을 때의 보관함 기준(Owned)이다.
        IReadOnlyList<CraftingRecipeView> Recipes { get; }
        CraftResult TryCraft(string recipeId);
        // MVP 마무리 F2 보관함 업그레이드 (§9, MF-Q5~Q9): 탄피로 줄 수를 늘린다(너비 고정, 그리드 제자리 확장 — Stash 참조 그대로).
        // 확인 없이 즉시, 읽기 전용 프로필에서도 허용(상인과 같음). 성공하면 메모리에서만 바뀌고 W2/W3/W4에서 저장 + HideoutChanged.
        int StashLevel { get; }
        StashUpgradeOffer NextStashUpgrade { get; }
        StashUpgradeResult TryUpgradeStash();
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
