using System;
using Ashfall.Core;
using UnityEngine;
using Random = System.Random;

namespace Ashfall.Audio
{
    // 재생 요청 종류 (테스트·디버그용 기록)
    public enum PlayedSoundKind
    {
        None,
        Footstep,
        Gunshot,
        Collapse,
        WeaponClick,   // M7 WeaponClicked (헛격발·고장·해제)
        EnemyVocal,    // M7 EnemyVocalized (변이체 울음·공격)
    }

    // 마지막 재생 요청 (§7 AC2 R3: 배치모드에 오디오 장치가 없을 수 있어 isPlaying 대신 이것으로 확인한다). 할당 없음.
    public readonly struct PlayedSound
    {
        public readonly PlayedSoundKind Kind;
        public readonly AudioClip Clip;
        public readonly Vector3 Position;
        public readonly float Gain;
        public readonly float Pitch;
        public readonly int Priority;
        public readonly int Voice;
        public readonly SoundSource Source;
        public readonly SurfaceKind Surface;    // Footstep만 의미 있음 (그 밖은 default — HasSurface로 확인)
        public readonly GunshotLayer Layer;     // Gunshot만 의미 있음 (그 밖은 default — HasLayer로 확인)

        public bool HasSurface => Kind == PlayedSoundKind.Footstep;
        public bool HasLayer => Kind == PlayedSoundKind.Gunshot;

        public PlayedSound(PlayedSoundKind kind, AudioClip clip, Vector3 position, float gain, float pitch, int priority, int voice,
            SoundSource source, SurfaceKind surface, GunshotLayer layer)
        {
            Kind = kind;
            Clip = clip;
            Position = position;
            Gain = gain;
            Pitch = pitch;
            Priority = priority;
            Voice = voice;
            Source = source;
            Surface = surface;
            Layer = layer;
        }
    }

    // 레이드 오디오 (§11, M6 설계 §1.4). 씬당 1개("AudioDirector", EditorTools가 배치), 정적 인스턴스 없음.
    // - FootstepTaken: 들리는 거리 밖이면 버림 → SurfaceProbe로 재질 → 재질 세트 클립(직전과 다르게) → 3D voice.
    // - ShotFired: 거리 → 근/중/원 레이어 하나(Q15) → 무기 분류 세트 → 3D voice. 지연·차폐 없음(Q16·Q17).
    // - AshStormChanged + IAshStormInfo: 바람·폭풍 루프·가이거(Q22). 금속 붕괴는 무작위 간격.
    // - 가이거 입력(M7 Q41) = 폭풍 률 + IRadiationField.ZoneRatePerSec(청취자 위치) — 환경 방사능률, 방독면 무관. 레이드 밖·구역 밖 = 0.
    // - WeaponClicked(M7): 헛격발·고장 = dryFireClip, 해제 = jamClearClip → 이벤트 위치 3D voice(우선순위 = 내 발소리보다 높음).
    // - EnemyVocalized(M7): 변이체 울음·공격 → 들리는 거리 밖이면 버림 → 종류별 클립(직전과 다르게) → 적 위치 3D voice(우선순위 = 적 발소리보다 높음).
    // - 일시정지(Q19): GameFlow.IsWorldPaused를 AudioListener.pause에 반영(timeScale은 건드리지 않는다). 파괴 시 false로 되돌린다.
    // 이벤트 경로는 할당 없음(설계 §1.7). 청취자 = IPlayerTarget.EyePosition(카메라의 AudioListener와 같은 위치).
    public sealed class AudioDirector : MonoBehaviour
    {
        // 서비스 재조회 주기·청취자 재탐색 주기는 AudioConfigSO(serviceRetrySec·listenerSearchRetrySec, M6 리뷰 낮음 4·8).
        // 이 값보다 짧게는 다시 찾지 않는다(설정이 0 이하여도 매 프레임 조회하지 않게 — 계산 보호값).
        private const float MinRetrySec = 0.1f;

