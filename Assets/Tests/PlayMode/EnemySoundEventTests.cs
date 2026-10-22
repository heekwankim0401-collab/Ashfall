#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M6 AC2 — 적 발소리·총성 이벤트 (§11, M6 설계 Q13·Q14, 2d). 실제 M3_AI 씬.
    // - 순찰하는 적 → FootstepTaken(Enemy) 간격 ≈ 보폭 / 속도, 사망 후 없음
    // - 교전 중 발사 수 = ShotFired(Enemy) 수 (탄창 소모로 센다)
    // - 적 총성은 NoiseEmitted를 내지 않아 다른 적이 Alert 되지 않는다 (M3 규칙 회귀)
    public class EnemySoundEventTests
    {
        private M3Scene scene;
        private readonly List<(float time, FootstepTaken evt, Vector3 enemyPos)> steps =
            new List<(float, FootstepTaken, Vector3)>();
        private Action<FootstepTaken> stepHandler;
        private EnemyAgent watched;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M3Scene();
            yield return M3Scene.Load(scene);
            steps.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (stepHandler != null && GameManager.Instance != null)
                GameManager.Instance.Events.Unsubscribe(stepHandler);
            stepHandler = null;
            watched = null;
        }

        private static bool Detected(EnemyAgent e) => e.State != EnemyState.Idle && e.State != EnemyState.Patrol;

        private void RecordSteps(EnemyAgent enemy)
        {
            watched = enemy;
            stepHandler = e => steps.Add((Time.time, e, watched != null ? watched.transform.position : Vector3.zero));
            scene.Events.Subscribe(stepHandler);
        }

        private static float HorizontalSpeed(EnemyAgent e)
        {
            Vector3 v = e.GetComponent<NavMeshAgent>().velocity;
            v.y = 0f;
            return v.magnitude;
        }

        // 순찰(1.5m/s) → 걷기 보폭 1.5m → 약 1초 간격의 Walk 걸음. 위치 = 그 적 발밑. 죽으면 더 없음.
        [UnityTest]
        public IEnumerator PatrollingEnemy_FootstepsAtStrideInterval_StopAfterDeath()
        {
            scene.TeleportPlayer(M3Scene.FarCorner);
            yield return scene.KeepOnly("A1");
            EnemyAgent scv = scene.Enemy("A1");
            yield return M3Scene.WaitUntil(() => scv.State == EnemyState.Patrol && HorizontalSpeed(scv) > 1f, 10f,
                () => $"순찰 시작 안 함: {scv.State}, 속도 {HorizontalSpeed(scv):0.00}");

            RecordSteps(scv);
            yield return M2Scene.WaitSeconds(6f);
            Assert.IsFalse(Detected(scv), $"플레이어는 시야 밖: {scv.State}");

            var enemySteps = steps.Where(s => s.evt.Source == SoundSource.Enemy).ToList();
            Assert.AreEqual(steps.Count, enemySteps.Count, "플레이어는 가만히 있다 — 모두 적 발소리");
            Assert.GreaterOrEqual(enemySteps.Count, 4, "6초 순찰 중 걸음 수");
            foreach (var s in enemySteps)
            {
                Assert.AreEqual(FootstepGait.Walk, s.evt.Gait, "순찰 속도 1.5 < 3.5 → Walk");
                Assert.Less(Vector3.Distance(s.evt.Position, s.enemyPos), 0.01f, "위치 = 적 발밑(발행 순간)");
            }

            float stride = scene.Director.Config.footstepStrideWalkM;
            float expected = stride / scv.Archetype.patrolSpeed;
            var intervals = new List<float>();
            for (int i = 1; i < enemySteps.Count; i++)
                intervals.Add(enemySteps[i].time - enemySteps[i - 1].time);
            intervals.Sort();
            float median = intervals[intervals.Count / 2];
            Debug.Log($"[EnemySoundEventTests] 순찰 걸음 {enemySteps.Count}개, 간격 중앙값 {median:0.00}초 (기대 {expected:0.00})");
            Assert.AreEqual(expected, median, expected * 0.3f, "걸음 간격 ≈ 보폭 / 순찰 속도");

            // 사망 → 걸음 없음
            Vector3 head = scv.transform.position + Vector3.up * (scv.CapsuleHeight * 0.95f);
            scv.ApplyDamage(new DamageInfo(10000f, 0, head, Vector3.forward, 10f));
            Assert.IsTrue(scv.IsDead, "머리 10000 피해로 사망");
            int atDeath = steps.Count;
            yield return M2Scene.WaitSeconds(2f);
            Assert.AreEqual(atDeath, steps.Count, "사망 후 발소리 없음");
        }

        // 교전: SCV A1이 무적 표적을 쏘는 동안 발사 수(탄창 소모) = ShotFired(Enemy) 수, 무기 정의 = 들고 있는 무기.
        // 같은 때 등 뒤 25m의 RDR C1(다른 분대, 표적은 시야 밖)은 평시 그대로 — 적 총성은 NoiseEmitted를 내지 않는다.
        [UnityTest]
        public IEnumerator EngagedEnemy_ShotFiredPerRound_OtherEnemyNotAlerted()
        {
            yield return scene.KeepOnly("A1", "C1");
            EnemyAgent shooter = scene.Enemy("A1");
            EnemyAgent bystander = scene.Enemy("C1");
            Vector3 targetPos = new Vector3(M3Scene.OpenLaneX, 0.05f, 0f);
            TestTarget target = TestTarget.Create(new Vector3(M3Scene.FarCorner.x, 0.05f, M3Scene.FarCorner.z + 3f));
            using var shots = new EventLog<ShotFired>(scene.Events);
            using var noises = new EventLog<NoiseEmitted>(scene.Events);
            try
            {
                yield return scene.SwapTargetTo(target, M3Scene.FarCorner);
                M3Scene.WarpEnemy(bystander, new Vector3(M3Scene.OpenLaneX, 0f, 40f), 0f);   // +z를 본다(표적 반대)
                M3Scene.WarpEnemy(shooter, new Vector3(M3Scene.OpenLaneX, 0f, 15f), 180f);
                yield return M2Scene.WaitSeconds(0.5f);
                Assert.IsFalse(Detected(bystander), $"시작 전 C1 평시: {bystander.State}");
                noises.Received.Clear();

                target.MoveTo(targetPos);
                yield return M3Scene.WaitUntil(() => shooter.State == EnemyState.Combat, 5f, () => $"A1 {shooter.State}");

                // 매 프레임(AiDirector.Update 뒤) 탄창 소모를 센다. 적은 프레임당 최대 1발. 재장전 = 새 탄창(가득) 교체.
                int roundsFired = 0;
                Magazine lastMag = shooter.Weapon.Item.LoadedMagazine;
                int lastRounds = lastMag != null ? lastMag.Rounds : 0;
                shots.Received.Clear();   // 같은 프레임 스냅숏부터 센다 (Combat 진입 프레임까지의 발사는 양쪽 다 제외)
                float deadline = Time.time + 12f;
                while (roundsFired < 10 && Time.time < deadline && !shooter.IsDead)
                {
                    yield return null;
                    Magazine mag = shooter.Weapon.Item.LoadedMagazine;
                    if (mag == null)
                    {
                        lastMag = null;
                        continue;
                    }
                    if (ReferenceEquals(mag, lastMag))
                        roundsFired += Mathf.Max(0, lastRounds - mag.Rounds);
                    else
                        roundsFired += Mathf.Max(0, mag.Capacity - mag.Rounds);
                    lastMag = mag;
                    lastRounds = mag.Rounds;
                    Assert.IsFalse(Detected(bystander), $"적 총성으로 C1이 깨어남: {bystander.State}");
                }

                Debug.Log($"[EnemySoundEventTests] 발사 {roundsFired}발, ShotFired {shots.Count}개, 표적 피격 {target.HitCount}");
                Assert.GreaterOrEqual(roundsFired, 3, "교전 중 발사");
                var enemyShots = shots.Received.Where(s => s.Source == SoundSource.Enemy).ToList();
                Assert.AreEqual(shots.Count, enemyShots.Count, "플레이어는 쏘지 않았다");
                Assert.AreEqual(roundsFired, enemyShots.Count, "발사 수 = ShotFired(Enemy) 수");
                WeaponDefinitionSO weapon = shooter.Weapon.Item.Definition;
                foreach (ShotFired s in enemyShots)
                {
                    Assert.AreSame(weapon, s.Weapon, "들고 있는 무기 정의");
                    Assert.Less(Vector3.Distance(s.Position, shooter.transform.position), 2.5f, "총구 위치");
                }

                Assert.IsFalse(noises.Received.Any(n => n.Kind == NoiseKind.Gunshot), "적 사격은 NoiseEmitted(Gunshot)를 내지 않는다");
                yield return M2Scene.WaitSeconds(1f);
                Assert.IsFalse(Detected(bystander), $"총성 뒤에도 C1 평시: {bystander.State}");
            }
            finally
            {
                if (target != null)
                    UnityEngine.Object.Destroy(target.gameObject);
            }
        }
    }
}
#endif
