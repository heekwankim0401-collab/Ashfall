using Ashfall.Weapons;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M7 7A-2a §5.1 고장 해제 = T 길게 (Q9: 1.2초, 떼면 처음부터)
    public class JamClearModelTests
    {
        private const float HoldSec = 1.2f;

        [Test]
        public void HoldFullTime_Clears_Once()
        {
            var m = new JamClearModel(HoldSec);
            Assert.IsFalse(m.Tick(0.6f, true, true));
            Assert.AreEqual(0.5f, m.Progress, 1e-4f);
            Assert.IsTrue(m.IsHolding);
            Assert.IsFalse(m.Tick(0.5f, true, true), "1.1초 — 아직");
            Assert.IsTrue(m.Tick(0.11f, true, true), "1.21초 — 해제");
            Assert.AreEqual(0f, m.Progress, "해제 뒤 처음으로");
            Assert.IsFalse(m.IsHolding);
        }

        [Test]
        public void Release_ResetsProgress()
        {
            var m = new JamClearModel(HoldSec);
            m.Tick(1.0f, true, true);
            Assert.IsFalse(m.Tick(0.016f, false, true), "뗌");
            Assert.AreEqual(0f, m.Progress, "떼면 처음부터");
            Assert.IsFalse(m.Tick(0.5f, true, true));
            Assert.IsFalse(m.Tick(0.5f, true, true), "다시 1.0초 — 아직");
            Assert.IsTrue(m.Tick(0.25f, true, true));
        }

        [Test]
        public void ShortTap_NeverClears()
        {
            var m = new JamClearModel(HoldSec);
            for (int i = 0; i < 20; i++)
            {
                Assert.IsFalse(m.Tick(0.1f, true, true), "짧게 누름");
                Assert.IsFalse(m.Tick(0.1f, false, true), "뗌");
            }
        }

        [Test]
        public void NotJammed_NoProgress()
        {
            var m = new JamClearModel(HoldSec);
            Assert.IsFalse(m.Tick(5f, true, false));
            Assert.AreEqual(0f, m.Progress);
        }

        [Test]
        public void Reset_DiscardsProgress()
        {
            var m = new JamClearModel(HoldSec);
            m.Tick(1.1f, true, true);
            m.Reset();
            Assert.AreEqual(0f, m.Progress);
            Assert.IsFalse(m.Tick(0.2f, true, true), "교체·화면 열림 뒤 처음부터");
        }

        [Test]
        public void SingleLargeTick_Clears()
        {
            var m = new JamClearModel(HoldSec);
            Assert.IsTrue(m.Tick(2f, true, true));
        }

        [Test]
        public void NegativeDt_Ignored()
        {
            var m = new JamClearModel(HoldSec);
            m.Tick(0.5f, true, true);
            m.Tick(-10f, true, true);
            Assert.AreEqual(0.5f / HoldSec, m.Progress, 1e-4f);
        }

        [Test]
        public void ZeroHold_ClearsOnFirstHeldTick()
        {
            var m = new JamClearModel(0f);
            Assert.IsTrue(m.Tick(0f, true, true));
            Assert.AreEqual(0f, m.Progress);
        }

        [Test]
        public void HandlingDefault_Is1_2Seconds()
        {
            var handling = M7TestData.Handling();
            Assert.AreEqual(1.2f, handling.jamClearHoldSec, 1e-6f, "TODO(GDD-?) Q9 임시값");
            UnityEngine.Object.DestroyImmediate(handling);
        }
    }
}
