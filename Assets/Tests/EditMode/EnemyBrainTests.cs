using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // §7 상태 머신 Idle → Patrol → Suspicious → Alert → Combat → Search → Patrol (+ Dead).
    // 전이 표 T1~T13 (M3 설계 §1.6.1), 유형별 전술 (§1.6.2). 가짜 감각 시퀀스로 검증한다.
    public class EnemyBrainTests
    {
        private AiConfigSO config;
        private FakeEnemyWorld world;
        private List<Vector3> route;

        private static readonly Vector3 Self = Vector3.zero;
        private static readonly Vector3 TargetFar = new Vector3(0f, 0f, 30f);

        [SetUp]
        public void SetUp()
        {
            config = M3TestData.AiConfig();
            world = new FakeEnemyWorld();
            route = new List<Vector3> { new Vector3(0f, 0f, 10f), new Vector3(10f, 0f, 10f), new Vector3(10f, 0f, 0f) };
        }

        private EnemyBrain Make(EnemyArchetypeSO archetype, IReadOnlyList<Vector3> patrol = null, Squad squad = null,
                                IEnemyWorld w = null, int id = 1) =>
            new EnemyBrain(id, archetype, config, w ?? world, patrol, squad, new System.Random(7));

        // 총성으로 Alert에 넣고(보이는 상태) 반응 시간 뒤 Combat까지
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

        // ── T1 ──
        [Test]
        public void T1_IdleWithRoute_PatrolAfterIdleDelay()
        {
            EnemyBrain brain = Make(M3TestData.Scv(), route);
            float t = 0f;
            for (int i = 0; i < 9; i++)
            {
                t += 0.2f;
                brain.Tick(AiTest.NoTarget(t, Self), 0.2f);
            }
            Assert.AreEqual(EnemyState.Idle, brain.State, "1.8초: 아직 대기");
            brain.Tick(AiTest.NoTarget(t + 0.3f, Self), 0.3f);
            Assert.AreEqual(EnemyState.Patrol, brain.State);
        }

        [Test]
        public void T1_IdleWithoutRoute_StaysIdle()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            for (int i = 1; i <= 50; i++)
                brain.Tick(AiTest.NoTarget(i * 0.2f, Self), 0.2f);
            Assert.AreEqual(EnemyState.Idle, brain.State);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move);
        }

        [Test]
        public void Patrol_WalksToWaypoints_WaitsThenNext()
        {
            EnemyBrain brain = Make(M3TestData.Scv(), route);
            brain.Tick(AiTest.NoTarget(3f, Self), 3f);
            Assert.AreEqual(EnemyState.Patrol, brain.State);
            brain.Tick(AiTest.NoTarget(3.2f, Self), 0.2f);
            Assert.AreEqual(EnemyMoveMode.Walk, brain.Intent.Move);
            Assert.AreEqual(route[0], brain.Intent.Destination, "가장 가까운 웨이포인트부터");

            // 도착 → 대기 (patrolWaitSec 2~4초)
            brain.Tick(AiTest.NoTarget(4f, route[0]), 0.8f);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move);
            brain.Tick(AiTest.NoTarget(5.5f, route[0]), 1.5f);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move, "최소 대기 2초 안");
            brain.Tick(AiTest.NoTarget(8.5f, route[0]), 3f);
            Assert.AreEqual(EnemyMoveMode.Walk, brain.Intent.Move);
            Assert.AreEqual(route[1], brain.Intent.Destination);
        }

        // ── T2 ──
        [Test]
        public void T2_PatrolHearsFootstep_Suspicious()
        {
            EnemyBrain brain = Make(M3TestData.Scv(), route);
            brain.Tick(AiTest.NoTarget(3f, Self), 3f);
            Assert.AreEqual(EnemyState.Patrol, brain.State);

            var noise = new Vector3(5f, 0f, -5f);
            brain.OnHeard(noise, NoiseKind.Footstep, 3.1f);
            Assert.AreEqual(EnemyState.Patrol, brain.State, "전이는 다음 Tick에서");
            brain.Tick(AiTest.NoTarget(3.2f, Self), 0.2f);
            Assert.AreEqual(EnemyState.Suspicious, brain.State);
            Assert.AreEqual(noise, brain.StimulusPosition);
        }

        [Test]
        public void T2_IdlePartialSight_SuspiciousBeforeAlert()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            var target = new Vector3(0f, 0f, 50f);
            float t = 0f;
            while (brain.State == EnemyState.Idle && t < 5f)
            {
                t += 0.1f;
                brain.Tick(AiTest.Seen(t, Self, target), 0.1f);
            }
            Assert.AreEqual(EnemyState.Suspicious, brain.State);
            Assert.GreaterOrEqual(brain.Awareness, config.suspiciousAt);
            Assert.Less(brain.Awareness, 1f);
            Assert.AreEqual(target, brain.StimulusPosition);
        }

        // ── T3 ──
        [Test]
        public void T3_SuspiciousGaugeFull_Alert_LkpIsTarget()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            brain.OnHeard(new Vector3(3f, 0f, 3f), NoiseKind.Footstep, 0f);
            brain.Tick(AiTest.NoTarget(0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Suspicious, brain.State);

            var close = new Vector3(0f, 0f, 3f);   // 즉시 인지 거리 안
            brain.Tick(AiTest.Seen(0.2f, Self, close), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.IsTrue(brain.HasLastKnown);
            Assert.AreEqual(close, brain.LastKnownPosition);
        }

        // ── T4 ──
        [Test]
        public void T4_Gunshot_FromIdle_Alert()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            var shot = new Vector3(40f, 0f, 0f);
            brain.OnHeard(shot, NoiseKind.Gunshot, 1f);
            brain.Tick(AiTest.Unseen(1.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(1f, brain.Awareness);
            Assert.AreEqual(shot, brain.LastKnownPosition);
        }

        [Test]
        public void T4_Damaged_FromPatrol_Alert_LkpIsEstimatedSource()
        {
            EnemyBrain brain = Make(M3TestData.Scv(), route);
            brain.Tick(AiTest.NoTarget(3f, Self), 3f);
            Assert.AreEqual(EnemyState.Patrol, brain.State);
            var source = new Vector3(-20f, 1.5f, 5f);
            brain.OnDamaged(source, 3.1f);
            brain.Tick(AiTest.Unseen(3.2f, Self), 0.2f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(source, brain.LastKnownPosition);
        }

        [Test]
        public void T4_SquadContact_FromSuspicious_Alert()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            brain.OnHeard(new Vector3(2f, 0f, 2f), NoiseKind.Footstep, 0f);
            brain.Tick(AiTest.NoTarget(0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Suspicious, brain.State);

            var contact = new Vector3(10f, 0f, 30f);
            brain.OnSquadContact(contact, 0.2f);
            brain.Tick(AiTest.Unseen(0.3f, Self), 0.2f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(contact, brain.LastKnownPosition);
        }

        [Test]
        public void T4_Gunshot_FromSearch_Alert()
        {
            EnemyBrain brain = ToSearch(out float t);
            var shot = new Vector3(-30f, 0f, 0f);
            brain.OnHeard(shot, NoiseKind.Gunshot, t);
            brain.Tick(AiTest.Unseen(t + 0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(shot, brain.LastKnownPosition);
        }

        // Combat → (못 봄 loseSightSec) → Search
        private EnemyBrain ToSearch(out float t)
        {
            EnemyBrain brain = Make(M3TestData.Scv(), route);
            t = ToCombat(brain, 0f, Self, TargetFar);
            t += 5.5f;
            brain.Tick(AiTest.Unseen(t, Self), 5.5f);
            Assert.AreEqual(EnemyState.Search, brain.State);
            return brain;
        }

        // ── T5 ──
        [Test]
        public void T5_SearchSeesTarget_Alert_ThenCombatAfterReaction()
        {
            EnemyBrain brain = ToSearch(out float t);
            brain.Tick(AiTest.Seen(t + 0.1f, Self, TargetFar), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(1f, brain.Awareness);
            brain.Tick(AiTest.Seen(t + 0.4f, Self, TargetFar), 0.3f);
            Assert.AreEqual(EnemyState.Alert, brain.State, "반응 시간 0.6 전");
            brain.Tick(AiTest.Seen(t + 0.8f, Self, TargetFar), 0.4f);
            Assert.AreEqual(EnemyState.Combat, brain.State);
        }

        // ── T6 ──
        [Test]
        public void T6_SearchHearsFootstep_Alert_LkpUpdated()
        {
            EnemyBrain brain = ToSearch(out float t);
            var step = new Vector3(8f, 0f, -8f);
            brain.OnHeard(step, NoiseKind.Footstep, t);
            brain.Tick(AiTest.Unseen(t + 0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(step, brain.LastKnownPosition);
        }

        // ── T7 ──
        [TestCase(true, 0.6f)]
        [TestCase(false, 0.4f)]
        public void T7_AlertReactionTime(bool scavenger, float reaction)
        {
            EnemyArchetypeSO a = scavenger ? M3TestData.Scv() : M3TestData.Rdr();
            Assert.AreEqual(reaction, a.reactionTimeSec, 1e-5f);
            EnemyBrain brain = Make(a);
            brain.OnHeard(TargetFar, NoiseKind.Gunshot, 10f);
            brain.Tick(AiTest.Seen(10f, Self, TargetFar), 0.1f);   // 보인 순간부터 반응 타이머
            Assert.AreEqual(EnemyState.Alert, brain.State);
            brain.Tick(AiTest.Seen(10f + reaction - 0.05f, Self, TargetFar), reaction - 0.05f);
            Assert.AreEqual(EnemyState.Alert, brain.State, "반응 시간 전");
            Assert.IsTrue(brain.Intent.LookAtPoint, "반응 중에도 표적 쪽으로 돈다");
            brain.Tick(AiTest.Seen(10f + reaction + 0.05f, Self, TargetFar), 0.1f);
            Assert.AreEqual(EnemyState.Combat, brain.State);
        }

        [Test]
        public void T7_ReactionResetsWhenSightLost()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            brain.OnHeard(TargetFar, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Seen(0f, Self, TargetFar), 0.1f);
            brain.Tick(AiTest.Unseen(0.3f, Self), 0.3f);
            brain.Tick(AiTest.Seen(0.5f, Self, TargetFar), 0.2f);
            brain.Tick(AiTest.Seen(0.9f, Self, TargetFar), 0.4f);
            Assert.AreEqual(EnemyState.Alert, brain.State, "다시 본 0.5부터 0.4초뿐");
            brain.Tick(AiTest.Seen(1.2f, Self, TargetFar), 0.3f);
            Assert.AreEqual(EnemyState.Combat, brain.State);
        }

        // ── T8 ──
        [Test]
        public void T8_AlertTimeoutWithoutSight_Search()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            brain.OnHeard(TargetFar, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            brain.Tick(AiTest.Unseen(11.5f, Self), 11.5f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            brain.Tick(AiTest.Unseen(12.1f, Self), 0.6f);
            Assert.AreEqual(EnemyState.Search, brain.State);
        }

        [Test]
        public void T8_NoiseInAlert_UpdatesLkpAndTimer()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            brain.OnHeard(TargetFar, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0f, Self), 0.1f);
            var step = new Vector3(4f, 0f, 20f);
            brain.OnHeard(step, NoiseKind.Footstep, 10f);
            brain.Tick(AiTest.Unseen(10f, Self), 10f);
            Assert.AreEqual(EnemyState.Alert, brain.State, "소리를 들으면 상태 유지, LKP만 갱신");
            Assert.AreEqual(step, brain.LastKnownPosition);
            brain.Tick(AiTest.Unseen(13f, Self), 3f);
            Assert.AreEqual(EnemyState.Alert, brain.State, "갱신 후 12초 전");
        }

        // ── T9 ──
        [Test]
        public void T9_CombatLosesSight_SearchAfterLoseSightSec()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            float t = ToCombat(brain, 0f, Self, TargetFar);
            brain.Tick(AiTest.Unseen(t + 4.9f, Self), 4.9f);
            Assert.AreEqual(EnemyState.Combat, brain.State);
            brain.Tick(AiTest.Unseen(t + 5.1f, Self), 0.2f);
            Assert.AreEqual(EnemyState.Search, brain.State);
        }

        // ── T10 ──
        [Test]
        public void T10_SearchDuration_PatrolFromNearestWaypoint()
        {
            EnemyBrain brain = ToSearch(out float t);
            var nearWaypoint3 = new Vector3(9f, 0f, 1f);
            brain.Tick(AiTest.Unseen(t + 24f, nearWaypoint3), 24f);
            Assert.AreEqual(EnemyState.Search, brain.State);
            brain.Tick(AiTest.Unseen(t + 25.5f, nearWaypoint3), 1.5f);
            Assert.AreEqual(EnemyState.Patrol, brain.State);
            Assert.AreEqual(0f, brain.Awareness, "경계를 풀고 돌아온다");
            brain.Tick(AiTest.Unseen(t + 25.6f, new Vector3(9f, 0f, 3f)), 0.1f);
            Assert.AreEqual(route[2], brain.Intent.Destination, "경로상 가장 가까운 웨이포인트부터");
        }

        [Test]
        public void T10_SearchWithoutRoute_Idle()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            float t = ToCombat(brain, 0f, Self, TargetFar);
            brain.Tick(AiTest.Unseen(t + 6f, Self), 6f);
            Assert.AreEqual(EnemyState.Search, brain.State);
            brain.Tick(AiTest.Unseen(t + 32f, Self), 26f);
            Assert.AreEqual(EnemyState.Idle, brain.State);
        }

        [Test]
        public void Search_WalksToNavPointsAroundLkp()
        {
            world.NavPointRule = (around, r) => around + Vector3.up * 0.01f;
            EnemyBrain brain = ToSearch(out float t);
            brain.Tick(AiTest.Unseen(t + 0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Walk, brain.Intent.Move);
            Vector3 d = brain.Intent.Destination - brain.LastKnownPosition;
            d.y = 0f;
            Assert.LessOrEqual(d.magnitude, config.searchRadiusM + 1e-3f);
            Assert.Greater(world.SampleNavCalls, 0);
        }

        // ── T11 ──
        [Test]
        public void T11_SuspiciousCalms_Patrol()
        {
            // RDR: 제자리에서 둘러본다 → 게이지 0 ∧ 5초 → 경로 있으면 Patrol
            EnemyBrain brain = Make(M3TestData.Rdr(), route);
            brain.OnHeard(new Vector3(5f, 0f, 5f), NoiseKind.Footstep, 0f);
            brain.Tick(AiTest.NoTarget(0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Suspicious, brain.State);
            float t = 0.1f;
            for (int i = 0; i < 24; i++)
            {
                t += 0.2f;
                brain.Tick(AiTest.NoTarget(t, Self), 0.2f);
            }
            Assert.AreEqual(EnemyState.Suspicious, brain.State, "4.8초: 아직 둘러봄");
            for (int i = 0; i < 3; i++)
            {
                t += 0.2f;
                brain.Tick(AiTest.NoTarget(t, Self), 0.2f);
            }
            Assert.AreEqual(EnemyState.Patrol, brain.State);
        }

        [Test]
        public void T11_ScavengerLooksOnlyAfterArriving()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            var noise = new Vector3(0f, 0f, 15f);
            brain.OnHeard(noise, NoiseKind.Footstep, 0f);
            brain.Tick(AiTest.NoTarget(0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Suspicious, brain.State);
            brain.Tick(AiTest.NoTarget(8f, new Vector3(0f, 0f, 5f)), 7.9f);   // 아직 가는 중 (Arrived는 다른 목적지)
            Assert.AreEqual(EnemyState.Suspicious, brain.State, "도착 전에는 둘러보는 시간이 쌓이지 않는다");
            brain.Tick(AiTest.NoTarget(9f, noise), 1f);
            brain.Tick(AiTest.NoTarget(13.5f, noise), 4.5f);
            Assert.AreEqual(EnemyState.Suspicious, brain.State);
            brain.Tick(AiTest.NoTarget(14.5f, noise), 1f);
            Assert.AreEqual(EnemyState.Idle, brain.State, "경로 없으면 Idle");
        }

        // ── 유형별 Suspicious ──
        [Test]
        public void Suspicious_Scavenger_WalksToStimulus()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            var noise = new Vector3(6f, 0f, 12f);
            brain.OnHeard(noise, NoiseKind.Footstep, 0f);
            brain.Tick(AiTest.NoTarget(0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Suspicious, brain.State);
            Assert.AreEqual(EnemyMoveMode.Walk, brain.Intent.Move);
            Assert.IsTrue(brain.Intent.HasDestination);
            Assert.AreEqual(noise, brain.Intent.Destination);
        }

        [Test]
        public void Suspicious_Raider_HoldsCrouchedAndLooks()
        {
            EnemyBrain brain = Make(M3TestData.Rdr());
            var noise = new Vector3(6f, 0f, 12f);
            brain.OnHeard(noise, NoiseKind.Footstep, 0f);
            brain.Tick(AiTest.NoTarget(0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Suspicious, brain.State);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move);
            Assert.IsTrue(brain.Intent.LookAtPoint);
            Assert.AreEqual(noise, brain.Intent.LookPoint);
            Assert.AreEqual(EnemyPosture.Crouch, brain.Intent.Posture);
        }

        // ── Alert·Combat 행동 ──
        [Test]
        public void Alert_Scavenger_RunsToLastKnown()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            var shot = new Vector3(20f, 0f, 20f);
            brain.OnHeard(shot, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(EnemyMoveMode.Run, brain.Intent.Move, "소리 추적");
            // 7D-4(7D-3 발견 1): 목적지 = LKP에서 자기 쪽으로 approachStopDistanceM 앞
            Assert.AreEqual(ApproachRules.StopShort(Self, shot, config.approachStopDistanceM), brain.Intent.Destination);
            Assert.IsFalse(brain.Intent.Fire);
        }

        [Test]
        public void Combat_Scavenger_InRangeFires_FarRuns()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            var near = new Vector3(0f, 0f, 15f);
            float t = ToCombat(brain, 0f, Self, near);
            brain.Tick(AiTest.Seen(t + 0.1f, Self, near), 0.1f);
            Assert.IsTrue(brain.Intent.Fire, "선호 거리(8~20) 안: 사격");
            Assert.AreEqual(near + Vector3.up * AiTest.CenterHeight, brain.Intent.FireAt);

            var far = new Vector3(0f, 0f, 35f);
            brain.Tick(AiTest.Seen(t + 0.2f, Self, far), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Run, brain.Intent.Move, "선호 거리 밖: 달려서 좁힘");
            Assert.IsFalse(brain.Intent.Fire);

            brain.Tick(AiTest.Unseen(t + 0.3f, Self), 0.1f);
            Assert.IsFalse(brain.Intent.Fire, "안 보이면 사격 안 함");
        }

        [Test]
        public void Combat_Scavenger_StrafesSideways()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            var near = new Vector3(0f, 0f, 15f);
            float t = ToCombat(brain, 0f, Self, near);
            bool strafed = false;
            for (int i = 1; i <= 60 && !strafed; i++)
            {
                brain.Tick(AiTest.Seen(t + i * 0.1f, Self, near), 0.1f);
                if (brain.Intent.Move == EnemyMoveMode.Walk)
                {
                    strafed = true;
                    Vector3 d = brain.Intent.Destination - Self;
                    Assert.AreEqual(0f, d.z, 1e-3f, "표적 방향의 옆으로");
                    Assert.That(Mathf.Abs(d.x), Is.InRange(2f, 4f));
                    Assert.IsTrue(brain.Intent.Fire, "옆걸음 중에도 사격");
                }
            }
            Assert.IsTrue(strafed, "strafeIntervalSec(2~4초) 안에 옆으로 움직임");
        }

        [Test]
        public void Combat_Raider_CoverCycle_RunHidePeek()
        {
            int coverId = world.AddCover(new Vector3(0f, 0f, 5f), Vector3.forward);
            EnemyBrain brain = Make(M3TestData.Rdr());
            var target = new Vector3(0f, 0f, 30f);
            float t = ToCombat(brain, 0f, Self, target);
            Assert.AreEqual(brain.AgentId, world.OccupantOf(coverId), "Alert에서 엄폐를 골라 예약");

            brain.Tick(AiTest.Seen(t + 0.1f, Self, target), 0.1f);
            Assert.AreEqual(EnemyMoveMode.Run, brain.Intent.Move);
            Assert.AreEqual(new Vector3(0f, 0f, 5f), brain.Intent.Destination);
            Assert.IsFalse(brain.Intent.Fire, "이동 중 사격 안 함");

            Vector3 atCover = new Vector3(0f, 0f, 5f);
            brain.Tick(AiTest.Seen(t + 0.2f, atCover, target, hidden: true), 0.1f);
            Assert.AreEqual(EnemyPosture.Crouch, brain.Intent.Posture, "Low 엄폐에 웅크려 숨음");
            Assert.IsFalse(brain.Intent.Fire);
            Assert.IsTrue(brain.Intent.Reload, "숨은 동안 재장전 허용");

            brain.Tick(AiTest.Unseen(t + 3f, atCover), 2.8f);   // hideSec 최대 2.5초 경과
            Assert.AreEqual(EnemyPosture.Stand, brain.Intent.Posture, "Peek: 일어섬");
            brain.Tick(AiTest.Seen(t + 3.1f, atCover, target), 0.1f);
            Assert.IsTrue(brain.Intent.Fire, "Peek 중 보이면 사격");
            Assert.AreEqual(EnemyState.Combat, brain.State);
        }

        [Test]
        public void Combat_Raider_HighCover_PeeksFromPeekPosition()
        {
            var hide = new Vector3(0f, 0f, 5f);
            var peek = new Vector3(1.9f, 0f, 5f);
            world.AddCover(hide, Vector3.forward, CoverHeight.High, peek);
            EnemyBrain brain = Make(M3TestData.Rdr());
            var target = new Vector3(0f, 0f, 30f);
            float t = ToCombat(brain, 0f, Self, target);
            brain.Tick(AiTest.Unseen(t + 0.1f, hide), 0.1f);
            Assert.AreEqual(EnemyPosture.Stand, brain.Intent.Posture, "High 엄폐는 서서 숨음");
            brain.Tick(AiTest.Unseen(t + 3f, hide), 2.9f);
            Assert.AreEqual(EnemyMoveMode.Walk, brain.Intent.Move);
            Assert.AreEqual(peek, brain.Intent.Destination, "PeekPosition으로 나와 사격");
        }

        [Test]
        public void Combat_Raider_ExposedInHide_ReselectsCover()
        {
            world.AddCover(new Vector3(0f, 0f, 5f), Vector3.forward);
            world.AddCover(new Vector3(6f, 0f, 5f), Vector3.forward);
            EnemyBrain brain = Make(M3TestData.Rdr());
            var target = new Vector3(0f, 0f, 30f);
            float t = ToCombat(brain, 0f, Self, target);
            var atCover = new Vector3(0f, 0f, 5f);
            brain.Tick(AiTest.Unseen(t + 0.1f, atCover), 0.1f);   // Hide
            int callsBefore = world.TryFindCoverCalls;

            brain.Tick(AiTest.Unseen(t + 0.2f, atCover, hidden: false), 0.1f);   // 표적에게 보임
            Assert.Greater(world.TryFindCoverCalls, callsBefore, "엄폐 무효화 → 다시 고름");
        }

        [Test]
        public void Combat_Raider_NoCover_FallsBackToScavenger()
        {
            EnemyBrain brain = Make(M3TestData.Rdr());
            var target = new Vector3(0f, 0f, 30f);   // RDR 선호 거리 15~40 안
            float t = ToCombat(brain, 0f, Self, target);
            brain.Tick(AiTest.Seen(t + 0.1f, Self, target), 0.1f);
            Assert.IsTrue(brain.Intent.Fire, "엄폐가 없으면 약탈자처럼 서서 사격");
        }

        [Test]
        public void Combat_Raider_FlanksAfterNoSight_OnlyOneInSquad()
        {
            var scripted = new ScriptedCoverWorld();
            int home = scripted.Inner.AddCover(new Vector3(0f, 0f, 5f), Vector3.forward);
            int side = scripted.Inner.AddCover(new Vector3(15f, 0f, 20f), Vector3.left);
            var squad = new Squad(3, config);
            EnemyBrain brain = Make(M3TestData.Rdr(), squad: squad, w: scripted, id: 1);
            EnemyBrain buddy = Make(M3TestData.Rdr(), squad: squad, w: scripted, id: 2);

            var target = new Vector3(0f, 0f, 30f);
            scripted.Script.Enqueue((CoverResult.Found, home));
            float t = ToCombat(brain, 0f, Self, target);
            var atHome = new Vector3(0f, 0f, 5f);
            brain.Tick(AiTest.Unseen(t + 0.1f, atHome), 0.1f);   // Hide
            brain.Tick(AiTest.Unseen(t + 2.7f, atHome), 2.6f);   // Peek 시작

            // 다른 분대원이 우회권을 쥐고 있으면 우회하지 않는다
            Assert.IsTrue(squad.TryClaimFlank(buddy.AgentId, t));
            scripted.Script.Enqueue((CoverResult.Found, side));
            float tt = t + 2.7f;
            for (int i = 0; i < 4; i++)
            {
                tt += 0.1f;
                brain.Tick(AiTest.Unseen(tt, atHome), 0.1f);
            }
            int queries = scripted.Queries.Count;
            squad.ReleaseFlank(buddy.AgentId, -100f);   // 쿨다운 없이 반납

            // Peek 중 6초 이상 못 봄 → 우회권 → 측면 엄폐로 Sprint (T9 유예)
            bool flanking = false;
            for (int i = 0; i < 120 && !flanking; i++)
            {
                tt += 0.1f;
                brain.Tick(AiTest.Unseen(tt, atHome), 0.1f);
                flanking = brain.Intent.Move == EnemyMoveMode.Sprint;
            }
            Assert.IsTrue(flanking);
            Assert.Greater(scripted.Queries.Count, queries);
            Assert.IsTrue(scripted.Queries[scripted.Queries.Count - 1].Flank);
            Assert.AreEqual(brain.AgentId, squad.FlankHolder);
            Assert.AreEqual(new Vector3(15f, 0f, 20f), brain.Intent.Destination);
            Assert.AreEqual(EnemyState.Combat, brain.State, "우회 중에는 loseSightSec가 지나도 Search로 안 감");
            Assert.AreEqual(side, scripted.Reserved[brain.AgentId], "측면 엄폐 예약");

            // 도착 → 우회권 반납, Peek
            brain.Tick(AiTest.Unseen(tt + 0.1f, new Vector3(15f, 0f, 20f)), 0.1f);
            Assert.AreEqual(-1, squad.FlankHolder);
            Assert.AreEqual(EnemyPosture.Stand, brain.Intent.Posture);
        }

        [Test]
        public void Combat_Raider_FlankFails_KeepsCoverAndReleasesClaim()
        {
            int home = world.AddCover(new Vector3(0f, 0f, 5f), Vector3.forward);
            var squad = new Squad(3, config);
            EnemyBrain brain = Make(M3TestData.Rdr(), squad: squad);
            var target = new Vector3(0f, 0f, 30f);
            float t = ToCombat(brain, 0f, Self, target);
            var atHome = new Vector3(0f, 0f, 5f);
            brain.Tick(AiTest.Unseen(t + 0.1f, atHome), 0.1f);
            brain.Tick(AiTest.Unseen(t + 2.7f, atHome), 2.6f);
            float tt = t + 2.7f;
            for (int i = 0; i < 150 && !world.LastCoverQuery.Flank; i++)
            {
                tt += 0.1f;
                brain.Tick(AiTest.Unseen(tt, atHome), 0.1f);
                Assert.AreEqual(EnemyState.Combat, brain.State, "우회를 시도하기 전에는 T9 유예");
            }
            Assert.IsTrue(world.LastCoverQuery.Flank, "우회 지점 질의를 했다");
            Assert.AreEqual(-1, squad.FlankHolder, "실패(자기 엄폐뿐) → 우회권 반납");
            Assert.AreEqual(brain.AgentId, world.OccupantOf(home), "엄폐 유지");

            // 우회가 끝났으니(실패) 이제 loseSightSec가 지난 상태 → Search
            brain.Tick(AiTest.Unseen(tt + 0.1f, atHome), 0.1f);
            Assert.AreEqual(EnemyState.Search, brain.State);
        }

        // ── T12 ──
        [Test]
        public void T12_TargetGone_PatrolAndReservationsReleased()
        {
            int coverId = world.AddCover(new Vector3(0f, 0f, 5f), Vector3.forward);
            EnemyBrain brain = Make(M3TestData.Rdr(), route);
            float t = ToCombat(brain, 0f, Self, TargetFar);
            Assert.AreEqual(brain.AgentId, world.OccupantOf(coverId));

            brain.Tick(AiTest.NoTarget(t + 0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Patrol, brain.State);
            Assert.AreEqual(-1, world.OccupantOf(coverId), "엄폐 예약 해제");
            Assert.AreEqual(0f, brain.Awareness);
        }

        [Test]
        public void T12_TargetDeadInAlert_IdleWithoutRoute()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            brain.OnHeard(TargetFar, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            brain.Tick(AiTest.NoTarget(0.2f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Idle, brain.State);
        }

        [Test]
        public void T12_TargetGoneInSearch_Patrol()
        {
            EnemyBrain brain = ToSearch(out float t);
            brain.Tick(AiTest.NoTarget(t + 0.1f, Self), 0.1f);
            Assert.AreEqual(EnemyState.Patrol, brain.State);
        }

        // ── T13 ──
        [Test]
        public void T13_Died_DeadAndIgnoresEverything()
        {
            int coverId = world.AddCover(new Vector3(0f, 0f, 5f), Vector3.forward);
            var squad = new Squad(1, config);
            EnemyBrain brain = Make(M3TestData.Rdr(), squad: squad, id: 1);
            EnemyBrain other = Make(M3TestData.Rdr(), squad: squad, id: 2);
            float t = ToCombat(brain, 0f, Self, TargetFar);
            Assert.IsTrue(squad.TryClaimFlank(brain.AgentId, t));

            var changes = new List<EnemyState>();
            brain.StateChanged += (from, to) => changes.Add(to);
            brain.OnDied();

            Assert.AreEqual(EnemyState.Dead, brain.State);
            Assert.AreEqual(new[] { EnemyState.Dead }, changes.ToArray());
            Assert.AreEqual(-1, world.OccupantOf(coverId), "엄폐 예약 해제");
            Assert.AreEqual(-1, squad.FlankHolder, "우회권 해제");
            Assert.AreEqual(1, squad.Count, "분대 탈퇴");
            Assert.AreEqual(other.AgentId, squad.LeaderId, "리더 승계");

            brain.OnHeard(TargetFar, NoiseKind.Gunshot, t + 1f);
            brain.OnDamaged(TargetFar, t + 1f);
            brain.OnSquadContact(TargetFar, t + 1f);
            brain.Tick(AiTest.Seen(t + 1f, Self, TargetFar), 0.1f);
            Assert.AreEqual(EnemyState.Dead, brain.State);
            Assert.AreEqual(EnemyMoveMode.Hold, brain.Intent.Move);
            Assert.IsFalse(brain.Intent.Fire);
            Assert.AreEqual(1, changes.Count);
        }

        [Test]
        public void StateChanged_ReportsPreviousAndNext()
        {
            EnemyBrain brain = Make(M3TestData.Scv(), route);
            EnemyState from = EnemyState.Dead, to = EnemyState.Dead;
            brain.StateChanged += (a, b) => { from = a; to = b; };
            brain.Tick(AiTest.NoTarget(3f, Self), 3f);
            Assert.AreEqual(EnemyState.Idle, from);
            Assert.AreEqual(EnemyState.Patrol, to);
            Assert.AreEqual(0f, brain.TimeInState, "상태 진입 시 타이머 초기화");
        }

        [Test]
        public void OneTransitionPerTick()
        {
            // 총성(T4)과 표적이 보이는 같은 틱: Alert까지만, Combat은 다음 틱 이후
            EnemyBrain brain = Make(M3TestData.Scv());
            brain.OnHeard(TargetFar, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Seen(5f, Self, TargetFar), 5f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
        }
    }
}
