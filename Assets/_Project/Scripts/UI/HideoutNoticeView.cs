using System;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // 은신처 알림 패널 (Art/UI/HideoutUI.uxml). 세이브 손상·RULE-03 처리 등. 데이터는 HideoutNoticeViewModel.
    public sealed class HideoutNoticeView
    {
        public const string PanelName = "hideout-notice-panel";
        public const string TextName = "hideout-notice-text";
        public const string ListSectionName = "hideout-notice-list-section";
        public const string ListName = "hideout-notice-list";
        public const string ConfirmName = "hideout-notice-confirm";

        private readonly VisualElement panel;
        private readonly Label text;
        private readonly VisualElement listSection;
        private readonly VisualElement list;
        private readonly Button confirm;
        private readonly Action onConfirm;

        public HideoutNoticeView(VisualElement documentRoot, Action onConfirm)
        {
            panel = RaidResultView.Find<VisualElement>(documentRoot, PanelName);
            text = RaidResultView.Find<Label>(documentRoot, TextName);
            listSection = RaidResultView.Find<VisualElement>(documentRoot, ListSectionName);
            list = RaidResultView.Find<VisualElement>(documentRoot, ListName);
            confirm = RaidResultView.Find<Button>(documentRoot, ConfirmName);
            confirm.text = UiText.NoticeConfirm;
            this.onConfirm = onConfirm ?? (() => { });
            confirm.clicked += this.onConfirm;
            SetVisible(false);
        }

        public void SetVisible(bool visible) =>
            panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void Render(HideoutNoticeViewModel model)
        {
            if (model == null)
                return;
            text.text = model.Text;
            listSection.style.display = model.ShowLost ? DisplayStyle.Flex : DisplayStyle.None;
            list.Clear();
            if (!model.ShowLost)
                return;
            if (model.Lost.Count == 0)
            {
                var empty = new Label(UiText.ResultNone) { pickingMode = PickingMode.Ignore };
                empty.AddToClassList(RaidResultView.LineClass);
                empty.AddToClassList(RaidResultView.EmptyClass);
                list.Add(empty);
                return;
            }
            foreach (string line in model.Lost)
            {
                var label = new Label(line) { pickingMode = PickingMode.Ignore };
                label.AddToClassList(RaidResultView.LineClass);
                list.Add(label);
            }
        }

        public void Dispose() => confirm.clicked -= onConfirm;
    }
}
