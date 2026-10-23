using System;
using System.Threading;
using UnityEngine;

namespace Ashfall.Core
{
    // 개발 빌드 진단 (M7 설계 §1.9, Q51). GameManager 오브젝트의 컴포넌트 — GameManager가 개발 빌드 플레이어에서만 붙인다
    // (ShouldAttach: 릴리스 빌드·에디터 Play에는 없음). 정적 인스턴스 없음.
    // - 프레임 시간: LogIntervalSec마다 중앙값·99%·최대 ms와 fps 추정을 Player.log에 한 줄(FrameTimeStats 고정 링 버퍼, 표본 추가 할당 0).
    // - 스모크: 명령줄 "-ashfallSmoke <폴더>"일 때만. 세이브 위치를 그 폴더로 바꾸고(FileSaveStorage, 실제 persistentDataPath 안 씀)
    //   SmokeScenario 단계를 SmokeRunner로 돌린 뒤 종료 코드(성공 0, 실패·예외·시간 초과 1)로 Application.Quit.
    //   폴더 정책(7D 리뷰 낮음 4, SmokeRunner.PrepareSaveFolder): persistentDataPath와 같으면 종료 1, 남은 save.json*·save.corrupt.json은 지우고 시작.
    public sealed class DevDiagnostics : MonoBehaviour
    {
        // TODO(GDD-?) Q51: 기술 상수(게임 수치 아님) — 프레임 시간 로그 간격(실시간 초)
        public const float LogIntervalSec = 10f;
        // TODO(GDD-?) Q51: 기술 상수 — 링 버퍼 용량(10초 × 약 400fps). 한 간격에 이보다 많은 프레임(약 410fps 이상)이 오면
        // 가장 오래된 표본부터 덮어써 그 간격의 최근 구간만 통계에 들어간다 — 로그의 "전체 n개"(TotalAdded)가 용량보다 크면 그 경우다.
        public const int FrameSampleCapacity = 4096;
        public const string LogTag = "[DevDiagnostics]";

        private FrameTimeStats stats;
        private float nextLogAt;
        private SmokeRunner smoke;
        private bool smokeArgumentError;
        private bool quitRequested;
        // 로그 콜백은 logMessageReceivedThreaded라 아무 스레드에서나 온다(7D 리뷰 낮음 6) → 카운터는 Interlocked, 첫 예외 문구는
        // CompareExchange로 한 번만 기록하고 메인 스레드 Update가 읽어 스모크를 실패시킨다.
        private int errorLogCount;
        private int exceptionLogCount;
        private string firstException;

        // 테스트·도구
        public FrameTimeStats Stats => stats;
        public SmokeRunner Smoke => smoke;

        // 개발 빌드 플레이어에서만 (에디터 Play는 Debug.isDebugBuild가 늘 true라 따로 뺀다)
        public static bool ShouldAttach(bool isDebugBuild, bool isEditor) => isDebugBuild && !isEditor;

        private void Awake()
        {
            stats = new FrameTimeStats(FrameSampleCapacity);
            nextLogAt = Time.realtimeSinceStartup + LogIntervalSec;
            Debug.Log($"{LogTag} 개발 빌드 진단 켜짐 — 프레임 시간 {LogIntervalSec:0}초마다 기록");

            SmokeArguments args = SmokeRunner.ParseArguments(Environment.GetCommandLineArgs());
            if (!args.Requested)
                return;
            Application.logMessageReceivedThreaded += OnLogMessage;
            if (!args.IsValid)
            {
                smokeArgumentError = true;
                Debug.LogError($"{SmokeRunner.LogTag} {args.Error} 종료 코드 {SmokeRunner.FailureExitCode}");
                return;
            }

            GameManager gm = GetComponent<GameManager>();
            if (gm == null || gm.Flow == null || gm.Profiles == null)
            {
                smokeArgumentError = true;
                Debug.LogError($"{SmokeRunner.LogTag} GameManager가 준비되지 않았다. 종료 코드 {SmokeRunner.FailureExitCode}");
                return;
            }
            // 첫 은신처 진입(EnsureLoaded) 전에 바꾼다 — 실제 세이브를 읽거나 쓰지 않게
            var storage = new FileSaveStorage(args.Folder);
            int deleted = SmokeRunner.PrepareSaveFolder(storage, Application.persistentDataPath, out string folderError);
            if (deleted < 0)
            {
                smokeArgumentError = true;
                Debug.LogError($"{SmokeRunner.LogTag} {folderError} 종료 코드 {SmokeRunner.FailureExitCode}");
                return;
            }
            gm.UseSaveStorage(storage);
            Debug.Log($"{SmokeRunner.LogTag} 세이브 폴더 {args.Folder} ({storage.FilePath}), 남은 세이브 파일 {deleted}개 지움");
            var env = new SmokeEnvironment(gm.Flow, gm.Services, gm.Events, gm.Profiles, storage);
            smoke = new SmokeRunner(SmokeScenario.Build(env), SmokeRunner.DefaultTimeoutSec, Debug.Log);
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLogMessage;
        }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            stats.Add(Time.unscaledDeltaTime * 1000f);
            if (now >= nextLogAt)
            {
                LogFrameTimes();
                stats.Clear();
                nextLogAt = now + LogIntervalSec;
            }

            if (quitRequested)
                return;
            if (smokeArgumentError)
            {
                Quit(SmokeRunner.FailureExitCode);
                return;
            }
            if (smoke == null)
                return;
            string exception = Volatile.Read(ref firstException);
            if (exception != null)
                smoke.Fail($"예외 로그: {exception}");
            smoke.Tick(now);
            if (smoke.IsFinished)
            {
                Debug.Log($"{SmokeRunner.LogTag} 종료 — 코드 {smoke.ExitCode}, 오류 로그 {Volatile.Read(ref errorLogCount)}개, " +
                          $"예외 로그 {Volatile.Read(ref exceptionLogCount)}개" +
                          (smoke.FailureReason != null ? $", 이유: {smoke.FailureReason}" : ""));
                Quit(smoke.ExitCode);
            }
        }

        private void LogFrameTimes()
        {
            FrameTimeSummary s = stats.Compute();
            if (s.Count == 0)
                return;
            Debug.Log($"{LogTag} 프레임 {s.Count}개(전체 {stats.TotalAdded}개, 버퍼 {stats.Capacity}) — 중앙값 {s.MedianMs:0.00}ms, 99% {s.P99Ms:0.00}ms, " +
                      $"최대 {s.MaxMs:0.00}ms, fps 약 {s.Fps:0} (평균 {s.MeanMs:0.00}ms)");
        }

        // 스모크 중 처리되지 않은 예외(버스가 삼킨 핸들러 예외 포함 — LogException, 다른 스레드 예외 포함) = 실패.
        // 오류 로그는 세어서 마지막에 알린다. 아무 스레드에서나 불린다 — Unity API·smoke를 직접 만지지 않는다.
        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception)
            {
                Interlocked.Increment(ref exceptionLogCount);
                Interlocked.CompareExchange(ref firstException, condition ?? "(내용 없음)", null);
            }
            else if (type == LogType.Error || type == LogType.Assert)
            {
                Interlocked.Increment(ref errorLogCount);
            }
        }

        private void Quit(int exitCode)
        {
            quitRequested = true;
            Application.Quit(exitCode);
        }
    }
}
