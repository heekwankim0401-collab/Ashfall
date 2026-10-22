using Ashfall.Core;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // ILooseAmmoSource — Core LocalMagazineSource (M7 7A, Q5·Q6): 탄 상자만, 가장 적은 상자부터, 탄창 재장전에서 상자 제외
    public class LooseAmmoSourceTests
    {
        private MagazineDefinitionSO box;
        private MagazineDefinitionSO mag9;

        [SetUp]
        public void SetUp()
        {
            box = M7TestData.Box12Ga8();
            mag9 = M3TestData.Mag9_12();
        }

        [Test]
        public void HasLooseRound_OnlyBoxesOfThatAmmo()
        {
            MagazineDefinitionSO plain12 = M7TestData.Box12Ga8();
            plain12.isAmmoBox = false;

            Assert.IsFalse(new LocalMagazineSource(new[] { Magazine.Full(plain12) }).HasLooseRound(AmmoType.Gauge12), "일반 탄창은 낱탄 아님");
            Assert.IsFalse(new LocalMagazineSource(new[] { Magazine.Full(mag9) }).HasLooseRound(AmmoType.Pistol9mm));
            Assert.IsFalse(new LocalMagazineSource(new[] { Magazine.Full(box) }).HasLooseRound(AmmoType.Pistol9mm), "탄 종류가 다름");
            Assert.IsTrue(new LocalMagazineSource(new[] { Magazine.Full(box) }).HasLooseRound(AmmoType.Gauge12));
            Assert.IsFalse(new LocalMagazineSource(new[] { new Magazine(box, 0) }).HasLooseRound(AmmoType.Gauge12), "빈 상자");
            Assert.IsFalse(new LocalMagazineSource(null).HasLooseRound(AmmoType.Gauge12));
        }

        [Test]
        public void TakeLooseRound_LeastFilledBoxFirst()
        {
            var five = new Magazine(box, 5);
            var two = new Magazine(box, 2);
            var seven = new Magazine(box, 7);
            var source = new LocalMagazineSource(new[] { five, two, seven });

            Assert.IsTrue(source.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(1, two.Rounds);
            Assert.IsTrue(source.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(0, two.Rounds);
            Assert.IsTrue(source.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(4, five.Rounds, "빈 상자를 건너뛰고 다음으로 적은 상자");
            Assert.AreEqual(7, seven.Rounds);
            Assert.AreEqual(3, source.Magazines.Count, "Q6: 빈 상자도 목록에 남는다");
        }

        [Test]
        public void TakeLooseRound_Tie_FirstInList()
        {
            var a = new Magazine(box, 4);
            var b = new Magazine(box, 4);
            var source = new LocalMagazineSource(new[] { a, b });
            source.TakeLooseRound(AmmoType.Gauge12);
            Assert.AreEqual(3, a.Rounds);
            Assert.AreEqual(4, b.Rounds);
        }

        [Test]
        public void TakeLooseRound_None_False()
        {
            var source = new LocalMagazineSource(new[] { new Magazine(box, 0), Magazine.Full(mag9) });
            Assert.IsFalse(source.TakeLooseRound(AmmoType.Gauge12));
            Assert.IsFalse(source.TakeLooseRound(AmmoType.Pistol9mm), "일반 탄창에서 낱탄을 빼지 않는다");
            Assert.AreEqual(12, ((Magazine)source.Magazines[1]).Rounds);
        }

        [Test]
        public void Fits_ExcludesBoxes_FromMagazineReload()
        {
            var b = Magazine.Full(box);
            var source = new LocalMagazineSource(new[] { b });
            Assert.IsFalse(source.HasLoadable(AmmoType.Gauge12), "상자는 무기에 넣을 수 없다");
            Assert.IsNull(source.TakeFullest(AmmoType.Gauge12));
            Assert.AreEqual(1, source.Magazines.Count);
        }

        [Test]
        public void MixedList_MagazinesAndBoxes_Independent()
        {
            var m = new Magazine(mag9, 6);
            var b = new Magazine(box, 3);
            var source = new LocalMagazineSource(new[] { b, m });
            Assert.IsTrue(source.HasLoadable(AmmoType.Pistol9mm));
            Assert.IsTrue(source.HasLooseRound(AmmoType.Gauge12));
            Assert.AreSame(m, source.TakeFullest(AmmoType.Pistol9mm));
            Assert.IsTrue(source.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(2, b.Rounds);
        }

        [Test]
        public void LocalMagazineSource_IsLooseAmmoSource()
        {
            Assert.IsInstanceOf<ILooseAmmoSource>(new LocalMagazineSource(null));
        }
    }
}
