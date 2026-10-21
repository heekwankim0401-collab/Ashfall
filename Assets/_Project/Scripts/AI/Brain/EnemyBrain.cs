using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.AI
{
    // §7 적 상태 머신 (SYS-AI, M3 설계 §1.6). 순수 C#: Unity 오브젝트를 만지지 않고 EnemySenses → EnemyIntent.
    // Idle → Patrol → Suspicious → Alert → Combat → Search → Patrol (+ 종료 상태 Dead).
    // 전이 표 T1~T13은 설계 §1.6.1. 전이는 Tick 안에서 한 번에 하나만(같은 틱 연쇄 없음).
    // 소리·피격·분대 연락은 받는 즉시 처리하지 않고 다음 Tick에서 전이로 바꾼다
    // (Alert·Combat에서는 LKP만 갱신). Tick·On* 은 할당 없음.
    public sealed class EnemyBrain
    {
        private readonly EnemyArchetypeSO archetype;
        private readonly AiConfigSO config;
        private readonly IEnemyWorld world;
        private readonly IReadOnlyList<Vector3> route;
        private readonly Squad squad;
        private readonly System.Random random;
        private readonly AwarenessModel awareness;
        private readonly IEnemyTactics tactics;
        private readonly TacticsContext ctx;

        private EnemyIntent intent;

        // 다음 Tick에서 처리할 자극
        private bool pendingAlert;          // 총성·피격 = 직접 감지 (T4)
        private Vector3 pendingAlertPos;
        private bool pendingSquad;          // 분대 연락 (T4). Alert·Combat에서는 LKP만 갱신하고 T8 타이머는 그대로
        private Vector3 pendingSquadPos;
        private bool pendingNoise;          // 발소리·점프 (T2, T6)
        private Vector3 pendingNoisePos;

        private float reactionStart = -1f;
        private bool detached;

        // 순찰
        private int patrolIndex;
        private bool patrolWaiting;
        private float patrolWaitUntil;
        private float formationNextUpdate = float.NegativeInfinity;
        private bool hasFormationSlot;
        private Vector3 formationSlot;
        private bool followedLastTick;      // 직전 순찰 틱에 분대원으로 대형을 따랐다 (리더 승계 감지)

        // 수색
        private int searchPointsVisited;
        private bool hasSearchPoint;
        private Vector3 searchPoint;
        private float searchLookUntil = -1f;

        // squadOrder: 분대 안 순서 키(작을수록 앞, 0번 = 리더). 같으면 들어온 순서. EnemyAgent는 씬 계층 순서를 넘긴다
        // (Start 순서는 비결정적이라 리더가 실행마다 바뀌었다 — 리뷰 12).
        public EnemyBrain(int agentId, EnemyArchetypeSO archetype, AiConfigSO config, IEnemyWorld world,
                          IReadOnlyList<Vector3> patrolRoute, Squad squad, System.Random random, int squadOrder = 0)
        {
            AgentId = agentId;
            this.archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            route = patrolRoute;
            this.squad = squad;
            this.random = random ?? new System.Random();
            awareness = new AwarenessModel(config);
            tactics = archetype.usesCover ? new RaiderTactics() : (IEnemyTactics)new ScavengerTactics();
            ctx = new TacticsContext(this, archetype, config, world, this.random, squad);
            State = EnemyState.Idle;
            intent.Posture = EnemyPosture.Stand;
            squad?.Join(this, squadOrder);
        }

        public int AgentId { get; }
        public EnemyState State { get; private set; }
        public float Awareness => awareness.Value;
        public bool HasLastKnown => ctx.HasLastKnown;
        public Vector3 LastKnownPosition => ctx.LastKnown;
        public EnemyIntent Intent => intent;
        public float TimeInState { get; private set; }

        // 마지막 Tick의 자기 위치·방향 (분대 대형·공유 거리 계산용)
        public Vector3 Position { get; private set; }
        public Vector3 Forward { get; private set; } = Vector3.forward;
        internal bool HasTicked { get; private set; }

        // Suspicious 자극 위치 (테스트·디버그용)
        public Vector3 StimulusPosition => ctx.Stimulus;
        public Squad Squad => squad;
        public bool HasRoute => route != null && route.Count > 0;

        // 평시(Idle·Patrol)가 아니면 감각을 더 자주 갱신한다 (설계 §1.8)
        public bool IsEngaged => State != EnemyState.Idle && State != EnemyState.Patrol;

        // EnemyAgent가 HiddenFromTarget 레이를 쏠지 (RDR 숨은 상태만)
        public bool WantsHiddenCheck => State == EnemyState.Combat && tactics.WantsHiddenCheck;

        // (이전, 다음). 디버그 뷰·테스트용
        public event Action<EnemyState, EnemyState> StateChanged;

        internal IEnemyTactics Tactics => tactics;

        // 감각 주기마다 (dt = 직전 Tick 이후 시간)
        public void Tick(in EnemySenses senses, float dt)
        {
            if (State == EnemyState.Dead || detached)
                return;

            ctx.Now = senses.Now;
            ctx.Dt = dt;
            Position = senses.SelfPosition;
            HasTicked = true;
            if (senses.SelfForward.sqrMagnitude > 1e-6f)
                Forward = senses.SelfForward;
            TimeInState += dt;

            // 소리·피격 추정 위치는 총구·발 높이가 섞여 있다(총성 = 총구 1.4m). LKP는 표적 발 위치(시야와 같은 기준)여야
            // ThreatEye(LKP + 선 눈 높이)가 실제 눈 높이가 된다 → NavMesh 바닥에 붙인다. (엄폐 사격 위치 검사가 3m 높이
            // 점을 기준으로 통과해 Peek에서 실제 표적을 못 보던 문제, M3_AIPerf Q4R2.) 분대 연락은 이미 발 위치.
            if (pendingAlert)
                pendingAlertPos = GroundPoint(pendingAlertPos);
            if (pendingNoise)
                pendingNoisePos = GroundPoint(pendingNoisePos);

            bool visible = senses.TargetExists && senses.TargetVisible;
            if (visible)
            {
                awareness.AddSight(senses.TargetDistance, senses.EffectiveSightRange, dt);
                ctx.LastSeenTime = senses.Now;
                ctx.LastSensedAt = senses.Now;
                ctx.Stimulus = senses.TargetPosition;
                ctx.SetLastKnown(senses.TargetPosition);
            }
            else
            {
                awareness.Decay(dt);
            }

            EvaluateTransition(senses, visible);
            pendingAlert = false;
            pendingSquad = false;
            pendingNoise = false;

            BuildIntent(senses, visible);
        }

        public void OnHeard(Vector3 position, NoiseKind kind, float now)
        {
            if (State == EnemyState.Dead || detached)
                return;
            if (kind == NoiseKind.Gunshot)
            {
                awareness.SetFull();
                SetPendingAlert(position);
                return;
            }
            awareness.AddNoise(kind);
            pendingNoise = true;
            pendingNoisePos = position;
        }

        public void OnDamaged(Vector3 estimatedSource, float now)
        {
            if (State == EnemyState.Dead || detached)
                return;
            awareness.SetFull();
            SetPendingAlert(estimatedSource);
        }

        // 분대 연락: Idle·Patrol·Suspicious·Search면 T4. Alert·Combat에서는 LKP만 갱신(T8 타이머는 직접 감지 기준).
        public void OnSquadContact(Vector3 position, float now)
        {
            if (State == EnemyState.Dead || detached)
                return;
            pendingSquad = true;
            pendingSquadPos = position;
        }

        // T13: 자기 사망. 엄폐 예약·분대 우회권 해제, 분대 탈퇴.
        public void OnDied()
        {
            if (State == EnemyState.Dead)
                return;
            ReleaseAll();
            ChangeState(EnemyState.Dead);
            intent = default;
            intent.Posture = EnemyPosture.Stand;
        }

        // 살아 있는 적이 씬에서 빠질 때(파괴·비활성화, 리뷰 5): 사망과 같은 정리(엄폐·우회권 반납, 분대 탈퇴 → 리더 승계)를
        // 하되 상태는 바꾸지 않는다. 이후 Tick·On*은 무시한다. 다시 붙이는 기능은 없다.
        public void Detach()
        {
            if (detached || State == EnemyState.Dead)
                return;
            ReleaseAll();
            detached = true;
            intent = default;
            intent.Posture = EnemyPosture.Stand;
        }

        public bool IsDetached => detached;

        private void ReleaseAll()
        {
            ctx.Now = world.Now;
            tactics.ReleaseReservations(ctx);
            world.ReleaseCover(AgentId);
            if (squad != null)
            {
                squad.ReleaseFlank(AgentId, world.Now);
                squad.Leave(this);
            }
            pendingAlert = false;
            pendingSquad = false;
            pendingNoise = false;
        }

        private void SetPendingAlert(Vector3 position)
        {
            pendingAlert = true;
            pendingAlertPos = position;
        }

        // ── 전이 (설계 §1.6.1) ─────────────────────────────

        // T4 자극(총성·피격 우선, 없으면 분대 연락)
        private bool AnyAlert => pendingAlert || pendingSquad;
        private Vector3 AlertPos => pendingAlert ? pendingAlertPos : pendingSquadPos;

        private void EvaluateTransition(in EnemySenses s, bool visible)
        {
            switch (State)
            {
                case EnemyState.Idle:
                case EnemyState.Patrol:
                    if (pendingNoise)
                        ctx.Stimulus = pendingNoisePos;
                    if (AnyAlert)
                        EnterAlert(AlertPos, visible, s.Now);                               // T4
                    else if (awareness.IsFull)
                        EnterAlert(ctx.Stimulus, visible, s.Now);                           // T3
                    else if (pendingNoise || awareness.Value >= config.suspiciousAt)
                        ChangeState(EnemyState.Suspicious);                                 // T2
                    else if (State == EnemyState.Idle && HasRoute && TimeInState >= config.idleBeforePatrolSec)
                        ChangeState(EnemyState.Patrol);                                     // T1
                    break;

                case EnemyState.Suspicious:
                    if (pendingNoise)
                    {
                        // 새 소리 → 다시 조사
                        ctx.Stimulus = pendingNoisePos;
                        ctx.SuspiciousLookElapsed = 0f;
                    }
                    if (AnyAlert)
                        EnterAlert(AlertPos, visible, s.Now);                               // T4
                    else if (awareness.IsFull)
                        EnterAlert(ctx.Stimulus, visible, s.Now);                           // T3
                    else if (awareness.Value <= 0f && ctx.SuspiciousLookElapsed >= config.suspiciousLookSec)
                        ChangeState(RelaxedState);                                          // T11
                    break;

                case EnemyState.Alert:
                    if (!s.TargetExists)
                    {
                        ChangeState(RelaxedState);                                          // T12
                        break;
                    }
                    // 분대 연락은 LKP만, 직접 들은 소리·피격은 LKP + 감지 시각 (리뷰 1). 보이면 본 위치가 LKP다.
                    if (pendingSquad && !visible)
                        ctx.SetLastKnown(pendingSquadPos);
                    if (pendingAlert && !visible)
                        SenseDirect(pendingAlertPos, s.Now);
                    if (pendingNoise && !visible)
                        SenseDirect(pendingNoisePos, s.Now);
                    if (visible)
                    {
                        if (reactionStart < 0f)
                            reactionStart = s.Now;
                        if (s.Now - reactionStart >= archetype.reactionTimeSec)
                            ChangeState(EnemyState.Combat);                                 // T7
                    }
                    else
                    {
                        reactionStart = -1f;
                        // T8: "LKP 갱신 후"를 "직접 감지(봄·들음·피격) 또는 Alert 진입 후"로 해석 (리뷰 1).
                        // 분대원이 0.5초마다 연락해도 스스로 아무것도 못 느끼면 alertTimeoutSec 뒤 Search로 나간다.
                        if (s.Now - ctx.LastSensedAt >= config.alertTimeoutSec)
                            ChangeState(EnemyState.Search);                                 // T8
                    }
                    break;

                case EnemyState.Combat:
                    if (!s.TargetExists)
                    {
                        ChangeState(RelaxedState);                                          // T12
                        break;
                    }
                    if (pendingSquad && !visible)
                        ctx.SetLastKnown(pendingSquadPos);
                    if (pendingAlert && !visible)
                        SenseDirect(pendingAlertPos, s.Now);
                    if (pendingNoise && !visible)
                        SenseDirect(pendingNoisePos, s.Now);
                    if (!visible && s.Now - ctx.LastSeenTime >= config.loseSightSec && !tactics.DefersLoseSight)
                        ChangeState(EnemyState.Search);                                     // T9
                    break;

                case EnemyState.Search:
                    if (!s.TargetExists)
                        ChangeState(RelaxedState);                                          // T12
                    else if (AnyAlert)
                        EnterAlert(AlertPos, visible, s.Now);                               // T4
                    else if (visible)
                        EnterAlert(s.TargetPosition, true, s.Now);                          // T5
                    else if (pendingNoise)
                        EnterAlert(pendingNoisePos, false, s.Now);                          // T6
                    else if (TimeInState >= config.searchDurationSec)
                        ChangeState(RelaxedState);                                          // T10
                    break;
            }
        }

        private EnemyState RelaxedState => HasRoute ? EnemyState.Patrol : EnemyState.Idle;

        private Vector3 GroundPoint(Vector3 position) =>
            world.TrySampleNavPoint(position, config.navSampleRadiusM, out Vector3 ground) ? ground : position;

        private void SenseDirect(Vector3 position, float now)
        {
            ctx.SetLastKnown(position);
            ctx.LastSensedAt = now;
        }

        private void EnterAlert(Vector3 lastKnown, bool visible, float now)
        {
            awareness.SetFull();
            SenseDirect(lastKnown, now);   // Alert 진입 = T8 타이머 시작
            ChangeState(EnemyState.Alert);
            reactionStart = visible ? now : -1f;
            squad?.ReportContact(AgentId, Position, lastKnown, now);
        }

        private void ChangeState(EnemyState next)
        {
            EnemyState previous = State;
            if (previous == next)
                return;

            // 엄폐·우회권은 교전 상태(Alert·Combat) 밖으로 나가면 반납 (T12, T13 포함)
            bool engagedNext = next == EnemyState.Alert || next == EnemyState.Combat;
            if (!engagedNext && next != EnemyState.Dead)
            {
                tactics.ReleaseReservations(ctx);
                world.ReleaseCover(AgentId);
            }

            State = next;
            TimeInState = 0f;
            tactics.Reset();
            ctx.ClearDestination();

            switch (next)
            {
                case EnemyState.Idle:
                case EnemyState.Patrol:
                    // 경계를 풀고 돌아온다 (게이지가 남아 곧바로 다시 경계하지 않게)
                    if (previous != EnemyState.Idle)
                        awareness.Clear();
                    reactionStart = -1f;
                    if (next == EnemyState.Patrol)
                        BeginPatrol();
                    break;
                case EnemyState.Suspicious:
                    ctx.SuspiciousLookElapsed = 0f;
                    break;
                case EnemyState.Search:
                    searchPointsVisited = 0;
                    hasSearchPoint = false;
                    searchLookUntil = -1f;
                    break;
            }

            LogTransition(previous, next);
            StateChanged?.Invoke(previous, next);
        }

        // T10 "경로상 가장 가까운 웨이포인트부터"
        private void BeginPatrol()
        {
            patrolWaiting = false;
            followedLastTick = false;
            hasFormationSlot = false;
            formationNextUpdate = float.NegativeInfinity;
            if (!HasRoute)
                return;
            float best = float.MaxValue;
            for (int i = 0; i < route.Count; i++)
            {
                float d = (route[i] - Position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    patrolIndex = i;
                }
            }
        }

        // ── 의도 ───────────────────────────────────────

        private void BuildIntent(in EnemySenses s, bool visible)
        {
            intent = default;
            intent.Posture = EnemyPosture.Stand;

            switch (State)
            {
                case EnemyState.Idle:
                    TickIdle(s);
                    break;
                case EnemyState.Patrol:
                    TickPatrol(s);
                    break;
                case EnemyState.Suspicious:
                    tactics.TickSuspicious(ctx, s, ref intent);
                    break;
                case EnemyState.Alert:
                    tactics.TickAlert(ctx, s, ref intent);
                    break;
                case EnemyState.Combat:
                    tactics.TickCombat(ctx, s, ref intent);
                    if (visible)
                        squad?.ReportContact(AgentId, Position, s.TargetPosition, s.Now);
                    break;
                case EnemyState.Search:
                    TickSearch(s);
                    break;
            }

            // 사격은 표적이 보일 때만 (설계 §1.4 사격 허용 조건의 brain 쪽 몫)
            if (!visible)
                intent.Fire = false;
        }

        private void TickIdle(in EnemySenses s)
        {
            TacticsContext.Hold(ref intent);
            // 분대원은 리더가 순찰을 시작하기 전에도 대형 자리로 간다.
            // 리더에게 경로가 없으면(경계 분대) 제자리를 지킨다 — 대형 자리로 가면 배치한 경계 위치를 벗어난다(리뷰 6).
            if (IsFollower && squad.Leader.HasRoute)
                FollowFormation(s);
        }

        private bool IsFollower => squad != null && squad.Leader != null && squad.Leader != this;

        private void TickPatrol(in EnemySenses s)
        {
            if (IsFollower && squad.Leader.HasRoute)
            {
                followedLastTick = true;
                FollowFormation(s);
                return;
            }
            if (followedLastTick)
            {
                // 리더 승계(앞 리더 탈퇴): 따라가던 동안 patrolIndex가 낡았으니 지금 위치에서 가장 가까운 웨이포인트부터 (리뷰 12)
                followedLastTick = false;
                BeginPatrol();
            }
            if (!HasRoute)
            {
                TacticsContext.Hold(ref intent);
                return;
            }

            if (patrolWaiting)
            {
                if (s.Now < patrolWaitUntil)
                {
                    TacticsContext.Hold(ref intent);
                    return;
                }
                patrolWaiting = false;
                patrolIndex = (patrolIndex + 1) % route.Count;
            }

            Vector3 waypoint = route[patrolIndex];
            if (ctx.Reached(s, waypoint))
            {
                patrolWaiting = true;
                patrolWaitUntil = s.Now + ctx.RandomRange(config.patrolWaitSec);
                TacticsContext.Hold(ref intent);
                return;
            }
            ctx.MoveTo(ref intent, waypoint, EnemyMoveMode.Walk);
        }

        // 리더 뒤 대형 슬롯을 formationUpdateSec(2Hz)마다 다시 계산해 따라간다. 리더가 멈추면 같이 멈춘다.
        private void FollowFormation(in EnemySenses s)
        {
            EnemyBrain leader = squad.Leader;
            if (!leader.HasTicked)
            {
                TacticsContext.Hold(ref intent);
                return;
            }
            if (s.Now >= formationNextUpdate || !hasFormationSlot)
            {
                formationNextUpdate = s.Now + config.formationUpdateSec;
                formationSlot = squad.FormationSlot(squad.IndexOf(AgentId), leader.Position, leader.Forward);
                hasFormationSlot = true;
            }

            Vector3 d = formationSlot - s.SelfPosition;
            d.y = 0f;
            float distance = d.magnitude;
            if (distance <= config.pointArriveDistanceM)
            {
                TacticsContext.Hold(ref intent);
                TacticsContext.LookAt(ref intent, s.SelfPosition + leader.Forward * config.lookAheadDistanceM);
                return;
            }
            ctx.MoveTo(ref intent, formationSlot,
                distance > config.formationCatchUpDistanceM ? EnemyMoveMode.Run : EnemyMoveMode.Walk);
        }

        // LKP 주변 searchRadiusM 안 NavMesh 점 searchPointCount개를 돌며 둘러본다.
        private void TickSearch(in EnemySenses s)
        {
            if (!hasSearchPoint && searchPointsVisited < config.searchPointCount)
            {
                Vector3 center = ctx.HasLastKnown ? ctx.LastKnown : s.SelfPosition;
                double angle = random.NextDouble() * 2.0 * Math.PI;
                float r = config.searchRadiusM * (float)Math.Sqrt(random.NextDouble());
                Vector3 candidate = center + new Vector3((float)Math.Cos(angle) * r, 0f, (float)Math.Sin(angle) * r);
                searchPoint = world.TrySampleNavPoint(candidate, config.navSampleRadiusM, out Vector3 sampled) ? sampled : center;
                hasSearchPoint = true;
                searchLookUntil = -1f;
            }

            if (!hasSearchPoint)
            {
                // 지점을 다 돌았으면 수색 시간이 끝날 때까지 LKP 쪽을 둘러본다
                TacticsContext.Hold(ref intent);
                LookAround(s, ctx.HasLastKnown ? ctx.LastKnown : s.SelfPosition + Forward);
                return;
            }

            if (searchLookUntil < 0f)
            {
                if (ctx.Reached(s, searchPoint))
                    searchLookUntil = s.Now + config.searchLookSec;
                else
                {
                    ctx.MoveTo(ref intent, searchPoint, EnemyMoveMode.Walk);
                    return;
                }
            }

            if (s.Now >= searchLookUntil)
            {
                hasSearchPoint = false;
                searchPointsVisited++;
            }
            TacticsContext.Hold(ref intent);
            LookAround(s, ctx.HasLastKnown ? ctx.LastKnown : s.SelfPosition + Forward);
        }

        // 기준 방향에서 좌우 searchLookYawDeg를 searchLookSec 주기로 훑는다.
        private void LookAround(in EnemySenses s, Vector3 focus)
        {
            Vector3 dir = focus - s.SelfPosition;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f)
                dir = Forward;
            float period = config.searchLookSec > 0f ? config.searchLookSec : 1f;
            float yaw = config.searchLookYawDeg * Mathf.Sin((TimeInState / period) * Mathf.PI);
            TacticsContext.LookAt(ref intent, s.SelfPosition + Quaternion.Euler(0f, yaw, 0f) * dir.normalized * config.lookAheadDistanceM);
        }

        [Conditional("ASHFALL_DEBUG_AI")]
        private void LogTransition(EnemyState from, EnemyState to)
        {
            UnityEngine.Debug.Log($"[AI {AgentId}] {from} → {to}");
        }
    }
}
