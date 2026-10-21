using UnityEngine;

namespace Ashfall.AI
{
    // AI-RDR 레이더 전술 (§7 "엄폐 활용, 측면 우회", M3 설계 §1.6.2).
    // - Suspicious: 제자리에서 웅크리고 자극 쪽을 봄
    // - Alert(안 보임): LKP를 위협으로 하는 엄폐를 골라 달려감. 엄폐에 닿으면 Combat처럼 Hide ↔ Peek 주기를 돌며
    //   Peek 때 일어서거나(Low) PeekPosition으로 나가(High) LKP 쪽을 본다(사격·우회 없음). 보이면 T7로 Combat.
    //   (리뷰 1: Alert에 Peek가 없으면 엄폐 뒤에서 표적을 못 봐 Alert에 갇혔다.) 엄폐가 없으면 약탈자처럼
    // - Combat: 엄폐로 이동(Run, 사격 안 함) → Hide(hideSec, Low 웅크림/High 선 자세, 숨는 자리로 돌아감) ↔
    //   Peek(peekSec, 선 자세, High는 PeekPosition) 반복, Peek 중 보이면 사격
    // - 엄폐 질의가 프레임 예산 초과(Deferred)면 제자리에서 위협 쪽을 본다(약탈자 행동은 None일 때만, 리뷰 3)
    // - 엄폐 무효화: 숨는 자리에서 표적에게 보이거나(HiddenFromTarget=false) 위협이 보호각 밖 → 그 지점을
    //   coverInvalidatedExcludeSec 동안 빼고 다시 고름(리뷰 4)
    // - 측면 우회: Peek 중 못 본 시간 누적 ≥ flankAfterNoSightSec ∧ 우회권 → 측면 엄폐로 Sprint, 도착하면 Peek·우회권 반납
    internal sealed class RaiderTactics : IEnemyTactics
    {
        private enum Phase
        {
            None,
            MovingToCover,
            Hide,
            Peek,
            Flanking,
        }

        private readonly ScavengerTactics fallback = new ScavengerTactics();

        private bool hasCover;
        private CoverSpot cover;
        private Phase phase;
        private float phaseUntil;
        private float noSightAccum;
        private bool flankClaimed;
        private float nextCoverRetryAt = float.NegativeInfinity;
        private bool canFlank;
        private bool flankResolved;   // 마지막으로 본 뒤 우회를 시도해 끝냈나(도착·실패)
        private bool hideSettled;     // Hide 중 숨는 자리(cover.Position)에 있다
        private bool refreshPeek;     // Alert의 Peek를 Combat이 이어받았다 → 다음 Combat 틱에 Peek 시간을 새로 잡는다
        private bool hasExcludedCover;
        private int excludedCoverId;
        private float excludedUntil;

        // T9 유예: 우회 중, 그리고 엄폐에 자리 잡고 아직 우회를 시도하지 못한 동안.
        // 설계 값은 loseSightSec(5) < flankAfterNoSightSec(6)이라 유예가 없으면 우회 전에 늘 Search로 간다(2b 구현 결정).
        public bool DefersLoseSight =>
            phase == Phase.Flanking ||
            (canFlank && hasCover && !flankResolved &&
             (phase == Phase.MovingToCover || phase == Phase.Hide || phase == Phase.Peek));
        // 숨는 자리에 실제로 있을 때만 (Peek 자리에서 돌아오는 중에는 보여도 무효화하지 않는다)
        public bool WantsHiddenCheck => hasCover && phase == Phase.Hide && hideSettled;

        // 테스트·디버그용
        internal bool HasCover => hasCover;
        internal CoverSpot Cover => cover;
        internal string PhaseName => phase.ToString();

        public void Reset()
        {
            fallback.Reset();
            noSightAccum = 0f;
            flankResolved = false;
            refreshPeek = false;
            // Alert → Combat에서 이미 잡은 엄폐는 이어서 쓴다. Alert에서 이미 숨거나 내다보던 중이면 그 단계를 잇는다
            // (Peek 중 표적을 봐서 Combat이 됐는데 다시 숨으면 반응이 늦어진다).
            if (!hasCover)
                phase = Phase.None;
            else if (phase == Phase.Peek)
                refreshPeek = true;
            else if (phase != Phase.Hide)
                phase = Phase.MovingToCover;
        }

