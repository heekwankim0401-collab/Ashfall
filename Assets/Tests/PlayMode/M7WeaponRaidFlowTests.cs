#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.UI;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M7 7A-4 검증 — 흐름 레이드(은신처 → 레이드)의 무기 (M7 설계 §6 "7A-4", §7 AC1, SYS-WPN, WPN-SHT-01, WPN-DMR-01, §1.6 save v2):
    // ① 부랑자 키트 권총만 반입 → 레이드 시작 무기 = Secondary(Q2), 키 3 → R → 사격(ShotFired·투사체·소음) = M5 Q16 해소.
    // ② 상인에서 산 샷건·DMR 장착 반입 → 실제 키(1/2·R·LMB)로 몇 발 → EXT-01 탈출 → 은신처: 감소한 내구도가 save.json(v2)에 있고
    //    "다음 실행"(저장소 다시 꽂기 = 메모리 프로필 버림)에서 파일로 다시 읽힌다. 고장 난 채 탈출한 무기는 은신처 장비가 되면 해제(Q14, 7A 리뷰 중간 2).
    // ③ v1 save.json을 SaveSandbox 폴더에 두고 첫 은신처 진입 → 무기 내구도 100으로 이전, 진입 저장(W1)이 version 2, .bak = 원래 v1.
    // 세이브는 테스트마다 SaveSandbox 새 폴더(실제 persistentDataPath 없음). 적은 지운다(흐름만 본다).
    public class M7WeaponRaidFlowTests
    {
        private const float ShotTimeoutSec = 6f;
        private const float ReloadTimeoutSec = 15f;

        private FileSaveStorage storage;
        private SimInput input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            M5Flow.ResetFlow();
            storage = SaveSandbox.UseFreshStorage("m7-weapon-raid-flow");
            Assert.IsFalse(SaveSandbox.IsUnderPersistentDataPath(storage.FilePath), "샌드박스 경로");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DisposeInput();
            yield return M5Flow.TearDown("after-m7-weapon-raid-flow");
        }

        // ── ① 키트 권총만 반입 → 3으로 사격 ──

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator KitPistolOnly_FlowRaid_Key3_ReloadAndFire()
        {
            using (var pointer = new UiPointer())
            {
                var hideout = new M5Hideout();
                yield return EnterHideoutFromMenu(pointer, hideout);
                yield return hideout.ClickKit(pointer);
                yield return hideout.SelectTab(pointer, HideoutTab.Loadout);
                hideout.Equip("WPN-PST-01", EquipSlot.Secondary);
                hideout.QuickMove("MAG-9MM-12");
                hideout.QuickMove("MAG-9MM-12");
                CollectionAssert.AreEqual(new[] { "MAG-9MM-12", "MAG-9MM-12", "WPN-PST-01" },
                    M5Hideout.SortedIds(Loadout.Capture(hideout.Inventory).Entries.Select(e => e.Item)), "반입 = 키트 권총 + 탄창 2");
                Assert.IsNull(hideout.Inventory.GetEquipped(EquipSlot.Primary1), "주무기 없음");
                Assert.IsNull(hideout.Inventory.GetEquipped(EquipSlot.Primary2), "주무기 2 없음");

                var raid = new M4Raid();
                yield return hideout.StartRaid(pointer, raid);
                Assert.IsTrue(raid.Gm.Flow.HasRaidInFlow, "흐름 레이드");
                WeaponController weapon = PlayerWeapon(raid);
                var pistol = raid.Inventory.GetEquipped(EquipSlot.Secondary) as WeaponItem;
                Assert.IsNotNull(pistol, "반입한 권총이 Secondary에 있다");
                Assert.AreEqual("WPN-PST-01", pistol.Definition.id);
                Assert.AreEqual(WeaponItem.MaxDurability, pistol.Durability, "키트 권총 내구도 100");

                // Q2: 시작 무기 = 첫 비지 않은 슬롯 (Primary1·2가 비어 Secondary). M5까지는 여기서 비무장이었다(Q16).
                Assert.IsTrue(weapon.IsArmed, "키트 권총만 반입해도 무장 (M5 Q16 해소)");
                Assert.AreEqual(EquipSlot.Secondary, weapon.ActiveSlot);
                Assert.AreSame(pistol, weapon.Model.Item);
                Assert.IsNull(pistol.LoadedMagazine, "키트 권총은 탄창 없이 지급 (M5 Q17)");

                BeginInput(raid);
                // 3 = Secondary: 이미 든 슬롯이라 교체 시간 없이 그대로 (교체 동작 자체는 WeaponSwitchFlowTests)
                yield return input.Keyboard.Tap(Key.Digit3);
                Assert.AreEqual(EquipSlot.Secondary, weapon.ActiveSlot, "3 = Secondary");
                Assert.IsFalse(weapon.IsSwitching);
                Assert.AreSame(pistol, weapon.Model.Item);

                // 빈 권총 → R(주머니 탄창) → LMB
                yield return Reload(weapon);
                Assert.IsNotNull(pistol.LoadedMagazine, "R → 주머니 탄창 장전");
                Assert.AreEqual(12, pistol.LoadedMagazine.Rounds);
                using (var shots = new EventLog<ShotFired>(raid.Events))
                using (var noise = new EventLog<NoiseEmitted>(raid.Events))
                {
                    int liveBefore = raid.Projectiles.LiveCount;
                    yield return FireOnce(shots);
                    Assert.AreEqual(1, shots.Count, "ShotFired 1");
                    Assert.AreSame(pistol.Definition, shots.Last.Weapon, "권총으로 쐈다");
                    Assert.AreEqual(SoundSource.Player, shots.Last.Source);
                    Assert.AreEqual(1, raid.Projectiles.LiveCount - liveBefore, "투사체 1발");
                    Assert.AreEqual(1, noise.Count, "사격 소음 1 (AI 청각)");
                }
                Assert.AreEqual(11, pistol.LoadedMagazine.Rounds);
                Assert.AreEqual(WeaponItem.MaxDurability - Loss(pistol), pistol.Durability, 1e-4f, "플레이어 무기 발당 마모");
                DisposeInput();
            }
        }

        // ── ② 샷건·DMR 반입 → 사격 → 탈출 → 내구도 저장·다시 읽기 ──

        [UnityTest]
        [Timeout(420000)]
        public IEnumerator ShotgunDmr_FlowRaid_FireExtract_DurabilitySavedAndReloadedFromFile()
        {
            GameManager gm = GameManager.Instance;
            const int ShotgunShots = 2;
            const int DmrShots = 2;
            float shotgunDurability, dmrDurability;
            int shotgunTubeRounds, dmrMagRounds;

            using (var pointer = new UiPointer())
            {
                var hideout = new M5Hideout();
                yield return EnterHideoutFromMenu(pointer, hideout);
                // 시작 탄피 6000으로는 SHT 2500 + DMR 5000을 다 못 산다 → 디버그 지급(HideoutChanged 알림 포함)
                hideout.Service.DebugAddCasings(5000);
                yield return M5Hideout.Frames(2);
                foreach (string id in new[] { "WPN-SHT-01", "WPN-DMR-01", "MAG-762-5", "BOX-12GA-8" })
                    yield return hideout.Buy(pointer, id);
                Assert.AreEqual(WeaponItem.MaxDurability, ((WeaponItem)hideout.StashItem("WPN-SHT-01")).Durability, "상인 무기 내구도 100");

                yield return hideout.SelectTab(pointer, HideoutTab.Loadout);
                hideout.Equip("WPN-SHT-01", EquipSlot.Primary1);
                hideout.Equip("WPN-DMR-01", EquipSlot.Primary2);
                hideout.QuickMove("MAG-762-5");
                hideout.QuickMove("BOX-12GA-8");
                CollectionAssert.AreEqual(new[] { "BOX-12GA-8", "MAG-762-5", "WPN-DMR-01", "WPN-SHT-01" },
                    M5Hideout.SortedIds(Loadout.Capture(hideout.Inventory).Entries.Select(e => e.Item)), "반입");

                // ── 흐름 레이드 ──
                var raid = new M4Raid();
                yield return hideout.StartRaid(pointer, raid);
                Assert.IsTrue(gm.Flow.HasRaidInFlow);
                SaveData atRaid = M5Save.Read(storage);
                Assert.IsTrue(atRaid.raidInProgress);
                foreach (ItemRecord r in atRaid.loadout.Where(r => r.id.StartsWith("WPN-")))
                    Assert.AreEqual(WeaponItem.MaxDurability, r.durability, 1e-4f, $"레이드 진입 기록 {r.id} 내구도 100");

                WeaponController weapon = PlayerWeapon(raid);
                var shotgun = raid.Inventory.GetEquipped(EquipSlot.Primary1) as WeaponItem;
                var dmr = raid.Inventory.GetEquipped(EquipSlot.Primary2) as WeaponItem;
                Assert.IsNotNull(shotgun, "샷건 반입");
                Assert.IsNotNull(dmr, "DMR 반입");
                Assert.AreEqual(EquipSlot.Primary1, weapon.ActiveSlot, "시작 = Primary1 (Q2)");
                Assert.AreSame(shotgun, weapon.Model.Item);
                Assert.IsTrue(weapon.Model.IsTubeReload, "샷건 = 튜브");
                Assert.AreEqual(0, shotgun.LoadedMagazine.Rounds, "상인 샷건 = 빈 튜브");
                ItemInstance box = raid.Carried().First(i => i.Definition.id == "BOX-12GA-8");

                BeginInput(raid);

                // 샷건: R(주머니 탄 상자, 한 발씩) → 2발 (발당 펠릿 8 = 투사체 8)
                yield return Reload(weapon);
                Assert.AreEqual(6, shotgun.LoadedMagazine.Rounds, "튜브 가득 (6)");
                Assert.AreEqual(2, ((Magazine)box).Rounds, "상자 8 → 2");
                using (var shots = new EventLog<ShotFired>(raid.Events))
                {
                    for (int i = 0; i < ShotgunShots; i++)
                    {
                        int liveBefore = raid.Projectiles.LiveCount;
                        yield return FireOnce(shots);
                        Assert.AreSame(shotgun.Definition, shots.Last.Weapon, "샷건으로 쐈다");
                        if (i == 0)
                            Assert.AreEqual(8, raid.Projectiles.LiveCount - liveBefore, "펠릿 8 = 투사체 8");
                    }
                    Assert.AreEqual(ShotgunShots, shots.Count, "샷건 ShotFired = 발 수 (펠릿 수 무관)");
                }
                Assert.AreEqual(6 - ShotgunShots, shotgun.LoadedMagazine.Rounds);
                Assert.AreEqual(WeaponItem.MaxDurability - ShotgunShots * Loss(shotgun), shotgun.Durability, 1e-4f, "샷건 마모");

                // 2 → DMR: R(주머니 MAG-762-5) → 2발
                yield return input.Keyboard.Tap(Key.Digit2);
                Assert.AreEqual(EquipSlot.Primary2, weapon.ActiveSlot, "2 = Primary2");
                Assert.AreSame(dmr, weapon.Model.Item);
                yield return M2Scene.WaitUntil(() => !weapon.IsSwitching, 3f);
                yield return Reload(weapon);
                Assert.IsNotNull(dmr.LoadedMagazine, "DMR 탄창 장전");
                Assert.AreEqual("MAG-762-5", dmr.LoadedMagazine.Definition.id);
                using (var shots = new EventLog<ShotFired>(raid.Events))
                {
                    for (int i = 0; i < DmrShots; i++)
                    {
                        int liveBefore = raid.Projectiles.LiveCount;
                        yield return FireOnce(shots);
                        Assert.AreSame(dmr.Definition, shots.Last.Weapon, "DMR로 쐈다");
                        if (i == 0)
                            Assert.AreEqual(1, raid.Projectiles.LiveCount - liveBefore, "DMR 투사체 1");
                    }
                    Assert.AreEqual(DmrShots, shots.Count);
                }
                Assert.AreEqual(5 - DmrShots, dmr.LoadedMagazine.Rounds);
                Assert.AreEqual(WeaponItem.MaxDurability - DmrShots * Loss(dmr), dmr.Durability, 1e-4f, "DMR 마모");
                Assert.IsFalse(shotgun.IsJammed || dmr.IsJammed, "내구도 50 초과 = 고장 없음");
                DisposeInput();

                shotgunDurability = shotgun.Durability;
                dmrDurability = dmr.Durability;
                shotgunTubeRounds = shotgun.LoadedMagazine.Rounds;
                dmrMagRounds = dmr.LoadedMagazine.Rounds;
                Assert.Less(shotgunDurability, WeaponItem.MaxDurability);
                Assert.Less(dmrDurability, WeaponItem.MaxDurability);
                // M7 리뷰 7A 중간 2 (Q14 A): 고장 난 채 탈출 → 은신처 귀환에서 해제 (내구도 > 50이라 직접 고장 상태로 둔다)
                dmr.IsJammed = true;

                // ── EXT-01 탈출 ──
                RaidResult extracted;
                using (var ended = new EventLog<RaidEnded>(raid.Events))
                {
                    yield return raid.EnterZone("EXT-01");
                    yield return M3Scene.WaitUntil(() => ended.Count > 0, 25f, () => "EXT-01 탈출");
                    extracted = raid.Controller.LastResult;
                }
                Assert.AreEqual(RaidOutcome.Extracted, extracted.Outcome);

                // ── 은신처: 결과 → [확인] → 장비 내구도 = 레이드 끝 값, save.json v2에 기록 ──
                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout);
                hideout.AssertResultPanel(extracted, expectCarriedOut: true);
                yield return hideout.ConfirmResult(pointer);
                var hShotgun = hideout.Inventory.GetEquipped(EquipSlot.Primary1) as WeaponItem;
                var hDmr = hideout.Inventory.GetEquipped(EquipSlot.Primary2) as WeaponItem;
                Assert.IsNotNull(hShotgun, "반출 샷건 Primary1");
                Assert.IsNotNull(hDmr, "반출 DMR Primary2");
                Assert.AreEqual(shotgunDurability, hShotgun.Durability, 1e-5f, "은신처 샷건 내구도 = 레이드 끝");
                Assert.AreEqual(dmrDurability, hDmr.Durability, 1e-5f, "은신처 DMR 내구도 = 레이드 끝");
                Assert.IsFalse(hDmr.IsJammed, "고장 난 채 탈출 → 은신처 장비가 되면 해제 (같은 실행의 다음 레이드에 남지 않음, Q14)");
                Assert.IsFalse(dmr.IsJammed, "레이드에서 든 인스턴스도 해제");

                SaveData afterExtract = M5Save.Read(storage);   // version = CurrentVersion 단언 포함
                Assert.IsFalse(afterExtract.raidInProgress);
                AssertWeaponRecord(afterExtract, "WPN-SHT-01", EquipSlot.Primary1, shotgunDurability, "MAG-12GA-TUBE", shotgunTubeRounds);
                AssertWeaponRecord(afterExtract, "WPN-DMR-01", EquipSlot.Primary2, dmrDurability, "MAG-762-5", dmrMagRounds);
                StringAssert.Contains("\"version\": " + SaveSerializer.CurrentVersion, File.ReadAllText(storage.FilePath));

                // ── [메인 메뉴](W2 저장) → 다음 실행 흉내(같은 폴더 저장소 다시 꽂기 = 프로필 버림) → [은신처] = 파일에서 읽기 ──
                yield return pointer.Click(hideout.ButtonNamed(HideoutHubView.MainMenuName));
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[메인 메뉴]가 눌리지 않았다");
                yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu, 30f, () => "Hideout → MainMenu");
                var menu = new M4Menu();
                yield return M4Menu.Attach(menu);
                AssertWeaponRecord(M5Save.Read(storage), "WPN-SHT-01", EquipSlot.Primary1, shotgunDurability, "MAG-12GA-TUBE", shotgunTubeRounds);

                M5Flow.ResetFlow();
                gm.UseSaveStorage(new FileSaveStorage(storage.DirectoryPath));
                Assert.IsFalse(gm.Profiles.IsLoaded, "다음 실행: 프로필 미로드");
                yield return pointer.Click(menu.Start);
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[은신처]가 눌리지 않았다");
                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                Assert.AreEqual(SaveLoadStatus.Loaded, hideout.Controller.EntryReport.Status, "파일에서 읽음");
                Assert.AreEqual(HideoutPanel.Hub, hideout.Presenter.Panel, "알림 없음");
                var rShotgun = hideout.Inventory.GetEquipped(EquipSlot.Primary1) as WeaponItem;
                var rDmr = hideout.Inventory.GetEquipped(EquipSlot.Primary2) as WeaponItem;
                Assert.IsNotNull(rShotgun);
                Assert.IsNotNull(rDmr);
                Assert.AreNotSame(hShotgun, rShotgun, "새로 읽은 인스턴스");
                Assert.AreEqual(shotgunDurability, rShotgun.Durability, 1e-4f, "다시 읽은 샷건 내구도");
                Assert.AreEqual(dmrDurability, rDmr.Durability, 1e-4f, "다시 읽은 DMR 내구도");
                Assert.AreEqual(shotgunTubeRounds, rShotgun.LoadedMagazine.Rounds, "튜브 탄 수 복원");
                Assert.AreEqual(dmrMagRounds, rDmr.LoadedMagazine.Rounds, "DMR 탄창 탄 수 복원");
                Assert.IsFalse(rShotgun.IsJammed || rDmr.IsJammed, "고장은 저장하지 않는다 (Q14)");
            }
        }

        // ── ③ v1 세이브 → 은신처 진입 → 100 이전, v2로 다시 저장 ──

        // 7A-1 SaveSerializerV2Tests.V1Json과 같은 모양(M5 형식, durability 필드 없음 — 소총에만 0을 넣어 "버전이 정한다"를 본다).
        private const string V1Json = @"{
    ""version"": 1,
    ""savedAtUtc"": ""2026-10-01T09:30:00.0000000Z"",
    ""raidInProgress"": false,
    ""casings"": 777,
    ""kitLastClaimUtcTicks"": 0,
    ""stash"": [
        { ""id"": ""WPN-RIF-01"", ""container"": ""Stash"", ""slot"": """", ""index"": 0, ""x"": 0, ""y"": 0, ""rotated"": false,
          ""rounds"": -1, ""usesLeft"": -1, ""loadedMagazineId"": ""MAG-556-30"", ""loadedMagazineRounds"": 17, ""durability"": 0.0 },
        { ""id"": ""MAG-556-30"", ""container"": ""Stash"", ""slot"": """", ""index"": 0, ""x"": 5, ""y"": 0, ""rotated"": false,
          ""rounds"": 9, ""usesLeft"": -1, ""loadedMagazineId"": """", ""loadedMagazineRounds"": 0 }
    ],
    ""loadout"": [
        { ""id"": ""WPN-PST-01"", ""container"": ""Equipped"", ""slot"": ""Secondary"", ""index"": 0, ""x"": 0, ""y"": 0, ""rotated"": false,
          ""rounds"": -1, ""usesLeft"": -1, ""loadedMagazineId"": """", ""loadedMagazineRounds"": 0 }
    ],
    ""quickSlots"": [-1, -1, -1, -1]
}";

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator V1SaveFile_HideoutEntry_WeaponsMigratedTo100_ResavedAsV2()
        {
            Directory.CreateDirectory(storage.DirectoryPath);
            File.WriteAllText(storage.FilePath, V1Json);
            Assert.IsFalse(GameManager.Instance.Profiles.IsLoaded, "새 저장소 = 프로필 미로드");

            using (var pointer = new UiPointer())
            {
                var hideout = new M5Hideout();
                yield return EnterHideoutFromMenu(pointer, hideout);
                SaveLoadReport report = hideout.Controller.EntryReport;
                Assert.AreEqual(SaveLoadStatus.Loaded, report.Status, "v1 파일 읽음");
                Assert.IsFalse(report.HasNotice, "이전은 알림 없음");
                Assert.AreEqual(777, hideout.Service.Casings, "v1 탄피 그대로");

                var rifle = hideout.StashItem("WPN-RIF-01") as WeaponItem;
                Assert.IsNotNull(rifle);
                Assert.AreEqual(WeaponItem.MaxDurability, rifle.Durability, "v1 → 100 (기록 값 0 무시)");
                Assert.AreEqual(17, rifle.LoadedMagazine.Rounds, "그 밖 필드 그대로");
                Assert.AreEqual(9, ((Magazine)hideout.StashItem("MAG-556-30")).Rounds);
                var pistol = hideout.Inventory.GetEquipped(EquipSlot.Secondary) as WeaponItem;
                Assert.IsNotNull(pistol, "v1 장비 권총");
                Assert.AreEqual(WeaponItem.MaxDurability, pistol.Durability, "v1 → 100");

                // 진입 저장(W1) = v2, 무기 durability 100, 그 밖 -1. 직전 세대(.bak) = 원래 v1 파일
                SaveData saved = M5Save.Read(storage);
                Assert.AreEqual(SaveSerializer.CurrentVersion, saved.version);
                StringAssert.Contains("\"version\": " + SaveSerializer.CurrentVersion, File.ReadAllText(storage.FilePath));
                Assert.AreEqual(777, saved.casings);
                Assert.AreEqual(100f, saved.stash.Single(r => r.id == "WPN-RIF-01").durability, 1e-4f);
                Assert.AreEqual(-1f, saved.stash.Single(r => r.id == "MAG-556-30").durability, "탄창 = 해당 없음 -1");
                Assert.AreEqual(100f, saved.loadout.Single(r => r.id == "WPN-PST-01").durability, 1e-4f);
                Assert.IsTrue(File.Exists(storage.BackupPath), ".bak 있음");
                StringAssert.Contains("\"version\": 1", File.ReadAllText(storage.BackupPath), ".bak = 이전 v1 세대");

                // 다음 실행에서 v2 파일을 다시 읽어도 같다
                yield return pointer.Click(hideout.ButtonNamed(HideoutHubView.MainMenuName));
                yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu, 30f, () => "Hideout → MainMenu");
                var menu = new M4Menu();
                yield return M4Menu.Attach(menu);
                M5Flow.ResetFlow();
                GameManager.Instance.UseSaveStorage(new FileSaveStorage(storage.DirectoryPath));
                yield return pointer.Click(menu.Start);
                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                Assert.AreEqual(SaveLoadStatus.Loaded, hideout.Controller.EntryReport.Status);
                Assert.AreEqual(WeaponItem.MaxDurability, ((WeaponItem)hideout.StashItem("WPN-RIF-01")).Durability, "v2 다시 읽기 100");
                Assert.AreEqual(WeaponItem.MaxDurability, ((WeaponItem)hideout.Inventory.GetEquipped(EquipSlot.Secondary)).Durability);
                Assert.AreEqual(SaveSerializer.CurrentVersion, M5Save.Read(storage).version);
            }
        }

        // ── 도우미 ──

        private static IEnumerator EnterHideoutFromMenu(UiPointer pointer, M5Hideout hideout)
        {
            var menu = new M4Menu();
            yield return M4Raid.LoadScene(SceneNames.MainMenu);
            yield return M4Menu.Attach(menu);
            yield return pointer.Click(menu.Start);
            Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[은신처]가 눌리지 않았다");
            yield return M5Hideout.Attach(hideout, 60f);
        }

        private static WeaponController PlayerWeapon(M4Raid raid)
        {
            WeaponController weapon = raid.Motor.GetComponentInChildren<WeaponController>(true);
            Assert.IsNotNull(weapon, "Player WeaponController 없음");
            return weapon;
        }

        // 가상 키보드·마우스 (UiPointer 안에서 만들고 먼저 해제 — 입력 설정 되돌리기 순서). 하늘로 쏘게 시선을 올린다.
        private void BeginInput(M4Raid raid)
        {
            input = new SimInput();
            raid.LookDown(-45f);
        }

        private void DisposeInput()
        {
            input?.Dispose();
            input = null;
        }

        private static float Loss(WeaponItem weapon) => weapon.Definition.durabilityLossPerShot;

        // R → 재장전이 끝날 때까지 (튜브 = 한 발씩)
        private IEnumerator Reload(WeaponController weapon)
        {
            Assert.IsTrue(weapon.Model.CanReload, $"{weapon.Model.Item.Definition.id} 재장전 가능");
            yield return input.Keyboard.Tap(Key.R);
            Assert.IsTrue(weapon.Model.IsReloading, "R로 재장전 시작");
            yield return M2Scene.WaitUntil(() => !weapon.Model.IsReloading, ReloadTimeoutSec);
        }

        // LMB 클릭을 사격이 나갈 때까지 되풀이 (연사 간격 RPM 70/40 대기 포함). 정확히 1발.
        private IEnumerator FireOnce(EventLog<ShotFired> shots)
        {
            int before = shots.Count;
            float deadline = Time.realtimeSinceStartup + ShotTimeoutSec;
            while (shots.Count == before)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "사격이 나가지 않았다");
                yield return input.Mouse.ClickLeft();
            }
            Assert.AreEqual(before + 1, shots.Count, "클릭 한 번 = 한 발");
        }

        private static void AssertWeaponRecord(SaveData data, string id, EquipSlot slot, float durability, string magazineId, int rounds)
        {
            ItemRecord record = data.loadout.SingleOrDefault(r => r.id == id);
            Assert.IsNotNull(record, $"save.json 장비에 {id} 없음");
            Assert.AreEqual(ItemRecord.EquippedContainer, record.container);
            Assert.AreEqual(slot.ToString(), record.slot);
            Assert.AreEqual(durability, record.durability, 1e-4f, $"{id} 저장 내구도");
            Assert.AreEqual(magazineId, record.loadedMagazineId, $"{id} 장착 탄창");
            Assert.AreEqual(rounds, record.loadedMagazineRounds, $"{id} 탄 수");
        }
    }
}
#endif
