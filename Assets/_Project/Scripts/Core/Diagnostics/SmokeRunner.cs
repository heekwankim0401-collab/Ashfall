using System;
using System.Collections.Generic;
using System.IO;

namespace Ashfall.Core
{
    // 스모크 단계 한 틱의 결과
    public enum SmokeStatus
    {
        Running,
        Passed,
        Failed,
    }

    // 단계 함수가 받는 상태. 단계가 바뀔 때마다 새로 시작한다.
    public sealed class SmokeStepContext
    {
        public int Tick { get; internal set; }          // 이 단계에서 몇 번째 틱인가 (0 = 첫 틱)
        public float ElapsedSec { get; internal set; }  // 이 단계 시작부터 실시간(초)
        public bool IsFirstTick => Tick == 0;
        // 통과·실패 설명 (로그 한 줄에 붙는다). 단계 함수가 채운다.
        public string Detail { get; set; }
    }

    // 스모크 단계 표 한 줄 (M7 설계 §1.9, Q51)
    public readonly struct SmokeStep
    {
        public readonly string Name;
        public readonly Func<SmokeStepContext, SmokeStatus> Run;

        public SmokeStep(string name, Func<SmokeStepContext, SmokeStatus> run)
        {
            Name = name;
            Run = run ?? throw new ArgumentNullException(nameof(run));
        }
    }

    // 명령줄 해석 결과. Requested = 인자가 있었다. Folder가 null이면(값 없음) Error에 이유.
    public readonly struct SmokeArguments
    {
        public readonly bool Requested;
        public readonly string Folder;
        public readonly string Error;

        public SmokeArguments(bool requested, string folder, string error)
        {
            Requested = requested;
            Folder = folder;
            Error = error;
        }

        public bool IsValid => Requested && Folder != null;
    }

    // 개발 빌드 스모크 실행기 (M7 설계 §1.9, Q51). 순수 C#: 단계 표를 차례로 돌리고 시간 초과·예외를 판정한다.
    // 실제 흐름 단계는 SmokeScenario.Build가 만든다(Core 서비스·이벤트·GameFlow만 사용). DevDiagnostics가 매 프레임 Tick하고
    // 끝나면 ExitCode로 Application.Quit한다. 성공 0, 실패·예외·시간 초과 1.
    public sealed class SmokeRunner
    {
        public const string ArgumentName = "-ashfallSmoke";
        // TODO(GDD-?) Q51: 기술 상수(게임 수치 아님) — 스모크 전체 제한 시간(실시간). 넘으면 실패(종료 코드 1).
        public const float DefaultTimeoutSec = 60f;
        public const int SuccessExitCode = 0;
        public const int FailureExitCode = 1;
        public const string LogTag = "[Smoke]";

        private readonly IReadOnlyList<SmokeStep> steps;
        private readonly float timeoutSec;
        private readonly Action<string> log;
        private readonly SmokeStepContext context = new SmokeStepContext();
        private bool started;
        private float startSec;
        private float stepStartSec;

        public SmokeRunner(IReadOnlyList<SmokeStep> steps, float timeoutSec = DefaultTimeoutSec, Action<string> log = null)
        {
            if (steps == null || steps.Count == 0)
                throw new ArgumentException("스모크 단계가 없다.", nameof(steps));
            if (!(timeoutSec > 0f))
                throw new ArgumentOutOfRangeException(nameof(timeoutSec));
            this.steps = steps;
            this.timeoutSec = timeoutSec;
            this.log = log ?? (_ => { });
        }

        public SmokeStatus Status { get; private set; } = SmokeStatus.Running;
        public int StepIndex { get; private set; }
        public int StepCount => steps.Count;
        public string CurrentStepName => StepIndex < steps.Count ? steps[StepIndex].Name : null;
        public string FailureReason { get; private set; }
        // 끝나지 않았으면 -1
        public int ExitCode => Status == SmokeStatus.Passed ? SuccessExitCode : Status == SmokeStatus.Failed ? FailureExitCode : -1;
        public bool IsFinished => Status != SmokeStatus.Running;
        // 첫 Tick부터 마지막 Tick까지 실시간(초)
        public float ElapsedSec { get; private set; }

        // 명령줄에서 "-ashfallSmoke <폴더>"를 찾는다 (이름은 대소문자 무시). 상대 경로는 현재 폴더 기준 절대 경로로.
        // 값이 없거나 다음 인자가 '-'로 시작하면 Requested = true, Folder = null(실패로 끝낸다).
        public static SmokeArguments ParseArguments(IReadOnlyList<string> args)
        {
            if (args == null)
                return new SmokeArguments(false, null, null);
            for (int i = 0; i < args.Count; i++)
            {
                if (!string.Equals(args[i], ArgumentName, StringComparison.OrdinalIgnoreCase))
                    continue;
                string value = i + 1 < args.Count ? args[i + 1] : null;
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("-", StringComparison.Ordinal))
                    return new SmokeArguments(true, null, $"{ArgumentName} 뒤에 세이브 폴더가 없다.");
                string folder;
                try
                {
                    folder = Path.GetFullPath(value.Trim().Trim('"'));
                }
                catch (Exception e)
                {
                    return new SmokeArguments(true, null, $"{ArgumentName} 폴더 경로가 잘못됐다: {e.Message}");
                }
                return new SmokeArguments(true, folder, null);
            }
            return new SmokeArguments(false, null, null);
        }

