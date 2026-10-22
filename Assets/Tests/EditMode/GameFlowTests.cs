using System;
using System.Text.RegularExpressions;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // §13.3 씬 흐름·GameManager (M4 설계 §1.3·§1.4, M5 설계 §1.2·§1.4): 씬 전환, 결과 보관, 세계 정지의 유일한 주체.
    // M5: MainMenu ↔ Hideout ↔ Raid, StartRaid(Loadout)은 Hideout에서만 + RULE-03 기록, 흐름 레이드 표.
    public class GameFlowTests
    {
        private FlowRecorder rec;
        private MemorySaveStorage storage;
        private ProfileService profiles;
        private GameFlow flow;
        private FakeCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            rec = new FlowRecorder();
            storage = new MemorySaveStorage();
            catalog = FakeCatalog.Standard();
            profiles = M5TestData.LoadedProfiles(storage: storage, catalog: catalog);
            flow = rec.Create(profiles);
        }

        private void Clear()
        {
            rec.Loads.Clear();
            rec.TimeScales.Clear();
        }

        private void InHideout()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            flow.EnterHideout();
            flow.OnSceneLoaded(SceneNames.Hideout);
            Clear();
        }

        // 은신처 → 흐름 레이드 씬 로드 완료
        private void EnterRaid(Loadout loadout = null)
        {
            InHideout();
            Assert.IsTrue(flow.StartRaid(loadout ?? Loadout.Empty));
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            Clear();
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
            Assert.IsFalse(flow.HasRaidInFlow);
            CollectionAssert.IsEmpty(rec.Loads);
            CollectionAssert.IsEmpty(rec.TimeScales);
        }

        [Test]
        public void SceneNames_RaidAndHideout()
        {
            Assert.AreEqual("Raid_CrimsonBasin", SceneNames.RaidCrimsonBasin);
            Assert.AreEqual("Hideout", SceneNames.Hideout);
        }

        [Test]
        public void GamePhase_HideoutAddedAtEnd()
        {
            Assert.AreEqual(0, (int)GamePhase.None);
            Assert.AreEqual(1, (int)GamePhase.MainMenu);
            Assert.AreEqual(2, (int)GamePhase.Raid);
            Assert.AreEqual(3, (int)GamePhase.Hideout);
        }

        // ── EnterHideout / LeaveHideout ──────────────────────

        [Test]
        public void EnterHideout_FromMainMenuOrNone_LoadsHideout()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            Assert.IsTrue(flow.EnterHideout());
            CollectionAssert.AreEqual(new[] { SceneNames.Hideout }, rec.Loads);

            var other = new FlowRecorder();
            GameFlow fromNone = other.Create();
            Assert.IsTrue(fromNone.EnterHideout(), "흐름 밖(None)에서도 진입");
            CollectionAssert.AreEqual(new[] { SceneNames.Hideout }, other.Loads);
        }

        [Test]
        public void EnterHideout_TwiceBeforeLoad_LoadsOnce()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            Assert.IsTrue(flow.EnterHideout());
            Assert.IsFalse(flow.EnterHideout());
            Assert.AreEqual(1, rec.Loads.Count);
        }

        [Test]
        public void EnterHideout_FromRaidOrHideout_Rejected()
        {
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            Assert.IsFalse(flow.EnterHideout(), "레이드 중 거부");
            flow.OnSceneLoaded(SceneNames.Hideout);
            Assert.IsFalse(flow.EnterHideout(), "이미 은신처");
            CollectionAssert.IsEmpty(rec.Loads);
        }

        [Test]
        public void EnterHideout_LoadFails_CanRetry()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            rec.LoadSucceeds = false;
            LogAssert.Expect(LogType.Error, new Regex("로드 요청 실패"));
            Assert.IsFalse(flow.EnterHideout());
            rec.LoadSucceeds = true;
            Assert.IsTrue(flow.EnterHideout());
        }

        [Test]
        public void EnterHideout_LoadDelegateThrows_ReturnsFalse()
        {
            var throwing = new GameFlow(_ => throw new InvalidOperationException("테스트"), _ => { });
            throwing.OnSceneLoaded(SceneNames.MainMenu);
            LogAssert.Expect(LogType.Exception, new Regex("테스트"));
            LogAssert.Expect(LogType.Error, new Regex("로드 요청 실패"));
            Assert.IsFalse(throwing.EnterHideout());
        }

        [Test]
        public void LeaveHideout_LoadsMainMenu_OnlyFromHideout()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            Assert.IsFalse(flow.LeaveHideout());
            CollectionAssert.IsEmpty(rec.Loads);

            InHideout();
            Assert.IsTrue(flow.LeaveHideout());
            Assert.IsFalse(flow.LeaveHideout(), "로드 중엔 무시");
            CollectionAssert.AreEqual(new[] { SceneNames.MainMenu }, rec.Loads);
        }

        // ── StartRaid(Loadout) — W4 ──────────────────────────

        [Test]
        public void StartRaid_FromHideout_BeginsRaidOnce_LoadsRaidScene()
        {
            InHideout();
            Loadout brought = M5TestData.SampleLoadout(catalog, out _);

            Assert.IsTrue(flow.StartRaid(brought));

            CollectionAssert.AreEqual(new[] { SceneNames.RaidCrimsonBasin }, rec.Loads);
            Assert.AreEqual(1, storage.WriteCount, "BeginRaid 저장 1회");
            Assert.IsTrue(storage.LastWritten().raidInProgress);
            Assert.AreEqual(4, storage.LastWritten().loadout.Count);
            Assert.IsTrue(flow.HasRaidInFlow);
            Assert.IsTrue(profiles.Profile.Loadout.IsEmpty);
        }

        [Test]
        public void StartRaid_NotInHideout_Rejected_NoWrite()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            Assert.IsFalse(flow.StartRaid(Loadout.Empty), "메인 메뉴에서 바로 레이드로 가는 길 없음 (RULE-03)");
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            Assert.IsFalse(flow.StartRaid(Loadout.Empty));
            flow.OnSceneLoaded("M3_AI");
            Assert.IsFalse(flow.StartRaid(Loadout.Empty));

            CollectionAssert.IsEmpty(rec.Loads);
            Assert.AreEqual(0, storage.WriteAttempts);
            Assert.IsFalse(flow.HasRaidInFlow);
        }

        [Test]
        public void StartRaid_WithoutProfiles_Rejected()
        {
            var other = new FlowRecorder();
            GameFlow noProfiles = other.Create();
            noProfiles.OnSceneLoaded(SceneNames.Hideout);
            Assert.IsFalse(noProfiles.StartRaid(Loadout.Empty));

            var notLoaded = new ProfileService(new MemorySaveStorage(), new FakeClock().Now);
            GameFlow unloaded = other.Create(notLoaded);
            unloaded.OnSceneLoaded(SceneNames.Hideout);
            Assert.IsFalse(unloaded.StartRaid(Loadout.Empty));
            CollectionAssert.IsEmpty(other.Loads);
        }

        [Test]
        public void StartRaid_SaveFails_NotStarted()
        {
            InHideout();
            Loadout brought = M5TestData.SampleLoadout(catalog, out _);
            profiles.Profile.Loadout = brought;
            storage.WriteFails = true;

            LogAssert.Expect(LogType.Error, new Regex("세이브 실패"));
            Assert.IsFalse(flow.StartRaid(brought), "RULE-03을 보장할 수 없으면 시작하지 않는다");

            CollectionAssert.IsEmpty(rec.Loads);
            Assert.IsFalse(flow.HasRaidInFlow);
            Assert.IsFalse(profiles.Profile.RaidInProgress);
            Assert.AreSame(brought, profiles.Profile.Loadout);
        }

        [Test]
        public void StartRaid_LoadRequestFails_CancelRaid_CanRetry()
        {
            InHideout();
            Loadout brought = M5TestData.SampleLoadout(catalog, out _);
            rec.LoadSucceeds = false;

            LogAssert.Expect(LogType.Error, new Regex("로드 요청 실패"));
            Assert.IsFalse(flow.StartRaid(brought));

            Assert.IsFalse(flow.HasRaidInFlow);
            Assert.AreSame(brought, profiles.Profile.Loadout, "장비 되돌림");
            Assert.IsFalse(profiles.Profile.RaidInProgress);
            Assert.AreEqual(2, storage.WriteCount, "BeginRaid + CancelRaid");
            Assert.IsFalse(storage.LastWritten().raidInProgress);

            rec.LoadSucceeds = true;
            Assert.IsTrue(flow.StartRaid(brought), "다시 시도");
            Assert.IsFalse(flow.StartRaid(brought), "요청이 받아들여진 뒤에는 무시");
            CollectionAssert.AreEqual(new[] { SceneNames.RaidCrimsonBasin, SceneNames.RaidCrimsonBasin }, rec.Loads);
        }

        [Test]
        public void StartRaid_TwiceBeforeLoad_LoadsOnce()
        {
            InHideout();
            Assert.IsTrue(flow.StartRaid(Loadout.Empty));
            Assert.IsFalse(flow.StartRaid(Loadout.Empty));
            Assert.AreEqual(1, rec.Loads.Count);
            Assert.AreEqual(1, storage.WriteCount);
        }

        [Test]
        public void StartRaid_WhilePaused_Unpauses()
        {
            InHideout();
            flow.SetWorldPaused(true);
            flow.StartRaid(Loadout.Empty);
            Assert.IsFalse(flow.IsWorldPaused);
            CollectionAssert.AreEqual(new[] { 0f, 1f }, rec.TimeScales);
        }

        [Test]
        public void StartRaid_NullLoadout_TreatedAsEmpty()
        {
            InHideout();
            Assert.IsTrue(flow.StartRaid(null));
            Assert.IsTrue(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out Loadout loadout));
            Assert.IsTrue(loadout.IsEmpty);
        }

        // ── CompleteRaid ──────────────────────────────────────

        // M4 리뷰 수정 유지: 정지는 CompleteRaid가 아니라 목적지(M5: Hideout) 로드 완료가 푼다
        [Test]
        public void CompleteRaid_StoresResult_LoadsHideout_StaysPausedUntilSceneLoaded()
        {
            EnterRaid();
            flow.SetWorldPaused(true);   // 종료 연출 중 세계 정지
            RaidResult result = M4TestData.Result(RaidOutcome.Killed);
            int writes = storage.WriteAttempts;

            Assert.IsTrue(flow.CompleteRaid(result));

            Assert.AreSame(result, flow.PendingResult);
            CollectionAssert.AreEqual(new[] { SceneNames.Hideout }, rec.Loads);
            Assert.IsTrue(flow.IsWorldPaused, "로드 중에도 레이드 세계는 정지");
            CollectionAssert.AreEqual(new[] { 0f }, rec.TimeScales);
            Assert.AreEqual(writes, storage.WriteAttempts, "레이드 중에는 쓰지 않는다 (RULE-03)");

            flow.OnSceneLoaded(SceneNames.Hideout);
            Assert.AreEqual(GamePhase.Hideout, flow.Phase);
            Assert.IsFalse(flow.IsWorldPaused, "Hideout 로드 완료에서 해제");
            CollectionAssert.AreEqual(new[] { 0f, 1f }, rec.TimeScales);
        }

        // M4 리뷰 수정 유지: 목적지 로드 요청 실패 → 결과는 남기고 정지만 푼다 (영구 정지 방지)
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

            flow.OnSceneLoaded(SceneNames.Hideout);
            flow.CompleteRaid(M4TestData.Result(RaidOutcome.Killed));
            Assert.IsNull(flow.PendingResult);

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
        public void AcknowledgeResult_ClearsPendingResult()
        {
            EnterRaid();
            flow.CompleteRaid(M4TestData.Result(RaidOutcome.Killed));
            flow.OnSceneLoaded(SceneNames.Hideout);
            Assert.IsNotNull(flow.PendingResult, "Hideout 로드 뒤에도 확인 전까지 남는다");

            flow.AcknowledgeResult();
            Assert.IsNull(flow.PendingResult);
        }

        // ── 흐름 레이드 표 (TakeRaidReturn) ─────────────────

        [Test]
        public void TakeRaidReturn_Finished_Once()
        {
            EnterRaid();
            RaidResult result = M4TestData.Result(RaidOutcome.Killed);
            flow.CompleteRaid(result);

            // 은신처 Awake 시점(OnSceneLoaded 전)에 꺼낸다 — Phase를 보지 않는다
            Assert.AreEqual(RaidReturn.Finished, flow.TakeRaidReturn(out RaidResult taken));
            Assert.AreSame(result, taken);
            Assert.IsFalse(flow.HasRaidInFlow);
            Assert.AreSame(result, flow.PendingResult, "표시용 결과는 그대로");

            Assert.AreEqual(RaidReturn.None, flow.TakeRaidReturn(out RaidResult again), "두 번 반영되지 않게");
            Assert.IsNull(again);
        }

        [Test]
        public void TakeRaidReturn_NoResult_Interrupted()
        {
            EnterRaid();
            // 결과 없이 은신처로 (디버그·테스트가 레이드 씬에서 바로 Hideout을 로드)
            Assert.AreEqual(RaidReturn.Interrupted, flow.TakeRaidReturn(out RaidResult result));
            Assert.IsNull(result);
            Assert.IsFalse(flow.HasRaidInFlow);
        }

        [Test]
        public void TakeRaidReturn_NoFlowRaid_None()
        {
            InHideout();
            Assert.AreEqual(RaidReturn.None, flow.TakeRaidReturn(out RaidResult result));
            Assert.IsNull(result);
        }

        // Q25: 에디터에서 레이드 씬 직접 Play → 끝나면 Hideout으로 가지만 결과는 표시만 (흐름 레이드 표 없음)
        [Test]
        public void DirectRaid_CompleteGoesToHideout_ReturnNone_ResultForDisplay()
        {
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            RaidResult result = M4TestData.Result(RaidOutcome.Extracted, M4TestData.Ext01, "남쪽 도로");

            Assert.IsTrue(flow.CompleteRaid(result));

            CollectionAssert.AreEqual(new[] { SceneNames.Hideout }, rec.Loads);
            Assert.AreEqual(RaidReturn.None, flow.TakeRaidReturn(out _));
            Assert.AreSame(result, flow.PendingResult);
        }

        // ── TryGetRaidLoadout ────────────────────────────────

        [Test]
        public void TryGetRaidLoadout_FlowRaid_RaidSceneOnly_UntilComplete()
        {
            InHideout();
            Loadout brought = M5TestData.SampleLoadout(catalog, out _);
            Assert.IsFalse(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out _), "흐름 레이드 전");

            flow.StartRaid(brought);
            // 레이드 씬 Awake 시점: OnSceneLoaded 전이라 Phase는 아직 Hideout
            Assert.IsTrue(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out Loadout loadout));
            Assert.AreSame(brought, loadout);
            Assert.IsFalse(flow.TryGetRaidLoadout("M2_HealthInventory", out Loadout other), "확인 씬은 startingItems");
            Assert.IsNull(other);

            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            Assert.IsTrue(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out _));

            flow.CompleteRaid(M4TestData.Result(RaidOutcome.Killed));
            Assert.IsFalse(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out _), "완료 후");
        }

        [Test]
        public void TryGetRaidLoadout_DirectRaid_False()
        {
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            Assert.IsFalse(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out _));
        }

        // ── 두 번째 판 ───────────────────────────────────────

        [Test]
        public void SecondRaid_AfterReturn_StartsAndGetsLoadoutAgain()
        {
            EnterRaid();
            flow.CompleteRaid(M4TestData.Result(RaidOutcome.MissingInAction));
            flow.TakeRaidReturn(out RaidResult first);
            profiles.ApplyRaidResult(first);
            flow.OnSceneLoaded(SceneNames.Hideout);
            flow.AcknowledgeResult();

            Loadout second = M5TestData.SampleLoadout(catalog, out _);
            Assert.IsTrue(flow.StartRaid(second));
            Assert.IsTrue(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out Loadout got),
                "이전 레이드 완료 표시가 새 레이드 Awake를 막지 않는다");
            Assert.AreSame(second, got);
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            RaidResult result = M4TestData.Result(RaidOutcome.Abandoned);
            Assert.IsTrue(flow.CompleteRaid(result));

            Assert.AreSame(result, flow.PendingResult);
            CollectionAssert.AreEqual(new[] { SceneNames.Hideout, SceneNames.RaidCrimsonBasin, SceneNames.Hideout }, rec.Loads);
        }

        // 흐름 레이드 표를 꺼내기 전에는 다음 레이드를 시작하지 않는다 (결과 반영을 건너뛰지 않게)
        [Test]
        public void StartRaid_BeforeReturnTaken_Rejected()
        {
            EnterRaid();
            flow.CompleteRaid(M4TestData.Result(RaidOutcome.Killed));
            flow.OnSceneLoaded(SceneNames.Hideout);
            Clear();

            Assert.IsFalse(flow.StartRaid(Loadout.Empty));
            CollectionAssert.IsEmpty(rec.Loads);
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
            flow.OnSceneLoaded(SceneNames.Hideout);
            Assert.AreEqual(GamePhase.Hideout, flow.Phase);
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
            flow.OnSceneLoaded(SceneNames.Hideout);
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
            Assert.IsNull(result.CarriedOutLoadout);
        }
    }
}
