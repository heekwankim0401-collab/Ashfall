using System;
using Ashfall.Core;

namespace Ashfall.UI
{
    // 전체 화면(인벤토리·부위 상태·지도·일시정지·레이드 종료) 열림 상태 머신. 한 번에 하나만 열린다.
    // - Tab/H/M: 같은 화면이면 닫고, 다른 화면이 열려 있으면 그걸 닫고 연다.
    // - Esc: 모두 닫는다. 화면이 없을 때 일시정지를 여는 것은 GameUIPresenter가 OpenPause로 한다.
    // - 사망 후에는 열 수 없고, 열려 있던 화면은 닫힌다.
    // - M4 일시정지(Pause): 열려 있는 동안 다른 화면은 열리지 않는다(Tab·H·M 무시). Esc/계속으로만 닫힌다.
    // - M4 레이드 종료(RaidEnd): 사망 후에도 열리고, 한 번 열리면 닫히지 않는다(IsDead·CloseAll 무시). 씬이 바뀔 때까지 유지.
    public sealed class ScreenStateModel
    {
        private bool isDead;
        private bool raidEnded;

        public ScreenKind Current { get; private set; } = ScreenKind.None;

        // 화면을 열 수 있는지 (서비스가 있는지 등). null이면 항상 열 수 있다.
        public Func<ScreenKind, bool> CanOpen { get; set; }

        // Current가 바뀔 때마다 새 값으로 호출된다.
        public event Action<ScreenKind> Changed;

        public bool IsDead
        {
            get => isDead;
            set
            {
                isDead = value;
                if (value && !raidEnded)
                    SetCurrent(ScreenKind.None);
            }
        }

        public bool IsRaidEnded => raidEnded;

        // §12 HUD 일시 표시(탄창 확인 문구, F 프롬프트, 탈출 카운트다운)는 화면이 모두 닫혀 있고 살아 있을 때만.
        public bool HudVisible => Current == ScreenKind.None && !isDead && !raidEnded;

        // 바뀌면 true
        public bool ToggleInventory() => Toggle(ScreenKind.Inventory);

        public bool ToggleBodyStatus() => Toggle(ScreenKind.BodyStatus);

        // RULE-06 M. 지도 아이템·레이드 정보 확인은 CanOpen이 한다.
        public bool ToggleMap() => Toggle(ScreenKind.Map);

        // 루트 컨테이너가 열릴 때. 이미 인벤토리면 false.
        public bool OpenInventory() => Current != ScreenKind.Inventory && TryOpen(ScreenKind.Inventory);

        // Esc (화면이 모두 닫혀 있을 때만). TODO(GDD-?) Q16
        public bool OpenPause() => Current == ScreenKind.None && TryOpen(ScreenKind.Pause);

        // Esc. 레이드 종료 오버레이는 닫히지 않는다.
        public bool CloseAll() => !raidEnded && SetCurrent(ScreenKind.None);

        // 레이드 종료 (RaidEnded). 사망 후에도 열린다. 이후 닫히지 않는다.
        public bool ShowRaidEnd()
        {
            raidEnded = true;
            return SetCurrent(ScreenKind.RaidEnd);
        }

        // UI가 꺼질 때(씬 언로드·비활성) 조작 차단을 풀기 위해 종료 오버레이까지 포함해 모두 닫는다.
        public bool Reset()
        {
            raidEnded = false;
            return SetCurrent(ScreenKind.None);
        }

        private bool Toggle(ScreenKind kind) =>
            Current == kind ? CloseAll() : TryOpen(kind);

        private bool TryOpen(ScreenKind kind)
        {
            if (isDead || raidEnded || Current == ScreenKind.Pause)
                return false;
            if (CanOpen != null && !CanOpen(kind))
                return false;
            return SetCurrent(kind);
        }

        private bool SetCurrent(ScreenKind kind)
        {
            if (Current == kind)
                return false;
            Current = kind;
            Changed?.Invoke(kind);
            return true;
        }
    }
}
