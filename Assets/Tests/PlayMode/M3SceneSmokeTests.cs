#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M3 확인 씬 2개(Ashfall/Setup/Create M3 Test Scenes가 생성)의 스모크 테스트 (M3 설계 §7 "3. 씬").
    // 예외·LogError는 테스트 프레임워크가 자동으로 실패 처리한다. 상태 머신·전투 흐름은 4단계 통합 테스트.
    public class M3SceneSmokeTests
    {
        private const string AiScenePath = "Assets/_Project/Scenes/Test/M3_AI.unity";
        private const string PerfScenePath = "Assets/_Project/Scenes/Test/M3_AIPerf.unity";
        private const float RunSeconds = 3f;           // 오류 없이 도는지 보는 최소 시간
        // 리더가 이만큼 움직이면 "순찰 이동"으로 본다. 순찰 속도 1.5m/s(임시값)면 2초.
        private const float MinPatrolTravelM = 3f;
        private const float PatrolTimeoutSec = 20f;

        [UnityTest]
        public IEnumerator M3AiScene_RunsWithoutErrors_EnemiesOnNavMeshAndPatrolling()
        {
            yield return RunScene(AiScenePath, expectedEnemies: 7, expectedPatrolSquads: 2);
        }

        [UnityTest]
        public IEnumerator M3AiPerfScene_RunsWithoutErrors_EnemiesOnNavMeshAndPatrolling()
        {
            yield return RunScene(PerfScenePath, expectedEnemies: 20, expectedPatrolSquads: 4);
        }

        private static IEnumerator RunScene(string path, int expectedEnemies, int expectedPatrolSquads)
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            EnemyAgent[] enemies = Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.InstanceID);
            Assert.AreEqual(expectedEnemies, enemies.Length, "적 수");
            var startPositions = new Dictionary<EnemyAgent, Vector3>();
            foreach (EnemyAgent enemy in enemies)
            {
                Assert.IsNull(enemy.transform.parent, $"{enemy.name}: 적 루트는 최상위여야 한다");
                startPositions[enemy] = enemy.transform.position;
            }

            yield return M2Scene.WaitSeconds(RunSeconds);

            GameManager gm = GameManager.Instance;
            Assert.IsNotNull(gm, "GameManager 없음");
            Assert.IsTrue(gm.Services.TryGet(out IPlayerTarget target), "IPlayerTarget 미등록");
            Assert.IsTrue(gm.Services.TryGet(out IProjectileService _), "IProjectileService 미등록");
            Assert.IsFalse(target.IsDead);
            Assert.IsTrue(NavMesh.SamplePosition(target.Position, out NavMeshHit _, 1f, NavMesh.AllAreas),
                "플레이어 위치에 NavMesh 없음");

            var directors = Object.FindObjectsByType<AiDirector>(FindObjectsSortMode.None);
            Assert.AreEqual(1, directors.Length, "AiDirector 수");
            AiDirector director = directors[0];
            Assert.IsTrue(director.enabled, "AiDirector 꺼짐 (config 누락)");
            Assert.AreEqual(expectedEnemies, director.Agents.Count, "등록된 적 수");
            Assert.IsNotNull(director.Covers, "CoverIndex 없음");
            Assert.AreSame(target, director.Target, "AiDirector가 IPlayerTarget을 잡지 못했다");
            Assert.IsNotNull(director.Projectiles, "AiDirector가 IProjectileService를 잡지 못했다");

            foreach (EnemyAgent enemy in enemies)
            {
                Assert.IsTrue(enemy.enabled, $"{enemy.name} 꺼짐");
                Assert.IsFalse(enemy.IsDead, $"{enemy.name} 사망");
                Assert.IsNotNull(enemy.Brain, $"{enemy.name}: Brain 없음 (등록 실패)");
                Assert.IsNotNull(enemy.Weapon, $"{enemy.name}: 무기 없음");
                var nav = enemy.GetComponent<NavMeshAgent>();
                Assert.IsTrue(nav.isOnNavMesh, $"{enemy.name}: NavMesh 위에 없다");
            }

            // 리더는 첫 웨이포인트에서 시작해 Idle(idleBeforePatrolSec) + 웨이포인트 대기(patrolWaitSec) 뒤 걷는다.
            // 순찰 분대(SCV)마다 리더가 MinPatrolTravelM 이상 움직일 때까지 기다린다(분대원 대형 맞추기와 구분).
            var leaders = new List<EnemyAgent>();
            foreach (EnemyAgent enemy in enemies)
            {
                if (enemy.Archetype.id == "AI-SCV" && director.TryGetSquad(enemy.SquadId, out Squad squad) &&
                    squad.LeaderId == enemy.AgentId)
                    leaders.Add(enemy);
            }
            Assert.AreEqual(expectedPatrolSquads, leaders.Count, "순찰 분대 리더 수");

            var movedSquads = new HashSet<int>();
            float startTime = Time.realtimeSinceStartup;
            float deadline = startTime + PatrolTimeoutSec;
            while (true)
            {
                foreach (EnemyAgent leader in leaders)
                {
                    if (Vector3.Distance(startPositions[leader], leader.transform.position) >= MinPatrolTravelM)
                        movedSquads.Add(leader.SquadId);
                }
                if (movedSquads.Count >= expectedPatrolSquads)
                    break;
                Assert.Less(Time.realtimeSinceStartup, deadline,
                    $"순찰 분대가 이동하지 않았다 (이동한 분대: {string.Join(",", movedSquads)})");
                yield return null;
            }
            Assert.AreEqual(expectedPatrolSquads, movedSquads.Count);
            Debug.Log($"[M3SceneSmokeTests] {path}: 순찰 리더 {leaders.Count}명 모두 {MinPatrolTravelM}m 이동까지 " +
                      $"추가 {Time.realtimeSinceStartup - startTime:0.0}초");

            foreach (EnemyAgent enemy in enemies)
                Assert.IsTrue(enemy.GetComponent<NavMeshAgent>().isOnNavMesh, $"{enemy.name}: 이동 후 NavMesh 밖");
        }
    }
}
#endif
