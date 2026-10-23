using Ashfall.Audio;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7B-2c: 적 울음 음량·들리는 거리 (설계 §1.2·§4.9, Q26). 순수 VocalMix.
    public class VocalMixTests
    {
        private AudioConfigSO cfg;

        [SetUp]
        public void SetUp() => cfg = ScriptableObject.CreateInstance<AudioConfigSO>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(cfg);

        [Test]
        public void Defaults_FollowDesign()
        {
            Assert.AreEqual(60f, cfg.vocalAudibleM, 1e-6f, "설계 §4.9 vocalAudibleM 60");
            Assert.IsNotNull(cfg.mutantAlertClips);
            Assert.IsNotNull(cfg.mutantAttackClips);
            Assert.IsEmpty(cfg.mutantAlertClips, "코드 기본 = 빈 배열(에셋은 오디오 메뉴가 채운다)");
            Assert.Less(FootstepMix.PriorityEnemyVocal, FootstepMix.PriorityEnemyFootstep, "적 발소리보다 높다(작을수록 높음)");
            Assert.Greater(FootstepMix.PriorityEnemyVocal, FootstepMix.PriorityGunshot, "총성보다는 낮다");
        }

        [Test]
        public void Evaluate_LinearFalloff_PerKindVolume()
        {
            cfg.vocalAudibleM = 60f;
            cfg.mutantAlertVolume = 1f;
            cfg.mutantAttackVolume = 0.5f;

            Assert.IsTrue(VocalMix.Evaluate(0f, EnemyVocalKind.MutantAlert, cfg, out float g0));
            Assert.AreEqual(1f, g0, 1e-5f);
            Assert.IsTrue(VocalMix.Evaluate(30f, EnemyVocalKind.MutantAlert, cfg, out float g30));
            Assert.AreEqual(0.5f, g30, 1e-5f, "선형 감쇠");
            Assert.IsTrue(VocalMix.Evaluate(30f, EnemyVocalKind.MutantAttack, cfg, out float a30));
            Assert.AreEqual(0.25f, a30, 1e-5f, "종류 음량 × 감쇠");
            Assert.IsTrue(VocalMix.Evaluate(60f, EnemyVocalKind.MutantAlert, cfg, out float g60));
            Assert.AreEqual(0f, g60, 1e-5f, "경계 = 0 (들리는 거리 안)");
            Assert.IsTrue(VocalMix.Evaluate(-5f, EnemyVocalKind.MutantAlert, cfg, out float neg));
            Assert.AreEqual(1f, neg, 1e-5f, "음수 거리 = 0m");
        }

        [Test]
        public void Evaluate_OutOfRange_NaN_ZeroAudible_NullConfig_NotPlayed()
        {
            Assert.IsFalse(VocalMix.Evaluate(60.01f, EnemyVocalKind.MutantAlert, cfg, out float g));
            Assert.AreEqual(0f, g);
            Assert.IsFalse(VocalMix.Evaluate(float.NaN, EnemyVocalKind.MutantAlert, cfg, out _));
            Assert.IsFalse(VocalMix.Evaluate(1f, EnemyVocalKind.MutantAlert, null, out _));
            cfg.vocalAudibleM = 0f;
            Assert.IsFalse(VocalMix.Evaluate(0f, EnemyVocalKind.MutantAlert, cfg, out _));
            cfg.vocalAudibleM = float.NaN;
            Assert.IsFalse(VocalMix.Evaluate(0f, EnemyVocalKind.MutantAlert, cfg, out _));
        }

        [Test]
        public void Evaluate_VolumeClamped01()
        {
            cfg.mutantAlertVolume = 3f;
            Assert.IsTrue(VocalMix.Evaluate(0f, EnemyVocalKind.MutantAlert, cfg, out float g));
            Assert.AreEqual(1f, g, 1e-5f);
            cfg.mutantAlertVolume = -1f;
            Assert.IsTrue(VocalMix.Evaluate(0f, EnemyVocalKind.MutantAlert, cfg, out g));
            Assert.AreEqual(0f, g, 1e-5f);
        }

        [Test]
        public void Clips_ByKind()
        {
            var alert = new AudioClip[1];
            var attack = new AudioClip[2];
            cfg.mutantAlertClips = alert;
            cfg.mutantAttackClips = attack;
            Assert.AreSame(alert, VocalMix.Clips(cfg, EnemyVocalKind.MutantAlert));
            Assert.AreSame(attack, VocalMix.Clips(cfg, EnemyVocalKind.MutantAttack));
            Assert.IsNull(VocalMix.Clips(null, EnemyVocalKind.MutantAlert));
        }
    }
}
