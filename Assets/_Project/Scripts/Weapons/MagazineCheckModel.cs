using System;
using Ashfall.Core;

namespace Ashfall.Weapons
{
    // RULE-04: HUD에 탄수 없음. T → 0.8초 확인 후 대략치("가득/절반/거의 없음").
    // 결과는 확인이 끝난 시점의 탄 수로 판정한다 (컨트롤러가 Tick이 true일 때 Classify 호출).
    public sealed class MagazineCheckModel
    {
        private readonly WeaponHandlingConfigSO config;
        private float remaining;

        public bool IsChecking { get; private set; }

        public MagazineCheckModel(WeaponHandlingConfigSO config)
        {
            this.config = config ? config : throw new ArgumentNullException(nameof(config));
        }

        // 재장전 중이거나 이미 확인 중이면 false.
        // M7: §5.1 T 길게(고장 해제)는 JamClearModel. 고장 중에는 컨트롤러가 이것을 부르지 않는다(탄창 확인 불가, Q9).
        // 샷건 내장 튜브도 탄창처럼 같은 비율 구간으로 분류한다 (TODO(GDD-?) Q5).
        public bool TryStart(bool isReloading)
        {
            if (isReloading || IsChecking)
                return false;
            IsChecking = true;
            remaining = config.magCheckTimeSec;
            return true;
        }

        // 사격·재장전 입력 시. 결과 없음.
        public void Cancel()
        {
            IsChecking = false;
        }

        // 확인 시간이 지난 Tick에 한 번 true.
        public bool Tick(float dt)
        {
            if (!IsChecking)
                return false;
            remaining -= dt;
            if (remaining > 0f)
                return false;
            IsChecking = false;
            return true;
        }

        // 탄 수/용량 비율로 구간을 고른다. 탄창이 없으면 NoMagazine. 0발은 "거의 없음" (TODO(GDD-?)).
        public static MagazineFillLevel Classify(Magazine magazine, WeaponHandlingConfigSO config)
        {
            if (magazine == null)
                return MagazineFillLevel.NoMagazine;
            float ratio = magazine.Capacity > 0 ? (float)magazine.Rounds / magazine.Capacity : 0f;
            if (ratio >= config.magCheckFullAtOrAbove)
                return MagazineFillLevel.Full;
            if (ratio >= config.magCheckHalfAtOrAbove)
                return MagazineFillLevel.Half;
            return MagazineFillLevel.AlmostEmpty;
        }
    }
}
