using Ashfall.Audio;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M6 2b: voice 배정 (설계 §1.4) — 빈 칸 → 같거나 낮은 우선순위 중 가장 오래된 것 빼앗기 → 없으면 −1
    public class VoiceAllocatorTests
    {
        private const int Gun = FootstepMix.PriorityGunshot;
        private const int Collapse = FootstepMix.PriorityCollapse;
        private const int Enemy = FootstepMix.PriorityEnemyFootstep;
        private const int Self = FootstepMix.PriorityPlayerFootstep;

        [Test]
        public void FreeSlots_UsedInOrder()
        {
            var a = new VoiceAllocator(3);
            Assert.AreEqual(0, a.Acquire(Enemy, 0f, 1f));
            Assert.AreEqual(1, a.Acquire(Enemy, 0f, 1f));
            Assert.AreEqual(2, a.Acquire(Enemy, 0f, 1f));
            Assert.IsFalse(a.LastAcquireStole);
            Assert.AreEqual(3, a.BusyCount(0.5f));
        }

        [Test]
        public void FinishedVoice_IsFreeAgain()
        {
            var a = new VoiceAllocator(1);
            a.Acquire(Gun, 0f, 1f);
            Assert.IsTrue(a.IsBusy(0, 0.99f));
            Assert.IsFalse(a.IsBusy(0, 1f), "끝나는 시각에 비어 있다");
            Assert.AreEqual(0, a.Acquire(Self, 1.5f, 1f));
            Assert.IsFalse(a.LastAcquireStole);
        }

        [Test]
        public void Release_FreesSlot()
        {
            var a = new VoiceAllocator(1);
            a.Acquire(Gun, 0f, 10f);
            a.Release(0);
            Assert.IsFalse(a.IsBusy(0, 1f));
            Assert.AreEqual(0, a.Acquire(Self, 1f, 1f));
        }

        [Test]
        public void Full_StealsOldestOfLowerOrEqualPriority()
        {
            var a = new VoiceAllocator(3);
            a.Acquire(Self, 0f, 10f);    // 0: 내 발소리, 가장 오래됨
            a.Acquire(Enemy, 1f, 10f);   // 1
            a.Acquire(Self, 2f, 10f);    // 2
            Assert.AreEqual(0, a.Acquire(Enemy, 3f, 10f), "같거나 낮은 우선순위 중 가장 오래된 칸");
            Assert.IsTrue(a.LastAcquireStole);
            Assert.AreEqual(Enemy, a.PriorityOf(0));

            // 이제 0(적 t3), 1(적 t1), 2(나 t2). 총성은 모두보다 높음 → 가장 오래된 1
            Assert.AreEqual(1, a.Acquire(Gun, 4f, 10f));
        }

        [Test]
        public void Full_OnlyHigherPriority_ReturnsMinusOne()
        {
            var a = new VoiceAllocator(2);
            a.Acquire(Gun, 0f, 10f);
            a.Acquire(Collapse, 0f, 10f);
            Assert.AreEqual(-1, a.Acquire(Enemy, 1f, 1f), "모두 더 높은 우선순위");
            Assert.IsFalse(a.LastAcquireStole);
            Assert.AreEqual(-1, a.Acquire(Self, 1f, 1f));
        }

        [Test]
        public void Full_SamePriority_StealsOldest()
        {
            var a = new VoiceAllocator(2);
            a.Acquire(Gun, 0f, 10f);
            a.Acquire(Gun, 1f, 10f);
            Assert.AreEqual(0, a.Acquire(Gun, 2f, 10f));
            Assert.AreEqual(1, a.Acquire(Gun, 3f, 10f));
        }

        [Test]
        public void ZeroCount_AlwaysMinusOne()
        {
            var a = new VoiceAllocator(0);
            Assert.AreEqual(-1, a.Acquire(Gun, 0f, 1f));
            Assert.IsFalse(a.IsBusy(0, 0f));
        }
    }
}
