#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using Ashfall.Core;
using Ashfall.Player;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // MVP 마무리 F1 전신 사인 (AC-F1 PlayMode, MVP_finish_design §7, MF-Q1·Q4):
    // 실제 Raid_CrimsonBasin에서 수분 0 → PlayerHealth의 탈수 피해(ApplySystemicDamage) → 흉부 0 사망
    // → PlayerDied(Thorax, Dehydration) → RaidController → 결과 → 은신처 결과 패널 제목 "사망 — 탈수".
    // 시간 단축: PlayerHealth의 config를 복제본(dehydratedHpPerSec 상향)으로 바꾼다 — 에셋 값은 건드리지 않는다.
    // 세이브는 SaveSandbox 새 폴더(실제 persistentDataPath 없음). 적은 지운다.
    public class DeathCauseFlowTests
    {
        // 테스트 전용 시간 단축 값 (게임 수치 아님): 흉부 80 → 약 2초
        private const float FastDehydratedHpPerSec = 40f;

        private static readonly FieldInfo ConfigField =
            typeof(PlayerHealth).GetField("config", BindingFlags.Instance | BindingFlags.NonPublic);

        private PlayerConfigSO fastConfig;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            M5Flow.ResetFlow();
            FileSaveStorage storage = SaveSandbox.UseFreshStorage("death-cause-flow");
            Assert.IsFalse(SaveSandbox.IsUnderPersistentDataPath(storage.FilePath), "샌드박스 경로");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return M5Flow.TearDown("after-death-cause-flow");
            if (fastConfig != null)
                Object.Destroy(fastConfig);
            fastConfig = null;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Dehydration_KillsPlayer_ResultTitleIsDehydration()
        {
            var raid = new M4Raid();
            yield return M4Raid.Load(raid);
            yield return raid.RemoveEnemies();
            Assert.IsNotNull(ConfigField, "PlayerHealth.config 필드 없음");
            var original = (PlayerConfigSO)ConfigField.GetValue(raid.Health);
            Assert.IsNotNull(original);
            fastConfig = Object.Instantiate(original);
            fastConfig.dehydratedHpPerSec = FastDehydratedHpPerSec;
            ConfigField.SetValue(raid.Health, fastConfig);

            RaidResult result;
            using (var deaths = new EventLog<PlayerDied>(raid.Events))
            using (var ended = new EventLog<RaidEnded>(raid.Events))
            {
                // 수분을 한 번에 0으로 (자연 감소를 크게 한 번) — 다음 프레임부터 PlayerHealth가 탈수 피해를 준다
                raid.Health.Survival.Tick(100000f, false);
                Assert.IsTrue(raid.Health.Survival.IsDehydrated, "수분 0");
                yield return M3Scene.WaitUntil(() => ended.Count > 0, 15f, () => "탈수 사망 → RaidEnded");
                Assert.AreEqual(1, deaths.Count, "PlayerDied 1회");
                Assert.AreEqual(BodyPart.Thorax, deaths.Last.Cause, "사인 부위 흉부 (머리 제외 Q33)");
                Assert.AreEqual(DeathCauseKind.Dehydration, deaths.Last.Kind);
                result = raid.Controller.LastResult;
            }
            Assert.IsNotNull(result);
            Assert.AreEqual(RaidOutcome.Killed, result.Outcome);
            Assert.IsTrue(result.HasDeathCause);
            Assert.AreEqual(DeathCauseKind.Dehydration, result.DeathKind);
            Assert.AreEqual("사망 — 탈수", RaidResultViewModel.TitleFor(result));

            // 흐름 밖 레이드 → 은신처가 결과를 표시만 한다 (Q25)
            var hideout = new M5Hideout();
            yield return M5Hideout.Attach(hideout);
            Assert.AreEqual(HideoutPanel.Result, hideout.Presenter.Panel, "은신처 결과 패널");
            Assert.AreEqual("사망 — 탈수", hideout.Text(RaidResultView.TitleName), "결과 제목");
        }
    }
}
#endif
