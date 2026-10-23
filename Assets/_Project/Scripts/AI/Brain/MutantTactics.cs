using UnityEngine;

namespace Ashfall.AI
{
    // AI-MUT 변이체 전술 (§7 "무리 행동, 근접", M7 설계 §1.4, Q24·Q25). 무장 없음, 엄폐·측면 우회 없음.
    // - Idle·Patrol·Search: EnemyBrain 기본 행동(분대 대형 순찰 = 무리 배회, 걷기 = patrolSpeed로 느리게)
    // - Suspicious: investigatesNoise면 소리(자극) 위치로 걷고 도착 후 둘러봄, 아니면 제자리에서 그쪽을 봄
    // - Alert·Combat(보임): 질주로 표적 둘레 자기 자리(MutantSurround) → 가까우면 표적 바로 옆 자리, 사거리 안쪽이면 멈춤.
    //   항상 표적 중심을 본다(EnemyAgent는 Fire일 때만 표적 쪽으로 돌기 때문, 시야 콘 140°). 사격 없음.
    // - Alert·Combat(안 보임): LKP를 표적 대신 쓴 같은 둘레 자리로 질주하고, LKP와 사거리 × mutantHoldInsideRangeRatio 안쪽이면 멈춰
    //   LKP 쪽을 본다(7B 리뷰 15: LKP = 표적 발로 곧장 달려 몸이 겹치던 문제 — 같은 무리에 시야가 가려진 Tick에 생겼다).
    // - EnemyIntent.Melee는 Alert·Combat 내내 true. 실제 타격은 EnemyAgent(사거리·시야) + EnemyMelee(준비·간격)가 정한다.
    //   EnemyAgent도 표적 바로 앞이면 매 프레임 멈춘다(감각 주기 사이 밀고 들어감 방지).
    // 비율(가까울 때 자리 반경·멈춤 거리)은 AiConfigSO(mutantCloseRadiusRatio·mutantHoldInsideRangeRatio, TODO(GDD-?) Q25).
    // Tick 할당 없음.
    internal sealed class MutantTactics : IEnemyTactics
    {
        public bool DefersLoseSight => false;
        public bool WantsHiddenCheck => false;

        public void Reset()
        {
        }

        // 평시·수색은 EnemyBrain 기본 행동 (대형 순찰 = 무리 배회)
        public bool TryTickRelaxed(EnemyState state, TacticsContext c, in EnemySenses s, ref EnemyIntent i) => false;

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

        public void TickAlert(TacticsContext c, in EnemySenses s, ref EnemyIntent i) => Engage(c, s, ref i);

        public void TickCombat(TacticsContext c, in EnemySenses s, ref EnemyIntent i) => Engage(c, s, ref i);

        public void ReleaseReservations(TacticsContext c)
        {
        }

        private static void Engage(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            i.Melee = true;
            i.Fire = false;

            bool visible = s.TargetExists && s.TargetVisible;
            if (!visible && !c.HasLastKnown)
            {
                TacticsContext.Hold(ref i);
                return;
            }

            // 보이면 표적 발, 안 보이면 LKP(표적 발 기준)를 중심으로 같은 둘레 규칙을 쓴다
            Vector3 focus = visible ? s.TargetPosition : c.LastKnown;
            TacticsContext.LookAt(ref i, visible ? s.TargetCenter : c.LastKnown + Vector3.up * c.Config.standEyeHeightM);

            float meleeRange = c.Archetype.meleeRangeM;
            float distance = MutantSurround.HorizontalDistance(s.SelfPosition, focus);
            if (distance <= meleeRange * c.Config.mutantHoldInsideRangeRatio)
            {
                // 이미 사거리 안쪽 깊이 붙었다 → 밀고 들어가지 않고 제자리에서 친다(안 보이면 LKP 쪽을 보며 기다림)
                TacticsContext.Hold(ref i);
                return;
            }

            int index = -1, count = 1;
            Vector3 basePosition = s.SelfPosition;
            Squad squad = c.Squad;
            if (squad != null)
            {
                index = squad.IndexOf(c.AgentId);
                count = squad.Count;
                EnemyBrain leader = squad.Leader;
                if (leader != null && leader.HasTicked)
                    basePosition = leader.Position;
            }

            Vector3 point = MutantSurround.ApproachPoint(focus, s.SelfPosition, basePosition, index, count,
                c.Archetype.surroundRadiusM, meleeRange, c.Config.mutantCloseRadiusRatio);
            if (!c.World.TrySampleNavPoint(point, c.Config.navSampleRadiusM, out Vector3 sampled))
                sampled = focus;
            c.MoveTo(ref i, sampled, EnemyMoveMode.Sprint);
        }
    }

    // 변이체 무리 둘레 접근 자리 (M7 설계 §1.4, Q25). 순수 계산, 할당 없음.
    // 기준 방향 = 표적 → 분대 리더(리더 자리 = 순번 0이 리더 쪽 정면). 순번 k의 각도 = 360° / 분대 수 × k.
    // 멀면(수평 거리 > surroundRadiusM + meleeRangeM) 반경 surroundRadiusM, 가까우면 반경 meleeRangeM × closeRadiusRatio로 좁힌다
    // (2.5m 둘레에 서 있으면 1.8m 사거리에 닿지 않는다). 분대 없음·순번 없음 = 자기 쪽 방향.
    public static class MutantSurround
    {
        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // closeRadiusRatio = AiConfigSO.mutantCloseRadiusRatio
        public static Vector3 ApproachPoint(Vector3 target, Vector3 self, Vector3 basePosition, int index, int count,
                                            float surroundRadiusM, float meleeRangeM, float closeRadiusRatio)
        {
            float distance = HorizontalDistance(self, target);
            float radius = distance > surroundRadiusM + meleeRangeM ? surroundRadiusM : meleeRangeM * closeRadiusRatio;
            if (!(radius > 0f))
                return target;

            Vector3 dir;
            if (index >= 0 && count > 1)
            {
                Vector3 baseDir = Horizontal(basePosition - target, self - target);
                float angle = 360f / count * index;
                dir = Quaternion.Euler(0f, angle, 0f) * baseDir;
            }
            else
            {
                dir = Horizontal(self - target, Vector3.forward);
            }
            return target + dir * radius;
        }

        // 수평 단위 벡터. 너무 짧으면 fallback(그것도 짧으면 +z).
        private static Vector3 Horizontal(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            if (v.sqrMagnitude > 1e-4f)
                return v.normalized;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 1e-4f ? fallback.normalized : Vector3.forward;
        }
    }
}
