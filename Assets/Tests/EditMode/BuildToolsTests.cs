using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ashfall.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7D 빌드 메서드 (M7 설계 §1.9, Q52·Q53): IL2CPP 모듈 판정(순수) + 이 PC에서의 실제 확인, link.xml, 백엔드 IL2CPP 고정(7D-IL2CPP).
    public class BuildToolsTests
    {
        [Test]
        public void Il2CppVariation_Detection()
        {
            Assert.IsFalse(BuildTools.HasWin64Il2CppVariation(null));
            Assert.IsFalse(BuildTools.HasWin64Il2CppVariation(new string[0]));
            Assert.IsFalse(BuildTools.HasWin64Il2CppVariation(new[]
            {
                "win64_player_development_mono", "win64_player_nondevelopment_mono", "win32_player_development_mono",
            }), "Mono 변형만 있으면 없음");
            Assert.IsFalse(BuildTools.HasWin64Il2CppVariation(new[] { "win32_player_nondevelopment_il2cpp" }), "32비트 IL2CPP는 아님");
            Assert.IsTrue(BuildTools.HasWin64Il2CppVariation(new[] { "win64_player_development_mono", "win64_player_nondevelopment_il2cpp" }));
            Assert.IsTrue(BuildTools.HasWin64Il2CppVariation(new[] { "WIN64_player_development_IL2CPP" }));
        }

        [Test]
        public void Il2CppProblem_Messages()
        {
            string[] mono = { "win64_player_nondevelopment_mono" };
            string[] il2cpp = { "win64_player_nondevelopment_il2cpp" };
            Assert.IsNull(BuildTools.DescribeIl2CppProblem(true, il2cpp, "X"));

            string noTarget = BuildTools.DescribeIl2CppProblem(false, il2cpp, "X");
            StringAssert.StartsWith(BuildTools.Il2CppMissingMessage, noTarget);

            string noVariation = BuildTools.DescribeIl2CppProblem(true, mono, "C:/Unity/Variations");
            StringAssert.StartsWith(BuildTools.Il2CppMissingMessage, noVariation);
            StringAssert.Contains("C:/Unity/Variations", noVariation);
            StringAssert.Contains("Windows Build Support (IL2CPP)", noVariation);
        }

        // 이 에디터 설치의 실제 상태. 모듈이 없으면(현재 PC) 명확한 예외, 설치 뒤에는 통과한다.
        // 7D 리뷰 낮음 10: 설치 여부는 구현(Win64VariationsPath·HasWin64Il2CppVariation)과 다른 경로로 따로 구한다 —
        // 에디터 폴더 아래 고정 위치를 파일 시스템 패턴(win64*il2cpp)으로 직접 찾고, 빌드 대상 지원 여부도 같이 본다.
        [Test]
        public void EnsureIl2CppAvailable_MatchesThisInstall()
        {
            string variations = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "WindowsStandaloneSupport", "Variations");
            bool targetSupported = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            bool hasIl2CppFolder = Directory.Exists(variations) && Directory.GetDirectories(variations, "win64*il2cpp").Length > 0;
            bool installed = targetSupported && hasIl2CppFolder;
            if (Directory.Exists(variations))
                Assert.IsTrue(string.Equals(Path.GetFullPath(variations).TrimEnd('\\', '/'), BuildTools.Win64VariationsPath().TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase), $"변형 폴더 위치: 구현 {BuildTools.Win64VariationsPath()} / 고정 {variations}");
            ScriptingImplementation before = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            if (installed)
            {
                Assert.DoesNotThrow(BuildTools.EnsureIl2CppAvailable);
            }
            else
            {
                var e = Assert.Throws<InvalidOperationException>(BuildTools.EnsureIl2CppAvailable);
                StringAssert.Contains(BuildTools.Il2CppMissingMessage, e.Message);
                Debug.Log($"[BuildToolsTests] 이 PC: {e.Message}");
            }
            Assert.AreEqual(before, PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone), "확인만으로 백엔드가 바뀌면 안 된다");
        }

        [Test]
        public void ProjectBackend_IsIl2Cpp()
        {
            // 7D-IL2CPP에서 §13.1 "IL2CPP 빌드"대로 ProjectSettings 백엔드를 IL2CPP로 영구 전환했다(SetStandaloneBackendIl2Cpp).
            // 기본 빌드(BuildWindows64·Development)가 IL2CPP이고 Mono는 BuildWindows64Mono 대체 경로만 임시로 쓴다.
            // 스택 추적은 MethodOnly를 명시해 두어 IL2CPP 개발 빌드의 임시 변경·되돌림 뒤에도 ProjectSettings가 같다.
            Assert.AreEqual(ScriptingImplementation.IL2CPP, BuildTools.ProjectBackend);
            Assert.AreEqual(BuildTools.ProjectBackend, PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone));
            Assert.AreEqual(BuildTools.ProjectStacktrace, PlayerSettings.GetIl2CppStacktraceInformation(NamedBuildTarget.Standalone));
        }

        // 7D-IL2CPP: 이전 Mono 빌드가 있는 폴더에 IL2CPP로 빌드하면 유니티가 실패한다 → 다른 백엔드 출력만 지운다
        [Test]
        public void OtherBackendOutput_Detection()
        {
            Assert.IsTrue(BuildTools.IsOtherBackendOutput(ScriptingImplementation.IL2CPP, hasMonoRuntimeFolder: true, hasGameAssembly: false));
            Assert.IsFalse(BuildTools.IsOtherBackendOutput(ScriptingImplementation.IL2CPP, false, true), "같은 백엔드 = 증분 빌드");
            Assert.IsFalse(BuildTools.IsOtherBackendOutput(ScriptingImplementation.IL2CPP, false, false), "빈 폴더");
            Assert.IsTrue(BuildTools.IsOtherBackendOutput(ScriptingImplementation.Mono2x, false, true));
            Assert.IsFalse(BuildTools.IsOtherBackendOutput(ScriptingImplementation.Mono2x, true, false));
        }

        [Test]
        public void OutputPaths_Distinct()
        {
            string[] paths =
            {
                BuildTools.OutputPath, BuildTools.DevelopmentOutputPath, BuildTools.Il2CppOutputPath, BuildTools.Il2CppDevelopmentOutputPath,
                BuildTools.MonoOutputPath, BuildTools.MonoDevelopmentOutputPath,
            };
            Assert.AreEqual(paths.Length, paths.Select(Path.GetDirectoryName).Distinct().Count());
            Assert.AreEqual("Builds/Windows64Dev/Ashfall.exe", BuildTools.DevelopmentOutputPath);
            Assert.IsTrue(paths.All(p => p.StartsWith("Builds/", StringComparison.Ordinal)));
        }

        // link.xml이 모든 런타임 Ashfall asmdef를 preserve="all"로 보존 (Q53). 에디터 전용 어셈블리는 넣지 않는다.
        [Test]
        public void LinkXml_PreservesAllRuntimeAssemblies()
        {
            string linkPath = Path.Combine(Application.dataPath, "_Project", "link.xml");
            Assert.IsTrue(File.Exists(linkPath), linkPath);
            XDocument doc = XDocument.Load(linkPath);
            var preserved = doc.Root.Elements("assembly")
                .Where(a => (string)a.Attribute("preserve") == "all")
                .Select(a => (string)a.Attribute("fullname"))
                .ToHashSet();

            // 7D 리뷰 낮음 10: 에디터 전용 판정은 includePlatforms 배열만 본다("Editor" 문자열이 다른 곳에 있어도 영향 없음), 이름은 asmdef "name".
            string scripts = Path.Combine(Application.dataPath, "_Project", "Scripts");
            var asmdefs = Directory.GetFiles(scripts, "*.asmdef", SearchOption.AllDirectories)
                .Select(p => File.ReadAllText(p))
                .Select(text => (name: AsmdefName(text), editorOnly: IncludePlatforms(text).Contains("Editor")))
                .ToList();
            string[] runtime = asmdefs.Where(a => !a.editorOnly).Select(a => a.name).ToArray();
            string[] editorOnly = asmdefs.Where(a => a.editorOnly).Select(a => a.name).ToArray();
            Assert.Greater(runtime.Length, 5);
            CollectionAssert.Contains(editorOnly, "Ashfall.EditorTools", "EditorTools = 에디터 전용 (includePlatforms)");
            foreach (string asm in runtime)
                Assert.IsTrue(preserved.Contains(asm), $"link.xml에 {asm} 없음");
            // 역방향: link.xml의 모든 assembly가 지금 있는 런타임 asmdef다(낡은 이름·에디터 전용 없음)
            var all = doc.Root.Elements("assembly").Select(a => (string)a.Attribute("fullname")).ToList();
            foreach (string asm in all)
            {
                CollectionAssert.Contains(runtime, asm, $"link.xml의 {asm}이 런타임 asmdef가 아니다");
                Assert.IsFalse(editorOnly.Contains(asm), $"link.xml에 에디터 전용 {asm}");
            }
            Assert.AreEqual(all.Count, all.Distinct().Count(), "link.xml 중복 없음");
            Assert.AreEqual(all.Count, preserved.Count, "모든 항목이 preserve=\"all\"");
        }

        private static string AsmdefName(string json)
        {
            Match m = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
            Assert.IsTrue(m.Success, "asmdef name 없음");
            return m.Groups[1].Value;
        }

        private static string[] IncludePlatforms(string json)
        {
            Match m = Regex.Match(json, "\"includePlatforms\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!m.Success)
                return Array.Empty<string>();
            return Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"").Cast<Match>().Select(x => x.Groups[1].Value).ToArray();
        }
    }
}
