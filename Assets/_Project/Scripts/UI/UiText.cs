using Ashfall.Core;

namespace Ashfall.UI
{
    // 화면에 쓰는 고정 문구. 수치(탄 수·무게·HP)는 여기서 만들지 않는다 (RULE-04, RULE-05, 정보 희소성).
    // TODO(M6): 현지화·폰트.
    public static class UiText
    {
        public const string HiddenItem = "?";
        public const string LootSearching = "검색 중…";
        public const string InventoryTitle = "인벤토리";
        public const string EquipmentTitle = "장비";
        public const string StorageTitle = "소지품";
        public const string QuickSlotTitle = "퀵슬롯";
        public const string BodyStatusTitle = "부위 상태";

        // ── M4 레이드 화면 (M4 설계 §3.4·§3.5) ─────────────
        public const string PauseTitle = "일시정지";
        public const string PauseResume = "계속";
        public const string PauseAbandon = "레이드 포기";
        // TODO(GDD-?) Q16: 포기 = 사망과 같은 RULE-01. 확인 문구는 문서에 없다.
        public const string AbandonConfirm = "레이드를 포기하면 가져온 장비를 모두 잃는다.";
        public const string AbandonYes = "포기";
        public const string AbandonNo = "취소";
        public const string MapTitle = "지도";
        public const string MainMenuTitle = "ASHFALL";
        // M5 Q20: 메인 메뉴 [은신처] / [종료].
        public const string MenuHideout = "은신처";
        public const string MenuQuit = "종료";
        public const string ResultConfirm = "확인";
        public const string ResultBroughtIn = "반입";
        public const string ResultCarriedOut = "반출";
        public const string ResultLost = "소실";
        public const string ResultNone = "없음";

        // ── M5 은신처 (M5 설계 §3.4) ─────────────────────
        // 탄 수·HP·무게 숫자는 은신처에서도 만들지 않는다. 가격·탄피·키트 남은 분은 메뉴 화면 정보라 표시한다.
        public const string HideoutTitle = "은신처";
        public const string HideoutTabLoadout = "장비";
        public const string HideoutTabTrader = "상인";
        public const string HideoutKitButton = "부랑자 키트 받기";
        public const string HideoutKitReady = "받을 수 있다";
        public const string HideoutKitUnavailable = "받을 수 없다";
        public const string HideoutStartRaid = "레이드 시작";
        public const string HideoutMainMenu = "메인 메뉴";
        public const string TraderOffersTitle = "판매 목록";
        public const string TraderSellTitle = "보관함 (판매)";
        public const string TraderBuy = "구매";
        public const string TraderSell = "판매";
        public const string TraderSellEmpty = "보관함이 비어 있다";
        public const string NoticeTitle = "알림";
        public const string NoticeConfirm = "확인";
        public const string NoticeLostTitle = "잃은 장비";
        // TODO(GDD-?) Q6: 알림 문구는 문서에 없다. 같은 실행에서 결과 없이 돌아온 경우도 같은 문구(보고서에 구분 정보가 없다).
        public const string NoticeCrash = "지난 레이드 도중 게임이 종료되어 사망으로 처리했다 (RULE-03). 반입 장비를 잃었다.";
        public const string NoticeCorrupt = "세이브 파일이 손상되어 새로 시작한다.";   // TODO(GDD-?) Q22
        public const string NoticeUnsupportedVersion = "세이브 파일 버전을 읽을 수 없어 새로 시작한다.";
        public const string NoticeQuarantined = "이전 파일은 따로 보관했다: ";
        // TODO(GDD-?) Q22·M5 리뷰 1: 읽기 오류(권한·잠금) — 파일은 그대로, 이 실행은 저장하지 않는다.
        public const string NoticeReadError =
            "세이브 파일을 읽지 못했다. 파일은 건드리지 않았다. 이번 실행에서는 저장하지 않으며(읽기 전용) 레이드를 시작할 수 없다. 게임을 다시 시작해 보라.";
        public const string MessageSaveFailed = "저장하지 못했다.";
        public const string MessageReadOnly = "읽기 전용 — 이번 실행에서는 저장하지 않는다.";
        public const string MessageStartRaidSaveFailed = "저장하지 못해 레이드를 시작할 수 없다.";   // W4 실패 (RULE-03)
        public const string MessageStartRaidReadOnly = "세이브를 읽지 못해 이번 실행에서는 레이드를 시작할 수 없다.";
        public const string MessageStartRaidFailed = "레이드를 시작할 수 없다.";
        public const string MessageMainMenuFailed = "메인 메뉴로 갈 수 없다.";
        // W2 저장 실패 (M5 리뷰 6): 이동하지 않는다. 다시 누르면 저장이 또 실패해도 이동한다.
        public const string MessageMainMenuSaveFailed = "저장하지 못했다. [메인 메뉴]를 다시 누르면 저장하지 못한 채로 나간다.";
        public const string MessageKitOk = "부랑자 키트를 보관함에 받았다.";
        public const string MessageKitOnCooldown = "부랑자 키트를 아직 받을 수 없다.";
        public const string MessageKitNoSpace = "보관함에 자리가 없어 부랑자 키트를 받을 수 없다.";   // TODO(GDD-?) Q15
        public const string MessageUnavailable = "지금은 할 수 없다.";
        public const string MessageNotEnoughCasings = "탄피가 모자란다.";
        public const string MessageNoSpace = "보관함에 자리가 없다.";
        public const string MessageNotForSale = "팔지 않는 물건이다.";
        public const string MessageNotInStash = "보관함에 있는 것만 팔 수 있다.";   // TODO(GDD-?) Q12

