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
        public const string MenuStartRaid = "레이드 시작";
        public const string MenuQuit = "종료";
        public const string ResultConfirm = "확인";
        public const string ResultBroughtIn = "반입";
        public const string ResultCarriedOut = "반출";
        public const string ResultLost = "소실";
        public const string ResultNone = "없음";

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
