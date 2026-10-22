using Ashfall.Audio;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 2b: 총성 거리 레이어 (§11, Q15) — 근 ≤ 50 / 중 ≤ 250 / 원 ≤ 1200, 구간 선형 게인(연속·단조 감소), 그 너머 안 들림
    public class GunshotMixTests
    {
        private AudioConfigSO cfg;

        [SetUp]
        public void SetUp() => cfg = ScriptableObject.CreateInstance<AudioConfigSO>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(cfg);

        private (bool ok, GunshotLayer layer, float gain) Eval(float d)
        {
            bool ok = GunshotMix.Evaluate(d, cfg, out GunshotLayer layer, out float gain);
            return (ok, layer, gain);
        }

        [Test]
        public void ZeroMeters_NearFullGain()
        {
            var r = Eval(0f);
            Assert.IsTrue(r.ok);
            Assert.AreEqual(GunshotLayer.Near, r.layer);
            Assert.AreEqual(1f, r.gain, 1e-6f);
        }

        [TestCase(30f, GunshotLayer.Near)]
        [TestCase(50f, GunshotLayer.Near)]
        [TestCase(50.01f, GunshotLayer.Mid)]
        [TestCase(100f, GunshotLayer.Mid)]
        [TestCase(250f, GunshotLayer.Mid)]
        [TestCase(250.01f, GunshotLayer.Far)]
        [TestCase(400f, GunshotLayer.Far)]
        [TestCase(1200f, GunshotLayer.Far)]
        public void Boundaries_PickLayer(float d, GunshotLayer expected)
        {
            var r = Eval(d);
            Assert.IsTrue(r.ok);
            Assert.AreEqual(expected, r.layer);
        }

        [Test]
        public void BoundaryGains_MatchConfig()
        {
            Assert.AreEqual(0.7f, Eval(50f).gain, 1e-5f);
            Assert.AreEqual(0.35f, Eval(250f).gain, 1e-5f);
            Assert.AreEqual(0.1f, Eval(1200f).gain, 1e-5f);
            Assert.AreEqual((1f + 0.7f) / 2f, Eval(25f).gain, 1e-5f, "구간 선형");
        }

        [Test]
        public void Gain_ContinuousAcrossBoundaries()
        {
            foreach (float b in new[] { 50f, 250f })
                Assert.AreEqual(Eval(b).gain, Eval(b + 0.001f).gain, 1e-3f, $"{b}m 경계에서 연속");
        }

        [Test]
        public void Gain_MonotonicDecreasing()
        {
            float prev = float.PositiveInfinity;
            for (float d = 0f; d <= 1200f; d += 5f)
            {
                var r = Eval(d);
                Assert.IsTrue(r.ok, $"{d}m");
                Assert.LessOrEqual(r.gain, prev + 1e-6f, $"{d}m에서 게인이 커졌다");
                prev = r.gain;
            }
        }

        [Test]
        public void Beyond_FarMax_NotAudible()
        {
            Assert.IsFalse(Eval(1200.5f).ok);
            Assert.IsFalse(Eval(1500f).ok);
            Assert.IsFalse(Eval(float.NaN).ok);
            Assert.IsFalse(GunshotMix.Evaluate(10f, null, out _, out _));
        }

        [Test]
        public void NegativeDistance_TreatedAsZero()
        {
            var r = Eval(-5f);
            Assert.IsTrue(r.ok);
            Assert.AreEqual(1f, r.gain, 1e-6f);
        }

        [Test]
        public void FindSet_ByClass_FallbackForNullOrMissing()
        {
            var pistol = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            var shotgun = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            try
            {
                pistol.cls = WeaponClass.Pistol;
                shotgun.cls = WeaponClass.Shotgun;
                Assert.AreEqual(WeaponClass.Pistol, GunshotMix.FindSet(cfg, pistol).weaponClass);
                Assert.AreEqual(WeaponClass.AssaultRifle, GunshotMix.FindSet(cfg, (WeaponDefinitionSO)null).weaponClass, "무기 없음 → 대체");
                Assert.AreEqual(WeaponClass.AssaultRifle, GunshotMix.FindSet(cfg, shotgun).weaponClass, "세트 없는 분류 → 대체");
                cfg.fallbackGunshotClass = WeaponClass.Smg;
                Assert.AreEqual(WeaponClass.Smg, GunshotMix.FindSet(cfg, shotgun).weaponClass);
            }
            finally
            {
                Object.DestroyImmediate(pistol);
                Object.DestroyImmediate(shotgun);
            }
        }

        [Test]
        public void Clip_ByLayer_AndFallbackWhenEmpty()
        {
            var near = AudioClip.Create("near", 10, 1, 22050, false);
            var mid = AudioClip.Create("mid", 10, 1, 22050, false);
            var far = AudioClip.Create("far", 10, 1, 22050, false);
            var rifleMid = AudioClip.Create("rifleMid", 10, 1, 22050, false);
            try
            {
                var set = new GunshotSet(WeaponClass.Pistol, 1f) { near = near, mid = mid, far = far };
                Assert.AreSame(near, GunshotMix.Clip(set, GunshotLayer.Near));
                Assert.AreSame(mid, GunshotMix.Clip(set, GunshotLayer.Mid));
                Assert.AreSame(far, GunshotMix.Clip(set, GunshotLayer.Far));
                Assert.IsNull(GunshotMix.Clip(null, GunshotLayer.Near));

                GunshotMix.FindSet(cfg, WeaponClass.AssaultRifle).mid = rifleMid;
                var partial = new GunshotSet(WeaponClass.Smg, 1f);
                Assert.AreSame(rifleMid, GunshotMix.ClipOrFallback(cfg, partial, GunshotLayer.Mid), "빈 레이어 → 대체 세트");
            }
            finally
            {
                Object.DestroyImmediate(near);
                Object.DestroyImmediate(mid);
                Object.DestroyImmediate(far);
                Object.DestroyImmediate(rifleMid);
            }
        }
    }
}
