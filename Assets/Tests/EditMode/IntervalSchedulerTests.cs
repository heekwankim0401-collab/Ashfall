using Ashfall.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 2b: 무작위 간격(금속 붕괴 40~120초) + 환경음 음량·배치 (§11 환경음)
    public class IntervalSchedulerTests
    {
        [Test]
        public void Intervals_WithinRange()
        {
            var s = new IntervalScheduler(new Vector2(40f, 120f), new System.Random(1));
            Assert.That(s.Remaining, Is.InRange(40f, 120f));
            const float dt = 0.5f;
            float since = 0f;
            int fired = 0;
            for (int i = 0; i < 20000; i++)
            {
                since += dt;
                if (s.Tick(dt))
                {
                    Assert.That(since, Is.InRange(40f - dt, 120f + dt), $"{fired}번째 간격");
                    Assert.That(s.LastInterval, Is.InRange(40f, 120f));
                    since = 0f;
                    fired++;
                }
            }
            Assert.Greater(fired, 50, "10000초에 약 125번");
        }

        [Test]
        public void InvertedRange_Sorted()
        {
            var s = new IntervalScheduler(new Vector2(120f, 40f), new System.Random(2));
            Assert.AreEqual(40f, s.MinSec);
            Assert.AreEqual(120f, s.MaxSec);
        }

        [Test]
        public void ZeroRange_ClampedToMinimum_OnePerTick()
        {
            var s = new IntervalScheduler(Vector2.zero, new System.Random(3));
            Assert.AreEqual(IntervalScheduler.MinIntervalSec, s.MinSec);
            Assert.IsTrue(s.Tick(10f), "긴 틱에도 한 번만");
            Assert.That(s.Remaining, Is.GreaterThan(0f));
        }

        [Test]
        public void ZeroOrNegativeDt_DoesNothing()
        {
            var s = new IntervalScheduler(new Vector2(1f, 1f), new System.Random(4));
            float before = s.Remaining;
            Assert.IsFalse(s.Tick(0f));
            Assert.IsFalse(s.Tick(-1f));
            Assert.AreEqual(before, s.Remaining);
        }

        [Test]
        public void Restart_PicksNewInterval()
        {
            var s = new IntervalScheduler(new Vector2(40f, 120f), new System.Random(5));
            s.Tick(39f);
            s.Restart();
            Assert.That(s.Remaining, Is.InRange(40f, 120f));
        }

        // ---------- AmbientMix ----------

        [Test]
        public void Ambient_WindAndStormVolumes()
        {
            var cfg = ScriptableObject.CreateInstance<Ashfall.Audio.AudioConfigSO>();
            try
            {
                Assert.AreEqual(0.35f, AmbientMix.WindVolume(cfg, 0f), 1e-6f);
                Assert.AreEqual(0.8f, AmbientMix.WindVolume(cfg, 1f), 1e-6f);
                Assert.AreEqual((0.35f + 0.8f) / 2f, AmbientMix.WindVolume(cfg, 0.5f), 1e-6f);
                Assert.AreEqual(0f, AmbientMix.StormVolume(cfg, 0f));
                Assert.AreEqual(0.7f, AmbientMix.StormVolume(cfg, 1f), 1e-6f);
                Assert.AreEqual(0.7f, AmbientMix.StormVolume(cfg, 3f), 1e-6f, "강도는 0~1로 자른다");
                Assert.AreEqual(0.35f, AmbientMix.WindVolume(cfg, float.NaN), 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void Ambient_StormLoopWanted_RisingPeakFalling()
        {
            Assert.IsFalse(AmbientMix.StormLoopWanted(Ashfall.Core.AshStormPhase.Pending));
            Assert.IsTrue(AmbientMix.StormLoopWanted(Ashfall.Core.AshStormPhase.Rising));
            Assert.IsTrue(AmbientMix.StormLoopWanted(Ashfall.Core.AshStormPhase.Peak));
            Assert.IsTrue(AmbientMix.StormLoopWanted(Ashfall.Core.AshStormPhase.Falling));
            Assert.IsFalse(AmbientMix.StormLoopWanted(Ashfall.Core.AshStormPhase.Over));
        }

        [Test]
        public void Ambient_RandomAround_InDistanceRing()
        {
            var r = new System.Random(11);
            var center = new Vector3(10f, 5f, -3f);
            for (int i = 0; i < 500; i++)
            {
                Vector3 p = AmbientMix.RandomAround(center, new Vector2(150f, 400f), r);
                Assert.AreEqual(center.y, p.y, 1e-4f);
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(center.x, center.z));
                Assert.That(d, Is.InRange(150f - 1e-2f, 400f + 1e-2f));
            }
        }

        // ---------- ClipPicker ----------

        [Test]
        public void ClipPicker_NeverRepeatsPrevious()
        {
            var p = new ClipPicker(new System.Random(1));
            int last = p.Next(3);
            for (int i = 0; i < 300; i++)
            {
                int next = p.Next(3);
                Assert.That(next, Is.InRange(0, 2));
                Assert.AreNotEqual(last, next);
                last = next;
            }
        }

        [Test]
        public void ClipPicker_EdgeCounts()
        {
            var p = new ClipPicker(new System.Random(2));
            Assert.AreEqual(-1, p.Next(0));
            Assert.AreEqual(0, p.Next(1));
            Assert.AreEqual(0, p.Next(1), "1개면 반복 허용");
            int a = p.Next(2);
            Assert.AreEqual(1 - a, p.Next(2), "2개면 번갈아");
        }

        [Test]
        public void ClipPicker_PitchWithinJitter()
        {
            var p = new ClipPicker(new System.Random(3));
            for (int i = 0; i < 200; i++)
                Assert.That(p.Pitch(0.06f), Is.InRange(0.94f, 1.06f));
            Assert.AreEqual(1f, p.Pitch(0f));
            Assert.AreEqual(1f, p.Pitch(float.NaN));
            Assert.That(p.Pitch(5f), Is.InRange(1f - ClipPicker.MaxJitter, 1f + ClipPicker.MaxJitter));
        }
    }
}
