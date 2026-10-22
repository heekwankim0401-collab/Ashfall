#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Ashfall.Core;
using Ashfall.Raid;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M4 AC2 — 탈출 EXT-01~04, 레이드 타이머 (§8.2, RULE-02, M4 설계 §7 ExtractionFlowTests).
    // 실제 Raid_CrimsonBasin을 이름으로 불러 RaidController·IPlayerPlacement·F 상호작용을 그대로 쓴다.
    // 스폰은 시드 주입으로 고정(SP-N: EXT-01·02·04 활성, EXT-03 비활성 / SP-W: EXT-01·02·03 활성, EXT-04 비활성 — RaidConfig spawnRules).
    // 적은 지운다(교전 변수 제거). 대기 시간은 실제 시간(timeScale 1)으로 흐른다. 15분 조건·25분 제한은 RaidController.AdvanceTime.
    public class ExtractionFlowTests
    {
        private const string Ext01 = "EXT-01", Ext02 = "EXT-02", Ext03 = "EXT-03", Ext04 = "EXT-04";
        private const string SpawnNorth = "SP-N", SpawnWest = "SP-W";
        private const float ZoneEventTimeoutSec = 5f;
        private const float WaitToleranceSec = 0.25f;   // 프레임 dt 한 번 + 여유

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
            // 다음 테스트에 결과·정지가 남지 않게
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
        public IEnumerator Ext01_Waits10Seconds_LeavingZoneResets_ThenExtractsToHideout()
        {
            yield return M4Raid.Load(raid);
            yield return raid.RemoveEnemies();
            CollectionAssert.Contains(raid.Controller.ActiveExtractionIds, Ext01, "EXT-01은 항상 활성(Q3)");
            float configured = raid.Model(Ext01).Config.waitSec;
            Assert.AreEqual(10f, configured, "§8.2 EXT-01 10초");

            using (var status = new EventLog<ExtractionStatusChanged>(raid.Events))
            using (var ended = new EventLog<RaidEnded>(raid.Events))
            {
                // 진입 → 카운트다운 10부터
                yield return raid.EnterZone(Ext01);
                yield return M3Scene.WaitUntil(() => status.Count > 0 && status.Last.InZone, ZoneEventTimeoutSec, () => "구역 진입 이벤트");
                ExtractionStatusChanged first = status.Received.First(s => s.InZone);
                Assert.AreEqual(ExtractionBlock.None, first.Block);
                Assert.AreEqual(10, first.SecondsLeft, "첫 표시 10");
                Assert.AreEqual("남쪽 도로", first.DisplayName);
                Assert.AreSame(raid.Model(Ext01), raid.Session.CurrentZone);

                // 몇 초 기다렸다가 나간다 → 숨김 + 초기화
                yield return M3Scene.WaitUntil(() => status.Last.SecondsLeft <= 7, 10f, () => $"카운트다운 진행 (마지막 {status.Last.SecondsLeft})");
                Assert.Less(raid.Model(Ext01).RemainingSec, 7.01f);
                yield return raid.LeaveZone();
                yield return M3Scene.WaitUntil(() => !status.Last.InZone, ZoneEventTimeoutSec, () => "구역 이탈 이벤트");
                Assert.IsNull(raid.Session.CurrentZone);
                Assert.AreEqual(ExtractionPhase.Idle, raid.Model(Ext01).Phase);
                Assert.AreEqual(10f, raid.Model(Ext01).RemainingSec, 1e-4f, "이탈 시 처음부터 (Q8)");
                Assert.IsFalse(raid.Session.IsOver);

                // 다시 들어가면 10부터, 끝까지 기다리면 탈출
                int before = status.Count;
                yield return raid.EnterZone(Ext01);
                yield return M3Scene.WaitUntil(() => status.Count > before && status.Last.InZone, ZoneEventTimeoutSec, () => "재진입 이벤트");
                Assert.AreEqual(10, status.Received[before].SecondsLeft, "재진입 시 10부터");
                float enteredAt = raid.Session.ElapsedSec;
                float realStart = Time.realtimeSinceStartup;
                yield return M3Scene.WaitUntil(() => ended.Count > 0, 20f, () => $"탈출 완료 (남은 {raid.Model(Ext01).RemainingSec:0.00})");
                float realWaited = Time.realtimeSinceStartup - realStart;

                Assert.AreEqual(1, ended.Count, "RaidEnded 1회");
                Assert.AreEqual(RaidOutcome.Extracted, ended.Last.Outcome);
                Assert.AreEqual("남쪽 도로", ended.Last.ExtractionName);
                RaidResult result = raid.Controller.LastResult;
                Assert.IsNotNull(result);
                Assert.AreEqual(Ext01, result.ExtractionId);
                Assert.AreEqual(10f, result.DurationSec - enteredAt, WaitToleranceSec, "구역 안 대기 = 10초 (레이드 시간 기준)");
                Assert.AreEqual(10f, realWaited, 1.5f, "실제 시간으로도 약 10초");
                // 종료 직후: 세계 정지 + 종료 오버레이, 탈출 HUD 숨김
                Assert.IsFalse(status.Last.InZone, "종료 시 탈출 HUD 숨김");
                Assert.AreEqual(0f, Time.timeScale, "종료 연출 중 세계 정지");
                Assert.AreEqual(ScreenKind.RaidEnd, raid.Presenter.Screens.Current);

                // RULE-01 탈출: 반출 = 종료 시점 소지품, 소실 없음
                CollectionAssert.AreEquivalent(raid.Carried(), result.CarriedOut, "반출 = 소지품");
                Assert.AreEqual(0, result.Lost.Count, "탈출 시 소실 없음");

                // endDelaySec 뒤 Hideout(M5 §1.2), 결과는 GameFlow가 들고 간다.
                // 이 레이드는 흐름 밖(씬 직접 로드)이라 은신처는 결과를 표시만 하고 프로필에 반영하지 않는다(Q25).
                GameFlow flow = raid.Flow;
                Assert.IsFalse(flow.HasRaidInFlow, "흐름 밖 레이드");
                yield return M4Raid.WaitForHideout();
                Assert.AreSame(result, flow.PendingResult, "GameFlow.PendingResult");
                Assert.AreEqual(GamePhase.Hideout, flow.Phase);
                Assert.AreEqual(1f, Time.timeScale, "은신처에서 정지 해제");
                HideoutRoot hideout = Object.FindFirstObjectByType<HideoutRoot>();
                Assert.AreEqual(HideoutPanel.Result, hideout.Presenter.Panel, "은신처 결과 패널");
            }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Ext02_BlockedWithoutPower_SwitchViaF_ThenExtractsAfter15Seconds()
        {
            yield return M4Raid.Load(raid, SpawnNorth);
            yield return raid.RemoveEnemies();
            CollectionAssert.Contains(raid.Controller.ActiveExtractionIds, Ext02);
            Assert.AreEqual(15f, raid.Model(Ext02).Config.waitSec, "§8.2 EXT-02 15초");
            PowerSwitch powerSwitch = Object.FindFirstObjectByType<PowerSwitch>();
            Assert.IsNotNull(powerSwitch, "전원 스위치 없음");
            Assert.AreEqual(raid.Model(Ext02).Config.requiredSwitchId, powerSwitch.SwitchId);

            using (var status = new EventLog<ExtractionStatusChanged>(raid.Events))
            using (var prompts = new EventLog<InteractionPromptChanged>(raid.Events))
            using (var ended = new EventLog<RaidEnded>(raid.Events))
            using (var keyboard = new SimKeyboard())
            {
                // 스위치 전: 구역 안이어도 NoPower, 카운트다운 없음
                yield return raid.EnterZone(Ext02);
                yield return M3Scene.WaitUntil(() => status.Count > 0 && status.Last.InZone, ZoneEventTimeoutSec, () => "구역 진입");
                Assert.AreEqual(ExtractionBlock.NoPower, status.Last.Block);
                yield return M2Scene.WaitSeconds(2f);
                Assert.AreEqual(ExtractionPhase.Blocked, raid.Model(Ext02).Phase);
                Assert.AreEqual(15f, raid.Model(Ext02).RemainingSec, 1e-4f, "전원 없으면 진행 없음");
                Assert.IsTrue(status.Received.Where(s => s.InZone).All(s => s.Block == ExtractionBlock.NoPower));

                // 스위치 앞에 서서 바라보고 F (PlayerInventoryBehaviour 레이캐스트 → IWorldInteractable)
                yield return StandFacing(powerSwitch);
                yield return M3Scene.WaitUntil(() => prompts.Count > 0 && prompts.Last.Visible && prompts.Last.Kind == InteractionKind.Activate,
                    ZoneEventTimeoutSec, () => $"작동 프롬프트 (마지막 {(prompts.Count > 0 ? prompts.Last.Kind + "/" + prompts.Last.Visible : "없음")})");
                Assert.AreEqual(powerSwitch.DisplayName, prompts.Last.TargetName);
                yield return keyboard.Tap(Key.F);
                yield return M3Scene.WaitUntil(() => powerSwitch.IsOn, 2f, () => "스위치 켜짐");
                Assert.IsTrue(raid.Session.IsSwitchOn(powerSwitch.SwitchId));
                yield return M3Scene.WaitUntil(() => !prompts.Last.Visible, 2f, () => "켠 스위치는 프롬프트 없음");

                // 다시 구역 → 15초 뒤 탈출
                int before = status.Count;
                yield return raid.EnterZone(Ext02);
                yield return M3Scene.WaitUntil(() => status.Count > before && status.Last.InZone, ZoneEventTimeoutSec, () => "재진입");
                Assert.AreEqual(ExtractionBlock.None, status.Received[before].Block);
                Assert.AreEqual(15, status.Received[before].SecondsLeft);
                float enteredAt = raid.Session.ElapsedSec;
                yield return M3Scene.WaitUntil(() => ended.Count > 0, 25f, () => "EXT-02 탈출");
                Assert.AreEqual(RaidOutcome.Extracted, ended.Last.Outcome);
                Assert.AreEqual(Ext02, raid.Controller.LastResult.ExtractionId);
                Assert.AreEqual(15f, raid.Controller.LastResult.DurationSec - enteredAt, WaitToleranceSec);
            }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Ext03_TooEarlyBefore900s_NoFlareBlocked_WithFlareConsumesOneAndExtractsAfter20Seconds()
        {
            yield return M4Raid.Load(raid, SpawnWest);
            yield return raid.RemoveEnemies();
            CollectionAssert.Contains(raid.Controller.ActiveExtractionIds, Ext03);
            ExtractionPointConfig cfg = raid.Model(Ext03).Config;
            Assert.AreEqual(20f, cfg.waitSec, "§8.2 EXT-03 20초");
            Assert.AreEqual(900f, cfg.availableAfterSec, "§8.2 15분 이후");
            Assert.IsNotNull(cfg.consumedItem, "신호탄 정의");
            ItemDefinitionSO flare = cfg.consumedItem;
            Assert.AreEqual(0, raid.CountCarried(flare), "시작 키트에 신호탄 없음(Q7)");

            using (var status = new EventLog<ExtractionStatusChanged>(raid.Events))
            using (var ended = new EventLog<RaidEnded>(raid.Events))
            {
                // 900초 전: TooEarly
                yield return raid.EnterZone(Ext03);
                yield return M3Scene.WaitUntil(() => status.Count > 0 && status.Last.InZone, ZoneEventTimeoutSec, () => "구역 진입");
                Assert.Less(raid.Session.ElapsedSec, 900f);
                Assert.AreEqual(ExtractionBlock.TooEarly, status.Last.Block);
                yield return M2Scene.WaitSeconds(1f);
                Assert.AreEqual(20f, raid.Model(Ext03).RemainingSec, 1e-4f);

                // 900초 경과 (경과 = 900 직후): 신호탄 없음 → MissingItem
                raid.Controller.AdvanceTime(900f - raid.Session.ElapsedSec + 0.01f);
                yield return M3Scene.WaitUntil(() => status.Last.Block == ExtractionBlock.MissingItem, ZoneEventTimeoutSec,
                    () => $"신호탄 없음 표시 (마지막 {status.Last.Block})");
                yield return M2Scene.WaitSeconds(1f);
                Assert.AreEqual(ExtractionPhase.Blocked, raid.Model(Ext03).Phase);
                Assert.IsFalse(raid.Session.IsOver);

                // 신호탄 2개를 줍는다(레이드 중 획득) → 20초 뒤 1개 소모 + 탈출
                Assert.IsTrue(raid.Inventory.TryAutoStore(ItemFactory.Create(flare)), "신호탄 1 넣기");
                Assert.IsTrue(raid.Inventory.TryAutoStore(ItemFactory.Create(flare)), "신호탄 2 넣기");
                Assert.AreEqual(2, raid.CountCarried(flare));
                yield return M3Scene.WaitUntil(() => status.Last.Block == ExtractionBlock.None, ZoneEventTimeoutSec, () => "카운트다운 시작");
                Assert.AreEqual(20, status.Last.SecondsLeft);
                float enteredAt = raid.Session.ElapsedSec;
                yield return M3Scene.WaitUntil(() => ended.Count > 0, 30f, () => "EXT-03 탈출");
                Assert.AreEqual(RaidOutcome.Extracted, ended.Last.Outcome);
                RaidResult result = raid.Controller.LastResult;
                Assert.AreEqual(Ext03, result.ExtractionId);
                Assert.AreEqual(20f, result.DurationSec - enteredAt, WaitToleranceSec);
                Assert.AreEqual(1, raid.CountCarried(flare), "신호탄 1개 소모");
                Assert.AreEqual(1, result.CarriedOut.Count(i => i.Definition == flare), "남은 신호탄 1개는 반출");
            }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Ext04_BlockedWithBackpack_WithoutBackpackExtractsAfter7Seconds()
        {
            yield return M4Raid.Load(raid, SpawnNorth);
            yield return raid.RemoveEnemies();
            CollectionAssert.Contains(raid.Controller.ActiveExtractionIds, Ext04);
            Assert.AreEqual(7f, raid.Model(Ext04).Config.waitSec, "§8.2 EXT-04 7초");
            ItemInstance backpack = raid.Inventory.GetEquipped(EquipSlot.Backpack);
            Assert.IsNotNull(backpack, "시작 키트에 배낭(Q13)");

            using (var status = new EventLog<ExtractionStatusChanged>(raid.Events))
            using (var ended = new EventLog<RaidEnded>(raid.Events))
            {
                yield return raid.EnterZone(Ext04);
                yield return M3Scene.WaitUntil(() => status.Count > 0 && status.Last.InZone, ZoneEventTimeoutSec, () => "구역 진입");
                Assert.AreEqual(ExtractionBlock.BackpackEquipped, status.Last.Block);
                yield return M2Scene.WaitSeconds(1.5f);
                Assert.AreEqual(7f, raid.Model(Ext04).RemainingSec, 1e-4f, "배낭을 메면 진행 없음");

                // 배낭을 벗는다(내용물째 꺼냄 — 규칙 판정만 본다). 조끼는 그대로(문구대로 배낭만 금지).
                Assert.IsTrue(raid.Inventory.Remove(backpack), "배낭 해제");
                Assert.IsNotNull(raid.Inventory.GetEquipped(EquipSlot.TacticalVest), "조끼는 허용");
                yield return M3Scene.WaitUntil(() => status.Last.Block == ExtractionBlock.None, ZoneEventTimeoutSec, () => "카운트다운 시작");
                Assert.AreEqual(7, status.Last.SecondsLeft);

                // 대기 중 다시 메면 처음부터 (매 틱 확인)
                yield return M3Scene.WaitUntil(() => status.Last.SecondsLeft <= 5, 5f, () => "진행");
                Assert.IsTrue(raid.Inventory.TryEquipNew(backpack, EquipSlot.Backpack), "배낭 다시 메기");
                yield return M3Scene.WaitUntil(() => status.Last.Block == ExtractionBlock.BackpackEquipped, ZoneEventTimeoutSec, () => "다시 막힘");
                Assert.AreEqual(7f, raid.Model(Ext04).RemainingSec, 1e-4f, "대기 중 배낭 → 초기화");
                Assert.IsTrue(raid.Inventory.Remove(backpack));
                yield return M3Scene.WaitUntil(() => status.Last.Block == ExtractionBlock.None, ZoneEventTimeoutSec, () => "다시 카운트다운");
                float enteredAt = raid.Session.ElapsedSec;

                yield return M3Scene.WaitUntil(() => ended.Count > 0, 15f, () => "EXT-04 탈출");
                Assert.AreEqual(RaidOutcome.Extracted, ended.Last.Outcome);
                Assert.AreEqual(Ext04, raid.Controller.LastResult.ExtractionId);
                Assert.AreEqual(7f, raid.Controller.LastResult.DurationSec - enteredAt, WaitToleranceSec);
                CollectionAssert.DoesNotContain(raid.Controller.LastResult.CarriedOut, backpack, "벗은 배낭은 반출 아님");
            }
        }

        // Q4: 비활성 탈출구는 들어가도 반응 없음 (HUD 없음, 조건을 모두 갖춰도 탈출 안 됨)
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator InactiveExtraction_NoHudAndNeverExtracts()
        {
            yield return M4Raid.Load(raid, SpawnNorth);
            yield return raid.RemoveEnemies();
            CollectionAssert.DoesNotContain(raid.Controller.ActiveExtractionIds, Ext03, "SP-N에서 EXT-03 비활성");
            ExtractionModel model = raid.Model(Ext03);
            Assert.IsFalse(model.IsActive);

            // 조건을 모두 갖춘다: 900초 경과 + 신호탄
            raid.Controller.AdvanceTime(901f);
            Assert.IsTrue(raid.Inventory.TryAutoStore(ItemFactory.Create(model.Config.consumedItem)));

            using (var status = new EventLog<ExtractionStatusChanged>(raid.Events))
            using (var ended = new EventLog<RaidEnded>(raid.Events))
            {
                yield return raid.EnterZone(Ext03);
                yield return M2Scene.WaitSeconds(model.Config.waitSec + 2f);
                Assert.AreEqual(0, status.Count, "비활성 탈출구: HUD 이벤트 없음");
                Assert.IsNull(raid.Session.CurrentZone);
                Assert.AreEqual(ExtractionPhase.Inactive, model.Phase);
                Assert.AreEqual(0, ended.Count);
                Assert.IsFalse(raid.Session.IsOver, "비활성 탈출구로는 탈출하지 않는다");
                Assert.AreEqual(1, raid.CountCarried(model.Config.consumedItem), "신호탄 소모 없음");
            }
        }

        // RULE-02: 25분(1500초) 경과 → MIA (사망 처리: 반출 0, 소지품 전부 소실)
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator RaidTimer_1500Seconds_EndsAsMissingInAction_AllItemsLost()
        {
            yield return M4Raid.Load(raid);
            yield return raid.RemoveEnemies();
            Assert.AreEqual(1500f, raid.Session.TimeLimitSec, "RULE-02 25분");

            using (var ended = new EventLog<RaidEnded>(raid.Events))
            {
                // 남은 1초까지 당기고 실제 시간으로 넘긴다
                raid.Controller.AdvanceTime(1499f - raid.Session.ElapsedSec);
                yield return null;
                Assert.IsFalse(raid.Session.IsOver, "1499초에는 진행 중");
                Assert.AreEqual(1f, raid.Session.RemainingSec, 0.35f);
                float realStart = Time.realtimeSinceStartup;
                yield return M3Scene.WaitUntil(() => ended.Count > 0, 5f, () => $"MIA (남은 {raid.Session.RemainingSec:0.00})");
                Assert.AreEqual(1f, Time.realtimeSinceStartup - realStart, 0.5f, "남은 1초 뒤 종료");

                Assert.AreEqual(1, ended.Count);
                Assert.AreEqual(RaidOutcome.MissingInAction, ended.Last.Outcome);
                RaidResult result = raid.Controller.LastResult;
                Assert.AreEqual(RaidOutcome.MissingInAction, result.Outcome);
                Assert.IsFalse(result.Survived);
                Assert.AreEqual(1500f, result.DurationSec, 1e-3f, "레이드 시간 = 제한 시간");
                Assert.IsFalse(raid.Health.IsDead, "MIA는 결과만 사망 처리 (몸은 그대로)");
                Assert.AreEqual(0, result.CarriedOut.Count, "RULE-01: 반출 0");
                CollectionAssert.AreEquivalent(raid.Carried().Select(i => i.Definition), result.Lost, "RULE-01: 소지품 전부 소실");
                Assert.Greater(result.Lost.Count, 0);
            }
        }

        // 스위치 패널 앞 1.1m에 서서 패널 중심을 본다. 눈 → 패널 레이가 다른 물체에 막히지 않는 쪽을 고른다.
        private IEnumerator StandFacing(PowerSwitch powerSwitch)
        {
            Transform t = powerSwitch.transform;
            Collider switchCollider = powerSwitch.GetComponentInChildren<Collider>();
            Assert.IsNotNull(switchCollider, "스위치 콜라이더 없음");
            Vector3 target = switchCollider.bounds.center;
            Vector3 forward = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(t.right, Vector3.up).normalized;
            foreach (Vector3 dir in new[] { forward, -forward, right, -right })
            {
                Vector3 stand = target + dir * 1.1f;
                if (!Physics.Raycast(stand + Vector3.up * 5f, Vector3.down, out RaycastHit ground, 20f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                Vector3 feet = ground.point + Vector3.up * 0.05f;
                Vector3 look = target - feet;
                float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                raid.Placement.PlaceAt(feet, yaw);
                yield return null;
                yield return null;
                Vector3 eye = raid.Motor.EyePosition;
                Vector3 toTarget = target - eye;
                float horizontal = new Vector2(toTarget.x, toTarget.z).magnitude;
                float pitchDown = Mathf.Atan2(-toTarget.y, horizontal) * Mathf.Rad2Deg;
                raid.LookDown(pitchDown);
                yield return null;
                yield return null;
                // 자기 캡슐은 건너뛴 가장 가까운 충돌 (PlayerInventoryBehaviour와 같은 규칙)
                Transform self = raid.Motor.transform.root;
                RaycastHit[] hits = Physics.RaycastAll(eye, toTarget.normalized, toTarget.magnitude + 0.5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                RaycastHit? nearest = null;
                foreach (RaycastHit h in hits)
                    if (h.collider.transform.root != self && (nearest == null || h.distance < nearest.Value.distance))
                        nearest = h;
                if (nearest.HasValue && nearest.Value.collider == switchCollider)
                    yield break;
            }
            Assert.Fail("스위치를 바라볼 수 있는 자리가 없다");
        }
    }
}
#endif
