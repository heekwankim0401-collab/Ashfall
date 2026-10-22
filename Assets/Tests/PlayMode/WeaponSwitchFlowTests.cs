#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M7 7A-2a 무기 교체 1/2/3 (SYS-WPN §12.1, Q1·Q2·Q3·Q8) + 5종 모두 플레이어 사격 (AC1, M5 Q16 해소).
    // M2 확인 씬(인벤토리 있음)에서 코드로 장착(PlayerInventory.TryEquipNew)하고 실제 키(가상 키보드·마우스)로 교체·사격한다.
    public class WeaponSwitchFlowTests
    {
        private const float Dt = 1f / 60f;
        private const float SwitchSec = 0.5f;   // WeaponHandlingConfig 기본값 (TODO(GDD-?) Q1)

        private M2Scene scene;
        private SimInput input;

        private WeaponController Weapon => scene.Weapon;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M2Scene();
            yield return M2Scene.Load(scene);
            input = new SimInput();
            Time.captureDeltaTime = Dt;
            scene.Motor.AddRecoil(45f, 0f);   // 하늘로 쏜다 (표적·벽에 맞지 않게)
            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            input?.Dispose();
            input = null;
            Time.captureDeltaTime = 0f;
        }

        private WeaponItem Equip(string weaponPath, string magazinePath, EquipSlot slot)
        {
            WeaponItem item = M7WeaponTestAssets.NewWeapon(weaponPath, magazinePath);
            Assert.IsTrue(scene.Inventory.TryEquipNew(item, slot), $"{item.Definition.id} → {slot} 장착");
            return item;
        }

        private IEnumerator Press(Key key) => input.Keyboard.Tap(key);

        private IEnumerator WaitSwitchDone() => M2Scene.WaitUntil(() => !Weapon.IsSwitching, 3f);

        [UnityTest]
        public IEnumerator Start_HoldsPrimary1_NotSwitching()
        {
            var rifle = scene.Inventory.GetEquipped(EquipSlot.Primary1) as WeaponItem;
            Assert.IsNotNull(rifle);
            Assert.AreEqual(EquipSlot.Primary1, Weapon.ActiveSlot);
            Assert.AreSame(rifle, Weapon.Model.Item);
            Assert.IsFalse(Weapon.IsSwitching, "레이드 시작 무기는 교체 시간 없음 (Q2)");
            Assert.IsNotNull(Weapon.CurrentAmmo, "탄약 표 (Q3)");
            Assert.AreEqual(rifle.Definition.ammo, Weapon.CurrentAmmo.type);
            yield break;
        }

        // Q1: 빈 슬롯·든 슬롯 키 무시, 교체 0.5초 동안 사격 불가, 끝나면 그 무기로 쏜다.
        [UnityTest]
        public IEnumerator Switch_EmptyAndSameSlotIgnored_SwitchTimeBlocksFire()
        {
            var rifle = (WeaponItem)Weapon.Model.Item;
            yield return Press(Key.Digit3);
            Assert.AreEqual(EquipSlot.Primary1, Weapon.ActiveSlot, "빈 Secondary 키 무시");
            Assert.IsFalse(Weapon.IsSwitching);
            yield return Press(Key.Digit1);
            Assert.IsFalse(Weapon.IsSwitching, "든 슬롯 키 무시");
            Assert.AreSame(rifle, Weapon.Model.Item);

            WeaponItem smg = Equip(M7WeaponTestAssets.SmgPath, M7WeaponTestAssets.Mag9mm30Path, EquipSlot.Primary2);
            Assert.AreSame(rifle, Weapon.Model.Item, "다른 슬롯 장착은 손에 든 무기를 바꾸지 않는다");

            using var shots = new EventLog<ShotFired>(scene.Events);
            int startFrame = Time.frameCount;
            input.Keyboard.Hold(Key.Digit2);
            yield return null;
            input.Keyboard.Release(Key.Digit2);
            Assert.AreEqual(EquipSlot.Primary2, Weapon.ActiveSlot);
            Assert.AreSame(smg, Weapon.Model.Item, "교체 중에도 새 무기를 가리킨다");
            Assert.IsTrue(Weapon.IsSwitching);
            Assert.AreEqual(0f, Weapon.AimProgress);

            yield return input.Mouse.ClickLeft();
            Assert.AreEqual(0, shots.Count, "교체 중 사격 불가");
            yield return WaitSwitchDone();
            float switchSec = (Time.frameCount - startFrame) * Dt;
            Assert.AreEqual(SwitchSec, switchSec, 2.5f * Dt, "교체 시간 0.5초");

            yield return input.Mouse.ClickLeft();
            Assert.AreEqual(1, shots.Count);
            Assert.AreSame(smg.Definition, shots.Last.Weapon, "SMG로 쐈다");
            Assert.AreEqual(29, smg.LoadedMagazine.Rounds);
        }

        // M7 리뷰 7A 낮음 5: 좌클릭을 누른 채 반자동 무기로 교체가 끝나도 새로 당기기 전에는 쏘지 않는다.
        [UnityTest]
        public IEnumerator TriggerHeldThroughSwitch_SemiWeapon_NoShotUntilNewPull()
        {
            WeaponItem dmr = Equip(M7WeaponTestAssets.DmrPath, M7WeaponTestAssets.Mag762_5Path, EquipSlot.Primary2);
            Assert.AreEqual(FireMode.Semi, dmr.Definition.modes[0], "DMR = 반자동");

            using var shots = new EventLog<ShotFired>(scene.Events);
            input.Mouse.SetLeft(true);
            yield return null;
            int heldShots = shots.Count;   // 들고 있던 소총이 누른 순간 쏜 발 (0 또는 1)
            yield return Press(Key.Digit2);
            Assert.AreSame(dmr, Weapon.Model.Item);
            Assert.IsTrue(Weapon.IsSwitching);
            int beforeSwitchEnd = shots.Count;
            yield return WaitSwitchDone();
            for (int i = 0; i < 10; i++)
                yield return null;
            Assert.AreEqual(beforeSwitchEnd, shots.Count, "누른 채 교체가 끝나도 반자동은 쏘지 않는다");
            Assert.AreEqual(5, dmr.LoadedMagazine.Rounds);
            Assert.GreaterOrEqual(beforeSwitchEnd, heldShots);

            input.Mouse.SetLeft(false);
            yield return null;
            yield return input.Mouse.ClickLeft();
            Assert.AreEqual(beforeSwitchEnd + 1, shots.Count, "새로 당기면 1발");
            Assert.AreSame(dmr.Definition, shots.Last.Weapon);
            Assert.AreEqual(4, dmr.LoadedMagazine.Rounds);
        }

        // 교체 = 진행 중 재장전·탄창 확인 취소(탄창 그대로), 다시 들면 같은 모델(캐시)
        [UnityTest]
        public IEnumerator Switch_CancelsReloadAndCheck_KeepsMagazine_ModelCached()
        {
            WeaponModel rifleModel = Weapon.Model;
            Magazine loaded = rifleModel.Current;
            for (int i = 0; i < 10; i++)
                loaded.TakeRound();
            Equip(M7WeaponTestAssets.SmgPath, M7WeaponTestAssets.Mag9mm30Path, EquipSlot.Primary2);
            int vestMagazines = scene.StorageGrid(GridKind.TacticalVest).Items.Count(p => p.Item is Magazine);

            Assert.IsTrue(rifleModel.PressReload());
            yield return Press(Key.Digit2);
            Assert.IsFalse(rifleModel.IsReloading, "교체 → 재장전 취소");
            Assert.AreSame(loaded, rifleModel.Current, "장착 탄창 그대로");
            Assert.AreEqual(20, loaded.Rounds);
            Assert.AreEqual(vestMagazines, scene.StorageGrid(GridKind.TacticalVest).Items.Count(p => p.Item is Magazine), "예비 탄창 그대로");

            yield return WaitSwitchDone();
            yield return Press(Key.T);
            Assert.IsTrue(Weapon.MagazineCheck.IsChecking);
            yield return Press(Key.Digit1);
            Assert.IsFalse(Weapon.MagazineCheck.IsChecking, "교체 → 탄창 확인 취소");
            Assert.AreSame(rifleModel, Weapon.Model, "다시 들면 같은 WeaponModel (연사 간격 상태 유지)");
            yield return WaitSwitchDone();
        }

        // 교체 중 R·T·조준도 막힌다
        [UnityTest]
        public IEnumerator DuringSwitch_ReloadCheckAimBlocked()
        {
            WeaponModel rifleModel = Weapon.Model;
            Equip(M7WeaponTestAssets.SmgPath, M7WeaponTestAssets.Mag9mm30Path, EquipSlot.Primary2);
            yield return Press(Key.Digit2);
            yield return WaitSwitchDone();
            yield return Press(Key.Digit1);   // 소총으로 돌아가는 중 (조끼에 5.56 탄창이 있다)
            Assert.IsTrue(Weapon.IsSwitching);
            Assert.AreSame(rifleModel, Weapon.Model);
            Weapon.Model.Current.TakeRound();
            Assert.IsTrue(Weapon.Model.CanReload, "재장전 가능한 상태");
            yield return Press(Key.R);
            Assert.IsFalse(Weapon.Model.IsReloading, "교체 중 R 무시");
            yield return Press(Key.T);
            Assert.IsFalse(Weapon.MagazineCheck.IsChecking, "교체 중 T 무시");
            input.Mouse.SetRight(true);
            yield return null;
            yield return null;
            Assert.AreEqual(0f, Weapon.AimProgress, "교체 중 조준 불가");
            input.Mouse.SetRight(false);
            yield return WaitSwitchDone();
        }

        // 든 슬롯의 무기를 빼면 비무장, 그 슬롯에 넣으면 바로 들되 교체 시간을 둔다
        [UnityTest]
        public IEnumerator ActiveSlotEmptied_Unarmed_Refilled_AppliesSwitchTime()
        {
            var rifle = (WeaponItem)Weapon.Model.Item;
            Assert.IsTrue(scene.Inventory.Remove(rifle));
            Assert.IsFalse(Weapon.IsArmed);
            Assert.AreEqual(EquipSlot.Primary1, Weapon.ActiveSlot, "든 슬롯은 그대로");

            WeaponItem dmr = Equip(M7WeaponTestAssets.DmrPath, M7WeaponTestAssets.Mag762_5Path, EquipSlot.Primary1);
            Assert.IsTrue(Weapon.IsArmed);
            Assert.AreSame(dmr, Weapon.Model.Item);
            Assert.IsTrue(Weapon.IsSwitching, "넣은 무기도 교체 시간");

            using var shots = new EventLog<ShotFired>(scene.Events);
            yield return input.Mouse.ClickLeft();
            Assert.AreEqual(0, shots.Count);
            yield return WaitSwitchDone();
            yield return input.Mouse.ClickLeft();
            Assert.AreEqual(1, shots.Count);
            Assert.AreSame(dmr.Definition, shots.Last.Weapon);
        }

        // AC1: 권총·SMG·소총·샷건·DMR 각 1발 — ShotFired 정의, 투사체 수(샷건 = 펠릿 8), NoiseEmitted 1, 탄 1발, 마모(플레이어만)
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator FiveWeapons_OneShotEach_ShotFiredPelletsNoise()
        {
            var rifle = (WeaponItem)Weapon.Model.Item;
            WeaponItem smg = Equip(M7WeaponTestAssets.SmgPath, M7WeaponTestAssets.Mag9mm30Path, EquipSlot.Primary2);
            WeaponItem pistol = Equip(M7WeaponTestAssets.PistolPath, M7WeaponTestAssets.Mag9mm12Path, EquipSlot.Secondary);

            yield return ShootOnce(null, rifle, 1);
            yield return ShootOnce(Key.Digit2, smg, 1);
            yield return ShootOnce(Key.Digit3, pistol, 1);   // 보조무기 슬롯 = 권총 (M5 Q16 해소)

            Assert.IsTrue(scene.Inventory.Remove(smg));
            WeaponItem shotgun = Equip(M7WeaponTestAssets.ShotgunPath, null, EquipSlot.Primary2);
            Assert.IsTrue(shotgun.HasInternalMagazine);
            yield return ShootOnce(Key.Digit2, shotgun, 8);

            Assert.IsTrue(scene.Inventory.Remove(rifle));
            WeaponItem dmr = Equip(M7WeaponTestAssets.DmrPath, M7WeaponTestAssets.Mag762_5Path, EquipSlot.Primary1);
            yield return ShootOnce(Key.Digit1, dmr, 1);
        }

        private IEnumerator ShootOnce(Key? key, WeaponItem item, int expectedProjectiles)
        {
            if (key.HasValue)
            {
                yield return Press(key.Value);
                Assert.AreSame(item, Weapon.Model.Item, $"{key} → {item.Definition.id}");
                yield return WaitSwitchDone();
            }
            yield return M2Scene.WaitUntil(() => scene.Projectiles.LiveCount == 0, 6f);

            int rounds = item.LoadedMagazine.Rounds;
            float durability = item.Durability;
            int liveAtShot = -1;
            Action<ShotFired> probe = _ => liveAtShot = scene.Projectiles.LiveCount;
            scene.Events.Subscribe(probe);
            using var shots = new EventLog<ShotFired>(scene.Events);
            using var noises = new EventLog<NoiseEmitted>(scene.Events);
            try
            {
                yield return input.Mouse.ClickLeft();
                yield return null;
            }
            finally
            {
                scene.Events.Unsubscribe(probe);
            }

            string id = item.Definition.id;
            Assert.AreEqual(1, shots.Count, $"{id}: 한 번 클릭 = 1발");
            Assert.AreSame(item.Definition, shots.Last.Weapon, $"{id}: ShotFired 무기 정의");
            Assert.AreEqual(SoundSource.Player, shots.Last.Source);
            Assert.AreEqual(expectedProjectiles, liveAtShot, $"{id}: 투사체 수");
            Assert.AreEqual(1, noises.Received.Count(n => n.Kind == NoiseKind.Gunshot), $"{id}: NoiseEmitted 발당 1회");
            Assert.AreEqual(rounds - 1, item.LoadedMagazine.Rounds, $"{id}: 탄 1발");
            Assert.AreEqual(durability - item.Definition.durabilityLossPerShot, item.Durability, 1e-4f, $"{id}: 발당 마모");
            Assert.Less(Vector3.Distance(shots.Last.Position, scene.Motor.EyePosition), 1.5f, $"{id}: 총구 위치");
            Transform view = Weapon.CurrentViewModel;
            if (item.Definition.viewModel != null)
            {
                Assert.IsNotNull(view, $"{id}: 뷰모델 프리팹 생성");
                Assert.IsTrue(view.gameObject.activeInHierarchy, $"{id}: 든 무기 뷰모델만 보인다");
                int visibleViews = Weapon.transform.Cast<Transform>().Count(t => t.gameObject.activeSelf);
                Assert.AreEqual(1, visibleViews, $"{id}: 보이는 뷰모델 1개");
            }
        }
    }

    // M7 무기 테스트 공용: 데이터 에셋 경로(Ashfall/Setup/Create M7 Weapon Data)와 아이템 만들기.
    internal static class M7WeaponTestAssets
    {
        private const string Folder = "Assets/_Project/Data/Weapons/";
        public const string PistolPath = Folder + "WPN-PST-01.asset";
        public const string SmgPath = Folder + "WPN-SMG-01.asset";
        public const string RiflePath = Folder + "WPN-RIF-01.asset";
        public const string ShotgunPath = Folder + "WPN-SHT-01.asset";
        public const string DmrPath = Folder + "WPN-DMR-01.asset";
        public const string Mag9mm12Path = Folder + "MAG-9MM-12.asset";
        public const string Mag9mm30Path = Folder + "MAG-9MM-30.asset";
        public const string Mag556Path = Folder + "MAG-556-30.asset";
        public const string Mag762_5Path = Folder + "MAG-762-5.asset";
        public const string Box12GaPath = Folder + "BOX-12GA-8.asset";
        public const string Ammo556Path = Folder + "AMMO-556.asset";
        public const string Ammo12GaPath = Folder + "AMMO-12GA.asset";
        public const string HandlingPath = Folder + "WeaponHandlingConfig.asset";

        public static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"{path} 없음 — Ashfall/Setup/Create M7 Weapon Data");
            return asset;
        }

        // 꽉 찬 탄창(튜브 무기는 꽉 찬 튜브)을 단 무기
        public static WeaponItem NewWeapon(string weaponPath, string magazinePath)
        {
            var definition = Load<WeaponDefinitionSO>(weaponPath);
            if (definition.internalMagazine != null)
                return new WeaponItem(definition, Magazine.Full(definition.internalMagazine));
            return new WeaponItem(definition, Magazine.Full(Load<MagazineDefinitionSO>(magazinePath)));
        }
    }

    // 인벤토리 없는 코드 장면: WeaponController 하나(Configure)만. 다른 씬은 내린다(남은 인벤토리 서비스가 끼지 않게).
    // 앞선 테스트의 씬과 겹치지 않게 높이 3000m에 둔다.
    internal sealed class WeaponCodeScene
    {
        private static int sceneCounter;
        public static readonly Vector3 Origin = new Vector3(0f, 3000f, 0f);

        public GameObject Root;
        public WeaponController Weapon;

        public EventBus Events => GameManager.Instance.Events;

        public static IEnumerator Create(WeaponCodeScene into, WeaponDefinitionSO weapon, AmmoDefinitionSO ammo,
            MagazineDefinitionSO magazine, int magazines, WeaponHandlingConfigSO handling, int seed)
        {
            Scene scene = SceneManager.CreateScene("M7WeaponCodeScene_" + (++sceneCounter));
            SceneManager.SetActiveScene(scene);
            var others = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s != scene && s.isLoaded)
                    others.Add(s);
            }
            foreach (Scene s in others)
                yield return SceneManager.UnloadSceneAsync(s);
            yield return null;

            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            Assert.IsFalse(GameManager.Instance.Services.TryGet(out IInventoryService _), "인벤토리 서비스가 남아 있다");
            Assert.IsNull(Object.FindFirstObjectByType<ProjectileSystem>(), "투사체 시스템이 남아 있다");

            into.Root = new GameObject("WeaponCodeSceneRoot");
            into.Root.transform.position = Origin;
            var go = new GameObject("Weapon");
            go.SetActive(false);
            go.transform.SetParent(into.Root.transform, false);
            into.Weapon = go.AddComponent<WeaponController>();
            into.Weapon.Configure(weapon, ammo, magazine, magazines, handling, null, seed);
            go.SetActive(true);
            yield return null;   // Start
            yield return null;
            Assert.IsTrue(into.Weapon.IsArmed, "코드 장면 무기");
        }

        public void Destroy()
        {
            if (Root != null)
                Object.Destroy(Root);
        }
    }
}
#endif
