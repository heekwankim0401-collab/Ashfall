using System;
using Ashfall.Core;

namespace Ashfall.UI
{
    public enum MainMenuPanel
    {
        Menu,     // [레이드 시작] / [종료]  (TODO(GDD-?) Q22: 설정 없음)
        Result,   // 레이드 결과 (GameFlow.PendingResult) → [확인]
    }

    // 메인 메뉴 화면 상태 (M4 설계 §1.3, §3.4). 순수 C#. MainMenuRoot가 버튼을 여기로 잇는다.
    // - GameFlow.PendingResult가 있으면 결과 패널을 먼저 보인다 (TODO(GDD-?) Q15: 결과는 레이드 씬이 아니라 MainMenu에서).
    // - [확인] → AcknowledgeResult → 메뉴. [레이드 시작] → GameFlow.StartRaid (한 번만). [종료] → quit 대리자.
    // M5 경계: 은신처가 생기면 결과 패널은 Hideout이 보이고, 메뉴의 [레이드 시작]은 은신처 진입으로 바뀐다.
    public sealed class MainMenuPresenter
    {
        private readonly GameFlow flow;
        private readonly Action quit;

        public MainMenuPresenter(GameFlow flow, Action quit)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.quit = quit ?? throw new ArgumentNullException(nameof(quit));
            Refresh();
        }

        public MainMenuPanel Panel { get; private set; }

        // Panel == Result일 때만 값이 있다
        public RaidResultViewModel Result { get; private set; }

        // [레이드 시작]을 눌러 씬을 불러오는 중 (버튼 비활성)
        public bool IsStarting { get; private set; }

        public event Action Changed;

        // GameFlow 상태로 패널을 다시 정한다 (생성 시 자동).
        public void Refresh()
        {
            RaidResult pending = flow.PendingResult;
            if (pending != null)
            {
                Panel = MainMenuPanel.Result;
                Result = RaidResultViewModel.From(pending);
            }
            else
            {
                Panel = MainMenuPanel.Menu;
                Result = null;
            }
            Changed?.Invoke();
        }

        // 결과 패널 [확인]
        public bool ConfirmResult()
        {
            if (Panel != MainMenuPanel.Result)
                return false;
            flow.AcknowledgeResult();
            Refresh();
            return true;
        }

        // [레이드 시작]. 결과 패널이 떠 있거나 이미 시작했으면 무시.
        // GameFlow가 거부하거나 씬 로드 요청이 실패하면 IsStarting을 켜지 않는다 (M4 리뷰 수정: 버튼이 영구 비활성되지 않게).
        // M5 경계: StartRaid(RaidRequest) — 반입 장비 선택은 은신처에서.
        public bool StartRaid()
        {
            if (Panel != MainMenuPanel.Menu || IsStarting)
                return false;
            if (!flow.StartRaid())
                return false;
            IsStarting = true;
            Changed?.Invoke();
            return true;
        }

        // [종료]
        public bool Quit()
        {
            if (Panel != MainMenuPanel.Menu || IsStarting)
                return false;
            quit();
            return true;
        }
    }
}
