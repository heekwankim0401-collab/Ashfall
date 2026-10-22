using System;
using Ashfall.Core;

namespace Ashfall.UI
{
    public enum MainMenuPanel
    {
        Menu,     // [은신처] / [종료]  (TODO(GDD-?) Q20: 설정·새로 시작 없음). M5 Q21: 결과 패널은 은신처(HideoutPresenter)로 옮겼다.
    }

    // 메인 메뉴 화면 상태 (M5 설계 §1.2, §3.4, §4.5). 순수 C#. MainMenuRoot가 버튼을 여기로 잇는다.
    // [은신처] → GameFlow.EnterHideout (한 번만). [종료] → quit 대리자.
    // 레이드 결과는 은신처 진입 때 보인다(Q21) — 메인 메뉴는 프로필·결과를 읽지 않는다.
    public sealed class MainMenuPresenter
    {
        private readonly GameFlow flow;
        private readonly Action quit;

        public MainMenuPresenter(GameFlow flow, Action quit)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.quit = quit ?? throw new ArgumentNullException(nameof(quit));
        }

        // 항상 Menu (M5)
        public MainMenuPanel Panel => MainMenuPanel.Menu;

        // [은신처]를 눌러 씬을 불러오는 중 (버튼 비활성)
        public bool IsEntering { get; private set; }

        public event Action Changed;

        // [은신처]. 이미 요청했으면 무시.
        // GameFlow가 거부하거나 씬 로드 요청이 실패하면 IsEntering을 켜지 않는다 (M4 리뷰 수정: 버튼이 영구 비활성되지 않게).
        public bool EnterHideout()
        {
            if (IsEntering)
                return false;
            if (!flow.EnterHideout())
                return false;
            IsEntering = true;
            Changed?.Invoke();
            return true;
        }

        // [종료]. 은신처를 불러오는 중이면 무시.
        public bool Quit()
        {
            if (IsEntering)
                return false;
            quit();
            return true;
        }
    }
}
