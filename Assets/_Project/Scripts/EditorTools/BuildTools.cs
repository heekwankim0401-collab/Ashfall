using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // §15 "각 마일스톤 종료 시 빌드 가능 상태" 확인용 Windows 빌드 (M7 설계 §1.9, Q51~Q53).
    // 빌드 설정(EditorBuildSettings)의 활성 씬을 그대로 쓰고, 출력은 Builds/(git 제외)에 둔다.
    // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.BuildTools.<메서드> -logFile build.log
    // 실패하면 배치모드 종료 코드가 0이 아니도록 예외를 던진다.
    // 7D-IL2CPP (§13.1 "IL2CPP 빌드"): ProjectSettings의 Standalone 백엔드 = IL2CPP(SetStandaloneBackendIl2Cpp가 영구 전환).
    // - BuildWindows64: 릴리스(IL2CPP) → Builds/Windows64/. 진단 컴포넌트 없음.
    // - BuildWindows64Development: 개발 빌드(IL2CPP) → Builds/Windows64Dev/. DevDiagnostics(프레임 시간·-ashfallSmoke)가 붙는다.
    // - BuildWindows64Il2Cpp(·Development): 이전 이름 그대로(출력 Builds/Windows64Il2Cpp*/). 백엔드가 이미 IL2CPP라 되돌림은 무동작.
    // - BuildWindows64Mono(·Development): 대체 경로(M7 설계 §1.9) → Builds/Windows64Mono*/. 이 빌드 동안만 백엔드를 Mono로 바꾸고 되돌린다.
    // IL2CPP 빌드는 모듈을 먼저 확인한다(없으면 "IL2CPP 모듈 설치 필요" 예외). IL2CPP 개발 빌드는 이 빌드 동안만 스택 추적에 파일·줄 번호.
    public static class BuildTools
    {
        private const string Tag = "[BuildTools]";
        public const string OutputPath = "Builds/Windows64/Ashfall.exe";
        public const string DevelopmentOutputPath = "Builds/Windows64Dev/Ashfall.exe";
        public const string Il2CppOutputPath = "Builds/Windows64Il2Cpp/Ashfall.exe";
        public const string Il2CppDevelopmentOutputPath = "Builds/Windows64Il2CppDev/Ashfall.exe";
        public const string MonoOutputPath = "Builds/Windows64Mono/Ashfall.exe";
        public const string MonoDevelopmentOutputPath = "Builds/Windows64MonoDev/Ashfall.exe";

        // ProjectSettings에 고정된 Standalone 백엔드 (§13.1, 테스트가 확인한다)
        public const ScriptingImplementation ProjectBackend = ScriptingImplementation.IL2CPP;
        // 릴리스 스택 추적(기본값). 키를 ProjectSettings에 명시해 두어 개발 빌드의 임시 변경·되돌림 뒤에도 파일이 같다.
        public const Il2CppStacktraceInformation ProjectStacktrace = Il2CppStacktraceInformation.MethodOnly;

        // IL2CPP 모듈 없음 예외 메시지 앞부분 (테스트가 확인한다)
        public const string Il2CppMissingMessage = "IL2CPP 모듈 설치 필요";
        // Windows 플레이어 변형 폴더 이름 규칙: win64_player_{development|nondevelopment}_{mono|il2cpp}
        public const string Win64VariationPrefix = "win64_";
        public const string Il2CppVariationSuffix = "_il2cpp";

        [MenuItem("Ashfall/Build/Windows64")]
        public static void BuildWindows64() => BuildWithBackend(ScriptingImplementation.IL2CPP, OutputPath, BuildOptions.None);

        [MenuItem("Ashfall/Build/Windows64 (Development)")]
        public static void BuildWindows64Development() =>
            BuildWithBackend(ScriptingImplementation.IL2CPP, DevelopmentOutputPath, BuildOptions.Development);

        [MenuItem("Ashfall/Build/Windows64 IL2CPP")]
        public static void BuildWindows64Il2Cpp() => BuildWithBackend(ScriptingImplementation.IL2CPP, Il2CppOutputPath, BuildOptions.None);

        [MenuItem("Ashfall/Build/Windows64 IL2CPP (Development)")]
        public static void BuildWindows64Il2CppDevelopment() =>
            BuildWithBackend(ScriptingImplementation.IL2CPP, Il2CppDevelopmentOutputPath, BuildOptions.Development);

        [MenuItem("Ashfall/Build/Windows64 Mono")]
        public static void BuildWindows64Mono() => BuildWithBackend(ScriptingImplementation.Mono2x, MonoOutputPath, BuildOptions.None);

        [MenuItem("Ashfall/Build/Windows64 Mono (Development)")]
        public static void BuildWindows64MonoDevelopment() =>
            BuildWithBackend(ScriptingImplementation.Mono2x, MonoDevelopmentOutputPath, BuildOptions.Development);

        // 7D-IL2CPP 영구 전환: ProjectSettings의 Standalone 백엔드를 IL2CPP로, 스택 추적은 기본(MethodOnly)을 명시한다.
        // 에디터 API로만 바꾼다(YAML 손작성 금지). 다시 실행해도 같다.
        // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.BuildTools.SetStandaloneBackendIl2Cpp
        [MenuItem("Ashfall/Build/Set Standalone Backend IL2CPP")]
        public static void SetStandaloneBackendIl2Cpp()
        {
            EnsureIl2CppAvailable();
            NamedBuildTarget target = NamedBuildTarget.Standalone;
            ScriptingImplementation before = PlayerSettings.GetScriptingBackend(target);
            PlayerSettings.SetScriptingBackend(target, ProjectBackend);
            PlayerSettings.SetIl2CppStacktraceInformation(target, ProjectStacktrace);
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} Standalone 스크립팅 백엔드 {before} → {PlayerSettings.GetScriptingBackend(target)}, " +
                      $"스택 추적 {PlayerSettings.GetIl2CppStacktraceInformation(target)} (ProjectSettings 저장)");
        }

        // ── IL2CPP 모듈 판정 (순수) ──

        // 변형 폴더 이름 목록에 win64 IL2CPP 플레이어가 하나라도 있나 (대소문자 무시)
        public static bool HasWin64Il2CppVariation(IEnumerable<string> variationDirectoryNames)
        {
            if (variationDirectoryNames == null)
                return false;
            foreach (string name in variationDirectoryNames)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                if (name.StartsWith(Win64VariationPrefix, StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(Il2CppVariationSuffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // IL2CPP 빌드 가능 여부. 불가면 이유(사람이 할 일 포함)를 돌려준다. 가능하면 null.
        public static string DescribeIl2CppProblem(bool targetSupported, IEnumerable<string> variationDirectoryNames, string variationsPath)
        {
            if (!targetSupported)
                return $"{Il2CppMissingMessage}: Windows 빌드 지원(StandaloneWindows64)이 설치되지 않았다. " +
                       "Unity Hub → 6000.3.26f1 → 모듈 추가 → \"Windows Build Support (IL2CPP)\".";
            if (!HasWin64Il2CppVariation(variationDirectoryNames))
                return $"{Il2CppMissingMessage}: win64 IL2CPP 플레이어 변형(win64_*{Il2CppVariationSuffix})이 없다 ({variationsPath}). " +
                       "Unity Hub → 6000.3.26f1 → 모듈 추가 → \"Windows Build Support (IL2CPP)\", " +
                       "그리고 Visual Studio 2022 또는 2026 \"C++를 사용한 데스크톱 개발\"(MSVC·Windows SDK)을 설치할 것.";
            return null;
        }

        // 이 에디터 설치의 실제 확인. 없으면 InvalidOperationException(배치모드 종료 코드 ≠ 0).
        public static void EnsureIl2CppAvailable()
        {
            bool supported = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            string variationsPath = Win64VariationsPath();
            string[] names = Directory.Exists(variationsPath)
                ? Directory.GetDirectories(variationsPath).Select(Path.GetFileName).ToArray()
                : Array.Empty<string>();
            string problem = DescribeIl2CppProblem(supported, names, variationsPath);
            if (problem != null)
                throw new InvalidOperationException($"{Tag} {problem}");
        }

        // <에디터>/Data/PlaybackEngines/windowsstandalonesupport/Variations
        public static string Win64VariationsPath()
        {
            string engine = null;
            try
            {
                engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.StandaloneWindows64, BuildOptions.None);
            }
            catch (Exception)
            {
                // 모듈이 아예 없으면 예외일 수 있다 — 기본 위치로 대신한다
            }
            if (string.IsNullOrEmpty(engine))
                engine = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "windowsstandalonesupport");
            return Path.GetFullPath(Path.Combine(engine, "Variations"));
        }

        // ── 빌드 ──

        // 이 빌드 동안만 백엔드(와 IL2CPP 개발 빌드의 스택 추적)를 맞추고 finally에서 되돌린다. 이미 같으면 건드리지 않는다
        // (영구 전환 뒤 IL2CPP 릴리스는 무동작, IL2CPP 개발은 스택 추적만, Mono는 백엔드만).
        private static void BuildWithBackend(ScriptingImplementation backend, string outputPath, BuildOptions options)
        {
            bool il2cpp = backend == ScriptingImplementation.IL2CPP;
            bool development = (options & BuildOptions.Development) != 0;
            if (il2cpp)
                EnsureIl2CppAvailable();

            NamedBuildTarget target = NamedBuildTarget.Standalone;
            ScriptingImplementation previousBackend = PlayerSettings.GetScriptingBackend(target);
            Il2CppStacktraceInformation previousStack = PlayerSettings.GetIl2CppStacktraceInformation(target);
            bool changeBackend = previousBackend != backend;
            // 개발 빌드는 스택 추적에 파일·줄 번호 (M7 설계 §1.9 IL2CPP 위험 6)
            bool changeStack = il2cpp && development && previousStack != Il2CppStacktraceInformation.MethodFileLineNumber;
            Debug.Log(changeBackend
                ? $"{Tag} 이 빌드 동안만 스크립팅 백엔드 {previousBackend} → {backend}"
                : $"{Tag} 스크립팅 백엔드 {backend} 그대로");
            try
            {
                if (changeBackend)
                    PlayerSettings.SetScriptingBackend(target, backend);
                if (changeStack)
                    PlayerSettings.SetIl2CppStacktraceInformation(target, Il2CppStacktraceInformation.MethodFileLineNumber);
                Build(outputPath, options);
            }
            finally
            {
                if (changeBackend)
                    PlayerSettings.SetScriptingBackend(target, previousBackend);
                if (changeStack)
                    PlayerSettings.SetIl2CppStacktraceInformation(target, previousStack);
                if (changeBackend || changeStack)
                    Debug.Log($"{Tag} 되돌림 → 백엔드 {PlayerSettings.GetScriptingBackend(target)}, " +
                              $"스택 추적 {PlayerSettings.GetIl2CppStacktraceInformation(target)}");
            }
        }

        // 출력 폴더에 다른 백엔드의 이전 빌드가 있나 (순수). Mono 빌드 = MonoBleedingEdge 폴더, IL2CPP 빌드 = GameAssembly.dll.
        // 유니티는 이런 폴더에 빌드하지 않는다("Build path contains a project previously built with the Mono2x scripting backend") —
        // 7D-IL2CPP 영구 전환 뒤 Builds/Windows64(이전 Mono)에서 실제로 실패했다.
        public static bool IsOtherBackendOutput(ScriptingImplementation backend, bool hasMonoRuntimeFolder, bool hasGameAssembly) =>
            backend == ScriptingImplementation.IL2CPP ? hasMonoRuntimeFolder : hasGameAssembly;

        public const string MonoRuntimeFolderName = "MonoBleedingEdge";
        public const string Il2CppGameAssemblyName = "GameAssembly.dll";

        // 다른 백엔드의 이전 빌드면 출력 폴더를 지운다(Builds/ 아래, git 제외 폴더만). 같은 백엔드면 그대로(증분 빌드).
        private static void ClearOtherBackendOutput(string outputPath, ScriptingImplementation backend)
        {
            string dir = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir) || !dir.StartsWith("Builds/", StringComparison.Ordinal) || !Directory.Exists(dir))
                return;
            bool mono = Directory.Exists(Path.Combine(dir, MonoRuntimeFolderName));
            bool il2cpp = File.Exists(Path.Combine(dir, Il2CppGameAssemblyName));
            if (!IsOtherBackendOutput(backend, mono, il2cpp))
                return;
            Debug.Log($"{Tag} {dir}에 다른 백엔드의 이전 빌드가 있어 지운다 (Mono {mono}, IL2CPP {il2cpp})");
            Directory.Delete(dir, true);
        }

        private static void Build(string outputPath, BuildOptions options)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException($"{Tag} 빌드 설정에 활성 씬이 없다.");
            ClearOtherBackendOutput(outputPath, PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone));

            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = options,
            };
            string kind = (options & BuildOptions.Development) != 0 ? "개발" : "릴리스";
            Debug.Log($"{Tag} 빌드 시작 ({kind}, 백엔드 {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone)}): " +
                      $"{string.Join(", ", scenes)} → {outputPath}");
            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            BuildSummary summary = report.summary;
            Debug.Log($"{Tag} 결과 {summary.result}, 오류 {summary.totalErrors}, 경고 {summary.totalWarnings}, " +
                      $"크기 {summary.totalSize} bytes, 시간 {summary.totalTime}");
            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"{Tag} 빌드 실패: {summary.result}");
        }
    }
}
