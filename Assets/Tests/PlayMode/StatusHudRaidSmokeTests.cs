using System.Collections;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ashfall.Tests.PlayMode
{
    // 상시 HUD(B안, 2026-10-28 — RULE-02·04·05 개정) 레이드 씬 스모크: 원천 서비스 등록, 남은 시간·체력·퀵슬롯이 보인다.
    public class StatusHudRaidSmokeTests
    {
        private const float LoadTimeoutSec = 30f;
        private const int WarmupFrames = 5;

        [UnityTest]
        public IEnumerator RaidScene_StatusHud_ShowsTimeHealthQuickSlots()
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(SceneNames.RaidCrimsonBasin, LoadSceneMode.Single);
            Assert.IsNotNull(op, "레이드 씬 로드 실패 (빌드 설정 확인)");
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSec;
            while (!op.isDone)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "레이드 씬 로드 시간 초과");
                yield return null;
            }
            for (int i = 0; i < WarmupFrames; i++)
                yield return null;

            GameManager gm = GameManager.Instance;
            Assert.IsTrue(gm.Services.TryGet(out IPlayerHudSource _), "IPlayerHudSource 미등록 (Player)");
            Assert.IsTrue(gm.Services.TryGet(out IWeaponHudSource _), "IWeaponHudSource 미등록 (Weapons)");
            Assert.IsTrue(gm.Services.TryGet(out IRaidInfo raid), "IRaidInfo 미등록");

            var roots = Object.FindObjectsByType<GameUIRoot>(FindObjectsSortMode.None);
            Assert.AreEqual(1, roots.Length, "GameUIRoot 수");
            VisualElement ui = roots[0].GetComponent<UIDocument>().rootVisualElement;

            Label time = ui.Q<Label>(HudStatusView.TimeName);
            Assert.AreEqual(DisplayStyle.Flex, time.resolvedStyle.display, "남은 시간 상시 표시 (RULE-02 표시 개정)");
            int shownSec = ParseSeconds(time.text);
            Assert.AreEqual(HudRules.CeilSeconds(raid.RemainingSec), shownSec, 1, $"남은 시간 \"{time.text}\"");

            Assert.AreEqual(DisplayStyle.Flex, ui.Q(HudStatusView.LeftName).resolvedStyle.display, "체력 패널 상시 표시");
            StringAssert.StartsWith("머리 ", ui.Q<Label>(HudStatusView.PartPrefix + (int)BodyPart.Head).text);
            Assert.AreEqual(DisplayStyle.Flex, ui.Q(HudStatusView.QuickSlotsName).resolvedStyle.display, "퀵슬롯 상시 표시");
            foreach (string edge in HudStatusView.EdgeNames)
                Assert.AreEqual(DisplayStyle.None, ui.Q(edge).resolvedStyle.display, $"{edge}: 피격 전 숨김");
        }

        private static int ParseSeconds(string mmss)
        {
            string[] parts = (mmss ?? string.Empty).Split(':');
            Assert.AreEqual(2, parts.Length, $"분:초 형식이 아니다: \"{mmss}\"");
            return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
        }
    }
}
