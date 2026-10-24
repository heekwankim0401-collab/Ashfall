using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.Hideout;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7D 개발 빌드 스모크 (M7 설계 §1.9, Q51): 명령줄 파싱, 단계 표, 실행기(시간 초과·예외·종료 코드),
    // 가짜 씬 로드로 단계 표를 끝까지 (실제 HideoutService·GameFlow·ProfileService, 메모리 세이브 — 실제 경로 없음).
    public class SmokeRunnerTests
    {
        // ── 세이브 폴더 정책 (7D 리뷰 낮음 4) ──

        [Test]
        public void IsSameFolder_NormalizesCaseAndTrailingSeparator()
        {
            string a = Path.Combine(Path.GetTempPath(), "ashfall_same");
            Assert.IsTrue(SmokeRunner.IsSameFolder(a, a + Path.DirectorySeparatorChar));
            Assert.IsTrue(SmokeRunner.IsSameFolder(a, a.ToUpperInvariant()));
            Assert.IsTrue(SmokeRunner.IsSameFolder(a, Path.Combine(a, "sub", "..")));
            Assert.IsFalse(SmokeRunner.IsSameFolder(a, a + "2"));
            Assert.IsFalse(SmokeRunner.IsSameFolder(a, null));
            Assert.IsFalse(SmokeRunner.IsSameFolder("", a));
        }

        [Test]
        public void PrepareSaveFolder_RealSaveFolder_Rejected_NothingDeleted()
        {
            string dir = NewTempDir("ashfall_smoke_real");
            try
            {
                var storage = new FileSaveStorage(dir);
                File.WriteAllText(storage.FilePath, "{}");
                Assert.AreEqual(-1, SmokeRunner.PrepareSaveFolder(storage, dir + Path.DirectorySeparatorChar, out string error));
                StringAssert.Contains("persistentDataPath", error);
                Assert.IsTrue(File.Exists(storage.FilePath), "실제 세이브 폴더면 아무것도 지우지 않는다");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void PrepareSaveFolder_DeletesOnlyOldSaveFiles_CreatesMissingFolder()
        {
            string dir = NewTempDir("ashfall_smoke_prep");
            try
            {
                var storage = new FileSaveStorage(dir);
                foreach (string p in new[] { storage.FilePath, storage.BackupPath, storage.TempPath, storage.CorruptPath })
                    File.WriteAllText(p, "old");
                string other = Path.Combine(dir, "smoke.log");
                File.WriteAllText(other, "log");
                Assert.AreEqual(4, SmokeRunner.PrepareSaveFolder(storage, Path.Combine(dir, "real"), out string error), error);
                Assert.IsNull(error);
                Assert.IsFalse(File.Exists(storage.FilePath));
                Assert.IsFalse(File.Exists(storage.BackupPath));
                Assert.IsFalse(File.Exists(storage.TempPath));
                Assert.IsFalse(File.Exists(storage.CorruptPath));
                Assert.IsTrue(File.Exists(other), "세이브 파일이 아닌 것은 그대로");
                Assert.AreEqual(0, SmokeRunner.PrepareSaveFolder(storage, Path.Combine(dir, "real"), out _), "두 번째 = 지울 것 없음");

                var missing = new FileSaveStorage(Path.Combine(dir, "new", "deeper"));
                Assert.AreEqual(0, SmokeRunner.PrepareSaveFolder(missing, Path.Combine(dir, "real"), out error), error);
                Assert.IsTrue(Directory.Exists(missing.DirectoryPath), "없는 폴더는 만든다");
                Assert.AreEqual(-1, SmokeRunner.PrepareSaveFolder(null, dir, out error));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static string NewTempDir(string prefix)
        {
            string dir = Path.Combine(Path.GetTempPath(), prefix + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ── 명령줄 ──

        [Test]
        public void Parse_NoArgument_NotRequested()
        {
            SmokeArguments a = SmokeRunner.ParseArguments(new[] { "Ashfall.exe", "-batchmode", "-nographics" });
            Assert.IsFalse(a.Requested);
            Assert.IsFalse(a.IsValid);
            Assert.IsFalse(SmokeRunner.ParseArguments(null).Requested);
        }

        [Test]
        public void Parse_Folder_AbsolutePath()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ashfall_smoke_parse");
            SmokeArguments a = SmokeRunner.ParseArguments(new[] { "Ashfall.exe", "-batchmode", "-ashfallSmoke", folder, "-logFile", "x.log" });
            Assert.IsTrue(a.IsValid);
            Assert.AreEqual(Path.GetFullPath(folder), a.Folder);
            Assert.IsNull(a.Error);
        }

        [Test]
        public void Parse_NameIsCaseInsensitive_RelativeBecomesFull()
        {
            SmokeArguments a = SmokeRunner.ParseArguments(new[] { "-ASHFALLSMOKE", "smoke_save" });
            Assert.IsTrue(a.IsValid);
            Assert.IsTrue(Path.IsPathRooted(a.Folder));
            Assert.AreEqual(Path.GetFullPath("smoke_save"), a.Folder);
        }

        [Test]
        public void Parse_MissingValue_RequestedButInvalid()
        {
            SmokeArguments end = SmokeRunner.ParseArguments(new[] { "Ashfall.exe", "-ashfallSmoke" });
            Assert.IsTrue(end.Requested);
            Assert.IsFalse(end.IsValid);
            StringAssert.Contains(SmokeRunner.ArgumentName, end.Error);

            SmokeArguments flag = SmokeRunner.ParseArguments(new[] { "-ashfallSmoke", "-logFile", "x.log" });
            Assert.IsTrue(flag.Requested);
            Assert.IsNull(flag.Folder);
        }

        [Test]
        public void RaidDuration_DefaultWhenAbsent_ParsedWhenGiven()
        {
            Assert.IsTrue(SmokeRunner.TryParseRaidDuration(new[] { "-ashfallSmoke", "x" }, 10f, out float d, out string e));
            Assert.AreEqual(10f, d);
            Assert.IsNull(e);

            Assert.IsTrue(SmokeRunner.TryParseRaidDuration(new[] { "-ashfallSmoke", "x", "-ASHFALLSMOKERAIDSEC", "75.5" }, 10f, out d, out e));
            Assert.AreEqual(75.5f, d, 1e-5f);
            // 폴더 인자 해석과 겹치지 않는다
            Assert.AreEqual(Path.GetFullPath("x"), SmokeRunner.ParseArguments(new[] { "-ashfallSmokeRaidSec", "5", "-ashfallSmoke", "x" }).Folder);
        }

        [Test]
        public void RaidDuration_InvalidValue_Fails()
        {
            foreach (string[] args in new[]
                     {
                         new[] { "-ashfallSmokeRaidSec" },
                         new[] { "-ashfallSmokeRaidSec", "abc" },
                         new[] { "-ashfallSmokeRaidSec", "0" },
                         new[] { "-ashfallSmokeRaidSec", "-5" },
                     })
            {
                Assert.IsFalse(SmokeRunner.TryParseRaidDuration(args, 10f, out _, out string e), string.Join(" ", args));
                StringAssert.Contains(SmokeRunner.RaidSecArgumentName, e);
            }
        }

        [Test]
        public void RaidDuration_ExtendsTimeout()
        {
            Assert.AreEqual(SmokeRunner.DefaultTimeoutSec, SmokeRunner.TimeoutForRaidDuration(SmokeScenario.DefaultRaidDurationSec));
            Assert.AreEqual(SmokeRunner.DefaultTimeoutSec, SmokeRunner.TimeoutForRaidDuration(1f));
            Assert.AreEqual(SmokeRunner.DefaultTimeoutSec + 110f,
                SmokeRunner.TimeoutForRaidDuration(SmokeScenario.DefaultRaidDurationSec + 110f), 1e-4f);
        }

        [Test]
        public void Diagnostics_OnlyInDevelopmentPlayer()
        {
            Assert.IsTrue(DevDiagnostics.ShouldAttach(isDebugBuild: true, isEditor: false));
            Assert.IsFalse(DevDiagnostics.ShouldAttach(isDebugBuild: false, isEditor: false), "릴리스 빌드에는 없다");
            Assert.IsFalse(DevDiagnostics.ShouldAttach(isDebugBuild: true, isEditor: true), "에디터 Play에는 없다");
        }

        // ── 단계 표 ──

        [Test]
        public void StepTable_Order()
        {
            CollectionAssert.AreEqual(new[]
            {
                "Boot", "EnterHideout", "HideoutSaveLoad", "StartRaid", "ForceStorm", "RaidPlay", "Abandon", "HideoutResult",
            }, SmokeScenario.StepNames.ToArray());

            var sim = new FlowSim();
            CollectionAssert.AreEqual(SmokeScenario.StepNames.ToArray(), SmokeScenario.Build(sim.Env).Select(s => s.Name).ToArray());
            Assert.AreEqual(10f, SmokeScenario.DefaultRaidDurationSec);
            Assert.AreEqual(60f, SmokeRunner.DefaultTimeoutSec);
        }

        // ── 실행기 ──

        private static SmokeStep Step(string name, Func<SmokeStepContext, SmokeStatus> run) => new SmokeStep(name, run);

        [Test]
        public void Runner_StepsInOrder_ExitCodeZero()
        {
            var log = new List<string>();
            var order = new List<string>();
            var runner = new SmokeRunner(new[]
            {
                Step("A", c => { order.Add("A" + c.Tick); return c.Tick < 2 ? SmokeStatus.Running : SmokeStatus.Passed; }),
                Step("B", c => { order.Add("B" + c.Tick); c.Detail = "ok"; return SmokeStatus.Passed; }),
            }, 60f, log.Add);

            Assert.AreEqual(-1, runner.ExitCode);
            float t = 0f;
            while (!runner.IsFinished)
                runner.Tick(t += 0.1f);
            CollectionAssert.AreEqual(new[] { "A0", "A1", "A2", "B0" }, order);
            Assert.AreEqual(SmokeStatus.Passed, runner.Status);
            Assert.AreEqual(0, runner.ExitCode);
            Assert.IsTrue(log.Any(l => l.Contains("1/2 A 통과")));
            Assert.IsTrue(log.Any(l => l.Contains("2/2 B 통과") && l.Contains("ok")));
            Assert.IsTrue(log.Last().Contains("성공"));
        }

        [Test]
        public void Runner_Timeout_FailsWithStepName()
        {
            var log = new List<string>();
            var runner = new SmokeRunner(new[] { Step("Wait", c => { c.Detail = "대기 중"; return SmokeStatus.Running; }) }, 60f, log.Add);
            runner.Tick(100f);
            runner.Tick(159.9f);
            Assert.IsFalse(runner.IsFinished, "60초 안은 계속");
            runner.Tick(160.1f);
            Assert.AreEqual(SmokeStatus.Failed, runner.Status);
            Assert.AreEqual(1, runner.ExitCode);
            StringAssert.Contains("시간 초과", runner.FailureReason);
            StringAssert.Contains("Wait", runner.FailureReason);
            StringAssert.Contains("대기 중", runner.FailureReason);
            Assert.IsTrue(log.Last().Contains("실패"));
        }

        [Test]
        public void Runner_TimeoutCountsWholeRun_NotPerStep()
        {
            var runner = new SmokeRunner(new[]
            {
                Step("A", c => c.ElapsedSec >= 40f ? SmokeStatus.Passed : SmokeStatus.Running),
                Step("B", c => c.ElapsedSec >= 40f ? SmokeStatus.Passed : SmokeStatus.Running),
            }, 60f);
            for (float t = 0f; t <= 70f && !runner.IsFinished; t += 1f)
                runner.Tick(t);
            Assert.AreEqual(SmokeStatus.Failed, runner.Status);
            Assert.AreEqual("B", runner.CurrentStepName);
        }

        [Test]
        public void Runner_StepFailOrException_ExitCodeOne()
        {
            var failed = new SmokeRunner(new[] { Step("F", c => { c.Detail = "이유"; return SmokeStatus.Failed; }) });
            failed.Tick(0f);
            Assert.AreEqual(1, failed.ExitCode);
            StringAssert.Contains("이유", failed.FailureReason);

            var thrown = new SmokeRunner(new[] { Step("X", c => throw new InvalidOperationException("터짐")) });
            thrown.Tick(0f);
            Assert.AreEqual(SmokeStatus.Failed, thrown.Status);
            StringAssert.Contains("터짐", thrown.FailureReason);
        }

        [Test]
        public void Runner_ExternalFail_StopsAndIgnoresLaterTicks()
        {
            int calls = 0;
            var runner = new SmokeRunner(new[] { Step("A", c => { calls++; return SmokeStatus.Running; }) });
            runner.Tick(0f);
            runner.Fail("예외 로그: NullReferenceException");
            runner.Tick(1f);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(1, runner.ExitCode);
            runner.Fail("두 번째");
            StringAssert.Contains("NullReference", runner.FailureReason);
        }

        // ── 단계 표 끝까지 (가짜 씬) ──

        [Test]
        public void Scenario_FullFlow_Passes_SaveVersionCurrent()
        {
            var sim = new FlowSim();
            var log = new List<string>();
            var runner = new SmokeRunner(SmokeScenario.Build(sim.Env), 60f, log.Add);
            sim.Run(runner);

            Assert.AreEqual(SmokeStatus.Passed, runner.Status, runner.FailureReason);
            Assert.AreEqual(0, runner.ExitCode);
            foreach (string name in SmokeScenario.StepNames)
                Assert.IsTrue(log.Any(l => l.Contains($" {name} 통과")), $"단계 로그 {name}");
            CollectionAssert.AreEqual(new[] { SceneNames.Hideout, SceneNames.RaidCrimsonBasin, SceneNames.Hideout }, sim.Recorder.Loads);
            Assert.IsTrue(sim.Storm.StartRequested, "폭풍 강제 시작 요청");
            Assert.IsTrue(sim.AbandonRequested);
            SaveData saved = sim.Storage.LastWritten();
            Assert.AreEqual(SaveSerializer.CurrentVersion, saved.version);
            Assert.IsFalse(saved.raidInProgress);
            Assert.AreEqual(GamePhase.Hideout, sim.Flow.Phase);
        }

        [Test]
        public void Scenario_StormNeverStarts_TimesOut()
        {
            var sim = new FlowSim { StormIgnoresRequest = true };
            var runner = new SmokeRunner(SmokeScenario.Build(sim.Env), 60f);
            sim.Run(runner);
            Assert.AreEqual(SmokeStatus.Failed, runner.Status);
            StringAssert.Contains("시간 초과", runner.FailureReason);
            StringAssert.Contains(SmokeScenario.StepForceStorm, runner.FailureReason);
        }

        [Test]
        public void Scenario_SaveWriteFails_FailsAtHideoutSaveLoad()
        {
            var sim = new FlowSim();
            sim.Storage.WriteFails = true;
            var runner = new SmokeRunner(SmokeScenario.Build(sim.Env), 60f);
            LogAssertIgnore(() => sim.Run(runner));
            Assert.AreEqual(SmokeStatus.Failed, runner.Status);
            Assert.AreEqual(SmokeScenario.StepHideoutSaveLoad, runner.CurrentStepName);
            StringAssert.Contains("저장 실패", runner.FailureReason);
        }

        private static void LogAssertIgnore(Action act)
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                act();
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }
        }

        // 씬 로드를 흉내 내는 시뮬레이터: 요청된 씬을 다음 프레임에 "로드"한다(Awake·OnEnable 등록 → sceneLoaded 순서).
        private sealed class FlowSim
        {
            public readonly FlowRecorder Recorder = new FlowRecorder();
            public readonly MemorySaveStorage Storage = new MemorySaveStorage();
            public readonly ServiceRegistry Services = new ServiceRegistry();
            public readonly EventBus Events = new EventBus();
            public readonly ProfileService Profiles;
            public readonly GameFlow Flow;
            public readonly SmokeEnvironment Env;
            public readonly FakeStorm Storm = new FakeStorm();
            public readonly FakeRaid Raid = new FakeRaid();
            public bool StormIgnoresRequest;
            public bool AbandonRequested;

            private readonly FakeCatalog catalog = FakeCatalog.Standard();
            private readonly FakeClock clock = new FakeClock();
            private readonly FakeInventoryService inventory = new FakeInventoryService();
            private HideoutService hideout;
            private int handledLoads;

            public FlowSim()
            {
                Profiles = new ProfileService(Storage, clock.Now, _ => { });
                Flow = Recorder.Create(Profiles);
                Env = new SmokeEnvironment(Flow, Services, Events, Profiles, Storage);
                Events.Subscribe<AshStormStartRequested>(_ =>
                {
                    Storm.StartRequested = true;
                    if (!StormIgnoresRequest)
                        Storm.Phase = AshStormPhase.Rising;
                });
                Events.Subscribe<RaidAbandonRequested>(_ =>
                {
                    AbandonRequested = true;
                    Flow.CompleteRaid(new RaidResult(RaidOutcome.Abandoned, null, null, false, default, Raid.ElapsedSec, 0,
                        null, null, null));
                });
            }

            public void Run(SmokeRunner runner)
            {
                const float dt = 0.05f;
                float t = 0f;
                for (int frame = 0; frame < 5000 && !runner.IsFinished; frame++)
                {
                    if (frame == 2)
                        Flow.OnSceneLoaded(SceneNames.MainMenu);   // Boot의 BootLoader
                    LoadPendingScene();
                    if (Flow.Phase == GamePhase.Raid)
                        Raid.ElapsedSec += dt;
                    runner.Tick(t);
                    t += dt;
                }
            }

            private void LoadPendingScene()
            {
                if (handledLoads >= Recorder.Loads.Count)
                    return;
                string scene = Recorder.Loads[handledLoads++];
                // 이전 씬 내리기 (OnDisable 해제)
                if (hideout != null)
                {
                    Services.Unregister<IHideoutService>(hideout);
                    hideout = null;
                }
                Services.Unregister<IRaidInfo>(Raid);
                Services.Unregister<IAshStormInfo>(Storm);

                if (scene == SceneNames.Hideout)
                {
                    // HideoutController.Awake: W1 진입 → 서비스 → OnEnable 등록
                    HideoutService.Enter(Profiles, Flow, catalog, M5TestData.Defaults());
                    hideout = new HideoutService(Profiles, Flow, M5TestData.HideoutConfig(catalog), M5TestData.TraderConfig(catalog),
                        () => inventory, clock.Now, () => { });
                    Services.Register<IHideoutService>(hideout);
                }
                else if (scene == SceneNames.RaidCrimsonBasin)
                {
                    Raid.ElapsedSec = 0f;
                    Storm.Phase = AshStormPhase.Pending;
                    Services.Register<IRaidInfo>(Raid);
                    Services.Register<IAshStormInfo>(Storm);
                }
                Flow.OnSceneLoaded(scene);
            }
        }

        private sealed class FakeStorm : IAshStormInfo
        {
            public bool StartRequested;
            public AshStormPhase Phase { get; set; }
            public float Intensity => Phase == AshStormPhase.Pending ? 0f : 0.5f;
            public float VisibilityM => float.PositiveInfinity;
            public float RadiationPerSec => 0f;
        }

        private sealed class FakeRaid : IRaidInfo
        {
            public float ElapsedSec { get; set; }
            public float RemainingSec => 1500f - ElapsedSec;
            public Rect MapAreaXZ => default;
            public IReadOnlyList<MapMarker> PointsOfInterest => Array.Empty<MapMarker>();
            public IReadOnlyList<MapMarker> ActiveExtractions => Array.Empty<MapMarker>();
        }
    }
}
