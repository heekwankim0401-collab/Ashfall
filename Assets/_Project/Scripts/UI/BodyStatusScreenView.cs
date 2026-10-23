using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // RULE-05 H 부위 상태 화면. 몸 실루엣 대신 7줄 목록(그레이박스) + M7 "전신" 한 줄(Q43). 수치·퍼센트·막대는 그리지 않는다.
    public sealed class BodyStatusScreenView
    {
        public const string ScreenName = "body-status-screen";
        public const string ListName = "body-status-list";
        public const string SurvivalName = "body-status-survival";

        private readonly VisualElement screen;
        private readonly VisualElement list;
        private readonly VisualElement survival;

        public BodyStatusScreenView(VisualElement documentRoot)
        {
            screen = documentRoot.Q<VisualElement>(ScreenName) ?? throw HudView.Missing(ScreenName);
            list = documentRoot.Q<VisualElement>(ListName) ?? throw HudView.Missing(ListName);
            survival = documentRoot.Q<VisualElement>(SurvivalName) ?? throw HudView.Missing(SurvivalName);
            SetVisible(false);
        }

        public void SetVisible(bool visible) =>
            screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        // 7부위만 (전신 줄은 비운다)
        public void Render(IReadOnlyList<BodyPartRow> rows) => Render(rows, null);

        public void Render(IReadOnlyList<BodyPartRow> rows, SurvivalRow? survivalRow)
        {
            list.Clear();
            foreach (BodyPartRow row in rows)
            {
                var line = new VisualElement();
                line.AddToClassList("bs-row");

                var part = new Label(row.Label);
                part.AddToClassList("bs-part");
                line.Add(part);

                var tier = new Label(row.TierText);
                tier.AddToClassList("bs-tier");
                tier.AddToClassList(row.TierClass);
                line.Add(tier);

                var statuses = new VisualElement();
                statuses.AddToClassList("bs-statuses");
                foreach (string status in row.StatusTexts)
                {
                    var chip = new Label(status);
                    chip.AddToClassList(BodyStatusPresenter.StatusClass);
                    statuses.Add(chip);
                }
                line.Add(statuses);

                list.Add(line);
            }

            RenderSurvival(survivalRow);
        }

        // "전신" 줄: 이름 + 상태 칩. Normal뿐이면 칩 없음(이름만).
        private void RenderSurvival(SurvivalRow? survivalRow)
        {
            survival.Clear();
            if (survivalRow == null)
                return;
            SurvivalRow row = survivalRow.Value;

            var line = new VisualElement();
            line.AddToClassList("bs-row");
            line.AddToClassList(BodyStatusPresenter.SurvivalRowClass);

            var label = new Label(row.Label);
            label.AddToClassList("bs-part");
            line.Add(label);

            var chips = new VisualElement();
            chips.AddToClassList("bs-statuses");
            foreach (SurvivalChip chip in row.Chips)
            {
                var text = new Label(chip.Text);
                text.AddToClassList(BodyStatusPresenter.StatusClass);
                text.AddToClassList(chip.TierClass);
                if (!string.IsNullOrEmpty(chip.StatClass))
                    text.AddToClassList(chip.StatClass);
                chips.Add(text);
            }
            line.Add(chips);

            survival.Add(line);
        }
    }
}
