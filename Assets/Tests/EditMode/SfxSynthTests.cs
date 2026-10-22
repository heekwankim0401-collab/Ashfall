using System;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.EditorTools;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M6 2b: 자리표시 효과음 합성 (설계 §2.2) — 길이, 피크 ≤ 0.9, 결정적, 무음 아님, 루프 이음매
    public class SfxSynthTests
    {
        private const int Rate = SfxSynth.DefaultRate;

        private static IEnumerable<TestCaseData> AllRecipes()
        {
            foreach (SurfaceKind k in M6AudioSetup.Surfaces)
                for (int v = 0; v < M6AudioSetup.FootstepVariants; v++)
                {
                    SurfaceKind kind = k;
                    int variant = v;
                    yield return new TestCaseData((Func<float[]>)(() => SfxSynth.Footstep(kind, variant, Rate)), SfxSynth.FootstepLengthSec(kind))
                        .SetName($"Footstep_{kind}_{variant}");
                }
            foreach (WeaponClass c in M6AudioSetup.GunshotClasses)
                foreach (GunshotLayer l in M6AudioSetup.GunshotLayers)
                {
                    WeaponClass cls = c;
                    GunshotLayer layer = l;
                    yield return new TestCaseData((Func<float[]>)(() => SfxSynth.Gunshot(cls, layer, Rate)), SfxSynth.GunshotLengthSec(cls, layer))
                        .SetName($"Gunshot_{cls}_{layer}");
                }
            yield return new TestCaseData((Func<float[]>)(() => SfxSynth.WindLoop(Rate)), SfxSynth.LoopSec).SetName("WindLoop");
            yield return new TestCaseData((Func<float[]>)(() => SfxSynth.StormLoop(Rate)), SfxSynth.LoopSec).SetName("StormLoop");
            yield return new TestCaseData((Func<float[]>)(() => SfxSynth.MetalCollapse(0, Rate)), SfxSynth.CollapseSec).SetName("Collapse_0");
            yield return new TestCaseData((Func<float[]>)(() => SfxSynth.MetalCollapse(1, Rate)), SfxSynth.CollapseSec).SetName("Collapse_1");
            yield return new TestCaseData((Func<float[]>)(() => SfxSynth.GeigerClick(Rate)), SfxSynth.GeigerClickSec).SetName("GeigerClick");
            yield return new TestCaseData((Func<float[]>)(() => SfxSynth.HideoutDrone(Rate)), SfxSynth.DroneSec).SetName("HideoutDrone");
        }

        [TestCaseSource(nameof(AllRecipes))]
        public void Recipe_Length_Peak_Deterministic_NotSilent(Func<float[]> synth, float expectedSec)
        {
            float[] a = synth();
            float[] b = synth();

            Assert.AreEqual(SfxSynth.SampleCount(expectedSec, Rate), a.Length, "길이");
            float peak = a.Max(x => Math.Abs(x));
            Assert.LessOrEqual(peak, SfxSynth.TargetPeak + 1e-5f, "피크 ≤ 0.9");
            Assert.Greater(peak, SfxSynth.TargetPeak - 1e-3f, "정규화됨");
            Assert.IsFalse(a.Any(float.IsNaN), "NaN 없음");
            double rms = Math.Sqrt(a.Average(x => (double)x * x));
            Assert.Greater(rms, 0.01, "무음 아님");
            CollectionAssert.AreEqual(a, b, "같은 레시피 = 같은 샘플(결정적)");
        }

        [Test]
        public void Loops_SeamIsNotADiscontinuity()
        {
            foreach (var (name, s) in new[]
                     {
                         ("Wind", SfxSynth.WindLoop(Rate)),
                         ("Storm", SfxSynth.StormLoop(Rate)),
                         ("Drone", SfxSynth.HideoutDrone(Rate)),
                     })
            {
                float seam = Math.Abs(s[s.Length - 1] - s[0]);
                float maxInterior = 0f;
                for (int i = 1; i < s.Length; i++)
                    maxInterior = Math.Max(maxInterior, Math.Abs(s[i] - s[i - 1]));
                Assert.LessOrEqual(seam, maxInterior, $"{name}: 이음매 차 {seam}가 내부 최대 이웃 차 {maxInterior}보다 크다");
                Assert.Less(seam, 0.2f, $"{name}: 이음매 차가 크다 {seam}");
            }
        }

        [Test]
        public void Variants_And_Classes_Differ()
        {
            CollectionAssert.AreNotEqual(SfxSynth.Footstep(SurfaceKind.Dirt, 0, Rate), SfxSynth.Footstep(SurfaceKind.Dirt, 1, Rate));
            CollectionAssert.AreNotEqual(SfxSynth.MetalCollapse(0, Rate), SfxSynth.MetalCollapse(1, Rate));
            Assert.AreNotEqual(SfxSynth.Gunshot(WeaponClass.Pistol, GunshotLayer.Near, Rate).Length,
                SfxSynth.Gunshot(WeaponClass.AssaultRifle, GunshotLayer.Near, Rate).Length, "분류별 길이 차이");
            Assert.Less(SfxSynth.GunshotLengthSec(WeaponClass.AssaultRifle, GunshotLayer.Near),
                SfxSynth.GunshotLengthSec(WeaponClass.AssaultRifle, GunshotLayer.Mid));
            Assert.Less(SfxSynth.GunshotLengthSec(WeaponClass.AssaultRifle, GunshotLayer.Mid),
                SfxSynth.GunshotLengthSec(WeaponClass.AssaultRifle, GunshotLayer.Far), "원거리 = 메아리 꼬리로 가장 길다");
        }

        [Test]
        public void CrossfadeLoop_JoinsTailToHead()
        {
            var raw = new float[] { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f };
            float[] loop = SfxSynth.CrossfadeLoop(raw, 2);
            Assert.AreEqual(8, loop.Length);
            Assert.AreEqual(8f, loop[0], 1e-6f, "첫 샘플 = 원본 끝 구간 시작(마지막 샘플 7의 다음)");
            Assert.AreEqual(7f, loop[7], 1e-6f);
            Assert.AreEqual(2f, loop[2], 1e-6f, "페이드 뒤는 원본 그대로");
        }

        [Test]
        public void Normalize_SilenceUnchanged()
        {
            var s = new float[4];
            SfxSynth.Normalize(s);
            CollectionAssert.AreEqual(new float[4], s);
        }
    }
}
