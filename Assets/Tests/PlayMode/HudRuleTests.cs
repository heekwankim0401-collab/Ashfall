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
    // M2 AC4 — RULE-04(T → 0.8초 후 대략치), RULE-05(H → 부위 상태 화면, 등급만).
    // 2026-10-28 상시 HUD(B안)로 RULE-04·05 개정: HUD 상시 영역에는 탄 수·HP 수치가 보인다. 그래서 "숫자 없음" 단언은
    // HUD 전체가 아니라 T 확인 문구(hud-toast)와 H 화면에만 남긴다. 상시 HUD 스모크는 아래 BPlan_* 테스트.
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

        // B안 이후: T 확인 문구는 여전히 대략치만(숫자 없음)
        private void AssertHudHasNoNumbers(string when)
        {
            Assert.IsFalse(M2Scene.DigitOrPercent.IsMatch(Toast.text ?? string.Empty), $"{when}: 탄창 확인 문구에 숫자 \"{Toast.text}\" (RULE-04)");
        }

        private static bool Shown(VisualElement e) => e.resolvedStyle.display != DisplayStyle.None;

        [UnityTest]
        public IEnumerator RULE04_T_ShowsApproximateLevelAfter08Sec_NoRoundCountInToast()
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

        // ── 상시 HUD(B안, 2026-10-28) 스모크 ──

        [UnityTest]
        public IEnumerator BPlan_StatusHud_ShowsHealthAndAmmo_UpdatesOnChange()
        {
            yield return null;
            yield return null;
            Assert.IsTrue(Shown(scene.UiRoot.Q(HudStatusView.LeftName)), "체력 패널 상시 표시");
            Label head = scene.UiRoot.Q<Label>(HudStatusView.PartPrefix + (int)BodyPart.Head);
            int maxHead = Mathf.CeilToInt(scene.Body.GetMaxHp(BodyPart.Head));
            Assert.AreEqual($"머리 {maxHead}/{maxHead}", head.text);

            Assert.IsTrue(Shown(scene.UiRoot.Q(HudStatusView.WeaponName)), "무기 들면 무기 패널");
            Magazine mag = scene.Weapon.Model.Current;
            Label ammo = scene.UiRoot.Q<Label>(HudStatusView.AmmoName);
            Assert.AreEqual($"{mag.Rounds}/{mag.Capacity}", ammo.text, "RULE-04 개정: 실제 탄 수/용량");

            mag.TakeRound();
            scene.Body.ApplyDamage(BodyPart.Head, 10f);
            yield return null;
            yield return null;
            Assert.AreEqual($"{mag.Rounds}/{mag.Capacity}", ammo.text, "탄 수 변화 반영");
            Assert.AreEqual($"머리 {Mathf.CeilToInt(scene.Body.GetHp(BodyPart.Head))}/{maxHead}", head.text, "HP 변화 반영");

            // 화면이 열리면 HUD 전체 숨김 (기존 규칙)
            yield return keyboard.Tap(Key.Tab);
            yield return null;
            Assert.AreEqual(ScreenKind.Inventory, scene.Ui.Presenter.Screens.Current);
            Assert.IsFalse(Shown(Hud), "인벤토리 열림 → HUD 숨김");
            yield return keyboard.Tap(Key.Tab);
            yield return null;
            Assert.IsTrue(Shown(Hud));
        }

        [UnityTest]
        public IEnumerator BPlan_HitFromRight_ShowsRightEdgeThenHides()
        {
            yield return null;
            VisualElement right = scene.UiRoot.Q("hud-edge-right");
            Assert.IsFalse(Shown(right), "평소 display:none");

            // 오른쪽에서 쏜 탄 = 플레이어 오른쪽에서 왼쪽으로 날아온다
            Transform t = scene.Health.transform;
            var info = new DamageInfo(1f, 0, t.position + Vector3.up * 1.0f, -t.right, 10f);
            scene.Health.ApplyDamage(info);
            yield return null;
            yield return null;
            Assert.IsTrue(Shown(right), "피격 직후 오른쪽 띠");
            Assert.IsFalse(Shown(scene.UiRoot.Q("hud-edge-left")));
            yield return M2Scene.WaitUntil(() => !Shown(right), 3f);
        }
    }
}
#endif
