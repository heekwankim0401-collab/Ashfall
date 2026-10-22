#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Player;
using Ashfall.Raid;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M4 리뷰 수정 (중간 1, RULE-01): 레이드 종료(결과 스냅샷) 뒤에는 소지품이 바뀌지 않는다.
    // - 탈출 대기 중 시작한 재장전 → 탈출 완료 시 취소, 이후 프레임(세계가 다시 돌아도)에 끝나지 않음 → CarriedOut = 실제 소지품(중복·누락 없음)
    // - 탈출 대기 중 시작한 의약품 사용 → 취소, 차감·효과 없음. 종료부터 목적지(M5: Hideout) 로드 완료까지 세계 정지 유지(CompleteRaid가 풀지 않음)
    public class RaidEndFreezeTests
    {
        private const string Ext01 = "EXT-01";

        private M4Raid raid;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            raid = new M4Raid();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.Flow != null)
            {
                gm.Flow.AcknowledgeResult();
                gm.Flow.SetWorldPaused(false);
            }
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator ReloadDuringExtraction_CancelledAtEnd_CarriedOutMatchesInventory()
        {
            yield return M4Raid.Load(raid);
            yield return raid.RemoveEnemies();
            WeaponController weapon = Object.FindFirstObjectByType<WeaponController>();
            Assert.IsNotNull(weapon, "WeaponController 없음");
            WeaponModel model = weapon.Model;
            Assert.IsNotNull(model, "Primary1 무기를 들고 있지 않다");
            Assert.IsTrue(model.CanReload, "예비 탄창 없음");
            float reloadSec = model.Item.Definition.reloadTimeSec;
            Assert.Less(reloadSec + 0.4f, raid.Config.endDelaySec, "종료 연출 시간 안에 재장전 시간이 지나가야 검증된다");

            Magazine loadedBefore = model.Current;
            int magazinesBefore = CountMagazines(raid.Carried(), model);

            using (var ended = new EventLog<RaidEnded>(raid.Events))
            {
                yield return raid.EnterZone(Ext01);
                // 재장전이 탈출 완료를 넘어가게: 남은 대기 < 재장전 시간의 절반에서 R
                yield return M3Scene.WaitUntil(() => raid.Model(Ext01).Phase == ExtractionPhase.CountingDown
                                                     && raid.Model(Ext01).RemainingSec < reloadSec * 0.5f,
                    20f, () => $"탈출 대기 (남은 {raid.Model(Ext01).RemainingSec:0.00})");
                Assert.IsTrue(model.PressReload(), "재장전 시작");
                Assert.IsTrue(model.IsReloading);

                yield return M3Scene.WaitUntil(() => ended.Count > 0, 10f, () => "탈출 완료");
                Assert.AreEqual(RaidOutcome.Extracted, ended.Last.Outcome);
                Assert.IsFalse(model.IsReloading, "탈출 완료 시 재장전 취소");
                Assert.AreSame(loadedBefore, model.Current, "장착 탄창 그대로");

                RaidResult result = raid.Controller.LastResult;
                Assert.IsNotNull(result);
                CollectionAssert.AreEquivalent(raid.Carried(), result.CarriedOut, "종료 직후 반출 = 소지품");

                // 세계가 다시 돌아도(정지를 일부러 풀어) 재장전 시간이 지나도록 기다린다 — 씬이 바뀌기 전
                raid.Flow.SetWorldPaused(false);
                float until = Time.realtimeSinceStartup + reloadSec + 0.3f;
                while (Time.realtimeSinceStartup < until)
                {
                    Assert.AreEqual(SceneNames.RaidCrimsonBasin, SceneManager.GetActiveScene().name, "확인 전에 씬이 바뀌었다");
                    yield return null;
                }

                Assert.IsFalse(model.IsReloading, "이후 프레임에도 재장전이 다시 시작·완료되지 않음");
                Assert.AreSame(loadedBefore, model.Current, "탄창 교환 없음");
                List<ItemInstance> carried = raid.Carried();
                CollectionAssert.AreEquivalent(carried, result.CarriedOut, "반출 = 실제 소지품 (중복·누락 없음)");
                Assert.AreEqual(carried.Count, carried.Distinct().Count(), "같은 인스턴스 중복 없음");
                CollectionAssert.DoesNotContain(carried, model.Current, "장착 탄창이 저장 공간에도 있으면 이중");
                Assert.AreEqual(magazinesBefore, CountMagazines(carried, model), "탄창 수 보존");
                Assert.AreEqual(1, ended.Count);

                // M5: 종료 목적지는 Hideout (§1.2)
                yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.Hideout,
                    M4Raid.EndToMenuTimeoutSec, () => "Hideout 전환");
                Assert.AreSame(result, raid.Flow.PendingResult);
            }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator MedicalUseDuringExtraction_CancelledAtEnd_WorldPausedUntilMenuLoaded()
        {
            yield return M4Raid.Load(raid);
            yield return raid.RemoveEnemies();
            BodyHealthModel body = raid.Health.Body;
            MedicalUseModel medical = raid.Health.Medical;
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.Bleed);
            MedicalItem bandage = raid.Carried().OfType<MedicalItem>()
                .FirstOrDefault(m => m.Definition.cures != null && m.Definition.cures.Contains(StatusEffect.Bleed));
            Assert.IsNotNull(bandage, "출혈을 막는 의약품이 시작 장비에 없다");
            int usesBefore = bandage.UsesLeft;
            float useSec = bandage.Definition.useTime;

            using (var ended = new EventLog<RaidEnded>(raid.Events))
            using (var completed = new EventLog<MedicalUseCompleted>(raid.Events))
            {
                yield return raid.EnterZone(Ext01);
                yield return M3Scene.WaitUntil(() => raid.Model(Ext01).Phase == ExtractionPhase.CountingDown
                                                     && raid.Model(Ext01).RemainingSec < useSec * 0.5f,
                    20f, () => $"탈출 대기 (남은 {raid.Model(Ext01).RemainingSec:0.00})");
                Assert.IsTrue(raid.Inventory.TryUse(bandage), "의약품 사용 요청");
                yield return null;
                Assert.IsTrue(medical.IsUsing, "사용 시작");

                yield return M3Scene.WaitUntil(() => ended.Count > 0, 10f, () => "탈출 완료");
                Assert.IsFalse(medical.IsUsing, "탈출 완료 시 사용 취소");
                Assert.IsTrue(raid.Inventory.IsRaidEndLocked, "인벤토리 종료 잠금");
                RaidResult result = raid.Controller.LastResult;
                CollectionAssert.Contains(result.CarriedOut, bandage);

                // 종료 → endDelaySec → CompleteRaid → Hideout(M5) 로드 완료까지 세계는 계속 정지
                while (SceneManager.GetActiveScene().name == SceneNames.RaidCrimsonBasin)
                {
                    Assert.AreEqual(0f, Time.timeScale, "Hideout 로드 완료 전 정지 해제");
                    Assert.AreEqual(usesBefore, bandage.UsesLeft, "사용 횟수 차감 없음");
                    yield return null;
                }
                yield return null;
                Assert.AreEqual(SceneNames.Hideout, SceneManager.GetActiveScene().name);
                Assert.AreEqual(1f, Time.timeScale, "Hideout에서 정지 해제");
                Assert.AreSame(result, raid.Flow.PendingResult);

                Assert.AreEqual(0, completed.Count, "사용 완료 없음");
                Assert.AreEqual(usesBefore, bandage.UsesLeft);
                Assert.IsTrue(body.HasStatus(BodyPart.LeftLeg, StatusEffect.Bleed), "효과 없음");
                // 인벤토리 모델(순수 C#)은 씬이 내려가도 읽을 수 있다 — 결과와 그대로 일치
                CollectionAssert.AreEquivalent(raid.Carried(), result.CarriedOut, "반출 = 실제 소지품");
            }
        }

        // 저장 공간의 탄창 + 무기에 장착된 탄창
        private static int CountMagazines(List<ItemInstance> carried, WeaponModel model) =>
            carried.Count(i => i is Magazine) + (model.Current != null ? 1 : 0);
    }
}
#endif
