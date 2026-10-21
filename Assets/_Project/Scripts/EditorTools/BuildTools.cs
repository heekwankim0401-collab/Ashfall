using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // §15 "각 마일스톤 종료 시 빌드 가능 상태" 확인용 Windows 빌드.
    // 빌드 설정(EditorBuildSettings)의 활성 씬을 그대로 쓰고, 출력은 Builds/(git 제외)에 둔다.
    // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.BuildTools.BuildWindows64 -logFile build.log
    // 실패하면 배치모드 종료 코드가 0이 아니도록 예외를 던진다.
    public static class BuildTools
    {
        private const string Tag = "[BuildTools]";
        public const string OutputPath = "Builds/Windows64/Ashfall.exe";

        [MenuItem("Ashfall/Build/Windows64")]
        public static void BuildWindows64()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException($"{Tag} 빌드 설정에 활성 씬이 없다.");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            Debug.Log($"{Tag} 빌드 시작: {string.Join(", ", scenes)} → {OutputPath}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"{Tag} 결과 {summary.result}, 오류 {summary.totalErrors}, 경고 {summary.totalWarnings}, " +
                      $"크기 {summary.totalSize} bytes, 시간 {summary.totalTime}");
            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"{Tag} 빌드 실패: {summary.result}");
        }
    }
}