        public static string Casings(int casings) => "탄피 " + casings;

        public static string Price(int casings) => casings + " 탄피";

        // "N분 뒤" (분 올림, 최소 1분). M5 설계 §3.4
        public static string KitRemaining(float remainingSec)
        {
            int minutes = (int)System.Math.Ceiling(remainingSec / 60f);
            if (minutes < 1)
                minutes = 1;
            return minutes + "분 뒤";
        }

        public static string Bought(string itemName) => "구매했다: " + itemName;

        public static string Sold(string itemName) => "판매했다: " + itemName;

        public static string NoticeUnknownItems(int count) => "알 수 없는 아이템 " + count + "개를 버렸다.";   // TODO(GDD-?) Q23

        public static string NoticeDroppedItems(int count) => "놓을 자리가 없는 아이템 " + count + "개를 버렸다.";

        public static string Trade(TradeResult result)
        {
            switch (result)
            {
                case TradeResult.NotEnoughCasings: return MessageNotEnoughCasings;
                case TradeResult.NoSpace: return MessageNoSpace;
                case TradeResult.NotForSale: return MessageNotForSale;
                case TradeResult.NotInStash: return MessageNotInStash;
                case TradeResult.Unavailable: return MessageUnavailable;
                default: return string.Empty;
            }
        }

        // ── M7 제작대 (§9, M7 설계 §1.5, Q44~Q47) ──
        public const string HideoutTabCrafting = "제작";
        public const string CraftingTitle = "제작대";
        public const string CraftingHint = "재료는 보관함에서만 쓴다.";   // Q44
        public const string CraftingButton = "제작";
        public const string CraftingEmpty = "제작법이 없다";               // 레시피 없음 (CraftingConfigSO 미연결 포함)
        public const string MessageCraftMissingMaterials = "재료가 모자란다.";
        public const string MessageCraftNoSpace = "보관함에 자리가 없어 만들 수 없다.";
        public const string MessageCraftUnknownRecipe = "알 수 없는 제작법이다.";

        public static string Crafted(string itemName) => "만들었다: " + itemName;

        // 재료 "가진 수/필요 수" — 재료 아이템 개수뿐 (탄 수·체력 수치 아님, RULE-04·05 대상 밖)
        public static string CraftingInputCount(int owned, int required) => owned + "/" + required;

        // 성공 문구는 Crafted(이름)
        public static string Craft(CraftResult result)
        {
            switch (result)
            {
                case CraftResult.MissingMaterials: return MessageCraftMissingMaterials;
                case CraftResult.NoSpace: return MessageCraftNoSpace;
                case CraftResult.UnknownRecipe: return MessageCraftUnknownRecipe;
                case CraftResult.Unavailable: return MessageUnavailable;
                default: return string.Empty;
            }
        }

        // ── M7 부위 상태 화면 "전신" 줄 (Q43, RULE-05: 등급 문구만, 숫자·막대 없음) ──
        public const string SurvivalRowLabel = "전신";

        // Normal → 빈 문자열 (표시 안 함)
        public static string SurvivalStatus(SurvivalStat stat, SurvivalTier tier)
        {
            if (tier == SurvivalTier.Normal)
                return string.Empty;
            bool critical = tier == SurvivalTier.Critical;
            switch (stat)
            {
                case SurvivalStat.Hydration: return critical ? "탈수" : "목마름";
                case SurvivalStat.Energy: return critical ? "허기" : "배고픔";
                default: return critical ? "방사선 피폭" : "방사능 오염";
            }
        }

        public static string Kit(KitClaimResult result)
        {
            switch (result)
            {
                case KitClaimResult.Ok: return MessageKitOk;
                case KitClaimResult.OnCooldown: return MessageKitOnCooldown;
                case KitClaimResult.NoSpace: return MessageKitNoSpace;
                default: return MessageUnavailable;
            }
        }

        // 아이콘이 없으므로 이름 텍스트 상자로 그린다. 탄창 탄 수·의약품 횟수는 붙이지 않는다.
        public static string ItemLabel(ItemInstance item)
        {
            if (item == null)
                return string.Empty;
            var def = item.Definition;
            return string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName;
        }

        public static string SlotLabel(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Primary1: return "주무기 1";
                case EquipSlot.Primary2: return "주무기 2";
                case EquipSlot.Secondary: return "보조무기";
                case EquipSlot.Helmet: return "헬멧";
                case EquipSlot.BodyArmor: return "방탄복";
                case EquipSlot.TacticalVest: return "전술조끼";
                default: return "배낭";
            }
        }

        public static string GridTitle(GridKind kind)
        {
            switch (kind)
            {
                case GridKind.Pocket: return "주머니";
                case GridKind.TacticalVest: return "전술조끼";
                case GridKind.Backpack: return "배낭";
                default: return "루트";
            }
        }

        public static string InteractionVerb(InteractionKind kind)
        {
            switch (kind)
            {
                case InteractionKind.OpenContainer: return "열기";
                case InteractionKind.Activate: return "작동";   // M4 EXT-02 전원 스위치 (IWorldInteractable)
                default: return "줍기";
            }
        }

        // "F  열기: 상자". 키 이름은 입력 바인딩 표시 문자열을 받는다.
        public static string Prompt(InteractionKind kind, string targetName, string keyLabel)
        {
            string verb = InteractionVerb(kind);
            string head = string.IsNullOrEmpty(keyLabel) ? verb : keyLabel + "  " + verb;
            return string.IsNullOrEmpty(targetName) ? head : head + ": " + targetName;
        }
    }
}
