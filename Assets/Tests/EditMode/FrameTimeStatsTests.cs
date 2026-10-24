using System;
using Ashfall.Core;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M7 7D 개발 빌드 진단 (M7 설계 §1.9, Q51): 프레임 시간 링 버퍼 — 중앙값·99%·최대·fps 추정, 덮어쓰기, 할당 0.
    public class FrameTimeStatsTests
    {
        [Test]
        public void Empty_SummaryIsZero()
        {
            var stats = new FrameTimeStats(8);
            FrameTimeSummary s = stats.Compute();
            Assert.AreEqual(0, s.Count);
            Assert.AreEqual(0f, s.MedianMs);
            Assert.AreEqual(0f, s.P95Ms);
            Assert.AreEqual(0f, s.P99Ms);
            Assert.AreEqual(0f, s.MaxMs);
            Assert.AreEqual(0f, s.Fps);
        }

        [Test]
        public void Capacity_MustBePositive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameTimeStats(0));
        }

        [Test]
        public void Median_OddAndEvenCounts()
        {
            var stats = new FrameTimeStats(16);
            foreach (float v in new[] { 30f, 10f, 20f })
                stats.Add(v);
            Assert.AreEqual(20f, stats.Compute().MedianMs, 1e-5f);

            stats.Add(40f);   // 10 20 30 40 → (20+30)/2
            Assert.AreEqual(25f, stats.Compute().MedianMs, 1e-5f);
        }

        [Test]
        public void P99_NearestRank_MaxAndFps()
        {
            // 1..100ms: 99% = 99번째 = 99ms, 최대 100ms, 평균 50.5ms → fps 1000/50.5
            var stats = new FrameTimeStats(128);
            for (int i = 100; i >= 1; i--)
                stats.Add(i);
            FrameTimeSummary s = stats.Compute();
            Assert.AreEqual(100, s.Count);
            Assert.AreEqual(50.5f, s.MedianMs, 1e-4f);
            Assert.AreEqual(95f, s.P95Ms, 1e-5f);
            Assert.AreEqual(99f, s.P99Ms, 1e-5f);
            Assert.AreEqual(100f, s.MaxMs, 1e-5f);
            Assert.AreEqual(50.5f, s.MeanMs, 1e-3f);
            Assert.AreEqual(1000f / 50.5f, s.Fps, 1e-2f);
        }

        [Test]
        public void P99_SmallCount_IsMax()
        {
            // n = 10 → ceil(9.9) = 10번째 = 최대
            var stats = new FrameTimeStats(16);
            for (int i = 1; i <= 10; i++)
                stats.Add(i);
            Assert.AreEqual(10f, stats.Compute().P99Ms, 1e-5f);
        }

        [Test]
        public void SteadyFrames_Fps60()
        {
            var stats = new FrameTimeStats(600);
            for (int i = 0; i < 600; i++)
                stats.Add(1000f / 60f);
            FrameTimeSummary s = stats.Compute();
            Assert.AreEqual(60f, s.Fps, 0.01f);
            Assert.AreEqual(1000f / 60f, s.MedianMs, 1e-3f);
        }

        [Test]
        public void RingBuffer_OverwritesOldest()
        {
            var stats = new FrameTimeStats(4);
            foreach (float v in new[] { 100f, 200f, 1f, 2f, 3f, 4f })
                stats.Add(v);
            FrameTimeSummary s = stats.Compute();
            Assert.AreEqual(4, stats.Count);
            Assert.AreEqual(6, stats.TotalAdded);
            Assert.AreEqual(4, s.Count);
            Assert.AreEqual(4f, s.MaxMs, 1e-5f, "덮어쓴 100·200ms가 남으면 안 된다");
            Assert.AreEqual(2.5f, s.MedianMs, 1e-5f);
        }

        [Test]
        public void Compute_DoesNotReorderRing()
        {
            // 정렬은 작업 배열에서만 — 이어서 덮어쓰는 순서가 그대로여야 한다
            var stats = new FrameTimeStats(3);
            stats.Add(30f);
            stats.Add(10f);
            stats.Add(20f);
            stats.Compute();
            stats.Add(5f);   // 가장 오래된 30을 덮어씀 → 5 10 20
            Assert.AreEqual(20f, stats.Compute().MaxMs, 1e-5f);
        }

        [Test]
        public void InvalidSamples_Ignored()
        {
            var stats = new FrameTimeStats(4);
            stats.Add(-1f);
            stats.Add(float.NaN);
            stats.Add(5f);
            Assert.AreEqual(1, stats.Count);
        }

        [Test]
        public void Clear_Resets()
        {
            var stats = new FrameTimeStats(4);
            stats.Add(5f);
            stats.Add(6f);
            stats.Clear();
            Assert.AreEqual(0, stats.Count);
            Assert.AreEqual(0, stats.TotalAdded);
            stats.Add(7f);
            Assert.AreEqual(7f, stats.Compute().MaxMs, 1e-5f);
        }

        [Test]
        public void AddComputeClear_NoAllocation()
        {
            var stats = new FrameTimeStats(DevDiagnostics.FrameSampleCapacity);
            float t = 0f;
            TestDelegate frame = () =>
            {
                for (int i = 0; i < 5000; i++)
                {
                    t += 0.37f;
                    stats.Add(8f + (t % 13f));
                }
                stats.Compute();
                stats.Clear();
            };
            AiTest.AssertNoAlloc(frame);   // 데우기 + 재시도(테스트 러너 잡음)
        }
    }
}
