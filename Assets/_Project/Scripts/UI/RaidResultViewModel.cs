using System;
using System.Collections.Generic;
using Ashfall.Core;

namespace Ashfall.UI
{
    // 레이드 결과 패널 데이터 (M4 설계 §3.4, TODO(GDD-?) Q18: 결과·원인/탈출구·시간·처치 수·반입/반출/소실 목록).
    // 아이템은 표시 이름 + 같은 정의 묶음 수량("군용 소총 탄창 ×3")만. 탄 수·의약품 횟수·HP 수치는 만들지 않는다
    // (RULE-04·05 정보 희소성 원칙을 결과 화면에도 그대로 적용).
    public sealed class RaidResultViewModel
    {
        private RaidResultViewModel(string title, string outcomeClass, string durationText, string killsText,
            IReadOnlyList<string> broughtIn, IReadOnlyList<string> carriedOut, IReadOnlyList<string> lost,
            bool showCarriedOut, bool showLost)
        {
            Title = title;
            OutcomeClass = outcomeClass;
            DurationText = durationText;
            KillsText = killsText;
            BroughtIn = broughtIn;
            CarriedOut = carriedOut;
            Lost = lost;
            ShowCarriedOut = showCarriedOut;
            ShowLost = showLost;
        }

        public string Title { get; }
        public string OutcomeClass { get; }          // USS 클래스 (생존 = Bone, 그 밖 = Ember)
        public string DurationText { get; }          // "레이드 시간 12:34"
        public string KillsText { get; }             // "처치 3"
        public IReadOnlyList<string> BroughtIn { get; }
        public IReadOnlyList<string> CarriedOut { get; }
        public IReadOnlyList<string> Lost { get; }
        public bool ShowCarriedOut { get; }          // 탈출일 때만 (RULE-01)
        public bool ShowLost { get; }                // 사망·MIA·포기일 때만

        public const string SurvivedClass = "result--survived";
        public const string DiedClass = "result--died";

        public static RaidResultViewModel From(RaidResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            bool survived = result.Survived;
            return new RaidResultViewModel(
                TitleFor(result),
                survived ? SurvivedClass : DiedClass,
                "레이드 시간 " + FormatDuration(result.DurationSec),
                "처치 " + result.EnemiesKilled,
                Group(result.BroughtIn),
                GroupInstances(result.CarriedOut),
                Group(result.Lost),
                survived,
                !survived);
        }

        public static string TitleFor(RaidResult result)
        {
            switch (result.Outcome)
            {
                case RaidOutcome.Extracted:
                    return string.IsNullOrEmpty(result.ExtractionName) ? "탈출 성공" : "탈출 성공 — " + result.ExtractionName;
                case RaidOutcome.Killed:
                    return result.HasDeathCause ? "사망 — " + DeathCauseLabel(result) : "사망";
                case RaidOutcome.MissingInAction:
                    return "작전 중 실종 (MIA)";
                default:
                    return "레이드 포기";   // TODO(GDD-?) Q16
            }
        }

        // MVP 마무리 F1(TODO(GDD-?) MF-Q2·Q4): 출혈·탈수 사망은 부위 없이 종류만, 피격 사망은 부위 이름.
        private static string DeathCauseLabel(RaidResult result)
        {
            switch (result.DeathKind)
            {
                case DeathCauseKind.Bleeding: return UiText.DeathBleeding;
                case DeathCauseKind.Dehydration: return UiText.DeathDehydration;
                default: return BodyStatusPresenter.PartLabel(result.DeathCause);
            }
        }

        // 종료 오버레이 한 줄 (레이드 씬, ScreenKind.RaidEnd). 원인·목록은 MainMenu 결과 패널에서.
        public static string OverlayText(RaidOutcome outcome)
        {
            switch (outcome)
            {
                case RaidOutcome.Extracted: return "탈출 성공";
                case RaidOutcome.Killed: return "사망";
                case RaidOutcome.MissingInAction: return "작전 중 실종";
                default: return "레이드 포기";
            }
        }

        // 분:초 (25분 제한이라 시간 단위는 없다)
        public static string FormatDuration(float seconds)
        {
            int total = seconds > 0f ? (int)seconds : 0;
            int minutes = total / 60;
            int secs = total % 60;
            return minutes + ":" + (secs < 10 ? "0" : string.Empty) + secs;
        }

        // 같은 정의를 처음 나온 순서로 묶는다. "이름 ×n" (n = 1이면 이름만). 비면 빈 목록(뷰가 "없음").
        public static IReadOnlyList<string> Group(IReadOnlyList<ItemDefinitionSO> definitions)
        {
            var order = new List<ItemDefinitionSO>();
            var counts = new Dictionary<ItemDefinitionSO, int>();
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Count; i++)
                    Count(definitions[i], order, counts);
            }
            return Lines(order, counts);
        }

        public static IReadOnlyList<string> GroupInstances(IReadOnlyList<ItemInstance> items)
        {
            var order = new List<ItemDefinitionSO>();
            var counts = new Dictionary<ItemDefinitionSO, int>();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] != null)
                        Count(items[i].Definition, order, counts);
                }
            }
            return Lines(order, counts);
        }

        public static string ItemName(ItemDefinitionSO definition) =>
            definition == null ? UiText.HiddenItem
            : string.IsNullOrEmpty(definition.displayName) ? definition.id : definition.displayName;

        private static void Count(ItemDefinitionSO definition, List<ItemDefinitionSO> order, Dictionary<ItemDefinitionSO, int> counts)
        {
            if (definition == null)
                return;
            if (counts.TryGetValue(definition, out int n))
            {
                counts[definition] = n + 1;
                return;
            }
            counts.Add(definition, 1);
            order.Add(definition);
        }

        private static IReadOnlyList<string> Lines(List<ItemDefinitionSO> order, Dictionary<ItemDefinitionSO, int> counts)
        {
            var lines = new string[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                int n = counts[order[i]];
                lines[i] = n > 1 ? ItemName(order[i]) + " ×" + n : ItemName(order[i]);
            }
            return lines;
        }
    }
}
