using Ashfall.AI;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 감각 갱신 분할 (M3 설계 §1.8, M3 AC3)
    public class AiSchedulerTests
    {
        [Test]
        public void Budget_OverflowGoesToNextFrame_OldestFirst()
        {
            var s = new AiTickScheduler(4);   // 용량보다 많이 넣어도 늘어난다
            for (int i = 0; i < 12; i++)
                s.Add(1f - i * 0.01f);         // 나중에 넣은 것일수록 더 오래 기다림
            Assert.AreEqual(12, s.ActiveCount);

            var outSlots = new int[8];
            int n = s.CollectDue(1f, 8, outSlots);
            Assert.AreEqual(8, n);
            for (int i = 0; i < n; i++)
            {
                Assert.AreEqual(11 - i, outSlots[i], "기한 오름차순 = 오래 기다린 순");
                s.Reschedule(outSlots[i], 1.2f);
            }

            n = s.CollectDue(1.016f, 8, outSlots);
            Assert.AreEqual(4, n, "남은 4개가 다음 프레임에");
            Assert.AreEqual(new[] { 3, 2, 1, 0 }, new[] { outSlots[0], outSlots[1], outSlots[2], outSlots[3] });
        }

        [Test]
        public void NotDue_NotCollected()
        {
            var s = new AiTickScheduler(4);
            s.Add(5f);
            var outSlots = new int[8];
            Assert.AreEqual(0, s.CollectDue(4.99f, 8, outSlots));
            Assert.AreEqual(1, s.CollectDue(5f, 8, outSlots));
        }

        [Test]
        public void Remove_NotCollected_SlotReused()
        {
            var s = new AiTickScheduler(4);
            int a = s.Add(0f);
            int b = s.Add(0f);
            s.Remove(a);
            var outSlots = new int[8];
            Assert.AreEqual(1, s.CollectDue(1f, 8, outSlots));
            Assert.AreEqual(b, outSlots[0]);
            Assert.AreEqual(a, s.Add(0f), "빈 슬롯 재사용");
            s.Remove(a);
            s.Remove(a);   // 두 번 지워도 안전
            Assert.AreEqual(1, s.ActiveCount);
        }

        [Test]
        public void Advance_KeepsPhase_ResetsWhenFarBehind()
        {
            var s = new AiTickScheduler(4);
            int slot = s.Add(0.05f);
            s.Advance(slot, 0.2f, 0.06f);          // 한 프레임 늦게 처리돼도 위상 유지
            Assert.AreEqual(0.25f, s.DueTime(slot), 1e-5f);
            s.Advance(slot, 0.2f, 1f);             // 한 주기 넘게 밀림 → now + period
            Assert.AreEqual(1.2f, s.DueTime(slot), 1e-5f);
        }

        [Test]
        public void PhaseOffset_InPeriod()
        {
            for (int i = 0; i < 100; i++)
            {
                float o = AiTickScheduler.PhaseOffset(i, 0.2f);
                Assert.GreaterOrEqual(o, 0f);
                Assert.Less(o, 0.2f);
            }
        }

        [Test]
        public void TwentyAgents_RelaxedPeriod_SpreadEvenlyAcrossFrames()
        {
            const int agents = 20;
            const float period = 0.2f;
            const float dt = 1f / 60f;
            var s = new AiTickScheduler(32);
            for (int i = 0; i < agents; i++)
                s.Add(AiTickScheduler.PhaseOffset(i, period));

            var outSlots = new int[8];
            int maxPerFrame = 0;
            int total = 0;
            var lastTick = new float[agents];
            for (int i = 0; i < agents; i++)
                lastTick[i] = -1f;
            float worstGap = 0f;

            const int frames = 240;   // 4초
            for (int f = 0; f < frames; f++)
            {
                float now = f * dt;
                int n = s.CollectDue(now, 8, outSlots);
                maxPerFrame = Mathf.Max(maxPerFrame, n);
                total += n;
                for (int k = 0; k < n; k++)
                {
                    int slot = outSlots[k];
                    if (lastTick[slot] >= 0f)
                        worstGap = Mathf.Max(worstGap, now - lastTick[slot]);
                    lastTick[slot] = now;
                    s.Advance(slot, period, now);
                }
            }

            // 평균 20 / 12프레임 ≈ 1.7명/프레임. 고르게 퍼져 한 프레임에 몰리지 않는다.
            Assert.LessOrEqual(maxPerFrame, 4);
            Assert.That(total, Is.InRange(agents * 4f / (period + dt) - agents, agents * 4f / period + agents));
            Assert.LessOrEqual(worstGap, period + 2f * dt, "예산(8) 안이면 주기 + 1프레임 안에 다시 갱신");
        }
    }
}
