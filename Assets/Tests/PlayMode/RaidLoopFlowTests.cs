#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ashfall.Tests.PlayMode
{
    // M4 AC3 — 사망/MIA/탈출/포기 결과 화면, RULE-01 (§3, §12, M4 설계 §1.3·§1.5·§3.4·§3.5, §7 RaidLoopFlowTests).
    // 실제 흐름: Boot → MainMenu → [레이드 시작](UI Toolkit 버튼) → Raid_CrimsonBasin → 종료 → 종료 오버레이(세계 정지)
    //           → endDelaySec 뒤 MainMenu 결과 패널 → [확인] → 메뉴. 버튼은 UiPointer(실제 입력 경로 우선)로 누른다.
    // 레이드 안에서는 적을 지운다(결과·흐름만 본다). 사망은 IDamageable 머리 피격, MIA는 AdvanceTime, 탈출은 EXT-01.
    public class RaidLoopFlowTests
    {
        private const float StartTimeoutSec = 60f;
        // 결과 패널 아이템 줄: "이름" 또는 "이름 ×n" (탄 수·횟수·HP 없음)
        private static readonly Regex ItemLine = new Regex(@"^(?<name>.+?)( ×(?<n>[0-9]+))?$");

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.Flow != null)
            {
                gm.Flow.AcknowledgeResult();
                gm.Flow.SetWorldPaused(false);
            }
            Time.timeScale = 1f;
            yield return null;
        }

        // 사망 → 결과(전부 소실) → [확인] → 두 번째 레이드 → 획득 후 탈출 → 결과(반출 = 소지품) → [확인]. 구독 누수 없음.
        [UnityTest]
        [Timeout(240000)]
        public IEnumerator BootToMenu_DeathThenExtraction_TwoLoops_ResultPanelsApplyRule01()
        {
            var menu = new M4Menu();
            SceneManager.LoadScene(SceneNames.Boot);
            yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu, 15f, () => "Boot → MainMenu");
            yield return M4Menu.Attach(menu);
            GameManager gm = GameManager.Instance;
            Assert.AreEqual(GamePhase.MainMenu, gm.Flow.Phase);
            Assert.IsNull(gm.Flow.PendingResult);
            Assert.IsTrue(UiQuery.IsShown(menu.MenuPanel), "메뉴 패널");
            Assert.IsFalse(UiQuery.IsShown(menu.ResultPanel), "결과 없으면 결과 패널 숨김");
            Dictionary<string, int> baseline = SubscriberCounts(gm.Events);

            using (var pointer = new UiPointer())
            {
                // ── 1회차: 사망 ──
                var raid = new M4Raid();
                yield return StartRaidFromMenu(menu, pointer, raid);
                List<ItemDefinitionSO> broughtIn = raid.Controller.BroughtIn.ToList();
                Assert.Greater(broughtIn.Count, 0, "반입 기록");
                RaidResult death;
                using (var ended = new EventLog<RaidEnded>(raid.Events))
                {
                    raid.ShootHead();
                    yield return M3Scene.WaitUntil(() => ended.Count > 0, 5f, () => "사망 → RaidEnded");
                    Assert.AreEqual(RaidOutcome.Killed, ended.Last.Outcome);
                    yield return AssertEndOverlay(raid, "사망");
                    death = raid.Controller.LastResult;
                }
                Assert.AreEqual(RaidOutcome.Killed, death.Outcome);
                Assert.IsTrue(death.HasDeathCause);
                Assert.AreEqual(BodyPart.Head, death.DeathCause, "머리 피격 사망");
                Assert.AreEqual(0, death.CarriedOut.Count, "RULE-01: 사망 반출 0");
                CollectionAssert.AreEquivalent(raid.Carried().Select(i => i.Definition), death.Lost, "RULE-01: 소지품 전부 소실");
                CollectionAssert.IsSubsetOf(broughtIn, death.Lost, "반입 장비 전부 소실");

                yield return M4Raid.WaitForMainMenu(menu);
                Assert.AreSame(death, gm.Flow.PendingResult);
                AssertResultPanel(menu, death, expectCarriedOut: false);
                Assert.That(menu.Text(RaidResultView.TitleName), Does.StartWith("사망").And.Contain("머리"));
                yield return pointer.Click(menu.Confirm);
                Assert.IsNull(gm.Flow.PendingResult, "[확인] → 결과 확인");
                Assert.IsTrue(UiQuery.IsShown(menu.MenuPanel));
                Assert.IsFalse(UiQuery.IsShown(menu.ResultPanel));
                AssertNoLeak(gm.Events, baseline, "1회차 후");

                // ── 2회차: 레이드 중 획득 + EXT-01 탈출 ──
                raid = new M4Raid();
                yield return StartRaidFromMenu(menu, pointer, raid);
                Assert.IsFalse(raid.Health.IsDead, "2회차는 새 몸 (고정 키트, Q13·Q14)");
                CollectionAssert.AreEquivalent(broughtIn, raid.Controller.BroughtIn, "2회차 반입 = 같은 고정 키트");
                ItemDefinitionSO loot = FindCrateLoot(raid);
                ItemInstance picked = ItemFactory.Create(loot);
                Assert.IsTrue(raid.Inventory.TryAutoStore(picked), $"획득 {loot.id}");
                RaidResult extracted;
                using (var ended = new EventLog<RaidEnded>(raid.Events))
                {
                    yield return raid.EnterZone("EXT-01");
                    yield return M3Scene.WaitUntil(() => ended.Count > 0, 20f, () => "EXT-01 탈출");
                    yield return AssertEndOverlay(raid, "탈출 성공");
                    extracted = raid.Controller.LastResult;
                }
                Assert.AreEqual(RaidOutcome.Extracted, extracted.Outcome);
                CollectionAssert.Contains(extracted.CarriedOut, picked, "획득한 아이템 반출");
                CollectionAssert.AreEquivalent(raid.Carried(), extracted.CarriedOut, "RULE-01: 탈출 시 소지품 = 반출");
                Assert.AreEqual(0, extracted.Lost.Count);

                yield return M4Raid.WaitForMainMenu(menu);
                AssertResultPanel(menu, extracted, expectCarriedOut: true);
                Assert.AreEqual("탈출 성공 — 남쪽 도로", menu.Text(RaidResultView.TitleName));
                CollectionAssert.Contains(menu.Lines(RaidResultView.CarriedOutName).Select(StripCount).ToList(),
                    RaidResultViewModel.ItemName(loot), "반출 목록에 획득 아이템");
                yield return pointer.Click(menu.Confirm);
                Assert.IsNull(gm.Flow.PendingResult);
                AssertNoLeak(gm.Events, baseline, "2회차 후");
                Debug.Log("[RaidLoopFlowTests] 클릭 경로: " + string.Join(" | ", UiPointer.Attempts));
            }
        }

        // MIA → 결과 "작전 중 실종", 반출 0
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator MenuToRaid_MissingInAction_ResultPanelAllLost()
        {
            var menu = new M4Menu();
            yield return M4Raid.LoadScene(SceneNames.MainMenu);
            yield return M4Menu.Attach(menu);
            using (var pointer = new UiPointer())
            {
                var raid = new M4Raid();
                yield return StartRaidFromMenu(menu, pointer, raid);
                raid.Controller.AdvanceTime(raid.Session.RemainingSec + 1f);
                yield return M3Scene.WaitUntil(() => raid.Session.IsOver, 5f, () => "MIA");
                yield return AssertEndOverlay(raid, "작전 중 실종");
                RaidResult result = raid.Controller.LastResult;
                Assert.AreEqual(RaidOutcome.MissingInAction, result.Outcome);
                Assert.AreEqual(0, result.CarriedOut.Count, "RULE-01/02: MIA 반출 0");
                CollectionAssert.AreEquivalent(raid.Carried().Select(i => i.Definition), result.Lost);

                yield return M4Raid.WaitForMainMenu(menu);
                AssertResultPanel(menu, result, expectCarriedOut: false);
                Assert.AreEqual("작전 중 실종 (MIA)", menu.Text(RaidResultView.TitleName));
                Assert.AreEqual("레이드 시간 25:00", menu.Text(RaidResultView.DurationName));
                yield return pointer.Click(menu.Confirm);
            }
        }

        // ESC 일시정지(세계·타이머·적 정지) → [계속](timeScale 0에서 버튼) → 재개 → ESC → [레이드 포기] → [포기] → 결과 "레이드 포기"
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator MenuToRaid_EscPauseFreezesWorld_ResumeButton_ThenAbandonViaButtons_ResultPanelAllLost()
        {
            var menu = new M4Menu();
            yield return M4Raid.LoadScene(SceneNames.MainMenu);
            yield return M4Menu.Attach(menu);
            using (var keyboard = new SimKeyboard())
            using (var pointer = new UiPointer())
            {
                var raid = new M4Raid();
                yield return StartRaidFromMenu(menu, pointer, raid, keepEnemies: true);
                GameFlow flow = raid.Flow;
                // 적이 움직이고 있음을 먼저 확인 (순찰)
                yield return M2Scene.WaitSeconds(1f);

                // Esc → Pause, 세계 정지
                yield return keyboard.Tap(Key.Escape);
                yield return null;
                Assert.AreEqual(ScreenKind.Pause, raid.Presenter.Screens.Current, "Esc → 일시정지");
                Assert.AreEqual(0f, Time.timeScale, "일시정지 = timeScale 0");
                Assert.IsTrue(flow.IsWorldPaused);
                Assert.IsTrue(UiQuery.IsShown(raid.UiRoot.Q(PauseScreenView.ScreenName)), "일시정지 화면 표시");
                Assert.AreNotEqual(CursorLockMode.Locked, UnityEngine.Cursor.lockState, "일시정지 중 커서 해제");
                float elapsedAtPause = raid.Session.ElapsedSec;
                Vector3[] enemyPositions = raid.Enemies.Select(e => e.transform.position).ToArray();
                EnemyState[] enemyStates = raid.Enemies.Select(e => e.State).ToArray();
                int aiSamples = raid.Director.Stats.Samples;
                float realStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - realStart < 1.5f)
                    yield return null;
                Assert.AreEqual(elapsedAtPause, raid.Session.ElapsedSec, "일시정지 중 레이드 타이머 정지 (RULE-02)");
                for (int i = 0; i < raid.Enemies.Count; i++)
                {
                    Assert.AreEqual(0f, Vector3.Distance(enemyPositions[i], raid.Enemies[i].transform.position), 1e-4f,
                        $"{raid.Enemies[i].name}: 일시정지 중 이동");
                    Assert.AreEqual(enemyStates[i], raid.Enemies[i].State, $"{raid.Enemies[i].name}: 일시정지 중 상태 변화");
                }
                Debug.Log($"[RaidLoopFlowTests] 일시정지 1.5초 동안 AI 틱 샘플 {raid.Director.Stats.Samples - aiSamples} (정지 전 누적 {aiSamples})");

                // Pause 중 Tab·H·M 무시
                yield return keyboard.Tap(Key.Tab);
                yield return keyboard.Tap(Key.M);
                Assert.AreEqual(ScreenKind.Pause, raid.Presenter.Screens.Current, "Pause 중 Tab·M 무시");

                // [계속] — timeScale 0에서 UI Toolkit 버튼
                yield return pointer.Click(raid.UiRoot.Q<Button>(PauseScreenView.ResumeName));
                UiClickPath resumePath = pointer.LastPath;
                Assert.AreNotEqual(UiClickPath.None, resumePath, "[계속] 버튼이 눌리지 않았다");
                Assert.AreEqual(ScreenKind.None, raid.Presenter.Screens.Current, "[계속] → 닫힘");
                Assert.AreEqual(1f, Time.timeScale, "재개");
                Assert.IsFalse(flow.IsWorldPaused);
                yield return M2Scene.WaitSeconds(0.5f);
                Assert.Greater(raid.Session.ElapsedSec, elapsedAtPause, "재개 후 타이머 진행");

                // Esc → Pause → Esc → 닫힘 (Esc로도 재개)
                yield return keyboard.Tap(Key.Escape);
                Assert.AreEqual(ScreenKind.Pause, raid.Presenter.Screens.Current);
                yield return keyboard.Tap(Key.Escape);
                Assert.AreEqual(ScreenKind.None, raid.Presenter.Screens.Current, "Esc 다시 → 닫기");
                Assert.AreEqual(1f, Time.timeScale);

                // 포기: Esc → [레이드 포기] → 확인 [포기]
                yield return keyboard.Tap(Key.Escape);
                Assert.AreEqual(ScreenKind.Pause, raid.Presenter.Screens.Current);
                Assert.IsTrue(raid.Presenter.CanAbandon, "레이드 중 [레이드 포기] 가능");
                yield return null;
                using (var ended = new EventLog<RaidEnded>(raid.Events))
                {
                    yield return pointer.Click(raid.UiRoot.Q<Button>(PauseScreenView.AbandonName));
                    Assert.IsTrue(raid.Presenter.IsConfirmingAbandon, "[레이드 포기] → 확인 단계");
                    Assert.AreEqual(0, ended.Count, "확인 전에는 끝나지 않는다");
                    yield return null;
                    yield return pointer.Click(raid.UiRoot.Q<Button>(PauseScreenView.ConfirmYesName));
                    yield return M3Scene.WaitUntil(() => ended.Count > 0, 5f, () => "포기 → RaidEnded");
                    Assert.AreEqual(RaidOutcome.Abandoned, ended.Last.Outcome);
                }
                yield return AssertEndOverlay(raid, "레이드 포기");
                RaidResult result = raid.Controller.LastResult;
                Assert.AreEqual(0, result.CarriedOut.Count, "RULE-01(포기 = 사망과 같게): 반출 0");
                CollectionAssert.AreEquivalent(raid.Carried().Select(i => i.Definition), result.Lost);

                yield return M4Raid.WaitForMainMenu(menu);
                AssertResultPanel(menu, result, expectCarriedOut: false);
                Assert.AreEqual("레이드 포기", menu.Text(RaidResultView.TitleName));
                yield return pointer.Click(menu.Confirm);
                Assert.IsNull(GameManager.Instance.Flow.PendingResult);
                Debug.Log($"[RaidLoopFlowTests] 일시정지 [계속] 클릭 경로: {resumePath}; 전체: " + string.Join(" | ", UiPointer.Attempts));
            }
        }

        // ── 도우미 ──

        private static IEnumerator StartRaidFromMenu(M4Menu menu, UiPointer pointer, M4Raid raid, bool keepEnemies = false)
        {
            Assert.AreEqual(MainMenuPanel.Menu, menu.Presenter.Panel, "메뉴 패널에서 시작");
            yield return pointer.Click(menu.Start);
            Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[레이드 시작] 버튼이 눌리지 않았다");
            yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.RaidCrimsonBasin,
                StartTimeoutSec, () => "레이드 씬 로드");
            for (int i = 0; i < 3; i++)
                yield return null;
            yield return M4Raid.Attach(raid);
            Assert.AreEqual(GamePhase.Raid, raid.Flow.Phase);
            Assert.AreEqual(1f, Time.timeScale);
            // 커서 잠금은 배치모드에서 적용되지 않는다(창 없음, lockState 항상 None) — 에디터 사람 확인 항목
            if (!Application.isBatchMode)
                Assert.AreEqual(CursorLockMode.Locked, UnityEngine.Cursor.lockState, "레이드 중 커서 잠금");
            if (!keepEnemies)
                yield return raid.RemoveEnemies();
        }

        // 종료 직후: RaidEnd 오버레이(문구), 세계 정지, Esc로 안 닫힘
        private static IEnumerator AssertEndOverlay(M4Raid raid, string expectedText)
        {
            yield return null;
            Assert.AreEqual(ScreenKind.RaidEnd, raid.Presenter.Screens.Current, "종료 오버레이");
            Assert.AreEqual(0f, Time.timeScale, "종료 연출 중 세계 정지");
            Label text = raid.UiRoot.Q<Label>(RaidEndOverlayView.TextName);
            Assert.IsTrue(UiQuery.IsShown(text), "오버레이 표시");
            Assert.AreEqual(expectedText, text.text);
            raid.Presenter.Cancel();   // Esc와 같은 경로
            Assert.AreEqual(ScreenKind.RaidEnd, raid.Presenter.Screens.Current, "종료 오버레이는 Esc로 닫히지 않는다");
            Assert.AreEqual(0f, Time.timeScale);
        }

        // 결과 패널 내용: 제목·시간·처치·반입/반출/소실, 탄 수·HP 없음
        private static void AssertResultPanel(M4Menu menu, RaidResult result, bool expectCarriedOut)
        {
            Assert.AreEqual(MainMenuPanel.Result, menu.Presenter.Panel);
            Assert.IsTrue(UiQuery.IsShown(menu.ResultPanel), "결과 패널 표시");
            Assert.IsFalse(UiQuery.IsShown(menu.MenuPanel), "결과 먼저 (메뉴 숨김)");
            Assert.AreEqual(RaidResultViewModel.TitleFor(result), menu.Text(RaidResultView.TitleName));
            Assert.That(menu.Text(RaidResultView.DurationName), Does.Match(@"^레이드 시간 [0-9]+:[0-5][0-9]$"));
            Assert.AreEqual("처치 " + result.EnemiesKilled, menu.Text(RaidResultView.KillsName));

            VisualElement carriedSection = menu.Ui.Q(RaidResultView.CarriedOutSectionName);
            VisualElement lostSection = menu.Ui.Q(RaidResultView.LostSectionName);
            Assert.AreEqual(expectCarriedOut, UiQuery.IsShown(carriedSection), "반출 섹션은 탈출만");
            Assert.AreEqual(!expectCarriedOut, UiQuery.IsShown(lostSection), "소실 섹션은 탈출 외");

            AssertItemLines(menu.Lines(RaidResultView.BroughtInName), result.BroughtIn);
            if (expectCarriedOut)
                AssertItemLines(menu.Lines(RaidResultView.CarriedOutName), result.CarriedOut.Select(i => i.Definition).ToList());
            else
            {
                AssertItemLines(menu.Lines(RaidResultView.LostName), result.Lost);
                Assert.Greater(result.Lost.Count, 0, "소실 목록이 비어 있다");
            }

            // 탄 수·HP 수치 없음: 아이템 줄은 위에서 '정의 표시 이름 (×n)'과 정확히 같음을 확인했다(현재 탄 수·남은 횟수가 끼어들 수 없다).
            // 이름 자체의 고정 규격(예: '5.56 탄창 (30발)' = 용량)은 아이템 데이터라 허용한다. 그 밖의 줄에 'n/m'·%·HP 표기가 없는지 본다.
            foreach (string line in menu.Ui.Q(RaidResultView.PanelName).Query<Label>().ToList().Select(l => l.text ?? ""))
            {
                Assert.That(line, Does.Not.Match(@"[0-9]+\s*/\s*[0-9]+").And.Not.Contain("HP").And.Not.Contain("%"),
                    $"결과 패널에 탄 수·HP 표기: '{line}'");
            }
        }

        // 줄 = 정의별 묶음 "이름 ×n"(첫 등장 순), 수량 합 = 정의 수
        private static void AssertItemLines(List<string> lines, IReadOnlyList<ItemDefinitionSO> definitions)
        {
            if (definitions.Count == 0)
            {
                CollectionAssert.AreEqual(new[] { UiText.ResultNone }, lines);
                return;
            }
            var expected = new List<string>();
            var counts = new Dictionary<string, int>();
            foreach (ItemDefinitionSO d in definitions)
            {
                string name = RaidResultViewModel.ItemName(d);
                if (!counts.ContainsKey(name))
                {
                    counts[name] = 0;
                    expected.Add(name);
                }
                counts[name]++;
            }
            Assert.AreEqual(expected.Count, lines.Count, "줄 수 = 정의 종류 수: " + string.Join(", ", lines));
            for (int i = 0; i < lines.Count; i++)
            {
                Match m = ItemLine.Match(lines[i]);
                Assert.IsTrue(m.Success, lines[i]);
                Assert.AreEqual(expected[i], m.Groups["name"].Value, "줄 순서·이름");
                int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
                Assert.AreEqual(counts[expected[i]], n, $"{expected[i]} 수량");
            }
        }

        private static string StripCount(string line) => ItemLine.Match(line).Groups["name"].Value;

        // 맵 상자 루트 테이블(LT-CRATE)의 첫 아이템 정의 (레이드 중 획득 대상)
        private static ItemDefinitionSO FindCrateLoot(M4Raid raid)
        {
            foreach (LootContainer crate in Object.FindObjectsByType<LootContainer>(FindObjectsSortMode.InstanceID))
            {
                if (crate.LootTable == null || crate.LootTable.entries == null)
                    continue;
                foreach (var entry in crate.LootTable.entries)
                    if (entry.item != null && raid.CountCarried(entry.item) == 0)
                        return entry.item;
            }
            Assert.Fail("상자 루트 테이블에서 아이템을 찾지 못했다");
            return null;
        }

        // 레이드 관련 이벤트 구독 수 (메뉴 기준과 같아야 한다 — 레이드 씬 객체가 내려가며 모두 해지)
        private static Dictionary<string, int> SubscriberCounts(EventBus bus) => new Dictionary<string, int>
        {
            { nameof(PlayerDied), bus.SubscriberCount<PlayerDied>() },
            { nameof(EnemyKilled), bus.SubscriberCount<EnemyKilled>() },
            { nameof(RaidAbandonRequested), bus.SubscriberCount<RaidAbandonRequested>() },
            { nameof(RaidEnded), bus.SubscriberCount<RaidEnded>() },
            { nameof(RaidStarted), bus.SubscriberCount<RaidStarted>() },
            { nameof(ExtractionStatusChanged), bus.SubscriberCount<ExtractionStatusChanged>() },
            { nameof(NoiseEmitted), bus.SubscriberCount<NoiseEmitted>() },
            { nameof(InventoryChanged), bus.SubscriberCount<InventoryChanged>() },
            { nameof(ScreenStateChanged), bus.SubscriberCount<ScreenStateChanged>() },
        };

        private static void AssertNoLeak(EventBus bus, Dictionary<string, int> baseline, string when)
        {
            Dictionary<string, int> now = SubscriberCounts(bus);
            foreach (KeyValuePair<string, int> kv in baseline)
                Assert.AreEqual(kv.Value, now[kv.Key], $"{when}: {kv.Key} 구독 수 (누수)");
        }
    }
}
#endif
