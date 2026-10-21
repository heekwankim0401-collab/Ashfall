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

    // RULE-05 부위 상태 화면 데이터. IBodyStatusView는 등급만 주므로 수치를 표시할 방법이 없다.
    public static class BodyStatusPresenter
    {
        private static readonly BodyPart[] Parts = (BodyPart[])Enum.GetValues(typeof(BodyPart));
        private static readonly StatusEffect[] Effects = (StatusEffect[])Enum.GetValues(typeof(StatusEffect));

        public const string StatusClass = "bs-status";

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
