using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 1a: 새 이벤트 (M6 설계 §1.2). 발소리·총성은 자주 발행되므로 Publish가 할당하지 않아야 한다(§1.7, M3 EventBus 방식).
    public class M6EventTests
    {
        private EventBus bus;

        [SetUp]
        public void SetUp() => bus = new EventBus();

        [Test]
        public void FootstepTaken_PublishPassesPayload()
        {
            FootstepTaken received = default;
            bus.Subscribe<FootstepTaken>(e => received = e);

            bus.Publish(new FootstepTaken(new Vector3(1f, 2f, 3f), FootstepGait.Crouch, SoundSource.Enemy));

            Assert.AreEqual(new Vector3(1f, 2f, 3f), received.Position);
            Assert.AreEqual(FootstepGait.Crouch, received.Gait);
            Assert.AreEqual(SoundSource.Enemy, received.Source);
        }

        [Test]
        public void ShotFired_PublishPassesPayload_WeaponMayBeNull()
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            try
            {
                ShotFired received = default;
                int count = 0;
                bus.Subscribe<ShotFired>(e => { received = e; count++; });

                bus.Publish(new ShotFired(new Vector3(4f, 5f, 6f), weapon, SoundSource.Player));
                Assert.AreEqual(new Vector3(4f, 5f, 6f), received.Position);
                Assert.AreSame(weapon, received.Weapon);
                Assert.AreEqual(SoundSource.Player, received.Source);

                bus.Publish(new ShotFired(Vector3.zero, null, SoundSource.Enemy));
                Assert.IsNull(received.Weapon);
                Assert.AreEqual(2, count);
            }
            finally
            {
                Object.DestroyImmediate(weapon);
            }
        }

        [Test]
        public void AshStormChanged_PublishPassesPhase()
        {
            AshStormPhase received = AshStormPhase.Pending;
            bus.Subscribe<AshStormChanged>(e => received = e.Phase);

            bus.Publish(new AshStormChanged(AshStormPhase.Peak));

            Assert.AreEqual(AshStormPhase.Peak, received);
        }

        [Test]
        public void AshStormPhase_Order()
        {
            CollectionAssert.AreEqual(
                new[] { AshStormPhase.Pending, AshStormPhase.Rising, AshStormPhase.Peak, AshStormPhase.Falling, AshStormPhase.Over },
                System.Enum.GetValues(typeof(AshStormPhase)));
        }

        [Test]
        public void NewEvents_Publish_DoNotAllocate()
        {
            int steps = 0, shots = 0, storms = 0;
            bus.Subscribe<FootstepTaken>(_ => steps++);
            bus.Subscribe<FootstepTaken>(_ => steps++);
            bus.Subscribe<ShotFired>(_ => shots++);
            bus.Subscribe<AshStormChanged>(_ => storms++);
            var step = new FootstepTaken(new Vector3(1f, 0f, 2f), FootstepGait.Sprint, SoundSource.Player);
            var shot = new ShotFired(new Vector3(0f, 1f, 0f), null, SoundSource.Enemy);
            var storm = new AshStormChanged(AshStormPhase.Rising);
            TestDelegate publishMany = () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    bus.Publish(step);
                    bus.Publish(shot);
                }
                bus.Publish(storm);
            };

            // 워밍업 + 측정 잡음 재시도 (M3 2b 메모 "0할당 측정 주의")
            AiTest.AssertNoAlloc(publishMany);
            Assert.Greater(steps, 0);
            Assert.AreEqual(steps, shots * 2);
            Assert.AreEqual(shots, storms * 100);
        }
    }
}
