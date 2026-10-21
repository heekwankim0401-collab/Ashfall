using System.IO;
using System.Linq;
using Ashfall.Core;
using NUnit.Framework;
using UnityEditor;

namespace Ashfall.Tests.EditMode
{
    // M0 AC: 빌드 시작 씬이 Boot이고 MainMenu가 빌드에 포함되는지 확인한다.
    public class BuildSettingsTests
    {
        private static string[] EnabledSceneNames() =>
            EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => Path.GetFileNameWithoutExtension(s.path))
                .ToArray();

        [Test]
        public void FirstEnabledScene_IsBoot()
        {
            var names = EnabledSceneNames();
            Assert.IsNotEmpty(names, "빌드 설정에 활성 씬이 없다. Ashfall/Setup 메뉴를 실행할 것.");
            Assert.AreEqual(SceneNames.Boot, names[0]);
        }

        [Test]
        public void MainMenu_IsInBuild()
        {
            CollectionAssert.Contains(EnabledSceneNames(), SceneNames.MainMenu);
        }
    }
}
