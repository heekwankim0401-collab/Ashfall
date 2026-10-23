using Ashfall.Core;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // §6 희귀도 색 (M7 설계 §3.11·§4.8, Q49 A): Rarity → USS 클래스. 색은 Ashfall.uss가 정한다
    // (고철 Ash_Grey, 일반 Bone, 우수 Toxic, 희귀 = 팔레트 밖 청색 상수 1개, 유물 Ember 발광).
    // 인벤토리·보관함 아이템 칸, 퀵슬롯, 상인 줄이 같이 쓴다. 검색 전 "?" 아이템에는 붙이지 않는다(§6 검색 연출).
    public static class RarityStyle
    {
        public const string BaseClass = "rarity";
        public const string ScrapClass = "rarity--scrap";
        public const string CommonClass = "rarity--common";
        public const string UncommonClass = "rarity--uncommon";
        public const string RareClass = "rarity--rare";
        public const string RelicClass = "rarity--relic";

        // enum 순서(Scrap~Relic)와 같다
        public static readonly string[] AllClasses = { ScrapClass, CommonClass, UncommonClass, RareClass, RelicClass };

        public static string ClassFor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Scrap: return ScrapClass;
                case Rarity.Common: return CommonClass;
                case Rarity.Uncommon: return UncommonClass;
                case Rarity.Rare: return RareClass;
                case Rarity.Relic: return RelicClass;
                // 범위 밖(데이터 오류·새 값) = 기본 테두리. 예외를 던지면 칸 하나 때문에 화면 전체가 실패한다 (7D 리뷰 낮음 8).
                default: return null;
            }
        }

        // 정의가 없으면 null (클래스 없음 = 기본 테두리)
        public static string ClassFor(ItemDefinitionSO definition) =>
            definition != null ? ClassFor(definition.rarity) : null;

        public static string ClassFor(ItemInstance item) =>
            item != null ? ClassFor(item.Definition) : null;

        // 다른 희귀도 클래스를 지우고 하나만 붙인다. styleClass가 null이면 전부 지운다.
        public static void Apply(VisualElement element, string styleClass)
        {
            if (element == null)
                return;
            foreach (string c in AllClasses)
                element.RemoveFromClassList(c);
            element.EnableInClassList(BaseClass, styleClass != null);
            if (styleClass != null)
                element.AddToClassList(styleClass);
        }

        public static void Apply(VisualElement element, ItemDefinitionSO definition) => Apply(element, ClassFor(definition));

        public static void Apply(VisualElement element, ItemInstance item) => Apply(element, ClassFor(item));
    }
}
