#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M3 AC3 — 동시 적 20명 60fps (§15 M3, M3 설계 §1.8 측정 방법).
    // M3_AIPerf(적 20명)에서 플레이어 대신 무적 TestTarget을 표적으로 등록 → 총성으로 모두 깨워 교전 →
    // 워밍업 3초 + 측정 10초 동안 AiDirector.Stats("Ashfall.AI.Tick")·ProjectileSystem 스텝 통계·프레임 시간.
    // 예산: AI 평균 ≤ 1.0ms, AI 99% ≤ 3ms, 투사체 평균 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms(참고).
    // 한계: 배치모드는 화면을 그리지 않아 GPU·렌더 비용이 빠진다(CPU 마커만 의미 있음). GTX 1660 60fps는 사람이 확인.
    public class AiPerformanceTests
    {
        private const int ExpectedEnemies = 20;
        private const float WakeNoiseRadiusM = 200f;
        // 교전 유지용 총성 간격 (플레이어가 계속 쏘는 상황). 엄폐 뒤 RDR은 Alert에서도 Peek로 내다봐 표적을 찾는다(리뷰 1).
        private const float NoiseIntervalSec = 2f;
        private const float AllCombatTimeoutSec = 60f;
        private const float WarmupSec = 3f;
        private const float MeasureSec = 10f;
        private const int GcFrames = 120;

        private const double AiAverageBudgetMs = 1.0;
        private const double AiP99BudgetMs = 3.0;
        private const double ProjectileAverageBudgetMs = 1.0;
        private const double FrameMedianBudgetMs = 16.6;

        private M3Scene scene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M3Scene();
            yield return M3Scene.Load(scene, M3Scene.PerfScenePath);
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator TwentyEnemiesInCombat_AiAndProjectileTimesWithinBudget_AiUpdateDoesNotAllocate()
        {
            Assert.AreEqual(ExpectedEnemies, scene.Enemies.Count);
            AiDirector director = scene.Director;

            // 표적: (0, 35) — 사분면 사이 빈 곳이며 RDR 8명(경로 없음, 제자리 경계)이 모두 시야 60m 안(약 50m)에 든다.
            // 맵 정중앙(0,0)은 북쪽 RDR 4명에게서 70m라 그들은 끝내 보지 못해 Alert에 머문다(1차 실행에서 확인).
            // 플레이어는 등록 해제 후 남서쪽 구석으로.
            Vector3 center = new Vector3(0f, 0f, 35f);
            Assert.IsTrue(NavMesh.SamplePosition(center, out NavMeshHit hit, 2f, NavMesh.AllAreas), "중앙 NavMesh");
            center = hit.position;
            Assert.IsFalse(Physics.CheckCapsule(center + Vector3.up * 0.4f, center + Vector3.up * 1.5f, 0.3f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "표적 자리가 막혀 있다");
            TestTarget target = TestTarget.Create(center);
            var immortalArchetypes = new List<EnemyArchetypeSO>();
            try
            {
                // 아군 사격(설계상 허용)으로 측정 중 적 수가 줄지 않게, 테스트에서만 적 부위 HP를 매우 크게 한 몸 모델로 바꾼다.
                // (1차 실행: 20명 중 4명이 아군 탄에 사망.) 피격 → brain.OnDamaged 반응 경로는 그대로다.
                foreach (EnemyAgent e in scene.Enemies)
                    MakeImmortal(e, immortalArchetypes);
                yield return scene.SwapTargetTo(target, new Vector3(-72f, 0.05f, -72f));

                // 총성으로 모두 깨우고, 20명 모두 한 번 이상 Combat에 들어갈 때까지 (총성은 계속).
                var everCombat = new HashSet<EnemyAgent>();
                float nextNoise = 0f;
                float start = Time.realtimeSinceStartup;
                while (everCombat.Count < LiveEnemies().Count())
                {
                    if (Time.realtimeSinceStartup - start > AllCombatTimeoutSec)
                        Assert.Fail($"{AllCombatTimeoutSec}초 안에 모두 Combat에 들어가지 않았다 (진입 {everCombat.Count}명): " +
                                    string.Join(" | ", LiveEnemies().Where(e => !everCombat.Contains(e)).Select(e => Diagnose(e, target))));
                    nextNoise = MaybeNoise(center, nextNoise);
                    yield return null;
                    foreach (EnemyAgent e in LiveEnemies())
                        if (e.State == EnemyState.Combat)
                            everCombat.Add(e);
                }
                float toAllCombat = Time.realtimeSinceStartup - start;
                Assert.AreEqual(ExpectedEnemies, everCombat.Count, "20명 모두 Combat 진입");

                // 워밍업
                float warmEnd = Time.realtimeSinceStartup + WarmupSec;
                while (Time.realtimeSinceStartup < warmEnd)
                {
                    nextNoise = MaybeNoise(center, nextNoise);
                    yield return null;
                }

                // 측정
                director.Stats.Reset();
                scene.Projectiles.ResetStats();
                var aiFrameMs = new List<double>(20000);
                var frameMs = new List<double>(20000);
                int combatFrames = 0, combatSum = 0, minCombat = int.MaxValue, maxLive = 0, minAlive = int.MaxValue;
                int targetHitsBefore = target.HitCount;
                float measureEnd = Time.realtimeSinceStartup + MeasureSec;
                yield return null;
                while (Time.realtimeSinceStartup < measureEnd)
                {
                    nextNoise = MaybeNoise(center, nextNoise);
                    yield return null;
                    aiFrameMs.Add(director.Stats.LastMs);
                    frameMs.Add(Time.unscaledDeltaTime * 1000.0);
                    int inCombat = 0, alive = 0;
                    foreach (EnemyAgent e in scene.Enemies)
                    {
                        if (e == null || e.IsDead)
                            continue;
                        alive++;
                        if (e.State == EnemyState.Combat)
                            inCombat++;
                    }
                    combatFrames++;
                    combatSum += inCombat;
                    minCombat = Math.Min(minCombat, inCombat);
                    minAlive = Math.Min(minAlive, alive);
                    maxLive = Math.Max(maxLive, scene.Projectiles.LiveCount);
                }

                double aiAvg = director.Stats.AverageMs;
                double aiMax = director.Stats.MaxMs;
                double aiP99 = Percentile(aiFrameMs, 0.99);
                double projAvg = scene.Projectiles.AverageStepMs;
                double projMax = scene.Projectiles.MaxStepMs;
                int projSteps = scene.Projectiles.StepSamples;
                double frameMedian = Percentile(frameMs, 0.5);
                int aliveEnd = LiveEnemies().Count();

                // GC: AiDirector를 끄고 같은 Update를 프레임마다 직접 불러 그 구간의 GC.Alloc만 잰다 (교전 지속 중).
                int gcAllocFrames = 0, gcAllocSamples = 0, gcNoiseFrames = 0;
                yield return MeasureDirectorGc(director, (frames, samples, noisy) =>
                {
                    gcAllocFrames = frames;
                    gcAllocSamples = samples;
                    gcNoiseFrames = noisy;
                });

                string report =
                    $"[AiPerformanceTests] 적 {ExpectedEnemies}명(측정 중 생존 최소 {minAlive}, 종료 {aliveEnd}), Combat 진입 {everCombat.Count}명({toAllCombat:0.0}초), " +
                    $"측정 {MeasureSec}초 {combatFrames}프레임: Combat 평균 {(double)combatSum / Math.Max(1, combatFrames):0.0}명(최소 {minCombat}) | " +
                    $"AI 틱 평균 {aiAvg:0.000}ms, 99% {aiP99:0.000}ms, 최대 {aiMax:0.000}ms ({director.Stats.Samples}샘플) | " +
                    $"투사체 스텝 평균 {projAvg:0.000}ms, 최대 {projMax:0.000}ms ({projSteps}스텝, 동시 탄 최대 {maxLive}) | " +
                    $"프레임 중앙값 {frameMedian:0.00}ms | 표적 명중 {target.HitCount - targetHitsBefore} | " +
                    $"AI Update GC.Alloc: {GcFrames}프레임 중 {gcAllocFrames}프레임, {gcAllocSamples}회 (측정 잡음으로 제외한 프레임 {gcNoiseFrames})";
                Debug.Log(report);

                Assert.AreEqual(ExpectedEnemies, minAlive, "측정 중 적 20명 생존");
                Assert.Greater(combatSum, 0, "측정 중 교전");
                Assert.LessOrEqual(aiAvg, AiAverageBudgetMs, "AI 틱 평균 예산");
                Assert.LessOrEqual(aiP99, AiP99BudgetMs, "AI 틱 99% 예산");
                Assert.LessOrEqual(projAvg, ProjectileAverageBudgetMs, "투사체 스텝 평균 예산");
                Assert.LessOrEqual(frameMedian, FrameMedianBudgetMs, "프레임 중앙값(참고)");
                Assert.AreEqual(0, gcAllocSamples, "AiDirector.Update 경로 GC 할당");
            }
            finally
            {
                if (target != null)
                    UnityEngine.Object.Destroy(target.gameObject);
                foreach (EnemyArchetypeSO a in immortalArchetypes)
                    UnityEngine.Object.Destroy(a);
            }
        }

        // 원본 아키타입 에셋은 건드리지 않고 복제본(HP 1e9)으로 EnemyBodyModel을 새로 만들어 EnemyAgent.Body에 넣는다.
        private void MakeImmortal(EnemyAgent enemy, List<EnemyArchetypeSO> clones)
        {
            EnemyArchetypeSO clone = UnityEngine.Object.Instantiate(enemy.Archetype);
            clone.hp = 1e9f;
            clone.headHp = 1e9f;
            clone.legsHp = 1e9f;
            clones.Add(clone);
            PropertyInfo body = typeof(EnemyAgent).GetProperty(nameof(EnemyAgent.Body));
            Assert.IsNotNull(body);
            body.SetValue(enemy, new EnemyBodyModel(clone, scene.Director.Config));
        }

        // 실패 진단: 상태·전술 단계·엄폐·의도·표적 시야
        private string Diagnose(EnemyAgent e, TestTarget target)
        {
            object tactics = typeof(EnemyBrain).GetProperty("Tactics", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(e.Brain);
            object phase = tactics?.GetType().GetProperty("PhaseName", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(tactics);
            int coverId = scene.Director.Covers.ReservedBy(e.AgentId);
            string cover = "없음";
            if (coverId >= 0)
            {
                CoverSpot spot = scene.Director.Covers.Get(coverId);
                Vector3 fireEye = (spot.Height == CoverHeight.Low ? spot.Position : spot.PeekPosition) + Vector3.up * 1.6f;
                bool fireBlocked = M3Scene.IsBlocked(fireEye, target.EyePosition, e.transform, target.transform);
                cover = $"{spot.Height}@{spot.Position} 사격눈{fireEye}→표적 {(fireBlocked ? "막힘" : "뚫림")}";
            }
            EnemyIntent i = e.Brain.Intent;
            Transform eye = M3Scene.EnemyEye(e);
            bool blocked = M3Scene.IsBlocked(eye.position, target.EyePosition, e.transform, target.transform);
            return $"{e.name}:{e.State}/{phase} 위치 {e.transform.position} 엄폐 {cover} 의도 {i.Move}/{i.Posture}" +
                   $"{(i.HasDestination ? "→" + i.Destination : "")} 시선 {i.LookPoint} LKP {e.Brain.LastKnownPosition} " +
                   $"눈→표적 {(blocked ? "막힘" : "뚫림")} 거리 {Vector3.Distance(eye.position, target.EyePosition):0.0}";
        }

        private IEnumerable<EnemyAgent> LiveEnemies() => scene.Enemies.Where(e => e != null && !e.IsDead);

        // 표적 위치에서 총성 (플레이어 WeaponController가 내는 것과 같은 이벤트)
        private float MaybeNoise(Vector3 at, float nextNoise)
        {
            if (Time.time < nextNoise)
                return nextNoise;
            scene.Events.Publish(new NoiseEmitted(at + Vector3.up * 1.4f, WakeNoiseRadiusM, NoiseKind.Gunshot));
            return Time.time + NoiseIntervalSec;
        }

        // Unity Test Framework의 AllocatingGCMemory와 같은 방식(Recorder "GC.Alloc", 현재 스레드)으로 director.Update만 감싼다.
        // 측정 잡음(첫 측정에서 바깥 샘플이 섞임, 2b 메모)을 피하려고 빈 대리자로 한 번 먼저 잰다.
        private static IEnumerator MeasureDirectorGc(AiDirector director, Action<int, int, int> result)
        {
            MethodInfo method = typeof(AiDirector).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            var update = (Action)Delegate.CreateDelegate(typeof(Action), director, method);
            director.enabled = false;   // 엔진 Update 대신 직접 부른다

            Recorder recorder = Recorder.Get("GC.Alloc");
            Action empty = () => { };
            Sample(recorder, empty);
            Sample(recorder, update);   // 첫 호출(JIT) 제외

            int frames = 0, samples = 0, noisy = 0;
            for (int i = 0; i < GcFrames; i++)
            {
                yield return null;
                int n = Sample(recorder, update);
                if (n > 0)
                {
                    // 잡음 확인: 바로 한 번 더 잰 빈 대리자도 할당으로 잡히면 측정 잡음으로 본다
                    if (Sample(recorder, empty) > 0)
                    {
                        noisy++;
                        continue;
                    }
                    frames++;
                    samples += n;
                }
            }
            director.enabled = true;
            result(frames, samples, noisy);
        }

        private static int Sample(Recorder recorder, Action action)
        {
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }
            return recorder.sampleBlockCount;
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
