using System;

namespace Ashfall.Weapons
{
    // §5.1 고장 해제: T를 길게 누른다. 순수 로직 (M7 설계 §1.3).
    // TODO(GDD-?) Q9: 길이는 WeaponHandlingConfigSO.jamClearHoldSec(1.2초). 계속 눌러야 하고, 떼면 처음부터.
    // 고장이 아닐 때의 T(짧게)는 RULE-04 탄창 확인 — 컨트롤러가 고른다.
    public sealed class JamClearModel
    {
        private float held;

        public float HoldSec { get; }
        // 0~1 (테스트·디버그용, 화면 표시 없음 — Q10)
        public float Progress => HoldSec > 0f ? Math.Min(1f, held / HoldSec) : 0f;
        public bool IsHolding => held > 0f;

        public JamClearModel(float holdSec)
        {
            HoldSec = Math.Max(0f, holdSec);
        }

        // 고장 중 T를 누르고 있으면 시간을 쌓는다. 해제가 끝난 Tick에 한 번 true(그리고 처음으로).
        // 떼거나 고장이 아니면 처음부터.
        public bool Tick(float dt, bool held, bool jammed)
        {
            if (!held || !jammed)
            {
                this.held = 0f;
                return false;
            }
            this.held += Math.Max(0f, dt);
            if (this.held < HoldSec)
                return false;
            this.held = 0f;
            return true;
        }

        // 교체·사망·화면 열림 등으로 진행을 버린다.
        public void Reset()
        {
            held = 0f;
        }
    }
}
