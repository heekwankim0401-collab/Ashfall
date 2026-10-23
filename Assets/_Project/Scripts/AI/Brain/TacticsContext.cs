using UnityEngine;

namespace Ashfall.AI
{
    // EnemyBrain이 들고 있는 것(아키타입, 설정, 분대, 월드, 난수, 자극·LKP, 타이머)을 전술에 넘기는 묶음 (M3 설계 §1.6.2).
    // brain당 1개(할당 1회).
    internal sealed class TacticsContext
    {
        public readonly EnemyBrain Brain;
        public readonly EnemyArchetypeSO Archetype;
        public readonly AiConfigSO Config;
        public readonly IEnemyWorld World;
        public readonly System.Random Random;
        public readonly Squad Squad;

        public float Now;
        public float Dt;

        // Suspicious 자극 위치 (시야·발소리)
        public Vector3 Stimulus;
        // 마지막으로 안 표적 위치 (LKP)
        public bool HasLastKnown;
        public Vector3 LastKnown;
        public float LastKnownUpdatedAt = float.NegativeInfinity;
        public float LastSeenTime = float.NegativeInfinity;
        // 자기가 직접 감지한(봄·들음·피격) 마지막 시각. Alert 진입도 기준점이 된다. T8은 이것만 본다
        // (분대 연락은 LKP만 갱신하고 이 값은 바꾸지 않는다 — 리뷰 1).
        public float LastSensedAt = float.NegativeInfinity;

        // Suspicious에서 조사 도착 후 둘러본 시간 (T11)
        public float SuspiciousLookElapsed;

        private bool hasDestination;
        private Vector3 destination;
        private float destinationSetAt = float.NegativeInfinity;

        // 거점 (M7 Q28): anchorRadiusM > 0이면 첫 Tick 위치(= 스폰 위치)가 중심. 그 전·반경 0이면 자르지 않는다.
        public bool HasAnchor { get; private set; }
        public Vector3 AnchorCenter { get; private set; }
        public float AnchorRadiusM => Archetype.anchorRadiusM;

        // ClampToAnchor 캐시 (직전 입력 → NavMesh에 붙인 결과)
        private bool hasClampCache;
        private Vector3 clampCacheIn;
        private Vector3 clampCacheOut;

        // ApproachPoint 캐시 (직전 표적 → 당겨서 NavMesh에 붙인 점). 같은 LKP면 방향을 처음 잡은 자리에서 고정한다
        // (매 Tick 자기 위치로 다시 당기면 목적지가 조금씩 움직여 MoveTo가 경로를 계속 새로 잡는다).
        private bool hasApproachCache;
        private Vector3 approachCacheIn;
        private Vector3 approachCacheOut;

        public TacticsContext(EnemyBrain brain, EnemyArchetypeSO archetype, AiConfigSO config, IEnemyWorld world,
                              System.Random random, Squad squad)
        {
            Brain = brain;
            Archetype = archetype;
            Config = config;
            World = world;
            Random = random;
            Squad = squad;
        }

        public int AgentId => Brain.AgentId;
        public Vector3 Position => Brain.Position;

        public void SetLastKnown(Vector3 position)
        {
            HasLastKnown = true;
            LastKnown = position;
            LastKnownUpdatedAt = Now;
        }

        // 위협 눈 위치: 보이면 표적 눈, 아니면 LKP + 선 눈 높이
        public Vector3 ThreatEye(in EnemySenses s)
        {
            if (s.TargetExists && s.TargetVisible)
                return s.TargetEyePosition;
            Vector3 basePos = HasLastKnown ? LastKnown : s.SelfPosition + s.SelfForward * Config.lookAheadDistanceM;
            return basePos + Vector3.up * Config.standEyeHeightM;
        }

        // EnemyBrain이 한 번 부른다 (anchorRadiusM > 0일 때만, 중심 = 분대 리더의 거점 또는 자기 첫 Tick 위치 — EnemyBrain.ResolveAnchor).
        public void SetAnchor(Vector3 center)
        {
            HasAnchor = true;
            AnchorCenter = center;
            hasClampCache = false;
        }

        // 이동 목적지를 거점 반경 안으로 자른다. 거점 없음·반경 0·반경 안 = 그대로 (M7 설계 §1.4).
        // 잘린 점은 NavMesh에 붙인다(7B 리뷰 7: 원래 목적지 높이를 그대로 두면 경사·언덕에서 공중·땅속 점이 된다).
        // 샘플이 반경 밖으로 밀리면 그 높이로 다시 수평만 자른다. 샘플 실패 = 잘린 점 그대로.
        // 같은 입력은 직전 결과를 다시 쓴다(MoveTo·Reached가 같은 점을 매 Tick 묻는다 — NavMesh 질의 1회).
        public Vector3 ClampToAnchor(Vector3 point)
        {
            if (!HasAnchor)
                return point;
            Vector3 clamped = AnchorRules.Clamp(point, AnchorCenter, AnchorRadiusM);
            if (clamped == point)
                return point;
            if (hasClampCache && clampCacheIn == point)
                return clampCacheOut;

            Vector3 result = clamped;
            if (World != null && World.TrySampleNavPoint(clamped, Config.navSampleRadiusM, out Vector3 ground))
                result = AnchorRules.Clamp(ground, AnchorCenter, AnchorRadiusM);
            hasClampCache = true;
            clampCacheIn = point;
            clampCacheOut = result;
            return result;
        }

