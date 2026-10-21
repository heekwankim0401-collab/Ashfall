using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M4 AC2 — §8.2 탈출 대기 (ExtractionModel, M4 설계 §3.1 Q8)
    public class ExtractionModelTests
    {
        private const float Frame = 0.1f;
        private RaidConfigSO config;

        [SetUp]
        public void SetUp() => config = M4TestData.RaidConfig();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        private ExtractionModel Model(string id, bool active = true) =>
            new ExtractionModel(M4TestData.Find(config, id), Vector3.zero, active);

        // 구역 안·조건 충족으로 frame씩 진행. 완료까지 걸린 틱 수 (최대 maxTicks)
        private static int TicksToComplete(ExtractionModel model, float frame, int maxTicks = 10000)
        {
            for (int i = 1; i <= maxTicks; i++)
                if (model.Tick(frame, true, ExtractionBlock.None))
                    return i;
            return -1;
        }

        [TestCase(M4TestData.Ext01, 10f)]
        [TestCase(M4TestData.Ext02, 15f)]
        [TestCase(M4TestData.Ext03, 20f)]
        [TestCase(M4TestData.Ext04, 7f)]
        public void CompletesExactlyAtWaitTime(string id, float waitSec)
        {
            ExtractionModel model = Model(id);
            Assert.AreEqual(waitSec, model.RemainingSec, 1e-4f, "시작 = §8.2 소요");

            int ticks = TicksToComplete(model, Frame);

            Assert.AreEqual(Mathf.RoundToInt(waitSec / Frame), ticks, $"{waitSec}초에 정확히 완료 (0.1초 틱)");
            Assert.AreEqual(ExtractionPhase.Completed, model.Phase);
            Assert.AreEqual(0f, model.RemainingSec);
        }

        [Test]
        public void NotCompleteOneTickBefore()
        {
            ExtractionModel model = Model(M4TestData.Ext04);
            for (int i = 0; i < 69; i++)
                Assert.IsFalse(model.Tick(Frame, true, ExtractionBlock.None), $"틱 {i}");
            Assert.AreEqual(ExtractionPhase.CountingDown, model.Phase);
            Assert.AreEqual(0.1f, model.RemainingSec, 1e-3f);
            Assert.IsTrue(model.Tick(Frame, true, ExtractionBlock.None), "7.0초");
        }

        [Test]
        public void LeavingZone_ResetsToFullWait()
        {
            ExtractionModel model = Model(M4TestData.Ext01);
            for (int i = 0; i < 50; i++)
                model.Tick(Frame, true, ExtractionBlock.None);
            Assert.AreEqual(5f, model.RemainingSec, 1e-3f);

            Assert.IsFalse(model.Tick(Frame, false, ExtractionBlock.None));
            Assert.AreEqual(ExtractionPhase.Idle, model.Phase);
            Assert.AreEqual(10f, model.RemainingSec, "구역을 나가면 처음부터 (Q8)");

            Assert.AreEqual(100, TicksToComplete(model, Frame), "다시 10초 전부");
        }

        [Test]
        public void ConditionBroken_ResetsAndReportsBlock()
        {
            ExtractionModel model = Model(M4TestData.Ext04);
            for (int i = 0; i < 30; i++)
                model.Tick(Frame, true, ExtractionBlock.None);

            Assert.IsFalse(model.Tick(Frame, true, ExtractionBlock.BackpackEquipped), "대기 중 배낭 장착");
            Assert.AreEqual(ExtractionPhase.Blocked, model.Phase);
            Assert.AreEqual(ExtractionBlock.BackpackEquipped, model.Block);
            Assert.AreEqual(7f, model.RemainingSec);

            model.Tick(Frame, true, ExtractionBlock.None);
            Assert.AreEqual(ExtractionPhase.CountingDown, model.Phase);
            Assert.AreEqual(ExtractionBlock.None, model.Block, "조건이 풀리면 Block 해제");
        }

        [Test]
        public void Blocked_NeverCountsDown()
        {
            ExtractionModel model = Model(M4TestData.Ext02);
            for (int i = 0; i < 1000; i++)
                Assert.IsFalse(model.Tick(Frame, true, ExtractionBlock.NoPower));
            Assert.AreEqual(15f, model.RemainingSec);
        }

        [Test]
        public void Inactive_AlwaysInactive_NeverCompletes()
        {
            ExtractionModel model = Model(M4TestData.Ext01, active: false);
            Assert.AreEqual(ExtractionPhase.Inactive, model.Phase);
            Assert.IsFalse(model.IsActive);
            for (int i = 0; i < 1000; i++)
                Assert.IsFalse(model.Tick(Frame, true, ExtractionBlock.None));
            Assert.AreEqual(ExtractionPhase.Inactive, model.Phase, "Q4: 비활성 탈출구는 작동하지 않는다");
            Assert.AreEqual(ExtractionBlock.None, model.Block);
        }

        [Test]
        public void AfterCompleted_IgnoresTicks()
        {
            ExtractionModel model = Model(M4TestData.Ext04);
            Assert.IsTrue(model.Tick(7f, true, ExtractionBlock.None), "긴 프레임 한 번에 완료");
            Assert.IsFalse(model.Tick(Frame, false, ExtractionBlock.None), "완료 후 이탈 무시");
            Assert.IsFalse(model.Tick(Frame, true, ExtractionBlock.None), "완료는 한 번만 true");
            Assert.AreEqual(ExtractionPhase.Completed, model.Phase);
        }

        [Test]
        public void Interrupt_AfterCompleted_RestartsWithBlock()
        {
            ExtractionModel model = Model(M4TestData.Ext03);
            Assert.IsTrue(model.Tick(20f, true, ExtractionBlock.None));

            model.Interrupt(ExtractionBlock.MissingItem);

            Assert.AreEqual(ExtractionPhase.Blocked, model.Phase);
            Assert.AreEqual(ExtractionBlock.MissingItem, model.Block);
            Assert.AreEqual(20f, model.RemainingSec);
        }

        [Test]
        public void IdAndPosition_FromConstructor()
        {
            var model = new ExtractionModel(M4TestData.Find(config, M4TestData.Ext02), new Vector3(470f, 3f, -250f), true);
            Assert.AreEqual(M4TestData.Ext02, model.Id);
            Assert.AreEqual(new Vector3(470f, 3f, -250f), model.Position);
            Assert.AreEqual(ExtractionPhase.Idle, model.Phase);
        }
    }
}
