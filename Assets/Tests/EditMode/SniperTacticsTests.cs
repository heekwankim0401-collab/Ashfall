using System;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7B-2a: AI-SNP 저격수 전술 (M7 설계 §1.4, Q22·Q23). EnemyBrain + SniperTactics를 순수 C#으로 돌린다.
    // 퍼치 고정(이동 의도 없음), 처음·다시 볼 때 telegraphSec 경고 뒤 사격, 놓치면 자리에서 LKP 쪽 훑기, 소리 → 돌아봄만.
    public class SniperTacticsTests
    {
        private const float Dt = 0.1f;
        private const float TelegraphSec = 1.5f;
        private const float ScanArcDeg = 60f;
        private static readonly Vector3 Perch = new Vector3(0f, 20f, 0f);
        private static readonly Vector3 TargetAt = new Vector3(0f, 0f, 120f);

        private AiConfigSO config;
        private FakeEnemyWorld world;
        private EnemyBrain brain;
        private float now;

        // §3.4 AI-SNP 값 (사격 관련 칸은 RDR 테스트 값). 퍼치라 이동 속도 0.
        private static EnemyArchetypeSO Sniper()
        {
            EnemyArchetypeSO a = M3TestData.Rdr();
            a.id = "AI-SNP";
            a.tactics = EnemyTacticsKind.Sniper;
            a.sightRange = 150f; a.sightAngle = 70f;
            a.patrolSpeed = 0f; a.moveSpeed = 0f; a.sprintSpeed = 0f;
            a.reactionTimeSec = 1.0f;
            a.semiShotIntervalSec = new Vector2(2f, 3.5f);
            a.burstRounds = Vector2Int.zero;
            a.engageMaxRangeM = 200f;
            a.preferredRangeM = Vector2.zero;
            a.usesCover = false; a.flanks = false; a.investigatesNoise = false;
            a.telegraphSec = TelegraphSec;
            a.scanArcDeg = ScanArcDeg;
            return a;
        }

        [SetUp]
        public void SetUp()
        {
            config = M3TestData.AiConfig();
            world = new FakeEnemyWorld();
            brain = new EnemyBrain(1, Sniper(), config, world, null, null, new System.Random(3));
            now = 0f;
            Assert.AreEqual(EnemyTacticsKind.Sniper, brain.TacticsKind);
        }

        // ── 도우미 ──

        private EnemySenses SeenAt(Vector3 target) => AiTest.Seen(now, Perch, target, 150f);
        private EnemySenses Unseen() => AiTest.Unseen(now, Perch);

        private void Step(EnemySenses s)
        {
            world.Now = now;
            brain.Tick(s, Dt);
            AssertHolds();
            now += Dt;
        }

        private void AssertHolds()
        {
            EnemyIntent i = brain.Intent;
            Assert.AreEqual(EnemyMoveMode.Hold, i.Move, $"{brain.State}: 이동 의도 없음");
            Assert.IsFalse(i.HasDestination, $"{brain.State}: 목적지 없음");
        }

        // 표적을 계속 보며 Telegraph가 처음 켜질 때까지. 그 시각을 돌려준다.
        private float SeeUntilTelegraph(Vector3 target, float timeoutSec = 10f)
        {
            float deadline = now + timeoutSec;
            while (now < deadline)
            {
                float t = now;
                Step(SeenAt(target));
                if (brain.Intent.Telegraph)
                    return t;
                Assert.IsFalse(brain.Intent.Fire, "경고 전 사격 없음");
            }
            Assert.Fail($"{timeoutSec}초 안에 경고가 켜지지 않았다 (상태 {brain.State}, 게이지 {brain.Awareness})");
            return 0f;
        }

        // 경고 시작 뒤 첫 사격 의도 시각까지 본다. 경고 중에는 Fire 없음·Telegraph 유지.
        private float SeeUntilFire(Vector3 target, float telegraphStart, float timeoutSec = 5f)
        {
            float deadline = now + timeoutSec;
            while (now < deadline)
            {
                float t = now;
                Step(SeenAt(target));
                Assert.IsTrue(brain.Intent.Telegraph, $"보이는 동안 섬광 유지 ({t - telegraphStart:0.00}s)");
                if (brain.Intent.Fire)
                    return t;
            }
            Assert.Fail($"{timeoutSec}초 안에 사격하지 않았다 (상태 {brain.State})");
            return 0f;
        }

        private static float YawBetween(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.SignedAngle(a, b, Vector3.up);
        }

        // ── 이동 없음 ──

        [Test]
        public void NeverMoves_ThroughAllStates()
        {
            // Idle (경로 없음 → Patrol로 가지 않음)
            for (int k = 0; k < 30; k++)
                Step(Unseen());
            Assert.AreEqual(EnemyState.Idle, brain.State);

            // 발소리 → Suspicious
            brain.OnHeard(Perch + new Vector3(10f, -20f, -10f), NoiseKind.Footstep, now);
            Step(Unseen());
            Assert.AreEqual(EnemyState.Suspicious, brain.State);

            // 봄 → Alert → Combat (사격)
            float start = SeeUntilTelegraph(TargetAt);
            SeeUntilFire(TargetAt, start);
            Assert.AreEqual(EnemyState.Combat, brain.State);

            // 놓침 → Search → (수색 시간 뒤) Idle
            float deadline = now + config.loseSightSec + config.searchDurationSec + 2f;
            bool sawSearch = false;
            while (now < deadline && brain.State != EnemyState.Idle)
            {
                Step(Unseen());
                sawSearch |= brain.State == EnemyState.Search;
            }
            Assert.IsTrue(sawSearch, "Search를 거쳤다");
            Assert.AreEqual(EnemyState.Idle, brain.State);
        }

        // ── 경고 ──

        [Test]
        public void FirstSight_TelegraphsBeforeFiring_NoShotFor1_5s()
        {
            float start = SeeUntilTelegraph(TargetAt);
            Assert.AreEqual(EnemyState.Alert, brain.State, "경고는 Alert에 들어가며 켜진다");
            Assert.IsFalse(brain.Intent.Fire);
            Assert.IsTrue(brain.Intent.LookAtPoint, "표적 쪽을 본다");
            Assert.AreEqual(Vector3.Distance(brain.Intent.LookPoint, TargetAt + Vector3.up * AiTest.CenterHeight), 0f, 1e-3f);

            float fireAt = SeeUntilFire(TargetAt, start);
            Assert.GreaterOrEqual(fireAt - start, TelegraphSec - 1e-3f, "경고 1.5초 전에는 쏘지 않는다");
            Assert.LessOrEqual(fireAt - start, TelegraphSec + Dt + 1e-3f, "경고가 끝나면 바로 쏜다");
            Assert.AreEqual(EnemyState.Combat, brain.State);
            Assert.AreEqual(Vector3.Distance(brain.Intent.FireAt, TargetAt + Vector3.up * AiTest.CenterHeight), 0f, 1e-3f);
        }

        [Test]
        public void BetweenShots_TelegraphStaysOn_FireStaysAllowed()
        {
            float start = SeeUntilTelegraph(TargetAt);
            SeeUntilFire(TargetAt, start);
            for (int k = 0; k < 30; k++)
            {
                Step(SeenAt(TargetAt));
                Assert.IsTrue(brain.Intent.Telegraph, "조준 중 섬광 유지");
                Assert.IsTrue(brain.Intent.Fire, "경고가 끝난 뒤 계속 보이면 사격 의도 유지(간격은 EnemyWeapon)");
            }
        }

        [Test]
        public void Reseen_RestartsTelegraph()
        {
            float start = SeeUntilTelegraph(TargetAt);
            SeeUntilFire(TargetAt, start);

            // 한 번 놓침 (Combat 유지 — loseSightSec 안)
            Step(Unseen());
            Assert.AreEqual(EnemyState.Combat, brain.State);
            Assert.IsFalse(brain.Intent.Telegraph, "안 보이면 섬광 꺼짐");
            Assert.IsFalse(brain.Intent.Fire);

            // 다시 봄: 경고부터 다시
            Vector3 moved = TargetAt + new Vector3(8f, 0f, 0f);
            float restart = now;
            Step(SeenAt(moved));
            Assert.IsTrue(brain.Intent.Telegraph, "다시 보면 섬광 켜짐");
            Assert.IsFalse(brain.Intent.Fire, "다시 본 순간 사격 없음");
            float fireAt = SeeUntilFire(moved, restart);
            Assert.GreaterOrEqual(fireAt - restart, TelegraphSec - 1e-3f, "다시 볼 때도 1.5초 경고");
        }

        [Test]
        public void ReseenFromSearch_RestartsTelegraph()
        {
            float start = SeeUntilTelegraph(TargetAt);
            SeeUntilFire(TargetAt, start);
            while (brain.State != EnemyState.Search)
                Step(Unseen());

            float restart = now;
            Step(SeenAt(TargetAt));
            Assert.AreEqual(EnemyState.Alert, brain.State, "T5: 수색 중 보면 Alert");
            Assert.IsTrue(brain.Intent.Telegraph);
            float fireAt = SeeUntilFire(TargetAt, restart);
            Assert.GreaterOrEqual(fireAt - restart, TelegraphSec - 1e-3f);
        }

        // ── 놓치면 자리에서 LKP 쪽 훑기 ──

        [Test]
        public void LostSight_Search_ScansTowardLastKnown_InPlace()
        {
            Vector3 lkp = new Vector3(40f, 0f, 100f);
            float start = SeeUntilTelegraph(lkp);
            SeeUntilFire(lkp, start);
            while (brain.State != EnemyState.Search)
                Step(Unseen());

            Vector3 toLkp = lkp - Perch;
            float minYaw = float.MaxValue, maxYaw = float.MinValue;
            for (int k = 0; k < 60; k++)
            {
                Step(Unseen());
                Assert.AreEqual(EnemyState.Search, brain.State);
                EnemyIntent i = brain.Intent;
                Assert.IsTrue(i.LookAtPoint, "훑기 = 시선 지점");
                float yaw = YawBetween(toLkp, i.LookPoint - Perch);
                Assert.LessOrEqual(Mathf.Abs(yaw), ScanArcDeg * 0.5f + 0.5f, "LKP 기준 좌우 scanArcDeg/2 안");
                minYaw = Mathf.Min(minYaw, yaw);
                maxYaw = Mathf.Max(maxYaw, yaw);
            }
            Assert.Less(minYaw, -20f, "왼쪽으로 훑는다");
            Assert.Greater(maxYaw, 20f, "오른쪽으로 훑는다");
        }

        [Test]
        public void Idle_ScansAroundStartForward()
        {
            float minYaw = float.MaxValue, maxYaw = float.MinValue;
            for (int k = 0; k < 60; k++)
            {
                Step(Unseen());
                EnemyIntent i = brain.Intent;
                Assert.IsTrue(i.LookAtPoint);
                Assert.IsFalse(i.Telegraph);
                Assert.IsFalse(i.Fire);
                float yaw = YawBetween(Vector3.forward, i.LookPoint - Perch);
                Assert.LessOrEqual(Mathf.Abs(yaw), ScanArcDeg * 0.5f + 0.5f);
                minYaw = Mathf.Min(minYaw, yaw);
                maxYaw = Mathf.Max(maxYaw, yaw);
            }
            Assert.AreEqual(EnemyState.Idle, brain.State);
            Assert.Less(minYaw, -20f);
            Assert.Greater(maxYaw, 20f);
        }

        // ── 소리 → 돌아봄만 ──

        [Test]
        public void Footstep_TurnsTowardSound_NoMove_NoTelegraph()
        {
            Step(Unseen());
            Vector3 noise = Perch + new Vector3(-15f, -20f, -15f);   // 등 뒤 왼쪽
            brain.OnHeard(noise, NoiseKind.Footstep, now);
            Step(Unseen());
            Assert.AreEqual(EnemyState.Suspicious, brain.State);
            Assert.IsTrue(brain.Intent.LookAtPoint);
            Assert.AreEqual(0f, Vector3.Distance(brain.Intent.LookPoint, noise), 1e-3f, "소리 쪽을 본다");
            Assert.IsFalse(brain.Intent.Telegraph);
        }

        [Test]
        public void Gunshot_Alert_LooksTowardSound_NoTelegraphUntilSeen()
        {
            Step(Unseen());
            Vector3 shot = new Vector3(-30f, 0f, -30f);
            brain.OnHeard(shot, NoiseKind.Gunshot, now);
            Step(Unseen());
            Assert.AreEqual(EnemyState.Alert, brain.State);
            EnemyIntent i = brain.Intent;
            Assert.IsTrue(i.LookAtPoint);
            Assert.AreEqual(0f, YawBetween(shot - Perch, i.LookPoint - Perch), 0.5f, "총성 쪽(LKP)으로 돈다");
            Assert.IsFalse(i.Telegraph, "못 보면 섬광 없음");
            Assert.IsFalse(i.Fire);
        }

        // ── 할당 ──

        [Test]
        public void Tick_Engaged_NoAllocation()
        {
            float start = SeeUntilTelegraph(TargetAt);
            SeeUntilFire(TargetAt, start);
            EnemySenses seen = SeenAt(TargetAt);
            EnemySenses unseen = Unseen();
            AiTest.AssertNoAlloc(() =>
            {
                seen.Now += Dt;
                brain.Tick(seen, Dt);
                unseen.Now = seen.Now + Dt;
                brain.Tick(unseen, Dt);
                seen.Now = unseen.Now;
            });
        }
    }
}
