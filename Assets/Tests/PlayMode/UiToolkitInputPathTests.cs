#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ashfall.Tests.PlayMode
{
    // M4 2b "확신 없는 API" 확인: UI Toolkit 런타임 Button.clicked가 EventSystem 없이(Input System → InputForUI → UI Toolkit),
    // timeScale 0(일시정지)에서도 오는가. 레이드 씬 일시정지 화면의 [계속]으로 확인한다.
    //  1) 가상 마우스(실제 입력 경로)만으로 누른다.
    //  2) 안 되면 UI Toolkit 기본 이벤트 시스템의 "앱 포커스" 값을 진단(배치모드는 창 포커스가 없다)하고, 포커스 값을 켠 뒤 다시 누른다.
    //  3) 그래도 안 되면 합성 PointerDown/Up(같은 Clickable 경로)으로 누른다 — 버튼 처리 자체가 timeScale 0에서 되는지.
    // 결과는 로그(UiToolkitInputPath)로 남기고, 단정은 "어떤 경로로든 timeScale 0에서 clicked → 재개"만 한다.
    public class UiToolkitInputPathTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RestoreFocusOverride();
            GameManager gm = GameManager.Instance;
            gm?.Flow?.SetWorldPaused(false);
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator PauseResumeButton_AtTimeScaleZero_WithoutEventSystem_ClickedFires()
        {
            var raid = new M4Raid();
            yield return M4Raid.Load(raid);
            yield return raid.RemoveEnemies();
            Assert.IsNull(GameObject.Find("EventSystem"), "EventSystem 없음 (설계)");

            var notes = new List<string>
            {
                $"batchmode {Application.isBatchMode}, isFocused {Application.isFocused}, Screen {Screen.width}x{Screen.height}",
                "DefaultEventSystem: " + DescribeDefaultEventSystem(),
            };
            UiClickPath path;
            using (var keyboard = new SimKeyboard())
            using (var pointer = new UiPointer())
            {
                yield return keyboard.Tap(Key.Escape);
                yield return null;
                Assert.AreEqual(ScreenKind.Pause, raid.Presenter.Screens.Current);
                Assert.AreEqual(0f, Time.timeScale);
                Button resume = raid.UiRoot.Q<Button>(PauseScreenView.ResumeName);

                // 1) 실제 입력 경로만
                yield return pointer.Click(resume, allowFallback: false);
                path = pointer.LastPath;
                notes.Add($"1) 가상 마우스(포커스 그대로): {path}");

                if (path == UiClickPath.None)
                {
                    // 2) 앱 포커스 값을 켜고 다시
                    string forced = ForceFocus();
                    notes.Add("2) 포커스 강제: " + forced);
                    yield return null;
                    yield return pointer.Click(resume, allowFallback: false);
                    path = pointer.LastPath;
                    notes.Add($"2) 가상 마우스(포커스 강제 후): {path}");
                    RestoreFocusOverride();
                }

                if (path == UiClickPath.None)
                {
                    // 3) 합성 이벤트
                    yield return pointer.Click(resume, allowFallback: true);
                    path = pointer.LastPath;
                    notes.Add($"3) 대안: {path}");
                }

                Debug.Log("[UiToolkitInputPath] " + string.Join(" | ", notes));
                Assert.AreNotEqual(UiClickPath.None, path, "어떤 경로로도 [계속]이 눌리지 않았다: " + string.Join(" | ", notes));
                Assert.AreEqual(ScreenKind.None, raid.Presenter.Screens.Current, "[계속] → 일시정지 닫힘");
                Assert.AreEqual(1f, Time.timeScale, "timeScale 0에서 누른 버튼으로 재개");
            }
        }

        // ── UI Toolkit 내부 진단 (공개 API가 아님 — 실패해도 테스트는 계속) ──

        private static object focusTarget;
        private static FieldInfo focusField;
        private static object focusOriginal;

        private static object DefaultEventSystem(out Type type)
        {
            Assembly asm = typeof(VisualElement).Assembly;
            type = asm.GetType("UnityEngine.UIElements.DefaultEventSystem");
            Type util = asm.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            object es = util?.GetProperty("defaultEventSystem", any)?.GetValue(null);
            if (es == null)
                es = util?.GetField("s_DefaultEventSystem", any)?.GetValue(null);
            return es;
        }

        private static string DescribeDefaultEventSystem()
        {
            try
            {
                object es = DefaultEventSystem(out Type type);
                if (type == null)
                    return "형식 없음";
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                IEnumerable<string> fields = type.GetFields(all)
                    .Where(f => f.FieldType == typeof(bool) || f.FieldType.IsEnum || f.Name.IndexOf("ocus", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(f => $"{f.Name}={SafeGet(f, f.IsStatic ? null : es)}");
                IEnumerable<string> props = type.GetProperties(all)
                    .Where(p => p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0)
                    .Select(p => $"{p.Name}={SafeGet(p, es)}");
                return (es == null ? "인스턴스 없음; " : "") + string.Join(", ", fields.Concat(props));
            }
            catch (Exception e)
            {
                return "진단 실패: " + e.GetType().Name + " " + e.Message;
            }
        }

        private static string SafeGet(FieldInfo f, object target)
        {
            try { return (f.IsStatic || target != null) ? Convert.ToString(f.GetValue(target)) : "?"; }
            catch (Exception e) { return e.GetType().Name; }
        }

        private static string SafeGet(PropertyInfo p, object target)
        {
            try
            {
                MethodInfo getter = p.GetGetMethod(true);
                return getter != null && (getter.IsStatic || target != null) ? Convert.ToString(p.GetValue(target)) : "?";
            }
            catch (Exception e) { return e.GetType().Name; }
        }

        // 앱 포커스로 보이는 bool 필드를 true로 (isAppFocused 등)
        private static string ForceFocus()
        {
            try
            {
                object es = DefaultEventSystem(out Type type);
                if (type == null || es == null)
                    return "DefaultEventSystem 없음";
                const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo f = type.GetFields(inst).FirstOrDefault(x => x.FieldType == typeof(bool) &&
                                                                        x.Name.IndexOf("focus", StringComparison.OrdinalIgnoreCase) >= 0);
                if (f == null)
                {
                    // 프로퍼티(isAppFocused)에 쓰기 접근자가 있으면 그것으로
                    PropertyInfo p = type.GetProperties(inst).FirstOrDefault(x => x.PropertyType == typeof(bool) &&
                                                                                 x.Name.IndexOf("focus", StringComparison.OrdinalIgnoreCase) >= 0);
                    MethodInfo setter = p?.GetSetMethod(true);
                    if (p == null)
                        return "포커스 필드·프로퍼티 없음";
                    if (setter == null)
                        return $"{p.Name}: 쓰기 접근자 없음(값 {p.GetValue(es)}, 계산 프로퍼티) — 강제 불가";
                    object before = p.GetValue(es);
                    setter.Invoke(es, new object[] { true });
                    focusRestore = () => setter.Invoke(es, new[] { before });
                    return $"{p.Name}: {before} → {p.GetValue(es)} (프로퍼티)";
                }
                focusTarget = es;
                focusField = f;
                focusOriginal = f.GetValue(es);
                f.SetValue(es, true);
                return $"{f.Name}: {focusOriginal} → true";
            }
            catch (Exception e)
            {
                return "실패: " + e.GetType().Name + " " + e.Message;
            }
        }

        private static Action focusRestore;

        private static void RestoreFocusOverride()
        {
            if (focusRestore != null)
            {
                try { focusRestore(); }
                catch (Exception) { /* 진단용 — 무시 */ }
                focusRestore = null;
            }
            if (focusField != null && focusTarget != null)
            {
                try { focusField.SetValue(focusTarget, focusOriginal); }
                catch (Exception) { /* 진단용 — 무시 */ }
            }
            focusField = null;
            focusTarget = null;
            focusOriginal = null;
        }
    }
}
#endif
