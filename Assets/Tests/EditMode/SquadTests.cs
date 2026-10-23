using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 분대 (§7 AI-SCV 2~3인 순찰, M3 설계 §1.6.3): 리더 승계, 대형, 정보 공유, 측면 우회권
    public class SquadTests
    {
        private AiConfigSO config;
        private FakeEnemyWorld world;

        [SetUp]
        public void SetUp()
        {
            config = M3TestData.AiConfig();
            world = new FakeEnemyWorld();
        }

        private EnemyBrain Member(Squad squad, int id) =>
            new EnemyBrain(id, M3TestData.Scv(), config, world, null, squad, new System.Random(id));

        [Test]
        public void Leader_IsFirstJoined_SucceededOnLeave()
        {
            var squad = new Squad(1, config);
            Assert.AreEqual(-1, squad.LeaderId);
            EnemyBrain a = Member(squad, 10);
            EnemyBrain b = Member(squad, 11);
            EnemyBrain c = Member(squad, 12);
            Assert.AreEqual(3, squad.Count);
            Assert.AreEqual(10, squad.LeaderId);
            Assert.AreEqual(0, squad.IndexOf(10));
            Assert.AreEqual(2, squad.IndexOf(12));

            a.OnDied();
            Assert.AreEqual(11, squad.LeaderId, "리더 사망 → 다음 생존자");
            Assert.AreEqual(1, squad.IndexOf(12));
            squad.Leave(b);
            Assert.AreEqual(12, squad.LeaderId);
            squad.Leave(c);
            Assert.AreEqual(-1, squad.LeaderId);
            Assert.AreEqual(-1, squad.IndexOf(12));
        }

        [Test]
        public void Join_IsIdempotent()
        {
            var squad = new Squad(1, config);
            EnemyBrain a = Member(squad, 1);
            squad.Join(a);
            Assert.AreEqual(1, squad.Count);
        }

        [Test]
        public void FormationSlot_BehindLeader_AlternatingSides()
        {
            var squad = new Squad(1, config);
            Vector3 leader = new Vector3(5f, 0f, 5f);
            Assert.AreEqual(leader, squad.FormationSlot(0, leader, Vector3.forward));
            AssertVec(leader + new Vector3(-2f, 0f, -2f), squad.FormationSlot(1, leader, Vector3.forward));
            AssertVec(leader + new Vector3(2f, 0f, -2f), squad.FormationSlot(2, leader, Vector3.forward));
            AssertVec(leader + new Vector3(-2f, 0f, -4f), squad.FormationSlot(3, leader, Vector3.forward));

            // 리더가 +x를 보면 뒤 = -x, 왼쪽 = +z
            AssertVec(leader + new Vector3(-2f, 0f, 2f), squad.FormationSlot(1, leader, Vector3.right));
        }

        [Test]
        public void Follower_WalksToFormationSlot()
        {
            // 순찰 분대 (경로 없는 경계 분대의 분대원은 제자리를 지킨다 — AiReviewFixTests.R6)
            var squad = new Squad(1, config);
            var route = new System.Collections.Generic.List<Vector3> { new Vector3(0f, 0f, 20f), new Vector3(20f, 0f, 20f) };
            EnemyBrain leader = new EnemyBrain(1, M3TestData.Scv(), config, world, route, squad, new System.Random(1));
            EnemyBrain follower = new EnemyBrain(2, M3TestData.Scv(), config, world, route, squad, new System.Random(2));
            leader.Tick(AiTest.NoTarget(0f, new Vector3(0f, 0f, 20f)), 0.1f);
            follower.Tick(AiTest.NoTarget(0f, Vector3.zero), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Run, follower.Intent.Move, "6m보다 멀면 달려서 따라감");
            AssertVec(new Vector3(-2f, 0f, 18f), follower.Intent.Destination);

            follower.Tick(AiTest.NoTarget(0.1f, new Vector3(-2f, 0f, 18f)), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Hold, follower.Intent.Move, "리더가 멈추면 같이 멈춤");
        }

        [Test]
        public void ReportContact_SharesWithinRange()
        {
            var squad = new Squad(1, config);
            EnemyBrain reporter = Member(squad, 1);
            EnemyBrain near = Member(squad, 2);
            EnemyBrain far = Member(squad, 3);
            reporter.Tick(AiTest.NoTarget(0f, Vector3.zero), 0.1f);
            near.Tick(AiTest.NoTarget(0f, new Vector3(49.9f, 0f, 0f)), 0.1f);
            far.Tick(AiTest.NoTarget(0f, new Vector3(0f, 0f, 50.1f)), 0.1f);

            var contact = new Vector3(5f, 0f, 30f);
            squad.ReportContact(reporter.AgentId, Vector3.zero, contact, 1f);
            // 보고자를 먼저: near가 Alert에 들어가며 다시 보고하기 전
            reporter.Tick(AiTest.Unseen(1.1f, Vector3.zero), 0.1f);
            Assert.AreNotEqual(EnemyState.Alert, reporter.State, "보고자 자신은 받지 않는다");

            near.Tick(AiTest.Unseen(1.1f, new Vector3(49.9f, 0f, 0f)), 0.1f);
            far.Tick(AiTest.Unseen(1.1f, new Vector3(0f, 0f, 50.1f)), 0.1f);
            Assert.AreEqual(EnemyState.Alert, near.State, "50m 안: 연락 → Alert");
            Assert.AreEqual(contact, near.LastKnownPosition);
            Assert.AreNotEqual(EnemyState.Alert, far.State, "50m 밖: 연락 못 받음 (near와도 70m)");
        }

        [Test]
        public void ReportContact_RateLimitedPerReporter()
        {
            var squad = new Squad(1, config);
            EnemyBrain reporter = Member(squad, 1);
            EnemyBrain listener = Member(squad, 2);
            reporter.Tick(AiTest.NoTarget(0f, Vector3.zero), 0.1f);
            listener.Tick(AiTest.NoTarget(0f, new Vector3(3f, 0f, 0f)), 0.1f);

            var first = new Vector3(0f, 0f, 20f);
            squad.ReportContact(1, Vector3.zero, first, 1f);
            listener.Tick(AiTest.Unseen(1.05f, new Vector3(3f, 0f, 0f)), 0.05f);
            Assert.AreEqual(EnemyState.Alert, listener.State);
            Assert.AreEqual(first, listener.LastKnownPosition);

            squad.ReportContact(1, Vector3.zero, new Vector3(0f, 0f, 25f), 1.3f);   // 0.5초 안 → 무시
            listener.Tick(AiTest.Unseen(1.35f, new Vector3(3f, 0f, 0f)), 0.3f);
            Assert.AreEqual(first, listener.LastKnownPosition);

            var third = new Vector3(0f, 0f, 28f);
            squad.ReportContact(1, Vector3.zero, third, 1.6f);
            listener.Tick(AiTest.Unseen(1.65f, new Vector3(3f, 0f, 0f)), 0.3f);
            Assert.AreEqual(third, listener.LastKnownPosition);
        }

        [Test]
        public void EnteringAlert_ReportsToSquad()
        {
            var squad = new Squad(1, config);
            EnemyBrain a = Member(squad, 1);
            EnemyBrain b = Member(squad, 2);
            a.Tick(AiTest.NoTarget(0f, Vector3.zero), 0.1f);
            b.Tick(AiTest.NoTarget(0f, new Vector3(4f, 0f, 0f)), 0.1f);

            a.OnHeard(new Vector3(0f, 0f, 40f), NoiseKind.Gunshot, 1f);
            a.Tick(AiTest.Unseen(1.1f, Vector3.zero), 0.1f);
            Assert.AreEqual(EnemyState.Alert, a.State);
            b.Tick(AiTest.Unseen(1.2f, new Vector3(4f, 0f, 0f)), 0.1f);
            Assert.AreEqual(EnemyState.Alert, b.State, "분대 연락");
            Assert.AreEqual(new Vector3(0f, 0f, 40f), b.LastKnownPosition);
        }

        [Test]
        public void Flank_OneHolderAtATime_WithCooldown()
        {
            var squad = new Squad(1, config);
            Assert.AreEqual(-1, squad.FlankHolder);
            Assert.IsTrue(squad.TryClaimFlank(1, 10f));
            Assert.IsTrue(squad.TryClaimFlank(1, 10.5f), "쥔 사람은 다시 true");
            Assert.IsFalse(squad.TryClaimFlank(2, 10.5f));
            Assert.AreEqual(1, squad.FlankHolder);

            squad.ReleaseFlank(2, 11f);   // 남의 것은 못 푼다
            Assert.AreEqual(1, squad.FlankHolder);
            squad.ReleaseFlank(1, 12f);
            Assert.AreEqual(-1, squad.FlankHolder);
            Assert.IsFalse(squad.TryClaimFlank(2, 19.9f), "반납 후 8초 쿨다운");
            Assert.IsTrue(squad.TryClaimFlank(2, 20.1f));
        }

        private static void AssertVec(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-4f);
            Assert.AreEqual(expected.y, actual.y, 1e-4f);
            Assert.AreEqual(expected.z, actual.z, 1e-4f);
        }

        // M7 7B 리뷰 10 (Q26): 분대 경계 울음권 — 쿨다운 안에서는 한 명만
        [Test]
        public void AlertVocal_OnePerSquadPerCooldown()
        {
            var squad = new Squad(10, config);
            Assert.IsTrue(squad.TryClaimAlertVocal(1f, 4f), "첫 대원");
            Assert.IsFalse(squad.TryClaimAlertVocal(1f, 4f), "같은 순간 둘째");
            Assert.IsFalse(squad.TryClaimAlertVocal(4.9f, 4f), "쿨다운 안");
            Assert.IsTrue(squad.TryClaimAlertVocal(5f, 4f), "쿨다운 뒤");
            var other = new Squad(11, config);
            Assert.IsTrue(other.TryClaimAlertVocal(5f, 4f), "다른 분대는 따로");
        }
    }
}
