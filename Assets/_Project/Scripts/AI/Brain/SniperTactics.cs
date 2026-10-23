using UnityEngine;

namespace Ashfall.AI
{
    // AI-SNP 저격수 전술 (§7 "고지대 고정, 레이저 반사 신호", M7 설계 §1.4, Q22·Q23).
    // 퍼치 고정: 어떤 상태에서도 이동 의도를 내지 않는다(Hold). 상태 전이는 EnemyBrain 몫이다.
    // - Idle·Patrol: 시작 방향(첫 Tick의 몸 방향 = 배치한 감시 방향)을 기준으로 좌우 scanArcDeg 폭을 훑는다.
    // - Search: 자리에서 LKP 쪽을 기준으로 같은 폭을 훑는다.
    // - Suspicious(발소리 등): 자극 쪽으로 돌아보기만 한다(investigatesNoise와 무관하게 이동 없음).
    // - Alert·Combat: 보이면 표적 쪽으로 돌고 Telegraph(섬광)를 켠다. 표적이 계속 보인 지 telegraphSec가 지나야
    //   사격한다(Combat에서만, engageMaxRangeM 안). 한 번이라도 안 보이는 감각 갱신이 있으면 경고를 처음부터 다시 한다.
    //   사격 사이에도 보이는 동안 섬광은 켜져 있다. 사격 간격은 EnemyWeapon(semiShotIntervalSec)이 정한다.
    //   안 보이면 자리에서 위협 쪽(LKP 눈 높이)을 본다.
    // Tick 경로 할당 없음.
    internal sealed class SniperTactics : IEnemyTactics
    {
        // 경고(조준) 진행: 표적이 연속으로 보이기 시작한 시각. 상태 전이(Alert → Combat)를 넘어 이어진다.
        private bool aiming;
        private float aimStartedAt;

        // 평시 훑기 기준 방향 (첫 평시 Tick의 몸 방향으로 한 번 정한다)
        private bool hasScanBase;
        private Vector3 scanBaseForward = Vector3.forward;

        public bool DefersLoseSight => false;
        public bool WantsHiddenCheck => false;

        // 경고 타이머는 여기서 지우지 않는다: Alert → Combat 전이에서도 Reset이 불리는데 그때 경고가 다시 시작되면 안 된다.
        // 안 보이는 틱·평시·Suspicious에서 끊긴다(StopAiming).
        public void Reset()
        {
        }

        // 퍼치 고정 훑기: Idle·Patrol = 감시 방향 기준, Search = LKP 기준. 이동 없음.
        public bool TryTickRelaxed(EnemyState state, TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            StopAiming();
            TacticsContext.Hold(ref i);
            if (!hasScanBase)
            {
                Vector3 f = s.SelfForward;
                f.y = 0f;
                // TODO(GDD-?) Q22: 감시 방향 = 배치 때 몸 방향(첫 평시 Tick). 별도 감시 지점 필드는 두지 않는다.
                scanBaseForward = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
                hasScanBase = true;
            }

            Vector3 focus = state == EnemyState.Search && c.HasLastKnown
                ? c.LastKnown
                : s.SelfPosition + scanBaseForward;
            // scanArcDeg = 훑는 전체 폭 → 좌우 각 절반. 주기는 수색 둘러보기와 같은 searchLookSec.
            TacticsContext.LookAround(ref i, s, focus, scanBaseForward, c.Archetype.scanArcDeg * 0.5f,
                c.Config.searchLookSec, c.Brain.TimeInState, c.Config.lookAheadDistanceM);
            return true;
        }

        // 소리 → 돌아봄만 (Q22)
        public void TickSuspicious(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            StopAiming();
            TacticsContext.Hold(ref i);
            TacticsContext.LookAt(ref i, c.Stimulus);
            c.SuspiciousLookElapsed += c.Dt;
        }

        // 반응 시간 중: 조준·섬광은 하되 쏘지 않는다
        public void TickAlert(TacticsContext c, in EnemySenses s, ref EnemyIntent i) => Engage(c, s, ref i, false);

        public void TickCombat(TacticsContext c, in EnemySenses s, ref EnemyIntent i) => Engage(c, s, ref i, true);

        public void ReleaseReservations(TacticsContext c)
        {
        }

        private void Engage(TacticsContext c, in EnemySenses s, ref EnemyIntent i, bool allowFire)
        {
            TacticsContext.Hold(ref i);
            if (!(s.TargetExists && s.TargetVisible))
            {
                // 놓침: 다시 보면 경고부터. 자리에서 LKP 쪽을 본다(Search로 가면 훑기).
                StopAiming();
                TacticsContext.LookAt(ref i, c.ThreatEye(s));
                return;
            }

            if (!aiming)
            {
                aiming = true;
                aimStartedAt = c.Now;
            }
            TacticsContext.LookAt(ref i, s.TargetCenter);
            i.Telegraph = true;
            i.FireAt = s.TargetCenter;
            i.Fire = allowFire
                     && c.Now - aimStartedAt >= c.Archetype.telegraphSec
                     && s.TargetDistance <= c.Archetype.engageMaxRangeM;
        }

        private void StopAiming() => aiming = false;
    }
}
