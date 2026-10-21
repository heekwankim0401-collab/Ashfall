#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Ashfall.Tests.PlayMode
{
    // M4 성능 (M4 설계 §2.5·§7 RaidPerformanceTests): 실제 Raid_CrimsonBasin(1km Terrain, 블록아웃, 엄폐 494, 적 20)에서
    // AiDirector 틱("Ashfall.AI.Tick")·투사체 스텝·프레임 시간을 M3 AiPerformanceTests와 같은 방식·예산으로 잰다.
    //  A) 자연 교전: 무적 TestTarget을 고속도로(POI-HWY) 남쪽 도로에 두고 총성 → 그 POI 적이 교전, 나머지는 순찰 (10초)
    //  B) 최악: 나머지 적을 표적 주변 35~55m로 옮겨 20명 모두 교전 (10초)
    //  + 엄폐 질의(CoverSelector.TrySelect)를 494개 지점 전체에 직접 2000회 — 선형 탐색 비용.
    // 예산: AI 평균 ≤ 1.0ms, 99% ≤ 3ms, 투사체 평균 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms(참고).
    // 한계: 배치모드는 화면을 그리지 않는다(GPU·Terrain 렌더·URP 후처리 비용 없음). 1080p 60fps는 사람이 Profiler/Stats로 확인.
    public class RaidPerformanceTests
    {
        private const int ExpectedEnemies = 20;
        private const float NaturalNoiseRadiusM = 200f;
        private const float StressNoiseRadiusM = 300f;
        private const float NoiseIntervalSec = 2f;
        private const int NaturalMinCombat = 3;
        private const float CombatTimeoutSec = 90f;
        private const float WarmupSec = 3f;
        private const float MeasureSec = 10f;
        private const int CoverQueries = 2000;

        private const double AiAverageBudgetMs = 1.0;
        private const double AiP99BudgetMs = 3.0;
        private const double ProjectileAverageBudgetMs = 1.0;
        private const double FrameMedianBudgetMs = 16.6;

        // 고속도로 아래 남북 도로(EXT-01 방향). 고속도로 적(SCV 3 + RDR 2)이 약 80m.
        private static readonly Vector2[] TargetCandidatesXZ =
        {
            new Vector2(0f, -385f), new Vector2(5f, -380f), new Vector2(-5f, -390f), new Vector2(0f, -370f),
        };

        private M4Raid raid;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            raid = new M4Raid();
            yield return M4Raid.Load(raid, "SP-W");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        [Timeout(420000)]
        public IEnumerator RaidScene_TwentyEnemies_NaturalAndFullCombat_AiProjectileFrameWithinBudget_CoverQueryCost()
        {
            Assert.AreEqual(ExpectedEnemies, raid.Enemies.Count, "적 20명");
            AiDirector director = raid.Director;
            int coverCount = director.Covers.Count;
            Assert.Greater(coverCount, 0);
            Terrain terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();

            Vector3 center = PickTargetSpot();
            TestTarget target = TestTarget.Create(center);
            var clones = new List<EnemyArchetypeSO>();
            try
            {
                foreach (EnemyAgent e in raid.Enemies)
                    MakeImmortal(e, clones);
                // 플레이어 대신 무적 표적. 플레이어는 서쪽 스폰에 그대로 (표적에서 약 520m).
                raid.Gm.Services.Unregister<IPlayerTarget>(raid.Motor);
                target.Register();
                yield return M2Scene.WaitUntil(() => ReferenceEquals(director.Target, target), 3f);

                // ── A) 자연 교전 ──
                float next = 0f;
                float start = Time.realtimeSinceStartup;
                while (InCombat() < NaturalMinCombat)
                {
                    if (Time.realtimeSinceStartup - start > CombatTimeoutSec)
                        Assert.Fail($"{CombatTimeoutSec}초 안에 {NaturalMinCombat}명 교전 안 됨 (교전 {InCombat()}): {StateSummary()}");
                    next = MaybeNoise(center, NaturalNoiseRadiusM, next);
                    yield return null;
                }
                float toNatural = Time.realtimeSinceStartup - start;
                Measurement natural = null;
                yield return Measure(center, NaturalNoiseRadiusM, r => natural = r);

                // ── B) 최악: 교전 중이 아닌 적을 표적 주변 고리로 옮겨 모두 교전 ──
                var ring = RingSpots(terrain, center, raid.Enemies.Count);
                int warped = 0;
                for (int i = 0; i < raid.Enemies.Count; i++)
                {
                    EnemyAgent e = raid.Enemies[i];
                    if (e.State == EnemyState.Combat)
                        continue;
                    Vector3 spot = ring[i % ring.Count];
                    Vector3 face = center - spot;
                    M3Scene.WarpEnemy(e, spot, Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg);
                    warped++;
                }
                var everCombat = new HashSet<EnemyAgent>();
                start = Time.realtimeSinceStartup;
                while (everCombat.Count < ExpectedEnemies)
                {
                    if (Time.realtimeSinceStartup - start > CombatTimeoutSec)
                        Assert.Fail($"{CombatTimeoutSec}초 안에 20명 교전 진입 안 됨 ({everCombat.Count}): {StateSummary()}");
                    next = MaybeNoise(center, StressNoiseRadiusM, next);
                    yield return null;
                    foreach (EnemyAgent e in raid.Enemies)
                        if (e.State == EnemyState.Combat)
                            everCombat.Add(e);
                }
                float toAll = Time.realtimeSinceStartup - start;
                Measurement stress = null;
                yield return Measure(center, StressNoiseRadiusM, r => stress = r);

                // ── 엄폐 질의 직접 측정 (494개 선형 탐색 + 상위 후보 레이) ──
                var cover = MeasureCoverQueries(director, target);

                string report =
                    $"[RaidPerformanceTests] 엄폐 지점 {coverCount}, Terrain {terrain.terrainData.size}, 적 {ExpectedEnemies} | " +
                    $"A) 자연 교전({toNatural:0.0}초 만에 {NaturalMinCombat}명+): {natural} | " +
                    $"B) 전원 교전(옮긴 적 {warped}, {toAll:0.0}초): {stress} | " +
                    $"엄폐 질의 {CoverQueries}회: 교전 위치 평균 {cover.combatUs:0.0}µs(선택 {cover.combatSelected}), 후보 많은 경우 평균 {cover.richUs:0.0}µs(선택 {cover.richSelected}) | " +
                    $"화면 {Screen.width}x{Screen.height}, 배치모드 렌더 없음";
                Debug.Log(report);

                foreach ((string label, Measurement m) in new[] { ("A", natural), ("B", stress) })
                {
                    Assert.AreEqual(ExpectedEnemies, m.MinAlive, $"{label}: 측정 중 20명 생존");
                    Assert.LessOrEqual(m.AiAvg, AiAverageBudgetMs, $"{label}: AI 틱 평균 예산");
                    Assert.LessOrEqual(m.AiP99, AiP99BudgetMs, $"{label}: AI 틱 99% 예산");
                    Assert.LessOrEqual(m.ProjAvg, ProjectileAverageBudgetMs, $"{label}: 투사체 평균 예산");
                    Assert.LessOrEqual(m.FrameMedian, FrameMedianBudgetMs, $"{label}: 프레임 중앙값(참고, 렌더 제외)");
                }
                Assert.GreaterOrEqual(natural.CombatAvg, 1.0, "A: 측정 중 교전");
                Assert.GreaterOrEqual(stress.CombatAvg, 10.0, "B: 측정 중 다수 교전");
                Assert.Less(cover.combatUs, 200.0, "엄폐 질의 1회 0.2ms 미만 (494개 선형 탐색)");
                Assert.Less(cover.richUs, 200.0, "엄폐 질의(후보 많음) 1회 0.2ms 미만");
            }
            finally
            {
                if (target != null)
                    UnityEngine.Object.Destroy(target.gameObject);
                foreach (EnemyArchetypeSO a in clones)
                    UnityEngine.Object.Destroy(a);
            }
        }

        private sealed class Measurement
        {
            public double AiAvg, AiP99, AiMax, ProjAvg, ProjMax, FrameMedian, FrameP99, CombatAvg;
            public int Frames, AiSamples, ProjSteps, MaxLive, MinAlive, MinCombat;

            public override string ToString() =>
                $"{Frames}프레임, 교전 평균 {CombatAvg:0.0}명(최소 {MinCombat}), AI 평균 {AiAvg:0.000}ms·99% {AiP99:0.000}ms·최대 {AiMax:0.000}ms ({AiSamples}샘플), " +
                $"투사체 평균 {ProjAvg:0.000}ms·최대 {ProjMax:0.000}ms ({ProjSteps}스텝, 동시 탄 최대 {MaxLive}), " +
                $"프레임 중앙값 {FrameMedian:0.00}ms·99% {FrameP99:0.00}ms, 생존 최소 {MinAlive}";
        }

        private IEnumerator Measure(Vector3 center, float noiseRadius, Action<Measurement> result)
        {
            float next = 0f;
            float warmEnd = Time.realtimeSinceStartup + WarmupSec;
            while (Time.realtimeSinceStartup < warmEnd)
            {
                next = MaybeNoise(center, noiseRadius, next);
                yield return null;
            }
            AiDirector director = raid.Director;
            director.Stats.Reset();
            raid.Projectiles.ResetStats();
            var ai = new List<double>(20000);
            var frames = new List<double>(20000);
            var m = new Measurement { MinAlive = int.MaxValue, MinCombat = int.MaxValue };
            int combatSum = 0;
            yield return null;
            float end = Time.realtimeSinceStartup + MeasureSec;
            while (Time.realtimeSinceStartup < end)
            {
                next = MaybeNoise(center, noiseRadius, next);
                yield return null;
                ai.Add(director.Stats.LastMs);
                frames.Add(Time.unscaledDeltaTime * 1000.0);
                int alive = raid.Enemies.Count(e => e != null && !e.IsDead);
                int combat = InCombat();
                m.MinAlive = Math.Min(m.MinAlive, alive);
                m.MinCombat = Math.Min(m.MinCombat, combat);
                combatSum += combat;
                m.MaxLive = Math.Max(m.MaxLive, raid.Projectiles.LiveCount);
            }
            m.Frames = frames.Count;
            m.AiAvg = director.Stats.AverageMs;
            m.AiMax = director.Stats.MaxMs;
            m.AiSamples = director.Stats.Samples;
            m.AiP99 = Percentile(ai, 0.99);
            m.ProjAvg = raid.Projectiles.AverageStepMs;
            m.ProjMax = raid.Projectiles.MaxStepMs;
            m.ProjSteps = raid.Projectiles.StepSamples;
            m.FrameMedian = Percentile(frames, 0.5);
            m.FrameP99 = Percentile(frames, 0.99);
            m.CombatAvg = (double)combatSum / Math.Max(1, m.Frames);
            result(m);
        }

        // 엄폐 질의 직접 측정. 두 경우:
        //  combat: 교전 중 적 위치에서 실제 표적을 위협으로 (실전과 같은 질의)
        //  rich: 엄폐 지점 바로 뒤 2m에서, 그 지점 정면 25m를 위협으로 — 후보가 많아 상위 후보 레이(최대 coverLosCandidates×2)까지 가는 경우
        private (double combatUs, int combatSelected, double richUs, int richSelected) MeasureCoverQueries(AiDirector director, TestTarget target)
        {
            AiConfigSO config = director.Config;
            var los = new PhysicsLineOfSight();
            int k = Math.Max(1, config.coverLosCandidates);
            var ids = new int[k];
            var scores = new float[k];
            var query = new CoverQuery
            {
                AgentId = 100000,   // 예약 없는 가짜 id
                ThreatEye = target.EyePosition,
                PreferredRangeM = new Vector2(10f, 40f),
            };
            EnemyAgent first = raid.Enemies[0];
            query.Self = first.transform.position;
            CoverSelector.TrySelect(director.Covers, query, config, los, first.transform, target.transform, ids, scores, out _);   // JIT 제외

            int combatSelected = 0;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < CoverQueries; i++)
            {
                EnemyAgent e = raid.Enemies[i % raid.Enemies.Count];
                query.Self = e.transform.position;
                if (CoverSelector.TrySelect(director.Covers, query, config, los, e.transform, target.transform, ids, scores, out _))
                    combatSelected++;
            }
            sw.Stop();
            double combatUs = sw.Elapsed.TotalMilliseconds * 1000.0 / CoverQueries;

            int richSelected = 0;
            CoverIndex covers = director.Covers;
            sw.Restart();
            for (int i = 0; i < CoverQueries; i++)
            {
                CoverSpot spot = covers.Get(i % covers.Count);
                query.Self = spot.Position - spot.Forward * 2f;
                query.ThreatEye = spot.Position + spot.Forward * 25f + Vector3.up * 1.6f;
                if (CoverSelector.TrySelect(covers, query, config, los, first.transform, target.transform, ids, scores, out _))
                    richSelected++;
            }
            sw.Stop();
            double richUs = sw.Elapsed.TotalMilliseconds * 1000.0 / CoverQueries;
            return (combatUs, combatSelected, richUs, richSelected);
        }

        private Vector3 PickTargetSpot()
        {
            foreach (Vector2 xz in TargetCandidatesXZ)
            {
                if (!NavMesh.SamplePosition(new Vector3(xz.x, 100f, xz.y), out NavMeshHit hit, 150f, NavMesh.AllAreas))
                    continue;
                Vector3 p = hit.position;
                if (Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.5f, 0.3f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                return p;
            }
            Assert.Fail("표적 자리를 찾지 못했다");
            return Vector3.zero;
        }

        // 표적 주변 35~55m 고리 위 NavMesh 지점 (막히지 않은 곳)
        private static List<Vector3> RingSpots(Terrain terrain, Vector3 center, int count)
        {
            var spots = new List<Vector3>();
            for (int i = 0; i < 72 && spots.Count < count; i++)
            {
                float angle = i * 5f * 7f * Mathf.Deg2Rad;   // 35°씩 돌며 퍼뜨림
                float radius = 35f + (i % 5) * 5f;
                Vector3 p = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                    continue;
                if (spots.Any(s => Vector3.Distance(s, hit.position) < 3f))
                    continue;
                spots.Add(hit.position);
            }
            Assert.GreaterOrEqual(spots.Count, count / 2, "고리 지점 부족");
            return spots;
        }

        private int InCombat() => raid.Enemies.Count(e => e != null && !e.IsDead && e.State == EnemyState.Combat);

        private string StateSummary() =>
            string.Join(", ", raid.Enemies.Where(e => e != null).Select(e => $"{e.name}:{e.State}@{(Vector2)new Vector2(e.transform.position.x, e.transform.position.z)}"));

        private float MaybeNoise(Vector3 at, float radius, float next)
        {
            if (Time.time < next)
                return next;
            raid.Events.Publish(new NoiseEmitted(at + Vector3.up * 1.4f, radius, NoiseKind.Gunshot));
            return Time.time + NoiseIntervalSec;
        }

        // M3 AiPerformanceTests와 같다: 아군 사격으로 수가 줄지 않게 복제 아키타입(HP 1e9)으로 몸 모델만 바꾼다.
        private void MakeImmortal(EnemyAgent enemy, List<EnemyArchetypeSO> clones)
        {
            EnemyArchetypeSO clone = UnityEngine.Object.Instantiate(enemy.Archetype);
            clone.hp = 1e9f;
            clone.headHp = 1e9f;
            clone.legsHp = 1e9f;
            clones.Add(clone);
            PropertyInfo body = typeof(EnemyAgent).GetProperty(nameof(EnemyAgent.Body));
            Assert.IsNotNull(body);
            body.SetValue(enemy, new EnemyBodyModel(clone, raid.Director.Config));
        }

        private static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0)
                return 0.0;
            var sorted = values.OrderBy(v => v).ToList();
            int index = Math.Min(sorted.Count - 1, Math.Max(0, (int)Math.Ceiling(p * sorted.Count) - 1));
            return sorted[index];
        }
    }
}
#endif
