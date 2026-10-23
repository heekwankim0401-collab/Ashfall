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
    // - BuildWindows64: 릴리스(현재 백엔드 = Mono). 진단 컴포넌트 없음.
    // - BuildWindows64Development: 개발 빌드(Development) → Builds/Windows64Dev/. DevDiagnostics(프레임 시간·-ashfallSmoke)가 붙는다.
    // - BuildWindows64Il2Cpp(·Development): IL2CPP 모듈을 먼저 확인하고, 이 빌드 동안만 백엔드를 IL2CPP로 바꾼 뒤 되돌린다.
    //   ProjectSettings의 백엔드는 바꾸지 않는다(영구 전환은 사용자 설치 확인 뒤 7D-IL2CPP 단계).
    public static class BuildTools
    {
        private const string Tag = "[BuildTools]";
        public const string OutputPath = "Builds/Windows64/Ashfall.exe";
        public const string DevelopmentOutputPath = "Builds/Windows64Dev/Ashfall.exe";
        public const string Il2CppOutputPath = "Builds/Windows64Il2Cpp/Ashfall.exe";
        public const string Il2CppDevelopmentOutputPath = "Builds/Windows64Il2CppDev/Ashfall.exe";

        // IL2CPP 모듈 없음 예외 메시지 앞부분 (테스트가 확인한다)
        public const string Il2CppMissingMessage = "IL2CPP 모듈 설치 필요";
        // Windows 플레이어 변형 폴더 이름 규칙: win64_player_{development|nondevelopment}_{mono|il2cpp}
        public const string Win64VariationPrefix = "win64_";
        public const string Il2CppVariationSuffix = "_il2cpp";

        [MenuItem("Ashfall/Build/Windows64")]
        public static void BuildWindows64() => Build(OutputPath, BuildOptions.None);

        [MenuItem("Ashfall/Build/Windows64 (Development)")]
        public static void BuildWindows64Development() => Build(DevelopmentOutputPath, BuildOptions.Development);

        [MenuItem("Ashfall/Build/Windows64 IL2CPP")]
        public static void BuildWindows64Il2Cpp() => BuildIl2Cpp(Il2CppOutputPath, BuildOptions.None);

        [MenuItem("Ashfall/Build/Windows64 IL2CPP (Development)")]
        public static void BuildWindows64Il2CppDevelopment() => BuildIl2Cpp(Il2CppDevelopmentOutputPath, BuildOptions.Development);

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
                       "그리고 Visual Studio 2022 \"C++를 사용한 데스크톱 개발\"(MSVC v143·Windows SDK)을 설치할 것.";
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

        private static void BuildIl2Cpp(string outputPath, BuildOptions options)
        {
            EnsureIl2CppAvailable();

            NamedBuildTarget target = NamedBuildTarget.Standalone;
            ScriptingImplementation previousBackend = PlayerSettings.GetScriptingBackend(target);
            Il2CppStacktraceInformation previousStack = PlayerSettings.GetIl2CppStacktraceInformation(target);
            Debug.Log($"{Tag} 이 빌드 동안만 스크립팅 백엔드 {previousBackend} → IL2CPP");
            try
            {
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
                // 개발 빌드는 스택 추적에 파일·줄 번호 (M7 설계 §1.9 IL2CPP 위험 6)
                if ((options & BuildOptions.Development) != 0)
                    PlayerSettings.SetIl2CppStacktraceInformation(target, Il2CppStacktraceInformation.MethodFileLineNumber);
                Build(outputPath, options);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, previousBackend);
                PlayerSettings.SetIl2CppStacktraceInformation(target, previousStack);
                Debug.Log($"{Tag} 스크립팅 백엔드 되돌림 → {PlayerSettings.GetScriptingBackend(target)}");
            }
        }

        private static void Build(string outputPath, BuildOptions options)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException($"{Tag} 빌드 설정에 활성 씬이 없다.");

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
