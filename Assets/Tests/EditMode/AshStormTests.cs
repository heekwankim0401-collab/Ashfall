using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 AC3 재 폭풍 일정·단계·값 (§8.3, M6 설계 §1.5·§7 AC3, Q23·Q24·Q26·Q27·Q28)
    public class AshStormTests
    {
        private const float Clear = 450f;   // 맑을 때 가시거리 (M6 기본값 — 맵 개선 뒤 SO는 600m, 이 테스트는 값과 무관)
        private AshStormConfigSO config;

        [SetUp]
        public void SetUp() => config = ScriptableObject.CreateInstance<AshStormConfigSO>();   // 필드 초기값 = 설계값

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        // ── AshStormSchedule ──

        [Test]
        public void PickStart_InsideWindow_ManySeeds()
        {
            for (int seed = 1; seed <= 500; seed++)
            {
                float start = AshStormSchedule.PickStartSec(config.startWindowSec, new System.Random(seed));
                Assert.GreaterOrEqual(start, 180f);
                Assert.LessOrEqual(start, 1200f);
            }
        }

        [Test]
        public void PickStart_SameSeed_SameResult_DifferentSeedsSpread()
        {
            float a = AshStormSchedule.PickStartSec(config.startWindowSec, new System.Random(42));
            float b = AshStormSchedule.PickStartSec(config.startWindowSec, new System.Random(42));
            Assert.AreEqual(a, b, "시드 결정적");
            var distinct = new HashSet<int>();
            for (int seed = 1; seed <= 50; seed++)
                distinct.Add(Mathf.RoundToInt(AshStormSchedule.PickStartSec(config.startWindowSec, new System.Random(seed))));
            Assert.Greater(distinct.Count, 30, "시드마다 다른 시각");
        }

        [Test]
        public void PickStart_ReversedWindow_IsSorted()
        {
            for (int seed = 1; seed <= 100; seed++)
            {
                float start = AshStormSchedule.PickStartSec(new Vector2(500f, 300f), new System.Random(seed));
                Assert.That(start, Is.InRange(300f, 500f));
            }
        }

        [Test]
        public void PickStart_ZeroWidthWindow_ReturnsThatTime()
        {
            Assert.AreEqual(600f, AshStormSchedule.PickStartSec(new Vector2(600f, 600f), new System.Random(7)), 1e-4f);
        }

        // ── AshStormModel ──

        [Test]
        public void Initial_Pending_NoStorm()
        {
            var model = new AshStormModel(config, Clear, 300f);
            Assert.AreEqual(AshStormPhase.Pending, model.Phase);
            Assert.AreEqual(0f, model.Intensity);
            Assert.IsTrue(float.IsPositiveInfinity(model.VisibilityM), "폭풍 아님 = ∞");
            Assert.AreEqual(0f, model.RadiationPerSec);
            Assert.IsFalse(model.Evaluate(299.9f), "시작 전에는 바뀌지 않음");
            Assert.AreEqual(AshStormPhase.Pending, model.Phase);
        }

        [Test]
        public void SteppedTime_PhasesInOrder_EachOnce()
        {
            var model = new AshStormModel(config, Clear, 300f);
            var phases = new List<AshStormPhase>();
            for (float t = 0f; t <= 1000f; t += 0.25f)
                if (model.Evaluate(t))
                    phases.Add(model.Phase);
            CollectionAssert.AreEqual(
                new[] { AshStormPhase.Rising, AshStormPhase.Peak, AshStormPhase.Falling, AshStormPhase.Over }, phases);
        }

        [Test]
        public void PhaseBoundaries_MatchConfig_TotalDuration220()
        {
            var model = new AshStormModel(config, Clear, 300f);
            Assert.AreEqual(520f, model.EndSec, 1e-3f, "전환 20 + Peak 180 + 전환 20");
            model.Evaluate(300f);
            Assert.AreEqual(AshStormPhase.Rising, model.Phase);
            model.Evaluate(319.99f);
            Assert.AreEqual(AshStormPhase.Rising, model.Phase);
            model.Evaluate(320f);
            Assert.AreEqual(AshStormPhase.Peak, model.Phase);
            model.Evaluate(499.99f);
            Assert.AreEqual(AshStormPhase.Peak, model.Phase);
            model.Evaluate(500f);
            Assert.AreEqual(AshStormPhase.Falling, model.Phase);
            model.Evaluate(520f);
            Assert.AreEqual(AshStormPhase.Over, model.Phase);
        }

        [Test]
        public void Peak_Visibility30_Radiation1()
        {
            var model = new AshStormModel(config, Clear, 300f);
            model.Evaluate(400f);
            Assert.AreEqual(AshStormPhase.Peak, model.Phase);
            Assert.AreEqual(1f, model.Intensity);
            Assert.AreEqual(30f, model.VisibilityM, 1e-4f, "§8.3 시야 30m");
            Assert.AreEqual(1f, model.RadiationPerSec, 1e-6f, "§8.3 방사능 +1/s");
        }

        [Test]
        public void Rising_And_Falling_AreLinear()
        {
            var model = new AshStormModel(config, Clear, 300f);
            model.Evaluate(305f);   // 상승 1/4
            Assert.AreEqual(0.25f, model.Intensity, 1e-4f);
            Assert.AreEqual(Mathf.Lerp(Clear, 30f, 0.25f), model.VisibilityM, 1e-3f);
            Assert.AreEqual(0.25f, model.RadiationPerSec, 1e-4f);
            model.Evaluate(310f);
            Assert.AreEqual(0.5f, model.Intensity, 1e-4f);
            Assert.AreEqual(240f, model.VisibilityM, 1e-3f, "lerp(450, 30, 0.5)");

            model.Evaluate(505f);   // 하강 1/4 → 강도 0.75
            Assert.AreEqual(AshStormPhase.Falling, model.Phase);
            Assert.AreEqual(0.75f, model.Intensity, 1e-4f);
            Assert.AreEqual(0.75f, model.RadiationPerSec, 1e-4f);
        }

        [Test]
        public void Visibility_DecreasesMonotonically_DuringRise()
        {
            var model = new AshStormModel(config, Clear, 0f);
            float last = float.PositiveInfinity;
            for (float t = 0.5f; t <= 20f; t += 0.5f)
            {
                model.Evaluate(t);
                Assert.Less(model.VisibilityM, last);
                last = model.VisibilityM;
            }
            Assert.AreEqual(30f, last, 1e-3f);
        }

        [Test]
        public void Over_StaysOver_NoSecondStorm()
        {
            var model = new AshStormModel(config, Clear, 300f);
            model.Evaluate(600f);
            Assert.AreEqual(AshStormPhase.Over, model.Phase);
            for (float t = 600f; t < 5000f; t += 7f)
            {
                Assert.IsFalse(model.Evaluate(t));
                Assert.AreEqual(AshStormPhase.Over, model.Phase);
                Assert.AreEqual(0f, model.Intensity);
                Assert.IsTrue(float.IsPositiveInfinity(model.VisibilityM));
                Assert.AreEqual(0f, model.RadiationPerSec);
            }
        }

        [Test]
        public void Reschedule_OnlyWhilePending()
        {
            var model = new AshStormModel(config, Clear, 900f);
            model.Evaluate(100f);
            Assert.IsTrue(model.Reschedule(100f), "Pending에서 당김");
            Assert.AreEqual(100f, model.StartSec);
            Assert.IsTrue(model.Evaluate(100f));
            Assert.AreEqual(AshStormPhase.Rising, model.Phase, "당긴 시각에 바로 시작");
            Assert.IsFalse(model.Reschedule(50f), "시작한 뒤에는 못 바꿈");
            Assert.AreEqual(100f, model.StartSec);
            model.Evaluate(1000f);
            Assert.IsFalse(model.Reschedule(2000f), "끝난 뒤에도 못 바꿈");
        }

        [Test]
        public void BigTimeJump_LandsInCorrectPhase()
        {
            var model = new AshStormModel(config, Clear, 300f);
            Assert.IsTrue(model.Evaluate(400f), "Pending → Peak 한 번에 (AdvanceTime)");
            Assert.AreEqual(AshStormPhase.Peak, model.Phase);
            Assert.AreEqual(30f, model.VisibilityM, 1e-4f);

            var model2 = new AshStormModel(config, Clear, 300f);
            Assert.IsTrue(model2.Evaluate(510f));
            Assert.AreEqual(AshStormPhase.Falling, model2.Phase);
            Assert.AreEqual(0.5f, model2.Intensity, 1e-4f);

            var model3 = new AshStormModel(config, Clear, 300f);
            Assert.IsTrue(model3.Evaluate(10000f));
            Assert.AreEqual(AshStormPhase.Over, model3.Phase);
        }

        [Test]
        public void Rewind_ReturnsStateAtThatTime()
        {
            var model = new AshStormModel(config, Clear, 300f);
            model.Evaluate(400f);
            Assert.IsTrue(model.Evaluate(250f));
            Assert.AreEqual(AshStormPhase.Pending, model.Phase);
            Assert.AreEqual(0f, model.Intensity);
        }

        [Test]
        public void ZeroTransition_JumpsStraightToPeak()
        {
            config.riseSec = 0f;
            config.fallSec = 0f;
            var model = new AshStormModel(config, Clear, 10f);
            model.Evaluate(10f);
            Assert.AreEqual(AshStormPhase.Peak, model.Phase);
            Assert.AreEqual(1f, model.Intensity);
            model.Evaluate(190f);
            Assert.AreEqual(AshStormPhase.Over, model.Phase);
        }

        [Test]
        public void NoClearVisibility_StillPeaksAt30_AndInfinityAtZero()
        {
            var model = new AshStormModel(config, float.PositiveInfinity, 0f);
            model.Evaluate(10f);
            Assert.AreEqual(60f, model.VisibilityM, 1e-3f, "시야/강도 (0.5 → 60m)");
            model.Evaluate(100f);
            Assert.AreEqual(30f, model.VisibilityM, 1e-4f);
        }

        [Test]
        public void NegativeStart_ClampsToZero()
        {
            var model = new AshStormModel(config, Clear, -50f);
            Assert.AreEqual(0f, model.StartSec);
        }
    }
}
