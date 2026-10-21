using System;
using System.Text.RegularExpressions;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // M4 메인 메뉴 (M4 설계 §1.3, §3.4, Q15·Q18·Q22): 결과 패널 먼저, [확인] → 메뉴, [레이드 시작] 한 번만, [종료]
    public class MainMenuPresenterTests
    {
        private FlowRecorder rec;
        private GameFlow flow;
        private int quits;

        [SetUp]
        public void SetUp()
        {
            rec = new FlowRecorder();
            flow = rec.Create();
            flow.OnSceneLoaded(SceneNames.MainMenu);
            quits = 0;
        }

        private MainMenuPresenter Create() => new MainMenuPresenter(flow, () => quits++);

        // 레이드 한 판을 끝내고 MainMenu로 돌아온 상태
        private void FinishRaid(RaidResult result)
        {
            flow.StartRaid();
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            flow.CompleteRaid(result);
            flow.OnSceneLoaded(SceneNames.MainMenu);
            rec.Loads.Clear();
        }

        [Test]
        public void Constructor_NullArgs_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new MainMenuPresenter(null, () => { }));
            Assert.Throws<ArgumentNullException>(() => new MainMenuPresenter(flow, null));
        }

        [Test]
        public void NoPendingResult_ShowsMenu()
        {
            var menu = Create();
            Assert.AreEqual(MainMenuPanel.Menu, menu.Panel);
            Assert.IsNull(menu.Result);
            Assert.IsFalse(menu.IsStarting);
        }

        [Test]
        public void PendingResult_ShowsResultFirst()
        {
            FinishRaid(M4TestData.Result(RaidOutcome.Killed));
            var menu = Create();

            Assert.AreEqual(MainMenuPanel.Result, menu.Panel);
            Assert.IsNotNull(menu.Result);
            Assert.AreEqual("사망 — 머리", menu.Result.Title);
            Assert.IsTrue(menu.Result.ShowLost);
        }

        [Test]
        public void ResultPanel_BlocksStartAndQuit_UntilConfirmed()
        {
            FinishRaid(M4TestData.Result(RaidOutcome.MissingInAction));
            var menu = Create();

            Assert.IsFalse(menu.StartRaid());
            Assert.IsFalse(menu.Quit());
            CollectionAssert.IsEmpty(rec.Loads);
            Assert.AreEqual(0, quits);
        }

        [Test]
        public void Confirm_AcknowledgesAndShowsMenu()
        {
            FinishRaid(M4TestData.Result(RaidOutcome.Extracted, "EXT-01", "남쪽 도로"));
            var menu = Create();
            int changed = 0;
            menu.Changed += () => changed++;

            Assert.IsTrue(menu.ConfirmResult());
            Assert.IsNull(flow.PendingResult);
            Assert.AreEqual(MainMenuPanel.Menu, menu.Panel);
            Assert.IsNull(menu.Result);
            Assert.AreEqual(1, changed);
            Assert.IsFalse(menu.ConfirmResult(), "메뉴에서는 확인 없음");
        }

        [Test]
        public void StartRaid_LoadsRaidScene_Once()
        {
            var menu = Create();
            int changed = 0;
            menu.Changed += () => changed++;

            Assert.IsTrue(menu.StartRaid());
            Assert.IsTrue(menu.IsStarting);
            Assert.IsFalse(menu.StartRaid(), "연속 클릭 무시");
            Assert.IsFalse(menu.Quit(), "불러오는 중엔 종료 안 함");
            CollectionAssert.AreEqual(new[] { SceneNames.RaidCrimsonBasin }, rec.Loads);
            Assert.AreEqual(1, changed);
        }

        // M4 리뷰 수정 (낮음 8): 로드 요청 실패 → IsStarting이 켜지지 않아 버튼이 살아 있고 다시 누를 수 있다
        [Test]
        public void StartRaid_LoadFails_NotStarting_CanRetry()
        {
            var menu = Create();
            int changed = 0;
            menu.Changed += () => changed++;
            rec.LoadSucceeds = false;

            LogAssert.Expect(LogType.Error, new Regex("로드 요청 실패"));
            Assert.IsFalse(menu.StartRaid());
            Assert.IsFalse(menu.IsStarting);
            Assert.AreEqual(0, changed);

            rec.LoadSucceeds = true;
            Assert.IsTrue(menu.StartRaid(), "다시 누르면 시작");
            Assert.IsTrue(menu.IsStarting);
        }

        // GameFlow가 거부(이미 레이드 씬 로드 중)하면 메뉴도 시작 상태가 되지 않는다
        [Test]
        public void StartRaid_FlowRejects_NotStarting()
        {
            Assert.IsTrue(flow.StartRaid());   // 다른 경로로 이미 요청됨
            var menu = Create();
            Assert.IsFalse(menu.StartRaid());
            Assert.IsFalse(menu.IsStarting);
        }

        [Test]
        public void Quit_CallsDelegate()
        {
            var menu = Create();
            Assert.IsTrue(menu.Quit());
            Assert.AreEqual(1, quits);
            CollectionAssert.IsEmpty(rec.Loads);
        }

        // 메뉴 ↔ 레이드 두 번째 판: 결과 확인 후 다시 시작할 수 있다
        [Test]
        public void SecondRaid_AfterConfirm_StartsAgain()
        {
            FinishRaid(M4TestData.Result(RaidOutcome.Abandoned));
            var menu = Create();
            menu.ConfirmResult();
            Assert.IsTrue(menu.StartRaid());
            CollectionAssert.AreEqual(new[] { SceneNames.RaidCrimsonBasin }, rec.Loads);
        }

        [Test]
        public void Refresh_PicksUpLaterResult()
        {
            var menu = Create();
            Assert.AreEqual(MainMenuPanel.Menu, menu.Panel);
            FinishRaid(M4TestData.Result(RaidOutcome.Killed));
            menu.Refresh();
            Assert.AreEqual(MainMenuPanel.Result, menu.Panel);
        }
    }
}
