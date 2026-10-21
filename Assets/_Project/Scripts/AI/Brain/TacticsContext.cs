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

        public void MoveTo(ref EnemyIntent i, Vector3 point, EnemyMoveMode mode)
        {
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

        // 수평 거리 ≤ arriveDistance, 또는 NavMeshAgent가 이 목적지에 대해 도착(경로 끝)이라고 한 지 충분히 지남.
        public bool Reached(in EnemySenses s, Vector3 point, float arriveDistance)
        {
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
