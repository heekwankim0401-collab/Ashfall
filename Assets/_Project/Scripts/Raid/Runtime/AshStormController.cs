using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // 재 폭풍 (§8.3 "AshStorm", M6 설계 §1.5, Q23~Q28). 레이드 씬 최상위 "AshStorm"에 하나. 정적 인스턴스 없음.
    // - OnEnable: IAshStormInfo 등록 / OnDisable: 해제. 레이드 밖에는 등록되지 않는다 = 소비자는 "폭풍 없음".
    // - 시각 = 레이드 경과 시간(RaidController.ElapsedSec): 일시정지·종료 정지 중 멈추고 디버그 AdvanceTime을 따른다(Q28).
    //   RaidController는 고치지 않고 읽기만 한다. 세션이 생긴 첫 프레임에 시작 시각을 정한다(레이드당 1회, Q23).
    // - Update: AshStormModel.Evaluate → 단계가 바뀌면 AshStormChanged(건너뛴 단계도 순서대로 한 번씩) → presenter.Apply.
    // 규칙은 AshStormModel·AshStormSchedule·FogMath(순수)에 있다.
    public sealed class AshStormController : MonoBehaviour, IAshStormInfo
    {
        [SerializeField] private RaidController raid;
        [SerializeField] private AshStormConfigSO config;
        [Tooltip("맑을 때 가시거리(clearVisibilityM) — 폭풍 시야가 여기서 30m로 줄어든다")]
        [SerializeField] private AtmosphereConfigSO atmosphere;
        [SerializeField] private AshStormPresenter presenter;
        [Tooltip("폭풍 시작 시각 시드. 0 = 매번 무작위")]
        [SerializeField] private int seed;

        private bool forceStartRequested;
        private bool warnedNoRaid;

        // ── 테스트·디버그 ──
        public AshStormModel Model { get; private set; }
        public AshStormPresenter Presenter => presenter;
        public AshStormConfigSO Config => config;
        public RaidController Raid => raid;
        public int Seed => seed;
        // 시작 시각 (레이드 경과 초). 아직 정하지 않았으면 NaN
        public float StartSec => Model != null ? Model.StartSec : float.NaN;

        // ── IAshStormInfo ──
        public AshStormPhase Phase => Model != null ? Model.Phase : AshStormPhase.Pending;
        public float Intensity => Model != null ? Model.Intensity : 0f;
        public float VisibilityM => Model != null ? Model.VisibilityM : float.PositiveInfinity;
        public float RadiationPerSec => Model != null ? Model.RadiationPerSec : 0f;

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[AshStormController] AshStormConfigSO가 연결되지 않았다. 재 폭풍 없음.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (config == null || GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Register<IAshStormInfo>(this);
            GameManager.Instance.Events.Subscribe<AshStormStartRequested>(OnStartRequested);
        }

        private void OnDisable()
        {
            if (GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Unregister<IAshStormInfo>(this);
            GameManager.Instance.Events.Unsubscribe<AshStormStartRequested>(OnStartRequested);
        }

        // M7 7D 스모크(Core SmokeRunner, Q51): 개발 빌드·에디터에서만 강제 시작. 릴리스는 무시한다.
        private void OnStartRequested(AshStormStartRequested evt)
        {
            if (!Debug.isDebugBuild)
                return;
            if (!ForceStartNow())
                Debug.Log($"[AshStormController] 강제 시작 요청 — 이미 시작했다 ({Phase}).", this);
        }

        private void Update()
        {
            if (Model == null && !TryCreateModel())
                return;

            AshStormPhase before = Model.Phase;
            if (Model.Evaluate(raid.ElapsedSec))
                PublishPhases(before, Model.Phase);
            if (presenter != null)
                presenter.Apply(Model.Intensity, Model.VisibilityM);
        }

        // 디버그·테스트: 지금(현재 레이드 경과 시간) 시작한다. 이미 시작했으면 false. 세션 전이면 세션이 생길 때 시작한다.
        public bool ForceStartNow()
        {
            if (Model == null)
            {
                forceStartRequested = true;
                return true;
            }
            return Model.Reschedule(raid.ElapsedSec);
        }

        [ContextMenu("Debug/재 폭풍 지금 시작")]
        private void DebugStartNow()
        {
            if (!ForceStartNow())
                Debug.Log($"[AshStormController] 이미 시작했다 ({Phase}).", this);
        }

        // 세션이 생긴 첫 프레임에 시작 시각을 정한다 (레이드당 1회).
        private bool TryCreateModel()
        {
            if (raid == null)
            {
                if (!warnedNoRaid)
                    Debug.LogWarning("[AshStormController] RaidController가 연결되지 않아 재 폭풍이 오지 않는다.", this);
                warnedNoRaid = true;
                return false;
            }
            if (raid.Session == null)
                return false;

            var random = new System.Random(seed != 0 ? seed : Random.Range(1, int.MaxValue));
            float start = AshStormSchedule.PickStartSec(config.startWindowSec, random);
            if (forceStartRequested)
                start = raid.ElapsedSec;
            forceStartRequested = false;
            float clear = atmosphere != null ? atmosphere.clearVisibilityM : float.PositiveInfinity;
            Model = new AshStormModel(config, clear, start);
            // 시작 시각은 개발 빌드·에디터에서만 로그로 남긴다(릴리스 Player.log로 폭풍 시각을 알 수 없게, M6 리뷰 낮음 7)
            if (Debug.isDebugBuild)
                Debug.Log($"[AshStormController] 재 폭풍 시작 시각 {start:0}초 (seed {seed})", this);
            return true;
        }

        // 한 프레임에 여러 단계를 건너뛰면(AdvanceTime·긴 프레임) 사이 단계도 순서대로 한 번씩 알린다.
        // 되감기(시간이 줄어드는 경우, 정상 흐름에는 없음)는 새 단계만 알린다.
        private static void PublishPhases(AshStormPhase from, AshStormPhase to)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null)
                return;
            if (to < from)
            {
                gm.Events.Publish(new AshStormChanged(to));
                return;
            }
            for (AshStormPhase p = from + 1; p <= to; p++)
                gm.Events.Publish(new AshStormChanged(p));
        }
    }
}
