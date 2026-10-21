using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // M3 시체 루트 (§6 시체, 설계 §1.9): LootContainer.AttachRuntime / AttachCorpse(EnemyKilled)
    public class CorpseLootContainerTests
    {
        private readonly List<Object> created = new List<Object>();

        private GameObject NewCorpse(string name = "Corpse")
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
                if (o != null)
                    Object.DestroyImmediate(o);
            created.Clear();
        }

        // 시체 내용물 순서 = 무기(장착 탄창 포함) → 예비 탄창 → 루트 (CorpseLootBuilder 규칙과 같은 순서)
        private static ItemInstance[] CorpseItems(out WeaponItem weapon, out Magazine spare, out ItemInstance junk)
        {
            weapon = new WeaponItem(M3TestData.Pistol9(), new Magazine(M3TestData.Mag9_12(), 7));
            spare = Magazine.Full(M3TestData.Mag9_12());
            junk = new ItemInstance(TestData.Wires());
            return new ItemInstance[] { weapon, spare, junk };
        }

        [Test]
        public void AttachRuntime_CreatesModelImmediately()
        {
            GameObject corpse = NewCorpse();
            ItemInstance[] items = CorpseItems(out _, out _, out _);

            LootContainer container = LootContainer.AttachRuntime(corpse, "약탈자 시체", new Vector2Int(6, 5), items, 0.5f);

            Assert.IsNotNull(container);
            Assert.AreSame(container, corpse.GetComponent<LootContainer>());
            Assert.IsNotNull(container.Model, "열기 전에 모델이 이미 있다");
            Assert.AreEqual("약탈자 시체", container.Model.DisplayName);
            Assert.AreEqual("약탈자 시체", container.TargetName, "프롬프트 이름");
            Assert.AreEqual(InteractionKind.OpenContainer, container.Kind);
            Assert.AreEqual(new Vector2Int(6, 5), container.Model.Size);
            Assert.AreEqual(3, container.Model.Items.Count);
            Assert.AreEqual(0, container.Model.RevealedCount, "처음엔 아무것도 드러나지 않았다");
        }

        [Test]
        public void AttachRuntime_KeepsSameInstances_WithRoundsPreserved()
        {
            ItemInstance[] items = CorpseItems(out WeaponItem weapon, out _, out _);
            LootContainer container = LootContainer.AttachRuntime(NewCorpse(), "시체", new Vector2Int(6, 5), items, 0.5f);

            var placed = container.Model.Items.Select(p => p.Item).ToList();
            CollectionAssert.Contains(placed, weapon, "새로 만들지 않고 받은 인스턴스를 그대로 넣는다");
            Assert.AreEqual(7, weapon.LoadedMagazine.Rounds, "장착 탄창의 남은 탄 수 보존");
        }

        [Test]
        public void Search_RevealsOnePerHalfSecond_InGivenOrder()
        {
            ItemInstance[] items = CorpseItems(out WeaponItem weapon, out Magazine spare, out ItemInstance junk);
            LootContainerModel model =
                LootContainer.AttachRuntime(NewCorpse(), "시체", new Vector2Int(6, 5), items, 0.5f).Model;

            Assert.IsFalse(model.Tick(0.49f));
            Assert.IsFalse(model.IsRevealed(weapon));

            Assert.IsTrue(model.Tick(0.01f));
            Assert.IsTrue(model.IsRevealed(weapon), "무기가 먼저");
            Assert.IsFalse(model.IsRevealed(spare));

            Assert.IsTrue(model.Tick(0.5f));
            Assert.IsTrue(model.IsRevealed(spare), "다음 탄창");
            Assert.IsFalse(model.IsRevealed(junk));

            Assert.IsTrue(model.Tick(0.5f));
            Assert.IsTrue(model.IsRevealed(junk), "마지막 루트");
            Assert.IsTrue(model.SearchComplete);
        }

        [Test]
        public void ItemsThatDoNotFit_AreExcludedWithWarning()
        {
            var tooBig = new ItemInstance(TestData.Item("TEST-BIG", "큰 물건", new Vector2Int(7, 1), 1f));
            var fits = new ItemInstance(TestData.Wires());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("들어가지 않아 뺀다"));
            LootContainerModel model = LootContainer.AttachRuntime(NewCorpse(), "시체", new Vector2Int(6, 5),
                new[] { tooBig, fits }, 0.5f).Model;

            Assert.AreEqual(1, model.Items.Count);
            Assert.AreSame(fits, model.Items[0].Item);
            model.Tick(0.5f);
            Assert.IsTrue(model.SearchComplete, "제외된 아이템은 검색 대상도 아니다");
        }

        [Test]
        public void AttachRuntime_NullHost_ReturnsNull()
        {
            Assert.IsNull(LootContainer.AttachRuntime(null, "시체", new Vector2Int(6, 5), new ItemInstance[0], 0.5f));
        }

        [Test]
        public void AttachRuntime_Twice_KeepsFirstContainer()
        {
            GameObject corpse = NewCorpse();
            var first = LootContainer.AttachRuntime(corpse, "시체", new Vector2Int(6, 5),
                new[] { new ItemInstance(TestData.Wires()) }, 0.5f);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("이미 루트 컨테이너"));
            var second = LootContainer.AttachRuntime(corpse, "다른", new Vector2Int(2, 2), new ItemInstance[0], 0.5f);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, corpse.GetComponents<LootContainer>().Length);
            Assert.AreEqual("시체", second.Model.DisplayName, "내용물을 덮어쓰지 않는다");
        }

        [Test]
        public void AttachCorpse_UsesConfigGridAndSearchTime()
        {
            InventoryConfigSO config = TestData.Inventory();
            config.corpseGridSize = new Vector2Int(6, 5);
            created.Add(config);
            GameObject corpse = NewCorpse();
            ItemInstance[] items = CorpseItems(out _, out _, out _);
            var evt = new EnemyKilled(corpse, "레이더 시체", items, Vector3.zero, "AI-RDR");

            LootContainer container = LootContainer.AttachCorpse(evt, config);

            Assert.IsNotNull(container);
            Assert.AreEqual("레이더 시체", container.TargetName);
            Assert.AreEqual(new Vector2Int(6, 5), container.Model.Size, "InventoryConfigSO.corpseGridSize");
            Assert.IsFalse(container.Model.Tick(0.49f));
            Assert.IsTrue(container.Model.Tick(0.01f), "searchTimePerItemSec 0.5");
        }

        [Test]
        public void AttachCorpse_DestroyedOrMissingCorpse_Ignored()
        {
            InventoryConfigSO config = TestData.Inventory();
            created.Add(config);
            Assert.IsNull(LootContainer.AttachCorpse(new EnemyKilled(null, "시체", new ItemInstance[0], Vector3.zero, "AI-SCV"), config));

            var corpse = new GameObject("Destroyed");
            Object.DestroyImmediate(corpse);
            Assert.IsNull(LootContainer.AttachCorpse(new EnemyKilled(corpse, "시체", new ItemInstance[0], Vector3.zero, "AI-SCV"), config));
        }

        [Test]
        public void InventoryConfigDefault_CorpseGrid_Is6x5()
        {
            var fresh = ScriptableObject.CreateInstance<InventoryConfigSO>();
            created.Add(fresh);
            Assert.AreEqual(new Vector2Int(6, 5), fresh.corpseGridSize);
        }
    }
}
