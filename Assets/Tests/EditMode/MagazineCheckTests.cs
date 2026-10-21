using Ashfall.Core;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // RULE-04 탄창 확인 (T 0.8초 → 가득/절반/거의 없음), 조준 흔들림 (설계 D12)
    public class MagazineCheckTests
    {
        private WeaponHandlingConfigSO handling;
        private MagazineDefinitionSO mag30;

        [SetUp]
        public void SetUp()
        {
            handling = TestData.WeaponHandling();
            mag30 = TestData.Magazine30();
        }

        [Test]
        public void Check_CompletesAfter0_8Seconds_Once()
        {
            var check = new MagazineCheckModel(handling);
            Assert.IsTrue(check.TryStart(false));
            Assert.IsFalse(check.Tick(0.79f));
            Assert.IsTrue(check.IsChecking);

            var exact = new MagazineCheckModel(handling);
            exact.TryStart(false);
            Assert.IsTrue(exact.Tick(0.8f));
            Assert.IsFalse(exact.IsChecking);
            Assert.IsFalse(exact.Tick(1f), "결과는 한 번만");
        }

        [Test]
        public void Check_RefusedWhileReloading_OrAlreadyChecking()
        {
            var check = new MagazineCheckModel(handling);
            Assert.IsFalse(check.TryStart(true));
            Assert.IsFalse(check.IsChecking);
            Assert.IsTrue(check.TryStart(false));
            Assert.IsFalse(check.TryStart(false));
        }

        [Test]
        public void Cancel_NoResult()
        {
            var check = new MagazineCheckModel(handling);
            check.TryStart(false);
            check.Tick(0.5f);
            check.Cancel();
            Assert.IsFalse(check.Tick(1f));
            Assert.IsFalse(check.IsChecking);
        }

        // 경계는 SO 값(0.67 / 0.34). 20/30 = 0.667 < 0.67 → 절반 (설계 §7의 "20/30 → 가득"과 다름, 2a 메모 참고)
        [TestCase(30, MagazineFillLevel.Full)]
        [TestCase(21, MagazineFillLevel.Full)]
        [TestCase(20, MagazineFillLevel.Half)]
        [TestCase(15, MagazineFillLevel.Half)]
        [TestCase(11, MagazineFillLevel.Half)]
        [TestCase(10, MagazineFillLevel.AlmostEmpty)]
        [TestCase(5, MagazineFillLevel.AlmostEmpty)]
        [TestCase(0, MagazineFillLevel.AlmostEmpty)]
        public void Classify_ByRatio(int rounds, MagazineFillLevel expected)
        {
            Assert.AreEqual(expected, MagazineCheckModel.Classify(new Magazine(mag30, rounds), handling));
        }

        [Test]
        public void Classify_NoMagazine()
        {
            Assert.AreEqual(MagazineFillLevel.NoMagazine, MagazineCheckModel.Classify(null, handling));
        }

        [Test]
        public void AimSway_BoundedByBaseTimesMultiplier()
        {
            for (int i = 0; i < 500; i++)
            {
                float t = i * 0.037f;
                Vector2 o = AimSway.Offset(t, 0.15f, 2f, 0.5f, 7);
                Assert.LessOrEqual(Mathf.Abs(o.x), 0.3f + 1e-5f);
                Assert.LessOrEqual(Mathf.Abs(o.y), 0.3f + 1e-5f);
            }
        }

        [Test]
        public void AimSway_ScalesLinearlyWithMultiplier()
        {
            Vector2 one = AimSway.Offset(3.3f, 0.15f, 1f, 0.5f, 3);
            Vector2 two = AimSway.Offset(3.3f, 0.15f, 2f, 0.5f, 3);
            Assert.AreEqual(one.x * 2f, two.x, 1e-5f);
            Assert.AreEqual(one.y * 2f, two.y, 1e-5f);
            Assert.AreEqual(Vector2.zero, AimSway.Offset(3.3f, 0.15f, 0f, 0.5f, 3));
        }

        [Test]
        public void AimSway_MovesOverTime()
        {
            bool moved = false;
            Vector2 first = AimSway.Offset(0f, 0.15f, 1f, 0.5f, 1);
            for (int i = 1; i < 20 && !moved; i++)
                moved = (AimSway.Offset(i * 0.25f, 0.15f, 1f, 0.5f, 1) - first).sqrMagnitude > 1e-8f;
            Assert.IsTrue(moved);
        }
    }
}
