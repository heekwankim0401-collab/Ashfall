#if UNITY_EDITOR
using System.Collections;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M6 AC3 — 재 폭풍 중 AI 시야 상한 (§8.3 "시야 30m로 감소", M6 설계 §1.5·Q26, 2d).
    // 실제 M3_AI 씬 + 가짜 IAshStormInfo(이 파일 안)를 레지스트리에 등록. AiDirector는 targetResolveRetrySec마다 다시 조회한다.
    // 유효 시야 = min(60m × 자세 배율, VisibilityM). 청각은 그대로.
    public class AiStormSightTests
    {
        // RDR(경로 없음)을 빈 통로에 세우고 -z를 보게 한다. 표적은 같은 x에서 거리만 바꾼다.
        private const float EnemyZ = 30f;

        private M3Scene scene;
        private FakeAshStorm storm;

        private sealed class FakeAshStorm : IAshStormInfo
        {
            public AshStormPhase Phase { get; set; } = AshStormPhase.Peak;
            public float Intensity { get; set; } = 1f;
            public float VisibilityM { get; set; } = float.PositiveInfinity;
            public float RadiationPerSec { get; set; }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M3Scene();
            yield return M3Scene.Load(scene);
        }

        [TearDown]
        public void TearDown()
        {
            if (storm != null && GameManager.Instance != null)
                GameManager.Instance.Services.Unregister<IAshStormInfo>(storm);
            storm = null;
        }

        private static bool Detected(EnemyAgent e) => e.State != EnemyState.Idle && e.State != EnemyState.Patrol;

        private IEnumerator RegisterStorm(float visibilityM)
        {
            storm = new FakeAshStorm { VisibilityM = visibilityM };
            scene.Gm.Services.Register<IAshStormInfo>(storm);
            AiDirector director = scene.Director;
            FakeAshStorm s = storm;
            yield return M2Scene.WaitUntil(() => ReferenceEquals(director.Storm, s), 3f);
            Assert.AreEqual(visibilityM, director.VisibilityCapM, 1e-4f);
        }

        private IEnumerator SetUpRaider(float playerDistance)
        {
            yield return scene.KeepOnly("C1");
            EnemyAgent rdr = scene.Enemy("C1");
            M3Scene.WarpEnemy(rdr, new Vector3(M3Scene.OpenLaneX, 0f, EnemyZ), 180f);
            scene.TeleportPlayer(new Vector3(M3Scene.OpenLaneX, 0.05f, EnemyZ - playerDistance), rdr.transform.position);
        }

        // 폭풍 없음(서비스 없음): 45m 선 플레이어를 본다 (기존 60m 규칙)
        [UnityTest]
        public IEnumerator NoStorm_Standing45m_Seen()
        {
            Assert.IsNull(scene.Director.Storm, "M3 씬에는 폭풍 제공자가 없다");
            Assert.IsTrue(float.IsPositiveInfinity(scene.Director.VisibilityCapM));
            yield return SetUpRaider(45f);
            EnemyAgent rdr = scene.Enemy("C1");
            yield return M2Scene.WaitUntil(() => Detected(rdr), 5f);
        }

        // 폭풍 Peak(30m): 45m 선 플레이어는 못 본다(게이지 0). 20m로 다가가면 본다.
        [UnityTest]
        public IEnumerator Storm30_Standing45m_NotSeen_20m_Seen()
        {
            scene.TeleportPlayer(M3Scene.FarCorner);
            yield return RegisterStorm(30f);
            yield return SetUpRaider(45f);
            EnemyAgent rdr = scene.Enemy("C1");

            float deadline = Time.time + 4f;
            while (Time.time < deadline)
            {
                Assert.IsFalse(Detected(rdr), $"폭풍 30m 중 45m에서 발견됨: {rdr.State}");
                Assert.AreEqual(0f, rdr.Brain.Awareness, 1e-4f, "폭풍 30m, 45m: 인지 게이지 0");
                yield return null;
            }

            scene.TeleportPlayer(new Vector3(M3Scene.OpenLaneX, 0.05f, EnemyZ - 20f), rdr.transform.position);
            yield return M2Scene.WaitUntil(() => Detected(rdr), 4f);
        }

        // 폭풍이 걷히면(VisibilityM = +∞, 서비스 값만 바뀜) 같은 45m에서 다시 본다 — 매 감각 갱신마다 현재 값을 읽는다
        [UnityTest]
        public IEnumerator StormClears_SameSpot_SeenAgain()
        {
            scene.TeleportPlayer(M3Scene.FarCorner);
            yield return RegisterStorm(30f);
            yield return SetUpRaider(45f);
            EnemyAgent rdr = scene.Enemy("C1");
            yield return M2Scene.WaitSeconds(2f);
            Assert.IsFalse(Detected(rdr), "폭풍 중 45m");

            storm.VisibilityM = float.PositiveInfinity;
            storm.Intensity = 0f;
            storm.Phase = AshStormPhase.Over;
            yield return M2Scene.WaitUntil(() => Detected(rdr), 5f);
        }

        // 청각은 폭풍과 무관 (§7 청각 = §4.1 소음 반경): 폭풍 중에도 등 뒤 15m 총성 → Alert 이상, LKP = 총성 위치
        [UnityTest]
        public IEnumerator Storm30_HearingUnchanged()
        {
            scene.TeleportPlayer(M3Scene.FarCorner);
            yield return RegisterStorm(30f);
            yield return scene.KeepOnly("C1");
            EnemyAgent rdr = scene.Enemy("C1");
            M3Scene.WarpEnemy(rdr, new Vector3(M3Scene.OpenLaneX, 0f, 10f), 0f);   // +z를 본다
            yield return M2Scene.WaitSeconds(0.3f);
            Assert.IsFalse(Detected(rdr));

            Vector3 noiseAt = new Vector3(M3Scene.OpenLaneX, 0f, -5f);   // 등 뒤 15m (시야 밖)
            scene.Events.Publish(new NoiseEmitted(noiseAt, 120f, NoiseKind.Gunshot));
            yield return M2Scene.WaitUntil(() => rdr.State == EnemyState.Alert || rdr.State == EnemyState.Combat, 1.5f);
            Vector3 lkp = rdr.Brain.LastKnownPosition;
            Assert.Less(Vector3.Distance(new Vector3(lkp.x, 0f, lkp.z), new Vector3(noiseAt.x, 0f, noiseAt.z)), 0.5f,
                "LKP = 총성 위치");
        }
    }
}
#endif
