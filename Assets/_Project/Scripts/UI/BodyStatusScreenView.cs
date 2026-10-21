using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // RULE-05 H 부위 상태 화면. 몸 실루엣 대신 7줄 목록(그레이박스). 수치·퍼센트는 그리지 않는다.
    public sealed class BodyStatusScreenView
    {
        public const string ScreenName = "body-status-screen";
        public const string ListName = "body-status-list";

        private readonly VisualElement screen;
        private readonly VisualElement list;

        public BodyStatusScreenView(VisualElement documentRoot)
        {
            screen = documentRoot.Q<VisualElement>(ScreenName) ?? throw HudView.Missing(ScreenName);
            list = documentRoot.Q<VisualElement>(ListName) ?? throw HudView.Missing(ListName);
            SetVisible(false);
        }

        public void SetVisible(bool visible) =>
            screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void Render(IReadOnlyList<BodyPartRow> rows)
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
        }
    }
}
