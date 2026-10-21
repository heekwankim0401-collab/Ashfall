using System;
using System.Text.RegularExpressions;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // §13.3 씬 흐름·GameManager (M4 설계 §1.3·§1.4): 씬 전환, 결과 보관, 세계 정지의 유일한 주체
    public class GameFlowTests
    {
        private FlowRecorder rec;
        private GameFlow flow;

        [SetUp]
        public void SetUp()
        {
            rec = new FlowRecorder();
            flow = rec.Create();
        }

        private void EnterRaid()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            flow.StartRaid();
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            rec.Loads.Clear();
            rec.TimeScales.Clear();
        }

        [Test]
        public void Constructor_NullDelegates_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new GameFlow(null, _ => { }));
            Assert.Throws<ArgumentNullException>(() => new GameFlow(_ => true, null));
        }

        [Test]
        public void Initial_NoPhase_NotPaused_NoResult()
        {
            Assert.AreEqual(GamePhase.None, flow.Phase);
            Assert.IsFalse(flow.IsWorldPaused);
            Assert.IsNull(flow.PendingResult);
            CollectionAssert.IsEmpty(rec.Loads);
            CollectionAssert.IsEmpty(rec.TimeScales);
        }

        [Test]
        public void SceneName_RaidCrimsonBasin()
        {
            Assert.AreEqual("Raid_CrimsonBasin", SceneNames.RaidCrimsonBasin);
        }

        // ── StartRaid ─────────────────────────────────────────

        [Test]
        public void StartRaid_LoadsRaidScene()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            flow.StartRaid();
            CollectionAssert.AreEqual(new[] { SceneNames.RaidCrimsonBasin }, rec.Loads);
        }

        [Test]
        public void StartRaid_TwiceBeforeLoad_LoadsOnce()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            flow.StartRaid();
            flow.StartRaid();
            Assert.AreEqual(1, rec.Loads.Count);
        }

        [Test]
        public void StartRaid_DuringRaid_Ignored()
        {
            EnterRaid();
            flow.StartRaid();
            CollectionAssert.IsEmpty(rec.Loads);
        }

        [Test]
        public void StartRaid_WhilePaused_Unpauses()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            flow.SetWorldPaused(true);
            flow.StartRaid();
            Assert.IsFalse(flow.IsWorldPaused);
            CollectionAssert.AreEqual(new[] { 0f, 1f }, rec.TimeScales);
        }

        // ── CompleteRaid ──────────────────────────────────────

        // M4 리뷰 수정: 정지는 CompleteRaid가 아니라 MainMenu 로드 완료가 푼다 (스냅샷 뒤 레이드 세계가 다시 돌지 않게)
        [Test]
        public void CompleteRaid_StoresResult_LoadsMainMenu_StaysPausedUntilSceneLoaded()
        {
            EnterRaid();
            flow.SetWorldPaused(true);   // 종료 연출 중 세계 정지
            RaidResult result = M4TestData.Result(RaidOutcome.Killed);

            Assert.IsTrue(flow.CompleteRaid(result));

            Assert.AreSame(result, flow.PendingResult);
            CollectionAssert.AreEqual(new[] { SceneNames.MainMenu }, rec.Loads);
            Assert.IsTrue(flow.IsWorldPaused, "로드 중에도 레이드 세계는 정지");
            CollectionAssert.AreEqual(new[] { 0f }, rec.TimeScales);

            flow.OnSceneLoaded(SceneNames.MainMenu);
            Assert.IsFalse(flow.IsWorldPaused, "MainMenu 로드 완료에서 해제");
            CollectionAssert.AreEqual(new[] { 0f, 1f }, rec.TimeScales);
        }

        // M4 리뷰 수정: MainMenu 로드 요청 실패 → 결과는 남기고 정지만 푼다 (영구 정지 방지)
        [Test]
        public void CompleteRaid_LoadRequestFails_KeepsResult_Unpauses()
        {
            EnterRaid();
            flow.SetWorldPaused(true);
            rec.LoadSucceeds = false;
            RaidResult result = M4TestData.Result(RaidOutcome.Extracted, M4TestData.Ext01, "남쪽 도로");

            LogAssert.Expect(LogType.Error, new Regex("로드 요청 실패"));
            Assert.IsFalse(flow.CompleteRaid(result));

            Assert.AreSame(result, flow.PendingResult);
            Assert.IsFalse(flow.IsWorldPaused);
            Assert.AreEqual(GamePhase.Raid, flow.Phase);
        }

        // M4 리뷰 수정 (낮음 8): 레이드 씬 로드 요청 실패 → false, 로드 중 표시가 남지 않아 다시 시도할 수 있다
        [Test]
        public void StartRaid_LoadRequestFails_ReturnsFalse_CanRetry()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            rec.LoadSucceeds = false;
            LogAssert.Expect(LogType.Error, new Regex("로드 요청 실패"));
            Assert.IsFalse(flow.StartRaid());

            rec.LoadSucceeds = true;
            Assert.IsTrue(flow.StartRaid(), "실패 뒤 다시 시도");
            CollectionAssert.AreEqual(new[] { SceneNames.RaidCrimsonBasin, SceneNames.RaidCrimsonBasin }, rec.Loads);
            Assert.IsFalse(flow.StartRaid(), "요청이 받아들여진 뒤에는 무시");
        }

        [Test]
        public void StartRaid_LoadDelegateThrows_ReturnsFalse()
        {
            var throwing = new GameFlow(_ => throw new InvalidOperationException("테스트"), _ => { });
            throwing.OnSceneLoaded(SceneNames.MainMenu);
            LogAssert.Expect(LogType.Exception, new Regex("테스트"));
            LogAssert.Expect(LogType.Error, new Regex("로드 요청 실패"));
            Assert.IsFalse(throwing.StartRaid());
        }

        [Test]
        public void CompleteRaid_SecondCall_Ignored()
        {
            EnterRaid();
            RaidResult first = M4TestData.Result(RaidOutcome.Extracted, M4TestData.Ext01, "남쪽 도로");
            flow.CompleteRaid(first);
            flow.CompleteRaid(M4TestData.Result(RaidOutcome.Killed));

            Assert.AreSame(first, flow.PendingResult);
            Assert.AreEqual(1, rec.Loads.Count);
        }

        [Test]
        public void CompleteRaid_NotInRaid_Ignored()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            flow.CompleteRaid(M4TestData.Result(RaidOutcome.Killed));
            Assert.IsNull(flow.PendingResult);
            CollectionAssert.IsEmpty(rec.Loads);

            flow.OnSceneLoaded("M3_AI");   // 흐름 밖 확인 씬
            flow.CompleteRaid(M4TestData.Result(RaidOutcome.Killed));
            Assert.IsNull(flow.PendingResult);
            CollectionAssert.IsEmpty(rec.Loads);
        }

        [Test]
        public void CompleteRaid_Null_Ignored()
        {
            EnterRaid();
            flow.CompleteRaid(null);
            Assert.IsNull(flow.PendingResult);
            CollectionAssert.IsEmpty(rec.Loads);
        }

        [Test]
        public void NextRaid_CanCompleteAgain()
        {
            EnterRaid();
            flow.CompleteRaid(M4TestData.Result(RaidOutcome.MissingInAction));
            flow.OnSceneLoaded(SceneNames.MainMenu);
            flow.AcknowledgeResult();

            flow.StartRaid();
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            RaidResult second = M4TestData.Result(RaidOutcome.Abandoned);
            flow.CompleteRaid(second);

            Assert.AreSame(second, flow.PendingResult);
            CollectionAssert.AreEqual(
                new[] { SceneNames.MainMenu, SceneNames.RaidCrimsonBasin, SceneNames.MainMenu }, rec.Loads);
        }

        [Test]
        public void AcknowledgeResult_ClearsPendingResult()
        {
            EnterRaid();
            flow.CompleteRaid(M4TestData.Result(RaidOutcome.Killed));
            flow.OnSceneLoaded(SceneNames.MainMenu);
            Assert.IsNotNull(flow.PendingResult, "MainMenu 로드 뒤에도 확인 전까지 남는다");

            flow.AcknowledgeResult();
            Assert.IsNull(flow.PendingResult);
        }

        // ── 세계 정지 ─────────────────────────────────────────

        [Test]
        public void SetWorldPaused_CallsDelegateOnlyOnChange()
        {
            flow.SetWorldPaused(false);
            CollectionAssert.IsEmpty(rec.TimeScales, "처음부터 정지 아님");

            flow.SetWorldPaused(true);
            flow.SetWorldPaused(true);
            Assert.IsTrue(flow.IsWorldPaused);
            flow.SetWorldPaused(false);
            flow.SetWorldPaused(false);

            CollectionAssert.AreEqual(new[] { 0f, 1f }, rec.TimeScales);
            Assert.IsFalse(flow.IsWorldPaused);
        }

        // ── 씬 로드 ──────────────────────────────────────────

        [Test]
        public void OnSceneLoaded_SetsPhaseByName()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            Assert.AreEqual(GamePhase.MainMenu, flow.Phase);
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            Assert.AreEqual(GamePhase.Raid, flow.Phase);
            flow.OnSceneLoaded(SceneNames.Boot);
            Assert.AreEqual(GamePhase.None, flow.Phase);
            flow.OnSceneLoaded("M2_HealthInventory");
            Assert.AreEqual(GamePhase.None, flow.Phase);
        }

        [Test]
        public void OnSceneLoaded_Unpauses()
        {
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            flow.SetWorldPaused(true);
            flow.OnSceneLoaded(SceneNames.MainMenu);
            Assert.IsFalse(flow.IsWorldPaused);
            CollectionAssert.AreEqual(new[] { 0f, 1f }, rec.TimeScales);
        }

        // ── RaidResult (Core) ────────────────────────────────

        [Test]
        public void RaidResult_CopiesLists_NullIsEmpty()
        {
            var lost = new System.Collections.Generic.List<ItemDefinitionSO> { TestData.Pipe() };
            var result = M4TestData.Result(RaidOutcome.Killed, lost: lost);
            lost.Clear();

            Assert.AreEqual(1, result.Lost.Count, "생성 후 원본 리스트를 바꿔도 결과는 그대로");
            Assert.AreEqual(0, result.BroughtIn.Count);
            Assert.AreEqual(0, result.CarriedOut.Count);
            Assert.IsFalse(result.Survived);
            Assert.IsTrue(M4TestData.Result(RaidOutcome.Extracted).Survived);
        }
    }
}
