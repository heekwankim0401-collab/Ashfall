using System.Collections.Generic;

namespace Ashfall.Core
{
    public enum SaveLoadStatus
    {
        NotLoaded,
        NewProfile,            // 파일 없음 (첫 실행)
        Loaded,
        Corrupt,               // 파싱 실패·빈 파일·version 없음·해석 중 예외 → save.corrupt.json으로 옮기고 새 프로필 (Q22)
        UnsupportedVersion,    // version > 현재 → 같은 처리, 문구만 다름
        ReadError,             // 재시도 뒤에도 읽기 예외(권한·잠금) → 파일을 건드리지 않고 이 실행은 읽기 전용(저장 막음) (M5 리뷰 1)
    }

    // 프로필 읽기 결과·알림 내용 (M5 설계 §2.4, §1.4). 은신처 알림 패널이 그린다.
    public sealed class SaveLoadReport
    {
        public SaveLoadStatus Status { get; set; }
        // RULE-03: 지난 레이드 도중 종료(다음 실행) 또는 같은 실행에서 결과 없이 돌아옴 → 은신처 장비 소실
        public bool CrashPenaltyApplied { get; set; }
        public List<ItemDefinitionSO> CrashLost { get; } = new List<ItemDefinitionSO>();   // 표시용 (알 수 없는 id 제외)
        public List<string> UnknownItemIds { get; } = new List<string>();                  // Q23 버린 레코드·탄창 id
        public int DroppedItemCount { get; set; }                                          // 자리 문제로 버린 수
        public string QuarantinedPath { get; set; }                                        // 손상 파일을 옮긴 곳
        public string Error { get; set; }                                                  // 파싱·읽기·격리 오류 (로그용)

        // Corrupt·UnsupportedVersion·ReadError·CrashPenalty·Unknown·Dropped 중 하나라도
        public bool HasNotice =>
            Status == SaveLoadStatus.Corrupt || Status == SaveLoadStatus.UnsupportedVersion ||
            Status == SaveLoadStatus.ReadError ||
            CrashPenaltyApplied || UnknownItemIds.Count > 0 || DroppedItemCount > 0;
    }
}
