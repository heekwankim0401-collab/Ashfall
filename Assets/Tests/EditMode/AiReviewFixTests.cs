using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // M3 커밋 전 리뷰 지적 수정의 회귀 테스트 (리뷰 항목 번호 = 이름 앞 R).
    public class AiReviewFixTests
    {
        private AiConfigSO config;
        private FakeEnemyWorld world;

        private static readonly Vector3 Self = Vector3.zero;
        private static readonly Vector3 Target = new Vector3(0f, 0f, 30f);
        private static readonly Vector3 CoverPos = new Vector3(0f, 0f, 5f);

        [SetUp]
        public void SetUp()
        {
            config = M3TestData.AiConfig();
            world = new FakeEnemyWorld();
        }

        private EnemyBrain Make(EnemyArchetypeSO archetype, int id = 1, Squad squad = null,
                                IReadOnlyList<Vector3> route = null, int order = 0) =>
            new EnemyBrain(id, archetype, config, world, route, squad, new System.Random(7), order);

        private static float ToCombat(EnemyBrain brain, float t, Vector3 self, Vector3 target)
        {
            brain.OnHeard(target, NoiseKind.Gunshot, t);
            brain.Tick(AiTest.Seen(t, self, target), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            t += 1f;
            brain.Tick(AiTest.Seen(t, self, target), 1f);
            Assert.AreEqual(EnemyState.Combat, brain.State);
            return t;
        }

        // ── R1: Alert 엄폐 Hide/Peek, T8은 직접 감지 기준 ──

        // 엄폐 뒤 RDR: 숨은 자세(웅크림)에서는 표적이 안 보이고 일어서면 보인다. 분대원이 0.5초마다 연락해도
        // Alert에서 Peek로 일어서 표적을 보고 반응 시간 뒤 Combat에 들어간다(전에는 60초 내내 Alert).
        [Test]
        public void R1_AlertAtCover_SquadContactKeepsComing_PeeksAndEntersCombat()
        {
            world.AddCover(CoverPos, Vector3.forward);
            var squad = new Squad(5, config);
            EnemyBrain brain = Make(M3TestData.Rdr(), 1, squad);
            EnemyBrain buddy = Make(M3TestData.Rdr(), 2, squad);
            var buddyPos = new Vector3(8f, 0f, 0f);

            brain.OnHeard(Target, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(EnemyMoveMode.Run, brain.Intent.Move, "엄폐로 달려감");
            Assert.AreEqual(CoverPos, brain.Intent.Destination);

            bool peeked = false;
            float t = 0f, lastReport = -1f;
            while (brain.State == EnemyState.Alert && t < 8f)
            {
                t += 0.1f;
                if (t - lastReport >= 0.5f)
                {
                    squad.ReportContact(buddy.AgentId, buddyPos, Target, t);
                    lastReport = t;
                }
                bool standing = brain.Intent.Posture == EnemyPosture.Stand && brain.Intent.Move == EnemyMoveMode.Hold;
                peeked |= standing && t > 0.2f;
                brain.Tick(standing && t > 0.2f ? AiTest.Seen(t, CoverPos, Target) : AiTest.Unseen(t, CoverPos), 0.1f);
            }
            Assert.IsTrue(peeked, "Alert에서도 엄폐 도착 후 Peek(일어섬)");
            Assert.AreEqual(EnemyState.Combat, brain.State, $"{t:0.0}초 안에 Combat (T7)");
            Assert.Less(t, 4f, "hideSec 최대 2.5 + 반응 0.4 안쪽");
        }

        [Test]
        public void R1_AlertAtCover_HideCrouchesThenPeekStands()
        {
            world.AddCover(CoverPos, Vector3.forward);
            EnemyBrain brain = Make(M3TestData.Rdr());
            brain.OnHeard(Target, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0f, Self), 0.1f);
            brain.Tick(AiTest.Unseen(0.1f, CoverPos), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(EnemyPosture.Crouch, brain.Intent.Posture, "Low 엄폐 Hide = 웅크림");
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move);
            brain.Tick(AiTest.Unseen(2.7f, CoverPos), 2.6f);   // hideSec 최대 2.5
            Assert.AreEqual(EnemyPosture.Stand, brain.Intent.Posture, "Peek = 일어섬");
            Assert.IsTrue(brain.Intent.LookAtPoint);
            Assert.AreEqual(Target.x, brain.Intent.LookPoint.x, 1e-4f, "LKP 쪽을 본다");
            Assert.AreEqual(Target.z, brain.Intent.LookPoint.z, 1e-4f);
            Assert.IsFalse(brain.Intent.Fire, "Alert에서는 쏘지 않는다");
        }

        [Test]
        public void R1_AlertAtHighCover_PeekMovesToPeekPosition()
        {
            var peek = new Vector3(1.9f, 0f, 5f);
            world.AddCover(CoverPos, Vector3.forward, CoverHeight.High, peek);
            EnemyBrain brain = Make(M3TestData.Rdr());
            brain.OnHeard(Target, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0f, Self), 0.1f);
            brain.Tick(AiTest.Unseen(0.1f, CoverPos), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move, "High Hide = 숨는 자리에서 섬");
            brain.Tick(AiTest.Unseen(2.7f, CoverPos), 2.6f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(EnemyMoveMode.Walk, brain.Intent.Move);
            Assert.AreEqual(peek, brain.Intent.Destination, "PeekPosition으로 나가 LKP 쪽을 본다");
        }

        // 분대 연락만 계속 오면 LKP는 따라가지만 T8 타이머는 연장되지 않는다.
        [Test]
        public void R1_T8_SquadContactAlone_DoesNotExtendAlert()
        {
            var squad = new Squad(1, config);
            EnemyBrain brain = Make(M3TestData.Scv(), 1, squad);
            EnemyBrain buddy = Make(M3TestData.Scv(), 2, squad);
            brain.OnHeard(Target, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);

            var contact = new Vector3(5f, 0f, 25f);
            float t = 0f;
            for (int i = 0; i < 23; i++)   // 11.5초까지 0.5초마다
            {
                t += 0.5f;
                squad.ReportContact(buddy.AgentId, Vector3.zero, contact + Vector3.right * i, t);
                brain.Tick(AiTest.Unseen(t, Self), 0.5f);
                Assert.AreEqual(EnemyState.Alert, brain.State, $"{t}초: 12초 전");
            }
            Assert.AreEqual(contact + Vector3.right * 22f, brain.LastKnownPosition, "분대 연락은 LKP를 갱신");

            squad.ReportContact(buddy.AgentId, Vector3.zero, contact, 12.1f);
            brain.Tick(AiTest.Unseen(12.1f, Self), 0.6f);
            Assert.AreEqual(EnemyState.Search, brain.State, "분대 연락만으로는 T8이 연장되지 않는다");
        }

        // 분대 연락으로 Alert에 들어왔으면 그 진입 시각부터 잰다 (곧바로 Search로 빠지지 않음).
        [Test]
        public void R1_T8_AlertBySquadContact_TimerStartsAtEntry()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            brain.Tick(AiTest.Unseen(20f, Self), 0.1f);
            brain.OnSquadContact(Target, 20f);
            brain.Tick(AiTest.Unseen(20.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State, "T4");
            brain.Tick(AiTest.Unseen(31.9f, Self), 11.8f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            brain.Tick(AiTest.Unseen(32.2f, Self), 0.3f);
            Assert.AreEqual(EnemyState.Search, brain.State);
        }

        // 직접 들은 총성은 T8 타이머를 연장한다 (분대 연락과 구분).
        [Test]
        public void R1_T8_DirectGunshotInAlert_ExtendsTimer()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            brain.OnHeard(Target, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0f, Self), 0.1f);
            brain.OnHeard(Target, NoiseKind.Gunshot, 10f);
            brain.Tick(AiTest.Unseen(10f, Self), 10f);
            brain.Tick(AiTest.Unseen(13f, Self), 3f);
            Assert.AreEqual(EnemyState.Alert, brain.State, "총성(직접 감지) 뒤 12초 전");
            brain.Tick(AiTest.Unseen(22.1f, Self), 9.1f);
            Assert.AreEqual(EnemyState.Search, brain.State);
        }

        // R1 보충: 엄폐는 시야·사거리 안에서만 고른다 (M3_AIPerf Q2R2: 65.8m 엄폐 → 내다봐도 시야 60m 밖)
        [Test]
        public void R1_CoverSelector_SkipsSpotsBeyondMaxThreatDistance()
        {
            var index = new CoverIndex(4);
            int far = index.Add(new Vector3(0f, 0f, -20f), Vector3.forward, CoverHeight.Low, Vector3.zero);    // 위협까지 50
            int near = index.Add(new Vector3(0f, 0f, 5f), Vector3.forward, CoverHeight.Low, Vector3.zero);     // 위협까지 25
            var q = new CoverQuery
            {
                AgentId = 1, Self = new Vector3(0f, 0f, -18f), ThreatEye = new Vector3(0f, 1.6f, 30f),
                PreferredRangeM = new Vector2(15f, 40f), MaxThreatDistanceM = 45f,
            };
            Assert.IsTrue(CoverSelector.TrySelect(index, q, config, null, null, null, new int[4], new float[4], out CoverSpot spot));
            Assert.AreEqual(near, spot.Id, "가까운 쪽(far)은 위협에서 45m 밖이라 제외");
            index.ReleaseAll(1);
            q.MaxThreatDistanceM = 0f;
            Assert.IsTrue(CoverSelector.TrySelect(index, q, config, null, null, null, new int[4], new float[4], out spot));
            Assert.AreEqual(far, spot.Id, "제한 없으면 자기에게 가까운 지점");
        }

        [Test]
        public void R1_RaiderCoverQuery_LimitedToSightAndEngageRange()
        {
            world.AddCover(CoverPos, Vector3.forward);
            EnemyArchetypeSO rdr = M3TestData.Rdr();
            rdr.engageMaxRangeM = 50f;
            EnemyBrain brain = Make(rdr);
            brain.OnHeard(Target, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0f, Self), 0.1f);
            Assert.AreEqual(50f, world.LastCoverQuery.MaxThreatDistanceM, 1e-4f, "min(sightRange 60, engageMaxRangeM 50)");
        }

        // R1 보충: 소리·피격 추정 위치(총구 높이)는 바닥에 붙여 LKP로 쓴다 → ThreatEye = 실제 선 눈 높이
        [Test]
        public void R1_HeardGunshotLkp_SnappedToGround()
        {
            world.NavPointRule = (around, r) => new Vector3(around.x, 0.08f, around.z);
            world.AddCover(CoverPos, Vector3.forward);
            EnemyBrain brain = Make(M3TestData.Rdr());
            brain.OnHeard(new Vector3(0f, 1.48f, 30f), NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0f, Self), 0.1f);
            Assert.AreEqual(new Vector3(0f, 0.08f, 30f), brain.LastKnownPosition);
            Assert.AreEqual(0.08f + config.standEyeHeightM, world.LastCoverQuery.ThreatEye.y, 1e-4f, "위협 눈 = 바닥 + 선 눈 높이");

            world.NavSampleSucceeds = false;   // NavMesh 밖이면 그대로
            brain.OnDamaged(new Vector3(5f, 1.4f, 30f), 1f);
            brain.Tick(AiTest.Unseen(1f, CoverPos), 1f);
            Assert.AreEqual(new Vector3(5f, 1.4f, 30f), brain.LastKnownPosition);
        }

        // ── R2: High 엄폐 Peek → Hide는 숨는 자리로 돌아간다 ──

        [Test]
        public void R2_HighCover_PeekThenHide_WalksBackToCoverPosition()
        {
            var peek = new Vector3(1.9f, 0f, 5f);
            world.AddCover(CoverPos, Vector3.forward, CoverHeight.High, peek);
            EnemyBrain brain = Make(M3TestData.Rdr());
            float t = ToCombat(brain, 0f, Self, Target);
            brain.Tick(AiTest.Unseen(t + 0.1f, CoverPos), 0.1f);   // 도착 → Hide
            brain.Tick(AiTest.Unseen(t + 2.7f, CoverPos), 2.6f);   // Peek
            Assert.AreEqual(peek, brain.Intent.Destination);

            brain.Tick(AiTest.Unseen(t + 5.8f, peek), 3.1f);       // peekSec 최대 3 → Hide
            Assert.AreEqual(EnemyState.Combat, brain.State, "우회 시도 전 T9 유예");
            Assert.AreEqual(EnemyMoveMode.Walk, brain.Intent.Move, "숨는 자리로 걸어 돌아감");
            Assert.AreEqual(CoverPos, brain.Intent.Destination, "Hide 위치 = cover.Position");
            Assert.AreEqual(EnemyPosture.Stand, brain.Intent.Posture);
            Assert.IsFalse(brain.WantsHiddenCheck, "돌아오는 중에는 숨음 판정을 하지 않는다");

            brain.Tick(AiTest.Unseen(t + 5.9f, CoverPos), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move, "도착하면 멈춤");
            Assert.IsTrue(brain.WantsHiddenCheck);
        }

        // ── R3: 엄폐 질의 Deferred → 제자리에서 위협 쪽, 약탈자 이동 없음 ──

        [Test]
        public void R3_CoverDeferred_HoldsAndLooks_NoFallbackMove()
        {
            world.AddCover(CoverPos, Vector3.forward);
            world.ForcedCoverResult = CoverResult.Deferred;
            EnemyBrain brain = Make(M3TestData.Rdr());
            brain.OnHeard(Target, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move, "Alert Deferred: LKP로 달리지 않는다");
            Assert.IsFalse(brain.Intent.HasDestination);
            Assert.IsTrue(brain.Intent.LookAtPoint);
            Assert.AreEqual(Target.z, brain.Intent.LookPoint.z, 1e-4f);

            brain.Tick(AiTest.Seen(1f, Self, Target), 0.9f);
            brain.Tick(AiTest.Seen(1.5f, Self, Target), 0.5f);
            Assert.AreEqual(EnemyState.Combat, brain.State);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move, "Combat Deferred: 제자리");
            Assert.IsTrue(brain.Intent.Fire, "보이면 서서 사격");

            int calls = world.TryFindCoverCalls;
            world.ForcedCoverResult = null;
            brain.Tick(AiTest.Seen(1.6f, Self, Target), 0.1f);
            Assert.Greater(world.TryFindCoverCalls, calls, "다음 틱에 다시 질의");
            Assert.AreEqual(EnemyMoveMode.Run, brain.Intent.Move);
            Assert.AreEqual(CoverPos, brain.Intent.Destination);
        }

        [Test]
        public void R3_CoverNone_StillFallsBackToScavenger()
        {
            world.ForcedCoverResult = CoverResult.None;
            EnemyBrain brain = Make(M3TestData.Rdr());
            brain.OnHeard(new Vector3(20f, 0f, 20f), NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0f, Self), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Run, brain.Intent.Move, "None: 약탈자처럼 LKP로");
            Assert.AreEqual(new Vector3(20f, 0f, 20f), brain.Intent.Destination);
        }

        // ── R4: 무효화한 지점은 잠시 후보에서 뺀다 ──

        [Test]
        public void R4_ExposedCover_NotReselectedWhileExcluded()
        {
            int near = world.AddCover(CoverPos, Vector3.forward);
            int other = world.AddCover(new Vector3(6f, 0f, 5f), Vector3.forward);
            EnemyBrain brain = Make(M3TestData.Rdr());
            float t = ToCombat(brain, 0f, Self, Target);
            brain.Tick(AiTest.Unseen(t + 0.1f, CoverPos), 0.1f);   // Hide (숨는 자리)
            Assert.AreEqual(brain.AgentId, world.OccupantOf(near));

            brain.Tick(AiTest.Unseen(t + 0.2f, CoverPos, hidden: false), 0.1f);   // 표적에게 보임
            Assert.IsTrue(world.LastCoverQuery.HasExcludedCover);
            Assert.AreEqual(near, world.LastCoverQuery.ExcludedCoverId);
            Assert.AreEqual(brain.AgentId, world.OccupantOf(other), "가장 가까운 무효 지점 대신 다른 지점");
            Assert.AreEqual(-1, world.OccupantOf(near));
        }

        [Test]
        public void R4_ExcludeExpires_AfterExcludeSec()
        {
            int only = world.AddCover(CoverPos, Vector3.forward);
            EnemyBrain brain = Make(M3TestData.Rdr());
            float t = ToCombat(brain, 0f, Self, Target);
            brain.Tick(AiTest.Unseen(t + 0.1f, CoverPos), 0.1f);
            brain.Tick(AiTest.Unseen(t + 0.2f, CoverPos, hidden: false), 0.1f);
            Assert.AreEqual(-1, world.OccupantOf(only), "제외 중: 다른 후보 없음 → None");

            float later = t + 0.2f + config.coverInvalidatedExcludeSec + config.coverRetrySec + 0.1f;
            brain.Tick(AiTest.Seen(later, Self, Target), later - t - 0.2f);
            Assert.IsFalse(world.LastCoverQuery.HasExcludedCover, "제외 시간이 지남");
            Assert.AreEqual(brain.AgentId, world.OccupantOf(only));
        }

        [Test]
        public void R4_CoverSelector_SkipsExcludedId()
        {
            var index = new CoverIndex(4);
            int a = index.Add(new Vector3(0f, 0f, 5f), Vector3.forward, CoverHeight.Low, Vector3.zero);
            int b = index.Add(new Vector3(3f, 0f, 5f), Vector3.forward, CoverHeight.Low, Vector3.zero);
            var q = new CoverQuery
            {
                AgentId = 1, Self = Vector3.zero, ThreatEye = new Vector3(0f, 1.6f, 30f),
                PreferredRangeM = new Vector2(15f, 40f), HasExcludedCover = true, ExcludedCoverId = a,
            };
            Assert.IsTrue(CoverSelector.TrySelect(index, q, config, null, null, null, new int[4], new float[4], out CoverSpot spot));
            Assert.AreEqual(b, spot.Id);
            q.ExcludedCoverId = b;
            index.ReleaseAll(1);
            Assert.IsTrue(CoverSelector.TrySelect(index, q, config, null, null, null, new int[4], new float[4], out spot));
            Assert.AreEqual(a, spot.Id);
        }

        // ── R5: 살아 있는 적이 빠질 때(파괴·비활성화) 정리 ──

        [Test]
        public void R5_Detach_ReleasesCoverAndFlank_LeavesSquad_LeaderSucceeds()
        {
            int cover = world.AddCover(CoverPos, Vector3.forward);
            var squad = new Squad(3, config);
            EnemyBrain leader = Make(M3TestData.Rdr(), 1, squad);
            EnemyBrain other = Make(M3TestData.Rdr(), 2, squad);
            float t = ToCombat(leader, 0f, Self, Target);
            Assert.AreEqual(leader.AgentId, world.OccupantOf(cover));
            Assert.IsTrue(squad.TryClaimFlank(leader.AgentId, t));
            Assert.AreEqual(leader.AgentId, squad.LeaderId);

            leader.Detach();

            Assert.IsTrue(leader.IsDetached);
            Assert.AreEqual(-1, world.OccupantOf(cover), "엄폐 예약 해제");
            Assert.AreEqual(-1, squad.FlankHolder, "우회권 해제");
            Assert.AreEqual(1, squad.Count, "분대 탈퇴");
            Assert.AreEqual(other.AgentId, squad.LeaderId, "리더 승계");
            Assert.AreEqual(EnemyState.Combat, leader.State, "사망이 아니다(상태 그대로)");

            // 이후 입력은 무시
            leader.OnSquadContact(Target, t + 1f);
            leader.Tick(AiTest.Seen(t + 1f, Self, Target), 0.1f);
            Assert.AreEqual(-1, world.OccupantOf(cover));
            Assert.IsFalse(leader.Intent.Fire);
        }

        // ── R6: 경로 없는 경계 분대의 분대원은 Idle에서 제자리 ──

        [Test]
        public void R6_IdleFollower_LeaderWithoutRoute_HoldsPosition()
        {
            var squad = new Squad(7, config);
            EnemyBrain leader = Make(M3TestData.Rdr(), 1, squad);
            EnemyBrain follower = Make(M3TestData.Rdr(), 2, squad);
            var leaderPos = new Vector3(3f, 0f, 35f);
            var followerPos = new Vector3(-3f, 0f, 35f);
            for (int i = 1; i <= 20; i++)
            {
                leader.Tick(AiTest.NoTarget(i * 0.2f, leaderPos), 0.2f);
                follower.Tick(AiTest.NoTarget(i * 0.2f, followerPos), 0.2f);
                Assert.AreEqual(EnemyState.Idle, follower.State);
                Assert.AreEqual(EnemyMoveMode.Hold, follower.Intent.Move, "대형 자리로 가지 않는다");
                Assert.IsFalse(follower.Intent.HasDestination);
            }
        }

        [Test]
        public void R6_IdleFollower_LeaderWithRoute_StillFollowsFormation()
        {
            var route = new List<Vector3> { new Vector3(0f, 0f, 10f), new Vector3(10f, 0f, 10f) };
            var squad = new Squad(7, config);
            EnemyBrain leader = Make(M3TestData.Scv(), 1, squad, route);
            EnemyBrain follower = Make(M3TestData.Scv(), 2, squad, route);
            leader.Tick(AiTest.NoTarget(0.2f, Vector3.zero), 0.2f);
            follower.Tick(AiTest.NoTarget(0.2f, new Vector3(20f, 0f, 0f)), 0.2f);
            Assert.AreEqual(EnemyState.Idle, follower.State);
            Assert.AreNotEqual(EnemyMoveMode.Hold, follower.Intent.Move, "순찰 분대원은 대형 자리로");
        }

        // ── R7: 무장 검사, 적별 예외 격리 ──

        [Test]
        public void R7_IsValidLoadout_RejectsMissingOrMismatchedAmmo()
        {
            EnemyArchetypeSO scv = M3TestData.Scv();
            EnemyLoadout ok = scv.loadouts[0];
            Assert.IsTrue(EnemyWeapon.IsValidLoadout(ok, out string problem), problem);

            EnemyLoadout noAmmo = ok;
            noAmmo.ammo = null;
            Assert.IsFalse(EnemyWeapon.IsValidLoadout(noAmmo, out problem));
            StringAssert.Contains("ammo", problem);
            Assert.Throws<ArgumentException>(() =>
                new EnemyWeapon(noAmmo, 1, scv, new FakeProjectileService(), new System.Random(1)), "ammo null 아키타입 거부");

            EnemyLoadout wrongAmmo = ok;
            wrongAmmo.ammo = M3TestData.Ammo556();
            Assert.IsFalse(EnemyWeapon.IsValidLoadout(wrongAmmo, out _), "탄 종류 불일치");

            EnemyLoadout wrongMag = ok;
            wrongMag.magazine = TestData.Magazine30();
            Assert.IsFalse(EnemyWeapon.IsValidLoadout(wrongMag, out _), "탄창 탄 종류 불일치");

            EnemyLoadout noWeapon = ok;
            noWeapon.weapon = null;
            Assert.IsFalse(EnemyWeapon.IsValidLoadout(noWeapon, out _));
        }

        private sealed class FakeTickable : IAiFrameTickable
        {
            public readonly List<FakeTickable> Owner;
            public bool Throws;
            public int Ticks;
            public int Faults;

            public FakeTickable(List<FakeTickable> owner) => Owner = owner;

            public void FrameTick(float now, float dt)
            {
                Ticks++;
                if (Throws)
                    throw new InvalidOperationException("테스트 예외");
            }

            // EnemyAgent처럼 스스로 목록에서 빠진다
            public void OnTickFaulted()
            {
                Faults++;
                Owner.Remove(this);
            }
        }

        [Test]
        public void R7_FrameTickAll_OneAgentThrows_OthersStillTick()
        {
            var list = new List<FakeTickable>();
            for (int i = 0; i < 5; i++)
                list.Add(new FakeTickable(list));
            FakeTickable bad = list[2];
            bad.Throws = true;
            FakeTickable[] all = list.ToArray();

            LogAssert.Expect(LogType.Exception, new Regex("테스트 예외"));
            AiTickGuard.FrameTickAll(list, 1f, 0.016f);

            foreach (FakeTickable a in all)
                Assert.AreEqual(1, a.Ticks, "예외가 난 적 앞뒤 모두 틱");
            Assert.AreEqual(1, bad.Faults);
            Assert.AreEqual(4, list.Count, "예외 난 적은 빠짐");

            AiTickGuard.FrameTickAll(list, 1.016f, 0.016f);
            Assert.AreEqual(1, bad.Ticks, "다음 프레임부터 제외");
            Assert.AreEqual(2, all[0].Ticks);
            Assert.AreEqual(2, all[4].Ticks);
        }

        [Test]
        public void R7_FrameTickAll_NoException_DoesNotAllocate()
        {
            var list = new List<FakeTickable>();
            for (int i = 0; i < 20; i++)
                list.Add(new FakeTickable(list));
            AiTest.AssertNoAlloc(() =>
            {
                for (int i = 0; i < 100; i++)
                    AiTickGuard.FrameTickAll(list, i, 0.016f);
            });
        }

        // ── R9: 발사 서비스가 없으면 쏘지 않는다 ──

        [Test]
        public void R9_EnemyWeapon_ProjectilesCleared_DoesNotFireOrConsume()
        {
            EnemyArchetypeSO scv = M3TestData.Scv();
            var projectiles = new FakeProjectileService();
            var weapon = new EnemyWeapon(scv.loadouts[1], 1, scv, projectiles, new System.Random(3));   // SMG 자동
            int rounds = weapon.Item.LoadedMagazine.Rounds;

            weapon.Projectiles = null;   // 서비스 해제
            for (int i = 0; i < 30; i++)
                weapon.Tick(i * 0.05f, 0.05f, true, Vector3.up, new Vector3(0f, 1f, 10f), 0f, null);
            Assert.AreEqual(0, projectiles.Count);
            Assert.AreEqual(rounds, weapon.Item.LoadedMagazine.Rounds, "탄을 쓰지 않는다");

            weapon.Projectiles = projectiles;   // 다시 등록
            for (int i = 30; i < 40; i++)
                weapon.Tick(i * 0.05f, 0.05f, true, Vector3.up, new Vector3(0f, 1f, 10f), 0f, null);
            Assert.Greater(projectiles.Count, 0);
        }

        // ── R12: 분대 리더는 순서 키로 결정, 승계 시 가장 가까운 웨이포인트부터 ──

        [Test]
        public void R12_SquadOrder_LowestOrderIsLeader_RegardlessOfJoinOrder()
        {
            var squad = new Squad(2, config);
            EnemyBrain c = Make(M3TestData.Scv(), 30, squad, order: 7);
            EnemyBrain a = Make(M3TestData.Scv(), 10, squad, order: 3);
            EnemyBrain b = Make(M3TestData.Scv(), 20, squad, order: 5);
            Assert.AreEqual(a.AgentId, squad.LeaderId, "Start(등록) 순서가 아니라 순서 키");
            Assert.AreEqual(1, squad.IndexOf(b.AgentId));
            Assert.AreEqual(2, squad.IndexOf(c.AgentId));
            a.OnDied();
            Assert.AreEqual(b.AgentId, squad.LeaderId);
        }

        [Test]
        public void R12_LeaderSuccession_PatrolRestartsFromNearestWaypoint()
        {
            var route = new List<Vector3> { new Vector3(0f, 0f, 10f), new Vector3(10f, 0f, 10f), new Vector3(10f, 0f, 0f) };
            var squad = new Squad(2, config);
            EnemyBrain leader = Make(M3TestData.Scv(), 1, squad, route);
            EnemyBrain follower = Make(M3TestData.Scv(), 2, squad, route);

            // 둘 다 Patrol. 분대원은 리더를 따른다(patrolIndex는 Patrol 진입 때 값 = 원점에서 가까운 0번)
            leader.Tick(AiTest.NoTarget(3f, Vector3.zero), 3f);
            follower.Tick(AiTest.NoTarget(3f, Vector3.zero), 3f);
            Assert.AreEqual(EnemyState.Patrol, follower.State);
            follower.Tick(AiTest.NoTarget(3.2f, Vector3.zero), 0.2f);

            // 따라가다 3번 웨이포인트 근처에서 리더가 빠짐 → 승계
            var nearThird = new Vector3(9.5f, 0f, 0.5f);
            leader.Detach();
            Assert.AreEqual(follower.AgentId, squad.LeaderId);
            follower.Tick(AiTest.NoTarget(3.4f, nearThird), 0.2f);
            Assert.AreEqual(EnemyMoveMode.Hold, follower.Intent.Move, "가장 가까운 웨이포인트(이미 도착) = 대기");
            follower.Tick(AiTest.NoTarget(8f, nearThird), 4.6f);
            Assert.AreEqual(route[0], follower.Intent.Destination, "3번 다음 = 0번 (낡은 0번부터가 아님)");
        }
    }
}
