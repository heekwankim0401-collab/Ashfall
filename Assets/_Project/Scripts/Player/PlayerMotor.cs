using Ashfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashfall.Player
{
    // §4.1 1인칭 이동·시점. 규칙은 LocomotionModel·StaminaModel에 있고 여기서는 입력과 CharacterController만 다룬다.
    // 구조: Player(이 컴포넌트, 좌우 회전) → eye(눈높이·상하 회전·기울이기) → Camera
    // M2: 소지 무게는 CarriedWeightChanged(Inventory)로, 체력 패널티는 같은 오브젝트의 PlayerHealth에서 받는다.
    //     화면이 열려 있거나(ScreenStateChanged) 사망하면 입력을 무시하고 중력만 적용한다 (설계 D9).
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour, IRecoilReceiver
    {
        [SerializeField] private PlayerConfigSO config;
        [SerializeField] private Transform eye;
        // 인벤토리가 없는 씬(M1)용 초기값. 인벤토리가 있으면 CarriedWeightChanged로 덮어쓴다.
        [SerializeField] private float carriedWeightKg;

        private CharacterController controller;
        private PlayerHealth health;
        private InputAction move, look, sprint, crouch, prone, leanLeft, leanRight, jump;

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

        // 화면 열림·사망으로 게임플레이 입력을 받지 않는 중
        public bool InputBlocked => screenOpen || (health != null && health.IsDead);

        // TODO(M3): AI 청각이 쓰도록 이벤트로 낸다. 지금은 값만 노출한다.
        public float CurrentNoiseRadius { get; private set; }

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
                GameManager.Instance.Events.Subscribe<CarriedWeightChanged>(OnCarriedWeightChanged);
                GameManager.Instance.Events.Subscribe<ScreenStateChanged>(OnScreenStateChanged);
            }
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (GameManager.Instance != null)
            {
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
            Stamina.Tick(dt, IsSprinting, moving);
            CurrentNoiseRadius = Locomotion.NoiseRadius(moving, IsSprinting);

            float gravity = Physics.gravity.y;
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = config.groundStickSpeed;

            if (!blocked && jump.WasPressedThisFrame() && Locomotion.CanJump(controller.isGrounded)
                && Stamina.TrySpend(config.jumpStaminaCost))
            {
                verticalVelocity = Locomotion.JumpVelocity(gravity);
                CurrentNoiseRadius = Mathf.Max(CurrentNoiseRadius, Locomotion.JumpNoise);
            }
            verticalVelocity += gravity * dt;

            Vector3 horizontal = (transform.right * input.x + transform.forward * input.y)
                                 * Locomotion.Speed(IsSprinting, carriedWeightKg, condition);
            controller.Move((horizontal + Vector3.up * verticalVelocity) * dt);

            UpdateEye(dt, blocked);
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

        private void ApplyCapsule()
        {
            float h = Locomotion.CapsuleHeight;
            controller.height = h;
            controller.center = new Vector3(0f, h * 0.5f, 0f);
        }
    }
}
