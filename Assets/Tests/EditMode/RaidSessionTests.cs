using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M4 AC2·AC3 — 레이드 한 판 (RaidSession, M4 설계 §1.2): RULE-02 타이머·MIA, §8.2 탈출, 사망·포기, 한 번만 종료
    public class RaidSessionTests
    {
        private const float Frame = 0.1f;

        // 탈출 구역 중심 (CrimsonBasinLayout 좌표와 비슷한 임의 값)
        private static readonly Vector3 Ext01Pos = new Vector3(0f, 10f, -470f);
        private static readonly Vector3 Ext02Pos = new Vector3(470f, 10f, -250f);
        private static readonly Vector3 Ext03Pos = new Vector3(60f, 10f, 460f);
        private static readonly Vector3 Ext04Pos = new Vector3(-470f, 10f, 200f);
        private static readonly Vector3 Away = new Vector3(0f, 10f, 0f);

        private RaidConfigSO config;
        private ItemDefinitionSO flare;
        private FakeInventoryService inventory;
        private FakeGridView pockets;
        private int endedCount;

        [SetUp]
        public void SetUp()
        {
            flare = M4TestData.Flare();
            config = M4TestData.RaidConfig(flare);
            inventory = new FakeInventoryService();
            pockets = new FakeGridView(new GridRef(GridKind.Pocket), new Vector2Int(4, 4));
            inventory.Grids.Add(pockets);
            endedCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(flare);
        }

        // 활성 id만 켠 세션 (기본: 4개 모두 활성)
        private RaidSession Session(params string[] active)
        {
            var set = new HashSet<string>(active.Length > 0
                ? active
                : new[] { M4TestData.Ext01, M4TestData.Ext02, M4TestData.Ext03, M4TestData.Ext04 });
            var models = new List<ExtractionModel>
            {
                new ExtractionModel(M4TestData.Find(config, M4TestData.Ext01), Ext01Pos, set.Contains(M4TestData.Ext01)),
                new ExtractionModel(M4TestData.Find(config, M4TestData.Ext02), Ext02Pos, set.Contains(M4TestData.Ext02)),
                new ExtractionModel(M4TestData.Find(config, M4TestData.Ext03), Ext03Pos, set.Contains(M4TestData.Ext03)),
                new ExtractionModel(M4TestData.Find(config, M4TestData.Ext04), Ext04Pos, set.Contains(M4TestData.Ext04)),
            };
            var session = new RaidSession(config, models);
            session.Ended += () => endedCount++;
            return session;
        }

        private static void TickFor(RaidSession session, float seconds, Vector3 position, IInventoryService inv)
        {
            int ticks = Mathf.RoundToInt(seconds / Frame);
            for (int i = 0; i < ticks; i++)
                session.Tick(Frame, position, inv);
        }

        private ItemInstance AddFlare()
        {
            var item = new ItemInstance(flare);
            pockets.Placed.Add(new PlacedItem(item, Vector2Int.zero, false));
            return item;
        }

        // ── 시간 (RULE-02) ───────────────────────────────────

        [Test]
        public void Initial_State()
        {
            RaidSession session = Session();
            Assert.AreEqual(0f, session.ElapsedSec);
            Assert.AreEqual(1500f, session.RemainingSec);
            Assert.IsFalse(session.IsOver);
            Assert.IsNull(session.CurrentZone);
            Assert.AreEqual(4, session.Extractions.Count);
        }

        [Test]
        public void TimeLimit1500_MissingInAction()
        {
            RaidSession session = Session();
            session.AdvanceTime(1499.9f);
            session.Tick(0f, Away, inventory);
            Assert.IsFalse(session.IsOver, "14:59.9");
            Assert.AreEqual(0.1f, session.RemainingSec, 1e-3f);

            session.Tick(Frame, Away, inventory);

            Assert.IsTrue(session.IsOver);
            Assert.AreEqual(RaidOutcome.MissingInAction, session.Outcome);
            Assert.AreEqual(0f, session.RemainingSec);
            Assert.IsNull(session.CompletedExtraction);
            Assert.IsFalse(session.HasDeathCause, "MIA는 몸을 죽이지 않는다");
            Assert.AreEqual(1, endedCount);
        }

        [Test]
        public void TimeLimit_ByTicking_EndsAt1500()
        {
            RaidSession session = Session();
            TickFor(session, 1499f, Away, inventory);
            Assert.IsFalse(session.IsOver);
            TickFor(session, 2f, Away, inventory);
            Assert.AreEqual(RaidOutcome.MissingInAction, session.Outcome);
            Assert.AreEqual(1500f, session.ElapsedSec, Frame, "넘은 뒤로는 시간이 흐르지 않는다");
        }

        [Test]
        public void TimeoutWhileWaiting_MissingInAction()
        {
            RaidSession session = Session();
            session.AdvanceTime(1495f);
            TickFor(session, 6f, Ext01Pos, inventory);
            Assert.IsTrue(session.IsOver);
            Assert.AreEqual(RaidOutcome.MissingInAction, session.Outcome, "10초 대기 중 25:00 도달");
        }

        [Test]
        public void CompletionAndTimeoutSameTick_ExtractionWins()
        {
            RaidSession session = Session();
            session.AdvanceTime(1490f);
            session.Tick(9.9f, Ext01Pos, inventory);
            Assert.IsFalse(session.IsOver);

            session.Tick(Frame, Ext01Pos, inventory);   // 대기 완료 = 1500초

            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome, "같은 틱이면 탈출 우선 (§1.2)");
            Assert.AreEqual(M4TestData.Ext01, session.CompletedExtraction.Id);
            Assert.AreEqual(1, endedCount);
        }

        [Test]
        public void AdvanceTime_DecidedOnNextTick()
        {
            RaidSession session = Session();
            session.AdvanceTime(1500f);
            Assert.IsFalse(session.IsOver, "종료 판정은 다음 Tick");
            session.Tick(0f, Away, inventory);
            Assert.AreEqual(RaidOutcome.MissingInAction, session.Outcome);
        }

        // ── 탈출 (§8.2) ──────────────────────────────────────

        [Test]
        public void Ext01_TenSeconds_Extracted()
        {
            RaidSession session = Session();
            TickFor(session, 9.9f, Ext01Pos, inventory);
            Assert.IsFalse(session.IsOver);
            Assert.AreSame(session.Extractions[0], session.CurrentZone);
            Assert.AreEqual(ExtractionPhase.CountingDown, session.CurrentZone.Phase);

            session.Tick(Frame, Ext01Pos, inventory);

            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);
            Assert.AreEqual(M4TestData.Ext01, session.CompletedExtraction.Id);
            Assert.IsNull(session.CurrentZone, "종료 후 구역 없음");
            Assert.AreEqual(10f, session.ElapsedSec, 0.01f);
        }

        [Test]
        public void CurrentZone_OnlyInsideActiveZone()
        {
            RaidSession session = Session(M4TestData.Ext01);
            session.Tick(Frame, Away, inventory);
            Assert.IsNull(session.CurrentZone);
            session.Tick(Frame, Ext04Pos, inventory);
            Assert.IsNull(session.CurrentZone, "비활성 구역은 표시 없음 (Q4)");
            session.Tick(Frame, Ext01Pos + new Vector3(0f, 3.5f, 0f), inventory);
            Assert.IsNull(session.CurrentZone, "높이 차 3m 초과");
            session.Tick(Frame, Ext01Pos + new Vector3(7.9f, 0f, 0f), inventory);
            Assert.IsNotNull(session.CurrentZone, "반경 8m 안");
        }

        [Test]
        public void InactiveExtraction_NeverExtracts()
        {
            RaidSession session = Session(M4TestData.Ext01);
            TickFor(session, 60f, Ext04Pos, inventory);
            Assert.IsFalse(session.IsOver);
        }

        [Test]
        public void Ext02_NeedsSwitch_ThenFifteenSeconds()
        {
            RaidSession session = Session();
            TickFor(session, 20f, Ext02Pos, inventory);
            Assert.IsFalse(session.IsOver);
            Assert.AreEqual(ExtractionBlock.NoPower, session.CurrentZone.Block);

            Assert.IsTrue(session.ActivateSwitch(M4TestData.PowerSwitchId), "처음 켜면 true");
            Assert.IsFalse(session.ActivateSwitch(M4TestData.PowerSwitchId), "두 번째는 false");
            Assert.IsTrue(session.IsSwitchOn(M4TestData.PowerSwitchId));

            TickFor(session, 14.9f, Ext02Pos, inventory);
            Assert.IsFalse(session.IsOver);
            session.Tick(Frame, Ext02Pos, inventory);
            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);
            Assert.AreEqual(M4TestData.Ext02, session.CompletedExtraction.Id);
        }

        [Test]
        public void Switch_InvalidIds()
        {
            RaidSession session = Session();
            Assert.IsFalse(session.ActivateSwitch(null));
            Assert.IsFalse(session.ActivateSwitch(""));
            Assert.IsFalse(session.IsSwitchOn(null));
            Assert.IsFalse(session.IsSwitchOn(M4TestData.PowerSwitchId));
        }

        [Test]
        public void Ext03_TooEarly_ThenMissingItem_ThenConsumesFlareOnce()
        {
            RaidSession session = Session();
            TickFor(session, 1f, Ext03Pos, inventory);
            Assert.AreEqual(ExtractionBlock.TooEarly, session.CurrentZone.Block, "15분 전");

            session.AdvanceTime(900f);
            session.Tick(Frame, Ext03Pos, inventory);
            Assert.AreEqual(ExtractionBlock.MissingItem, session.CurrentZone.Block, "신호탄 없음");

            ItemInstance flareItem = AddFlare();
            TickFor(session, 19.9f, Ext03Pos, inventory);
            Assert.IsFalse(session.IsOver);
            Assert.IsFalse(inventory.Calls.Contains(nameof(IInventoryService.TryConsume)), "대기 중에는 소모하지 않는다 (Q6)");

            session.Tick(Frame, Ext03Pos, inventory);

            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);
            Assert.AreEqual(M4TestData.Ext03, session.CompletedExtraction.Id);
            Assert.AreEqual(1, inventory.Calls.FindAll(c => c == nameof(IInventoryService.TryConsume)).Count, "완료 순간 1회");
            Assert.IsFalse(pockets.Placed.Exists(p => p.Item == flareItem), "신호탄 1개 소모");
        }

        [Test]
        public void Ext03_DroppingFlareWhileWaiting_Resets()
        {
            RaidSession session = Session();
            session.AdvanceTime(900f);
            AddFlare();
            TickFor(session, 10f, Ext03Pos, inventory);
            Assert.AreEqual(10f, session.CurrentZone.RemainingSec, 0.01f);

            pockets.Placed.Clear();
            session.Tick(Frame, Ext03Pos, inventory);

            Assert.AreEqual(ExtractionBlock.MissingItem, session.CurrentZone.Block);
            Assert.AreEqual(20f, session.CurrentZone.RemainingSec, "처음부터");
        }

        // Contains는 true인데 소모 실패(잠김 등) → 탈출하지 않고 처음부터
        [Test]
        public void Ext03_ConsumeFails_NotExtracted()
        {
            var unconsumable = new UnconsumableGrid(new ItemInstance(flare));
            inventory.Grids.Add(unconsumable);
            RaidSession session = Session();
            session.AdvanceTime(900f);

            TickFor(session, 20f, Ext03Pos, inventory);

            Assert.IsFalse(session.IsOver, "소모 실패면 완료 안 함");
            Assert.AreEqual(1, inventory.Calls.FindAll(c => c == nameof(IInventoryService.TryConsume)).Count);
            Assert.AreEqual(ExtractionBlock.MissingItem, session.Extractions[2].Block);
            Assert.AreEqual(20f, session.Extractions[2].RemainingSec);
        }

        // M4 리뷰 수정 (낮음 3): 대기 판정 = TryConsume 범위. 잠긴 신호탄만 있으면 Contains는 true여도 대기하지 않는다
        // (예전: 20초 대기 → 소모 실패 → 처음부터가 무한 반복).
        [Test]
        public void Ext03_OnlyLockedFlare_MissingItem_NoWaitLoop()
        {
            ItemInstance lockedFlare = AddFlare();
            inventory.Locked.Add(lockedFlare);
            Assert.IsTrue(inventory.Contains(flare), "보유는 하고 있다");
            RaidSession session = Session();
            session.AdvanceTime(900f);

            TickFor(session, 45f, Ext03Pos, inventory);

            Assert.IsFalse(session.IsOver);
            Assert.AreEqual(ExtractionBlock.MissingItem, session.Extractions[2].Block);
            Assert.AreEqual(20f, session.Extractions[2].RemainingSec, "대기가 진행되지 않는다");
            Assert.IsFalse(inventory.Calls.Contains(nameof(IInventoryService.TryConsume)), "소모 시도 없음");

            inventory.Locked.Remove(lockedFlare);   // 사용 가능해지면 정상 탈출
            TickFor(session, 20.1f, Ext03Pos, inventory);
            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);
            Assert.IsFalse(pockets.Placed.Exists(p => p.Item == lockedFlare));
        }

        [Test]
        public void Ext03_FlareOnlyEquipped_MissingItem()
        {
            // 장착 슬롯의 아이템은 소모 대상이 아니다 (TryConsume 범위 = 저장 공간)
            inventory.Equipped[EquipSlot.Primary2] = new ItemInstance(flare);
            Assert.IsTrue(inventory.Contains(flare));
            RaidSession session = Session();
            session.AdvanceTime(900f);

            TickFor(session, 25f, Ext03Pos, inventory);

            Assert.IsFalse(session.IsOver);
            Assert.AreEqual(ExtractionBlock.MissingItem, session.CurrentZone.Block);
            Assert.IsFalse(inventory.Calls.Contains(nameof(IInventoryService.TryConsume)));
        }

        [Test]
        public void Ext03_NullInventory_MissingItem()
        {
            RaidSession session = Session();
            session.AdvanceTime(900f);
            TickFor(session, 30f, Ext03Pos, null);
            Assert.IsFalse(session.IsOver);
            Assert.AreEqual(ExtractionBlock.MissingItem, session.CurrentZone.Block);
        }

        [Test]
        public void Ext04_Backpack_Blocks_ThenSevenSeconds()
        {
            inventory.Equipped[EquipSlot.Backpack] = new ItemInstance(TestData.Backpack());
            inventory.Equipped[EquipSlot.TacticalVest] = new ItemInstance(TestData.Vest());
            RaidSession session = Session();
            TickFor(session, 10f, Ext04Pos, inventory);
            Assert.IsFalse(session.IsOver);
            Assert.AreEqual(ExtractionBlock.BackpackEquipped, session.CurrentZone.Block);

            inventory.Equipped.Remove(EquipSlot.Backpack);
            TickFor(session, 6.9f, Ext04Pos, inventory);
            Assert.IsFalse(session.IsOver);
            session.Tick(Frame, Ext04Pos, inventory);
            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome, "조끼는 허용");
            Assert.AreEqual(M4TestData.Ext04, session.CompletedExtraction.Id);
        }

        [Test]
        public void Ext01_NullInventory_StillWorks()
        {
            RaidSession session = Session();
            TickFor(session, 10f, Ext01Pos, null);
            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);
        }

        // ── 사망·포기·한 번만 종료 ──────────────────────────

        [Test]
        public void Death_Killed_WithCause()
        {
            RaidSession session = Session();
            TickFor(session, 5f, Ext01Pos, inventory);

            session.EndByDeath(BodyPart.Head);

            Assert.IsTrue(session.IsOver);
            Assert.AreEqual(RaidOutcome.Killed, session.Outcome);
            Assert.IsTrue(session.HasDeathCause);
            Assert.AreEqual(BodyPart.Head, session.DeathCause);
            Assert.IsNull(session.CompletedExtraction);
        }

        // M4 리뷰 수정 (낮음 4): PlayerDied를 놓쳐도 죽은 몸은 탈출하지 않는다 — 대상 오버로드가 IsDead면 사망(원인 불명)
        [Test]
        public void TickWithTarget_DeadBody_EndsKilled_NotExtracted()
        {
            RaidSession session = Session();
            var target = new FakePlayerTarget { Position = Ext01Pos };
            for (int i = 0; i < 50; i++)
                session.Tick(Frame, target, inventory);   // 5초 대기
            Assert.IsFalse(session.IsOver);

            target.IsDead = true;
            for (int i = 0; i < 100; i++)
                session.Tick(Frame, target, inventory);   // 대기 시간을 넘겨도

            Assert.AreEqual(RaidOutcome.Killed, session.Outcome);
            Assert.IsFalse(session.HasDeathCause, "원인 불명");
            Assert.IsNull(session.CompletedExtraction);
            Assert.AreEqual(1, endedCount);
        }

        [Test]
        public void TickWithTarget_DeadAtStart_NeverMia()
        {
            RaidSession session = Session();
            session.AdvanceTime(config.timeLimitSec);
            var target = new FakePlayerTarget { Position = Away, IsDead = true };
            session.Tick(Frame, target, inventory);
            Assert.AreEqual(RaidOutcome.Killed, session.Outcome, "시간 초과와 겹쳐도 사망이 먼저");
        }

        [Test]
        public void TickWithTarget_Alive_SameAsPosition()
        {
            RaidSession session = Session();
            var target = new FakePlayerTarget { Position = Ext01Pos };
            for (int i = 0; i < 101; i++)
                session.Tick(Frame, target, inventory);
            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);
            Assert.AreEqual(M4TestData.Ext01, session.CompletedExtraction.Id);
        }

        [Test]
        public void EndByDeath_NoCause_AfterEnd_Ignored()
        {
            RaidSession session = Session();
            session.Abandon();
            session.EndByDeath();
            Assert.AreEqual(RaidOutcome.Abandoned, session.Outcome);
            Assert.AreEqual(1, endedCount);
        }

        [Test]
        public void Abandon_Abandoned()
        {
            RaidSession session = Session();
            session.Abandon();
            Assert.AreEqual(RaidOutcome.Abandoned, session.Outcome);
            Assert.IsFalse(session.HasDeathCause);
            Assert.AreEqual(1, endedCount);
        }

        [Test]
        public void AfterEnd_AllInputsIgnored_EndedOnce()
        {
            RaidSession session = Session();
            session.RecordKill();
            TickFor(session, 10f, Ext01Pos, inventory);
            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);
            float elapsed = session.ElapsedSec;

            session.EndByDeath(BodyPart.Thorax);   // 탈출 후 연출 중 피격
            session.Abandon();
            session.RecordKill();
            session.AdvanceTime(2000f);
            session.Tick(Frame, Ext02Pos, inventory);

            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome, "결과는 바뀌지 않는다");
            Assert.IsFalse(session.HasDeathCause);
            Assert.AreEqual(1, session.EnemiesKilled);
            Assert.AreEqual(elapsed, session.ElapsedSec);
            Assert.IsFalse(session.ActivateSwitch(M4TestData.PowerSwitchId));
            Assert.AreEqual(1, endedCount);
        }

        [Test]
        public void RecordKill_Counts()
        {
            RaidSession session = Session();
            session.RecordKill();
            session.RecordKill();
            session.RecordKill();
            Assert.AreEqual(3, session.EnemiesKilled);
        }

        [Test]
        public void NullConfig_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new RaidSession(null, new List<ExtractionModel>()));
        }

        [Test]
        public void NullExtractions_OnlyTimer()
        {
            var session = new RaidSession(config, null);
            Assert.AreEqual(0, session.Extractions.Count);
            session.AdvanceTime(1500f);
            session.Tick(0f, Away, inventory);
            Assert.AreEqual(RaidOutcome.MissingInAction, session.Outcome);
        }

        // ── 할당 (매 프레임 RaidController.Update) ──────────

        [Test]
        public void Tick_DoesNotAllocate()
        {
            RaidSession session = Session();
            inventory.Equipped[EquipSlot.Backpack] = new ItemInstance(TestData.Backpack());
            session.Tick(Frame, Away, inventory);
            AiTest.AssertNoAlloc(() =>
            {
                session.Tick(0.001f, Away, inventory);
                session.Tick(0.001f, Ext01Pos, inventory);
                session.Tick(0.001f, Ext02Pos, inventory);
                session.Tick(0.001f, Ext04Pos, inventory);
            });
        }

        // TryConsume이 건드리지 못하는 그리드 (FakeInventoryService.TryConsume은 FakeGridView에서만 뺀다)
        private sealed class UnconsumableGrid : IGridView
        {
            private readonly List<PlacedItem> items = new List<PlacedItem>();

            public UnconsumableGrid(ItemInstance item) => items.Add(new PlacedItem(item, Vector2Int.zero, false));

            public GridRef Ref => new GridRef(GridKind.TacticalVest);
            public Vector2Int Size => new Vector2Int(4, 4);
            public IReadOnlyList<PlacedItem> Items => items;
        }
    }
}
