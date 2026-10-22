using System;
using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // AI 갱신 경로 GC 할당 0 (M3 설계 §1.8, M3 AC3). 1단계 메모대로 측정할 대리자를 먼저 한 번 실행한 뒤 측정한다.
    public class AiAllocationTests
    {
        private AiConfigSO config;
        private FakeEnemyWorld world;
        private GameObject shooter;

        [SetUp]
        public void SetUp()
        {
            config = M3TestData.AiConfig();
            world = new FakeEnemyWorld();
            shooter = new GameObject("AiAllocationTests_Shooter");
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(shooter);

        private static void AssertNoAlloc(TestDelegate act) => AiTest.AssertNoAlloc(act);

        // 재시도 도우미가 쓰는 "Is.Not 해석 + ApplyTo" 경로가 진짜 할당을 매번 잡는지 (재시도가 헛통과하지 않음)
        [Test]
        public void Helper_RejectsAllocatingDelegate()
        {
            object sink = null;
            TestDelegate alloc = () => { sink = new int[64]; };
            alloc();
            NUnit.Framework.Constraints.IConstraint notAllocating =
                ((NUnit.Framework.Constraints.IResolveConstraint)UnityEngine.TestTools.Constraints.ConstraintExtensions
                    .AllocatingGCMemory(Is.Not)).Resolve();
            for (int i = 0; i < 4; i++)
                Assert.IsFalse(notAllocating.ApplyTo(alloc).IsSuccess);
            Assert.IsNotNull(sink);
        }

        private EnemyBrain Brain(EnemyArchetypeSO a, IReadOnlyList<Vector3> route = null, Squad squad = null, int id = 1) =>
            new EnemyBrain(id, a, config, world, route, squad, new System.Random(id));

        [Test]
        public void BrainTick_IdlePatrol()
        {
            var route = new List<Vector3> { new Vector3(0f, 0f, 10f), new Vector3(10f, 0f, 10f) };
            EnemyBrain brain = Brain(M3TestData.Scv(), route);
            float t = 0f;
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    t += 0.2f;
                    brain.Tick(AiTest.NoTarget(t, new Vector3(0f, 0f, (i % 20) * 0.5f)), 0.2f);
                }
            });
            Assert.AreEqual(EnemyState.Patrol, brain.State);
        }

        [Test]
        public void BrainTick_SuspiciousAlertSearch_WithNoise()
        {
            EnemyBrain brain = Brain(M3TestData.Scv());
            float t = 0f;
            var noise = new Vector3(5f, 0f, 20f);
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 300; i++)
                {
                    t += 0.1f;
                    if (i % 50 == 0)
                        brain.OnHeard(noise, NoiseKind.Footstep, t);
                    if (i % 150 == 10)
                        brain.OnHeard(noise, NoiseKind.Gunshot, t);
                    brain.Tick(AiTest.Unseen(t, Vector3.zero), 0.1f);
                }
            });
        }

        [Test]
        public void BrainTick_ScavengerCombat_SquadReport()
        {
            var squad = new Squad(1, config);
            EnemyBrain brain = Brain(M3TestData.Scv(), squad: squad, id: 1);
            EnemyBrain buddy = Brain(M3TestData.Scv(), squad: squad, id: 2);
            buddy.Tick(AiTest.NoTarget(0f, new Vector3(3f, 0f, 0f)), 0.1f);
            var target = new Vector3(0f, 0f, 15f);
            brain.OnHeard(target, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Seen(0f, Vector3.zero, target), 0.1f);
            brain.Tick(AiTest.Seen(1f, Vector3.zero, target), 1f);
            Assert.AreEqual(EnemyState.Combat, brain.State);

            float t = 1f;
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    t += 0.1f;
                    brain.Tick(AiTest.Seen(t, Vector3.zero, target), 0.1f);
                    buddy.Tick(AiTest.Unseen(t, new Vector3(3f, 0f, 0f)), 0.1f);
                }
            });
            Assert.AreEqual(EnemyState.Combat, brain.State);
        }

        [Test]
        public void BrainTick_RaiderCoverCycle()
        {
            world.AddCover(new Vector3(0f, 0f, 5f), Vector3.forward);
            world.AddCover(new Vector3(6f, 0f, 5f), Vector3.forward);
            EnemyBrain brain = Brain(M3TestData.Rdr());
            var target = new Vector3(0f, 0f, 30f);
            brain.OnHeard(target, NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Seen(0f, Vector3.zero, target), 0.1f);
            brain.Tick(AiTest.Seen(1f, Vector3.zero, target), 1f);
            Assert.AreEqual(EnemyState.Combat, brain.State);

            var atCover = new Vector3(0f, 0f, 5f);
            float t = 1f;
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 300; i++)
                {
                    t += 0.1f;
                    EnemySenses s = (i % 7 == 0) ? AiTest.Seen(t, atCover, target) : AiTest.Unseen(t, atCover);
                    brain.Tick(s, 0.1f);
                }
            });
        }

        [Test]
        public void SightConeAndAwareness()
        {
            var los = new FakeLineOfSight((from, to) => to.x > 5f);
            var awareness = new AwarenessModel(config);
            int seen = 0;
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    float range = SightCone.EffectiveRange(60f, (TargetPosture)(i % 3), config);
                    Vector3 p = new Vector3(i % 11, 1f, 20f);
                    if (SightCone.IsVisible(Vector3.up, Vector3.forward, p, p, 110f, range, los, null, null))
                    {
                        seen++;
                        awareness.AddSight(20f, range, 0.1f);
                    }
                    else
                        awareness.Decay(0.1f);
                    awareness.AddNoise(NoiseKind.Footstep);
                }
            });
            Assert.Greater(seen, 0);
        }

        // M6: 재 폭풍 시야 상한 오버로드 + 적 발소리 걸음 선택·리듬 (설계 §1.7, 2d)
        [Test]
        public void SightConeStormCap_FootstepPick_Cadence()
        {
            var los = new FakeLineOfSight((from, to) => to.x > 5f);
            var cadence = new StrideCadence();
            float[] caps = { float.PositiveInfinity, 450f, 30f, 0f };
            int seen = 0;
            int steps = 0;
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    float range = SightCone.EffectiveRange(60f, (TargetPosture)(i % 3), config, caps[i % caps.Length]);
                    Vector3 p = new Vector3(i % 11, 1f, 10f + i % 40);
                    if (SightCone.IsVisible(Vector3.up, Vector3.forward, p, p, 110f, range, los, null, null))
                        seen++;
                    float speed = i % 97 == 0 ? 0f : 1.5f + (i % 3) * 1.5f;   // 0(멈춤) · 1.5 · 3 · 4.5
                    if (EnemyAgent.TryPickFootstep(speed, i % 5 == 0, config, out FootstepGait gait, out float stride))
                    {
                        if (cadence.Tick(speed * 0.1f, stride) && gait != FootstepGait.Prone)
                            steps++;
                    }
                    else
                        cadence.Reset();
                }
            });
            Assert.Greater(seen, 0);
            Assert.Greater(steps, 0);
        }

        [Test]
        public void EventBus_PublishEnemySoundEvents()
        {
            var bus = new EventBus();
            int heard = 0;
            Action<FootstepTaken> onStep = e => heard++;
            Action<ShotFired> onShot = e => heard++;
            bus.Subscribe(onStep);
            bus.Subscribe(onShot);
            var step = new FootstepTaken(Vector3.one, FootstepGait.Walk, SoundSource.Enemy);
            var shot = new ShotFired(Vector3.one, null, SoundSource.Enemy);
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    bus.Publish(step);
                    bus.Publish(shot);
                }
            });
            Assert.AreEqual(800, heard);
        }

        [Test]
        public void CoverSelector_TrySelect()
        {
            var index = new CoverIndex(32);
            for (int i = 0; i < 30; i++)
                index.Add(new Vector3(i - 15f, 0f, 4f + (i % 5)), Vector3.forward, CoverHeight.Low, Vector3.zero);
            var los = new FakeLineOfSight((from, to) => Mathf.Min(from.y, to.y) < 1.2f);
            var ids = new int[32];
            var scores = new float[32];
            var query = new CoverQuery
            {
                AgentId = 1, Self = Vector3.zero, ThreatEye = new Vector3(0f, 1.6f, 30f), PreferredRangeM = new Vector2(15f, 40f),
            };
            var flank = query;
            flank.Flank = true;
            flank.FlankFrom = Vector3.zero;
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    CoverSelector.TrySelect(index, query, config, los, null, null, ids, scores, out _);
                    CoverSelector.TrySelect(index, flank, config, los, null, null, ids, scores, out _);
                    index.ReleaseAll(1);
                }
            });
        }

        [Test]
        public void Scheduler_CollectDue()
        {
            var s = new AiTickScheduler(32);
            for (int i = 0; i < 20; i++)
                s.Add(AiTickScheduler.PhaseOffset(i, 0.2f));
            var outSlots = new int[8];
            float now = 0f;
            AssertNoAlloc(() =>
            {
                for (int f = 0; f < 600; f++)
                {
                    now += 1f / 60f;
                    int n = s.CollectDue(now, 8, outSlots);
                    for (int k = 0; k < n; k++)
                        s.Advance(outSlots[k], 0.2f, now);
                }
            });
        }

        [Test]
        public void EnemyWeapon_Tick_FireAndReload()
        {
            var projectiles = new FakeProjectileService(8192);
            EnemyArchetypeSO scv = M3TestData.Scv();
            var smg = new EnemyWeapon(scv.loadouts[1], 2, scv, projectiles, new System.Random(1));
            var pistol = new EnemyWeapon(scv.loadouts[0], 2, scv, projectiles, new System.Random(2));
            float now = 0f;
            const float dt = 1f / 60f;
            AssertNoAlloc(() =>
            {
                for (int f = 0; f < 600; f++)
                {
                    now += dt;
                    float r = AimError.Radius(scv, 20f, f * dt, f % 2 == 0, f % 3 == 0, smg.ShotsInBurst);
                    smg.Tick(now, dt, true, Vector3.zero, Vector3.forward * 20f, r, shooter.transform);
                    pistol.Tick(now, dt, true, Vector3.zero, Vector3.forward * 20f, r, shooter.transform);
                }
            });
            Assert.Greater(projectiles.Count, 30, "재장전을 거쳐 계속 쐈다");
        }

        [Test]
        public void EventBus_PublishNoise()
        {
            var bus = new EventBus();
            int heard = 0;
            Action<NoiseEmitted> handler = e => heard++;
            bus.Subscribe(handler);
            var evt = new NoiseEmitted(Vector3.one, 20f, NoiseKind.Footstep);
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 200; i++)
                    bus.Publish(evt);
            });
            Assert.AreEqual(400, heard);
        }

        [Test]
        public void Squad_ReportAndFormation()
        {
            var squad = new Squad(1, config);
            EnemyBrain a = Brain(M3TestData.Scv(), squad: squad, id: 1);
            EnemyBrain b = Brain(M3TestData.Scv(), squad: squad, id: 2);
            a.Tick(AiTest.NoTarget(0f, Vector3.zero), 0.1f);
            b.Tick(AiTest.NoTarget(0f, Vector3.right), 0.1f);
            float t = 0f;
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    t += 0.25f;
                    squad.ReportContact(1, Vector3.zero, Vector3.forward * 10f, t);
                    squad.FormationSlot(i % 3, Vector3.zero, Vector3.forward);
                    squad.TryClaimFlank(1 + i % 2, t);
                    squad.ReleaseFlank(1, t);
                }
            });
        }
    }
}
