#if UNITY_EDITOR
using System.Collections;
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
    // M7 7A-3 통합 스모크 (M7 설계 §1.7·§6 "7A-3", SYS-WPN, WPN-SHT-01, WPN-DMR-01):
    // Ashfall/Setup/Apply M7 Content(M7SceneSetup.ApplyAll)를 돌린 뒤의 Raid_CrimsonBasin을 이름으로 불러
    // Player의 WeaponController·handling(탄약 표 4종)과 무기 5종 정의의 뷰모델 프리팹("Muzzle" 자식)이 연결됐는지,
    // 그리고 실제 인벤토리의 탄 상자 → 샷건 튜브 R 장전(7A-2b 레지스트리 ILooseAmmoSource) 경로가 도는지 본다.
    // 콘솔 오류가 나면 테스트 프레임워크가 실패시킨다. 경로·이름은 M7Names 계약 값(테스트 asmdef는 EditorTools를 참조하지 않는다).
    public class M7WeaponSceneSmokeTests
    {
        private const string WeaponsFolder = "Assets/_Project/Data/Weapons/";
        private const string HandlingPath = WeaponsFolder + "WeaponHandlingConfig.asset";
        private const string Box12GaPath = WeaponsFolder + "BOX-12GA-8.asset";
        private const string MuzzleChild = "Muzzle";
        private static readonly string[] PlayerWeaponIds = { "WPN-PST-01", "WPN-SMG-01", "WPN-RIF-01", "WPN-SHT-01", "WPN-DMR-01" };
        private static readonly AmmoType[] TableAmmo = { AmmoType.Pistol9mm, AmmoType.Rifle556, AmmoType.Rifle762, AmmoType.Gauge12 };
        private const float Dt = 1f / 60f;
        private const float RunSeconds = 1f;

        private static string WeaponPath(string id) => WeaponsFolder + id + ".asset";
        private static string ViewModelPath(string id) => $"Assets/_Project/Prefabs/Weapons/VM_{id}.prefab";

        private SimInput input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Flow.SetWorldPaused(false);
                Time.timeScale = 1f;
            }
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            input?.Dispose();
            input = null;
            Time.captureDeltaTime = 0f;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator RaidScene_PlayerWeapon_HandlingAmmoTable_ViewModelPrefabs()
        {
            var raid = new M4Raid();
            yield return M4Raid.Load(raid);

            // ── Player의 WeaponController 하나, handling = WeaponHandlingConfig ──
            WeaponController[] controllers = raid.Motor.GetComponentsInChildren<WeaponController>(true);
            Assert.AreEqual(1, controllers.Length, "Player의 WeaponController 수");
            WeaponController weapon = controllers[0];
            Assert.AreEqual(1, Object.FindObjectsByType<WeaponController>(FindObjectsSortMode.None).Length, "씬의 WeaponController 수");
            var handlingAsset = AssetDatabase.LoadAssetAtPath<WeaponHandlingConfigSO>(HandlingPath);
            Assert.IsNotNull(handlingAsset, $"{HandlingPath} 없음");
            var handling = new SerializedObject(weapon).FindProperty("handling").objectReferenceValue as WeaponHandlingConfigSO;
            Assert.AreSame(handlingAsset, handling, "WeaponController.handling");

            // ── 탄약 표 4종 (9mm·5.56·7.62·12ga) ──
            Assert.AreEqual(4, handling.ammoDefinitions.Length, "탄약 표 항목 수");
            CollectionAssert.AreEquivalent(TableAmmo, handling.ammoDefinitions.Select(a => a.type).ToArray(), "탄약 표 종류");
            foreach (AmmoType type in TableAmmo)
            {
                Assert.IsTrue(handling.TryGetAmmo(type, out AmmoDefinitionSO a), $"탄약 표에 {type} 없음");
                Assert.AreEqual(type, a.type);
            }

            // ── 무기 5종 정의 → 뷰모델 프리팹(계약 경로) + "Muzzle" 자식, 탄약 표로 탄약 해석 ──
            foreach (string id in PlayerWeaponIds)
            {
                var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>(WeaponPath(id));
                Assert.IsNotNull(definition, $"{id} 정의 없음");
                Assert.IsNotNull(definition.viewModel, $"{id}.viewModel 비었음");
                Assert.AreEqual(ViewModelPath(id), AssetDatabase.GetAssetPath(definition.viewModel), $"{id}.viewModel 경로");
                Assert.IsNotNull(definition.viewModel.transform.Find(MuzzleChild), $"{id} 프리팹에 \"{MuzzleChild}\" 자식 없음");
                Assert.IsTrue(handling.TryGetAmmo(definition.ammo, out _), $"{id}의 탄({definition.ammo})이 탄약 표에 없음");
            }

            // ── 시작 무기: 프리팹 뷰모델을 들고 있다 ──
            Assert.IsTrue(weapon.IsArmed, "시작 무기 없음");
            Assert.IsNotNull(weapon.CurrentAmmo, "시작 무기 탄약 (탄약 표)");
            Assert.IsNotNull(weapon.CurrentViewModel, "든 뷰모델 없음");
            Assert.IsNotNull(weapon.CurrentViewModel.Find(MuzzleChild), "든 뷰모델에 Muzzle 없음 (프리팹 뷰모델이어야 한다)");

            // ── 몇 초 돌려도 오류 없음 ──
            float end = Time.realtimeSinceStartup + RunSeconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
            Assert.AreEqual(SceneNames.RaidCrimsonBasin, SceneManager.GetActiveScene().name);
        }

        // 7A-2b 경로: 레이드 씬 실제 인벤토리(레지스트리 ILooseAmmoSource)의 12ga 탄 상자 → 빈 튜브 샷건 R = 한 발씩 6발.
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator RaidScene_InventoryAmmoBox_ShotgunTubeReload()
        {
            var raid = new M4Raid();
            yield return M4Raid.Load(raid);
            WeaponController weapon = raid.Motor.GetComponentInChildren<WeaponController>(true);
            Assert.IsNotNull(weapon);

            Assert.IsTrue(raid.Gm.Services.TryGet(out ILooseAmmoSource loose), "ILooseAmmoSource 미등록");
            Assert.AreSame(raid.Inventory, loose, "낱탄 공급원 = 플레이어 인벤토리");

            // 빈 튜브 샷건을 Primary2에, 8발 상자를 조끼·주머니에
            var shotgun = new WeaponItem(AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>(WeaponPath("WPN-SHT-01")));
            Assert.IsTrue(shotgun.HasInternalMagazine);
            Assert.AreEqual(0, shotgun.LoadedMagazine.Rounds, "새 샷건 = 빈 튜브");
            ItemInstance occupied = raid.Inventory.GetEquipped(EquipSlot.Primary2);
            if (occupied != null)
                Assert.IsTrue(raid.Inventory.Remove(occupied), "Primary2 비우기");
            Assert.IsTrue(raid.Inventory.TryEquipNew(shotgun, EquipSlot.Primary2), "샷건 → Primary2");
            var boxDefinition = AssetDatabase.LoadAssetAtPath<MagazineDefinitionSO>(Box12GaPath);
            Assert.IsNotNull(boxDefinition, $"{Box12GaPath} 없음");
            Magazine box = Magazine.Full(boxDefinition);
            Assert.IsTrue(raid.Inventory.TryAutoStore(box), "탄 상자 넣기");
            Assert.IsTrue(loose.HasLooseRound(AmmoType.Gauge12), "탄 상자가 재장전 범위(조끼·주머니)에 없다");

            input = new SimInput();
            Time.captureDeltaTime = Dt;
            yield return null;

            // 2 = Primary2로 교체
            yield return input.Keyboard.Tap(Key.Digit2);
            yield return M2Scene.WaitUntil(() => weapon.Model != null && ReferenceEquals(weapon.Model.Item, shotgun) && !weapon.IsSwitching, 10f);
            Assert.AreEqual(EquipSlot.Primary2, weapon.ActiveSlot);
            Assert.IsTrue(weapon.Model.IsTubeReload);
            Assert.AreEqual(AmmoType.Gauge12, weapon.CurrentAmmo.type, "탄약 표 12ga");

            // R → 한 발씩 6발 (상자 8 → 2)
            yield return input.Keyboard.Tap(Key.R);
            Assert.IsTrue(weapon.Model.IsReloading, "R로 튜브 장전 시작");
            yield return M2Scene.WaitUntil(() => !weapon.Model.IsReloading, 15f);
            Assert.AreEqual(6, shotgun.LoadedMagazine.Rounds, "튜브 가득");
            Assert.AreEqual(6, weapon.Model.RoundsLoadedThisReload);
            Assert.AreEqual(2, box.Rounds, "상자에서 6발");
            Assert.IsTrue(raid.Inventory.IsInStorage(box), "상자는 그리드에 남는다 (Q6)");
        }
    }
}
#endif
