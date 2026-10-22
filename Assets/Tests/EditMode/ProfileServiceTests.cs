using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // §13.3 세이브·RULE-03·RULE-01 반영 (M5 설계 §1.3~§1.5, §2.4): ProfileService (메모리 저장소)
    public class ProfileServiceTests
    {
        private MemorySaveStorage storage;
        private FakeCatalog catalog;
        private FakeClock clock;

        [SetUp]
        public void SetUp()
        {
            storage = new MemorySaveStorage();
            catalog = FakeCatalog.Standard();
            clock = new FakeClock();
        }

        private ProfileService Create() => new ProfileService(storage, clock.Now);

        private ProfileService Loaded(string json = null) => M5TestData.LoadedProfiles(json, storage, catalog, clock);

        // 다른 ProfileService가 같은 저장소에서 읽는다 (= 다음 실행)
        private ProfileService NextRun()
        {
            var next = new ProfileService(storage, clock.Now);
            next.EnsureLoaded(catalog, M5TestData.Defaults());
            return next;
        }

        // ── 생성·첫 로드 ──────────────────────────────────────

        [Test]
        public void Constructor_NullArgs_Throw_AndDoesNotRead()
        {
            Assert.Throws<ArgumentNullException>(() => new ProfileService(null, clock.Now));
            Assert.Throws<ArgumentNullException>(() => new ProfileService(storage, null));

            ProfileService profiles = Create();
            Assert.AreEqual(0, storage.ReadCount, "생성자는 파일을 읽지 않는다 (§1.6)");
            Assert.IsFalse(profiles.IsLoaded);
            Assert.IsNull(profiles.Profile);
            Assert.AreEqual(storage.Location, profiles.StorageLocation);
        }

        [Test]
        public void EnsureLoaded_NullCatalog_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Create().EnsureLoaded(null, M5TestData.Defaults()));
        }

        [Test]
        public void NoFile_NewProfileDefaults_NoNotice_NoWrite()
        {
            ProfileService profiles = Create();
            SaveLoadReport report = profiles.EnsureLoaded(catalog, M5TestData.Defaults());

            Assert.AreEqual(SaveLoadStatus.NewProfile, report.Status);
            Assert.IsTrue(profiles.IsLoaded);
            PlayerProfile p = profiles.Profile;
            Assert.AreEqual(6000, p.Casings);
            Assert.AreEqual(new Vector2Int(10, 30), p.Stash.Size);
            Assert.AreEqual(GridKind.Loot, p.Stash.Ref.Kind, "보관함은 열린 외부 그리드 자리 (Q28)");
            Assert.IsTrue(p.Loadout.IsEmpty);
            Assert.IsNull(p.LastKitClaimUtc);
            Assert.IsFalse(p.RaidInProgress);
            Assert.IsNull(profiles.PendingNotice);
            Assert.AreEqual(0, storage.WriteAttempts, "읽기는 쓰지 않는다 (W1 저장은 은신처가)");
        }

        [Test]
        public void EnsureLoaded_ReadsOnlyOnce()
        {
            ProfileService profiles = Create();
            SaveLoadReport first = profiles.EnsureLoaded(catalog, M5TestData.Defaults());
            PlayerProfile profile = profiles.Profile;
            storage.Text = "{\"version\": 1, \"casings\": 1}";

            SaveLoadReport second = profiles.EnsureLoaded(catalog, M5TestData.Defaults());

            Assert.AreEqual(1, storage.ReadCount);
            Assert.AreSame(first, second);
            Assert.AreSame(profile, profiles.Profile);
        }

        [Test]
        public void Save_ThenNextRun_SameState()
        {
            ProfileService profiles = Loaded();
            profiles.Profile.Casings = 1234;
            profiles.Profile.LastKitClaimUtc = clock.UtcNow;
            Assert.IsTrue(profiles.Profile.Stash.TryPlace(M5TestData.Mag556(catalog, 4), new Vector2Int(2, 3), true));
            profiles.Profile.Loadout = M5TestData.SampleLoadout(catalog, out _);

            Assert.IsTrue(profiles.Save());
            Assert.IsFalse(profiles.LastSaveFailed);
            Assert.AreEqual(1, storage.WriteCount);

            ProfileService next = NextRun();
            Assert.AreEqual(SaveLoadStatus.Loaded, next.EnsureLoaded(catalog, M5TestData.Defaults()).Status);
            Assert.AreEqual(1234, next.Profile.Casings);
            Assert.AreEqual(clock.UtcNow, next.Profile.LastKitClaimUtc);
            Assert.AreEqual(1, next.Profile.Stash.Items.Count);
            Assert.IsTrue(next.Profile.Stash.Items[0].Rotated);
            Assert.AreEqual(4, next.Profile.Loadout.Entries.Count);
            Assert.IsNull(next.PendingNotice);
        }

        [Test]
        public void Save_NotLoaded_False()
        {
            ProfileService profiles = Create();
            LogAssert.Expect(LogType.Error, new Regex("저장 거부"));
            Assert.IsFalse(profiles.Save());
            Assert.AreEqual(0, storage.WriteAttempts);
        }

        [Test]
        public void Save_WriteFails_LastSaveFailed()
        {
            ProfileService profiles = Loaded();
            storage.WriteFails = true;

            LogAssert.Expect(LogType.Error, new Regex("세이브 실패"));
            Assert.IsFalse(profiles.Save());
            Assert.IsTrue(profiles.LastSaveFailed);
            StringAssert.Contains("테스트 쓰기 실패", profiles.LastSaveError);

            storage.WriteFails = false;
            Assert.IsTrue(profiles.Save());
            Assert.IsFalse(profiles.LastSaveFailed);
            Assert.IsNull(profiles.LastSaveError);
        }

        // ── 손상·버전·읽기 오류 (Q22) ─────────────────────────

        [Test]
        public void Corrupt_Quarantined_NewProfile_Notice()
        {
            ProfileService profiles = Loaded("this is not json");

            SaveLoadReport notice = profiles.PendingNotice;
            Assert.IsNotNull(notice);
            Assert.AreEqual(SaveLoadStatus.Corrupt, notice.Status);
            Assert.AreEqual(1, storage.QuarantineCount);
            Assert.AreEqual("this is not json", storage.QuarantinedText);
            Assert.AreEqual("memory://save.corrupt.json", notice.QuarantinedPath);
            Assert.AreEqual(6000, profiles.Profile.Casings, "새 프로필");
            Assert.IsTrue(notice.HasNotice);
        }

        [Test]
        public void UnsupportedVersion_Quarantined_NewProfile()
        {
            ProfileService profiles = Loaded("{\"version\": 99, \"casings\": 5}");

            Assert.AreEqual(SaveLoadStatus.UnsupportedVersion, profiles.PendingNotice.Status);
            Assert.AreEqual(1, storage.QuarantineCount);
            Assert.AreEqual(6000, profiles.Profile.Casings);
        }

        // M5 리뷰 1: 읽기 오류는 손상과 다르다 — 재시도 뒤에도 실패하면 격리·덮어쓰기 없이 이 실행은 읽기 전용
        [Test]
        public void ReadError_RetriesThenReadOnly_FileUntouched_NoWrites()
        {
            const string original = "{\"version\": 1, \"casings\": 77}";
            storage.Text = original;
            storage.ReadFails = true;
            var waits = new List<int>();
            var profiles = new ProfileService(storage, clock.Now, waits.Add);
            LogAssert.Expect(LogType.Error, new Regex("세이브를 읽지 못했다"));

            SaveLoadReport report = profiles.EnsureLoaded(catalog, M5TestData.Defaults());

            Assert.AreEqual(ProfileService.ReadAttempts, storage.ReadCount, "짧게 재시도");
            CollectionAssert.AreEqual(
                Enumerable.Repeat(ProfileService.ReadRetryDelayMs, ProfileService.ReadAttempts - 1).ToArray(), waits);
            Assert.AreEqual(SaveLoadStatus.ReadError, report.Status);
            StringAssert.Contains("테스트 읽기 오류", report.Error);
            Assert.AreSame(report, profiles.PendingNotice, "알림");
            Assert.IsTrue(profiles.IsReadOnly);
            Assert.AreEqual(0, storage.QuarantineCount, "격리하지 않는다");
            Assert.IsNull(report.QuarantinedPath);
            Assert.IsTrue(profiles.IsLoaded, "은신처는 열린다(메모리의 새 프로필)");
            Assert.AreEqual(M5TestData.StartingCasings, profiles.Profile.Casings);

            Assert.IsFalse(profiles.Save(), "W1·W2·W3 저장 거부");
            Assert.IsTrue(profiles.LastSaveFailed);
            Assert.AreEqual(ProfileService.ReadOnlySaveError, profiles.LastSaveError);
            Assert.IsFalse(profiles.BeginRaid(M5TestData.SampleLoadout(catalog, out _)), "W4 실패 → 레이드 시작 거부");
            Assert.IsFalse(profiles.Profile.RaidInProgress);
            profiles.CancelRaid(null);
            Assert.AreEqual(0, storage.WriteAttempts, "파일에 한 번도 쓰지 않는다");
            Assert.AreEqual(original, storage.Text);

            // 같은 실행의 재진입은 다시 읽지 않고 계속 읽기 전용, 저장소를 바꾸면(= 다음 실행) 풀린다
            profiles.EnsureLoaded(catalog, M5TestData.Defaults());
            Assert.IsTrue(profiles.IsReadOnly);
            storage.ReadFails = false;
            profiles.UseStorage(storage);
            Assert.IsFalse(profiles.IsReadOnly);
            Assert.AreEqual(SaveLoadStatus.Loaded, profiles.EnsureLoaded(catalog, M5TestData.Defaults()).Status);
            Assert.AreEqual(77, profiles.Profile.Casings, "잠금이 풀린 다음 실행은 원래 진행도를 읽는다");
            Assert.IsTrue(profiles.Save());
        }

        [Test]
        public void ReadError_Transient_RetrySucceeds_Loaded()
        {
            storage.Text = "{\"version\": 1, \"casings\": 55}";
            storage.ReadFailCount = ProfileService.ReadAttempts - 1;
            var waits = new List<int>();
            var profiles = new ProfileService(storage, clock.Now, waits.Add);

            SaveLoadReport report = profiles.EnsureLoaded(catalog, M5TestData.Defaults());

            Assert.AreEqual(SaveLoadStatus.Loaded, report.Status);
            Assert.AreEqual(ProfileService.ReadAttempts, storage.ReadCount);
            Assert.AreEqual(ProfileService.ReadAttempts - 1, waits.Count);
            Assert.IsFalse(profiles.IsReadOnly);
            Assert.IsNull(profiles.PendingNotice);
            Assert.AreEqual(55, profiles.Profile.Casings);
        }

        // M5 리뷰 2: 파싱은 됐지만 해석(ToProfile) 중 예외 → 손상과 같게 격리 + 새 프로필 (은신처가 멈추지 않게)
        [Test]
        public void ToProfileThrows_TreatedAsCorrupt_Quarantined_NewProfile()
        {
            storage.Text = "{\"version\": 1, \"casings\": 9, \"stash\": [{\"id\": \"WPN-RIF-01\", \"container\": \"Stash\"}]}";
            var profiles = new ProfileService(storage, clock.Now, _ => { });

            SaveLoadReport report = null;
            Assert.DoesNotThrow(() => report = profiles.EnsureLoaded(new ThrowingCatalog(), M5TestData.Defaults()));

            Assert.AreEqual(SaveLoadStatus.Corrupt, report.Status);
            StringAssert.Contains("해석 실패", report.Error);
            Assert.AreEqual(1, storage.QuarantineCount);
            Assert.IsNotNull(report.QuarantinedPath);
            Assert.AreEqual(0, report.UnknownItemIds.Count, "일부 채운 보고서는 버린다");
            Assert.IsTrue(profiles.IsLoaded);
            Assert.IsFalse(profiles.IsReadOnly);
            Assert.AreEqual(M5TestData.StartingCasings, profiles.Profile.Casings);
            Assert.IsTrue(profiles.Save());
        }

        private sealed class ThrowingCatalog : IItemCatalog
        {
            public bool TryGet(string id, out ItemDefinitionSO definition) =>
                throw new InvalidOperationException("테스트 카탈로그 예외");
        }

        // M5 리뷰 7: 시도 직전 실패 표시 지우기
        [Test]
        public void ClearSaveFailure_ResetsFlag_KeepsReadOnly()
        {
            ProfileService profiles = Loaded();
            storage.WriteFails = true;
            LogAssert.Expect(LogType.Error, new Regex("세이브 실패"));
            Assert.IsFalse(profiles.Save());
            profiles.ClearSaveFailure();
            Assert.IsFalse(profiles.LastSaveFailed);
            Assert.IsNull(profiles.LastSaveError);
        }

        // M5 리뷰 5: 은신처 진입 완료 표시
        [Test]
        public void HideoutEntryToken_MarkClear_BeginRaidAndUseStorageClear()
        {
            ProfileService profiles = Loaded();
            Assert.IsFalse(profiles.IsHideoutEntered(0));
            Assert.IsFalse(profiles.IsHideoutEntered(5));
            profiles.MarkHideoutEntered(5);
            Assert.IsTrue(profiles.IsHideoutEntered(5));
            Assert.IsFalse(profiles.IsHideoutEntered(6), "다른 씬 인스턴스");
            profiles.ClearHideoutEntry();
            Assert.IsFalse(profiles.IsHideoutEntered(5));

            profiles.MarkHideoutEntered(7);
            Assert.IsTrue(profiles.BeginRaid(Loadout.Empty));
            Assert.IsFalse(profiles.IsHideoutEntered(7), "레이드 시작 = 은신처를 떠남");

            profiles.MarkHideoutEntered(8);
            profiles.UseStorage(new MemorySaveStorage());
            Assert.AreEqual(0, profiles.HideoutEntryToken);
        }

        [Test]
        public void Corrupt_QuarantineFails_StillNewProfile()
        {
            storage.QuarantineFails = true;
            LogAssert.Expect(LogType.Error, new Regex("격리도 실패"));

            ProfileService profiles = Loaded("garbage");

            Assert.IsTrue(profiles.IsLoaded);
            Assert.AreEqual(SaveLoadStatus.Corrupt, profiles.PendingNotice.Status);
            Assert.IsNull(profiles.PendingNotice.QuarantinedPath);
        }

        [Test]
        public void UnknownIds_Notice()
        {
            ProfileService profiles = Loaded(
                "{\"version\": 1, \"stash\": [{\"id\": \"NOPE\", \"container\": \"Stash\"}]}");

            Assert.AreEqual(SaveLoadStatus.Loaded, profiles.PendingNotice.Status);
            CollectionAssert.AreEqual(new[] { "NOPE" }, profiles.PendingNotice.UnknownItemIds);
        }

        [Test]
        public void AcknowledgeNotice_Clears()
        {
            ProfileService profiles = Loaded("garbage");
            Assert.IsNotNull(profiles.PendingNotice);
            profiles.AcknowledgeNotice();
            Assert.IsNull(profiles.PendingNotice);
        }

        // ── RULE-03 (§1.4) ────────────────────────────────────

        [Test]
        public void RaidInProgressFile_LoadoutDeleted_StashCasingsKitKept_Notice()
        {
            // 레이드 시작 기록(W4) 뒤 강제 종료된 파일을 만든다
            ProfileService first = Loaded();
            first.Profile.Casings = 777;
            first.Profile.LastKitClaimUtc = clock.UtcNow;
            var stashItem = M5TestData.Plain(catalog, "JNK-02");
            Assert.IsTrue(first.Profile.Stash.TryPlace(stashItem, Vector2Int.zero, false));
            Loadout brought = M5TestData.SampleLoadout(catalog, out _);
            Assert.IsTrue(first.BeginRaid(brought));
            StringAssert.Contains("\"raidInProgress\": true", storage.Text);

            ProfileService next = NextRun();   // 다음 실행

            SaveLoadReport notice = next.PendingNotice;
            Assert.IsNotNull(notice);
            Assert.IsTrue(notice.CrashPenaltyApplied);
            CollectionAssert.AreEqual(
                new[] { catalog.Get("WPN-RIF-01"), catalog.Get("RIG-01"), catalog.Get("MAG-556-30"), catalog.Get("MED-BND-01") },
                notice.CrashLost);
            Assert.IsTrue(next.Profile.Loadout.IsEmpty);
            Assert.IsFalse(next.Profile.RaidInProgress);
            Assert.AreEqual(777, next.Profile.Casings);
            Assert.AreEqual(clock.UtcNow, next.Profile.LastKitClaimUtc);
            Assert.AreEqual(1, next.Profile.Stash.Items.Count);

            // W1 저장으로 파일에 반영 (raidInProgress=false, loadout 빈 배열)
            Assert.IsTrue(next.Save());
            SaveData written = storage.LastWritten();
            Assert.IsFalse(written.raidInProgress);
            Assert.AreEqual(0, written.loadout.Count);
            Assert.AreEqual(1, written.stash.Count);
        }

        [Test]
        public void RaidInProgressFile_WithUnknownIds_CountsInNotice()
        {
            ProfileService profiles = Loaded(
                "{\"version\": 1, \"raidInProgress\": true, \"loadout\": [" +
                "{\"id\": \"NOPE\", \"container\": \"Equipped\", \"slot\": \"Helmet\"}," +
                "{\"id\": \"JNK-03\", \"container\": \"Pocket\", \"index\": 0}]}");

            SaveLoadReport notice = profiles.PendingNotice;
            Assert.IsTrue(notice.CrashPenaltyApplied);
            CollectionAssert.AreEqual(new[] { catalog.Get("JNK-03") }, notice.CrashLost);
            CollectionAssert.AreEqual(new[] { "NOPE" }, notice.UnknownItemIds);
            Assert.IsTrue(profiles.Profile.Loadout.IsEmpty);
        }

        // ── BeginRaid / CancelRaid (W4) ───────────────────────

        [Test]
        public void BeginRaid_WritesLoadoutAndFlag_ThenEmptiesMemory()
        {
            ProfileService profiles = Loaded();
            Loadout brought = M5TestData.SampleLoadout(catalog, out _);

            Assert.IsTrue(profiles.BeginRaid(brought));

            Assert.AreEqual(1, storage.WriteCount);
            SaveData written = storage.LastWritten();
            Assert.IsTrue(written.raidInProgress, "§13.3 레이드 진입 기록");
            Assert.AreEqual(4, written.loadout.Count, "파일엔 반입 장비가 그대로 (Q4)");
            Assert.AreEqual("WPN-RIF-01", written.loadout[0].id);
            Assert.IsTrue(profiles.Profile.RaidInProgress);
            Assert.IsTrue(profiles.Profile.Loadout.IsEmpty, "메모리 장비는 비운다");
        }

        [Test]
        public void BeginRaid_EmptyLoadout_Allowed()
        {
            ProfileService profiles = Loaded();
            Assert.IsTrue(profiles.BeginRaid(Loadout.Empty), "빈 장비도 허용 (Q26)");
            Assert.IsFalse(profiles.BeginRaid(null), "이미 레이드 중이면 거부");
        }

        [Test]
        public void BeginRaid_WriteFails_RestoresAndReturnsFalse()
        {
            ProfileService profiles = Loaded();
            Loadout before = M5TestData.SampleLoadout(catalog, out _);
            profiles.Profile.Loadout = before;
            storage.WriteFails = true;

            LogAssert.Expect(LogType.Error, new Regex("세이브 실패"));
            Assert.IsFalse(profiles.BeginRaid(Loadout.Empty));

            Assert.IsFalse(profiles.Profile.RaidInProgress);
            Assert.AreSame(before, profiles.Profile.Loadout);
            Assert.IsTrue(profiles.LastSaveFailed);
        }

        [Test]
        public void BeginRaid_NotLoaded_False()
        {
            LogAssert.Expect(LogType.Error, new Regex("BeginRaid 거부"));
            Assert.IsFalse(Create().BeginRaid(Loadout.Empty));
            Assert.AreEqual(0, storage.WriteAttempts);
        }

        [Test]
        public void CancelRaid_RestoresLoadout_ClearsFlag_Saves()
        {
            ProfileService profiles = Loaded();
            Loadout brought = M5TestData.SampleLoadout(catalog, out _);
            profiles.BeginRaid(brought);

            profiles.CancelRaid(brought);

            Assert.AreSame(brought, profiles.Profile.Loadout);
            Assert.IsFalse(profiles.Profile.RaidInProgress);
            Assert.AreEqual(2, storage.WriteCount);
            SaveData written = storage.LastWritten();
            Assert.IsFalse(written.raidInProgress);
            Assert.AreEqual(4, written.loadout.Count);
        }

        [Test]
        public void CancelRaid_NullLoadout_UsesBroughtLoadout()
        {
            ProfileService profiles = Loaded();
            Loadout brought = M5TestData.SampleLoadout(catalog, out _);
            profiles.BeginRaid(brought);
            profiles.CancelRaid(null);
            Assert.AreSame(brought, profiles.Profile.Loadout);
        }

        // ── ApplyRaidResult (RULE-01 반영 지점) ───────────────

        [Test]
        public void ApplyRaidResult_Extracted_CarriedOutLoadoutBecomesLoadout()
        {
            ProfileService profiles = Loaded();
            var stashItem = M5TestData.Plain(catalog, "JNK-02");
            profiles.Profile.Stash.TryPlace(stashItem, Vector2Int.zero, false);
            profiles.BeginRaid(M5TestData.SampleLoadout(catalog, out _));
            Loadout carried = M5TestData.SampleLoadout(catalog, out _);
            int writes = storage.WriteCount;

            profiles.ApplyRaidResult(M5TestData.Extracted(carried));

            Assert.AreSame(carried, profiles.Profile.Loadout, "위치 그대로 (Q3)");
            Assert.IsFalse(profiles.Profile.RaidInProgress);
            Assert.AreEqual(1, profiles.Profile.Stash.Items.Count, "보관함 변화 없음");
            Assert.AreEqual(writes, storage.WriteCount, "저장은 호출자 (W1)");
            Assert.IsNull(profiles.PendingNotice);
        }

        [TestCase(RaidOutcome.Killed)]
        [TestCase(RaidOutcome.MissingInAction)]
        [TestCase(RaidOutcome.Abandoned)]
        public void ApplyRaidResult_NotExtracted_LoadoutEmpty(RaidOutcome outcome)
        {
            ProfileService profiles = Loaded();
            profiles.BeginRaid(M5TestData.SampleLoadout(catalog, out _));
            Loadout carried = M5TestData.SampleLoadout(catalog, out _);

            profiles.ApplyRaidResult(new RaidResult(outcome, null, null, outcome == RaidOutcome.Killed, BodyPart.Head,
                10f, 0, null, null, null, carried));

            Assert.IsTrue(profiles.Profile.Loadout.IsEmpty, "RULE-01");
            Assert.IsFalse(profiles.Profile.RaidInProgress);
        }

        [Test]
        public void ApplyRaidResult_ExtractedWithoutLoadout_Empty()
        {
            ProfileService profiles = Loaded();
            profiles.BeginRaid(Loadout.Empty);
            profiles.ApplyRaidResult(M4TestData.Result(RaidOutcome.Extracted, "EXT-01", "남쪽 도로"));
            Assert.IsTrue(profiles.Profile.Loadout.IsEmpty);
            Assert.IsFalse(profiles.Profile.RaidInProgress);
        }

        [Test]
        public void ApplyRaidResult_ThenSave_FileReflectsResult()
        {
            ProfileService profiles = Loaded();
            profiles.BeginRaid(M5TestData.SampleLoadout(catalog, out _));
            profiles.ApplyRaidResult(M4TestData.Result(RaidOutcome.Killed));
            Assert.IsTrue(profiles.Save());

            SaveData written = storage.LastWritten();
            Assert.IsFalse(written.raidInProgress);
            Assert.AreEqual(0, written.loadout.Count);
        }

        // ── ApplyInterruptedRaid (같은 실행, 결과 없이 귀환) ──

        [Test]
        public void ApplyInterruptedRaid_LikeDeath_WithNotice()
        {
            ProfileService profiles = Loaded();
            profiles.BeginRaid(M5TestData.SampleLoadout(catalog, out _));

            profiles.ApplyInterruptedRaid();

            Assert.IsTrue(profiles.Profile.Loadout.IsEmpty);
            Assert.IsFalse(profiles.Profile.RaidInProgress);
            SaveLoadReport notice = profiles.PendingNotice;
            Assert.IsNotNull(notice);
            Assert.IsTrue(notice.CrashPenaltyApplied);
            Assert.AreEqual(4, notice.CrashLost.Count, "맡긴 반입 목록");
        }

        [Test]
        public void ApplyInterruptedRaid_MergesIntoExistingNotice()
        {
            ProfileService profiles = Loaded("{\"version\": 1, \"stash\": [{\"id\": \"NOPE\", \"container\": \"Stash\"}]}");
            SaveLoadReport existing = profiles.PendingNotice;
            profiles.BeginRaid(Loadout.Empty);

            profiles.ApplyInterruptedRaid();

            Assert.AreSame(existing, profiles.PendingNotice);
            Assert.IsTrue(existing.CrashPenaltyApplied);
            Assert.AreEqual(1, existing.UnknownItemIds.Count);
        }

        // M5 리뷰 3: 손상 알림을 확인한 뒤의 새 RULE-03 알림에 손상 문구(Status)가 다시 붙지 않는다
        [Test]
        public void ApplyInterruptedRaid_AfterAcknowledgedCorrupt_NewNoticeIsLoaded()
        {
            ProfileService profiles = Loaded("garbage");
            Assert.AreEqual(SaveLoadStatus.Corrupt, profiles.PendingNotice.Status);
            profiles.AcknowledgeNotice();
            profiles.BeginRaid(Loadout.Empty);

            profiles.ApplyInterruptedRaid();

            SaveLoadReport notice = profiles.PendingNotice;
            Assert.AreEqual(SaveLoadStatus.Loaded, notice.Status);
            Assert.IsTrue(notice.CrashPenaltyApplied);
            Assert.IsNull(notice.QuarantinedPath);
        }

        // ── UseStorage ───────────────────────────────────────

        [Test]
        public void UseStorage_DropsProfile_NextEnsureLoadedReadsNewStorage()
        {
            ProfileService profiles = Loaded("garbage");
            Assert.IsNotNull(profiles.PendingNotice);

            var other = new MemorySaveStorage { Text = "{\"version\": 1, \"casings\": 42}" };
            profiles.UseStorage(other);

            Assert.IsFalse(profiles.IsLoaded);
            Assert.IsNull(profiles.Profile);
            Assert.IsNull(profiles.PendingNotice);
            Assert.AreEqual(other.Location, profiles.StorageLocation);
            Assert.AreEqual(0, other.ReadCount, "UseStorage는 읽지 않는다");

            profiles.EnsureLoaded(catalog, M5TestData.Defaults());
            Assert.AreEqual(1, other.ReadCount);
            Assert.AreEqual(42, profiles.Profile.Casings);
            Assert.Throws<ArgumentNullException>(() => profiles.UseStorage(null));
        }

        // ── PlayerProfile ────────────────────────────────────

        [Test]
        public void PlayerProfile_Setters_Clamp()
        {
            var p = new PlayerProfile(new Vector2Int(0, -2), -5);
            Assert.AreEqual(Vector2Int.one, p.Stash.Size);
            Assert.AreEqual(0, p.Casings);
            p.Casings = -1;
            Assert.AreEqual(0, p.Casings);
            p.Loadout = null;
            Assert.AreSame(Loadout.Empty, p.Loadout);
        }

        [Test]
        public void SaveLoadReport_HasNotice()
        {
            Assert.IsFalse(new SaveLoadReport { Status = SaveLoadStatus.Loaded }.HasNotice);
            Assert.IsFalse(new SaveLoadReport { Status = SaveLoadStatus.NewProfile }.HasNotice);
            Assert.IsTrue(new SaveLoadReport { Status = SaveLoadStatus.Corrupt }.HasNotice);
            Assert.IsTrue(new SaveLoadReport { Status = SaveLoadStatus.UnsupportedVersion }.HasNotice);
            Assert.IsTrue(new SaveLoadReport { Status = SaveLoadStatus.ReadError }.HasNotice);
            Assert.IsTrue(new SaveLoadReport { CrashPenaltyApplied = true }.HasNotice);
            Assert.IsTrue(new SaveLoadReport { DroppedItemCount = 1 }.HasNotice);
            var unknown = new SaveLoadReport();
            unknown.UnknownItemIds.Add("X");
            Assert.IsTrue(unknown.HasNotice);
        }
    }
}
