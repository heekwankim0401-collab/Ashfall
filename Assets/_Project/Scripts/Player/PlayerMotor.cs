using Ashfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashfall.Player
{
    // §4.1 1인칭 이동·시점. 규칙은 LocomotionModel·StaminaModel에 있고 여기서는 입력과 CharacterController만 다룬다.
    // 구조: Player(이 컴포넌트, 좌우 회전) → eye(눈높이·상하 회전·기울이기) → Camera
    // M2: 소지 무게는 CarriedWeightChanged(Inventory)로, 체력 패널티는 같은 오브젝트의 PlayerHealth에서 받는다.
    //     화면이 열려 있거나(ScreenStateChanged) 사망하면 입력을 무시하고 중력만 적용한다 (설계 D9).
    // M3: AI가 보는 표적(IPlayerTarget)으로 등록하고, 발소리·점프 소음을 NoiseEmitted로 낸다 (§4.1, §7 청각).
    // M4: 레이드 시작 스폰 배치(IPlayerPlacement)를 받는다 (M4 설계 §1.1). Raid는 Transform을 직접 건드리지 않는다.
    // M6: 오디오 발소리(§11). Move 뒤 실제 수평 이동 거리(접지 중만)를 보폭(StrideCadence)으로 나눠 걸음마다 FootstepTaken(발 위치, 걸음, Player).
    //     걸음 = 자세(엎드림 > 웅크림) > 질주 > 걷기 (SelectGait). AI 청각용 NoiseEmitted(Footstep) 0.5초 펄스는 그대로.
    // M7: 에너지 0이면 스태미나 재생 × 0.5 (StaminaRegenMultiplier, §4.3 STAT-HUNG).
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour, IRecoilReceiver, IPlayerTarget, IPlayerPlacement
    {
        [SerializeField] private PlayerConfigSO config;
        [SerializeField] private Transform eye;
        // 인벤토리가 없는 씬(M1)용 초기값. 인벤토리가 있으면 CarriedWeightChanged로 덮어쓴다.
        [SerializeField] private float carriedWeightKg;

        private CharacterController controller;
        private PlayerHealth health;
        private InputAction move, look, sprint, crouch, prone, leanLeft, leanRight, jump;
        private FootstepNoiseModel footsteps;
        private readonly StrideCadence stride = new StrideCadence();

        private float pitch;
        private float lean;          // -1(왼쪽) ~ 1(오른쪽)
        private float eyeHeight;
        private float verticalVelocity;
        private bool screenOpen;

        public LocomotionModel Locomotion { get; private set; }
        public StaminaModel Stamina { get; private set; }
        public bool IsSprinting { get; private set; }
        public float CarriedWeightKg => carriedWeightKg;

        // 체력 상태 패널티 (PlayerHealth가 없으면 정상)
        public ConditionModifiers Condition =>
            health != null && health.Body != null ? health.Body.Modifiers : ConditionModifiers.Healthy;

        // M7 §4.3 STAT-HUNG: 에너지 0(허기)이면 스태미나 재생 배율 (PlayerHealth의 SurvivalModel을 같은 어셈블리에서 직접 읽는다). 없으면 1.
        public float StaminaRegenMultiplier =>
            health != null && health.Survival != null && health.Survival.IsStarving ? config.starvingStaminaRegenMultiplier : 1f;

        // 화면 열림·사망으로 게임플레이 입력을 받지 않는 중
        public bool InputBlocked => screenOpen || (health != null && health.IsDead);

        // 이번 프레임 소음 반경 (이동 + 점프). AI 청각에는 NoiseEmitted로 따로 낸다 (M3).
        public float CurrentNoiseRadius { get; private set; }

        // M6: 지금 걸음 종류 (발소리 음량·들리는 거리)
        public FootstepGait CurrentGait => SelectGait(Posture, IsSprinting);

        // M6 순수 규칙: 걸음 = 자세(엎드림 > 웅크림) > 질주 > 걷기.
        public static FootstepGait SelectGait(TargetPosture posture, bool sprinting)
        {
            switch (posture)
            {
                case TargetPosture.Prone: return FootstepGait.Prone;
                case TargetPosture.Crouching: return FootstepGait.Crouch;
                default: return sprinting ? FootstepGait.Sprint : FootstepGait.Walk;
            }
        }

        // M6 걸음별 보폭 (PlayerConfigSO §3.6, TODO(GDD-?))
        public static float StrideLength(PlayerConfigSO config, FootstepGait gait)
        {
            if (config == null)
                return 0f;
            switch (gait)
            {
                case FootstepGait.Sprint: return config.strideSprintM;
                case FootstepGait.Crouch: return config.strideCrouchM;
                case FootstepGait.Prone: return config.strideProneM;
                default: return config.strideWalkM;
            }
        }

        // ── IPlayerTarget (AI 감각, M3) ──
        public Transform Root => transform.root;
        public Vector3 Position => transform.position;
        public Vector3 EyePosition => eye != null ? eye.position : transform.position;
        public Vector3 CenterPosition => transform.TransformPoint(controller.center);
        public Vector3 Velocity => controller.velocity;
        public TargetPosture Posture => Locomotion != null ? Locomotion.Posture : TargetPosture.Standing;
        public bool IsDead => health != null && health.IsDead;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<PlayerHealth>();
            if (config == null)
            {
                Debug.LogError("[PlayerMotor] PlayerConfigSO가 연결되지 않았다.", this);
                enabled = false;
                return;
            }
            Locomotion = new LocomotionModel(config);
            Stamina = new StaminaModel(config);
            footsteps = new FootstepNoiseModel(config);
            eyeHeight = Locomotion.EyeHeight;
            ApplyCapsule();

            var map = InputSystem.actions.FindActionMap("Player", throwIfNotFound: true);
            move = map.FindAction("Move", true);
            look = map.FindAction("Look", true);
            sprint = map.FindAction("Sprint", true);
            crouch = map.FindAction("Crouch", true);
            prone = map.FindAction("Prone", true);
            leanLeft = map.FindAction("LeanLeft", true);
            leanRight = map.FindAction("LeanRight", true);
            jump = map.FindAction("Jump", true);
            map.Enable();
        }

        private void OnEnable()
        {
            // 커서는 UI(GameUIRoot)가 관리한다. UI가 없는 M1 씬을 위해 시작 잠금만 둔다.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (GameManager.Instance != null)
            {
                GameManager.Instance.Services.Register<IPlayerTarget>(this);
                GameManager.Instance.Services.Register<IPlayerPlacement>(this);
                GameManager.Instance.Events.Subscribe<CarriedWeightChanged>(OnCarriedWeightChanged);
                GameManager.Instance.Events.Subscribe<ScreenStateChanged>(OnScreenStateChanged);
            }
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            footsteps?.Reset();
            stride.Reset();
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Services.Unregister<IPlayerTarget>(this);
                GameManager.Instance.Services.Unregister<IPlayerPlacement>(this);
                GameManager.Instance.Events.Unsubscribe<CarriedWeightChanged>(OnCarriedWeightChanged);
                GameManager.Instance.Events.Unsubscribe<ScreenStateChanged>(OnScreenStateChanged);
            }
        }

        private void OnCarriedWeightChanged(CarriedWeightChanged evt) => carriedWeightKg = evt.TotalKg;
        private void OnScreenStateChanged(ScreenStateChanged evt) => screenOpen = evt.Screen != ScreenKind.None;

        public void AddRecoil(float pitchDeg, float yawDeg)
        {
            if (config == null)
                return;
            pitch = Mathf.Clamp(pitch - pitchDeg, -config.pitchLimitDeg, config.pitchLimitDeg);
            transform.Rotate(0f, yawDeg, 0f);
        }

        // ── IPlayerPlacement (M4 레이드 스폰 배치) ──
        // CharacterController가 켜진 채로 transform을 옮기면 다음 Move가 이전 위치로 되돌릴 수 있어
        // 끄고 → 옮기고 → 되돌린 뒤 물리에 즉시 반영한다. 시선은 수평(pitch 0), 기울이기·낙하 속도·발소리 누적 초기화.
        public void PlaceAt(Vector3 feetPosition, float yawDeg)
        {
            bool controllerWasEnabled = controller != null && controller.enabled;
            if (controller != null)
                controller.enabled = false;
            transform.SetPositionAndRotation(feetPosition, Quaternion.Euler(0f, yawDeg, 0f));
            if (controller != null)
                controller.enabled = controllerWasEnabled;
            Physics.SyncTransforms();

            pitch = 0f;
            lean = 0f;
            verticalVelocity = 0f;
            footsteps?.Reset();
            stride.Reset();
            if (eye != null && Locomotion != null)
            {
                eye.localPosition = new Vector3(0f, eyeHeight, 0f);
                eye.localRotation = Quaternion.identity;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            bool blocked = InputBlocked;
            ConditionModifiers condition = Condition;

            if (!blocked)
            {
                UpdateLook();
                if (crouch.WasPressedThisFrame()) { Locomotion.ToggleCrouch(); ApplyCapsule(); }
                if (prone.WasPressedThisFrame()) { Locomotion.ToggleProne(); ApplyCapsule(); }
            }

            Vector2 input = blocked ? Vector2.zero : Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            bool moving = input.sqrMagnitude > 0f;

            IsSprinting = !blocked && sprint.IsPressed()
                          && Locomotion.CanSprint(moving, carriedWeightKg, Stamina, condition);
            Stamina.Tick(dt, IsSprinting, moving, StaminaRegenMultiplier);
            CurrentNoiseRadius = Locomotion.NoiseRadius(moving, IsSprinting);
            // 발소리: 이동 중 noisePulseIntervalSec마다 (SYS-PLR-01 §4.1 소음 반경 → §7 청각)
            if (footsteps.Tick(dt, CurrentNoiseRadius))
                PublishNoise(CurrentNoiseRadius, NoiseKind.Footstep);

            float gravity = Physics.gravity.y;
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = config.groundStickSpeed;

            if (!blocked && jump.WasPressedThisFrame() && Locomotion.CanJump(controller.isGrounded)
                && Stamina.TrySpend(config.jumpStaminaCost))
            {
                verticalVelocity = Locomotion.JumpVelocity(gravity);
                CurrentNoiseRadius = Mathf.Max(CurrentNoiseRadius, Locomotion.JumpNoise);
                PublishNoise(Locomotion.JumpNoise, NoiseKind.Jump);
            }
            verticalVelocity += gravity * dt;

            Vector3 horizontal = (transform.right * input.x + transform.forward * input.y)
                                 * Locomotion.Speed(IsSprinting, carriedWeightKg, condition);
            Vector3 before = transform.position;
            controller.Move((horizontal + Vector3.up * verticalVelocity) * dt);
            TickStride(before, moving && !blocked);

            UpdateEye(dt, blocked);
        }

        // M6 발소리 (§11): 실제로 움직인 수평 거리로 보폭을 센다(벽에 막히면 걸음 없음). 공중에서는 세지 않고 누적을 유지한다
        // (경사·계단의 isGrounded 깜빡임에 리듬이 끊기지 않게). 이동 입력이 없거나(밀림·미끄러짐) 멈추면 StrideCadence가 리셋한다.
        private void TickStride(Vector3 before, bool walking)
        {
            if (!controller.isGrounded)
                return;
            Vector3 delta = transform.position - before;
            delta.y = 0f;
            FootstepGait gait = CurrentGait;
            if (stride.Tick(walking ? delta.magnitude : 0f, StrideLength(config, gait)) && GameManager.Instance != null)
                GameManager.Instance.Events.Publish(new FootstepTaken(transform.position, gait, SoundSource.Player));
        }

        private void UpdateLook()
        {
            Vector2 delta = look.ReadValue<Vector2>() * config.lookSensitivity;
            transform.Rotate(0f, delta.x, 0f);
            pitch = Mathf.Clamp(pitch - delta.y, -config.pitchLimitDeg, config.pitchLimitDeg);
        }

        // 눈높이는 자세에 맞춰 부드럽게, 기울이기(Q/E 누르고 있는 동안)는 15° 회전 + 좌우 이동.
        private void UpdateEye(float dt, bool blocked)
        {
            float leanTarget = blocked ? 0f : (leanRight.IsPressed() ? 1f : 0f) - (leanLeft.IsPressed() ? 1f : 0f);
            lean = Mathf.MoveTowards(lean, leanTarget, config.leanSpeed * dt);
            eyeHeight = Mathf.MoveTowards(eyeHeight, Locomotion.EyeHeight, config.stanceChangeSpeed * dt);

            eye.localPosition = new Vector3(lean * config.leanOffsetM, eyeHeight, 0f);
            eye.localRotation = Quaternion.Euler(pitch, 0f, -lean * config.leanAngleDeg);
        }

        // 발 위치에서 낸다. 반경 0 이하는 내지 않는다.
        private void PublishNoise(float radiusM, NoiseKind kind)
        {
            if (radiusM > 0f && GameManager.Instance != null)
                GameManager.Instance.Events.Publish(new NoiseEmitted(transform.position, radiusM, kind));
        }

        private void ApplyCapsule()
        {
            float h = Locomotion.CapsuleHeight;
            controller.height = h;
            controller.center = new Vector3(0f, h * 0.5f, 0f);
        }
    }
}
