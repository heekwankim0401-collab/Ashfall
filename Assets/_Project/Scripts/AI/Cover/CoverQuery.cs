using UnityEngine;

namespace Ashfall.AI
{
    // 엄폐·측면 우회 지점 질의 (M3 설계 §1.7).
    public struct CoverQuery
    {
        public int AgentId;
        public Vector3 Self;
        public Vector3 ThreatEye;      // 위협(표적 눈 또는 LKP + 선 눈 높이)
        public Vector2 PreferredRangeM;
        // 위협까지 수평 거리가 이보다 먼 지점은 뺀다(0 이하 = 제한 없음). 엄폐에서 내다봐도 표적이 시야 밖이면 교전에 끼지 못한다.
        public float MaxThreatDistanceM;
        public bool Flank;             // true면 측면 각 조건 적용
        public Vector3 FlankFrom;      // 우회 전 자기 위치
        // 방금 무효화한 지점을 coverInvalidatedExcludeSec 동안 후보에서 뺀다 (같은 지점 재선택 반복 방지, 리뷰 4)
        public bool HasExcludedCover;
        public int ExcludedCoverId;
    }
}
