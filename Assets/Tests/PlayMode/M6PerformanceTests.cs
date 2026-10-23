#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Ashfall.AI;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M6 4단계 성능 (M6 설계 §1.7·§6 4단계·§7 AC2/AC3): 실제 Raid_CrimsonBasin(AudioDirector·AshStorm·AshParticles 포함)에서
    // 재 폭풍을 Peak로 강제하고, 적 20명을 무적 TestTarget(= 오디오 청취자) 주변 12~24m로 모아 전원 교전시킨 채 잰다.
    // (RaidPerformanceTests B와 같은 방식. Peak 시야 30m라 고리를 35~55m → 12~24m로 좁혔다.)
    // M7(7B-4, 오케스트레이터 결정 (b)): 씬은 34명(M4 20 + M7 14). M7 적은 씬 제자리에 두어 부하(AI 틱 34명·총성에 스스로 반응)에 포함하고,
    // 고리로 옮기는 것과 "전원 교전 진입" 전제는
    // M4 고정 배치 20명(이름이 Enemy_M7_로 시작하지 않는 SCV·RDR)에만 건다. M7 유형 최악 부하는 7D M7PerformanceTests.
    // 측정 직전 탄을 테스트 도우미(RaidPerformanceTests.RefillAmmo)로 가득 채운다 — 교전 대기 중 탄이 바닥나면 측정 구간 ShotFired 0(7B-3 진단).
    // 표적 근처 변이체(고속도로 무리)는 RaidPerformanceTests.ParkNearbyMutants로 표적에서 500m+ 떨어진 NavMesh로 옮긴다(씬·틱에는 남음, 7D에서 별도 계측).
    //  - AI 틱("Ashfall.AI.Tick" Stats)·투사체 스텝·프레임 중앙값: 기존 M3/M4 예산 그대로.
    //  - AudioDirector.Update + AshStormController.Update: 엔진 호출과 별도로 같은 메서드를 한 번 더 직접 불러 Stopwatch·GC.Alloc 측정
    //    (AiPerformanceTests의 director.Update 측정과 같은 방식 — 끄면 이벤트 구독·서비스 등록이 풀리므로 끄지 않고 추가 호출한다.
    //     그래서 측정 구간에는 AudioDirector의 환경음 틱(가이거 등)이 프레임당 두 번 돈다 = 보수적 측정).
    //    에디터가 스크립트 Update 마커(이름에 클래스명·Update)를 내면 Recorder로 엔진 호출 시간도 같이 보고한다(참고 — 마커 구성이 버전마다 달라 단언하지 않음).
    //  - 폭풍 전환(Rising) 중 프레젠터 쓰기 비용·할당: presenter.Apply를 강도를 살짝 바꿔 직접 부른다(같은 값이면 쓰지 않으므로).
    //  - 오디오 이벤트 경로: 측정 중 실제 발행된 FootstepTaken·ShotFired를 기록해 두었다가 같은 버스로 다시 발행하며 GC·시간을 잰다.
    //  - 재 파티클 수 ≤ AtmosphereConfigSO.ashMaxParticlesStorm.
    // 한계: 배치모드는 화면을 그리지 않는다(GPU·파티클 렌더·포스트 비용 없음, 오디오 장치 없을 수 있음) — 사람 확인 P1.
    public class M6PerformanceTests
    {
        // M4 고정 배치 20(CrimsonBasinLayout.ExpectedEnemyCount) + M7 배치 14(M7EnemyLayout.Count). PlayMode는 EditorTools를 안 보므로 상수(M7 설계 §6 7B-3).
        private const int ExpectedEnemies = 34;
        private const float StressNoiseRadiusM = 300f;
        private const float NoiseIntervalSec = 2f;
        private const float CombatTimeoutSec = 90f;
        private const float WarmupSec = 3f;
        private const float MeasureSec = 10f;
        private const int TransitionFrames = 120;
        private const int MaxReplayEvents = 512;

        private const double AiAverageBudgetMs = 1.0;
        private const double AiP99BudgetMs = 3.0;
        private const double ProjectileAverageBudgetMs = 1.0;
        private const double FrameMedianBudgetMs = 16.6;
        private const double AudioStormAverageBudgetMs = 0.2;

        // RaidPerformanceTests와 같은 표적 후보 (고속도로 아래 남북 도로)
        private static readonly Vector2[] TargetCandidatesXZ =
        {
            new Vector2(0f, -385f), new Vector2(5f, -380f), new Vector2(-5f, -390f), new Vector2(0f, -370f),
        };

        private M4Raid raid;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            raid = new M4Raid();
            yield return M4Raid.Load(raid, "SP-W");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (raid?.Gm != null && raid.Gm.Flow.IsWorldPaused)
                raid.Gm.Flow.SetWorldPaused(false);
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        [Timeout(420000)]
        public IEnumerator RaidScene_StormPeak_TwentyEnemiesCombat_AudioOn_BudgetsHold_AudioStormUpdateUnder02ms_NoGc()
        {
            Assert.AreEqual(ExpectedEnemies, raid.Enemies.Count, $"적 {ExpectedEnemies}명");
            AiDirector director = raid.Director;
            AudioDirector audio = Object.FindFirstObjectByType<AudioDirector>();
            AshStormController storm = Object.FindFirstObjectByType<AshStormController>();
            Assert.IsNotNull(audio, "AudioDirector 없음 — Ashfall/Setup/Apply M6 Art & Audio");
            Assert.IsNotNull(storm, "AshStormController 없음");
            Assert.IsTrue(audio.isActiveAndEnabled && audio.IsInitialized, "오디오 켬 (AudioDirector 초기화)");
            AshStormPresenter presenter = storm.Presenter;
            Assert.IsNotNull(presenter, "AshStormPresenter");
            ParticleSystem ash = presenter.AshParticles;
            Assert.IsNotNull(ash, "AshParticles 연결");
            AtmosphereConfigSO atmosphere = PresenterAtmosphere(presenter);
            int stormCap = atmosphere.ashMaxParticlesStorm;
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();

            Action audioUpdate = UpdateDelegate(audio);
            Action stormUpdate = UpdateDelegate(storm);

            Vector3 center = PickTargetSpot();
            TestTarget target = TestTarget.Create(center);
            var clones = new List<EnemyArchetypeSO>();
            var capture = new EventCapture(raid.Events, MaxReplayEvents);
            try
            {
                foreach (EnemyAgent e in raid.Enemies)
                    MakeImmortal(e, clones);
                string parked = RaidPerformanceTests.ParkNearbyMutants(raid.Enemies, center);   // 변이체 = 7D에서 별도 계측
                raid.Gm.Services.Unregister<IPlayerTarget>(raid.Motor);
                target.Register();
                audio.ResolveServices();   // 청취자 = 표적 (재조회 0.5초를 기다리지 않음)
                yield return M2Scene.WaitUntil(() => ReferenceEquals(director.Target, target), 3f);

                // ── 재 폭풍: 지금 시작 → Rising 중간에서 전환 쓰기 측정 → Peak ──
                Assert.IsTrue(storm.ForceStartNow(), "폭풍 강제 시작");
                yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Rising, 3f, () => "Rising");
                raid.Controller.AdvanceTime(storm.Config.riseSec * 0.25f);
                yield return null;
                yield return null;
                Assert.AreEqual(AshStormPhase.Rising, storm.Phase);
                Transition transition = null;
                yield return MeasureTransition(storm, presenter, r => transition = r);

                raid.Controller.AdvanceTime(storm.Config.riseSec);
                yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Peak, 3f, () => "Peak");
                Assert.AreEqual(30f, storm.VisibilityM, 1e-4f, "Peak 시야 30m");
                yield return M3Scene.WaitUntil(() => Mathf.Approximately(director.VisibilityCapM, 30f), 3f, () => "AI 시야 상한 30m 반영");
                Assert.IsTrue(audio.Ambient.StormLoopActive, "폭풍 루프 재생 요청");

                // ── M4 적 20명을 표적 주변 12~24m로 모아 전원 교전 (M7 14명은 제자리) ──
                var ring = RingSpots(terrain, center, raid.Enemies.Count);
                for (int i = 0; i < raid.Enemies.Count; i++)
                {
                    EnemyAgent e = raid.Enemies[i];
                    if (RaidPerformanceTests.IsM7(e))
                        continue;   // M7 적은 제자리(부하에는 포함 — 소리를 듣고 스스로 움직인다)
                    Vector3 spot = ring[i % ring.Count];
                    Vector3 face = center - spot;
                    M3Scene.WarpEnemy(e, spot, Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg);
                }
                int m4Count = raid.Enemies.Count(e => !RaidPerformanceTests.IsM7(e));
                Assert.AreEqual(RaidPerformanceTests.ExpectedM4Enemies, m4Count, "M4 고정 배치 20명");
                var everCombat = new HashSet<EnemyAgent>();
                var m7EverCombat = new HashSet<EnemyAgent>();
                float next = 0f;
                float start = Time.realtimeSinceStartup;
                while (everCombat.Count < m4Count)
                {
                    if (Time.realtimeSinceStartup - start > CombatTimeoutSec)
                        Assert.Fail($"{CombatTimeoutSec}초 안에 M4 {m4Count}명 교전 진입 안 됨 ({everCombat.Count}): {StateSummary()}");
                    next = MaybeNoise(center, next);
                    yield return null;
                    foreach (EnemyAgent e in raid.Enemies)
                        if (e.State == EnemyState.Combat)
                            (RaidPerformanceTests.IsM7(e) ? m7EverCombat : everCombat).Add(e);
                }
                float toAll = Time.realtimeSinceStartup - start;
                int refilled = RaidPerformanceTests.RefillAmmo(raid.Enemies);
                Assert.AreEqual(AshStormPhase.Peak, storm.Phase, "교전 대기 중에도 Peak (180초)");

                // ── 측정 ──
                Measurement m = null;
                yield return Measure(storm, audio, audioUpdate, stormUpdate, ash, capture, center, r => m = r);
                capture.Stop();

                // ── 이벤트 경로 재발행 (실제 위치·무기, 실제 지면 콜라이더) ──
                Replay replay = MeasureReplay(raid.Events, capture);

                Assert.AreEqual(AshStormPhase.Peak, storm.Phase, "측정 내내 Peak");
                string report =
                    $"[M6PerformanceTests] 적 {ExpectedEnemies}({parked}), 폭풍 Peak(시야 {storm.VisibilityM}m, AI 상한 {director.VisibilityCapM}m), M4 {m4Count}명 교전 진입 {toAll:0.0}초(그동안 교전한 M7 {m7EverCombat.Count}/{ExpectedEnemies - m4Count}, 측정 전 탄 보충 {refilled}명) | " +
                    $"{m} | 전환(Rising) {transition} | 이벤트 재발행 {replay} | " +
                    $"오디오: 재생 {audio.PlayedCount}, 거리 밖 {audio.CulledCount}, voice 빼앗기 {audio.Voices.StealCount}, 버림 {audio.Voices.DroppedCount}, 가이거 클릭 {audio.Ambient.GeigerClicks} | " +
                    $"재 파티클 최대 {m.MaxParticles} / 상한 {ash.main.maxParticles} (폭풍 상한 {stormCap}) | 엔진 마커 {m.EngineMarkers} | " +
                    $"화면 {Screen.width}x{Screen.height}, 배치모드 렌더 없음";
                Debug.Log(report);

                Assert.AreEqual(ExpectedEnemies, m.MinAlive, $"측정 중 {ExpectedEnemies}명 생존");
                Assert.LessOrEqual(m.AiAvg, AiAverageBudgetMs, "AI 틱 평균 예산");
                Assert.LessOrEqual(m.AiP99, AiP99BudgetMs, "AI 틱 99% 예산");
                Assert.LessOrEqual(m.ProjAvg, ProjectileAverageBudgetMs, "투사체 평균 예산");
                Assert.LessOrEqual(m.FrameMedian, FrameMedianBudgetMs, "프레임 중앙값(참고, 렌더 제외)");
                Assert.GreaterOrEqual(m.CombatAvg, 5.0, "측정 중 다수 교전");
                Assert.Greater(m.ShotsSeen, 0, "측정 중 적 총성(ShotFired) 발생 — 오디오 경로가 실제로 돌았다");
                Assert.Greater(m.FootstepsSeen, 0, "측정 중 발소리(FootstepTaken) 발생");

                Assert.LessOrEqual(m.AudioStormAvgMs, AudioStormAverageBudgetMs, "AudioDirector + AshStormController 갱신 평균 ≤ 0.2ms");
                Assert.LessOrEqual(transition.AvgMs, AudioStormAverageBudgetMs, "폭풍 전환 쓰기(프레젠터) 평균 ≤ 0.2ms");

                Assert.AreEqual(0, m.GcFrames, $"측정 구간 오디오·폭풍 Update GC 0 (할당 프레임 {m.GcFrames}, 샘플 {m.GcSamples}, 잡음 {m.GcNoisy})");
                Assert.Less(m.GcNoisy, m.Frames / 2, "GC 측정 잡음이 너무 많다");
                Assert.AreEqual(0, transition.GcFrames, $"폭풍 전환 쓰기 GC 0 (할당 프레임 {transition.GcFrames}, 잡음 {transition.GcNoisy})");
                Assert.Greater(replay.Events, 0, "재발행할 이벤트");
                Assert.IsTrue(replay.NoAlloc, $"오디오 이벤트 경로 GC 0 ({replay})");

                Assert.LessOrEqual(m.MaxParticles, stormCap, "재 파티클 수 ≤ 폭풍 상한");
                Assert.LessOrEqual(ash.main.maxParticles, stormCap, "파티클 상한 설정 ≤ 폭풍 상한");
                Assert.AreEqual(stormCap, ash.main.maxParticles, "Peak 상한 = ashMaxParticlesStorm");
            }
            finally
            {
                capture.Dispose();
                if (target != null)
                    Object.Destroy(target.gameObject);
                foreach (EnemyArchetypeSO a in clones)
                    Object.Destroy(a);
            }
        }

        // ── 측정 ──

        private sealed class Measurement
        {
            public double AiAvg, AiP99, ProjAvg, FrameMedian, FrameP99, CombatAvg;
            public double AudioAvgMs, StormAvgMs, AudioStormAvgMs, AudioStormMaxMs, EngineAvgMs;
            public int Frames, MinAlive, MaxLive, MaxParticles, GcFrames, GcSamples, GcNoisy, EngineSamples, ShotsSeen, FootstepsSeen;
            public string EngineMarkers = "(없음)";

            public override string ToString() =>
                $"{Frames}프레임, 교전 평균 {CombatAvg:0.0}명, AI 평균 {AiAvg:0.000}ms·99% {AiP99:0.000}ms, 투사체 평균 {ProjAvg:0.000}ms(동시 탄 최대 {MaxLive}), " +
                $"프레임 중앙값 {FrameMedian:0.00}ms·99% {FrameP99:0.00}ms, 생존 최소 {MinAlive} | " +
                $"AudioDirector.Update 평균 {AudioAvgMs:0.0000}ms + AshStormController.Update 평균 {StormAvgMs:0.0000}ms = {AudioStormAvgMs:0.0000}ms(최대 {AudioStormMaxMs:0.000}ms), " +
                $"엔진 마커 평균 {(EngineSamples > 0 ? EngineAvgMs.ToString("0.0000") + "ms(" + EngineSamples + "프레임)" : "없음")}, " +
                $"GC 할당 프레임 {GcFrames}(샘플 {GcSamples}, 잡음 {GcNoisy}), 측정 중 ShotFired {ShotsSeen}·FootstepTaken {FootstepsSeen}";
        }

        private sealed class Transition
        {
            public double AvgMs, MaxMs;
            public int Frames, GcFrames, GcNoisy;
            public override string ToString() => $"{Frames}프레임 프레젠터 쓰기 평균 {AvgMs:0.0000}ms·최대 {MaxMs:0.000}ms, GC 프레임 {GcFrames}(잡음 {GcNoisy})";
        }

        private sealed class Replay
        {
            public int Events, Shots, Footsteps, Attempts;
            public bool NoAlloc;
            public double UsPerEvent;
            public override string ToString() =>
                $"{Events}개(총성 {Shots}·발소리 {Footsteps}) 평균 {UsPerEvent:0.0}µs/이벤트, GC 0 = {NoAlloc} (시도 {Attempts})";
        }

        private IEnumerator Measure(AshStormController storm, AudioDirector audio, Action audioUpdate, Action stormUpdate,
            ParticleSystem ash, EventCapture capture, Vector3 center, Action<Measurement> result)
        {
            float next = 0f;
            float warmEnd = Time.realtimeSinceStartup + WarmupSec;
            while (Time.realtimeSinceStartup < warmEnd)
            {
                next = MaybeNoise(center, next);
                yield return null;
            }
            // 첫 호출(JIT) 제외
            audioUpdate();
            stormUpdate();

            // 엔진이 내는 스크립트 Update 마커가 있으면(에디터) 같이 본다 — 없으면 직접 호출 측정만.
            var markerNames = new List<string>();
            Sampler.GetNames(markerNames);
            string[] engineNames = markerNames
                .Where(n => (n.Contains("AudioDirector") || n.Contains("AshStormController")) && n.Contains("Update"))
                .Distinct().ToArray();
            Recorder[] engine = engineNames.Select(Recorder.Get).Where(r => r != null && r.isValid).ToArray();
            foreach (Recorder r in engine)
                r.enabled = true;

            AiDirector director = raid.Director;
            director.Stats.Reset();
            raid.Projectiles.ResetStats();
            capture.Begin();
            Recorder gc = Recorder.Get("GC.Alloc");
            Action empty = () => { };
            SampleGc(gc, empty);

            var ai = new List<double>(20000);
            var frames = new List<double>(20000);
            var m = new Measurement { MinAlive = int.MaxValue, EngineMarkers = engineNames.Length > 0 ? string.Join(", ", engineNames) : "(없음)" };
            int combatSum = 0;
            double audioSum = 0, stormSum = 0, engineSum = 0;
            var sw = new Stopwatch();
            yield return null;
            float end = Time.realtimeSinceStartup + MeasureSec;
            while (Time.realtimeSinceStartup < end)
            {
                next = MaybeNoise(center, next);
                yield return null;
                ai.Add(director.Stats.LastMs);
                frames.Add(Time.unscaledDeltaTime * 1000.0);

                // 시간과 할당을 같은 호출에서 잰다 (Recorder 켜기·끄기는 Stopwatch 밖)
                int n = SampleGcTimed(gc, audioUpdate, sw, out double a) + SampleGcTimed(gc, stormUpdate, sw, out double s);
                audioSum += a;
                stormSum += s;
                m.AudioStormMaxMs = Math.Max(m.AudioStormMaxMs, a + s);
                if (n > 0)
                {
                    if (SampleGc(gc, empty) > 0)
                        m.GcNoisy++;
                    else
                    {
                        m.GcFrames++;
                        m.GcSamples += n;
                    }
                }

                if (engine.Length > 0)
                {
                    double frameNs = 0;
                    int count = 0;
                    foreach (Recorder r in engine)
                    {
                        frameNs += r.elapsedNanoseconds;
                        count += r.sampleBlockCount;
                    }
                    if (count > 0)
                    {
                        engineSum += frameNs / 1e6;
                        m.EngineSamples++;
                    }
                }

                int alive = raid.Enemies.Count(e => e != null && !e.IsDead);
                m.MinAlive = Math.Min(m.MinAlive, alive);
                combatSum += InCombat();
                m.MaxLive = Math.Max(m.MaxLive, raid.Projectiles.LiveCount);
                m.MaxParticles = Math.Max(m.MaxParticles, ash.particleCount);
            }
            foreach (Recorder r in engine)
                r.enabled = false;
            m.Frames = frames.Count;
            m.AiAvg = director.Stats.AverageMs;
            m.AiP99 = Percentile(ai, 0.99);
            m.ProjAvg = raid.Projectiles.AverageStepMs;
            m.FrameMedian = Percentile(frames, 0.5);
            m.FrameP99 = Percentile(frames, 0.99);
            m.CombatAvg = (double)combatSum / Math.Max(1, m.Frames);
            m.AudioAvgMs = audioSum / Math.Max(1, m.Frames);
            m.StormAvgMs = stormSum / Math.Max(1, m.Frames);
            m.AudioStormAvgMs = m.AudioAvgMs + m.StormAvgMs;
            m.EngineAvgMs = engineSum / Math.Max(1, m.EngineSamples);
            m.ShotsSeen = capture.Shots;
            m.FootstepsSeen = capture.Footsteps;
            result(m);
        }

        // Rising 중: 프레젠터는 강도가 바뀐 프레임에만 RenderSettings·하늘 복제본·파티클을 쓴다. 엔진 Update가 이미 이번 프레임 값을 썼으므로
        // 강도를 살짝 바꿔 한 번, 원래 값으로 한 번 더 쓰게 해서 쓰기 비용·할당을 잰다(다음 프레임 엔진 Update가 정상 값으로 덮는다).
        private static IEnumerator MeasureTransition(AshStormController storm, AshStormPresenter presenter, Action<Transition> result)
        {
            Recorder gc = Recorder.Get("GC.Alloc");
            Action empty = () => { };
            float intensity = 0f, visibility = 0f;
            Action write = () =>
            {
                presenter.Apply(intensity * 0.999f, visibility * 1.001f);
                presenter.Apply(intensity, visibility);
            };
            intensity = storm.Intensity;
            visibility = storm.VisibilityM;
            write();   // JIT·복제본 생성 이후 상태
            SampleGc(gc, empty);
            var t = new Transition();
            double sum = 0;
            var sw = new Stopwatch();
            for (int i = 0; i < TransitionFrames; i++)
            {
                yield return null;
                Assert.AreEqual(AshStormPhase.Rising, storm.Phase, "전환 측정 중 Rising 유지");
                intensity = storm.Intensity;
                visibility = storm.VisibilityM;
                sw.Restart();
                write();
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds / 2.0;   // Apply 1회당
                sum += ms;
                t.MaxMs = Math.Max(t.MaxMs, ms);
                if (SampleGc(gc, write) > 0)
                {
                    if (SampleGc(gc, empty) > 0)
                        t.GcNoisy++;
                    else
                        t.GcFrames++;
                }
                t.Frames++;
            }
            t.AvgMs = sum / Math.Max(1, t.Frames);
            result(t);
        }

        // 기록한 실제 이벤트를 같은 버스로 다시 발행 (구독자 = AudioDirector). 측정 잡음 대비 최대 6회 시도, 한 번이라도 0이면 통과.
        private static Replay MeasureReplay(EventBus bus, EventCapture capture)
        {
            var r = new Replay { Events = capture.Count, Shots = capture.Shots, Footsteps = capture.Footsteps };
            if (capture.Count == 0)
                return r;
            Action publish = () => capture.Publish(bus);
            publish();   // 워밍업
            var sw = Stopwatch.StartNew();
            publish();
            sw.Stop();
            r.UsPerEvent = sw.Elapsed.TotalMilliseconds * 1000.0 / capture.Count;
            Recorder gc = Recorder.Get("GC.Alloc");
            Action empty = () => { };
            for (int i = 0; i < 6 && !r.NoAlloc; i++)
            {
                r.Attempts++;
                SampleGc(gc, empty);
                r.NoAlloc = SampleGc(gc, publish) == 0;
                if (!r.NoAlloc)
                    System.Threading.Thread.Sleep(50 * (i + 1));
            }
            return r;
        }

        private static int SampleGcTimed(Recorder recorder, Action action, Stopwatch sw, out double ms)
        {
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                sw.Restart();
                action();
                sw.Stop();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }
            ms = sw.Elapsed.TotalMilliseconds;
            return recorder.sampleBlockCount;
        }

        private static int SampleGc(Recorder recorder, Action action)
        {
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }
            return recorder.sampleBlockCount;
        }

        // 측정 구간에 발행된 FootstepTaken·ShotFired를 고정 배열에 기록한다 (기록 자체는 할당 없음).
        private sealed class EventCapture : IDisposable
        {
            private readonly EventBus bus;
            private readonly FootstepTaken[] steps;
            private readonly ShotFired[] shots;
            private readonly Action<FootstepTaken> onStep;
            private readonly Action<ShotFired> onShot;
            private bool recording;
            private bool subscribed;

            public int Footsteps { get; private set; }
            public int Shots { get; private set; }
            public int Count => Footsteps + Shots;

            public EventCapture(EventBus bus, int capacity)
            {
                this.bus = bus;
                steps = new FootstepTaken[capacity];
                shots = new ShotFired[capacity];
                onStep = e =>
                {
                    if (recording && Footsteps < steps.Length)
                        steps[Footsteps++] = e;
                };
                onShot = e =>
                {
                    if (recording && Shots < shots.Length)
                        shots[Shots++] = e;
                };
                bus.Subscribe(onStep);
                bus.Subscribe(onShot);
                subscribed = true;
            }

            public void Begin()
            {
                Footsteps = 0;
                Shots = 0;
                recording = true;
            }

            public void Stop()
            {
                recording = false;
                Unsubscribe();
            }

            public void Publish(EventBus target)
            {
                for (int i = 0; i < Shots; i++)
                    target.Publish(shots[i]);
                for (int i = 0; i < Footsteps; i++)
                    target.Publish(steps[i]);
            }

            private void Unsubscribe()
            {
                if (!subscribed)
                    return;
                bus.Unsubscribe(onStep);
                bus.Unsubscribe(onShot);
                subscribed = false;
            }

            public void Dispose() => Unsubscribe();
        }

        // ── 도우미 ──

        private static Action UpdateDelegate(MonoBehaviour behaviour)
        {
            MethodInfo method = behaviour.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(method, $"{behaviour.GetType().Name}.Update");
            return (Action)Delegate.CreateDelegate(typeof(Action), behaviour, method);
        }

        private static AtmosphereConfigSO PresenterAtmosphere(AshStormPresenter presenter)
        {
            FieldInfo field = typeof(AshStormPresenter).GetField("atmosphere", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            var atmosphere = (AtmosphereConfigSO)field.GetValue(presenter);
            Assert.IsNotNull(atmosphere, "presenter.atmosphere 연결");
            return atmosphere;
        }

        private Vector3 PickTargetSpot()
        {
            foreach (Vector2 xz in TargetCandidatesXZ)
            {
                if (!NavMesh.SamplePosition(new Vector3(xz.x, 100f, xz.y), out NavMeshHit hit, 150f, NavMesh.AllAreas))
                    continue;
                Vector3 p = hit.position;
                if (Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.5f, 0.3f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                return p;
            }
            Assert.Fail("표적 자리를 찾지 못했다");
            return Vector3.zero;
        }

        // 표적 주변 12~24m 고리 위 NavMesh 지점 (Peak 시야 30m 안)
        private static List<Vector3> RingSpots(Terrain terrain, Vector3 center, int count)
        {
            var spots = new List<Vector3>();
            for (int i = 0; i < 72 && spots.Count < count; i++)
            {
                float angle = i * 5f * 7f * Mathf.Deg2Rad;
                float radius = 12f + (i % 5) * 3f;
                Vector3 p = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    continue;
                if (spots.Any(s => Vector3.Distance(s, hit.position) < 2.5f))
                    continue;
                spots.Add(hit.position);
            }
            Assert.GreaterOrEqual(spots.Count, count / 2, "고리 지점 부족");
            return spots;
        }

        private int InCombat() => raid.Enemies.Count(e => e != null && !e.IsDead && e.State == EnemyState.Combat);

        private string StateSummary() =>
            string.Join(", ", raid.Enemies.Where(e => e != null).Select(e => $"{e.name}:{e.State}@{new Vector2(e.transform.position.x, e.transform.position.z)}"));

        private float MaybeNoise(Vector3 at, float next)
        {
            if (Time.time < next)
                return next;
            raid.Events.Publish(new NoiseEmitted(at + Vector3.up * 1.4f, StressNoiseRadiusM, NoiseKind.Gunshot));
            return Time.time + NoiseIntervalSec;
        }

        private void MakeImmortal(EnemyAgent enemy, List<EnemyArchetypeSO> clones)
        {
            EnemyArchetypeSO clone = Object.Instantiate(enemy.Archetype);
            clone.hp = 1e9f;
            clone.headHp = 1e9f;
            clone.legsHp = 1e9f;
            clones.Add(clone);
            PropertyInfo body = typeof(EnemyAgent).GetProperty(nameof(EnemyAgent.Body));
            Assert.IsNotNull(body);
            body.SetValue(enemy, new EnemyBodyModel(clone, raid.Director.Config));
        }

        private static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0)
                return 0.0;
            var sorted = values.OrderBy(v => v).ToList();
            int index = Math.Min(sorted.Count - 1, Math.Max(0, (int)Math.Ceiling(p * sorted.Count) - 1));
            return sorted[index];
        }
    }
}
#endif
