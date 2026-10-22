using System.Linq;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEditor;

namespace Ashfall.Tests.EditMode
{
    // M5 리뷰 4 (RULE-01): 은신처 장비(Hideout의 HideoutInventoryBehaviour)와 레이드 소지품(Raid_CrimsonBasin의 PlayerInventoryBehaviour)은
    // 같은 InventoryConfigSO여야 한다 — 다르면 반입 적용(TryApplyLoadout)이 거부한 아이템이 레이드에서 사라진다(broughtIn·결과에도 없음).
    // 씬 파일은 M4SceneSetup·M5SceneSetup(M2DataSetup.InventoryConfigPath)이 만든다. 씬이 직접 참조하는 에셋으로 확인한다(씬을 열지 않음).
    public class SceneInventoryConfigTests
    {
        // EditorTools 상수와 같은 값 (테스트 어셈블리는 EditorTools를 참조하지 않는다)
        private const string InventoryConfigPath = "Assets/_Project/Data/Inventory/InventoryConfig.asset";   // M2DataSetup.InventoryConfigPath
        private const string HideoutScenePath = "Assets/_Project/Scenes/Hideout.unity";                       // M5SceneSetup.HideoutScenePath
        private const string RaidScenePath = "Assets/_Project/Scenes/Raid_CrimsonBasin.unity";                // M4SceneSetup.RaidScenePath

        private static string[] InventoryConfigsOf(string scenePath)
        {
            Assert.IsNotNull(AssetDatabase.LoadMainAssetAtPath(scenePath), $"씬이 없다: {scenePath}");
            return AssetDatabase.GetDependencies(scenePath, false)
                .Where(path => AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(InventoryConfigSO))
                .OrderBy(path => path)
                .ToArray();
        }

        [Test]
        public void InventoryConfigAsset_Exists()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<InventoryConfigSO>(InventoryConfigPath));
        }

        [TestCase(HideoutScenePath)]
        [TestCase(RaidScenePath)]
        public void Scene_ReferencesOnlyTheSharedInventoryConfig(string scenePath)
        {
            CollectionAssert.AreEqual(new[] { InventoryConfigPath }, InventoryConfigsOf(scenePath),
                $"{scenePath}의 InventoryConfigSO가 공용 에셋 하나가 아니다");
        }

        [Test]
        public void HideoutAndRaid_SameInventoryConfig()
        {
            CollectionAssert.AreEqual(InventoryConfigsOf(RaidScenePath), InventoryConfigsOf(HideoutScenePath));
        }
    }
}
