using System;
using System.Collections.Generic;
using Ashfall.Core;

namespace Ashfall.UI
{
    // H 부위 상태 화면의 한 줄. RULE-05: 숫자·퍼센트 없이 등급 문구와 상태이상 이름만.
    public readonly struct BodyPartRow
    {
        public readonly BodyPart Part;
        public readonly string Label;
        public readonly string TierText;
        public readonly string TierClass;                 // USS 클래스 (색 토큰)
        public readonly IReadOnlyList<string> StatusTexts;

        public BodyPartRow(BodyPart part, string label, string tierText, string tierClass, IReadOnlyList<string> statusTexts)
        {
            Part = part;
            Label = label;
            TierText = tierText;
            TierClass = tierClass;
            StatusTexts = statusTexts;
        }
    }

    // 전신 줄의 상태 칩 하나 (예: "목마름"). RULE-05: 문구와 USS 클래스만.
    public readonly struct SurvivalChip
    {
        public readonly SurvivalStat Stat;
        public readonly SurvivalTier Tier;
        public readonly string Text;
        public readonly string TierClass;   // bs-survival--low / --critical
        public readonly string StatClass;   // 방사능만 bs-survival--radiation, 그 밖 빈 문자열

        public SurvivalChip(SurvivalStat stat, SurvivalTier tier, string text, string tierClass, string statClass)
        {
            Stat = stat;
            Tier = tier;
            Text = text;
            TierClass = tierClass;
            StatClass = statClass;
        }
    }

    // M7 H 화면 7부위 아래 "전신" 한 줄 (M7 설계 §1.5, Q43). 수분·에너지·방사능 순, Normal은 칩 없음(빈 줄).
    public readonly struct SurvivalRow
    {
        public readonly string Label;
        public readonly IReadOnlyList<SurvivalChip> Chips;

        public SurvivalRow(string label, IReadOnlyList<SurvivalChip> chips)
        {
            Label = label;
            Chips = chips;
        }

        public bool IsEmpty => Chips.Count == 0;
    }

    // RULE-05 부위 상태 화면 데이터. IBodyStatusView는 등급만 주므로 수치를 표시할 방법이 없다.
    public static class BodyStatusPresenter
    {
        private static readonly BodyPart[] Parts = (BodyPart[])Enum.GetValues(typeof(BodyPart));
        private static readonly StatusEffect[] Effects = (StatusEffect[])Enum.GetValues(typeof(StatusEffect));
        private static readonly SurvivalStat[] SurvivalStats = (SurvivalStat[])Enum.GetValues(typeof(SurvivalStat));

        public const string StatusClass = "bs-status";
        public const string SurvivalRowClass = "bs-survival-row";
        public const string SurvivalLowClass = "bs-survival--low";
        public const string SurvivalCriticalClass = "bs-survival--critical";
        public const string SurvivalRadiationClass = "bs-survival--radiation";

        // "전신" 줄: GetSurvivalTier(수분·에너지·방사능)를 문구로. 경계 판정은 Player(IBodyStatusView)가 한다.
        public static SurvivalRow BuildSurvival(IBodyStatusView view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));
            var chips = new List<SurvivalChip>(SurvivalStats.Length);
            foreach (SurvivalStat stat in SurvivalStats)
            {
                SurvivalTier tier = view.GetSurvivalTier(stat);
                string text = UiText.SurvivalStatus(stat, tier);
                if (string.IsNullOrEmpty(text))
                    continue;
                chips.Add(new SurvivalChip(stat, tier, text,
                    tier == SurvivalTier.Critical ? SurvivalCriticalClass : SurvivalLowClass,
                    stat == SurvivalStat.Radiation ? SurvivalRadiationClass : string.Empty));
            }
            return new SurvivalRow(UiText.SurvivalRowLabel, chips);
        }

        // BodyPart 순서대로 7줄
        public static IReadOnlyList<BodyPartRow> Build(IBodyStatusView view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            var rows = new List<BodyPartRow>(Parts.Length);
            foreach (BodyPart part in Parts)
            {
                var statuses = new List<string>();
                foreach (StatusEffect effect in Effects)
                {
                    if (view.HasStatus(part, effect))
                        statuses.Add(StatusText(effect));
                }
                HealthTier tier = view.GetTier(part);
                rows.Add(new BodyPartRow(part, PartLabel(part), TierText(tier), TierClass(tier), statuses));
            }
            return rows;
        }

        public static string PartLabel(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.Head: return "머리";
                case BodyPart.Thorax: return "흉부";
                case BodyPart.Stomach: return "복부";
                case BodyPart.LeftArm: return "왼팔";
                case BodyPart.RightArm: return "오른팔";
                case BodyPart.LeftLeg: return "왼다리";
                default: return "오른다리";
            }
        }

        public static string TierText(HealthTier tier)
        {
            switch (tier)
            {
                case HealthTier.Healthy: return "정상";
                case HealthTier.Injured: return "부상";
                case HealthTier.Critical: return "중상";
                default: return "파괴";
            }
        }

        public static string TierClass(HealthTier tier)
        {
            switch (tier)
            {
                case HealthTier.Healthy: return "tier-healthy";
                case HealthTier.Injured: return "tier-injured";
                case HealthTier.Critical: return "tier-critical";
                default: return "tier-destroyed";
            }
        }

        public static string StatusText(StatusEffect effect)
        {
            switch (effect)
            {
                case StatusEffect.Bleed: return "출혈";
                case StatusEffect.HeavyBleed: return "중출혈";
                default: return "골절";
            }
        }
    }
}
