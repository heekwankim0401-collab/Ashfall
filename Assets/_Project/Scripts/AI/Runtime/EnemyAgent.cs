using Ashfall.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Ashfall.AI
{
    // 적 1명 브리지 (M3 설계 §5.1, §6). 로직은 EnemyBrain·EnemyBodyModel·EnemyWeapon(순수 C#)에 있고
    // 이것은 감각 수집(Sense, 감각 주기마다)과 의도 적용(FrameTick, 매 프레임)만 한다. 둘 다 AiDirector가 부른다.
    // - 적 루트는 씬 최상위여야 한다(ProjectileSystem 자기 탄 판정·시야 루트 비교, D15)
    // - 피격: 단일 캡슐 높이 비율 → 3부위 (D4)
    // - 사망: 시체로 눕히고 EnemyKilled 발행 → Inventory가 루트 컨테이너를 붙인다 (§1.9)
    // - M6 (§11, Q13·Q14·Q26): 보폭마다 FootstepTaken(Enemy), 발사마다 ShotFired(Enemy) — Audio 전용.
    //   AI 청각용 NoiseEmitted는 내지 않는다(적 총성·발소리를 다른 적이 듣지 않는 M3 규칙 유지). 시야는 재 폭풍 상한을 받는다.
    // - M7 (§1.4): 근접(EnemyIntent.Melee → EnemyMelee → 표적 IDamageable), 저격 섬광(EnemyIntent.Telegraph → 자식 SniperGlint),
    //   변이체 울음(EnemyVocalized, Audio 전용).
    // - MVP 마무리 F4 (§5.3, MF-Q16 B): 피격 부위를 덮는 유형 방어구(EnemyArmorModel, 적마다 내구도)가 있으면 Core ArmorRules로 관통 판정.
    // Update 경로 할당 없음.
    [RequireComponent(typeof(NavMeshAgent), typeof(CapsuleCollider), typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class EnemyAgent : MonoBehaviour, IDamageable, IAiFrameTickable
    {
        [SerializeField] private EnemyArchetypeSO archetype;
        [Tooltip("비면 Awake에서 FindFirstObjectByType 1회")]
        [SerializeField] private AiDirector director;
        [Tooltip("비면 경로 없음 (Idle 경계)")]
        [SerializeField] private PatrolRoute patrolRoute;
        [Tooltip("0 = 단독. 같은 값끼리 분대")]
        [SerializeField] private int squadId;
        [Tooltip("-1 = 가중치 무작위")]
        [SerializeField] private int loadoutIndex = -1;
        [Tooltip("0 = 무작위 (테스트·씬은 고정값)")]
        [SerializeField] private int seed;
        [SerializeField] private Transform eye;
        [SerializeField] private Transform muzzle;
        [Tooltip("사망 시 눕힐 시각 루트")]
        [SerializeField] private Transform bodyVisual;

        private NavMeshAgent nav;
        private CapsuleCollider capsule;
        private AiConfigSO config;
        private System.Random random;
        // MVP 마무리 F4 (§5.3, MF-Q16 B): 방어구 전용 난수 — random(무장·명중·루트 순서)을 밀지 않는다. 방어구 없는 유형은 null.
        private System.Random armorRandom;
        private ArmorRuleParams armorParams;
        private EnemyLoadout loadout;
        private bool hasLoadout;
        private bool registered;
        private bool warnedOffNavMesh;

        // 감각 결과 (사격용)
        private IPlayerTarget currentTarget;
        private float lastSenseTime;
        private bool lastVisible;
        private float visibleSince;
        private Vector3 lastTargetVelocity;

        // 자세
        private EnemyPosture appliedPosture = EnemyPosture.Stand;
        private Vector3 eyeStandLocal;
        private Vector3 muzzleStandLocal;
        private Vector3 bodyStandLocalPos;
        private Vector3 bodyStandLocalScale;

        // 경로 요청
        private bool hasRequestedPath;
        private Vector3 lastRequestedDestination;
        private float lastPathRequestTime = float.NegativeInfinity;

        // 발소리 리듬 (M6 Q12·Q13, Core 공용 규칙)
        private readonly StrideCadence footstepCadence = new StrideCadence();

        // M7 훅 (설계 §1.4·§4.4): 근접(AI-MUT)·섬광(AI-SNP)·울음.
        // 동작은 EnemyMelee·SniperGlint·각 전술(EnemyIntent.Melee·Telegraph)이 정한다.
        private EnemyMelee melee;                 // archetype.meleeDamage > 0일 때만
        private SniperGlint glint;                // 자식에 있으면 (저격수)
        private bool vocalizes;                   // 전술 = Mutant
        private bool wasEngagedForVocal;
        private float nextVocalAt = float.NegativeInfinity;
        private Transform meleeTargetRoot;        // 근접 표적 IDamageable 캐시 (Root가 바뀔 때만 다시 찾는다)
        private IDamageable meleeTargetDamageable;
        private bool meleeClosingHeld;            // 근접 유형: 표적 바로 앞이라 이동을 멈춘 상태 (리뷰 15)
        private Squad squadForVocal;              // 분대 경계 울음권 (리뷰 10). 단독 = null

        public int AgentId { get; private set; } = -1;
        public EnemyArchetypeSO Archetype => archetype;
        public EnemyBrain Brain { get; private set; }
        public EnemyState State => Brain != null ? Brain.State : (IsDead ? EnemyState.Dead : EnemyState.Idle);
        public EnemyBodyModel Body { get; private set; }
        // MVP 마무리 F4: 헬멧·방탄복 런타임 내구도 (유형에 없으면 HasArmor = false)
        public EnemyArmorModel Armor { get; private set; }
        public EnemyWeapon Weapon { get; private set; }
        public bool IsDead { get; private set; }
        public int SquadId => squadId;
        public float CapsuleHeight => capsule != null ? capsule.height : 0f;

        internal int SchedulerSlot { get; private set; } = -1;
        internal bool IsEngaged => Brain != null && Brain.IsEngaged;
        internal AiConfigSO Config => config;

        private void Awake()
        {
            nav = GetComponent<NavMeshAgent>();
            capsule = GetComponent<CapsuleCollider>();
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            if (archetype == null)
            {
                Debug.LogError($"[EnemyAgent] {name}: EnemyArchetypeSO가 연결되지 않았다.", this);
                enabled = false;
                return;
            }
            if (director == null)
                director = FindFirstObjectByType<AiDirector>();
            if (director == null || director.Config == null)
            {
                Debug.LogError($"[EnemyAgent] {name}: AiDirector(또는 그 AiConfigSO)가 없다.", this);
                enabled = false;
                return;
            }
            config = director.Config;

            if (eye == null)
                eye = transform;
            if (muzzle == null)
                muzzle = eye;
            eyeStandLocal = eye.localPosition;
            muzzleStandLocal = muzzle.localPosition;
            if (bodyVisual != null)
            {
                bodyStandLocalPos = bodyVisual.localPosition;
                bodyStandLocalScale = bodyVisual.localScale;
            }

            random = seed != 0 ? new System.Random(seed) : new System.Random();
            Body = new EnemyBodyModel(archetype, config);
            Body.Died += OnBodyDied;
            Armor = new EnemyArmorModel(archetype);
            armorParams = config.ArmorRuleParams;
            // 고정 seed면 방어구 난수도 같은 seed로 결정적 (별도 인스턴스라 random 순서와 무관)
            if (Armor.HasArmor)
                armorRandom = seed != 0 ? new System.Random(seed) : new System.Random();

            nav.updateRotation = false;   // 회전은 직접 (turnSpeedDegPerSec)
            nav.speed = archetype.patrolSpeed;

            // M7: 섬광 자식은 Awake에서 한 번만 찾는다 (매 프레임 검색 없음)
            glint = GetComponentInChildren<SniperGlint>(true);
            if (glint != null)
                glint.SetActive(false);
            if (archetype.meleeDamage > 0f)
                melee = new EnemyMelee(archetype.meleeWindupSec, archetype.meleeIntervalSec);
        }

        private void Start()
        {
            if (config == null)
                return;

            director.Register(this);
            if (AgentId < 0)
            {
                enabled = false;
                return;
            }
            registered = true;

            Squad squad = director.SquadFor(squadId);
            // 분대 순서 = 씬 계층 순서 (적 루트는 최상위라 루트 순서. Start 순서와 달리 실행마다 같다, 리뷰 12)
            Brain = new EnemyBrain(AgentId, archetype, config, director.World,
                patrolRoute != null ? patrolRoute.Points : null, squad, random, transform.GetSiblingIndex());
            vocalizes = Brain.TacticsKind == EnemyTacticsKind.Mutant;
            squadForVocal = squad;

            int index = EnemyWeapon.PickLoadout(archetype.loadouts, loadoutIndex, random);
            string problem = "무장 목록이 비었다";
            if (index >= 0 && EnemyWeapon.IsValidLoadout(archetype.loadouts[index], out problem))
            {
                loadout = archetype.loadouts[index];
                hasLoadout = true;
            }
            else if (!(archetype.meleeDamage > 0f && (archetype.loadouts == null || archetype.loadouts.Count == 0)))
            {
                // 근접 유형(변이체 AI-MUT, SYS-AI §7)은 무장 목록이 비어 있는 것이 정상 — 경고 생략. 목록이 있는데 못 쓰면 여전히 경고.
                Debug.LogWarning($"[EnemyAgent] {name}: 쓸 수 있는 무장이 없다 (비무장): {problem}", this);
            }

            lastSenseTime = Time.time;
            TryCreateWeapon();
        }

        // 살아 있는 채로 파괴·비활성화되면 AI에서 빠진다: 엄폐 예약·우회권 반납, 분대 탈퇴(리더 승계), director 등록 해제
        // (리뷰 5). 사망은 Die()가 이미 정리한다.
        // TODO(GDD-?): 비활성화 뒤 다시 켜는 것은 지원하지 않는다(다시 등록하지 않음). 필요해지면 재등록 경로를 만든다.
        private void OnDisable() => DetachFromAi();

        private void OnDestroy() => DetachFromAi();

        private void DetachFromAi()
        {
            if (!registered)
                return;
            registered = false;
            if (!IsDead)
                Brain?.Detach();
            if (director != null)
                director.Unregister(this);
        }

        // 틱 중 예외 (AiTickGuard가 로그를 남긴 뒤): 이 적만 멈추고 AI에서 뺀다.
        void IAiFrameTickable.OnTickFaulted()
        {
            enabled = false;   // → OnDisable → DetachFromAi
        }

        void IAiFrameTickable.FrameTick(float now, float dt) => FrameTick(now, dt);

        internal void AssignRegistration(int agentId, int schedulerSlot)
        {
            AgentId = agentId;
            SchedulerSlot = schedulerSlot;
        }

        // ── 감각 갱신 (감각 주기마다, 설계 §6) ─────────────────────

        // visibilityCapM: 재 폭풍 시야 상한 (AiDirector가 IAshStormInfo.VisibilityM을 넘긴다, 없으면 +∞ = 기존 그대로)
        internal void Sense(IPlayerTarget target, ILineOfSight los, float now, float visibilityCapM = float.PositiveInfinity)
        {
            if (IsDead || Brain == null || !isActiveAndEnabled)
                return;

            float dt = Mathf.Max(0f, now - lastSenseTime);
            lastSenseTime = now;
            currentTarget = target;

            var s = new EnemySenses
            {
                Now = now,
                SelfPosition = transform.position,
                SelfForward = FlatForward(),
                Arrived = ComputeArrived(),
                HiddenFromTarget = true,
            };

            bool visible = false;
            if (target != null && !target.IsDead)
            {
                s.TargetExists = true;
                float range = SightCone.EffectiveRange(archetype.sightRange, target.Posture, config, visibilityCapM);
                s.EffectiveSightRange = range;
                Vector3 eyePos = eye.position;
                Vector3 targetEye = target.EyePosition;
                Vector3 targetCenter = target.CenterPosition;
                Transform targetRoot = target.Root;

                visible = SightCone.IsVisible(eyePos, s.SelfForward, targetEye, targetCenter, archetype.sightAngle, range,
                    los, targetRoot, transform);
                if (visible)
                {
                    s.TargetVisible = true;
                    s.TargetPosition = target.Position;
                    s.TargetEyePosition = targetEye;
                    s.TargetCenter = targetCenter;
                    s.TargetVelocity = target.Velocity;
                    s.TargetDistance = Vector3.Distance(eyePos, targetCenter);
                    lastTargetVelocity = s.TargetVelocity;
                }
                // 숨은 상태(RDR Hide)일 때만: 표적 눈 → 내 눈이 뚫리면 엄폐 무효
                if (Brain.WantsHiddenCheck && los != null)
                    s.HiddenFromTarget = !los.CanSee(targetEye, eyePos, transform, targetRoot);
            }

            if (visible && !lastVisible)
                visibleSince = now;
            lastVisible = visible;

            Brain.Tick(s, dt);
        }

        internal void OnHeard(in NoiseEmitted noise, float now)
        {
            if (IsDead || Brain == null)
                return;
            Brain.OnHeard(noise.Position, noise.Kind, now);
        }

        // ── 의도 적용 (매 프레임) ─────────────────────────────

        internal void FrameTick(float now, float dt)
        {
            if (IsDead || Brain == null || !isActiveAndEnabled)
                return;
            // 표적 제공자(Unity 오브젝트)가 파괴됐으면 놓는다 — 감각 때 캐시한 참조로 MissingReferenceException (리뷰 8)
            if (currentTarget is UnityEngine.Object targetObject && targetObject == null)
            {
                currentTarget = null;
                lastVisible = false;
            }
            if (Weapon == null)
                TryCreateWeapon();
            else
                Weapon.Projectiles = director.Projectiles;   // 해제된 발사 서비스는 쓰지 않는다 (리뷰 9)

            EnemyIntent intent = Brain.Intent;
            ApplyPosture(intent.Posture);
            ApplyMovement(intent, now);
            TickFootsteps(dt);
            ApplyRotation(intent, dt);
            ApplyWeapon(intent, now, dt);
            ApplyMelee(intent, now);
            ApplyTelegraph(intent);
            TickVocal(now);
        }

        // ── M7 훅: 근접·섬광·울음 (설계 §1.4, Q23·Q24·Q26) ──────────

        // 의도(Melee) + 표적 수평 거리 ≤ meleeRangeM ∧ 발 높이 차 ≤ meleeMaxHeightDiffM → inRange, 시야 = 마지막 감각 갱신에서 보였음.
        // TODO(GDD-?) Q24: "눈 → 표적 중심 시야"는 감각 주기의 시야 판정(lastVisible)으로 대신한다(매 프레임 레이 없음).
        private void ApplyMelee(in EnemyIntent intent, float now)
        {
            if (melee == null)
                return;
            IPlayerTarget target = currentTarget;
            bool alive = target != null && !target.IsDead;
            bool inRange = intent.Melee && alive &&
                           EnemyMelee.InReach(transform.position, target.Position, archetype.meleeRangeM, config.meleeMaxHeightDiffM);

            switch (melee.Tick(now, inRange, alive && lastVisible))
            {
                case MeleeEvent.WindupStarted:
                    TryVocalize(EnemyVocalKind.MutantAttack, now);
                    break;
                case MeleeEvent.Strike:
                    if (alive)
                        Strike(target);
                    break;
            }
        }

        // 피해 = IDamageable(표적 루트) + DamageInfo(meleeDamage, 관통 0, 표적 중심, 방향, 거리, 부위 힌트).
        // 부위는 EnemyMelee.PickStrikePart(AiConfigSO 가중치: 흉부·복부·팔)가 정하고 PlayerHealth는 힌트를 그대로 쓴다(리뷰 1).
        private void Strike(IPlayerTarget target)
        {
            Transform root = target.Root;
            if (root == null)
                return;
            if (root != meleeTargetRoot)
            {
                meleeTargetRoot = root;
                meleeTargetDamageable = root.TryGetComponent(out IDamageable damageable) ? damageable : null;
            }
            if (meleeTargetDamageable == null)
                return;

            BodyPart part = EnemyMelee.PickStrikePart(random, config);
            Vector3 point = target.CenterPosition;
            Vector3 from = eye.position;
            Vector3 dir = point - from;
            float distance = dir.magnitude;
            dir = distance > 1e-4f ? dir / distance : transform.forward;
            meleeTargetDamageable.ApplyDamage(new DamageInfo(archetype.meleeDamage, 0, point, dir, distance, part));
        }

        // 섬광: 의도(Telegraph)가 있고 표적이 보일 때만 켜고 표적 눈 쪽으로.
        private void ApplyTelegraph(in EnemyIntent intent)
        {
            if (glint == null)
                return;
            IPlayerTarget target = currentTarget;
            bool on = intent.Telegraph && lastVisible && target != null && !target.IsDead;
            glint.SetActive(on);
            if (on)
                glint.Face(target.EyePosition, config.glintAngularSizeDeg);
        }

        // 울음(Mutant 전술만): 평시 → Alert/Combat 진입 순간 MutantAlert, 근접 준비 시작마다 MutantAttack.
        // 같은 적은 vocalCooldownSec 간격으로만 (두 종류 공용 쿨다운, TODO(GDD-?) Q26).
        // 경계 울음은 분대 단위(리뷰 10): 같은 분대에서 먼저 경계에 든 한 명만 운다(Squad.TryClaimAlertVocal, 같은 쿨다운).
        private void TickVocal(float now)
        {
            if (!vocalizes)
                return;
            EnemyState state = Brain.State;
            bool engaged = state == EnemyState.Alert || state == EnemyState.Combat;
            if (engaged && !wasEngagedForVocal)
                TryVocalize(EnemyVocalKind.MutantAlert, now);
            wasEngagedForVocal = engaged;
        }

        private void TryVocalize(EnemyVocalKind kind, float now)
        {
            if (!vocalizes || now < nextVocalAt)
                return;
            if (kind == EnemyVocalKind.MutantAlert && squadForVocal != null && squadForVocal.Count > 1 &&
                !squadForVocal.TryClaimAlertVocal(now, config.vocalCooldownSec))
                return;   // 분대의 다른 대원이 방금 울었다 — 내 쿨다운은 쓰지 않는다
            nextVocalAt = now + config.vocalCooldownSec;
            GameManager gm = GameManager.Instance;
            if (gm != null)
                gm.Events.Publish(new EnemyVocalized(eye.position, kind));
        }

        // ── 발소리 (M6 §11, Q12·Q13) ─────────────────────────

        // NavMeshAgent 수평 속도로 걸음 종류·보폭을 고른다(순수). 걸음이 없으면 false.
        // - 속도 < footstepMinSpeedMps(또는 0 이하·NaN) → 걸음 없음
        // - 웅크린 채 이동 → Crouch(걷기 보폭). TODO(GDD-?) Q13: 적 웅크림 보폭은 SO에 따로 없어 걷기 보폭을 쓴다.
        // - 속도 > footstepRunSpeedMps → Sprint(달리기 보폭), 그 밖 → Walk(걷기 보폭)
        public static bool TryPickFootstep(float horizontalSpeedMps, bool crouched, AiConfigSO config,
                                           out FootstepGait gait, out float strideM)
        {
            gait = FootstepGait.Walk;
            strideM = 0f;
            if (config == null || !(horizontalSpeedMps > 0f) || horizontalSpeedMps < config.footstepMinSpeedMps)
                return false;
            if (crouched)
            {
                gait = FootstepGait.Crouch;
                strideM = config.footstepStrideWalkM;
            }
            else if (horizontalSpeedMps > config.footstepRunSpeedMps)
            {
                gait = FootstepGait.Sprint;
                strideM = config.footstepStrideRunM;
            }
            else
            {
                strideM = config.footstepStrideWalkM;
            }
            return strideM > 0f;
        }

        // 이동 거리 = NavMeshAgent 수평 속도 × dt (Warp 순간이동은 걸음으로 치지 않는다). NavMesh 밖·멈춤 → 리듬 초기화.
        private void TickFootsteps(float dt)
        {
            if (!nav.enabled || !nav.isOnNavMesh)
            {
                footstepCadence.Reset();
                return;
            }
            Vector3 v = nav.velocity;
            v.y = 0f;
            float speed = v.magnitude;
            if (!TryPickFootstep(speed, appliedPosture == EnemyPosture.Crouch, config, out FootstepGait gait, out float strideM))
            {
                footstepCadence.Reset();
                return;
            }
            if (!footstepCadence.Tick(speed * dt, strideM))
                return;
            GameManager gm = GameManager.Instance;
            if (gm != null)
                gm.Events.Publish(new FootstepTaken(transform.position, gait, SoundSource.Enemy));
        }

        private void ApplyMovement(in EnemyIntent intent, float now)
        {
            if (!nav.enabled)
                return;
            if (!nav.isOnNavMesh)
            {
                // 시작 위치가 NavMesh 밖이면 조용히 제자리였다 → 한 번 경고 (리뷰 10)
                if (!warnedOffNavMesh)
                {
                    warnedOffNavMesh = true;
                    Debug.LogWarning($"[EnemyAgent] {name}: NavMesh 위에 있지 않아 움직이지 못한다 (위치 {transform.position}).", this);
                }
                return;
            }

            float speed;
            switch (intent.Move)
            {
                case EnemyMoveMode.Walk:
                    speed = archetype.patrolSpeed;
                    break;
                case EnemyMoveMode.Run:
                    speed = archetype.moveSpeed;
                    break;
                case EnemyMoveMode.Sprint:
                    speed = Body.SprintAllowed ? archetype.sprintSpeed : archetype.moveSpeed;
                    break;
                default:
                    speed = 0f;
                    break;
            }
            nav.speed = speed * Body.MoveSpeedMultiplier;

            bool hold = intent.Move == EnemyMoveMode.Hold || !intent.HasDestination;
            if (melee != null)
            {
                // 근접 유형(리뷰 15): 표적 바로 앞(수평 ≤ 사거리 × mutantHoldInsideRangeRatio)이면 감각 주기를 기다리지 않고 매 프레임 멈춘다
                // — 질주 중 감각 주기 사이에 표적 몸 안까지 밀고 들어가 겹치지 않게(몸 겹침 = 다른 적 시야 차단). 표적 위치는 몸 충돌용이라
                // 보이지 않아도 쓴다. 멈추는 순간 속도도 0(감속 미끄러짐 방지).
                IPlayerTarget target = currentTarget;
                if (!hold && intent.Melee && target != null && !target.IsDead &&
                    EnemyMelee.InReach(transform.position, target.Position,
                        archetype.meleeRangeM * config.mutantHoldInsideRangeRatio, config.meleeMaxHeightDiffM))
                    hold = true;
                if (hold && !meleeClosingHeld)
                    nav.velocity = Vector3.zero;
                meleeClosingHeld = hold;
            }
            nav.isStopped = hold;
            if (hold)
                return;

            // 목적지가 repathDistanceM 이상 바뀌고 직전 요청 뒤 repathMinIntervalSec 지났을 때만, 프레임 예산 안에서 (§1.8)
            float minSqr = config.repathDistanceM * config.repathDistanceM;
            bool changed = !hasRequestedPath || (intent.Destination - lastRequestedDestination).sqrMagnitude >= minSqr;
            bool intervalOk = !hasRequestedPath || now - lastPathRequestTime >= config.repathMinIntervalSec;
            if (changed && intervalOk && director.TryConsumePathRequest())
            {
                nav.SetDestination(intent.Destination);
                hasRequestedPath = true;
                lastRequestedDestination = intent.Destination;
                lastPathRequestTime = now;
            }
        }

        private void ApplyRotation(in EnemyIntent intent, float dt)
        {
            Vector3 dir;
            if (intent.Fire && lastVisible && currentTarget != null)
                dir = currentTarget.CenterPosition - transform.position;
            else if (intent.LookAtPoint)
                dir = intent.LookPoint - transform.position;
            else if (nav.enabled && nav.velocity.sqrMagnitude >
                     config.movingSpeedThresholdMps * config.movingSpeedThresholdMps)
                dir = nav.velocity;
            else
                return;

            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f)
                return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir),
                config.turnSpeedDegPerSec * dt);
        }

        private void ApplyWeapon(in EnemyIntent intent, float now, float dt)
        {
            if (Weapon == null)
                return;

            bool wantsFire = false;
            Vector3 aimPoint = intent.FireAt;
            float radius = 0f;

            // 사격 허용 조건 (설계 §1.4): 마지막 감각 갱신에서 보였고, 몸 방향 오차 ≤ fireMaxYawErrorDeg, 거리 ≤ engageMaxRangeM
            if (intent.Fire && lastVisible && currentTarget != null && !currentTarget.IsDead)
            {
                aimPoint = currentTarget.CenterPosition;
                Vector3 to = aimPoint - transform.position;
                to.y = 0f;
                float yawError = to.sqrMagnitude > 1e-6f ? Vector3.Angle(FlatForward(), to) : 0f;
                float distance = Vector3.Distance(muzzle.position, aimPoint);
                if (yawError <= config.fireMaxYawErrorDeg && distance <= archetype.engageMaxRangeM)
                {
                    wantsFire = true;
                    bool selfMoving = nav.enabled && nav.velocity.sqrMagnitude >
                                      config.movingSpeedThresholdMps * config.movingSpeedThresholdMps;
                    bool targetMoving = lastTargetVelocity.sqrMagnitude >
                                        config.movingSpeedThresholdMps * config.movingSpeedThresholdMps;
                    radius = AimError.Radius(archetype, distance, now - visibleSince, selfMoving, targetMoving,
                        Weapon.ShotsInBurst);
                }
            }

            int fired = Weapon.Tick(now, dt, wantsFire, muzzle.position, aimPoint, radius, transform);
            // 총성 소리 (M6 Q14): Audio 전용 ShotFired. NoiseEmitted는 내지 않는다(다른 적은 적 총성을 듣지 않는다, M3 규칙).
            if (fired > 0)
            {
                GameManager gm = GameManager.Instance;
                if (gm != null)
                    gm.Events.Publish(new ShotFired(muzzle.position, Weapon.Item.Definition, SoundSource.Enemy));
            }

            // 숨은 동안 탄 비율이 낮으면 미리 재장전 (RDR tacticalReloadBelow, 0이면 안 함)
            if (intent.Reload && Weapon.AmmoRatio < archetype.tacticalReloadBelow)
                Weapon.TryReload();
        }

        private void ApplyPosture(EnemyPosture posture)
        {
            if (posture == appliedPosture)
                return;
            appliedPosture = posture;

            bool crouch = posture == EnemyPosture.Crouch;
            float height = crouch ? config.crouchHeightM : config.standHeightM;
            capsule.height = height;
            capsule.center = new Vector3(0f, height * 0.5f, 0f);

            float eyeOffset = crouch ? config.crouchEyeHeightM - config.standEyeHeightM : 0f;
            if (eye != transform)
                eye.localPosition = eyeStandLocal + Vector3.up * eyeOffset;
            if (muzzle != transform && muzzle != eye)
                muzzle.localPosition = muzzleStandLocal + Vector3.up * eyeOffset;

            if (bodyVisual != null && config.standHeightM > 0f)
            {
                float ratio = height / config.standHeightM;
                bodyVisual.localScale = new Vector3(bodyStandLocalScale.x, bodyStandLocalScale.y * ratio, bodyStandLocalScale.z);
                bodyVisual.localPosition = new Vector3(bodyStandLocalPos.x, bodyStandLocalPos.y * ratio, bodyStandLocalPos.z);
            }
        }

        // ── 피격·사망 ────────────────────────────────────

        public void ApplyDamage(in DamageInfo info)
        {
            if (IsDead || Body == null)
                return;

            Vector3 local = transform.InverseTransformPoint(info.Point);
            EnemyHitRegion region = EnemyHitResolver.Resolve(local.y, capsule.height, config);
            // MVP 마무리 F4 §5.3: 그 부위를 덮는 방어구가 있으면 관통 판정(관통 = 그대로, 비관통 = 둔탁 30% + 내구도 감소)
            ArmorHitResult armorHit = Armor.Absorb(region, info.Penetration, info.Amount, armorParams, armorRandom);
            Body.ApplyHit(region, armorHit.BodyDamage);   // 사망이면 Died → Die()

            // 추정 사격 위치 (DamageInfo만으로 계산, 사수 참조 불필요)
            // TODO(GDD-?): 아군 탄에 맞아도 그 총구 쪽을 LKP로 삼는다(리뷰 11). DamageInfo에 사수 구분이 생기면 아군 피격은
            //  경계만 하고 LKP는 바꾸지 않는 식으로 나눈다.
            if (!IsDead)
                Brain?.OnDamaged(info.Point - info.Direction * info.DistanceM, Time.time);
        }

        private void OnBodyDied(EnemyHitRegion cause) => Die();

        private void Die()
        {
            if (IsDead)
                return;
            IsDead = true;
            footstepCadence.Reset();
            if (glint != null)
                glint.SetActive(false);
            melee?.Reset();

            Brain?.OnDied();
            if (nav.enabled)
            {
                if (nav.isOnNavMesh)
                    nav.isStopped = true;
                nav.enabled = false;
            }
            if (registered && director != null)
                director.Unregister(this);
            registered = false;

            LayDown();

            if (Weapon == null && hasLoadout)
                Weapon = new EnemyWeapon(loadout, archetype.spareMagazines, archetype, null, random);
            ItemInstance[] items = CorpseLootBuilder.Build(Weapon, archetype.loot, random ?? new System.Random());
            // 무기·탄창은 이제 시체 컨테이너 소유다. 죽은 적이 같은 인스턴스를 계속 들고 있지 않게 놓는다 (리뷰 15)
            Weapon = null;

            GameManager gm = GameManager.Instance;
            if (gm != null)
                gm.Events.Publish(new EnemyKilled(gameObject, archetype.corpseDisplayName, items, transform.position, archetype.id));

            enabled = false;   // Update 중지. 콜라이더는 남아 탄을 막고 상호작용 레이캐스트가 맞힌다.
        }

        // 시각 루트를 90° 눕히고 캡슐을 Z축으로 (설계 §1.9)
        private void LayDown()
        {
            float radius = capsule.radius;
            capsule.direction = 2;
            capsule.height = config != null ? config.standHeightM : capsule.height;
            capsule.center = new Vector3(0f, radius, 0f);
            if (bodyVisual != null)
            {
                bodyVisual.localScale = bodyStandLocalScale;
                bodyVisual.localRotation = Quaternion.Euler(90f, 0f, 0f);
                bodyVisual.localPosition = new Vector3(bodyStandLocalPos.x, radius, bodyStandLocalPos.z);
            }
        }

        // ── 도우미 ─────────────────────────────────────

        private void TryCreateWeapon()
        {
            if (Weapon != null || !hasLoadout || director == null || director.Projectiles == null)
                return;
            Weapon = new EnemyWeapon(loadout, archetype.spareMagazines, archetype, director.Projectiles, random);
        }

        private bool ComputeArrived()
        {
            if (!nav.enabled || !nav.isOnNavMesh)
                return true;
            if (nav.pathPending)
                return false;
            if (!nav.hasPath)
                return true;
            return nav.remainingDistance <= nav.stoppingDistance;
        }

        private Vector3 FlatForward()
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        }
    }
}
