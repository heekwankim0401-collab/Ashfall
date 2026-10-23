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

        // M6 설계 §3.6, Q12·Q13: 적 발소리(FootstepTaken) 리듬. NavMeshAgent 속도로 걸음 종류를 고른다.
        [Header("M6 발소리 (TODO(GDD-?))")]
        [Tooltip("걷기 보폭 (m)")]
        public float footstepStrideWalkM = 1.5f;
        [Tooltip("달리기(Sprint 걸음) 보폭 (m)")]
        public float footstepStrideRunM = 2.2f;
        [Tooltip("이 속도(m/s)보다 빠르면 Sprint 걸음")]
        public float footstepRunSpeedMps = 3.5f;
        [Tooltip("이 속도(m/s)보다 느리면 걸음 없음")]
        public float footstepMinSpeedMps = 0.3f;

        // M7 설계 §3.4 (Q23·Q26). TODO(GDD-?) 임시값.
        [Header("M7 저격 섬광·울음 (TODO(GDD-?))")]
        [Tooltip("저격 섬광 쿼드의 보이는 각 크기 (도). 크기 = 거리 × tan(이 값)")]
        public float glintAngularSizeDeg = 0.35f;
        [Tooltip("같은 적의 울음(EnemyVocalized) 최소 간격 (초)")]
        public float vocalCooldownSec = 4f;

        // M7 7B 리뷰 수정 (1·3·5·15). 기존 에셋에 필드가 없으면 아래 초기값으로 읽힌다. 전부 TODO(GDD-?) 임시값.
        [Header("M7 변이체 근접 (TODO(GDD-?) Q24·Q25)")]
        [Tooltip("근접 타격 부위 가중치: 흉부 (머리·다리는 맞히지 않는다 — Q24 '흉부·복부·팔')")]
        public float meleePartWeightThorax = 1f;
        [Tooltip("근접 타격 부위 가중치: 복부")]
        public float meleePartWeightStomach = 1f;
        [Tooltip("근접 타격 부위 가중치: 왼팔")]
        public float meleePartWeightLeftArm = 0.5f;
        [Tooltip("근접 타격 부위 가중치: 오른팔")]
        public float meleePartWeightRightArm = 0.5f;
        [Tooltip("근접 사거리 판정의 높이 차 상한 (발 기준 |Δy|, m). 지붕 위·아래 표적은 못 친다")]
        public float meleeMaxHeightDiffM = 1.2f;
        [Tooltip("표적에 가까울 때(수평 ≤ 둘레 반경 + 사거리) 둘레 자리 반경 = 사거리 × 이 값")]
        public float mutantCloseRadiusRatio = 0.5f;
        [Tooltip("표적(또는 LKP)과 수평 거리 ≤ 사거리 × 이 값이면 멈춘다 (밀고 들어가 몸이 겹치지 않게)")]
        public float mutantHoldInsideRangeRatio = 0.75f;

        // 7D-3 발견 1: 약탈자·습격자(같은 경로를 쓰는 보스·호위 포함)가 LKP = 표적 발까지 걸어 들어와 몸이 겹치던 문제.
        [Header("M7 7D 접근 정지 (TODO(GDD-?))")]
        [Tooltip("약탈자·습격자가 LKP·표적 위치로 다가갈 때 그 점에서 자기 쪽으로 이 수평 거리(m) 앞에서 멈춘다. 0 = 끝까지 (변이체는 둘레 규칙을 따로 쓴다)")]
        public float approachStopDistanceM = 2.0f;

        [Header("디버그")]
        [Tooltip("머리 위 상태 색 표시 (그레이박스 확인용). M6 Q8에서 끔 — 기본값도 false(새로 만든 AiConfig·테스트 인스턴스)")]
        public bool showStateIndicator = false;
    }
}
