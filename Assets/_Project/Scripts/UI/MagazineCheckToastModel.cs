using System;
using Ashfall.Core;

namespace Ashfall.UI
{
    // RULE-04 탄창 확인 결과 문구. 탄 수는 절대 표시하지 않고 대략치 문구만 잠깐 보인다.
    // 확인 동작(0.8초)은 Weapons가 하고, 여기서는 결과 문구와 표시 시간만 다룬다.
    public sealed class MagazineCheckToastModel
    {
        private readonly HudConfigSO config;
        private float remainingSec;

        public MagazineCheckToastModel(HudConfigSO config)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
        }

        // null = 표시 안 함
        public string Text { get; private set; }

        // 확인 시작: 이전 결과를 지운다. 확인 중 별도 문구는 없다(애니메이션만).
        public void OnStarted() => Clear();

        public void OnCompleted(MagazineFillLevel level)
        {
            remainingSec = config.magCheckResultSec;
            Text = remainingSec > 0f ? TextFor(level) : null;
        }

        public void Tick(float dt)
        {
            if (Text == null)
                return;
            remainingSec -= dt;
            if (remainingSec <= 0f)
                Clear();
        }

        public void Clear()
        {
            Text = null;
            remainingSec = 0f;
        }

        // RULE-04 "가득/절반/거의 없음". 탄창이 없으면 "탄창 없음"(설계 §5.4).
        public static string TextFor(MagazineFillLevel level)
        {
            switch (level)
            {
                case MagazineFillLevel.Full: return "가득";
                case MagazineFillLevel.Half: return "절반";
                case MagazineFillLevel.AlmostEmpty: return "거의 없음";
                default: return "탄창 없음";
            }
        }
    }
}