        [SerializeField] private AudioConfigSO config;
        [Tooltip("0이면 매번 무작위 (클립 선택·피치·금속 붕괴·가이거)")]
        [SerializeField] private int seed;

        private AudioVoicePool voices;
        private AmbientSoundscape ambient;
        private SurfaceProbe probe;
        private ClipPicker[] footstepPickers;
        private ClipPicker collapsePicker;
        private Random random;
        private bool initialized;
        private bool subscribed;
        private EventBus subscribedBus;

        private IPlayerTarget target;
        private IAshStormInfo storm;
        private IRadiationField radiationField;   // M7 정유소 구역 (레이드 씬에만, 서비스 재조회로 잡는다)
        private AudioListener fallbackListener;
        private float nextResolveTime = float.NegativeInfinity;
        private float nextListenerSearchTime = float.NegativeInfinity;

        private Action<FootstepTaken> onFootstep;
        private Action<ShotFired> onShot;
        private Action<AshStormChanged> onStorm;
        private Action<WeaponClicked> onWeaponClick;
        private Action<EnemyVocalized> onVocal;
        private ClipPicker[] vocalPickers;   // EnemyVocalKind마다 (직전 기억이 종류별)

        public AudioConfigSO Config => config;
        public AudioVoicePool Voices => voices;
        public AmbientSoundscape Ambient => ambient;
        public SurfaceProbe Probe => probe;
        public PlayedSound LastPlayed { get; private set; }
        // 마지막으로 재생한 무기 클릭 종류 (LastPlayed.Kind == WeaponClick일 때 의미, 테스트용)
        public WeaponClickKind LastWeaponClick { get; private set; }
        // 마지막으로 재생한 적 울음 종류 (LastPlayed.Kind == EnemyVocal일 때 의미, 테스트용)
        public EnemyVocalKind LastEnemyVocal { get; private set; }
        // 누적: 재생 요청 성공 수 / 들리는 거리 밖이라 버린 수
        public int PlayedCount { get; private set; }
        public int CulledCount { get; private set; }
        public bool IsInitialized => initialized;
        // 마지막 Update에서 가이거에 넣은 두 몫 (M7 Q41, 테스트용). 합 = Ambient.GeigerInputPerSec
        public float StormRadiationPerSec { get; private set; }
        public float ZoneRadiationPerSec { get; private set; }
        public bool HasRadiationField => radiationField != null;
        // 지금 쓰는 청취자 위치
        public Vector3 ListenerPosition => ResolveListenerPosition();

        // 테스트·도구: Awake 전에(비활성 상태에서) 설정을 넣는다.
        public void Configure(AudioConfigSO audioConfig, int randomSeed)
        {
            if (initialized)
            {
                Debug.LogWarning("[AudioDirector] 이미 초기화됨 — Configure 무시", this);
                return;
            }
            config = audioConfig;
            seed = randomSeed;
        }

        private void Awake()
        {
            onFootstep = HandleFootstep;
            onShot = HandleShot;
            onStorm = HandleStormChanged;
            onWeaponClick = HandleWeaponClicked;
            onVocal = HandleVocalized;
            Initialize();
        }

        private void Initialize()
        {
            if (initialized)
                return;
            if (config == null)
            {
                Debug.LogError("[AudioDirector] AudioConfigSO가 없다 — 소리를 내지 않는다.", this);
                enabled = false;
                return;
            }
            random = seed != 0 ? new Random(seed) : new Random();
            probe = new SurfaceProbe(config);
            voices = new AudioVoicePool(transform, config.voiceCount, config.farMaxM);
            ambient = new AmbientSoundscape(transform, config, random);
            int surfaceCount = Enum.GetValues(typeof(SurfaceKind)).Length;
            footstepPickers = new ClipPicker[surfaceCount];
            for (int i = 0; i < surfaceCount; i++)
                footstepPickers[i] = new ClipPicker(random);
            collapsePicker = new ClipPicker(random);
            int vocalKinds = Enum.GetValues(typeof(EnemyVocalKind)).Length;
            vocalPickers = new ClipPicker[vocalKinds];
            for (int i = 0; i < vocalKinds; i++)
                vocalPickers[i] = new ClipPicker(random);
            initialized = true;
        }

