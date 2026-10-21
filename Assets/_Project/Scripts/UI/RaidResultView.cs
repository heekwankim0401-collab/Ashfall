using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // 레이드 결과 패널 (MainMenu 씬, Art/UI/MainMenu.uxml). 데이터는 RaidResultViewModel.
    // 탄 수·HP 수치는 뷰모델이 만들지 않으므로 여기서도 그릴 수 없다.
    public sealed class RaidResultView
    {
        public const string PanelName = "result-panel";
        public const string TitleName = "result-title";
        public const string DurationName = "result-duration";
        public const string KillsName = "result-kills";
        public const string BroughtInName = "result-brought-in";
        public const string CarriedOutSectionName = "result-carried-out-section";
        public const string CarriedOutName = "result-carried-out";
        public const string LostSectionName = "result-lost-section";
        public const string LostName = "result-lost";
        public const string ConfirmName = "result-confirm";

        public const string LineClass = "result-line";
        public const string EmptyClass = "result-line--empty";

        private readonly VisualElement panel;
        private readonly Label title;
        private readonly Label duration;
        private readonly Label kills;
        private readonly VisualElement broughtIn;
        private readonly VisualElement carriedOutSection;
        private readonly VisualElement carriedOut;
        private readonly VisualElement lostSection;
        private readonly VisualElement lost;
        private readonly Button confirm;
        private readonly Action onConfirm;

        public RaidResultView(VisualElement documentRoot, Action onConfirm)
        {
            panel = Find<VisualElement>(documentRoot, PanelName);
            title = Find<Label>(documentRoot, TitleName);
            duration = Find<Label>(documentRoot, DurationName);
            kills = Find<Label>(documentRoot, KillsName);
            broughtIn = Find<VisualElement>(documentRoot, BroughtInName);
            carriedOutSection = Find<VisualElement>(documentRoot, CarriedOutSectionName);
            carriedOut = Find<VisualElement>(documentRoot, CarriedOutName);
            lostSection = Find<VisualElement>(documentRoot, LostSectionName);
            lost = Find<VisualElement>(documentRoot, LostName);
            confirm = Find<Button>(documentRoot, ConfirmName);
            confirm.text = UiText.ResultConfirm;
            this.onConfirm = onConfirm;
            confirm.clicked += onConfirm;
            SetVisible(false);
        }

        public void SetVisible(bool visible) =>
            panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void Render(RaidResultViewModel model)
        {
            if (model == null)
                return;
            title.text = model.Title;
            title.RemoveFromClassList(RaidResultViewModel.SurvivedClass);
            title.RemoveFromClassList(RaidResultViewModel.DiedClass);
            title.AddToClassList(model.OutcomeClass);
            duration.text = model.DurationText;
            kills.text = model.KillsText;
            Fill(broughtIn, model.BroughtIn);
            carriedOutSection.style.display = model.ShowCarriedOut ? DisplayStyle.Flex : DisplayStyle.None;
            Fill(carriedOut, model.CarriedOut);
            lostSection.style.display = model.ShowLost ? DisplayStyle.Flex : DisplayStyle.None;
            Fill(lost, model.Lost);
        }

        public void Dispose() => confirm.clicked -= onConfirm;

        private static void Fill(VisualElement list, IReadOnlyList<string> lines)
        {
            list.Clear();
            if (lines == null || lines.Count == 0)
            {
                var empty = new Label(UiText.ResultNone);
                empty.AddToClassList(LineClass);
                empty.AddToClassList(EmptyClass);
                list.Add(empty);
                return;
            }
            foreach (string line in lines)
            {
                var label = new Label(line);
                label.AddToClassList(LineClass);
                list.Add(label);
            }
        }

        internal static T Find<T>(VisualElement root, string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new InvalidOperationException(
                $"[MainMenu] UXML에 '{name}' 요소가 없다 (Art/UI/MainMenu.uxml 확인).");
    }
}
