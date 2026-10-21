using System;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // Esc 일시정지 화면 (TODO(GDD-?) Q16): [계속] / [레이드 포기] → 확인 한 번 더 [포기] / [취소].
    // 상태(확인 단계, 포기 가능 여부)는 GameUIPresenter가 정한다. 여기서는 그리기와 버튼 연결만.
    public sealed class PauseScreenView
    {
        public const string ScreenName = "pause-screen";
        public const string MainName = "pause-main";
        public const string ResumeName = "pause-resume";
        public const string AbandonName = "pause-abandon";
        public const string ConfirmName = "pause-confirm";
        public const string ConfirmYesName = "pause-confirm-yes";
        public const string ConfirmNoName = "pause-confirm-no";

        private readonly VisualElement screen;
        private readonly VisualElement main;
        private readonly VisualElement confirm;
        private readonly Button resume;
        private readonly Button abandon;
        private readonly Button yes;
        private readonly Button no;
        private readonly Action onResume, onAbandon, onYes, onNo;

        public PauseScreenView(VisualElement documentRoot, Action onResume, Action onAbandon, Action onYes, Action onNo)
        {
            screen = documentRoot.Q<VisualElement>(ScreenName) ?? throw HudView.Missing(ScreenName);
            main = documentRoot.Q<VisualElement>(MainName) ?? throw HudView.Missing(MainName);
            confirm = documentRoot.Q<VisualElement>(ConfirmName) ?? throw HudView.Missing(ConfirmName);
            resume = documentRoot.Q<Button>(ResumeName) ?? throw HudView.Missing(ResumeName);
            abandon = documentRoot.Q<Button>(AbandonName) ?? throw HudView.Missing(AbandonName);
            yes = documentRoot.Q<Button>(ConfirmYesName) ?? throw HudView.Missing(ConfirmYesName);
            no = documentRoot.Q<Button>(ConfirmNoName) ?? throw HudView.Missing(ConfirmNoName);

            // 문구는 UiText가 원본 (UXML text는 미리보기용)
            resume.text = UiText.PauseResume;
            abandon.text = UiText.PauseAbandon;
            yes.text = UiText.AbandonYes;
            no.text = UiText.AbandonNo;

            this.onResume = onResume;
            this.onAbandon = onAbandon;
            this.onYes = onYes;
            this.onNo = onNo;
            resume.clicked += onResume;
            abandon.clicked += onAbandon;
            yes.clicked += onYes;
            no.clicked += onNo;
            SetVisible(false);
        }

        public void SetVisible(bool visible) =>
            screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void Render(bool confirming, bool canAbandon)
        {
            main.style.display = confirming ? DisplayStyle.None : DisplayStyle.Flex;
            confirm.style.display = confirming ? DisplayStyle.Flex : DisplayStyle.None;
            // 확인 씬(레이드 정보 없음)이나 이미 포기를 요청했으면 [계속]만
            abandon.style.display = canAbandon ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Dispose()
        {
            resume.clicked -= onResume;
            abandon.clicked -= onAbandon;
            yes.clicked -= onYes;
            no.clicked -= onNo;
        }
    }
}
