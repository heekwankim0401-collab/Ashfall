using System;
using System.Collections.Generic;
using Ashfall.Core;

namespace Ashfall.UI
{
    // 은신처 알림 패널 데이터 (M5 설계 §1.4 "알리는 방식", §2.4, TODO(GDD-?) Q6·Q22·Q23).
    // SaveLoadReport → 문구 줄 + 잃은 장비 묶음("이름 ×n", RaidResultViewModel.Group 규칙).
    // 탄 수·HP 수치는 만들지 않는다. 파일 경로는 손상 파일을 옮겼을 때만 보인다(플레이어가 찾을 수 있게).
    public sealed class HideoutNoticeViewModel
    {
        private HideoutNoticeViewModel(string title, IReadOnlyList<string> lines, IReadOnlyList<string> lost, bool showLost)
        {
            Title = title;
            Lines = lines;
            Lost = lost;
            ShowLost = showLost;
        }

        public string Title { get; }
        public IReadOnlyList<string> Lines { get; }   // 위에서 아래로 한 줄씩
        public IReadOnlyList<string> Lost { get; }    // RULE-03 소실 묶음 (알 수 없는 id 제외). 비면 뷰가 "없음"
        public bool ShowLost { get; }                 // RULE-03 처리일 때만

        // 한 줄로 합친 본문 (뷰의 Label 하나)
        public string Text => string.Join("\n", Lines);

        public static HideoutNoticeViewModel From(SaveLoadReport report)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            var lines = new List<string>();
            if (report.CrashPenaltyApplied)
                lines.Add(UiText.NoticeCrash);
            if (report.Status == SaveLoadStatus.Corrupt)
                lines.Add(UiText.NoticeCorrupt);
            else if (report.Status == SaveLoadStatus.UnsupportedVersion)
                lines.Add(UiText.NoticeUnsupportedVersion);
            else if (report.Status == SaveLoadStatus.ReadError)
                lines.Add(UiText.NoticeReadError);
            if (!string.IsNullOrEmpty(report.QuarantinedPath))
                lines.Add(UiText.NoticeQuarantined + report.QuarantinedPath);
            if (report.UnknownItemIds.Count > 0)
                lines.Add(UiText.NoticeUnknownItems(report.UnknownItemIds.Count));
            if (report.DroppedItemCount > 0)
                lines.Add(UiText.NoticeDroppedItems(report.DroppedItemCount));

            bool showLost = report.CrashPenaltyApplied;
            IReadOnlyList<string> lost = showLost ? RaidResultViewModel.Group(report.CrashLost) : Array.Empty<string>();
            return new HideoutNoticeViewModel(UiText.NoticeTitle, lines, lost, showLost);
        }
    }
}
