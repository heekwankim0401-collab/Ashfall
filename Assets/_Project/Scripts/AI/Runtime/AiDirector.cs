using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ashfall.Core;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ashfall.AI
{
    // 씬당 1개의 AI 관리자 (M3 설계 §5.1). 정적 Instance 없음(싱글톤은 GameManager 하나, §13.3).
    // - 적 등록·분대 묶기·엄폐 수집(CoverPoint → CoverIndex)
    // - 서비스 조회: IPlayerTarget, IProjectileService, IAshStormInfo(M6, 없으면 폭풍 없음) — targetResolveRetrySec마다 재조회
    // - 소음 분배: NoiseEmitted → 반경 × hearingMultiplier 안의 살아 있는 적
    // - 틱: 감각 갱신은 AiTickScheduler로 분산(프레임당 maxSenseUpdatesPerFrame), 의도 적용은 매 프레임
    // - 측정: ProfilerMarker "Ashfall.AI.Tick" + Stopwatch → Stats
    // Update 경로 할당 없음.
    [DisallowMultipleComponent]
    public sealed class AiDirector : MonoBehaviour
    {
        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("Ashfall.AI.Tick");

        [SerializeField] private AiConfigSO config;

        private readonly List<EnemyAgent> agents = new List<EnemyAgent>(32);
        private readonly List<EnemyAgent> slotAgents = new List<EnemyAgent>(32);
        private readonly Dictionary<int, Squad> squads = new Dictionary<int, Squad>();

        private AiTickScheduler scheduler;
        private int[] dueSlots;
        private PhysicsLineOfSight lineOfSight;
        private EnemyWorld world;
        private Action<NoiseEmitted> noiseHandler;
        private bool subscribed;
        private bool initialized;
        private int nextAgentId;
        private int registeredCount;
        private int pathRequestsThisFrame;
        private float nextResolveTime = float.NegativeInfinity;

        public AiConfigSO Config => config;
        public IReadOnlyList<EnemyAgent> Agents => agents;
        public TickStats Stats { get; } = new TickStats();
        public CoverIndex Covers { get; private set; }
        public IPlayerTarget Target { get; private set; }
        public IProjectileService Projectiles { get; private set; }
        // 재 폭풍 상태 (M6 §1.5, Q26). 레이드 밖·폭풍 제공자 없음 = null.
        public IAshStormInfo Storm { get; private set; }

        // 감각 갱신에 넘기는 시야 상한 (m). 폭풍 없음 = +∞ (SightCone.EffectiveRange가 기존 거리 그대로 쓴다).
        public float VisibilityCapM => Storm != null ? Storm.VisibilityM : float.PositiveInfinity;

        internal IEnemyWorld World => world;
        internal ILineOfSight LineOfSight => lineOfSight;

        public bool TryGetSquad(int squadId, out Squad squad) => squads.TryGetValue(squadId, out squad);

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[AiDirector] AiConfigSO가 연결되지 않았다.", this);
                enabled = false;
                return;
            }
            Initialize();
        }

        private void OnEnable() => TrySubscribe();

        // GameManager는 첫 씬 로드 전에 생기지만, 없었으면 Start에서 한 번 더 시도한다.
        private void TrySubscribe()
        {
            if (subscribed || GameManager.Instance == null)
                return;
            noiseHandler ??= OnNoise;
            GameManager.Instance.Events.Subscribe(noiseHandler);
            subscribed = true;
        }

        private void OnDisable()
        {
            if (subscribed && GameManager.Instance != null)
                GameManager.Instance.Events.Unsubscribe(noiseHandler);
            subscribed = false;
        }

        private void Start()
        {
            TrySubscribe();
            ResolveServices(Time.time);
        }

        private void Initialize()
        {
            if (initialized || config == null)
                return;

            CoverPoint[] points = FindObjectsByType<CoverPoint>(FindObjectsSortMode.InstanceID);
            Covers = new CoverIndex(Mathf.Max(8, points.Length));
            foreach (CoverPoint p in points)
                Covers.Add(p.transform.position, p.Forward, p.Height, p.PeekPosition);

            lineOfSight = new PhysicsLineOfSight();
            world = new EnemyWorld(config, Covers, lineOfSight);
            scheduler = new AiTickScheduler(32);
            dueSlots = new int[Mathf.Max(1, config.maxSenseUpdatesPerFrame)];
            initialized = true;
        }

        // 적 등록: AgentId·스케줄 슬롯 부여. config가 없으면 -1을 준다(적은 스스로 꺼진다).
        public void Register(EnemyAgent agent)
        {
            if (agent == null || agents.Contains(agent))
                return;
            Initialize();
            if (!initialized)
            {
                agent.AssignRegistration(-1, -1);
                return;
            }

            int id = nextAgentId++;
            float now = Time.time;
            int slot = scheduler.Add(now + AiTickScheduler.PhaseOffset(registeredCount++, config.relaxedSensePeriodSec));
            while (slotAgents.Count <= slot)
                slotAgents.Add(null);
            slotAgents[slot] = agent;
            agents.Add(agent);
            world.SetAgentRoot(id, agent.transform);
            agent.AssignRegistration(id, slot);
        }

        public void Unregister(EnemyAgent agent)
        {
            if (agent == null || !agents.Remove(agent))
                return;
            int slot = agent.SchedulerSlot;
            if (slot >= 0)
            {
                scheduler.Remove(slot);
                if (slot < slotAgents.Count)
                    slotAgents[slot] = null;
            }
            if (agent.AgentId >= 0)
            {
                Covers?.ReleaseAll(agent.AgentId);
                world?.SetAgentRoot(agent.AgentId, null);
            }
        }

        // squadId(0 = 단독)에 맞는 분대. 없으면 만든다(등록 때만).
        internal Squad SquadFor(int squadId)
        {
            if (squadId == 0 || config == null)
                return null;
            if (!squads.TryGetValue(squadId, out Squad squad))
            {
                squad = new Squad(squadId, config);
                squads.Add(squadId, squad);
            }
            return squad;
        }

        // 프레임당 NavMesh 경로 요청 예산 (maxPathRequestsPerFrame)
        internal bool TryConsumePathRequest()
        {
            if (pathRequestsThisFrame >= config.maxPathRequestsPerFrame)
                return false;
            pathRequestsThisFrame++;
            return true;
        }

        private void Update()
        {
            if (!initialized)
                return;

            long start = Stopwatch.GetTimestamp();
            using (TickMarker.Auto())
            {
                float now = Time.time;
                float dt = Time.deltaTime;

                ValidateServices();
                if (now >= nextResolveTime)
                    ResolveServices(now);

                world.BeginFrame();
                world.SetTarget(Target != null ? Target.Root : null);
                pathRequestsThisFrame = 0;

                if (dueSlots.Length != Mathf.Max(1, config.maxSenseUpdatesPerFrame))
                    dueSlots = new int[Mathf.Max(1, config.maxSenseUpdatesPerFrame)];

                float visibilityCap = VisibilityCapM;
                int due = scheduler.CollectDue(now, config.maxSenseUpdatesPerFrame, dueSlots);
                for (int i = 0; i < due; i++)
                {
                    int slot = dueSlots[i];
                    EnemyAgent agent = slot < slotAgents.Count ? slotAgents[slot] : null;
                    if (agent == null)
                    {
                        scheduler.Remove(slot);
                        continue;
                    }
                    try
                    {
                        agent.Sense(Target, lineOfSight, now, visibilityCap);
                    }
                    catch (Exception e)
                    {
                        // 한 적의 예외가 다른 적의 틱을 막지 않게 (리뷰 7). 그 적은 스스로 빠진다(등록 해제 → 슬롯 제거).
                        AiTickGuard.Fault(agent, e);
                        continue;
                    }
                    float period = agent.IsEngaged ? config.engagedSensePeriodSec : config.relaxedSensePeriodSec;
                    scheduler.Advance(slot, period, now);
                }

                AiTickGuard.FrameTickAll(agents, now, dt);
            }
            Stats.Add((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        }

        private void OnNoise(NoiseEmitted noise)
        {
            float now = Time.time;
            for (int i = agents.Count - 1; i >= 0; i--)
            {
                EnemyAgent agent = agents[i];
                if (agent == null || agent.IsDead || agent.Archetype == null)
                    continue;
                float radius = noise.RadiusM * agent.Archetype.hearingMultiplier;
                if ((agent.transform.position - noise.Position).sqrMagnitude <= radius * radius)
                    agent.OnHeard(noise, now);
            }
        }

        // 파괴된 제공자(Unity 오브젝트)를 놓는다.
        private void ValidateServices()
        {
            if (Target is UnityEngine.Object t && t == null)
                Target = null;
            if (Projectiles is UnityEngine.Object p && p == null)
                Projectiles = null;
            if (Storm is UnityEngine.Object s && s == null)
                Storm = null;
        }

        private void ResolveServices(float now)
        {
            nextResolveTime = now + Mathf.Max(0.1f, config.targetResolveRetrySec);
            GameManager gm = GameManager.Instance;
            if (gm == null)
                return;
            if (gm.Services.TryGet(out IPlayerTarget target))
                Target = target;
            else
                Target = null;
            // 해제된 서비스는 놓는다 (리뷰 9: 등록 해제 뒤에도 옛 참조로 쏘던 문제). 적 무기는 매 프레임 이 값을 쓴다.
            if (gm.Services.TryGet(out IProjectileService projectiles))
                Projectiles = projectiles;
            else
                Projectiles = null;
            // 재 폭풍 (Raid AshStormController가 레이드 씬에서만 등록, M6 Q26). 해제되면 다음 조회에서 놓는다 → 상한 없음.
            if (gm.Services.TryGet(out IAshStormInfo storm))
                Storm = storm;
            else
                Storm = null;
        }
    }
}
