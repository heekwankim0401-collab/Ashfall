using UnityEngine;

namespace Ashfall.AI
{
    // AI 전역 수치 (SYS-AI-*). §14에 없는 SO (M3 설계 §4.2). 에셋: Data/AI/AiConfig.asset.
    // 필드 초기값 = 설계값 (CreateInstance만 해도 설계값, M2 관례). §7·§4.2 출처가 없는 값은 TODO(GDD-?) 임시값.
    [CreateAssetMenu(menuName = "Ashfall/AI Config", fileName = "AiConfig")]
    public class AiConfigSO : ScriptableObject
    {
        [Header("§7 자세별 시야 배율")]
        public float crouchSightMultiplier = 0.6f;
        public float proneSightMultiplier = 0.3f;

        [Header("인지 게이지 (TODO(GDD-?))")]
        [Tooltip("이 값 이상이면 Suspicious")]
        public float suspiciousAt = 0.3f;
        [Tooltip("가까이(거리 0)에서 보일 때 초당 증가량")]
        public float sightGainNearPerSec = 2.5f;
        [Tooltip("유효 시야 끝에서 보일 때 초당 증가량")]
        public float sightGainFarPerSec = 0.35f;
        [Tooltip("이 거리 안에서 보이면 즉시 1 (m)")]
        public float instantDetectRangeM = 4f;
        [Tooltip("자극이 없을 때 초당 감소량")]
        public float awarenessDecayPerSec = 0.15f;
        [Tooltip("발소리를 들었을 때 더하는 값")]
        public float footstepAwarenessGain = 0.25f;
        [Tooltip("점프 소리를 들었을 때 더하는 값")]
        public float jumpAwarenessGain = 0.4f;

        [Header("상태 타이머 (초, TODO(GDD-?))")]
        public float idleBeforePatrolSec = 2f;
        [Tooltip("순찰 웨이포인트 대기 최소~최대")]
        public Vector2 patrolWaitSec = new Vector2(2f, 4f);
        public float suspiciousLookSec = 5f;
        public float alertTimeoutSec = 12f;
        public float loseSightSec = 5f;
        public float searchDurationSec = 25f;
        [Tooltip("수색 반경 (m)")]
        public float searchRadiusM = 12f;
        public int searchPointCount = 3;
        public float searchLookSec = 2f;

        [Header("§4.2 피해 공식 (3부위 축약)")]
        [Tooltip("파괴된 부위가 받은 피해 중 몸통으로 가는 비율")]
        public float destroyedPartRedistribution = 0.7f;
        [Tooltip("다리 파괴 시 이동 속도 배율")]
        public float legsDestroyedSpeedMultiplier = 0.6f;

        [Header("부위 판정 (캡슐 높이 비율, TODO(GDD-?))")]
        [Tooltip("이 비율 이상 = 머리")]
        public float headFrom = 0.85f;
        [Tooltip("이 비율 이상 = 몸통, 그 아래 = 다리")]
        public float bodyFrom = 0.45f;

        [Header("몸 (TODO(GDD-?))")]
        public float standHeightM = 1.8f;
        public float crouchHeightM = 1.2f;
        public float standEyeHeightM = 1.6f;
        public float crouchEyeHeightM = 1.0f;
        public float turnSpeedDegPerSec = 360f;
        [Tooltip("몸 방향과 표적 방향의 수평 각 차가 이 값 이하일 때만 사격 (도)")]
        public float fireMaxYawErrorDeg = 10f;

        [Header("분대 (TODO(GDD-?))")]
        [Tooltip("분대원에게 접촉을 알리는 최대 거리 (m)")]
        public float squadShareRangeM = 50f;
        public float squadReportMinIntervalSec = 0.5f;
        public float formationSpacingM = 2f;
        public float formationUpdateSec = 0.5f;
        public float flankCooldownSec = 8f;

        [Header("엄폐 (TODO(GDD-?))")]
        public float coverSearchRadiusM = 25f;
        public float coverProtectionHalfAngleDeg = 70f;
        public float coverDistanceWeight = 1f;
        public float coverRangeWeight = 0.5f;
        public float coverAdvancePenalty = 5f;
        [Tooltip("점수 상위 몇 개까지 레이로 확인할지")]
        public int coverLosCandidates = 3;
        public float coverArriveDistanceM = 0.6f;

        [Header("측면 우회 (TODO(GDD-?))")]
        public float flankAfterNoSightSec = 6f;
        public float flankMinAngleDeg = 45f;
        public float flankMaxAngleDeg = 120f;
        public float flankSearchRadiusM = 30f;

        [Header("성능 예산 (TODO(GDD-?))")]
        public float relaxedSensePeriodSec = 0.2f;
        public float engagedSensePeriodSec = 0.1f;
        public int maxSenseUpdatesPerFrame = 8;
        public int maxPathRequestsPerFrame = 3;
        public float repathMinIntervalSec = 0.5f;
        public float repathDistanceM = 1.0f;
        public int maxCoverQueriesPerFrame = 1;
        [Tooltip("IPlayerTarget·IProjectileService가 없을 때 다시 찾는 간격 (초)")]
        public float targetResolveRetrySec = 1f;

        [Header("2b 구현 추가 (TODO(GDD-?))")]
        [Tooltip("순찰 웨이포인트·조사·수색 지점에 도착했다고 보는 수평 거리 (m)")]
        public float pointArriveDistanceM = 1.0f;
        [Tooltip("이 속도(m/s)보다 빠르면 '이동 중'으로 보고 명중 오차 배율을 쓴다 (설계 §1.4 0.5m/s)")]
        public float movingSpeedThresholdMps = 0.5f;
        [Tooltip("약탈자 교전 중 옆으로 움직이는 거리 최소~최대 (m, 설계 §1.6.2 2~4m)")]
        public Vector2 strafeDistanceM = new Vector2(2f, 4f);
        [Tooltip("수색·옆걸음 지점을 NavMesh에 붙일 때 찾는 반경 (m)")]
        public float navSampleRadiusM = 3f;
        [Tooltip("분대원이 대형 자리에서 이 거리보다 멀면 달려서 따라간다 (m)")]
        public float formationCatchUpDistanceM = 6f;
        [Tooltip("수색 중 둘러볼 때 기준 방향에서 좌우로 도는 최대 각 (도)")]
        public float searchLookYawDeg = 90f;
        [Tooltip("엄폐를 못 찾았을 때 다시 찾기까지 기다리는 시간 (초)")]
        public float coverRetrySec = 1f;

        [Header("M3 리뷰 수정 추가 (TODO(GDD-?))")]
        [Tooltip("방향만 정할 때(대형 정렬·둘러보기·LKP 없는 위협) 바라볼 점까지 거리 (m)")]
        public float lookAheadDistanceM = 10f;
        [Tooltip("숨은 자리가 무효화된 엄폐 지점을 다시 고르지 않는 시간 (초)")]
        public float coverInvalidatedExcludeSec = 5f;

        [Header("디버그")]
        [Tooltip("머리 위 상태 색 표시 (그레이박스 확인용, M6에서 끔)")]
        public bool showStateIndicator = true;
    }
}
