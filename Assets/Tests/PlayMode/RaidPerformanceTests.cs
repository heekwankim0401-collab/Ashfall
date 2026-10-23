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
    //  B) 최악: 나머지 적을 표적 주변 35~55m로 옮겨 M4 고정 배치 20명 모두 교전 (10초)
    //  M7(7B-4, 오케스트레이터 결정 (b)): 씬은 34명(M4 20 + M7 14). M7 적(이름 Enemy_M7_*: 움직이지 않는 저격수·거점 묶인 보스 무리·근접 변이체)도
    //  씬 제자리에 두어 부하(AI 틱 34명·총성에 스스로 반응)에 포함하고, B에서 고리로 옮기는 것과 "전원 교전 진입" 전제는 M4 20명(SCV·RDR)에만 건다.
    //  (M7까지 고리로 옮기면 변이체가 표적 1.35m 둘레를 감싸 다른 적의 시야를 가려 M4가 90초 안에 교전에 들지 못한다 — 7B-3·7B-4 진단, AI 규칙은 그대로.)
    //  M7 유형의 최악 부하(변이체 근접 반복·보스 LMG 연사·저격수 경고)는 7D M7PerformanceTests가 잰다.
    //  표적 근처 변이체는 시작 때 ParkNearbyMutants로 표적에서 500m+ 떨어진 NavMesh로 옮긴다(씬·AI 틱에는 남음, 변이체는 7D에서 별도 계측).
    //  측정 직전 모든 적의 탄을 테스트에서 가득 채우고(교전 대기 동안 무적 표적에 쏘아 탄이 바닥나면 측정 구간 사격 0 — 7B-3 진단),
    //  측정 구간의 적 ShotFired를 세어 B에 실제 사격 부하가 있었는지 단언한다.
    //  + 엄폐 질의(CoverSelector.TrySelect)를 494개 지점 전체에 직접 2000회 — 선형 탐색 비용.
    // 예산: AI 평균 ≤ 1.0ms, 99% ≤ 3ms, 투사체 평균 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms(참고).
    // 한계: 배치모드는 화면을 그리지 않는다(GPU·Terrain 렌더·URP 후처리 비용 없음). 1080p 60fps는 사람이 Profiler/Stats로 확인.
    public class RaidPerformanceTests
    {
        // M4 고정 배치 20(CrimsonBasinLayout.ExpectedEnemyCount) + M7 배치 14(M7EnemyLayout.Count). PlayMode는 EditorTools를 안 보므로 상수(M7 설계 §6 7B-3).
        private const int ExpectedEnemies = 34;
        // 교전 진입 전제를 거는 M4 고정 배치 수(CrimsonBasinLayout.ExpectedEnemyCount). M7 적은 이름이 이 접두사로 시작한다(M3SceneSetup "Enemy_" + M7Names.M7EnemyPrefix).
        internal const int ExpectedM4Enemies = 20;
        internal const string M7EnemyNamePrefix = "Enemy_M7_";
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
        public IEnumerator RaidScene_34Enemies_NaturalAndM4FullCombat_AiProjectileFrameWithinBudget_CoverQueryCost()
        {
            Assert.AreEqual(ExpectedEnemies, raid.Enemies.Count, $"적 {ExpectedEnemies}명");
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
                string parked = ParkNearbyMutants(raid.Enemies, center);   // 변이체 = 7D에서 별도 계측
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
                    if (e.State == EnemyState.Combat || IsM7(e))   // M7 적은 제자리(부하에는 포함)
                        continue;
                    Vector3 spot = ring[i % ring.Count];
                    Vector3 face = center - spot;
                    M3Scene.WarpEnemy(e, spot, Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg);
                    warped++;
                }
                List<EnemyAgent> m4 = raid.Enemies.Where(e => !IsM7(e)).ToList();
                Assert.AreEqual(ExpectedM4Enemies, m4.Count, $"M4 고정 배치 {ExpectedM4Enemies}명 (나머지 {ExpectedEnemies - ExpectedM4Enemies}명 = {M7EnemyNamePrefix}*)");
                var everCombat = new HashSet<EnemyAgent>();
                var m7EverCombat = new HashSet<EnemyAgent>();
                start = Time.realtimeSinceStartup;
                while (everCombat.Count < ExpectedM4Enemies)
                {
                    if (Time.realtimeSinceStartup - start > CombatTimeoutSec)
                        Assert.Fail($"{CombatTimeoutSec}초 안에 M4 {ExpectedM4Enemies}명 교전 진입 안 됨 ({everCombat.Count}): {StateSummary()}");
                    next = MaybeNoise(center, StressNoiseRadiusM, next);
                    yield return null;
                    foreach (EnemyAgent e in raid.Enemies)
                        if (e.State == EnemyState.Combat)
                            (IsM7(e) ? m7EverCombat : everCombat).Add(e);
                }
                float toAll = Time.realtimeSinceStartup - start;
                Measurement stress = null;
                yield return Measure(center, StressNoiseRadiusM, r => stress = r);

                // ── 엄폐 질의 직접 측정 (494개 선형 탐색 + 상위 후보 레이) ──
                var cover = MeasureCoverQueries(director, target);

                string report =
                    $"[RaidPerformanceTests] 엄폐 지점 {coverCount}, Terrain {terrain.terrainData.size}, 적 {ExpectedEnemies}({parked}) | " +
                    $"A) 자연 교전({toNatural:0.0}초 만에 {NaturalMinCombat}명+): {natural} | " +
                    $"B) M4 전원 교전(옮긴 적 {warped}, M4 {ExpectedM4Enemies}명 {toAll:0.0}초, 그동안 교전한 M7 {m7EverCombat.Count}/{ExpectedEnemies - ExpectedM4Enemies}): {stress} | " +
                    $"엄폐 질의 {CoverQueries}회: 교전 위치 평균 {cover.combatUs:0.0}µs(선택 {cover.combatSelected}), 후보 많은 경우 평균 {cover.richUs:0.0}µs(선택 {cover.richSelected}) | " +
                    $"화면 {Screen.width}x{Screen.height}, 배치모드 렌더 없음";
                Debug.Log(report);

                foreach ((string label, Measurement m) in new[] { ("A", natural), ("B", stress) })
                {
                    Assert.AreEqual(ExpectedEnemies, m.MinAlive, $"{label}: 측정 중 {ExpectedEnemies}명 생존");
                    Assert.LessOrEqual(m.AiAvg, AiAverageBudgetMs, $"{label}: AI 틱 평균 예산");
                    Assert.LessOrEqual(m.AiP99, AiP99BudgetMs, $"{label}: AI 틱 99% 예산");
                    Assert.LessOrEqual(m.ProjAvg, ProjectileAverageBudgetMs, $"{label}: 투사체 평균 예산");
                    Assert.LessOrEqual(m.FrameMedian, FrameMedianBudgetMs, $"{label}: 프레임 중앙값(참고, 렌더 제외)");
                }
                Assert.GreaterOrEqual(natural.CombatAvg, 1.0, "A: 측정 중 교전");
                Assert.GreaterOrEqual(stress.CombatAvg, 10.0, "B: 측정 중 다수 교전");
                Assert.Greater(stress.Shots, 0, "B: 측정 중 적 사격(ShotFired) — 사격 부하가 실제로 측정에 들어갔다");
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
            public int Frames, AiSamples, ProjSteps, MaxLive, MinAlive, MinCombat, Shots, Refilled;

            public override string ToString() =>
                $"{Frames}프레임, 교전 평균 {CombatAvg:0.0}명(최소 {MinCombat}), AI 평균 {AiAvg:0.000}ms·99% {AiP99:0.000}ms·최대 {AiMax:0.000}ms ({AiSamples}샘플), " +
                $"투사체 평균 {ProjAvg:0.000}ms·최대 {ProjMax:0.000}ms ({ProjSteps}스텝, 동시 탄 최대 {MaxLive}), " +
                $"프레임 중앙값 {FrameMedian:0.00}ms·99% {FrameP99:0.00}ms, 생존 최소 {MinAlive}, 측정 중 적 ShotFired {Shots}(측정 전 탄 보충 {Refilled}명)";
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
            int refilled = RefillAmmo(raid.Enemies);
            AiDirector director = raid.Director;
            director.Stats.Reset();
            raid.Projectiles.ResetStats();
            int shots = 0;
            bool counting = false;
            Action<ShotFired> onShot = e =>
            {
                if (counting && e.Source == SoundSource.Enemy)
                    shots++;
            };
            raid.Events.Subscribe(onShot);
            var ai = new List<double>(20000);
            var frames = new List<double>(20000);
            var m = new Measurement { MinAlive = int.MaxValue, MinCombat = int.MaxValue };
            int combatSum = 0;
            yield return null;
            counting = true;
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
            counting = false;
            raid.Events.Unsubscribe(onShot);
            m.Shots = shots;
            m.Refilled = refilled;
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

        // 변이체 "별도 계측"(오케스트레이터 결정 (b)): 표적 근처(< ParkIfWithinM) 변이체를 표적에서 ParkMinDistanceM 넘게 떨어진 NavMesh로 옮긴다.
        // 씬에는 그대로 남아 AI 틱(순찰로 제 경로를 향해 걷기)에 포함되지만 총성(반경 ≤ 300m)을 듣지 못한다.
        // 이유: 고속도로 변이체 무리(표적에서 약 170m)가 A의 총성을 듣고 와서 표적에 겹쳐 서면(수평 0~1m) 다른 적의 시야 레이가
        // 변이체 캡슐에 막혀 M4 20명이 90초 동안 한 명도 교전에 들지 못했다(7B-4 진단 — AI 규칙 "적끼리 시야 가림"은 그대로).
        private const float ParkIfWithinM = 400f;
        private const float ParkMinDistanceM = 500f;
        private static readonly Vector2[] ParkCandidatesXZ =
        {
            new Vector2(-300f, 250f), new Vector2(-250f, 300f), new Vector2(-350f, 150f), new Vector2(-200f, 350f), new Vector2(-380f, 300f),
        };

        internal static string ParkNearbyMutants(IReadOnlyList<EnemyAgent> enemies, Vector3 center)
        {
            List<EnemyAgent> mutants = enemies.Where(e => e != null && !e.IsDead && e.Brain != null &&
                                                          e.Brain.TacticsKind == EnemyTacticsKind.Mutant &&
                                                          FlatDistance(e.transform.position, center) < ParkIfWithinM).ToList();
            if (mutants.Count == 0)
                return "옮긴 변이체 0";
            var spots = new List<Vector3>();
            foreach (Vector2 xz in ParkCandidatesXZ)
            {
                if (!NavMesh.SamplePosition(new Vector3(xz.x, 100f, xz.y), out NavMeshHit hit, 150f, NavMesh.AllAreas))
                    continue;
                if (FlatDistance(hit.position, center) < ParkMinDistanceM)
                    continue;
                for (int k = 0; k < 6 && spots.Count < mutants.Count; k++)
                {
                    Vector3 p = hit.position + new Vector3((k % 3) * 2.5f, 0f, (k / 3) * 2.5f);
                    if (NavMesh.SamplePosition(p, out NavMeshHit h2, 2f, NavMesh.AllAreas) &&
                        FlatDistance(h2.position, center) >= ParkMinDistanceM &&
                        spots.All(s => Vector3.Distance(s, h2.position) > 1.5f))
                        spots.Add(h2.position);
                }
                if (spots.Count >= mutants.Count)
                    break;
            }
            Assert.GreaterOrEqual(spots.Count, mutants.Count, "변이체를 옮길 먼 NavMesh 자리 부족");
            for (int i = 0; i < mutants.Count; i++)
                M3Scene.WarpEnemy(mutants[i], spots[i], 0f);
            return $"옮긴 변이체 {mutants.Count}({string.Join(",", mutants.Select(m => m.name))}) → 표적에서 {spots.Take(mutants.Count).Min(s => FlatDistance(s, center)):0}m+";
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        internal static bool IsM7(EnemyAgent e) => e != null && e.name.StartsWith(M7EnemyNamePrefix, StringComparison.Ordinal);

        // 테스트 도우미: 무장 있는 적의 장착·예비 탄창을 가득 채운다(제품 코드 무변경, Magazine.TryAddRound). 채운 적 수.
        // 교전 대기(최대 90초) 동안 무적 표적에 쏘아 탄이 바닥나면 측정 구간에 사격이 없다(7B-3 진단: ShotFired 0) — 측정 직전에 부른다.
        internal static int RefillAmmo(IReadOnlyList<EnemyAgent> enemies)
        {
            int count = 0;
            foreach (EnemyAgent e in enemies)
            {
                EnemyWeapon w = e != null ? e.Weapon : null;
                if (w == null)
                    continue;
                Fill(w.Item.LoadedMagazine);
                IReadOnlyList<Magazine> spares = w.Spares.Magazines;
                for (int i = 0; i < spares.Count; i++)
                    Fill(spares[i]);
                count++;
            }
            return count;
        }

        private static void Fill(Magazine m)
        {
            if (m == null)
                return;
            while (!m.IsFull && m.TryAddRound()) { }
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
