#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M3 AC2 — 엄폐 사용·측면 우회 (§7 AI-RDR "엄폐 활용, 측면 우회", M3 설계 §9 AiCombatFlowTests).
    // 실제 M3_AI 씬의 CoverPoint·NavMesh·물리 시야. 분대 C(RDR 2명)만 남긴다.
    public class AiCombatFlowTests
    {
        // 열린 지점: 낮은 벽 (±10,0)·(±5,15)·(±15,28), 높은 벽 (0,30)으로 둘러싸인 중앙. 분대 C 시작 자리(±3,35)에서 보인다.
        private static readonly Vector3 ExposedSpot = new Vector3(0f, 0.05f, 8f);

        private M3Scene scene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M3Scene();
            yield return M3Scene.Load(scene);
        }

        // Combat에 들어간 RDR이 CoverPoint를 예약하고 그 자리로 가서, 숨은 자세의 눈이 플레이어 눈에서 가려진다.
        [UnityTest]
        public IEnumerator Rdr_InCombat_MovesToCoverPoint_HiddenFromPlayerEye()
        {
            yield return scene.KeepOnly("C1", "C2");
            EnemyAgent[] raiders = scene.Enemies.ToArray();
            scene.TeleportPlayer(ExposedSpot, raiders[0].transform.position);

            yield return M2Scene.WaitUntil(() => raiders.Any(r => r.State == EnemyState.Combat), 5f);

            EnemyAgent hidden = null;
            CoverSpot spot = default;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (hidden == null)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "15초 안에 엄폐 지점에서 숨은 RDR이 없다");
                Assert.IsFalse(scene.Health.IsDead, "플레이어 사망 (시험 조건 깨짐)");
                foreach (EnemyAgent r in raiders)
                {
                    int id = scene.Director.Covers.ReservedBy(r.AgentId);
                    if (id < 0 || r.State != EnemyState.Combat)
                        continue;
                    CoverSpot s = scene.Director.Covers.Get(id);
                    bool atSpot = Flat(r.transform.position - s.Position).magnitude <= 0.7f;
                    bool hidePosture = s.Height == CoverHeight.High || r.CapsuleHeight < 1.5f;   // Low = 웅크림
                    if (!atSpot || !hidePosture)
                        continue;
                    Vector3 enemyEye = M3Scene.EnemyEye(r).position;
                    Assert.IsTrue(M3Scene.IsBlocked(scene.PlayerEye.position, enemyEye, r.transform, scene.Motor.transform),
                        $"{r.name}: 엄폐({s.Height}, {s.Position})에서 숨은 눈 {enemyEye}이 플레이어 눈에서 보인다");
                    hidden = r;
                    spot = s;
                    break;
                }
                yield return null;
            }
            Assert.AreEqual(scene.Director.Covers.OccupantOf(spot.Id), hidden.AgentId, "예약자 = 그 RDR");
            Vector3 toThreat = Flat(scene.Motor.transform.position - spot.Position);
            Assert.LessOrEqual(Vector3.Angle(spot.Forward, toThreat), 70f, "엄폐 방향이 플레이어 쪽을 막는다(보호각 70°)");
            Debug.Log($"[AiCombatFlowTests] {hidden.name} 엄폐 {spot.Height} {spot.Position} 도착·숨음");
        }

        // 표적이 사라진 뒤(시야 밖) 엄폐에서 flankAfterNoSightSec(6) 동안 못 보면 분대에서 한 명이 우회권을 쥐고
        // 마지막 위치(LKP) 기준 45° 이상 옆으로 돈다. 사망 조건을 없애려고 무적 TestTarget을 쓴다.
        [UnityTest]
        public IEnumerator Rdr_TargetHidden_OneRaiderFlanksAroundLastKnownPosition()
        {
            yield return scene.KeepOnly("C1", "C2");
            EnemyAgent[] raiders = scene.Enemies.ToArray();
            TestTarget target = TestTarget.Create(ExposedSpot);
            try
            {
                yield return scene.SwapTargetTo(target, M3Scene.FarCorner);
                yield return M2Scene.WaitUntil(() => raiders.All(r => r.State == EnemyState.Combat), 8f);
                Assert.IsTrue(scene.Director.TryGetSquad(raiders[0].SquadId, out Squad squad));

                // 표적을 치운다 (60m 밖). LKP는 마지막으로 본 자리에 남는다.
                target.MoveTo(new Vector3(45f, 0f, -45f));
                var startAngle = new Dictionary<EnemyAgent, float>();
                yield return M2Scene.WaitSeconds(0.3f);
                foreach (EnemyAgent r in raiders)
                    startAngle[r] = AngleAround(r.Brain.LastKnownPosition, r.transform.position);

                int holderSeen = -1;
                EnemyAgent flanker = null;
                float maxTurn = 0f;
                float deadline = Time.realtimeSinceStartup + 45f;
                while (flanker == null)
                {
                    Assert.Less(Time.realtimeSinceStartup, deadline,
                        $"45초 안에 우회 없음 (우회권 {holderSeen}, 최대 회전 {maxTurn:0.0}°, 상태 " +
                        string.Join(",", raiders.Select(r => $"{r.name}:{r.State}")) + ")");
                    if (squad.FlankHolder >= 0)
                        holderSeen = squad.FlankHolder;
                    foreach (EnemyAgent r in raiders)
                    {
                        if (r.State == EnemyState.Dead || !r.Brain.HasLastKnown)
                            continue;
                        float turn = Mathf.Abs(Mathf.DeltaAngle(startAngle[r], AngleAround(r.Brain.LastKnownPosition, r.transform.position)));
                        maxTurn = Mathf.Max(maxTurn, turn);
                        if (turn >= 45f && holderSeen == r.AgentId)
                            flanker = r;
                    }
                    yield return null;
                }
                Debug.Log($"[AiCombatFlowTests] 우회: {flanker.name} LKP 기준 {maxTurn:0.0}° 이동, 상태 {flanker.State}");
                Assert.AreEqual(1, raiders.Count(r => r.AgentId == holderSeen), "우회권은 한 명");

                // 표적을 AI가 돌아가는 채로 지운다: 캐시한 표적 참조로 MissingReferenceException이 나지 않고(리뷰 8)
                // 표적 없음(T12)으로 경계를 푼다(경로 없는 분대 → Idle).
                Object.Destroy(target.gameObject);
                target = null;
                yield return M3Scene.WaitUntil(() => raiders.All(r => r.State == EnemyState.Idle), 3f,
                    () => "표적 파괴 뒤 Idle 아님: " + string.Join(",", raiders.Select(r => $"{r.name}:{r.State}")));
                Assert.AreEqual(-1, squad.FlankHolder, "T12: 우회권 반납");
            }
            finally
            {
                if (target != null)
                    Object.Destroy(target.gameObject);
            }
        }

        // 위에서 본 각도(도, 0 = +z) — center에서 point 방향
        private static float AngleAround(Vector3 center, Vector3 point)
        {
            Vector3 d = Flat(point - center);
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
#endif
