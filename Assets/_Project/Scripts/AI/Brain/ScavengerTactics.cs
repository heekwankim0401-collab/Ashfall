using UnityEngine;

namespace Ashfall.AI
{
    // AI-SCV 약탈자 전술 (§7 "2~3인 순찰, 소리 추적", M3 설계 §1.6.2). RDR이 엄폐를 못 찾을 때도 이걸 쓴다.
    // - Suspicious: investigatesNoise면 자극 위치로 걷고 도착 후 둘러봄, 아니면 제자리에서 자극 쪽을 봄
    // - Alert(안 보임): LKP로 달린다(LKP에서 approachStopDistanceM 앞에서 멈춤 — 7D-3 발견 1). 보이면 멈춰서 표적을 본다(반응 시간)
    // - Combat: 선호 거리 밖이면 표적 쪽으로 달림(사격 안 함), 안이면 정지 사격 + strafeIntervalSec마다 옆으로 걸으며 사격
    //   (M7 거점 유형은 선호 거리 밖이어도 다가가지 않고 제자리 사격 — 7B 리뷰 2)
    internal sealed class ScavengerTactics : IEnemyTactics
    {
        private float nextStrafeAt = -1f;
        private bool strafing;
        private Vector3 strafePoint;

        public bool DefersLoseSight => false;
        public bool WantsHiddenCheck => false;

        // 평시·수색은 EnemyBrain 기본 행동 (M7 훅, IEnemyTactics.TryTickRelaxed)
        public bool TryTickRelaxed(EnemyState state, TacticsContext c, in EnemySenses s, ref EnemyIntent i) => false;

        public void Reset()
        {
            nextStrafeAt = -1f;
            strafing = false;
        }

        public void TickSuspicious(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            if (c.Archetype.investigatesNoise && !c.Reached(s, c.Stimulus))
            {
                c.MoveTo(ref i, c.Stimulus, EnemyMoveMode.Walk);
                c.SuspiciousLookElapsed = 0f;
                return;
            }
            TacticsContext.Hold(ref i);
            TacticsContext.LookAt(ref i, c.Stimulus);
            c.SuspiciousLookElapsed += c.Dt;
        }

        public void TickAlert(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            if (s.TargetExists && s.TargetVisible)
            {
                // 반응 시간 중에도 몸은 표적 쪽으로 돈다
                TacticsContext.Hold(ref i);
                TacticsContext.LookAt(ref i, s.TargetCenter);
                return;
            }
            ChaseLastKnown(c, s, ref i);
        }

        public void TickCombat(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            if (!(s.TargetExists && s.TargetVisible))
            {
                strafing = false;
                ChaseLastKnown(c, s, ref i);
                return;
            }

            TacticsContext.LookAt(ref i, s.TargetCenter);
            i.FireAt = s.TargetCenter;

            if (s.TargetDistance > c.Archetype.preferredRangeM.y && c.HasAnchor)
            {
                // 거점 유형(보스·호위가 엄폐를 못 찾았을 때, 7B 리뷰 2): 거리를 좁히러 나가지 않는다(거점 경계에서 멈춰 쏘지 않던 문제).
                // 제자리에서 보이고 사거리 안이면 쏜다.
                strafing = false;
                TacticsContext.Hold(ref i);
                i.Fire = s.TargetDistance <= c.Archetype.engageMaxRangeM;
                return;
            }

            if (s.TargetDistance > c.Archetype.preferredRangeM.y)
            {
                // 멀면 달려서 좁힌다. 달리는 중에는 쏘지 않는다. 목적지는 표적에서 approachStopDistanceM 앞(7D-3 발견 1).
                strafing = false;
                c.MoveTo(ref i, c.ApproachPoint(s, s.TargetPosition), EnemyMoveMode.Run);
                i.Fire = false;
                return;
            }

            UpdateStrafe(c, s, ref i);
            i.Fire = s.TargetDistance <= c.Archetype.engageMaxRangeM;
        }

        public void ReleaseReservations(TacticsContext c)
        {
            strafing = false;
        }

        private void UpdateStrafe(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            Vector2 interval = c.Archetype.strafeIntervalSec;
            if (interval.y <= 0f)
            {
                TacticsContext.Hold(ref i);
                return;
            }

            if (nextStrafeAt < 0f)
                nextStrafeAt = c.Now + c.RandomRange(interval);

            if (strafing)
            {
                if (c.Reached(s, strafePoint))
                    strafing = false;
                else
                {
                    c.MoveTo(ref i, strafePoint, EnemyMoveMode.Walk);
                    return;
                }
            }

            if (c.Now >= nextStrafeAt)
            {
                nextStrafeAt = c.Now + c.RandomRange(interval);
                Vector3 toTarget = s.TargetPosition - s.SelfPosition;
                toTarget.y = 0f;
                Vector3 right = toTarget.sqrMagnitude > 1e-6f
                    ? new Vector3(toTarget.z, 0f, -toTarget.x).normalized
                    : new Vector3(s.SelfForward.z, 0f, -s.SelfForward.x).normalized;
                float side = c.Random.NextDouble() < 0.5 ? -1f : 1f;
                Vector3 candidate = s.SelfPosition + right * (side * c.RandomRange(c.Config.strafeDistanceM));
                if (c.World.TrySampleNavPoint(candidate, c.Config.navSampleRadiusM, out Vector3 point))
                {
                    strafing = true;
                    strafePoint = point;
                    c.MoveTo(ref i, strafePoint, EnemyMoveMode.Walk);
                    return;
                }
            }
            TacticsContext.Hold(ref i);
        }

        // "소리 추적": LKP로 달려가고, 도착하면 그 자리에서 LKP 쪽을 본다.
        // 목적지·도착 = LKP에서 approachStopDistanceM 앞 점(7D-3 발견 1: 표적 발까지 걸어 들어가 몸이 겹치지 않게).
        internal static void ChaseLastKnown(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            if (c.HasLastKnown)
            {
                Vector3 approach = c.ApproachPoint(s, c.LastKnown);
                if (!c.Reached(s, approach))
                {
                    c.MoveTo(ref i, approach, EnemyMoveMode.Run);
                    return;
                }
            }
            TacticsContext.Hold(ref i);
            if (c.HasLastKnown)
                TacticsContext.LookAt(ref i, c.LastKnown);
        }
    }
}