        public void TickSuspicious(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            TacticsContext.Hold(ref i);
            TacticsContext.LookAt(ref i, c.Stimulus);
            i.Posture = EnemyPosture.Crouch;
            c.SuspiciousLookElapsed += c.Dt;
        }

        public void TickAlert(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            bool visible = s.TargetExists && s.TargetVisible;
            Vector3 threatEye = c.ThreatEye(s);

            if (!hasCover && !AcquireOrFallback(c, s, ref i, threatEye, visible, false))
                return;

            // 분대 연락으로 LKP가 옮겨 엄폐가 더는 막아 주지 못하면 다시 고른다
            if ((phase == Phase.Hide || phase == Phase.Peek) && !InProtection(c, threatEye))
            {
                InvalidateCover(c);
                if (!AcquireOrFallback(c, s, ref i, threatEye, visible, false))
                    return;
            }

            TacticsContext.LookAt(ref i, visible ? s.TargetCenter : threatEye);

            switch (phase)
            {
                case Phase.None:
                case Phase.MovingToCover:
                case Phase.Flanking:
                    if (c.Reached(s, cover.Position, c.Config.coverArriveDistanceM))
                    {
                        EnterHide(c);
                        goto case Phase.Hide;
                    }
                    c.MoveTo(ref i, cover.Position, EnemyMoveMode.Run);
                    return;

                case Phase.Hide:
                    if (c.Now >= phaseUntil)
                    {
                        EnterPeek(c);
                        goto case Phase.Peek;
                    }
                    HoldHideSpot(c, s, ref i);
                    return;

                case Phase.Peek:
                    // 내다보며 LKP 쪽을 본다. 보이면 brain이 반응 시간 뒤 Combat(T7). 사격·우회는 Combat에서만.
                    MoveToFirePosition(c, s, ref i);
                    if (c.Now >= phaseUntil && !visible)
                    {
                        EnterHide(c);
                        HoldHideSpot(c, s, ref i);
                    }
                    return;
            }
        }

        public void TickCombat(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            bool visible = s.TargetExists && s.TargetVisible;
            Vector3 threatEye = c.ThreatEye(s);
            Vector3 look = visible ? s.TargetCenter : threatEye;
            canFlank = c.Archetype.flanks;
            if (visible)
                flankResolved = false;

            if (!hasCover && !AcquireOrFallback(c, s, ref i, threatEye, visible, true))
                return;

            // 엄폐 무효화 (Hide·Peek)
            if ((phase == Phase.Hide || phase == Phase.Peek) &&
                (!InProtection(c, threatEye) || (phase == Phase.Hide && hideSettled && !s.HiddenFromTarget)))
            {
                InvalidateCover(c);
                if (!AcquireOrFallback(c, s, ref i, threatEye, visible, true))
                    return;
            }

            TacticsContext.LookAt(ref i, look);
            i.FireAt = s.TargetCenter;

            switch (phase)
            {
                case Phase.None:
                case Phase.MovingToCover:
                    if (c.Reached(s, cover.Position, c.Config.coverArriveDistanceM))
                    {
                        EnterHide(c);
                        goto case Phase.Hide;
                    }
                    c.MoveTo(ref i, cover.Position, EnemyMoveMode.Run);
                    return;

                case Phase.Hide:
                    if (c.Now >= phaseUntil)
                    {
                        EnterPeek(c);
                        TickPeek(c, s, ref i, visible, threatEye);
                        return;
                    }
                    HoldHideSpot(c, s, ref i);
                    i.Reload = true;   // 숨은 동안 재장전해도 좋다 (탄 비율 판단은 EnemyAgent: tacticalReloadBelow)
                    return;

                case Phase.Peek:
                    if (refreshPeek)
                    {
                        refreshPeek = false;
                        phaseUntil = c.Now + c.RandomRange(c.Archetype.peekSec);
                    }
                    TickPeek(c, s, ref i, visible, threatEye);
                    return;

                case Phase.Flanking:
                    if (c.Reached(s, cover.Position, c.Config.coverArriveDistanceM))
                    {
                        ReleaseFlank(c);
                        flankResolved = true;
                        EnterPeek(c);
                        noSightAccum = 0f;
                        TickPeek(c, s, ref i, visible, threatEye);
                        return;
                    }
                    c.MoveTo(ref i, cover.Position, EnemyMoveMode.Sprint);
                    return;
            }
        }

