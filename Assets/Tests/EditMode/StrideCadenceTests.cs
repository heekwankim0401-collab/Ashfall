using Ashfall.Core;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M6 1a: 보폭 거리 기반 걸음 리듬 (M6 설계 §4.1, Q12). 거리 값은 float로 정확히 표현되는 수만 쓴다.
    public class StrideCadenceTests
    {
        private const float Stride = 1.5f;

        // 같은 거리로 n틱 돌려 걸음이 난 틱 번호(1부터)를 모은다.
        private static System.Collections.Generic.List<int> StepTicks(StrideCadence cadence, float distancePerTick, int ticks, float stride = Stride)
        {
            var steps = new System.Collections.Generic.List<int>();
            for (int i = 1; i <= ticks; i++)
            {
                if (cadence.Tick(distancePerTick, stride))
                    steps.Add(i);
            }
            return steps;
        }

        [Test]
        public void FirstStep_AfterHalfStride()
        {
            var cadence = new StrideCadence();

            // 0.25m씩: 시작 누적 0.75 → 1.0, 1.25, 1.5 → 3번째 틱(이동 0.75m = 보폭 절반)에 첫 걸음
            Assert.IsFalse(cadence.Tick(0.25f, Stride));
            Assert.IsFalse(cadence.Tick(0.25f, Stride));
            Assert.IsTrue(cadence.Tick(0.25f, Stride));
        }

        [Test]
        public void ConstantSpeed_StepsEveryStride()
        {
            var cadence = new StrideCadence();

            // 0.25m/틱, 보폭 1.5m → 첫 걸음 3틱, 이후 6틱마다
            CollectionAssert.AreEqual(new[] { 3, 9, 15, 21 }, StepTicks(cadence, 0.25f, 24));
        }

        [Test]
        public void StepCount_MatchesDistanceOverStride()
        {
            var cadence = new StrideCadence();

            // 걷기 3.0m/s(0.05m × 60틱 = 1초 단위)로 10초 → 30m / 1.5m = 20걸음(첫 걸음이 반 보폭 앞당겨져 20)
            int steps = StepTicks(cadence, 0.05f, 600).Count;
            Assert.AreEqual(20, steps);
        }

        [Test]
        public void LongFrame_AtMostOneStep_NoBacklog()
        {
            var cadence = new StrideCadence();
            cadence.Tick(0.25f, Stride);

            // 한 틱에 보폭 여러 배 → 1걸음, 밀린 거리는 버린다
            Assert.IsTrue(cadence.Tick(10f, Stride));
            Assert.AreEqual(0f, cadence.AccumulatedM);
            // 다음 걸음은 다시 보폭 하나를 가야 난다
            CollectionAssert.AreEqual(new[] { 6 }, StepTicks(cadence, 0.25f, 6));
        }

        [Test]
        public void Stop_ResetsToHalfStride()
        {
            var cadence = new StrideCadence();
            StepTicks(cadence, 0.25f, 5);   // 3틱에 걸음, 누적 0.5

            Assert.IsFalse(cadence.Tick(0f, Stride));
            Assert.IsFalse(cadence.IsMoving);

            // 다시 움직이면 처음처럼 반 보폭 뒤 첫 걸음
            CollectionAssert.AreEqual(new[] { 3 }, StepTicks(cadence, 0.25f, 3));
        }

        [Test]
        public void Reset_SameAsStop()
        {
            var cadence = new StrideCadence();
            StepTicks(cadence, 0.25f, 5);

            cadence.Reset();

            Assert.IsFalse(cadence.IsMoving);
            Assert.AreEqual(0f, cadence.AccumulatedM);
            CollectionAssert.AreEqual(new[] { 3 }, StepTicks(cadence, 0.25f, 3));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void NonPositiveStride_NeverSteps(float stride)
        {
            var cadence = new StrideCadence();

            CollectionAssert.IsEmpty(StepTicks(cadence, 5f, 10, stride));
            Assert.IsFalse(cadence.IsMoving);
        }

        [TestCase(-0.5f)]
        [TestCase(float.NaN)]
        public void NegativeOrNaNDistance_IsStop(float distance)
        {
            var cadence = new StrideCadence();
            cadence.Tick(0.25f, Stride);

            Assert.IsFalse(cadence.Tick(distance, Stride));
            Assert.IsFalse(cadence.IsMoving);
        }

        [Test]
        public void StrideChange_UsesNewStride()
        {
            var cadence = new StrideCadence();

            // 걷기 보폭으로 시작(누적 0.75 + 0.25 = 1.0) → 웅크림 보폭 0.8로 바뀌면 바로 걸음
            Assert.IsFalse(cadence.Tick(0.25f, Stride));
            Assert.IsTrue(cadence.Tick(0.25f, 0.8f));
        }
    }
}
