#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Ashfall.Core;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M7 7A-2a WPN-SHT-01 튜브 한 발씩 장전 (Q5~Q7): 코드 장면(인벤토리 없음) — 탄 상자는 LocalMagazineSource(계약)가 준다.
    // 인벤토리 상자 순서(조끼 → 주머니 → 배낭)는 7A-2b PlayerInventoryLooseAmmoTests 몫.
    public class TubeReloadFlowTests
    {
        private const float Dt = 1f / 60f;
        private const int Seed = 4242;

        private WeaponCodeScene scene;
        private SimInput input;

        private WeaponController Weapon => scene.Weapon;
        private WeaponModel Model => scene.Weapon.Model;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new WeaponCodeScene();
            yield return WeaponCodeScene.Create(scene,
                M7WeaponTestAssets.Load<WeaponDefinitionSO>(M7WeaponTestAssets.ShotgunPath),
                M7WeaponTestAssets.Load<AmmoDefinitionSO>(M7WeaponTestAssets.Ammo12GaPath),
                M7WeaponTestAssets.Load<MagazineDefinitionSO>(M7WeaponTestAssets.Box12GaPath), 2,
                M7WeaponTestAssets.Load<WeaponHandlingConfigSO>(M7WeaponTestAssets.HandlingPath), Seed);
            input = new SimInput();
            Time.captureDeltaTime = Dt;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            input?.Dispose();
            input = null;
            Time.captureDeltaTime = 0f;
            scene?.Destroy();
        }

        private void EmptyTube()
        {
            while (Model.Current.TakeRound()) { }
        }

        [UnityTest]
        public IEnumerator Start_TubeFull_TwoBoxes()
        {
            Assert.IsTrue(Model.IsTubeReload);
            Assert.AreEqual(6, Model.Current.Capacity, "튜브 6 (GDD)");
            Assert.IsTrue(Model.Current.IsFull, "인벤토리 없는 씬: 튜브 가득");
            Assert.AreEqual(2, Weapon.MagazineSource.Magazines.Count);
            Assert.IsTrue(Weapon.MagazineSource.Magazines.All(m => m.Definition.isAmmoBox && m.IsFull));
            Assert.AreEqual(AmmoType.Gauge12, Weapon.CurrentAmmo.type);
            Assert.AreEqual(8, Weapon.CurrentAmmo.pelletCount);
            yield break;
        }

        // 상자 2개(3발·8발) → R 한 번에 6발: 적은 상자부터, 발당 0.5초, 가득 차면 끝. R 재입력 무시(빠른 재장전 없음).
        [UnityTest]
        public IEnumerator Reload_FromTwoBoxes_SixRounds_OneByOne()
        {
            EmptyTube();
            Magazine small = Weapon.MagazineSource.Magazines[0];
            Magazine large = Weapon.MagazineSource.Magazines[1];
            for (int i = 0; i < 5; i++)
                small.TakeRound();
            Assert.AreEqual(3, small.Rounds);
            Assert.AreEqual(8, large.Rounds);

            int startFrame = Time.frameCount;
            yield return input.Keyboard.Tap(Key.R);
            Assert.IsTrue(Model.IsReloading);
            yield return input.Keyboard.Tap(Key.R);
            Assert.IsFalse(Model.IsFastReload, "튜브는 빠른 재장전 없음");

            yield return M2Scene.WaitUntil(() => Model.Current.Rounds >= 1, 2f);
            float firstSec = (Time.frameCount - startFrame) * Dt;
            Assert.AreEqual(Model.Item.Definition.roundReloadTimeSec, firstSec, 3f * Dt, "한 발 0.5초");

            yield return M2Scene.WaitUntil(() => !Model.IsReloading, 6f);
            float totalSec = (Time.frameCount - startFrame) * Dt;
            Assert.AreEqual(6, Model.Current.Rounds, "가득");
            Assert.AreEqual(6, Model.RoundsLoadedThisReload);
            Assert.AreEqual(6f * Model.Item.Definition.roundReloadTimeSec, totalSec, 3f * Dt, "6발 × 0.5초");
            Assert.AreEqual(0, small.Rounds, "탄이 적은 상자부터");
            Assert.AreEqual(5, large.Rounds);
            Assert.AreEqual(2, Weapon.MagazineSource.Magazines.Count, "빈 상자도 남는다 (Q6)");
        }

        // Q7: 장전 중 방아쇠 → 1발 이상이면 장전 중단 후 사격(펠릿 8개, ShotFired 1). 0발이면 무시.
        [UnityTest]
        public IEnumerator FireDuringReload_InterruptsWhenLoaded_IgnoredWhenEmpty()
        {
            EmptyTube();
            using var shots = new EventLog<ShotFired>(scene.Events);
            using var noises = new EventLog<NoiseEmitted>(scene.Events);
            using var clicks = new EventLog<WeaponClicked>(scene.Events);
            yield return input.Keyboard.Tap(Key.R);
            Assert.IsTrue(Model.IsReloading);

            yield return input.Mouse.ClickLeft();
            Assert.AreEqual(0, shots.Count, "0발 — 사격 무시");
            Assert.IsTrue(Model.IsReloading, "장전 계속");
            Assert.AreEqual(0, clicks.Count, "장전 중 헛격발 소리 없음");

            yield return M2Scene.WaitUntil(() => Model.Current.Rounds >= 2, 3f);
            int loaded = Model.Current.Rounds;
            int boxRounds = Weapon.MagazineSource.Magazines.Sum(m => m.Rounds);
            yield return input.Mouse.ClickLeft();
            Assert.AreEqual(1, shots.Count, "장전 중단 후 사격");
            Assert.IsFalse(Model.IsReloading, "장전 중단");
            Assert.AreEqual(loaded - 1, Model.Current.Rounds, "넣은 탄 유지 − 1발");
            Assert.AreEqual(boxRounds, Weapon.MagazineSource.Magazines.Sum(m => m.Rounds), "진행 중인 한 발은 꺼내지 않았다");
            Assert.AreEqual(1, noises.Received.Count(n => n.Kind == NoiseKind.Gunshot), "펠릿 수와 상관없이 소음 1회");

            yield return M2Scene.WaitSeconds(1f);
            Assert.IsFalse(Model.IsReloading, "중단 뒤 저절로 다시 장전하지 않는다");
            Assert.AreEqual(loaded - 1, Model.Current.Rounds);
        }

        // 낱탄이 다 떨어지면 장전 끝 (튜브가 덜 차도)
        [UnityTest]
        public IEnumerator Reload_StopsWhenBoxesEmpty()
        {
            EmptyTube();
            foreach (Magazine box in Weapon.MagazineSource.Magazines)
                while (box.Rounds > 1)
                    box.TakeRound();
            yield return input.Keyboard.Tap(Key.R);
            yield return M2Scene.WaitUntil(() => !Model.IsReloading, 4f);
            Assert.AreEqual(2, Model.Current.Rounds, "상자 1발 + 1발");
            Assert.IsFalse(Model.CanReload, "낱탄 없음");
            yield return input.Keyboard.Tap(Key.R);
            Assert.IsFalse(Model.IsReloading);
        }

        // RULE-04: 튜브도 탄창처럼 T로 대략치
        [UnityTest]
        public IEnumerator MagazineCheck_ClassifiesTube()
        {
            using var completed = new EventLog<MagazineCheckCompleted>(scene.Events);
            yield return input.Keyboard.Tap(Key.T);
            yield return M2Scene.WaitUntil(() => completed.Count == 1, 3f);
            Assert.AreEqual(MagazineFillLevel.Full, completed.Last.Level, "6/6");
            EmptyTube();
            yield return input.Keyboard.Tap(Key.T);
            yield return M2Scene.WaitUntil(() => completed.Count == 2, 3f);
            Assert.AreEqual(MagazineFillLevel.AlmostEmpty, completed.Last.Level, "0/6");
        }
    }
}
#endif