        public void ReleaseReservations(TacticsContext c)
        {
            fallback.ReleaseReservations(c);
            ReleaseCover(c);
            ReleaseFlank(c);
            phase = Phase.None;
            noSightAccum = 0f;
            refreshPeek = false;
        }

        private void TickPeek(TacticsContext c, in EnemySenses s, ref EnemyIntent i, bool visible, Vector3 threatEye)
        {
            MoveToFirePosition(c, s, ref i);

            i.Fire = visible && s.TargetDistance <= c.Archetype.engageMaxRangeM;

            if (visible)
                noSightAccum = 0f;
            else
                noSightAccum += c.Dt;

            if (c.Archetype.flanks && noSightAccum >= c.Config.flankAfterNoSightSec && TryFlank(c, s, threatEye))
            {
                c.MoveTo(ref i, cover.Position, EnemyMoveMode.Sprint);
                i.Fire = false;
                i.Posture = EnemyPosture.Stand;
                return;
            }

            if (c.Now >= phaseUntil)
            {
                EnterHide(c);
                HoldHideSpot(c, s, ref i);
                i.Fire = false;
                i.Reload = true;
            }
        }

        // Peek: 선 자세로 사격 위치(Low = 숨는 자리, High = PeekPosition)에 선다
        private void MoveToFirePosition(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            i.Posture = EnemyPosture.Stand;
            Vector3 firePos = cover.Height == CoverHeight.High ? cover.PeekPosition : cover.Position;
            if (c.Reached(s, firePos, c.Config.coverArriveDistanceM))
                TacticsContext.Hold(ref i);
            else
                c.MoveTo(ref i, firePos, EnemyMoveMode.Walk);
        }

        // Hide: 숨는 자세로 숨는 자리(cover.Position)에 있는다. High 엄폐 Peek 뒤에는 PeekPosition에서 걸어 돌아온다(리뷰 2).
        private void HoldHideSpot(TacticsContext c, in EnemySenses s, ref EnemyIntent i)
        {
            i.Posture = cover.Height == CoverHeight.Low ? EnemyPosture.Crouch : EnemyPosture.Stand;
            hideSettled = c.Reached(s, cover.Position, c.Config.coverArriveDistanceM);
            if (hideSettled)
                TacticsContext.Hold(ref i);
            else
                c.MoveTo(ref i, cover.Position, EnemyMoveMode.Walk);
        }

        private void EnterHide(TacticsContext c)
        {
            phase = Phase.Hide;
            hideSettled = false;
            phaseUntil = c.Now + c.RandomRange(c.Archetype.hideSec);
        }

        private void EnterPeek(TacticsContext c)
        {
            phase = Phase.Peek;
            hideSettled = false;
            refreshPeek = false;
            phaseUntil = c.Now + c.RandomRange(c.Archetype.peekSec);
        }

        // 엄폐를 잡으면 true(phase = MovingToCover). 못 잡으면 의도를 채우고 false:
        // Deferred(프레임 예산 초과) → 제자리에서 위협 쪽을 봄(Combat에서 보이면 사격), None → 약탈자 행동.
        private bool AcquireOrFallback(TacticsContext c, in EnemySenses s, ref EnemyIntent i, Vector3 threatEye,
                                       bool visible, bool combat)
        {
            CoverResult result = TryAcquireCover(c, s, threatEye);
            if (result == CoverResult.Found)
            {
                phase = Phase.MovingToCover;
                return true;
            }
            if (result == CoverResult.Deferred)
            {
                TacticsContext.Hold(ref i);
                TacticsContext.LookAt(ref i, visible ? s.TargetCenter : threatEye);
                if (combat)
                {
                    i.FireAt = s.TargetCenter;
                    i.Fire = visible && s.TargetDistance <= c.Archetype.engageMaxRangeM;
                }
                return false;
            }
            if (combat)
                fallback.TickCombat(c, s, ref i);
            else
                fallback.TickAlert(c, s, ref i);
            return false;
        }

