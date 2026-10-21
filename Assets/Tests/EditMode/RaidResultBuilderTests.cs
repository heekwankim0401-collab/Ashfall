using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M4 AC3 — RULE-01 적용 지점 (RaidResultBuilder, M4 설계 §1.5).
    // 실제 PlayerInventory + InventorySnapshot으로 반입·획득·소모를 만든 뒤 결과를 확인한다.
    public class RaidResultBuilderTests
    {
        private static readonly Vector3 Ext01Pos = new Vector3(0f, 0f, -470f);

        private RaidConfigSO config;
        private PlayerInventory inventory;
        private WeaponItem rifle;
        private ItemInstance vest;
        private ItemInstance bandage;
        private readonly List<ItemDefinitionSO> broughtIn = new List<ItemDefinitionSO>();

        [SetUp]
        public void SetUp()
        {
            config = M4TestData.RaidConfig();
            inventory = new PlayerInventory(TestData.Inventory());
            rifle = new WeaponItem(TestData.Rifle(), Magazine.Full(TestData.Magazine30()));
            vest = new ItemInstance(TestData.Vest());
            bandage = ItemFactory.Create(TestData.Bandage());
            Assert.IsTrue(inventory.TryEquipNew(rifle, EquipSlot.Primary1));
            Assert.IsTrue(inventory.TryEquipNew(vest, EquipSlot.TacticalVest));
            Assert.IsTrue(inventory.TryAutoStore(bandage));

            // 레이드 시작 반입 기록 (RaidController.Start와 같은 경로)
            broughtIn.Clear();
            RaidResultBuilder.ToDefinitions(Snapshot(), broughtIn);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        private List<ItemInstance> Snapshot()
        {
            var list = new List<ItemInstance>();
            InventorySnapshot.Collect(inventory, list);
            return list;
        }

        private RaidSession Session()
        {
            var models = new List<ExtractionModel>
            {
                new ExtractionModel(M4TestData.Find(config, M4TestData.Ext01), Ext01Pos, true),
            };
            return new RaidSession(config, models);
        }

        // 레이드 중 획득 (루트에서 주운 것)
        private ItemInstance PickUpJunk()
        {
            var junk = new ItemInstance(TestData.Wires());
            Assert.IsTrue(inventory.TryAutoStore(junk));
            return junk;
        }

        [Test]
        public void BroughtIn_IsStartDefinitions()
        {
            CollectionAssert.AreEqual(new ItemDefinitionSO[] { rifle.Definition, vest.Definition, bandage.Definition }, broughtIn,
                "장착 슬롯 순 → 저장 공간, 무기 장착 탄창은 무기에 포함");
        }

        [Test]
        public void Extracted_CarriesOutEverything_IncludingLoot_NothingLost()
        {
            ItemInstance junk = PickUpJunk();
            RaidSession session = Session();
            session.RecordKill();
            for (int i = 0; i < 100; i++)
                session.Tick(0.1f, Ext01Pos, inventory);
            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);

            RaidResult result = RaidResultBuilder.Build(session, broughtIn, Snapshot());

            Assert.IsTrue(result.Survived);
            Assert.AreEqual(M4TestData.Ext01, result.ExtractionId);
            Assert.AreEqual("남쪽 도로", result.ExtractionName);
            Assert.IsFalse(result.HasDeathCause);
            Assert.AreEqual(1, result.EnemiesKilled);
            Assert.AreEqual(10f, result.DurationSec, 0.01f);
            CollectionAssert.AreEquivalent(new ItemInstance[] { rifle, vest, bandage, junk }, result.CarriedOut,
                "반출 = 종료 시점 소지품 실물 (획득 루트 포함)");
            Assert.AreSame(rifle, result.CarriedOut[0], "실물 인스턴스 (탄 수 유지)");
            Assert.IsEmpty(result.Lost);
        }

        [TestCase(RaidOutcome.Killed)]
        [TestCase(RaidOutcome.MissingInAction)]
        [TestCase(RaidOutcome.Abandoned)]
        public void NotExtracted_CarriesOutNothing_LosesEverything(RaidOutcome outcome)
        {
            ItemInstance junk = PickUpJunk();
            RaidSession session = Session();
            switch (outcome)
            {
                case RaidOutcome.Killed:
                    session.EndByDeath(BodyPart.Thorax);
                    break;
                case RaidOutcome.MissingInAction:
                    session.AdvanceTime(1500f);
                    session.Tick(0f, Vector3.zero, inventory);
                    break;
                default:
                    session.Abandon();
                    break;
            }
            Assert.AreEqual(outcome, session.Outcome);

            RaidResult result = RaidResultBuilder.Build(session, broughtIn, Snapshot());

            Assert.IsFalse(result.Survived);
            Assert.AreEqual(outcome, result.Outcome);
            Assert.IsEmpty(result.CarriedOut, "RULE-01: 반출 0");
            CollectionAssert.AreEquivalent(
                new ItemDefinitionSO[] { rifle.Definition, vest.Definition, bandage.Definition, junk.Definition }, result.Lost,
                "소실 = 반입 + 획득");
            Assert.IsNull(result.ExtractionId);
            Assert.IsNull(result.ExtractionName);
        }

        [Test]
        public void Killed_HasDeathCause()
        {
            RaidSession session = Session();
            session.EndByDeath(BodyPart.Head);
            RaidResult result = RaidResultBuilder.Build(session, broughtIn, Snapshot());
            Assert.IsTrue(result.HasDeathCause);
            Assert.AreEqual(BodyPart.Head, result.DeathCause);
        }

        [Test]
        public void MissingInAction_DurationCappedAtTimeLimit()
        {
            RaidSession session = Session();
            session.AdvanceTime(1600f);
            session.Tick(0f, Vector3.zero, inventory);
            RaidResult result = RaidResultBuilder.Build(session, broughtIn, Snapshot());
            Assert.AreEqual(1500f, result.DurationSec);
            Assert.IsFalse(result.HasDeathCause);
        }

        [Test]
        public void BroughtIn_KeepsConsumedItems()
        {
            // 레이드 중 붕대를 다 쓰고 버림 → 반입 목록에는 남는다
            Assert.IsTrue(inventory.TryDrop(bandage));
            RaidSession session = Session();
            session.EndByDeath(BodyPart.Stomach);

            RaidResult result = RaidResultBuilder.Build(session, broughtIn, Snapshot());

            CollectionAssert.Contains(result.BroughtIn, bandage.Definition, "반입은 시작 시점 기록");
            CollectionAssert.DoesNotContain(result.Lost, bandage.Definition, "소실은 종료 시점 소지품만");
            Assert.AreEqual(2, result.Lost.Count);
        }

        [Test]
        public void OpenLoot_NotCarriedOut()
        {
            var lootItem = new ItemInstance(TestData.Pipe());
            inventory.OpenLoot(new LootContainerModel("상자", new Vector2Int(4, 4), new[] { lootItem }, 0.5f));
            RaidSession session = Session();
            for (int i = 0; i < 100; i++)
                session.Tick(0.1f, Ext01Pos, inventory);

            RaidResult result = RaidResultBuilder.Build(session, broughtIn, Snapshot());

            CollectionAssert.DoesNotContain(result.CarriedOut, lootItem, "열린 상자 안 아이템은 소지품이 아니다");
        }

        [Test]
        public void ResultListsAreCopies()
        {
            RaidSession session = Session();
            session.Abandon();
            List<ItemInstance> end = Snapshot();
            RaidResult result = RaidResultBuilder.Build(session, broughtIn, end);
            int lost = result.Lost.Count;
            int brought = result.BroughtIn.Count;

            end.Clear();
            broughtIn.Clear();

            Assert.AreEqual(lost, result.Lost.Count);
            Assert.AreEqual(brought, result.BroughtIn.Count, "스냅샷 리스트를 재사용해도 결과는 그대로");
        }

        [Test]
        public void NullLists_Empty()
        {
            RaidSession session = Session();
            session.Abandon();
            RaidResult result = RaidResultBuilder.Build(session, null, null);
            Assert.IsEmpty(result.BroughtIn);
            Assert.IsEmpty(result.CarriedOut);
            Assert.IsEmpty(result.Lost);
        }

        [Test]
        public void NotOver_Throws()
        {
            Assert.Throws<System.InvalidOperationException>(() => RaidResultBuilder.Build(Session(), broughtIn, Snapshot()));
            Assert.Throws<System.ArgumentNullException>(() => RaidResultBuilder.Build(null, broughtIn, Snapshot()));
        }

        [Test]
        public void ToDefinitions_SkipsNull()
        {
            var into = new List<ItemDefinitionSO>();
            RaidResultBuilder.ToDefinitions(new ItemInstance[] { rifle, null, vest }, into);
            CollectionAssert.AreEqual(new[] { rifle.Definition, vest.Definition }, into);
            RaidResultBuilder.ToDefinitions(null, into);
            Assert.AreEqual(2, into.Count);
        }
    }
}