        private void OnEnable()
        {
            if (!initialized)
                return;
            Subscribe();
        }

        private void Start()
        {
            if (!initialized)
                return;
            probe.Prewarm();
            ambient.Begin();
            ResolveServices();
        }

        private void OnDisable()
        {
            Unsubscribe();
            // 꺼진 디렉터가 일시정지 상태를 남기지 않게
            if (AudioListener.pause)
                AudioListener.pause = false;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            AudioListener.pause = false;
        }

        private void Subscribe()
        {
            if (subscribed)
                return;
            GameManager gm = GameManager.Instance;
            if (gm == null)
                return;
            subscribedBus = gm.Events;
            subscribedBus.Subscribe(onFootstep);
            subscribedBus.Subscribe(onShot);
            subscribedBus.Subscribe(onStorm);
            subscribedBus.Subscribe(onWeaponClick);
            subscribedBus.Subscribe(onVocal);
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
                return;
            if (subscribedBus != null)
            {
                subscribedBus.Unsubscribe(onFootstep);
                subscribedBus.Unsubscribe(onShot);
                subscribedBus.Unsubscribe(onStorm);
                subscribedBus.Unsubscribe(onWeaponClick);
                subscribedBus.Unsubscribe(onVocal);
            }
            subscribedBus = null;
            subscribed = false;
        }

        private void Update()
        {
            if (!initialized)
                return;
            if (!subscribed)
                Subscribe();

            // Q19: 일시정지·종료 오버레이 중 소리 정지 (은신처 음악은 ignoreListenerPause)
            GameManager gm = GameManager.Instance;
            bool paused = gm != null && gm.Flow != null && gm.Flow.IsWorldPaused;
            if (AudioListener.pause != paused)
                AudioListener.pause = paused;

            if (Time.unscaledTime >= nextResolveTime)
                ResolveServices();
            DropDestroyedServices();

            float intensity = 0f, radiation = 0f;
            if (storm != null)
            {
                intensity = Sanitize(storm.Intensity);
                radiation = Sanitize(storm.RadiationPerSec);
            }
            // M7 Q41: 청취자 위치의 구역 률을 더한다(합은 AmbientSoundscape). 할당 없음.
            float zone = radiationField != null ? Sanitize(radiationField.ZoneRatePerSec(ResolveListenerPosition())) : 0f;
            StormRadiationPerSec = radiation;
            ZoneRadiationPerSec = zone;
            if (ambient.Tick(Time.deltaTime, intensity, radiation, zone))
                PlayCollapse();
        }

        // 서비스 다시 찾기 (늦게 등록·해제된 것 반영). 테스트가 바로 부를 수 있다.
        public void ResolveServices()
        {
            float now = Time.unscaledTime;
            nextResolveTime = now + Mathf.Max(MinRetrySec, config != null ? config.serviceRetrySec : MinRetrySec);
            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                target = null;
                storm = null;
                radiationField = null;
                return;
            }
            gm.Services.TryGet(out target);
            gm.Services.TryGet(out storm);
            gm.Services.TryGet(out radiationField);
            // 표적이 없을 때만 씬의 AudioListener를 찾는다. 씬 전체 탐색이라 실패하면 listenerSearchRetrySec 동안 다시 찾지 않는다(리뷰 낮음 8).
            if (target == null && fallbackListener == null && now >= nextListenerSearchTime)
            {
                fallbackListener = FindAnyObjectByType<AudioListener>();
                nextListenerSearchTime = now + Mathf.Max(MinRetrySec, config != null ? config.listenerSearchRetrySec : MinRetrySec);
            }
        }

