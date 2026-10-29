using System;
using System.Collections;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // 레이드 씬 브리지 (M4 설계 §4.2). 씬당 1개, 정적 Instance 없음 (싱글톤은 GameManager 하나, §13.3).
    // 규칙은 RaidSession·ExtractionModel·ExtractionRules·ActiveExtractionPicker·RaidResultBuilder에 있다.
    // - Awake: 씬의 ExtractionZone·PlayerSpawnPoint·PowerSwitch·PointOfInterest 수집·검증
    // - OnEnable: IRaidInfo 등록, PlayerDied·EnemyKilled·RaidAbandonRequested 구독
    // - Start: 서비스 조회 → 스폰 선택·배치 → 활성 탈출구 → 세션 → 반입 기록 → RaidStarted
    // - Update: 세션 틱 → ExtractionStatusChanged (구역·상태·초가 바뀔 때만)
    // - 종료: 결과(RULE-01) → RaidEnded → 세계 정지 → endDelaySec(실시간) 뒤 GameFlow.CompleteRaid
    // M5: 종료 시점 소지품을 위치까지(Loadout.Capture) 결과에 담는다 → 탈출이면 은신처 장비(RaidResult.CarriedOutLoadout).
    //     반입 장비 주입(GameFlow.TryGetRaidLoadout → PlayerInventoryBehaviour)·raidInProgress 기록(GameFlow → ProfileService)은
    //     다른 곳. 레이드 중에는 세이브를 쓰지 않는다(RULE-03).
    public sealed class RaidController : MonoBehaviour, IRaidInfo
    {
        [SerializeField] private RaidConfigSO config;
        [Tooltip("스폰·활성 탈출구 선택 시드. 0 = 매번 무작위")]
        [SerializeField] private int seed;

        private ExtractionZone[] zones = Array.Empty<ExtractionZone>();
        private PlayerSpawnPoint[] spawns = Array.Empty<PlayerSpawnPoint>();
        private PowerSwitch[] switches = Array.Empty<PowerSwitch>();
        private readonly List<MapMarker> poiMarkers = new List<MapMarker>();
        private readonly List<MapMarker> activeMarkers = new List<MapMarker>();
        private readonly List<string> activeIds = new List<string>();
        private readonly List<ItemDefinitionSO> broughtIn = new List<ItemDefinitionSO>();
        private readonly List<ItemInstance> snapshot = new List<ItemInstance>();

        private IPlayerTarget target;
        private IInventoryService inventory;

        // 마지막으로 발행한 탈출 HUD 상태 (바뀔 때만 발행)
        private bool statusVisible;
        private ExtractionModel statusZone;
        private ExtractionBlock statusBlock;
        private int statusSeconds;

        public RaidConfigSO Config => config;
        public int Seed => seed;

        // ── 테스트·디버그 ──
        public RaidSession Session { get; private set; }
        public string SpawnId { get; private set; }
        // 맵 밀도 ③: 이번 레이드가 쓴 시드와 거기서 파생한 루트 활성화 시드(RaidStarted.LootSeed). 시작 전 0.
        public int UsedSeed { get; private set; }
        public int LootSeed { get; private set; }
        public IReadOnlyList<string> ActiveExtractionIds => activeIds;
        public IReadOnlyList<ItemDefinitionSO> BroughtIn => broughtIn;
        public RaidResult LastResult { get; private set; }

        // ── IRaidInfo (지도·일시정지 화면, RULE-06: 내 위치는 주지 않는다) ──
        public float ElapsedSec => Session != null ? Session.ElapsedSec : 0f;
        public float RemainingSec => Session != null ? Session.RemainingSec : (config != null ? config.timeLimitSec : 0f);
        public Rect MapAreaXZ => config != null ? config.mapAreaXZ : default;
        public IReadOnlyList<MapMarker> PointsOfInterest => poiMarkers;
        public IReadOnlyList<MapMarker> ActiveExtractions => activeMarkers;

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[RaidController] RaidConfigSO가 연결되지 않았다.", this);
                enabled = false;
                return;
            }
            CollectScene();
        }

        private void OnEnable()
        {
            if (config == null || GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Register<IRaidInfo>(this);
            EventBus events = GameManager.Instance.Events;
            events.Subscribe<PlayerDied>(OnPlayerDied);
            events.Subscribe<EnemyKilled>(OnEnemyKilled);
            events.Subscribe<RaidAbandonRequested>(OnAbandonRequested);
            events.Subscribe<DevReturnRequested>(OnDevReturnRequested);   // DEV-RETURN
        }

        private void OnDisable()
        {
            if (GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Unregister<IRaidInfo>(this);
            EventBus events = GameManager.Instance.Events;
            events.Unsubscribe<PlayerDied>(OnPlayerDied);
            events.Unsubscribe<EnemyKilled>(OnEnemyKilled);
            events.Unsubscribe<RaidAbandonRequested>(OnAbandonRequested);
            events.Unsubscribe<DevReturnRequested>(OnDevReturnRequested);   // DEV-RETURN
        }

        private void OnDestroy()
        {
            if (Session != null)
                Session.Ended -= OnSessionEnded;
            foreach (PowerSwitch s in switches)
                if (s != null)
                    s.Bind(null);
        }

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                Debug.LogError("[RaidController] GameManager가 없다.", this);
                enabled = false;
                return;
            }
            // 서비스는 각 기능이 OnEnable에서 등록한다 (Start 전에 모두 끝남).
            gm.Services.TryGet(out target);
            gm.Services.TryGet(out IPlayerPlacement placement);
            gm.Services.TryGet(out inventory);
            if (target == null || placement == null || inventory == null)
            {
                Debug.LogError($"[RaidController] 서비스 없음 — IPlayerTarget {target != null}, " +
                               $"IPlayerPlacement {placement != null}, IInventoryService {inventory != null}. 레이드를 시작하지 않는다.", this);
                enabled = false;
                return;
            }

            // 맵 밀도 ③(설계 §5.3): 쓴 시드를 기억해 루트 활성화 시드를 파생한다 — Random 소비 순서는 그대로(스폰·탈출구 선택 회귀 없음).
            int usedSeed = seed != 0 ? seed : UnityEngine.Random.Range(1, int.MaxValue);
            UsedSeed = usedSeed;
            LootSeed = SeedMath.RaidLootSeed(usedSeed);
            var random = new System.Random(usedSeed);

            // TODO(GDD-?) Q2: 고정 스폰 중 무작위 1곳
            int spawnIndex = ActiveExtractionPicker.PickSpawnIndex(spawns.Length, random);
            if (spawnIndex >= 0)
            {
                PlayerSpawnPoint spawn = spawns[spawnIndex];
                SpawnId = spawn.Id;
                placement.PlaceAt(spawn.FeetPosition, spawn.YawDeg);
            }
            else
            {
                Debug.LogWarning("[RaidController] PlayerSpawnPoint가 없어 플레이어 현재 위치에서 시작한다.", this);
            }

            // TODO(GDD-?) Q3: 스폰별 활성 세트 (EXT-01 항상 포함). 비활성 탈출구는 작동하지 않는다.
            activeIds.Clear();
            ActiveExtractionPicker.Pick(config, SpawnId, random, activeIds);
            Session = new RaidSession(config, BuildExtractions());
            Session.Ended += OnSessionEnded;
            foreach (PowerSwitch s in switches)
                s.Bind(Session);

            // 반입 기록 (M4 설계 §1.6: 정의 목록. 결과 화면 "반입" 표시용)
            snapshot.Clear();
            broughtIn.Clear();
            InventorySnapshot.Collect(inventory, snapshot);
            RaidResultBuilder.ToDefinitions(snapshot, broughtIn);
            snapshot.Clear();

            gm.Events.Publish(new RaidStarted(SpawnId, config.timeLimitSec, LootSeed));
        }

        private void Update()
        {
            if (Session == null || Session.IsOver)
                return;
            // 시간은 scaled deltaTime — 일시정지(timeScale 0) 중 멈춘다 (RULE-02).
            // M4 리뷰 수정: 대상 오버로드 — 죽은 몸이면(PlayerDied를 구독 전에 놓친 경우 등) 탈출·MIA 대신 사망으로 끝낸다.
            Session.Tick(Time.deltaTime, target, inventory);
            if (!Session.IsOver)
                PublishStatusIfChanged();
        }

        // ── 디버그 (에디터 인스펙터 메뉴, 입력 추가 없음 — M4 설계 §3.3) ──

        [ContextMenu("Debug/시간 +5분")]
        private void DebugAdvanceFiveMinutes() => AdvanceTime(300f);

        [ContextMenu("Debug/즉시 MIA")]
        private void DebugMissingInAction()
        {
            if (Session != null)
                AdvanceTime(Session.RemainingSec);
        }

        // 디버그·테스트: 경과 시간을 더한다 (종료 판정은 다음 Update).
        public void AdvanceTime(float seconds) => Session?.AdvanceTime(seconds);

        // EXT-02 스위치 켜기 (PowerSwitch와 같은 경로). 처음 켜면 true.
        public bool ActivateSwitch(string switchId) => Session != null && Session.ActivateSwitch(switchId);

        // ── 이벤트 → 세션 ──

        private void OnPlayerDied(PlayerDied evt) => Session?.EndByDeath(evt.Cause, evt.Kind);   // MVP 마무리 F1 사인 종류
        private void OnEnemyKilled(EnemyKilled evt) => Session?.RecordKill();
        private void OnAbandonRequested(RaidAbandonRequested evt) => Session?.Abandon();

        // DEV-RETURN: 에디터·개발 빌드에서만 따른다. 릴리스 빌드면 (UI가 발행하지 않지만) 와도 무시한다.
        private void OnDevReturnRequested(DevReturnRequested evt)
        {
            if (!DevFeatures.IsDevReturnAvailable)
            {
                Debug.LogWarning("[RaidController] 릴리스 빌드라 개발용 귀환 요청을 무시한다.", this);
                return;
            }
            Session?.DevReturn();
        }

        // ── 종료 ──

        // 종료 순서: 결과(RULE-01) → 탈출 HUD 숨김 → RaidEnded → 세계 정지 → endDelaySec 뒤 CompleteRaid.
        // M4 리뷰 수정: 앞 단계에서 예외가 나도 RaidEnded·정지·완료 예약은 finally에서 반드시 한다 (레이드 씬에 갇히지 않게).
        private void OnSessionEnded()
        {
            bool endedPublished = false;
            try
            {
                // 종료 시점 소지품(위치 포함, RaidEnded 직전 = 잠금 전) → RULE-01 (RaidResultBuilder 한 곳)
                LastResult = RaidResultBuilder.Build(Session, broughtIn, Loadout.Capture(inventory));

                GameManager gm = GameManager.Instance;
                if (gm == null)
                    return;   // 흐름 밖 (결과만 보관) — finally도 gm 없으면 아무것도 안 한다

                if (statusVisible)
                    PublishStatus(false, null, ExtractionBlock.None, 0);
                // 이 이벤트로 Inventory·Weapons·Player가 소지품 변경을 멈춘다 (재장전 취소·의약품 사용 취소·인벤토리 잠금).
                endedPublished = true;
                gm.Events.Publish(new RaidEnded(LastResult.Outcome, LastResult.ExtractionName));
            }
            finally
            {
                FinishEnding(endedPublished);
            }
        }

        // OnSessionEnded의 뒷부분. 결과가 없으면(앞에서 예외) 소지품 없는 결과로 대신한다.
        private void FinishEnding(bool endedPublished)
        {
            snapshot.Clear();
            if (LastResult == null)
            {
                Debug.LogError("[RaidController] 결과를 만들지 못해 소지품 없는 결과로 레이드를 끝낸다.", this);
                LastResult = RaidResultBuilder.Build(Session, broughtIn, Loadout.Empty);
            }

            GameManager gm = GameManager.Instance;
            if (gm == null)
                return;
            if (!endedPublished)
                gm.Events.Publish(new RaidEnded(LastResult.Outcome, LastResult.ExtractionName));   // 버스는 핸들러 예외를 삼킨다

            // TODO(GDD-?) Q15: 종료 오버레이 endDelaySec(실시간) 동안 세계 정지 → 은신처에서 결과 표시 (M5 Q21).
            // 정지는 CompleteRaid가 풀지 않고 Hideout 로드 완료(GameFlow.OnSceneLoaded)에서 풀린다 (M4 리뷰 수정).
            GameFlow flow = gm.Flow;
            if (flow == null)
                return;
            flow.SetWorldPaused(true);
            if (isActiveAndEnabled)
                StartCoroutine(CompleteAfterDelay(flow, LastResult));
            else
                Complete(flow, LastResult);
        }

        private IEnumerator CompleteAfterDelay(GameFlow flow, RaidResult result)
        {
            if (config.endDelaySec > 0f)
                yield return new WaitForSecondsRealtime(config.endDelaySec);
            Complete(flow, result);
        }

        private void Complete(GameFlow flow, RaidResult result)
        {
            if (flow.Phase == GamePhase.Raid)
            {
                // 씬 로드 요청이 실패하면 GameFlow가 정지를 풀고 LogError (세계가 영구 정지하지 않게).
                flow.CompleteRaid(result);
                return;
            }
            // 흐름 밖 씬(Phase None: 코드로 만든 테스트 씬, 레이드 씬을 Additive로 올린 경우) — 의도된 동작.
            // 돌아갈 흐름이 없으니 씬을 바꾸지 않고 결과는 LastResult에만 남긴다. 정지는 풀어 테스트·에디터 Play가 계속 돌게 한다.
            // 종료 오버레이(RaidEnd)와 조작 차단은 일부러 유지한다: 레이드는 끝났고(인벤토리·무기도 RaidEnded로 잠김)
            // 이 씬에서 더 할 일이 없다. 에디터에서는 Play를 멈추면 된다. 정상 경로(Raid_CrimsonBasin을 이름으로 로드,
            // 에디터에서 그 씬을 바로 Play하는 경우 포함)는 Phase == Raid라 여기에 오지 않는다.
            Debug.LogWarning($"[RaidController] 흐름 밖 씬(단계 {flow.Phase})이라 결과({result.Outcome})만 보관한다.", this);
            flow.SetWorldPaused(false);
        }

        // ── 탈출 HUD 이벤트 ──

        private void PublishStatusIfChanged()
        {
            ExtractionModel zone = Session.CurrentZone;
            if (zone == null)
            {
                if (statusVisible)
                    PublishStatus(false, null, ExtractionBlock.None, 0);
                return;
            }
            ExtractionBlock block = zone.Block;
            int seconds = Mathf.CeilToInt(zone.RemainingSec);
            if (statusVisible && ReferenceEquals(zone, statusZone) && block == statusBlock && seconds == statusSeconds)
                return;
            PublishStatus(true, zone, block, seconds);
        }

        private void PublishStatus(bool visible, ExtractionModel zone, ExtractionBlock block, int seconds)
        {
            statusVisible = visible;
            statusZone = zone;
            statusBlock = block;
            statusSeconds = seconds;
            GameManager.Instance?.Events.Publish(
                new ExtractionStatusChanged(visible, zone?.Config.displayName, block, seconds));
        }

        // ── 씬 수집 ──

        private void CollectScene()
        {
            zones = FindSorted<ExtractionZone>(z => z.Id);
            spawns = FindSorted<PlayerSpawnPoint>(s => s.Id);
            switches = FindSorted<PowerSwitch>(s => s.SwitchId);
            PointOfInterest[] pois = FindSorted<PointOfInterest>(p => p.Id);

            ReportDuplicates(zones, z => z.Id, "ExtractionZone");
            ReportDuplicates(spawns, s => s.Id, "PlayerSpawnPoint");
            ReportDuplicates(pois, p => p.Id, "PointOfInterest");

            // config ↔ 씬 일치 확인
            ExtractionPointConfig[] extractions = config.extractions ?? Array.Empty<ExtractionPointConfig>();
            foreach (ExtractionPointConfig e in extractions)
                if (FindZone(e.id) == null)
                    Debug.LogError($"[RaidController] 설정의 탈출구 {e.id}에 맞는 ExtractionZone이 씬에 없다.", this);
            foreach (ExtractionZone z in zones)
                if (!FindConfig(z.Id, out _))
                    Debug.LogError($"[RaidController] ExtractionZone {z.Id}가 RaidConfigSO.extractions에 없다.", z);

            poiMarkers.Clear();
            foreach (PointOfInterest p in pois)
                poiMarkers.Add(new MapMarker(p.Id, p.DisplayName, ToXZ(p.transform.position)));
        }

        // config 순서대로, 씬에 구역이 있는 탈출구만 모델을 만든다.
        private List<ExtractionModel> BuildExtractions()
        {
            var models = new List<ExtractionModel>();
            activeMarkers.Clear();
            if (config.extractions == null)
                return models;
            foreach (ExtractionPointConfig e in config.extractions)
            {
                ExtractionZone zone = FindZone(e.id);
                if (zone == null)
                    continue;
                bool active = activeIds.Contains(e.id);
                models.Add(new ExtractionModel(e, zone.transform.position, active));
                if (active)
                    activeMarkers.Add(new MapMarker(e.id, e.displayName, ToXZ(zone.transform.position)));
            }
            return models;
        }

        private ExtractionZone FindZone(string id)
        {
            foreach (ExtractionZone z in zones)
                if (z.Id == id)
                    return z;
            return null;
        }

        private bool FindConfig(string id, out ExtractionPointConfig found)
        {
            if (config.extractions != null)
                foreach (ExtractionPointConfig e in config.extractions)
                    if (e.id == id)
                    {
                        found = e;
                        return true;
                    }
            found = default;
            return false;
        }

        // 씬 순서에 기대지 않도록 id 순으로 정렬한다 (시드 재현성).
        private static T[] FindSorted<T>(Func<T, string> key) where T : MonoBehaviour
        {
            T[] found = FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Array.Sort(found, (a, b) => string.CompareOrdinal(key(a), key(b)));
            return found;
        }

        private void ReportDuplicates<T>(T[] sorted, Func<T, string> key, string label) where T : MonoBehaviour
        {
            for (int i = 1; i < sorted.Length; i++)
                if (key(sorted[i]) == key(sorted[i - 1]))
                    Debug.LogError($"[RaidController] {label} id 중복: {key(sorted[i])}", sorted[i]);
        }

        private static Vector2 ToXZ(Vector3 p) => new Vector2(p.x, p.z);
    }
}
