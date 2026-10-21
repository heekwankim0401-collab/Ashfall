#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ashfall.Tests.PlayMode
{
    // M2 AC4 — RULE-04(탄수 비표시, T → 0.8초 후 대략치), RULE-05(체력 수치 비표시, H → 부위 상태 화면).
    // 가상 키보드로 실제 키 바인딩(T, H)을 거친다. 판정은 UIDocument 트리의 Label 텍스트로 한다.
    public class HudRuleTests
    {
        private M2Scene scene;
        private SimKeyboard keyboard;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M2Scene();
            yield return M2Scene.Load(scene);
            keyboard = new SimKeyboard();
        }

        [TearDown]
        public void TearDown()
        {
            keyboard?.Dispose();
            keyboard = null;
        }

        private VisualElement Hud => scene.UiRoot.Q(HudView.RootName);
        private Label Toast => scene.UiRoot.Q<Label>(HudView.ToastName);

        private void AssertHudHasNoNumbers(string when)
        {
            foreach (string text in M2Scene.LabelTexts(Hud))
                Assert.IsFalse(M2Scene.DigitOrPercent.IsMatch(text), $"{when}: HUD에 숫자 \"{text}\" (RULE-04)");
        }

        private static bool Shown(VisualElement e) => e.resolvedStyle.display != DisplayStyle.None;

        [UnityTest]
        public IEnumerator RULE04_T_ShowsApproximateLevelAfter08Sec_NoRoundCountOnHud()
        {
            using var started = new EventLog<MagazineCheckStarted>(scene.Events);
            using var completed = new EventLog<MagazineCheckCompleted>(scene.Events);
            AssertHudHasNoNumbers("평상시");
            Assert.IsTrue(string.IsNullOrEmpty(Toast.text), "평상시 탄창 문구 없음");

            // T → 0.8초 → "가득" (30/30)
            keyboard.Hold(Key.T);
            yield return M2Scene.WaitUntil(() => started.Count == 1, 2f);
            int startFrame = started.Frames[0];
            Assert.AreEqual(Time.frameCount, startFrame);
            float startFrameDt = Time.deltaTime;   // 시작한 프레임에도 Tick이 한 번 돈다
            keyboard.Release(Key.T);
            float checkTime = 0f;
            yield return M2Scene.WaitUntil(() => completed.Count == 1, 3f, e => checkTime = e);
            checkTime += startFrameDt;
            Assert.AreEqual(MagazineFillLevel.Full, completed.Last.Level);
            Assert.Greater(completed.Frames[0], startFrame);
            Assert.GreaterOrEqual(checkTime, 0.8f - 1e-3f, "RULE-04 0.8초 전에 결과가 나오면 안 된다");
            Assert.AreEqual(0.8f, checkTime, 0.1f, "RULE-04 확인 시간 0.8초");
            yield return null;
            Assert.AreEqual("가득", Toast.text);
            Assert.IsTrue(Shown(Toast));
            AssertHudHasNoNumbers("확인 결과 표시 중");

            // 15/30 → "절반"
            var magazine = scene.Weapon.Model.Current;
            for (int i = 0; i < 15; i++)
                magazine.TakeRound();
            yield return keyboard.Tap(Key.T);
            yield return M2Scene.WaitUntil(() => completed.Count == 2, 3f);
            Assert.AreEqual(MagazineFillLevel.Half, completed.Last.Level);
            yield return null;
            Assert.AreEqual("절반", Toast.text);

            // 3/30 → "거의 없음"
            for (int i = 0; i < 12; i++)
                magazine.TakeRound();
            yield return keyboard.Tap(Key.T);
            yield return M2Scene.WaitUntil(() => completed.Count == 3, 3f);
            Assert.AreEqual(MagazineFillLevel.AlmostEmpty, completed.Last.Level);
            yield return null;
            Assert.AreEqual("거의 없음", Toast.text);
            AssertHudHasNoNumbers("거의 없음 표시 중");

            // 결과 문구는 일정 시간 뒤 사라진다
            yield return M2Scene.WaitUntil(() => string.IsNullOrEmpty(Toast.text), 5f);
            Assert.IsFalse(Shown(Toast));
        }

        [UnityTest]
        public IEnumerator RULE04_CheckCancelledByReload_NoResult()
        {
            using var completed = new EventLog<MagazineCheckCompleted>(scene.Events);
            yield return keyboard.Tap(Key.T);
            Assert.IsTrue(scene.Weapon.MagazineCheck.IsChecking);
            yield return keyboard.Tap(Key.R);   // 재장전 → 확인 취소
            Assert.IsFalse(scene.Weapon.MagazineCheck.IsChecking);
            Assert.IsTrue(scene.Weapon.Model.IsReloading);
            yield return M2Scene.WaitSeconds(1.2f);
            Assert.AreEqual(0, completed.Count, "취소된 확인은 결과 없음");
        }

        [UnityTest]
        public IEnumerator RULE05_H_OpensBodyStatusScreen_WithoutNumbers()
        {
            var body = scene.Body;
            body.ApplyDamage(BodyPart.LeftArm, 60f);        // 파괴
            body.ApplyDamage(BodyPart.Thorax, 30f);         // 50/80 = 0.625 → 부상
            body.ApplyDamage(BodyPart.RightLeg, 50f);       // 15/65 = 0.23 → 중상
            body.AddStatus(BodyPart.RightLeg, StatusEffect.Bleed);
            body.AddStatus(BodyPart.LeftArm, StatusEffect.Fracture);

            yield return keyboard.Tap(Key.H);
            Assert.AreEqual(ScreenKind.BodyStatus, scene.Ui.Presenter.Screens.Current, "H → 부위 상태 화면");
            Assert.IsTrue(scene.Motor.InputBlocked, "화면 열림 → 조작 차단");
            yield return null;

            VisualElement screen = scene.UiRoot.Q("body-status-screen");
            Assert.IsNotNull(screen);
            Assert.IsTrue(Shown(screen));
            var texts = M2Scene.LabelTexts(screen).ToList();
            Assert.Greater(texts.Count, 7);
            foreach (string text in texts)
                Assert.IsFalse(M2Scene.DigitOrPercent.IsMatch(text), $"부위 상태 화면에 숫자 \"{text}\" (RULE-05)");
            foreach (string part in new[] { "머리", "흉부", "복부", "왼팔", "오른팔", "왼다리", "오른다리" })
                Assert.Contains(part, texts);
            foreach (string word in new[] { "정상", "부상", "중상", "파괴", "출혈", "골절" })
                Assert.Contains(word, texts, $"등급·상태 문구 \"{word}\"");

            // 열린 상태에서 상태가 바뀌면 다시 그린다 (PlayerBodyStatusChanged)
            body.RemoveStatus(BodyPart.RightLeg, StatusEffect.Bleed);
            yield return null;
            yield return null;
            Assert.IsFalse(M2Scene.LabelTexts(screen).Contains("출혈"), "출혈 해제가 화면에 반영");

            yield return keyboard.Tap(Key.H);
            Assert.AreEqual(ScreenKind.None, scene.Ui.Presenter.Screens.Current, "H 다시 → 닫힘");
            Assert.IsFalse(scene.Motor.InputBlocked);
        }
    }
}
#endif