        // 두 폴더가 같은 곳인가 (절대 경로·끝 구분자·대소문자 무시 — Windows 기준). 하나라도 비었으면 false.
        public static bool IsSameFolder(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            try
            {
                string na = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string nb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // 스모크 세이브 폴더 정책 (7D 리뷰 낮음 4): ① 실제 세이브 폴더(persistentDataPath)와 같으면 거부(실제 세이브를 지우거나 덮지 않게) →
        // 실패(종료 코드 1). ② 폴더가 없으면 만든다. ③ 이전 실행이 남긴 세이브 파일(save.json·.bak·.tmp, save.corrupt.json —
        // FileSaveStorage가 쓰는 이름만)을 지우고 빈 프로필로 시작한다(이전 save.json을 그대로 읽지 않게). 다른 파일은 건드리지 않는다.
        // 지운 파일 수를 돌려준다. 실패면 -1과 error.
        public static int PrepareSaveFolder(FileSaveStorage storage, string realSaveFolder, out string error)
        {
            error = null;
            if (storage == null)
            {
                error = "세이브 저장소가 없다.";
                return -1;
            }
            if (IsSameFolder(storage.DirectoryPath, realSaveFolder))
            {
                error = $"스모크 세이브 폴더가 실제 세이브 폴더(persistentDataPath)와 같다: {storage.DirectoryPath} — 다른 폴더를 준다.";
                return -1;
            }
            try
            {
                Directory.CreateDirectory(storage.DirectoryPath);
                int deleted = 0;
                foreach (string path in new[] { storage.FilePath, storage.BackupPath, storage.TempPath, storage.CorruptPath })
                {
                    if (!File.Exists(path))
                        continue;
                    File.Delete(path);
                    deleted++;
                }
                return deleted;
            }
            catch (Exception e)
            {
                error = $"스모크 세이브 폴더 준비 실패 ({storage.DirectoryPath}): {e.GetType().Name}: {e.Message}";
                return -1;
            }
        }

        // nowSec = 실시간(timeScale과 무관). 한 틱에 한 단계까지만 진행한다. 끝났으면 아무것도 하지 않는다.
        public SmokeStatus Tick(float nowSec)
        {
            if (Status != SmokeStatus.Running)
                return Status;
            if (!started)
            {
                started = true;
                startSec = nowSec;
                BeginStep(nowSec);
                log($"{LogTag} 시작 — 단계 {steps.Count}개, 제한 {timeoutSec:0}초");
            }
            ElapsedSec = nowSec - startSec;
            if (ElapsedSec > timeoutSec)
            {
                Fail($"시간 초과 ({timeoutSec:0}초) — 단계 {CurrentStepName}에서 멈춤" +
                     (string.IsNullOrEmpty(context.Detail) ? "" : " (" + context.Detail + ")"));
                return Status;
            }

            SmokeStep step = steps[StepIndex];
            context.ElapsedSec = nowSec - stepStartSec;
            SmokeStatus result;
            try
            {
                result = step.Run(context);
            }
            catch (Exception e)
            {
                Fail($"단계 {step.Name} 예외 {e.GetType().Name}: {e.Message}");
                return Status;
            }

            switch (result)
            {
                case SmokeStatus.Passed:
                    log($"{LogTag} 단계 {StepIndex + 1}/{steps.Count} {step.Name} 통과 ({ElapsedSec:0.0}초)" +
                        (string.IsNullOrEmpty(context.Detail) ? "" : " — " + context.Detail));
                    StepIndex++;
                    if (StepIndex >= steps.Count)
                    {
                        Status = SmokeStatus.Passed;
                        log($"{LogTag} 성공 — 전체 {ElapsedSec:0.0}초, 종료 코드 {SuccessExitCode}");
                    }
                    else
                    {
                        BeginStep(nowSec);
                    }
                    break;
                case SmokeStatus.Failed:
                    Fail($"단계 {step.Name} 실패" + (string.IsNullOrEmpty(context.Detail) ? "" : " — " + context.Detail));
                    break;
                default:
                    context.Tick++;
                    break;
            }
            return Status;
        }

        // 바깥(예외 로그 감시 등)에서 실패시킨다. 이미 끝났으면 무시.
        public void Fail(string reason)
        {
            if (Status != SmokeStatus.Running)
                return;
            Status = SmokeStatus.Failed;
            FailureReason = reason;
            log($"{LogTag} 실패 — {reason} (종료 코드 {FailureExitCode})");
        }

        private void BeginStep(float nowSec)
        {
            stepStartSec = nowSec;
            context.Tick = 0;
            context.ElapsedSec = 0f;
            context.Detail = null;
        }
    }
}
