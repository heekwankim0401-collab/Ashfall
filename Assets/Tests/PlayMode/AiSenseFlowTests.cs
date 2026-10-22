#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M3 AC1 — AI-SCV, AI-RDR 상태 머신·시야·청각 (§7, §4.1 소음 반경, M3 설계 §9 AiSenseFlowTests).
    // 실제 M3_AI 씬, 실제 플레이어(입력은 가상 키보드·마우스), 실제 감각(물리 레이·NoiseEmitted).
    // 시간 의존 판정은 설계 임시값 기준 예상 시간의 2배 이상 여유를 둔다.
    public class AiSenseFlowTests
    {
        // 질주 시간: 첫 발소리 펄스는 이동 첫 틱에 바로 나온다. 짧게 달리고 치워서, 적이 소리 쪽으로 돌아 플레이어를 보는 일(→ Alert)을 막는다.
        private const float SprintSec = 0.25f;

        private M3Scene scene;
        private SimInput input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M3Scene();
            yield return M3Scene.Load(scene);
        }

        [TearDown]
        public void TearDown()
        {
            input?.Dispose();
            input = null;
        }

        private static bool Detected(EnemyAgent e) => e.State != EnemyState.Idle && e.State != EnemyState.Patrol;

        // 정면 20m 선 플레이어 → Suspicious → Alert → Combat (설계: 2초 안. 여유 4초)
        [UnityTest]
        public IEnumerator Rdr_SeesStandingPlayerAt20m_SuspiciousAlertCombat()
        {
            yield return scene.KeepOnly("C1");
            EnemyAgent rdr = scene.Enemy("C1");
            M3Scene.WarpEnemy(rdr, new Vector3(M3Scene.OpenLaneX, 0f, 30f), 180f);
            var log = M3Scene.RecordStates(rdr);
            float start = Time.time;
            scene.TeleportPlayer(new Vector3(M3Scene.OpenLaneX, 0.05f, 10f), rdr.transform.position);

            yield return M2Scene.WaitUntil(() => rdr.State == EnemyState.Combat, 4f);
            float took = Time.time - start;
            Debug.Log($"[AiSenseFlowTests] RDR 20m 발견 → Combat {took:0.00}초: {M3Scene.Describe(log)}");
            var sequence = log.Select(l => l.to).ToList();
            CollectionAssert.AreEqual(new[] { EnemyState.Suspicious, EnemyState.Alert, EnemyState.Combat }, sequence,
                "Idle → Suspicious → Alert → Combat");
            Assert.LessOrEqual(took, 2.5f, "설계 기준 2초(+감각 주기 여유)");
            Assert.IsTrue(rdr.Brain.HasLastKnown);
            Assert.Less(Vector3.Distance(rdr.Brain.LastKnownPosition, scene.Motor.transform.position), 1.5f, "LKP = 플레이어 위치");
        }

        // 웅크림 40m(유효 36m) → 못 봄, 같은 자리에서 일어서면 봄 (§7 웅크림 0.6배)
        [UnityTest]
        public IEnumerator Crouching_At40m_NotSeen_StandingSameSpot_Seen()
        {
            yield return scene.KeepOnly("C1");
            EnemyAgent rdr = scene.Enemy("C1");
            M3Scene.WarpEnemy(rdr, new Vector3(M3Scene.OpenLaneX, 0f, 30f), 180f);
            input = new SimInput();

            // 시야 밖(시작 위치, 80m+)에서 웅크린 뒤 들어간다
            yield return input.Keyboard.Tap(Key.C);
            yield return M2Scene.WaitSeconds(0.5f);
            Assert.AreEqual(TargetPosture.Crouching, scene.Motor.Posture);
            scene.TeleportPlayer(new Vector3(M3Scene.OpenLaneX, 0.05f, -10f), rdr.transform.position);

            float deadline = Time.time + 5f;
            while (Time.time < deadline)
            {
                Assert.IsFalse(Detected(rdr), $"웅크림 40m에서 발견됨: {rdr.State}");
                Assert.AreEqual(0f, rdr.Brain.Awareness, 1e-4f, "웅크림 40m: 인지 게이지 0");
                yield return null;
            }

            yield return input.Keyboard.Tap(Key.C);
            Assert.AreEqual(TargetPosture.Standing, scene.Motor.Posture);
            yield return M2Scene.WaitUntil(() => Detected(rdr), 5f);
            Assert.AreEqual(EnemyState.Suspicious, rdr.State, "선 자세 40m: 원거리라 먼저 Suspicious");
        }

        // 엎드림 20m(유효 18m) → 못 봄, 같은 자리에서 웅크리면(유효 36m) 봄 (§7 엎드림 0.3배)
        [UnityTest]
        public IEnumerator Prone_At20m_NotSeen_CrouchingSameSpot_Seen()
        {
            yield return scene.KeepOnly("C1");
            EnemyAgent rdr = scene.Enemy("C1");
            M3Scene.WarpEnemy(rdr, new Vector3(M3Scene.OpenLaneX, 0f, 30f), 180f);
            input = new SimInput();

            yield return input.Keyboard.Tap(Key.Z);
            yield return M2Scene.WaitSeconds(0.6f);
            Assert.AreEqual(TargetPosture.Prone, scene.Motor.Posture);
            scene.TeleportPlayer(new Vector3(M3Scene.OpenLaneX, 0.05f, 10f), rdr.transform.position);

            float deadline = Time.time + 5f;
            while (Time.time < deadline)
            {
                Assert.IsFalse(Detected(rdr), $"엎드림 20m에서 발견됨: {rdr.State}");
                Assert.AreEqual(0f, rdr.Brain.Awareness, 1e-4f, "엎드림 20m: 인지 게이지 0");
                yield return null;
            }

            yield return input.Keyboard.Tap(Key.C);
            Assert.AreEqual(TargetPosture.Crouching, scene.Motor.Posture);
            yield return M2Scene.WaitUntil(() => Detected(rdr), 4f);
        }

        // 높은 벽(2.5m) 뒤 14m → 못 봄. 벽 끝 밖으로 나오면 봄 (가림 레이)
        [UnityTest]
        public IEnumerator BehindHighWall_NotSeen_SteppingOut_Seen()
        {
            yield return scene.KeepOnly("C1");
            EnemyAgent rdr = scene.Enemy("C1");
            // 높은 벽 HighWall (25, 10): x 23.5~26.5, z 9.8~10.2
            M3Scene.WarpEnemy(rdr, new Vector3(25f, 0f, 22f), 180f);
            scene.TeleportPlayer(new Vector3(25f, 0.05f, 8f), rdr.transform.position);
            Assert.IsTrue(M3Scene.IsBlocked(M3Scene.EnemyEye(rdr).position, scene.PlayerEye.position + Vector3.up * 0.1f,
                rdr.transform, scene.Motor.transform), "배치 확인: 벽이 시선을 막아야 한다");

            float deadline = Time.time + 4f;
            while (Time.time < deadline)
            {
                Assert.IsFalse(Detected(rdr), $"벽 뒤에서 발견됨: {rdr.State}");
                yield return null;
            }

            scene.TeleportPlayer(new Vector3(29.5f, 0.05f, 8f), rdr.transform.position);
            yield return M2Scene.WaitUntil(() => Detected(rdr), 4f);
        }

        // SCV 등 뒤 15m에서 짧게 질주(소음 반경 20m) → Suspicious, 소리 위치로 걸어간다 ("소리 추적")
        [UnityTest]
        public IEnumerator Scv_HearsSprintBehind_SuspiciousAndWalksToNoise()
        {
            yield return scene.KeepOnly("A1");
            EnemyAgent scv = scene.Enemy("A1");
            M3Scene.WarpEnemy(scv, new Vector3(M3Scene.OpenLaneX, 0f, 10f), 0f);   // +z를 봄 (플레이어는 등 뒤)
            var log = M3Scene.RecordStates(scv);
            using var noises = new EventLog<NoiseEmitted>(scene.Events);
            input = new SimInput();

            Vector3 playerStart = new Vector3(M3Scene.OpenLaneX, 0.05f, -5f);
            scene.TeleportPlayer(playerStart, playerStart + Vector3.back);   // 적 반대쪽으로 달린다
            input.Keyboard.Hold(Key.W, Key.LeftShift);
            yield return M2Scene.WaitSeconds(SprintSec);
            input.Keyboard.Release(Key.W, Key.LeftShift);
            // 시야로 확인하지 못하게 치운다 → 소리만으로 반응
            scene.TeleportPlayer(M3Scene.FarCorner);

            Assert.IsTrue(noises.Received.Any(n => n.Kind == NoiseKind.Footstep && Mathf.Approximately(n.RadiusM, 20f)),
                "질주 발소리 반경 20m (§4.1)");
            yield return M3Scene.WaitUntil(() => scv.State == EnemyState.Suspicious, 2f, () => M3Scene.Describe(log));
            Debug.Log($"[AiSenseFlowTests] SCV 발소리: {M3Scene.Describe(log)}");

            Vector3 stimulus = scv.Brain.StimulusPosition;
            Assert.Less(Vector3.Distance(new Vector3(stimulus.x, 0f, stimulus.z), new Vector3(playerStart.x, 0f, playerStart.z)), 8f,
                "자극 위치 = 발소리 위치 근처");
            float startDistance = Vector3.Distance(scv.transform.position, stimulus);
            Assert.AreEqual(EnemyMoveMode.Walk, scv.Brain.Intent.Move, "SCV Suspicious: 자극 위치로 걷기");
            yield return M2Scene.WaitUntil(() => Vector3.Distance(scv.transform.position, stimulus) < startDistance - 5f, 8f);
            Assert.AreEqual(EnemyState.Suspicious, scv.State, "보지 못했으므로 Suspicious 유지");
        }

        // RDR은 같은 발소리에 제자리에서 웅크리고 소리 쪽을 본다 (SCV와 차이). 소리가 끊기면 다시 평시로 (T11)
        [UnityTest]
        public IEnumerator Rdr_HearsSprintBehind_HoldsAndCrouches_ThenRelaxes()
        {
            yield return scene.KeepOnly("C1");
            EnemyAgent rdr = scene.Enemy("C1");
            Vector3 home = new Vector3(M3Scene.OpenLaneX, 0f, 10f);
            M3Scene.WarpEnemy(rdr, home, 0f);
            var log = M3Scene.RecordStates(rdr);
            input = new SimInput();

            Vector3 playerStart = new Vector3(M3Scene.OpenLaneX, 0.05f, -5f);
            scene.TeleportPlayer(playerStart, playerStart + Vector3.back);
            input.Keyboard.Hold(Key.W, Key.LeftShift);
            yield return M2Scene.WaitSeconds(SprintSec);
            input.Keyboard.Release(Key.W, Key.LeftShift);
            scene.TeleportPlayer(M3Scene.FarCorner);

            yield return M3Scene.WaitUntil(() => rdr.State == EnemyState.Suspicious, 2f, () => M3Scene.Describe(log));
            yield return M2Scene.WaitSeconds(0.5f);
            Assert.AreEqual(EnemyState.Suspicious, rdr.State);
            Assert.AreEqual(EnemyMoveMode.Hold, rdr.Brain.Intent.Move, "RDR Suspicious: 제자리");
            Assert.AreEqual(EnemyPosture.Crouch, rdr.Brain.Intent.Posture, "RDR Suspicious: 웅크림");
            Assert.Less(rdr.CapsuleHeight, 1.5f, "웅크린 캡슐");
            Vector3 toNoise = rdr.Brain.StimulusPosition - rdr.transform.position;
            toNoise.y = 0f;
            Assert.Less(Vector3.Angle(rdr.transform.forward, toNoise), 20f, "소리 쪽을 봄");

            // 게이지 감소(0.15/s) + suspiciousLookSec(5) 뒤 평시 복귀 (경로 없음 → Idle)
            yield return M2Scene.WaitUntil(() => rdr.State == EnemyState.Idle, 15f);
            Assert.Less(Vector3.Distance(Flat(rdr.transform.position), Flat(home)), 0.5f, "RDR은 움직이지 않았다");
            Debug.Log($"[AiSenseFlowTests] RDR 발소리: {M3Scene.Describe(log)}");
        }

        // 사격(LMB) → 소음 반경 120m 안 모든 적 Alert(이상), LKP = 총성 위치. SCV는 LKP로 달려가고 RDR은 엄폐를 잡는다.
        [UnityTest]
        public IEnumerator Gunshot_AlertsAllEnemiesInRadius_ScvChasesRdrTakesCover()
        {
            input = new SimInput();
            using var noises = new EventLog<NoiseEmitted>(scene.Events);
            var enemies = scene.Enemies.ToList();
            Vector3 muzzle = scene.PlayerMuzzlePosition;
            foreach (EnemyAgent e in enemies)
                Assert.Less(Vector3.Distance(e.transform.position, muzzle), 120f, $"{e.name}: 소음 반경 안");

            scene.SetPitch(-30f);   // 하늘로 쏜다
            yield return null;
            yield return null;
            yield return input.Mouse.ClickLeft();
            Assert.IsTrue(noises.Received.Any(n => n.Kind == NoiseKind.Gunshot), "NoiseEmitted(Gunshot) 발행");
            NoiseEmitted shot = noises.Received.First(n => n.Kind == NoiseKind.Gunshot);
            Assert.AreEqual(120f, shot.RadiusM, 1e-3f, "WPN-RIF-01 사격 소음 반경");

            yield return M2Scene.WaitUntil(() => enemies.All(e => e.State == EnemyState.Alert || e.State == EnemyState.Combat), 1.5f);
            foreach (EnemyAgent e in enemies)
                Assert.Less(Vector3.Distance(Flat(e.Brain.LastKnownPosition), Flat(shot.Position)), 0.5f, $"{e.name}: LKP = 총성 위치");

            EnemyAgent scvLeader = scene.Enemy("A1");
            EnemyAgent[] raiders = { scene.Enemy("C1"), scene.Enemy("C2") };
            float scvStart = Vector3.Distance(Flat(scvLeader.transform.position), Flat(shot.Position));
            yield return M2Scene.WaitUntil(() => raiders.All(r => scene.Director.Covers.ReservedBy(r.AgentId) >= 0), 4f);
            yield return M2Scene.WaitUntil(
                () => Vector3.Distance(Flat(scvLeader.transform.position), Flat(shot.Position)) < scvStart - 4f, 6f);
            foreach (EnemyAgent e in enemies.Where(e => e.Archetype.id == "AI-SCV"))
                Assert.AreEqual(-1, scene.Director.Covers.ReservedBy(e.AgentId), $"{e.name}: SCV는 엄폐를 쓰지 않는다");
        }

        // 분대 A 리더가 플레이어를 보면(4m 안 즉시 인지) 30m 떨어진 분대원도 0.5초 안에 Alert (분대 공유).
        // 플레이어는 엎드려(유효 시야 18m) 분대원이 직접 보지 못하게 한다.
        [UnityTest]
        public IEnumerator SquadA_LeaderSpotsPlayer_MembersAlertedWithinHalfSecond()
        {
            yield return scene.KeepOnly("A1", "A2", "A3");
            Assert.IsTrue(scene.Director.TryGetSquad(1, out Squad squad), "분대 A(1)");
            Assert.AreEqual(3, squad.Count);
            EnemyAgent leader = scene.Enemies.First(e => e.AgentId == squad.LeaderId);
            EnemyAgent[] members = scene.Enemies.Where(e => e != leader).ToArray();
            input = new SimInput();
            yield return input.Keyboard.Tap(Key.Z);   // 시야 밖(시작 위치)에서 엎드린다

            // 분대원: 리더 북쪽 30m(공유 거리 50m 안)
            M3Scene.WarpEnemy(members[0], new Vector3(M3Scene.OpenLaneX - 2f, 0f, 30f), 0f);
            M3Scene.WarpEnemy(members[1], new Vector3(M3Scene.OpenLaneX + 2f, 0f, 30f), 0f);
            M3Scene.WarpEnemy(leader, new Vector3(M3Scene.OpenLaneX, 0f, 0f), 180f);
            // 분대원 brain의 위치(공유 거리 판정)가 감각 갱신으로 새 자리를 반영할 때까지
            yield return M2Scene.WaitSeconds(0.5f);
            Assert.AreEqual(TargetPosture.Prone, scene.Motor.Posture);
            foreach (EnemyAgent m in members)
                Assert.IsFalse(Detected(m), $"{m.name}: 시작 전 평시");

            var alertTimes = new Dictionary<EnemyAgent, float>();
            foreach (EnemyAgent e in members.Append(leader))
            {
                EnemyAgent agent = e;
                agent.Brain.StateChanged += (a, b) =>
                {
                    if (b == EnemyState.Alert && !alertTimes.ContainsKey(agent))
                        alertTimes[agent] = Time.time;
                };
            }
            scene.TeleportPlayer(new Vector3(M3Scene.OpenLaneX, 0.05f, -3f), leader.transform.position);

            yield return M2Scene.WaitUntil(() => alertTimes.Count == 3, 3f);
            float leaderAt = alertTimes[leader];
            foreach (EnemyAgent m in members)
            {
                Assert.LessOrEqual(alertTimes[m] - leaderAt, 0.5f, $"{m.name}: 분대 연락으로 0.5초 안에 Alert");
                Assert.Less(Vector3.Distance(Flat(m.Brain.LastKnownPosition), Flat(scene.Motor.transform.position)), 1.5f,
                    $"{m.name}: 연락 위치 = 플레이어 위치");
            }
            Debug.Log($"[AiSenseFlowTests] 분대 공유: 리더 {leaderAt:0.00}, 분대원 " +
                      string.Join(", ", members.Select(m => $"{alertTimes[m] - leaderAt:+0.00;-0.00}")));
        }

        // Combat 뒤 시야 상실 → loseSightSec(5) 뒤 Search → searchDurationSec(25) 뒤 Patrol 복귀 (T9, T10)
        [UnityTest]
        public IEnumerator Scv_LosesSight_SearchThenBackToPatrol()
        {
            yield return scene.KeepOnly("A1");
            EnemyAgent scv = scene.Enemy("A1");
            M3Scene.WarpEnemy(scv, new Vector3(M3Scene.OpenLaneX, 0f, 0f), 180f);
            var log = M3Scene.RecordStates(scv);
            scene.TeleportPlayer(new Vector3(M3Scene.OpenLaneX, 0.05f, -12f), scv.transform.position);

            yield return M2Scene.WaitUntil(() => scv.State == EnemyState.Combat, 4f);
            // 사살당하기 전에 시야 밖(90m+)으로 치운다
            scene.TeleportPlayer(M3Scene.FarCorner);
            float lostAt = Time.time;

            yield return M2Scene.WaitUntil(() => scv.State == EnemyState.Search, 10f);
            float searchAt = Time.time;
            Assert.AreEqual(5f, searchAt - lostAt, 1.0f, "loseSightSec 5초 뒤 Search");
            Assert.IsFalse(scene.Health.IsDead, "플레이어는 시험 중 살아 있어야 한다(T12가 아니라 T9 경로)");

            yield return M2Scene.WaitUntil(() => scv.State == EnemyState.Patrol, 40f);
            Assert.AreEqual(25f, Time.time - searchAt, 1.0f, "searchDurationSec 25초 뒤 Patrol");
            var sequence = log.Select(l => l.to).ToList();
            Debug.Log($"[AiSenseFlowTests] 시야 상실: {M3Scene.Describe(log)}");
            int combat = sequence.IndexOf(EnemyState.Combat);
            CollectionAssert.AreEqual(new[] { EnemyState.Combat, EnemyState.Search, EnemyState.Patrol },
                sequence.Skip(combat).ToArray(), "Combat → Search → Patrol");
            Assert.IsTrue(sequence.Take(combat).Contains(EnemyState.Alert), "Combat 전 Alert");
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
#endif
