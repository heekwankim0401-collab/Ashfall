using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // 레이드 종료 오버레이 (ScreenKind.RaidEnd, M4 설계 §1.3·§3.4). 한 줄 문구만. 닫기 버튼 없음 —
    // Raid가 endDelaySec(실시간) 뒤 GameFlow.CompleteRaid로 MainMenu를 불러온다.
    public sealed class RaidEndOverlayView
    {
        public const string ScreenName = "raid-end-screen";
        public const string TextName = "raid-end-text";

        private readonly VisualElement screen;
        private readonly Label text;

        public RaidEndOverlayView(VisualElement documentRoot)
        {
            screen = documentRoot.Q<VisualElement>(ScreenName) ?? throw HudView.Missing(ScreenName);
            text = documentRoot.Q<Label>(TextName) ?? throw HudView.Missing(TextName);
            SetVisible(false);
        }

        public void SetVisible(bool visible) =>
            screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void Render(string value) => text.text = value ?? string.Empty;
    }
}