        // 3단계 통합: 등록 해제 없이 파괴된 Unity 제공자(씬 언로드·테스트 정리)를 놓는다 — 재조회(0.5초) 전 이벤트에서
        // MissingReferenceException이 나지 않게 (AiDirector.ValidateServices와 같은 방식, 할당 없음).
        private void DropDestroyedServices()
        {
            if (target is UnityEngine.Object t && t == null)
                target = null;
            if (storm is UnityEngine.Object s && s == null)
                storm = null;
            if (radiationField is UnityEngine.Object r && r == null)
                radiationField = null;
        }

        private Vector3 ResolveListenerPosition()
        {
            DropDestroyedServices();
            if (target != null)
                return target.EyePosition;
            if (fallbackListener != null)
                return fallbackListener.transform.position;
            return transform.position;
        }

        // ---------- 이벤트 ----------

        private void HandleFootstep(FootstepTaken e)
        {
            if (!initialized)
                return;
            float d = Vector3.Distance(e.Position, ResolveListenerPosition());
            if (!FootstepMix.Evaluate(d, e.Gait, e.Source, config, out float gain))
            {
                CulledCount++;
                return;
            }
            SurfaceKind kind = probe.Probe(e.Position);
            SurfaceFootsteps set = FootstepMix.FindSurface(config, kind);
            if (set == null || set.clips == null || set.clips.Length == 0)
                return;
            ClipPicker picker = footstepPickers[(int)kind];
            int index = picker.Next(set.clips.Length);
            AudioClip clip = index >= 0 ? set.clips[index] : null;
            if (clip == null)
                return;
            float pitch = picker.Pitch(config.pitchJitter);
            int priority = FootstepMix.Priority(e.Source);
            Play(PlayedSoundKind.Footstep, clip, e.Position, gain * Mathf.Max(0f, set.volume), pitch, priority, e.Source, kind, default);
        }

        private void HandleShot(ShotFired e)
        {
            if (!initialized)
                return;
            float d = Vector3.Distance(e.Position, ResolveListenerPosition());
            if (!GunshotMix.Evaluate(d, config, out GunshotLayer layer, out float gain))
            {
                CulledCount++;
                return;
            }
            GunshotSet set = GunshotMix.FindSet(config, e.Weapon);
            AudioClip clip = GunshotMix.ClipOrFallback(config, set, layer);
            if (clip == null)
                return;
            float volume = set != null ? Mathf.Max(0f, set.volume) : 1f;
            // 총성은 피치 변주 없음(설계 §1.4는 발소리만 ±pitchJitter)
            Play(PlayedSoundKind.Gunshot, clip, e.Position, gain * volume, 1f, FootstepMix.PriorityGunshot, e.Source, default, layer);
        }

        // M7 7A-2c: 플레이어 무기 클릭(설계 §1.2, Q10 — HUD 없이 소리만). DryFire·Jammed = dryFireClip, JamCleared = jamClearClip.
        // 플레이어 자신의 소리라 거리 판정 없이 이벤트 위치(총구)에서 3D로, 피치 변주 없음. 클립이 비면 소리 없음.
        private void HandleWeaponClicked(WeaponClicked e)
        {
            if (!initialized)
                return;
            bool clear = e.Kind == WeaponClickKind.JamCleared;
            AudioClip clip = clear ? config.jamClearClip : config.dryFireClip;
            if (clip == null)
                return;
            float gain = Mathf.Clamp01(clear ? config.jamClearVolume : config.dryFireVolume);
            if (Play(PlayedSoundKind.WeaponClick, clip, e.Position, gain, 1f, FootstepMix.PriorityWeaponClick, SoundSource.Player, default, default))
                LastWeaponClick = e.Kind;
        }