        // 재시도 대기(coverRetrySec) 중이면 None으로 본다.
        private CoverResult TryAcquireCover(TacticsContext c, in EnemySenses s, Vector3 threatEye)
        {
            if (c.Now < nextCoverRetryAt)
                return CoverResult.None;
            CoverQuery query = MakeQuery(c, s, threatEye, false);
            CoverResult result = c.World.TryFindCover(query, out CoverSpot spot);
            if (result == CoverResult.Found)
            {
                hasCover = true;
                cover = spot;
            }
            else if (result == CoverResult.None)
            {
                nextCoverRetryAt = c.Now + c.Config.coverRetrySec;
            }
            // Deferred: 다음 틱에 다시
            return result;
        }

        private CoverQuery MakeQuery(TacticsContext c, in EnemySenses s, Vector3 threatEye, bool flank)
        {
            bool exclude = hasExcludedCover && c.Now < excludedUntil;
            return new CoverQuery
            {
                AgentId = c.AgentId,
                Self = s.SelfPosition,
                ThreatEye = threatEye,
                PreferredRangeM = c.Archetype.preferredRangeM,
                // 엄폐에서 내다볼 때 표적이 시야·사거리 안이어야 한다 (M3_AIPerf에서 65m 엄폐를 골라 Alert에 남던 문제).
                // TODO(GDD-?): 자세 배율(웅크린 표적 = 시야 0.6배)은 반영하지 않는다
                MaxThreatDistanceM = Mathf.Min(c.Archetype.sightRange, c.Archetype.engageMaxRangeM),
                Flank = flank,
                FlankFrom = s.SelfPosition,
                HasExcludedCover = exclude,
                ExcludedCoverId = exclude ? excludedCoverId : -1,
            };
        }

        // 지금 엄폐를 버리고 coverInvalidatedExcludeSec 동안 후보에서 뺀다.
        private void InvalidateCover(TacticsContext c)
        {
            if (hasCover)
            {
                hasExcludedCover = true;
                excludedCoverId = cover.Id;
                excludedUntil = c.Now + c.Config.coverInvalidatedExcludeSec;
            }
            ReleaseCover(c);
        }

        private bool TryFlank(TacticsContext c, in EnemySenses s, Vector3 threatEye)
        {
            if (c.Squad != null && !c.Squad.TryClaimFlank(c.AgentId, c.Now))
                return false;
            flankClaimed = true;

            CoverQuery query = MakeQuery(c, s, threatEye, true);
            CoverResult result = c.World.TryFindCover(query, out CoverSpot spot);
            if (result == CoverResult.Deferred)
                return false;   // 우회권은 쥔 채 다음 틱 재시도

            if (result == CoverResult.Found && (!hasCover || spot.Id != cover.Id))
            {
                hasCover = true;
                cover = spot;
                phase = Phase.Flanking;
                hideSettled = false;
                noSightAccum = 0f;
                return true;
            }

            // 실패: 엄폐 유지, 우회권 반납, 다시 flankAfterNoSightSec 기다린다
            // TODO(GDD-?): 이동 경로가 표적에게 보이는지는 확인하지 않는다
            ReleaseFlank(c);
            noSightAccum = 0f;
            flankResolved = true;
            return false;
        }

        private bool InProtection(TacticsContext c, Vector3 threatEye)
        {
            Vector3 toThreat = threatEye - cover.Position;
            toThreat.y = 0f;
            if (toThreat.sqrMagnitude < 1e-6f)
                return false;
            return Vector3.Angle(cover.Forward, toThreat) <= c.Config.coverProtectionHalfAngleDeg;
        }

        private void ReleaseCover(TacticsContext c)
        {
            if (hasCover)
                c.World.ReleaseCover(c.AgentId);
            hasCover = false;
            hideSettled = false;
            phase = Phase.None;
        }

        private void ReleaseFlank(TacticsContext c)
        {
            if (!flankClaimed)
                return;
            flankClaimed = false;
            c.Squad?.ReleaseFlank(c.AgentId, c.Now);
        }
    }
}
