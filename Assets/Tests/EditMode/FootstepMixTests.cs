using Ashfall.Audio;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 2b: 발소리 음량·들리는 거리 (§11, 설계 §1.4·§3.5)
    public class FootstepMixTests
    {
        private AudioConfigSO cfg;

        [SetUp]
        public void SetUp() => cfg = ScriptableObject.CreateInstance<AudioConfigSO>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(cfg);

        [TestCase(FootstepGait.Walk, 0.7f, 25f)]
        [TestCase(FootstepGait.Sprint, 1.0f, 40f)]
        [TestCase(FootstepGait.Crouch, 0.35f, 10f)]
        [TestCase(FootstepGait.Prone, 0.2f, 5f)]
        public void Gait_VolumeAtZero_AndAudibleLimit(FootstepGait gait, float volume, float audible)
        {
            Assert.IsTrue(FootstepMix.Evaluate(0f, gait, SoundSource.Enemy, cfg, out float g0));
            Assert.AreEqual(volume, g0, 1e-5f);
            Assert.IsTrue(FootstepMix.Evaluate(audible, gait, SoundSource.Enemy, cfg, out float gEdge), "경계는 포함");
            Assert.AreEqual(0f, gEdge, 1e-5f);
            Assert.IsFalse(FootstepMix.Evaluate(audible + 0.01f, gait, SoundSource.Enemy, cfg, out float gOut), "들리는 거리 밖");
            Assert.AreEqual(0f, gOut);
        }

        [Test]
        public void Gain_QuadraticFalloff()
        {
            FootstepMix.Evaluate(12.5f, FootstepGait.Walk, SoundSource.Enemy, cfg, out float g);
            Assert.AreEqual(0.7f * 0.25f, g, 1e-5f, "(1 − 12.5/25)² = 0.25");
        }

        [Test]
        public void PlayerSelf_ScaledByPlayerSelfVolume()
        {
            FootstepMix.Evaluate(1.6f, FootstepGait.Walk, SoundSource.Enemy, cfg, out float enemy);
            FootstepMix.Evaluate(1.6f, FootstepGait.Walk, SoundSource.Player, cfg, out float player);
            Assert.AreEqual(enemy * 0.6f, player, 1e-6f);
        }

        [Test]
        public void Invalid_NotAudible()
        {
            Assert.IsFalse(FootstepMix.Evaluate(float.NaN, FootstepGait.Walk, SoundSource.Enemy, cfg, out _));
            Assert.IsFalse(FootstepMix.Evaluate(1f, FootstepGait.Walk, SoundSource.Enemy, null, out _));
            cfg.gaitAudibleM = new GaitValues(0f, 40f, 10f, 5f);
            Assert.IsFalse(FootstepMix.Evaluate(0f, FootstepGait.Walk, SoundSource.Enemy, cfg, out _), "들리는 거리 0 = 안 들림");
        }

        [Test]
        public void Priority_PlayerLowestEnemyAboveIt_GunshotHighest()
        {
            Assert.AreEqual(FootstepMix.PriorityPlayerFootstep, FootstepMix.Priority(SoundSource.Player));
            Assert.AreEqual(FootstepMix.PriorityEnemyFootstep, FootstepMix.Priority(SoundSource.Enemy));
            Assert.Less(FootstepMix.PriorityGunshot, FootstepMix.PriorityCollapse);
            Assert.Less(FootstepMix.PriorityCollapse, FootstepMix.PriorityEnemyFootstep);
            Assert.Less(FootstepMix.PriorityEnemyFootstep, FootstepMix.PriorityPlayerFootstep);
        }

        [Test]
        public void FindSurface_BySurfaceKind()
        {
            foreach (SurfaceKind k in new[] { SurfaceKind.Dirt, SurfaceKind.Metal, SurfaceKind.Debris, SurfaceKind.Water })
                Assert.AreEqual(k, FootstepMix.FindSurface(cfg, k).surface);
            cfg.footsteps = new[] { new SurfaceFootsteps(SurfaceKind.Metal, 1f) };
            Assert.IsNull(FootstepMix.FindSurface(cfg, SurfaceKind.Dirt));
        }
    }
}
