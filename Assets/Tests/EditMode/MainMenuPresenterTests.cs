using System;
using System.Text.RegularExpressions;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // M5 메인 메뉴 (M5 설계 §1.2, §3.4, §4.5, Q20·Q21): [은신처] → EnterHideout 한 번만, [종료]. 결과 패널 없음(은신처로 이동).
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

        [Test]
        public void Constructor_NullArgs_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new MainMenuPresenter(null, () => { }));
            Assert.Throws<ArgumentNullException>(() => new MainMenuPresenter(flow, null));
        }

        [Test]
        public void ShowsMenu_NotEntering()
        {
            var menu = Create();
            Assert.AreEqual(MainMenuPanel.Menu, menu.Panel);
            Assert.IsFalse(menu.IsEntering);
            CollectionAssert.IsEmpty(rec.Loads, "만들기만 해서는 아무 씬도 부르지 않는다");
        }

        // Q21: 결과가 남아 있어도 메인 메뉴는 결과 패널을 보이지 않고 확인하지도 않는다(은신처가 보인다)
        [Test]
        public void PendingResult_StaysMenu_AndIsNotAcknowledged()
        {
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            flow.CompleteRaid(M4TestData.Result(RaidOutcome.Killed));
            flow.OnSceneLoaded(SceneNames.MainMenu);
            rec.Loads.Clear();

            var menu = Create();
            Assert.AreEqual(MainMenuPanel.Menu, menu.Panel);
            Assert.IsTrue(menu.EnterHideout());
            Assert.IsNotNull(flow.PendingResult, "결과는 은신처 결과 패널 [확인]까지 남는다");
        }

        [Test]
        public void EnterHideout_LoadsHideout_Once()
        {
            var menu = Create();
            int changed = 0;
            menu.Changed += () => changed++;

            Assert.IsTrue(menu.EnterHideout());
            Assert.IsTrue(menu.IsEntering);
            Assert.IsFalse(menu.EnterHideout(), "연속 클릭 무시");
            Assert.IsFalse(menu.Quit(), "불러오는 중엔 종료 안 함");
            CollectionAssert.AreEqual(new[] { SceneNames.Hideout }, rec.Loads);
            Assert.AreEqual(1, changed);
            Assert.AreEqual(0, quits);
        }

        // M4 리뷰 수정 (낮음 8): 로드 요청 실패 → IsEntering이 켜지지 않아 버튼이 살아 있고 다시 누를 수 있다
        [Test]
        public void EnterHideout_LoadFails_NotEntering_CanRetry()
        {
            var menu = Create();
            int changed = 0;
            menu.Changed += () => changed++;
            rec.LoadSucceeds = false;

            LogAssert.Expect(LogType.Error, new Regex("로드 요청 실패"));
            Assert.IsFalse(menu.EnterHideout());
            Assert.IsFalse(menu.IsEntering);
            Assert.AreEqual(0, changed);
            Assert.IsTrue(menu.Quit(), "실패 뒤 종료 가능");

            rec.LoadSucceeds = true;
            Assert.IsTrue(menu.EnterHideout(), "다시 누르면 진입");
            Assert.IsTrue(menu.IsEntering);
        }

        // GameFlow가 거부(이미 다른 경로로 로드 요청)하면 메뉴도 진입 상태가 되지 않는다
        [Test]
        public void EnterHideout_FlowRejects_NotEntering()
        {
            Assert.IsTrue(flow.EnterHideout());
            var menu = Create();
            Assert.IsFalse(menu.EnterHideout());
            Assert.IsFalse(menu.IsEntering);
        }

        [Test]
        public void Quit_CallsDelegate()
        {
            var menu = Create();
            Assert.IsTrue(menu.Quit());
            Assert.AreEqual(1, quits);
            CollectionAssert.IsEmpty(rec.Loads);
        }

        // 은신처 → [메인 메뉴] → 다시 [은신처]: 새 MainMenu 씬의 새 프레젠터는 다시 진입할 수 있다
        [Test]
        public void BackFromHideout_NewPresenter_EntersAgain()
        {
            Create().EnterHideout();
            flow.OnSceneLoaded(SceneNames.Hideout);
            Assert.IsTrue(flow.LeaveHideout());
            flow.OnSceneLoaded(SceneNames.MainMenu);
            rec.Loads.Clear();

            var menu = Create();
            Assert.IsTrue(menu.EnterHideout());
            CollectionAssert.AreEqual(new[] { SceneNames.Hideout }, rec.Loads);
        }

        [Test]
        public void MenuText_IsHideoutAndQuit()
        {
            Assert.AreEqual("은신처", UiText.MenuHideout);
            Assert.AreEqual("종료", UiText.MenuQuit);
        }
    }
}
