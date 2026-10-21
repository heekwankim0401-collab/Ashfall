using Ashfall.Core;

namespace Ashfall.UI
{
    // §12 허용 HUD: 탈출 카운트다운 (M4 설계 §3.1, §4.5). 구역 안에서만 보인다.
    // - 카운트다운 중: "탈출까지 7" (초, 올림 정수 — Raid가 계산해서 보낸다)
    // - 조건 미충족: 짧은 이유 문구 (TODO(GDD-?) Q9: §12 HUD 목록 밖이지만 없으면 EXT-02~04가 왜 안 되는지 알 수 없다)
    // - 비활성 탈출구는 Raid가 InZone=false로 보낸다 → 표시 없음 (TODO(GDD-?) Q4)
    // 레이드 남은 시간은 여기 없다 (TODO(GDD-?) Q10: 지도 화면에서만 분 단위 대략치).
    public sealed class ExtractionHudModel
    {
        private int lastSeconds = int.MinValue;
        private ExtractionBlock lastBlock;

        // null = 표시 안 함
        public string Text { get; private set; }

        // 이벤트는 초 값·상태가 바뀔 때만 오므로(초당 최대 1회) 문자열 생성은 그때만 한다.
        public void Apply(in ExtractionStatusChanged evt)
        {
            if (!evt.InZone)
            {
                Clear();
                return;
            }
            if (Text != null && evt.Block == lastBlock && evt.SecondsLeft == lastSeconds)
                return;
            lastBlock = evt.Block;
            lastSeconds = evt.SecondsLeft;
            Text = TextFor(evt.Block, evt.SecondsLeft);
        }

        public void Clear()
        {
            Text = null;
            lastSeconds = int.MinValue;
            lastBlock = ExtractionBlock.None;
        }

        public static string TextFor(ExtractionBlock block, int secondsLeft)
        {
            switch (block)
            {
                case ExtractionBlock.None: return CountdownText(secondsLeft);
                case ExtractionBlock.NoPower: return "전원이 꺼져 있다";
                case ExtractionBlock.TooEarly: return "아직 헬기가 오지 않는다";
                // TODO(GDD-?) Q6·Q7: 소모 아이템은 지금 EXT-03 신호탄 하나뿐이라 문구를 고정한다.
                case ExtractionBlock.MissingItem: return "신호탄이 필요하다";
                default: return "배낭을 메고는 들어갈 수 없다";
            }
        }

        public static string CountdownText(int secondsLeft) =>
            "탈출까지 " + (secondsLeft < 0 ? 0 : secondsLeft);
    }
}
