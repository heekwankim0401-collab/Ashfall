using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // 인지 게이지 (M3 설계 §1.5, SYS-AI AC1). 값은 AiConfigSO 초기값(TODO(GDD-?)).
    public class AwarenessTests
    {
        private AiConfigSO config;

        [SetUp]
        public void SetUp() => config = M3TestData.AiConfig();

        private float TimeToFull(float distance, float range)
        {
            var a = new AwarenessModel(config);
            float t = 0f;
            while (!a.IsFull && t < 60f)
            {
                a.AddSight(distance, range, 0.05f);
                t += 0.05f;
            }
            return t;
        }

        [Test]
        public void NearFasterThanFar()
        {
            float near = TimeToFull(10f, 60f);
            float far = TimeToFull(50f, 60f);
            Assert.Less(near, far);
            Assert.Less(near, 1f, "10m: 약 0.5초");
            Assert.Greater(far, 1f, "50m: 1초 넘게 걸림");
        }

        [Test]
        public void GainRate_LerpsNearToFar()
        {
            var a = new AwarenessModel(config);
            a.AddSight(30f, 60f, 0.1f);   // 중간 거리: (2.5 + 0.35) / 2 = 1.425/s
            Assert.AreEqual(0.1425f, a.Value, 1e-4f);
        }

        [Test]
        public void InsideInstantRange_ImmediatelyFull()
        {
            var a = new AwarenessModel(config);
            a.AddSight(3.9f, 60f, 0.01f);
            Assert.IsTrue(a.IsFull);
            Assert.AreEqual(1f, a.Value);
        }

        [Test]
        public void Decay_LowersToZero()
        {
            var a = new AwarenessModel(config);
            a.AddNoise(NoiseKind.Footstep);
            a.Decay(1f);
            Assert.AreEqual(0.25f - 0.15f, a.Value, 1e-5f);
            a.Decay(10f);
            Assert.AreEqual(0f, a.Value);
        }

        [Test]
        public void Noise_FootstepJumpGunshot()
        {
            var a = new AwarenessModel(config);
            a.AddNoise(NoiseKind.Footstep);
            Assert.AreEqual(0.25f, a.Value, 1e-5f);
            a.AddNoise(NoiseKind.Jump);
            Assert.AreEqual(0.65f, a.Value, 1e-5f);

            var b = new AwarenessModel(config);
            b.AddNoise(NoiseKind.Gunshot);
            Assert.IsTrue(b.IsFull, "총성은 즉시 1");
        }

        [Test]
        public void ClampedAtOne_ClearResets()
        {
            var a = new AwarenessModel(config);
            for (int i = 0; i < 10; i++)
                a.AddNoise(NoiseKind.Jump);
            Assert.AreEqual(1f, a.Value);
            a.Clear();
            Assert.AreEqual(0f, a.Value);
        }
    }
}
