using System.IO;
using System.Linq;
using Ashfall.Core;
using NUnit.Framework;
using UnityEditor;

namespace Ashfall.Tests.EditMode
{
    // M0 AC: 빌드 시작 씬이 Boot이고 MainMenu가 빌드에 포함되는지 확인한다.
    // M4: 레이드 씬(Raid_CrimsonBasin)이 빌드에 포함된다 (M4 설계 §1.3).
    // M5: 은신처 씬(Hideout)이 추가되고 순서는 Boot → MainMenu → Hideout → Raid_CrimsonBasin (M5 설계 §4.9).
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

        [Test]
        public void Hideout_IsInBuild()
        {
            CollectionAssert.Contains(EnabledSceneNames(), SceneNames.Hideout,
                "은신처 씬이 빌드 설정에 없다. Ashfall/Setup/Create M5 Hideout Scene을 실행할 것.");
        }

        [Test]
        public void RaidCrimsonBasin_IsInBuild()
        {
            CollectionAssert.Contains(EnabledSceneNames(), SceneNames.RaidCrimsonBasin,
                "레이드 씬이 빌드 설정에 없다. Ashfall/Setup/Create M4 Raid Scenes를 실행할 것.");
        }

        [Test]
        public void EnabledScenes_AreBootMainMenuHideoutRaid_InOrder()
        {
            CollectionAssert.AreEqual(
                new[] { SceneNames.Boot, SceneNames.MainMenu, SceneNames.Hideout, SceneNames.RaidCrimsonBasin },
                EnabledSceneNames());
        }

        [Test]
        public void AllBuildScenes_AreEnabled()
        {
            Assert.AreEqual(4, EditorBuildSettings.scenes.Length, "빌드 설정 씬 수 (비활성 포함)");
            Assert.IsTrue(EditorBuildSettings.scenes.All(s => s.enabled), "비활성 빌드 씬이 있다");
        }

        [Test]
        public void EnabledScenePaths_Exist()
        {
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes.Where(s => s.enabled))
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path), $"빌드 씬 파일 없음: {s.path}");
        }
    }
}
