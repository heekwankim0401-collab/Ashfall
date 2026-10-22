using System.Collections;
using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Raid;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ashfall.Tests.PlayMode
{
    // M4 3단계 스모크 (M4 설계 §6 "3. 통합", §7 AC1·회귀).
    // 씬은 Ashfall/Setup/Create M4 Raid Scenes(M4SceneSetup)가 만든다. 둘 다 빌드 설정에 있으므로 이름으로 불러온다.
    // 예외·LogError는 테스트 프레임워크가 자동으로 실패 처리한다. 탈출·결과 흐름은 4단계 통합 테스트.
    public class RaidSceneSmokeTests
    {
        private const float RunSeconds = 3f;           // 오류 없이 도는지 보는 최소 시간
        private const float LoadTimeoutSec = 30f;
        private const int WarmupFrames = 5;
        private const int ExpectedEnemies = 20;        // TODO(GDD-?) Q12: SCV 12 + RDR 8
        private const int ExpectedSpawns = 3;          // Q2
        private const string AlwaysActiveExtraction = "EXT-01"; // Q3: EXT-01 항상 열림
        private const float SpawnToleranceM = 1f;      // 배치 후 수평 오차(착지·충돌 해소)

        [UnityTest]
        public IEnumerator RaidScene_Loads_PlayerAtSpawn_ServicesRegistered_EnemiesOnNavMesh()
        {
            yield return LoadScene(SceneNames.RaidCrimsonBasin);

            GameManager gm = GameManager.Instance;
            Assert.IsNotNull(gm, "GameManager 없음");
            Assert.AreEqual(GamePhase.Raid, gm.Flow.Phase, "레이드 씬 로드 후 단계");

            var controllers = Object.FindObjectsByType<RaidController>(FindObjectsSortMode.None);
            Assert.AreEqual(1, controllers.Length, "RaidController 수");
            RaidController controller = controllers[0];
            Assert.IsTrue(controller.enabled, "RaidController 꺼짐 (config·서비스 누락)");
            Assert.IsNotNull(controller.Session, "레이드 세션이 시작되지 않았다");

            // 서비스
            Assert.IsTrue(gm.Services.TryGet(out IRaidInfo raidInfo), "IRaidInfo 미등록");
            Assert.AreSame(controller, raidInfo);
            Assert.IsTrue(gm.Services.TryGet(out IPlayerTarget target), "IPlayerTarget 미등록");
            Assert.IsTrue(gm.Services.TryGet(out IPlayerPlacement _), "IPlayerPlacement 미등록");
            Assert.IsTrue(gm.Services.TryGet(out IProjectileService _), "IProjectileService 미등록");
            Assert.IsTrue(gm.Services.TryGet(out IInventoryService _), "IInventoryService 미등록");

            // 플레이어가 스폰 3곳 중 하나(RaidController가 고른 곳)에 있다
            var spawns = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
            Assert.AreEqual(ExpectedSpawns, spawns.Length, "PlayerSpawnPoint 수");
            PlayerSpawnPoint chosen = null;
            foreach (PlayerSpawnPoint s in spawns)
                if (s.Id == controller.SpawnId)
                    chosen = s;
            Assert.IsNotNull(chosen, $"선택된 스폰 {controller.SpawnId}가 씬에 없다");
            Assert.Less(HorizontalDistance(target.Position, chosen.FeetPosition), SpawnToleranceM,
                $"플레이어 {target.Position}가 스폰 {chosen.Id} {chosen.FeetPosition}에 없다");
            Assert.IsTrue(NavMesh.SamplePosition(target.Position, out NavMeshHit _, 2f, NavMesh.AllAreas),
                "플레이어 위치에 NavMesh 없음");

            // 활성 탈출구 2~3개, EXT-01 포함 (지도용 IRaidInfo와 같다)
            IReadOnlyList<string> active = controller.ActiveExtractionIds;
            Assert.That(active.Count, Is.InRange(2, 3), "활성 탈출구 수");
            CollectionAssert.Contains(active, AlwaysActiveExtraction);
            Assert.AreEqual(active.Count, raidInfo.ActiveExtractions.Count, "IRaidInfo 활성 탈출구 수");
            Assert.AreEqual(5, raidInfo.PointsOfInterest.Count, "POI 수");

            // 적 20명, 최상위, NavMesh 위, AiDirector 등록
            EnemyAgent[] enemies = Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.InstanceID);
            Assert.AreEqual(ExpectedEnemies, enemies.Length, "적 수");
            var directors = Object.FindObjectsByType<AiDirector>(FindObjectsSortMode.None);
            Assert.AreEqual(1, directors.Length, "AiDirector 수");
            Assert.IsTrue(directors[0].enabled, "AiDirector 꺼짐");
            Assert.AreEqual(ExpectedEnemies, directors[0].Agents.Count, "등록된 적 수");
            Assert.AreSame(target, directors[0].Target, "AiDirector가 IPlayerTarget을 잡지 못했다");
            foreach (EnemyAgent enemy in enemies)
            {
                Assert.IsNull(enemy.transform.parent, $"{enemy.name}: 적 루트는 최상위여야 한다");
                Assert.IsTrue(enemy.GetComponent<NavMeshAgent>().isOnNavMesh, $"{enemy.name}: NavMesh 위에 없다");
            }

            // 몇 초 돌린다 — 예외·LogError 없음(프레임워크), 레이드가 끝나지 않고 세계가 멈추지 않는다
            yield return WaitSeconds(RunSeconds);
            Assert.IsFalse(controller.Session.IsOver, "레이드가 시작 직후 끝났다");
            Assert.IsFalse(target.IsDead, "플레이어 사망");
            Assert.AreEqual(1f, Time.timeScale, "세계 정지 상태");
            Assert.Greater(controller.Session.ElapsedSec, 0f, "레이드 시간이 흐르지 않는다");
            foreach (EnemyAgent enemy in enemies)
                Assert.IsTrue(enemy.GetComponent<NavMeshAgent>().isOnNavMesh, $"{enemy.name}: 실행 후 NavMesh 밖");
        }

        [UnityTest]
        public IEnumerator MainMenuScene_Loads_UiRootAndMenuPanel()
        {
            yield return LoadScene(SceneNames.MainMenu);

            GameManager gm = GameManager.Instance;
            Assert.IsNotNull(gm, "GameManager 없음");
            Assert.AreEqual(GamePhase.MainMenu, gm.Flow.Phase);

            var roots = Object.FindObjectsByType<MainMenuRoot>(FindObjectsSortMode.None);
            Assert.AreEqual(1, roots.Length, "MainMenuRoot 수");
            MainMenuRoot root = roots[0];
            Assert.IsTrue(root.enabled, "MainMenuRoot 꺼짐");
            Assert.IsNotNull(root.Presenter, "MainMenuPresenter 없음");

            var document = root.GetComponent<UIDocument>();
            Assert.IsNotNull(document.panelSettings, "PanelSettings 없음");
            Assert.IsNotNull(document.visualTreeAsset, "MainMenu.uxml 없음");
            VisualElement ui = document.rootVisualElement;
            Assert.IsNotNull(ui.Q<VisualElement>(MainMenuRoot.MenuPanelName), "메뉴 패널 없음");
            Button start = ui.Q<Button>(MainMenuRoot.StartName);
            Assert.IsNotNull(start, "[은신처] 버튼 없음");
            // M5 Q20: 메인 메뉴 [은신처]/[종료]. 버튼 이름 menu-start는 그대로, 문구만 "은신처"
            yield return null;   // 뷰가 첫 프레임에 문구를 덮어쓴다
            Assert.AreEqual(UiText.MenuHideout, start.text, "메뉴 버튼 문구");
            Assert.AreEqual("은신처", start.text);
            Assert.IsNull(ui.Q<VisualElement>(RaidResultView.PanelName), "MainMenu에 결과 패널이 남아 있다 (M5 Q21: 은신처로 이동)");
            Assert.IsNotNull(ui.Q<Button>(MainMenuRoot.QuitName), "[종료] 버튼 없음");
            Assert.AreEqual(1, Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length, "카메라 수");
            Assert.AreEqual(CursorLockMode.None, UnityEngine.Cursor.lockState, "메뉴에서 커서가 잠겨 있다");
        }

        private static IEnumerator LoadScene(string sceneName)
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            Assert.IsNotNull(op, $"{sceneName} 로드 실패 (빌드 설정 확인)");
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSec;
            while (!op.isDone)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"{sceneName} 로드 시간 초과");
                yield return null;
            }
            for (int i = 0; i < WarmupFrames; i++)
                yield return null;
            Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name);
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
                yield return null;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b) =>
            Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
