using System;
using System.Linq;
using Ashfall.Core;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // §5.1 내구도·고장 (M7 7A, Q9~Q18): WeaponModel의 WeaponWear 옵션, WeaponItem.Durability/IsJammed
    public class WeaponModelWearTests
    {
        private const float Threshold = 50f;
        private const float PerPoint = 0.001f;

        private WeaponDefinitionSO rifle;
        private MagazineDefinitionSO mag30;
        private LocalMagazineSource source;

        [SetUp]
        public void SetUp()
        {
            rifle = TestData.Rifle();
            rifle.durabilityLossPerShot = 0.05f;
            mag30 = TestData.Magazine30();
        }

        private WeaponModel NewRifle(float durability, WeaponWear wear, int spares = 1)
        {
            source = new LocalMagazineSource(Enumerable.Range(0, spares).Select(_ => Magazine.Full(mag30)));
            var item = new WeaponItem(rifle, Magazine.Full(mag30)) { Durability = durability };
            return new WeaponModel(item, source, wear: wear);
        }

        private static WeaponWear Gdd(int seed = 1) => new WeaponWear(Threshold, PerPoint, new Random(seed));

        // 내구도 0에서 반드시 고장 나는 옵션 (확률/점 1 → 1로 자름)
        private static WeaponWear AlwaysJam() => new WeaponWear(Threshold, 1f, new Random(1));

        // 반자동처럼 한 번씩 당긴다 (자동 무기도 같은 결과)
        private static bool Pull(WeaponModel weapon, ref float t)
        {
            weapon.TryFire(t, false);
            t += 0.2f;
            bool fired = weapon.TryFire(t, true);
            t += 0.2f;
            return fired;
        }

        // ── WeaponItem ───────────────────────────────────────

        [Test]
        public void WeaponItem_DefaultDurability100_NotJammed()
        {
            var item = new WeaponItem(rifle);
            Assert.AreEqual(100f, item.Durability);
            Assert.IsFalse(item.IsJammed);
            Assert.AreEqual(100f, ((WeaponItem)ItemFactory.Create(rifle)).Durability, "Q12: 상인·제작·키트·상자 = 100");
        }

        [TestCase(150f, 100f)]
        [TestCase(-5f, 0f)]
        [TestCase(float.NaN, 100f)]
        [TestCase(42.5f, 42.5f)]
        public void WeaponItem_Durability_Clamped(float set, float expected)
        {
            var item = new WeaponItem(rifle) { Durability = set };
            Assert.AreEqual(expected, item.Durability);
        }

        [Test]
        public void WeaponWear_NullRandom_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new WeaponWear(Threshold, PerPoint, null));
            Assert.IsFalse(WeaponWear.None.Enabled);
            Assert.IsFalse(default(WeaponWear).Enabled);
        }

        // ── 마모 ─────────────────────────────────────────────

        [Test]
        public void Wear_EachShot_LosesDurabilityPerShot()
        {
            WeaponModel weapon = NewRifle(100f, Gdd());
            float t = 0f;
            for (int i = 0; i < 10; i++)
                Assert.IsTrue(Pull(weapon, ref t));
            Assert.AreEqual(100f - 10 * 0.05f, weapon.Item.Durability, 1e-4f);
            Assert.AreEqual(20, weapon.Current.Rounds);
        }

        [Test]
        public void Wear_DoesNotGoBelowZero()
        {
            rifle.durabilityLossPerShot = 30f;
            WeaponModel weapon = NewRifle(100f, new WeaponWear(Threshold, 0f, new Random(1)));   // 확률 0 = 고장 없음
            float t = 0f;
            for (int i = 0; i < 5; i++)
                Pull(weapon, ref t);
            Assert.AreEqual(0f, weapon.Item.Durability);
        }

        [Test]
        public void Wear_DryTriggerOnEmptyMagazine_NoWear()
        {
            WeaponModel weapon = NewRifle(100f, Gdd(), spares: 0);
            weapon.Item.LoadedMagazine = new Magazine(mag30, 0);
            float t = 0f;
            Assert.IsFalse(Pull(weapon, ref t));
            Assert.AreEqual(100f, weapon.Item.Durability);
        }

        [Test]
        public void NoWearOption_SameAsBefore_NoWearNoJam()
        {
            WeaponModel weapon = NewRifle(0f, WeaponWear.None);
            Assert.IsFalse(weapon.WearEnabled);
            int shots = 0;
            for (float t = 0f; t < 10f; t += 0.01f)
                if (weapon.TryFire(t, true)) shots++;
            Assert.AreEqual(30, shots, "옵션 없음 = 기존 동작 (적 무기, Q18)");
            Assert.AreEqual(0f, weapon.Item.Durability);
            Assert.IsFalse(weapon.IsJammed);
        }

        [Test]
        public void TwoArgConstructor_HasNoWear()
        {
            var item = new WeaponItem(rifle, Magazine.Full(mag30)) { Durability = 0f };
            var weapon = new WeaponModel(item, new LocalMagazineSource(null));
            Assert.IsFalse(weapon.WearEnabled);
            float t = 0f;
            Assert.IsTrue(Pull(weapon, ref t));
            Assert.AreEqual(0f, item.Durability);
        }

        // ── 고장 ─────────────────────────────────────────────

        [Test]
        public void Jam_NoRoundSpent_NoShot_EventOnce()
        {
            WeaponModel weapon = NewRifle(0f, AlwaysJam());
            int jamEvents = 0;
            weapon.Jammed += () => jamEvents++;
            float t = 0f;

            Assert.IsFalse(Pull(weapon, ref t), "고장 = 발사 없음");
            Assert.IsTrue(weapon.IsJammed);
            Assert.IsTrue(weapon.Item.IsJammed);
            Assert.AreEqual(30, weapon.Current.Rounds, "Q9: 탄을 쓰지 않는다");
            Assert.AreEqual(0f, weapon.Item.Durability, "발사 안 됨 = 마모 없음");
            Assert.AreEqual(1, jamEvents);

            Assert.IsFalse(Pull(weapon, ref t), "고장 중 방아쇠 = 발사 없음");
            Assert.AreEqual(30, weapon.Current.Rounds);
            Assert.AreEqual(1, jamEvents, "고장 중에는 다시 굴리지 않는다");
        }

        [Test]
        public void Jam_BlocksReload()
        {
            WeaponModel weapon = NewRifle(0f, AlwaysJam(), spares: 2);
            float t = 0f;
            Pull(weapon, ref t);
            Assert.IsTrue(weapon.IsJammed);
            Assert.IsFalse(weapon.CanReload);
            Assert.IsFalse(weapon.PressReload());
            Assert.IsFalse(weapon.IsReloading);
        }

        [Test]
        public void ClearJam_RestoresFiring()
        {
            WeaponModel weapon = NewRifle(0f, AlwaysJam());
            float t = 0f;
            Pull(weapon, ref t);
            Assert.IsTrue(weapon.ClearJam());
            Assert.IsFalse(weapon.IsJammed);
            Assert.IsFalse(weapon.ClearJam(), "고장이 아니면 false");

            weapon.Item.Durability = 100f;   // 확률 0
            Assert.IsTrue(Pull(weapon, ref t));
            Assert.AreEqual(29, weapon.Current.Rounds);
            Assert.IsTrue(weapon.CanReload);
        }

        [Test]
        public void Jam_AboveThreshold_Never()
        {
            rifle.durabilityLossPerShot = 0f;   // 마모로 50 아래로 내려가지 않게
            WeaponModel weapon = NewRifle(50f, new WeaponWear(Threshold, 1f, new Random(3)));
            int shots = 0;
            for (float t = 0f; t < 10f; t += 0.01f)
                if (weapon.TryFire(t, true)) shots++;
            Assert.AreEqual(30, shots);
            Assert.IsFalse(weapon.IsJammed);
        }

        [Test]
        public void Jam_GddChance_Seeded_EventuallyJamsAndIsDeterministic()
        {
            int FirstJamShot(int seed)
            {
                rifle.durabilityLossPerShot = 0f;
                var item = new WeaponItem(rifle, new Magazine(mag30, 30)) { Durability = 0f };   // 5%
                var weapon = new WeaponModel(item, new LocalMagazineSource(null), wear: Gdd(seed));
                float t = 0f;
                for (int shot = 0; shot < 2000; shot++)
                {
                    if (item.LoadedMagazine.IsEmpty)
                        item.LoadedMagazine = Magazine.Full(mag30);
                    if (!Pull(weapon, ref t))
                        return weapon.IsJammed ? shot : -1;
                }
                return -1;
            }

            int first = FirstJamShot(2024);
            Assert.GreaterOrEqual(first, 0, "5%면 2000발 안에 고장 난다");
            Assert.AreEqual(first, FirstJamShot(2024), "같은 시드 = 같은 결과");
        }

        [Test]
        public void JamState_IsOnItem_SharedByNewModel()
        {
            WeaponModel weapon = NewRifle(0f, AlwaysJam());
            float t = 0f;
            Pull(weapon, ref t);
            var again = new WeaponModel(weapon.Item, source);
            Assert.IsTrue(again.IsJammed, "고장은 아이템 상태 — 다시 들어도 고장");
            float t2 = 10f;
            Assert.IsFalse(Pull(again, ref t2));
        }
    }
}
