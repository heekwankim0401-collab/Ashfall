using System;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // Esc 일시정지 화면 (TODO(GDD-?) Q16): [계속] / [레이드 포기] → 확인 한 번 더 [포기] / [취소].
    // DEV-RETURN: onDevReturn을 주면(에디터·개발 빌드) [귀환(개발)] 버튼을 코드로 만들어 붙인다 → 확인 [귀환] / [취소].
    //             릴리스 빌드는 null을 넘겨 버튼 자체가 없다(UXML에도 없음).
    // 상태(확인 단계, 포기·귀환 가능 여부)는 GameUIPresenter가 정한다. 여기서는 그리기와 버튼 연결만.
    public sealed class PauseScreenView
    {
        public const string ScreenName = "pause-screen";
        public const string MainName = "pause-main";
        public const string ResumeName = "pause-resume";
        public const string AbandonName = "pause-abandon";
        public const string ConfirmName = "pause-confirm";
        public const string ConfirmTextName = "pause-confirm-text";
        public const string ConfirmYesName = "pause-confirm-yes";
        public const string ConfirmNoName = "pause-confirm-no";
        public const string DevReturnName = "pause-dev-return";   // DEV-RETURN (코드로 만든다)

        private readonly VisualElement screen;
        private readonly VisualElement main;
        private readonly VisualElement confirm;
        private readonly Label confirmText;   // 없으면(옛 UXML) 문구를 바꾸지 않는다
        private readonly Button resume;
        private readonly Button abandon;
        private readonly Button devReturn;    // DEV-RETURN: 릴리스 빌드면 null
        private readonly Button yes;
        private readonly Button no;
        private readonly Action onResume, onAbandon, onYes, onNo, onDevReturn;

        public PauseScreenView(VisualElement documentRoot, Action onResume, Action onAbandon, Action onYes, Action onNo,
            Action onDevReturn = null)
        {
            screen = documentRoot.Q<VisualElement>(ScreenName) ?? throw HudView.Missing(ScreenName);
            main = documentRoot.Q<VisualElement>(MainName) ?? throw HudView.Missing(MainName);
            confirm = documentRoot.Q<VisualElement>(ConfirmName) ?? throw HudView.Missing(ConfirmName);
            confirmText = documentRoot.Q<Label>(ConfirmTextName);
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
            this.onDevReturn = onDevReturn;
            resume.clicked += onResume;
            abandon.clicked += onAbandon;
            yes.clicked += onYes;
            no.clicked += onNo;

            // DEV-RETURN: 개발 빌드에서만 버튼을 만든다 (pause-main 맨 끝)
            if (onDevReturn != null)
            {
                devReturn = new Button { name = DevReturnName, text = UiText.PauseDevReturn };
                devReturn.AddToClassList("ash-button");
                devReturn.clicked += onDevReturn;
                main.Add(devReturn);
            }
            SetVisible(false);
        }

        // DEV-RETURN: 테스트용 — 버튼이 만들어졌는지
        public bool HasDevReturnButton => devReturn != null;

        public void SetVisible(bool visible) =>
            screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        // confirmingDevReturn·canDevReturn은 DEV-RETURN (릴리스 빌드에서는 늘 false)
        public void Render(bool confirming, bool canAbandon, bool confirmingDevReturn = false, bool canDevReturn = false)
        {
            bool anyConfirm = confirming || confirmingDevReturn;
            main.style.display = anyConfirm ? DisplayStyle.None : DisplayStyle.Flex;
            confirm.style.display = anyConfirm ? DisplayStyle.Flex : DisplayStyle.None;
            // 확인 씬(레이드 정보 없음)이나 이미 포기를 요청했으면 [계속]만
            abandon.style.display = canAbandon ? DisplayStyle.Flex : DisplayStyle.None;
            if (devReturn != null)
                devReturn.style.display = canDevReturn ? DisplayStyle.Flex : DisplayStyle.None;

            // 확인 단계 문구·[예] 버튼은 무엇을 확인하는지에 따라
            if (confirmText != null)
                confirmText.text = confirmingDevReturn ? UiText.DevReturnConfirm : UiText.AbandonConfirm;
            yes.text = confirmingDevReturn ? UiText.DevReturnYes : UiText.AbandonYes;
            yes.EnableInClassList("ash-button--danger", !confirmingDevReturn);
        }

        public void Dispose()
        {
            resume.clicked -= onResume;
            abandon.clicked -= onAbandon;
            yes.clicked -= onYes;
            no.clicked -= onNo;
            // DEV-RETURN: 코드로 만든 버튼이라 트리에서도 뗀다 (다시 만들 때 버튼이 둘 생기지 않게)
            if (devReturn != null)
            {
                devReturn.clicked -= onDevReturn;
                devReturn.RemoveFromHierarchy();
            }
        }
    }
}
