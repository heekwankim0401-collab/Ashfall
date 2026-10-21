#if UNITY_EDITOR
using System.Collections;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ashfall.Tests.PlayMode
{
    // M2 확인 씬(Ashfall/Setup/Create M2 Test Scene이 생성)이 오류 없이 시작되는지 확인하는 스모크 테스트.
    // 예외·LogError는 테스트 프레임워크가 자동으로 실패 처리한다. 상세 흐름 테스트는 4단계(InventoryFlowTests 등).
    public class M2SceneSmokeTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Test/M2_HealthInventory.unity";
        private const int FramesToRun = 30;

        [UnityTest]
        public IEnumerator M2Scene_RunsWithoutErrors_ServicesAndUiReady()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < FramesToRun; i++)
                yield return null;

            GameManager gm = GameManager.Instance;
            Assert.IsNotNull(gm, "GameManager 없음");
            Assert.IsTrue(gm.Services.TryGet(out IInventoryService inventory), "IInventoryService 미등록");
            Assert.IsTrue(gm.Services.TryGet(out IMagazineSource _), "IMagazineSource 미등록");
            Assert.IsTrue(gm.Services.TryGet(out IBodyStatusView body), "IBodyStatusView 미등록");
            Assert.IsFalse(body.IsDead);

            // 시작 장비가 들어갔다 (설계 §5.6)
            Assert.IsNotNull(inventory.GetEquipped(EquipSlot.Primary1), "Primary1 무기 없음");
            Assert.IsNotNull(inventory.GetEquipped(EquipSlot.TacticalVest), "조끼 없음");
            Assert.IsNotNull(inventory.GetEquipped(EquipSlot.Backpack), "배낭 없음");
            for (int i = 0; i < 3; i++)
                Assert.IsNotNull(inventory.GetQuickSlot(i), $"퀵슬롯 {i} 비어 있음");

            // 고fps + 느린 이동에서 멈추지 않게 (리뷰 수정, M2SceneSetup)
            var controllers = Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
            Assert.IsNotEmpty(controllers, "플레이어 CharacterController 없음");
            foreach (CharacterController controller in controllers)
                Assert.AreEqual(0f, controller.minMoveDistance, $"{controller.name} minMoveDistance");

            var document = Object.FindFirstObjectByType<UIDocument>();
            Assert.IsNotNull(document, "UIDocument 없음");
            Assert.IsNotNull(document.panelSettings, "PanelSettings 미연결");
            Assert.IsNotNull(document.visualTreeAsset, "GameUI.uxml 미연결");
            Assert.IsNotNull(document.rootVisualElement, "rootVisualElement 없음");
            var uiRoot = document.GetComponent<GameUIRoot>();
            Assert.IsNotNull(uiRoot, "GameUIRoot 없음");
            Assert.IsNotNull(uiRoot.Presenter, "GameUIRoot가 활성화되지 않았다 (config 누락 등)");
            Assert.AreEqual(ScreenKind.None, uiRoot.Presenter.Screens.Current);
        }
    }
}
#endif
