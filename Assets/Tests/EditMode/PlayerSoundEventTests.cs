using Ashfall.Core;
using Ashfall.Player;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 플레이어 발소리 걸음 선택·보폭 (§11, M6 설계 §3.6·§4.4, Q12). 발행 자체는 PlayMode PlayerSoundEventTests.
    public class PlayerSoundEventTests
    {
        [TestCase(TargetPosture.Standing, false, FootstepGait.Walk)]
        [TestCase(TargetPosture.Standing, true, FootstepGait.Sprint)]
        [TestCase(TargetPosture.Crouching, false, FootstepGait.Crouch)]
        [TestCase(TargetPosture.Crouching, true, FootstepGait.Crouch)]
        [TestCase(TargetPosture.Prone, false, FootstepGait.Prone)]
        [TestCase(TargetPosture.Prone, true, FootstepGait.Prone)]
        public void SelectGait_PostureOverSprintOverWalk(TargetPosture posture, bool sprinting, FootstepGait expected)
        {
            Assert.AreEqual(expected, PlayerMotor.SelectGait(posture, sprinting));
        }

        [Test]
        public void StrideLength_PerGait_FromConfig()
        {
            var config = ScriptableObject.CreateInstance<PlayerConfigSO>();
            try
            {
                Assert.AreEqual(config.strideWalkM, PlayerMotor.StrideLength(config, FootstepGait.Walk));
                Assert.AreEqual(config.strideSprintM, PlayerMotor.StrideLength(config, FootstepGait.Sprint));
                Assert.AreEqual(config.strideCrouchM, PlayerMotor.StrideLength(config, FootstepGait.Crouch));
                Assert.AreEqual(config.strideProneM, PlayerMotor.StrideLength(config, FootstepGait.Prone));
                Assert.AreEqual(1.5f, PlayerMotor.StrideLength(config, FootstepGait.Walk), 1e-6f, "§3.6 기본값");
                Assert.AreEqual(2.2f, PlayerMotor.StrideLength(config, FootstepGait.Sprint), 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void StrideLength_NoConfig_Zero_NoSteps()
        {
            Assert.AreEqual(0f, PlayerMotor.StrideLength(null, FootstepGait.Walk));
            var cadence = new StrideCadence();
            Assert.IsFalse(cadence.Tick(10f, PlayerMotor.StrideLength(null, FootstepGait.Walk)));
        }

        // 걷기 3 m/s·60fps로 10초 → 보폭 1.5m면 첫 걸음(절반) 포함 약 20걸음 (보폭/속도 = 0.5초 간격)
        [Test]
        public void WalkCadence_StepInterval_StrideOverSpeed()
        {
            var config = ScriptableObject.CreateInstance<PlayerConfigSO>();
            try
            {
                var cadence = new StrideCadence();
                const float dt = 1f / 60f;
                const float speed = 3f;
                float stride = PlayerMotor.StrideLength(config, FootstepGait.Walk);
                int steps = 0;
                float lastStep = -1f, interval = 0f;
                for (int i = 1; i <= 600; i++)
                {
                    if (!cadence.Tick(speed * dt, stride))
                        continue;
                    float t = i * dt;
                    if (lastStep >= 0f)
                        interval = t - lastStep;
                    lastStep = t;
                    steps++;
                }
                Assert.AreEqual(stride / speed, interval, dt * 1.01f, "걸음 간격 = 보폭/속도");
                Assert.That(steps, Is.InRange(19, 21));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
