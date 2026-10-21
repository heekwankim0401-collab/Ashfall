using Ashfall.Core;
using Ashfall.Player;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M3 청각 입력 (§4.1 소음 반경 → NoiseEmitted): 발소리 펄스 간격, 자세별 반경, 사격 소음, AI용 자세 변환
    public class NoiseEmitTests
    {
        private PlayerConfigSO config;

        [SetUp]
        public void SetUp()
        {
            config = TestData.Player();
            config.noisePulseIntervalSec = 0.5f;   // 설계 §4.5 임시값
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        // ── FootstepNoiseModel ──

        [Test]
        public void ConfigDefault_PulseInterval_IsHalfSecond()
        {
            var fresh = ScriptableObject.CreateInstance<PlayerConfigSO>();
            Assert.AreEqual(0.5f, fresh.noisePulseIntervalSec, 1e-6f);
            Object.DestroyImmediate(fresh);
        }

        [Test]
        public void Moving_FirstTickPulsesImmediately()
        {
            var model = new FootstepNoiseModel(config);
            Assert.IsTrue(model.Tick(0.016f, 8f), "움직이기 시작한 첫 틱에 바로 낸다");
        }

        [Test]
        public void Moving_PulsesEveryHalfSecond()
        {
            var model = new FootstepNoiseModel(config);
            const float dt = 0.1f;
            int pulses = 0;
            int lastPulseTick = -1;
            for (int tick = 0; tick < 31; tick++)   // 0 ~ 3.0초
            {
                if (!model.Tick(dt, 8f))
                    continue;
                if (lastPulseTick >= 0)
                    Assert.AreEqual(5, tick - lastPulseTick, "0.5초(0.1 × 5) 간격");
                lastPulseTick = tick;
                pulses++;
            }
            Assert.AreEqual(7, pulses, "0, 0.5, ..., 3.0초");
        }

        [Test]
        public void Moving_NoPulseBeforeInterval()
        {
            var model = new FootstepNoiseModel(config);
            model.Tick(0.1f, 20f);
            Assert.IsFalse(model.Tick(0.2f, 20f));
            Assert.IsFalse(model.Tick(0.29f, 20f), "누적 0.49초");
            Assert.IsTrue(model.Tick(0.01f, 20f), "누적 0.5초");
        }

        [Test]
        public void Stationary_NeverPulses()
        {
            var model = new FootstepNoiseModel(config);
            for (int i = 0; i < 100; i++)
                Assert.IsFalse(model.Tick(0.1f, 0f), "정지(반경 0) 시 소음 없음");
        }

        [Test]
        public void StopThenMove_PulsesImmediatelyAgain()
        {
            var model = new FootstepNoiseModel(config);
            Assert.IsTrue(model.Tick(0.1f, 8f));
            Assert.IsFalse(model.Tick(0.1f, 8f));
            Assert.IsFalse(model.Tick(0.1f, 0f), "멈춤 → 누적 초기화");
            Assert.IsTrue(model.Tick(0.1f, 8f), "다시 움직이면 첫 펄스는 곧바로");
        }

        [Test]
        public void LongFrame_PulsesOnlyOnce()
        {
            var model = new FootstepNoiseModel(config);
            model.Tick(0.1f, 8f);
            Assert.IsTrue(model.Tick(2.0f, 8f), "긴 프레임");
            Assert.IsFalse(model.Tick(0.1f, 8f), "밀린 펄스를 몰아 내지 않는다");
        }

        // ── 자세별 반경 (§4.1) ──

        [Test]
        public void NoiseRadius_PerStance_MatchesGdd()
        {
            var loco = new LocomotionModel(config);
            Assert.AreEqual(8f, loco.NoiseRadius(true, false), "걷기 8");
            Assert.AreEqual(20f, loco.NoiseRadius(true, true), "질주 20");
            Assert.AreEqual(0f, loco.NoiseRadius(false, false), "정지 0");
            loco.ToggleCrouch();
            Assert.AreEqual(3f, loco.NoiseRadius(true, false), "웅크림 3");
            loco.ToggleProne();
            Assert.AreEqual(1f, loco.NoiseRadius(true, false), "엎드림 1");
            Assert.AreEqual(15f, loco.JumpNoise, "점프 15");
        }

        [Test]
        public void Posture_MapsStanceToTargetPosture()
        {
            var loco = new LocomotionModel(config);
            Assert.AreEqual(TargetPosture.Standing, loco.Posture);
            loco.ToggleCrouch();
            Assert.AreEqual(TargetPosture.Crouching, loco.Posture);
            loco.ToggleProne();
            Assert.AreEqual(TargetPosture.Prone, loco.Posture);
            loco.ToggleProne();
            Assert.AreEqual(TargetPosture.Standing, loco.Posture);
        }

        // ── 사격 소음 ──

        [Test]
        public void ShotNoise_UsesWeaponRadius_AsGunshot()
        {
            var rifle = M3TestData.RifleWithDamage();
            var pos = new Vector3(1f, 2f, 3f);
            Assert.IsTrue(WeaponNoise.TryCreateShot(rifle, pos, out NoiseEmitted noise));
            Assert.AreEqual(NoiseKind.Gunshot, noise.Kind);
            Assert.AreEqual(rifle.shotNoiseRadiusM, noise.RadiusM);
            Assert.AreEqual(120f, noise.RadiusM, "소총 임시값 120");
            Assert.AreEqual(pos, noise.Position);
            Object.DestroyImmediate(rifle);
        }

        [Test]
        public void ShotNoise_ZeroRadiusOrNullWeapon_NoNoise()
        {
            var silent = TestData.Rifle();
            silent.shotNoiseRadiusM = 0f;
            Assert.IsFalse(WeaponNoise.TryCreateShot(silent, Vector3.zero, out _), "0 = 소음 없음");
            Assert.IsFalse(WeaponNoise.TryCreateShot(null, Vector3.zero, out _));
            Object.DestroyImmediate(silent);
        }
    }
}
