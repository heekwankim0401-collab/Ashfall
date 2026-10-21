#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M3 AC2 — 적 사망 시 시체 루팅 (§6 검색 0.5초/개, §7, M3 설계 §1.9·§9 CorpseLootFlowTests).
    // 플레이어가 LMB로 SCV 머리를 쏴 1발에 사살(소총 45 ≥ 머리 35, AC4) → EnemyKilled → 시체에 LootContainer →
    // 시체를 보고 F → 0.5초마다 무기 → 탄창 → 루트 순서로 드러남 → 무기(장착 탄창 남은 탄 그대로) 가져오기.
    public class CorpseLootFlowTests
    {
        private const float PerItemSec = 0.5f;
        private const int RoundsLeftBeforeDeath = 7;   // 사망 전 장착 탄창(12발)에서 탄을 빼 둔다 = 쏘고 남은 탄

        private M3Scene scene;
        private SimInput input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M3Scene();
            yield return M3Scene.Load(scene);
        }

        [TearDown]
        public void TearDown()
        {
            input?.Dispose();
            input = null;
        }

        [UnityTest]
        public IEnumerator PlayerHeadshot_KillsScv_CorpseLootSearchedAndTaken()
        {
            yield return scene.KeepOnly("A1");
            EnemyAgent scv = scene.Enemy("A1");
            Assert.AreEqual("WPN-PST-01", scv.Weapon.Item.Definition.id);
            // 등 뒤 6m에서 쏜다(적은 +z를 보고 플레이어는 발소리 없이 순간이동).
            M3Scene.WarpEnemy(scv, new Vector3(M3Scene.OpenLaneX, 0f, 10f), 0f);
            Magazine loaded = scv.Weapon.Item.LoadedMagazine;
            while (loaded.Rounds > RoundsLeftBeforeDeath)
                loaded.TakeRound();
            scene.TeleportPlayer(new Vector3(M3Scene.OpenLaneX, 0.05f, 4f), scv.transform.position);
            input = new SimInput();
            using var killed = new EventLog<EnemyKilled>(scene.Events);

            // 조준(RMB, 퍼짐 0) → 머리 높이(1.68m, 비율 0.93)를 총구에서 겨눔 → LMB 1발.
            // 조준하는 동안 적이 순찰을 시작해 걸어가지 않게 AI 틱만 잠깐 멈춘다(피격·사망 처리는 EnemyAgent 자체 경로).
            scene.Director.enabled = false;
            input.Mouse.SetRight(true);
            yield return M2Scene.WaitSeconds(0.4f);
            yield return scene.AimAt(() => scv.transform.position + Vector3.up * 1.68f, fromMuzzle: true);
            yield return input.Mouse.ClickLeft();
            input.Mouse.SetRight(false);
            yield return M3Scene.WaitUntil(() => killed.Count > 0, 2f,
                () => $"사살 안 됨: HP 머리 {scv.Body.GetHp(EnemyHitRegion.Head)} 몸통 {scv.Body.GetHp(EnemyHitRegion.Body)} " +
                      $"다리 {scv.Body.GetHp(EnemyHitRegion.Legs)}, 적 위치 {scv.transform.position}");
            scene.Director.enabled = true;

            Assert.AreEqual(1, killed.Count, "EnemyKilled 1회");
            Assert.IsTrue(scv.IsDead);
            Assert.AreEqual(EnemyHitRegion.Head, scv.Body.DeathCause, "머리 1발 즉사 (45 ≥ 35)");
            Assert.AreEqual(EnemyState.Dead, scv.State);
            Assert.AreEqual(0, scene.Director.Agents.Count, "사망 → director 등록 해제");
            EnemyKilled evt = killed.Last;
            Assert.AreSame(scv.gameObject, evt.Corpse);
            Assert.AreEqual("AI-SCV", evt.ArchetypeId);
            Assert.AreEqual(scv.Archetype.corpseDisplayName, evt.DisplayName);

            // 시체 = 루트 컨테이너, 내용 순서: 무기(장착 탄창 남은 탄 그대로) → 예비 탄창 → 루트
            var container = scv.GetComponent<LootContainer>();
            Assert.IsNotNull(container, "시체에 LootContainer");
            ItemInstance[] items = evt.Items;
            Assert.IsInstanceOf<WeaponItem>(items[0], "첫 아이템 = 무기");
            var weapon = (WeaponItem)items[0];
            Assert.AreEqual("WPN-PST-01", weapon.Definition.id);
            Assert.AreEqual(RoundsLeftBeforeDeath, weapon.LoadedMagazine.Rounds, "장착 탄창 남은 탄 수 보존");
            int spares = scv.Archetype.spareMagazines;
            for (int i = 1; i <= spares; i++)
            {
                Assert.IsInstanceOf<Magazine>(items[i], $"{i + 1}번째 = 예비 탄창");
                Assert.AreEqual(12, ((Magazine)items[i]).Rounds, "예비 탄창은 가득");
            }
            Assert.Greater(items.Length, spares + 1, "루트 테이블 굴림 1개 이상 (LT-SCV rollCount 1~2)");

            // 시체 옆 1m로 가서 시체를 보고 F (실제 상호작용 레이캐스트)
            using var prompt = new EventLog<InteractionPromptChanged>(scene.Events);
            Vector3 corpseCenter = scv.transform.position + Vector3.up * 0.35f;
            scene.TeleportPlayer(scv.transform.position + scv.transform.right * 1.0f + Vector3.up * 0.05f, corpseCenter);
            yield return scene.AimAt(corpseCenter, fromMuzzle: false);
            yield return M3Scene.WaitUntil(() => prompt.Count > 0 && prompt.Last.Visible, 2f,
                () => $"프롬프트 없음 (이벤트 {prompt.Count}개, 시선 피치 {scene.CurrentPitch:0.0}, 시선 레이 첫 충돌 " +
                      (Physics.Raycast(scene.PlayerCamera.position, scene.PlayerCamera.forward, out RaycastHit h, 2f,
                          Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                          ? $"{h.collider.name}({h.collider.transform.root.name}) {h.distance:0.00}m" : "없음") + ")");
            Assert.AreEqual(evt.DisplayName, prompt.Last.TargetName, "프롬프트 \"열기: 약탈자 시체\"");
            // F를 누른 프레임에 열린다. 그 뒤 프레임의 deltaTime을 더해 공개 시각을 잰다(M2 LootSearchFlowTests와 같은 기준).
            input.Keyboard.Hold(Key.F);
            yield return null;
            input.Keyboard.Release(Key.F);

            IInventoryService inventory = scene.Inventory;
            ILootView loot = inventory.ActiveLoot;
            Assert.IsNotNull(loot, "F → 시체 루트 열림");
            Assert.AreEqual(evt.DisplayName, loot.DisplayName);
            ItemInstance[] placed = loot.Items.Select(p => p.Item).ToArray();
            CollectionAssert.AreEqual(items, placed, "배치 = 이벤트 순서 (그리드 6×5에 모두 들어감)");

            // 0.5초마다 하나씩 드러남
            LootContainerModel model = container.Model;
            float elapsed = 0f;
            var revealAt = new List<float>();
            int seen = model.RevealedCount;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!loot.SearchComplete)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "검색이 끝나지 않는다");
                yield return null;
                elapsed += Time.deltaTime;
                while (seen < model.RevealedCount)
                {
                    Assert.IsTrue(loot.IsRevealed(placed[seen]), "배치 순서대로 드러남");
                    revealAt.Add(elapsed);
                    seen++;
                }
            }
            Assert.AreEqual(placed.Length, revealAt.Count);
            for (int i = 0; i < revealAt.Count; i++)
                Assert.AreEqual(PerItemSec * (i + 1), revealAt[i], 0.1f, $"{i + 1}번째 공개 시각");

            // 무기 가져오기 (루트 → 저장 공간)
            Assert.IsTrue(inventory.TryQuickMove(weapon), "무기 가져오기");
            Assert.IsTrue(scene.Inventory.IsInStorage(weapon));
            Assert.AreEqual(RoundsLeftBeforeDeath, weapon.LoadedMagazine.Rounds, "가져온 무기의 탄 수 그대로");
            Assert.IsTrue(inventory.TryQuickMove(items[1]), "예비 탄창 가져오기");
        }
    }
}
#endif