        // M7 7B-2c: 변이체 울음·공격(설계 §1.2·§4.9, Q26). 들리는 거리(vocalAudibleM) 밖이면 버림 → 종류 배열에서 직전과 다른 클립 →
        // 적 눈 위치 3D voice, 피치 ± pitchJitter, 우선순위 = 적 발소리보다 높음(PriorityEnemyVocal). 클립이 비면 소리 없음.
        private void HandleVocalized(EnemyVocalized e)
        {
            if (!initialized)
                return;
            float d = Vector3.Distance(e.Position, ResolveListenerPosition());
            if (!VocalMix.Evaluate(d, e.Kind, config, out float gain))
            {
                CulledCount++;
                return;
            }
            AudioClip[] clips = VocalMix.Clips(config, e.Kind);
            int kind = (int)e.Kind;
            if (clips == null || clips.Length == 0 || kind < 0 || kind >= vocalPickers.Length)
                return;
            ClipPicker picker = vocalPickers[kind];
            int index = picker.Next(clips.Length);
            AudioClip clip = index >= 0 ? clips[index] : null;
            if (clip == null)
                return;
            float pitch = picker.Pitch(config.pitchJitter);
            if (Play(PlayedSoundKind.EnemyVocal, clip, e.Position, gain, pitch, FootstepMix.PriorityEnemyVocal, SoundSource.Enemy, default, default))
                LastEnemyVocal = e.Kind;
        }

        private void HandleStormChanged(AshStormChanged e)
        {
            if (!initialized)
                return;
            ambient.OnStormPhase(e.Phase);
        }

        private void PlayCollapse()
        {
            AudioClip[] clips = config.collapseClips;
            if (clips == null || clips.Length == 0)
                return;
            int index = collapsePicker.Next(clips.Length);
            AudioClip clip = index >= 0 ? clips[index] : null;
            if (clip == null)
                return;
            Vector3 at = AmbientMix.RandomAround(ResolveListenerPosition(), config.collapseDistanceM, random);
            Play(PlayedSoundKind.Collapse, clip, at, Mathf.Clamp01(config.collapseVolume), 1f, FootstepMix.PriorityCollapse,
                SoundSource.Enemy, default, default);
        }

        private bool Play(PlayedSoundKind kind, AudioClip clip, Vector3 position, float gain, float pitch, int priority,
            SoundSource source, SurfaceKind surface, GunshotLayer layer)
        {
            if (!voices.Play(clip, position, gain, pitch, priority, Time.time))
                return false;
            PlayedCount++;
            LastPlayed = new PlayedSound(kind, clip, position, gain, pitch, priority, voices.LastVoice, source, surface, layer);
            return true;
        }

        private static float Sanitize(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : Mathf.Max(0f, v);

        // ---------- 디버그 (§7.1 A3·A4) ----------

        // 청취자 앞 레이어 구간 가운데 거리에서 대체 분류 총성
        public void DebugGunshot(GunshotLayer layer)
        {
            if (!initialized)
                return;
            float near = config.nearMaxM, mid = config.midMaxM, far = config.farMaxM;
            float distance = layer == GunshotLayer.Near ? near * 0.5f
                : layer == GunshotLayer.Mid ? (near + mid) * 0.5f
                : (mid + far) * 0.5f;
            DropDestroyedServices();
            Vector3 forward = target != null && target.Root != null ? target.Root.forward : Vector3.forward;
            HandleShot(new ShotFired(ResolveListenerPosition() + forward * distance, null, SoundSource.Enemy));
        }

        public void DebugCollapseNow()
        {
            if (!initialized)
                return;
            PlayCollapse();
            ambient.CollapseSchedule.Restart();
        }

        [ContextMenu("Debug/총성 근")]
        private void DebugGunshotNear() => DebugGunshot(GunshotLayer.Near);

        [ContextMenu("Debug/총성 중")]
        private void DebugGunshotMid() => DebugGunshot(GunshotLayer.Mid);

        [ContextMenu("Debug/총성 원")]
        private void DebugGunshotFar() => DebugGunshot(GunshotLayer.Far);

        [ContextMenu("Debug/금속 붕괴 지금")]
        private void DebugCollapseMenu() => DebugCollapseNow();
    }
}