        // 접근 목적지 (7D-3 발견 1): LKP·표적 위치 대신 그 점에서 자기 쪽으로 approachStopDistanceM 앞 점(NavMesh에 붙임).
        // 이미 그 거리 안이면 자기 위치(= Reached 즉시 참 → 제자리). MoveTo·Reached 둘 다 이 점을 쓴다.
        // 샘플 실패 = 당긴 점 그대로. 같은 표적이면 캐시(NavMesh 질의 1회). 0이면 표적 그대로(M3 동작).
        public Vector3 ApproachPoint(in EnemySenses s, Vector3 target)
        {
            float stop = Config.approachStopDistanceM;
            if (!(stop > 0f))
                return target;
            if (ApproachRules.IsWithinStop(s.SelfPosition, target, stop))
                return s.SelfPosition;
            if (hasApproachCache && (approachCacheIn - target).sqrMagnitude <= 1e-6f)
                return approachCacheOut;

            Vector3 pulled = ApproachRules.StopShort(s.SelfPosition, target, stop);
            Vector3 result = pulled;
            if (World != null && World.TrySampleNavPoint(pulled, Config.navSampleRadiusM, out Vector3 ground))
                result = ground;
            hasApproachCache = true;
            approachCacheIn = target;
            approachCacheOut = result;
            return result;
        }

        // 모든 이동은 여기를 지난다 → 목적지는 거점 안으로 잘린다. Reached도 같은 자른 점으로 판정해
        // 반경 밖 지점(순찰·수색·LKP)을 향하면 반경 경계에 닿은 것을 도착으로 본다.
        // 엄폐 선택은 CoverQuery의 거점(HasAnchor·AnchorCenter·AnchorRadiusM)으로 반경 밖 후보를 아예 뺀다(7B 리뷰 2, Q29).
        public void MoveTo(ref EnemyIntent i, Vector3 point, EnemyMoveMode mode)
        {
            point = ClampToAnchor(point);
            if (!hasDestination || (destination - point).sqrMagnitude > 0.01f)
            {
                hasDestination = true;
                destination = point;
                destinationSetAt = Now;
            }
            i.HasDestination = true;
            i.Destination = point;
            i.Move = mode;
        }

        public static void Hold(ref EnemyIntent i)
        {
            i.Move = EnemyMoveMode.Hold;
            i.HasDestination = false;
        }

        public static void LookAt(ref EnemyIntent i, Vector3 point)
        {
            i.LookAtPoint = true;
            i.LookPoint = point;
        }

        // focus 쪽을 기준으로 좌우 yawDeg를 period 주기로 훑는다(elapsed = 상태 경과 시간). 수색 둘러보기·저격수 훑기 공용.
        // focus가 자기 위치와 겹치면 fallbackForward 기준.
        public static void LookAround(ref EnemyIntent i, in EnemySenses s, Vector3 focus, Vector3 fallbackForward,
                                      float yawDeg, float period, float elapsed, float lookAheadM)
        {
            Vector3 dir = focus - s.SelfPosition;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f)
                dir = fallbackForward;
            if (period <= 0f)
                period = 1f;
            float yaw = yawDeg * Mathf.Sin((elapsed / period) * Mathf.PI);
            LookAt(ref i, s.SelfPosition + Quaternion.Euler(0f, yaw, 0f) * dir.normalized * lookAheadM);
        }

        // 수평 거리 ≤ arriveDistance, 또는 NavMeshAgent가 이 목적지에 대해 도착(경로 끝)이라고 한 지 충분히 지남.
        public bool Reached(in EnemySenses s, Vector3 point, float arriveDistance)
        {
            point = ClampToAnchor(point);
            Vector3 d = point - s.SelfPosition;
            d.y = 0f;
            if (d.sqrMagnitude <= arriveDistance * arriveDistance)
                return true;
            return s.Arrived && hasDestination && (destination - point).sqrMagnitude <= 0.01f
                   && Now - destinationSetAt >= Config.repathMinIntervalSec;
        }

        public bool Reached(in EnemySenses s, Vector3 point) => Reached(s, point, Config.pointArriveDistanceM);

        public float RandomRange(Vector2 range)
        {
            float min = Mathf.Max(0f, range.x);
            float max = Mathf.Max(min, range.y);
            return min + (float)Random.NextDouble() * (max - min);
        }

        public void ClearDestination()
        {
            hasDestination = false;
        }
    }
}
