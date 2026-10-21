using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Player
{
    // SYS-HP-01 플레이어 체력의 얇은 브리지. 규칙은 BodyHealthModel·MedicalUseModel에 있다.
    // - IDamageable: 캡슐 피격 지점으로 부위를 판정해 ApplyHit (설계 D3)
    // - IBodyStatusView 등록 (RULE-05 H 화면), 모델 변화 → 버스 이벤트
    // - MedicalUseRequested → 사용 시간 → MedicalUseCompleted
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private PlayerConfigSO config;

        // 엔진 상수(게임 수치 아님): PhysX PxControllerDesc::scaleCoeff 기본값. Unity CharacterController의 쿼리용 캡슐은
        // height·radius의 0.8배다(4단계 PlayMode 측정: 높이 1.8·반지름 0.3 → 1.44·0.24). 이걸 무시하면 머리 구간(≥0.87)이
        // 실제 캡슐 꼭대기 몇 cm만 남아 사실상 맞지 않는다.
        private const float PhysicsActorScale = 0.8f;

        private CharacterController controller;
        private MedicalUseModel medical;

        public BodyHealthModel Body { get; private set; }
        public MedicalUseModel Medical => medical;
        public bool IsDead => Body != null && Body.IsDead;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (config == null)
            {
                Debug.LogError("[PlayerHealth] PlayerConfigSO가 연결되지 않았다.", this);
                enabled = false;
                return;
            }
            Body = new BodyHealthModel(config, new System.Random());
            medical = new MedicalUseModel(Body);
            Body.StatusChanged += OnStatusChanged;
            Body.ModifiersChanged += OnModifiersChanged;
            Body.Died += OnDied;
        }

        private void OnEnable()
        {
            if (Body == null || GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Register<IBodyStatusView>(Body);
            GameManager.Instance.Events.Subscribe<MedicalUseRequested>(OnMedicalUseRequested);
        }

        private void OnDisable()
        {
            if (Body == null || GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Unregister<IBodyStatusView>(Body);
            GameManager.Instance.Events.Unsubscribe<MedicalUseRequested>(OnMedicalUseRequested);
        }

        public void ApplyDamage(in DamageInfo info)
        {
            // config 누락으로 꺼진 경우(Body 없음)에도 IDamageable로는 계속 불린다.
            if (Body == null || IsDead)
                return;

            // 발 = y 0 기준 로컬 좌표로 바꿔 캡슐 높이 비율로 부위를 고른다.
            // 레이캐스트가 맞히는 것은 PhysX 캐릭터 컨트롤러의 kinematic actor이고, 이 캡슐은 컨트롤러 크기의
            // PhysicsActorScale배다. 중심 기준으로 되돌려 컨트롤러 크기(height·radius) 기준 좌표로 판정한다.
            Vector3 local = (transform.InverseTransformPoint(info.Point) - controller.center) / PhysicsActorScale;
            local.y += controller.height * 0.5f;
            BodyPart part = BodyPartResolver.Resolve(local, controller.height, controller.radius, config);
            Body.ApplyHit(part, info.Amount);
            LogHit(part, info.Amount);
        }

        // RULE-05: 피해 수치를 콘솔에 상시 찍지 않는다. 디버그할 때만 스크립팅 심볼 ASHFALL_DEBUG_DAMAGE를 켠다.
        [System.Diagnostics.Conditional("ASHFALL_DEBUG_DAMAGE")]
        private void LogHit(BodyPart part, float amount) =>
            Debug.Log($"[PlayerHealth] 피격 {part}: 피해 {amount}", this);

        private void Update()
        {
            float dt = Time.deltaTime;
            Body.Tick(dt);

            MedicalItem item = medical.Current;
            BodyPart target = medical.Target;
            if (medical.Tick(dt))
                Publish(new MedicalUseCompleted(item, target));
        }

        private void OnMedicalUseRequested(MedicalUseRequested evt)
        {
            if (evt.Item == null)
                return;

            switch (medical.TryStart(evt.Item))
            {
                case MedicalStartResult.Started:
                    Publish(new MedicalUseStarted(evt.Item, medical.Target, medical.RemainingSec));
                    break;
                case MedicalStartResult.NotNeeded:
                    Reject(evt.Item, MedicalUseFailReason.NotNeeded);
                    break;
                case MedicalStartResult.Busy:
                    Reject(evt.Item, MedicalUseFailReason.Busy);
                    break;
                case MedicalStartResult.Dead:
                    Reject(evt.Item, MedicalUseFailReason.Dead);
                    break;
                case MedicalStartResult.Depleted:
                    Reject(evt.Item, MedicalUseFailReason.Depleted);
                    break;
            }
        }

        private void Reject(MedicalItem item, MedicalUseFailReason reason)
        {
            Debug.Log($"[PlayerHealth] 의약품 사용 거부 {item}: {reason}");
            Publish(new MedicalUseRejected(item, reason));
        }

        private void OnStatusChanged() => Publish(new PlayerBodyStatusChanged());
        private void OnModifiersChanged() => Publish(new PlayerConditionChanged(Body.Modifiers));

        private void OnDied(BodyPart cause)
        {
            Debug.Log($"[PlayerHealth] 사망: {cause}");
            Publish(new PlayerDied(cause));
        }

        private static void Publish<T>(in T evt) where T : struct, IGameEvent
        {
            if (GameManager.Instance != null)
                GameManager.Instance.Events.Publish(evt);
        }
    }
}
