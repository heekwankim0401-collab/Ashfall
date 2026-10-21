using System;
using Ashfall.Core;

namespace Ashfall.UI
{
    // 전체 화면(인벤토리·부위 상태) 열림 상태 머신. 한 번에 하나만 열린다.
    // - Tab/H: 같은 화면이면 닫고, 다른 화면이 열려 있으면 그걸 닫고 연다.
    // - Esc: 모두 닫는다.
    // - 사망 후에는 열 수 없고, 열려 있던 화면은 닫힌다.
    public sealed class ScreenStateModel
    {
        private bool isDead;

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
                if (value)
                    SetCurrent(ScreenKind.None);
            }
        }

        // §12 HUD 일시 표시(탄창 확인 문구, F 프롬프트)는 화면이 모두 닫혀 있고 살아 있을 때만.
        public bool HudVisible => Current == ScreenKind.None && !isDead;

        // 바뀌면 true
        public bool ToggleInventory() => Toggle(ScreenKind.Inventory);

        public bool ToggleBodyStatus() => Toggle(ScreenKind.BodyStatus);

        // 루트 컨테이너가 열릴 때. 이미 인벤토리면 false.
        public bool OpenInventory() => Current != ScreenKind.Inventory && TryOpen(ScreenKind.Inventory);

        // Esc
        public bool CloseAll() => SetCurrent(ScreenKind.None);

        private bool Toggle(ScreenKind kind) =>
            Current == kind ? SetCurrent(ScreenKind.None) : TryOpen(kind);

        private bool TryOpen(ScreenKind kind)
        {
            if (isDead)
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
