using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Hideout;
using Ashfall.Weapons;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // M7 7A 무기 데이터 (M7 설계 §3.1·§3.2·§3.3, §4.10, Q3·Q5·Q11·Q20·Q27).
    // ① 새 정의(없을 때만): AMMO-12GA·AMMO-762, MAG-762-5·MAG-762-100(적 전용)·MAG-12GA-TUBE(내장 튜브)·BOX-12GA-8(탄 상자),
    //    WPN-SHT-01·WPN-DMR-01·WPN-LMG-01(적 전용, enemyOnly)
    // ② 기존 무기 3종(PST·SMG·RIF) durabilityLossPerShot — 파일에 필드가 없거나 0일 때만(조정 보존, 0 = 미설정). 다른 값은 바꾸지 않는다.
    // ③ WeaponHandlingConfig.asset §3.3 필드 — 파일에 없을 때만 기록, 탄약 표는 빠진 탄 종류만 추가(AMMO-9MM·556·762·12GA)
    // ④ TraderConfig.catalog·LT-CRATE — 없는 항목만 끝에 추가(Q20: SHT·DMR·MAG-762-5·BOX-12GA-8 / 상자: BOX-12GA-8·MAG-762-5).
    //    적 전용(LMG·MAG-762-100)과 내장 튜브는 어디에도 넣지 않는다.
    // ⑤ ItemDatabase 재수집(M5DataSetup.CreateItemDatabase).
    // GDD 값: RPM·피해(펠릿당 18 × 8)·탄창 수(튜브 6, 5)·분류·탄 = §5.2, 고장 임계 50·0.1%/점 = §5.1. 나머지는 전부 TODO(GDD-?) 임시값(설계 §3).
    // 멱등: 값이 바뀔 때만 SetDirty한다. 두 번째 실행은 파일·GUID를 바꾸지 않는다.
    // 배치모드: -executeMethod Ashfall.EditorTools.M7WeaponDataSetup.CreateData
    public static class M7WeaponDataSetup
    {
        private const string Tag = "[M7WeaponDataSetup]";

        // 기존 무기 3종 발당 마모 (설계 §3.1, TODO(GDD-?) Q11)
        private static readonly (string path, string id, float loss)[] ExistingWeaponWear =
        {
            (M3DataSetup.PistolPath, "WPN-PST-01", 0.06f),
            (M3DataSetup.SmgPath, "WPN-SMG-01", 0.04f),
            (M3DataSetup.RiflePath, "WPN-RIF-01", 0.05f),
        };

        // §3.3 탄약 표 (Q3: AmmoType마다 하나)
        private static readonly string[] AmmoTablePaths =
        {
            M3DataSetup.Ammo9mmPath, M3DataSetup.Ammo556Path, M7Names.Ammo762Path, M7Names.Ammo12GaPath,
        };

        // Q20 상인 판매 목록 추가분 (이 순서로 끝에)
        public static readonly string[] TraderAdditionPaths =
        {
            M7Names.ShotgunPath, M7Names.DmrPath, M7Names.Mag762_5Path, M7Names.Box12Ga8Path,
        };

        // 설계 §3.9 LT-CRATE 추가분 중 7A 몫 (TODO(GDD-?) 가중치). 물·음식 등은 7C가 추가한다.
        public static readonly (string path, float weight)[] CrateAdditions =
        {
            (M7Names.Box12Ga8Path, 1.5f),
            (M7Names.Mag762_5Path, 0.5f),
        };

        [MenuItem("Ashfall/Setup/Create M7 Weapon Data")]
        public static void CreateData()
        {
            // M1~M6 데이터(기존 무기·상인·LT-CRATE·ItemDatabase 등)가 먼저 있어야 한다. 이미 있는 에셋은 건너뛴다.
            M6DataSetup.CreateData();

            WriteM7WeaponData();
            Debug.Log($"{Tag} 완료");
        }

        // 7A 몫만 (이전 메뉴를 부르지 않는다). 통합 메뉴·테스트가 재실행 바이트 동일을 확인할 때 쓴다.
        public static void WriteM7WeaponData()
        {
            CreateAmmo();
            CreateMagazines();
            CreateWeapons();
            WriteExistingWeaponWear();
            WriteHandlingConfig();
            AppendTraderCatalog();
            AppendCrateLoot();
            AssetDatabase.SaveAssets();
            // 새 정의를 저장한 뒤 DB를 다시 모은다(바뀔 때만 기록).
            M5DataSetup.CreateItemDatabase();
            AssetDatabase.SaveAssets();
        }

        // ---------- ① 탄약 (§3.2) ----------

        private static void CreateAmmo()
        {
            SetupUtil.CreateAssetIfMissing<AmmoDefinitionSO>(M7Names.Ammo12GaPath, a =>
            {
                a.id = M7Names.Ammo12GaId; a.displayName = "12ga 탄"; a.type = AmmoType.Gauge12;
                AdsBallisticsSetup.FillNewAmmo(a);   // AB-Q3
                a.damage = 18f;      // §5.2 18×8 의 펠릿당 피해 (무기 damage가 0일 때만 쓰는 기본값)
                a.pelletCount = 8;   // §5.2 18×8
                // TODO(GDD-?): 아래 전부 임시값 (설계 §3.2)
                a.penetration = 1; a.velocity = 400f; a.maxFlightTimeSec = 0.8f;
                a.size = Vector2Int.one; a.weightKg = 0f;
                a.rarity = Rarity.Scrap; a.basePrice = 0;   // 탄약 정의는 인벤토리 아이템이 아니다 (M5)
            }, Tag);

            SetupUtil.CreateAssetIfMissing<AmmoDefinitionSO>(M7Names.Ammo762Path, a =>
            {
                a.id = M7Names.Ammo762Id; a.displayName = "7.62 탄"; a.type = AmmoType.Rifle762;
                AdsBallisticsSetup.FillNewAmmo(a);   // AB-Q3
                a.damage = 110f;     // §5.2 WPN-DMR-01 피해 (기본값)
                a.pelletCount = 1;
                // TODO(GDD-?): 아래 전부 임시값 (설계 §3.2)
                a.penetration = 4; a.velocity = 820f; a.maxFlightTimeSec = 3.0f;
                a.size = Vector2Int.one; a.weightKg = 0f;
                a.rarity = Rarity.Scrap; a.basePrice = 0;
            }, Tag);
        }

        // ---------- ① 탄창·튜브·탄 상자 (§3.2) ----------
        // 희귀도 = 설계 §3.11(TODO(GDD-?) Q48). 내장 튜브·적 전용은 Scrap(표시 안 됨).

        private static void CreateMagazines()
        {
            SetupUtil.CreateAssetIfMissing<MagazineDefinitionSO>(M7Names.Mag762_5Path, m =>
            {
                m.id = M7Names.Mag762_5Id; m.displayName = "7.62 탄창 (5발)"; m.ammo = AmmoType.Rifle762;
                m.capacity = 5;      // §5.2 WPN-DMR-01 탄창
                // TODO(GDD-?): 크기·무게·가격·희귀도 임시값
                m.size = new Vector2Int(1, 1); m.weightKg = 0.15f; m.basePrice = 300; m.rarity = Rarity.Uncommon;
            }, Tag);

            SetupUtil.CreateAssetIfMissing<MagazineDefinitionSO>(M7Names.Mag762_100Path, m =>
            {
                // 적 전용 (WPN-LMG-01, Q27) — 상인·루트에 없고 시체에서도 빠진다(7B CorpseLootBuilder)
                m.id = M7Names.Mag762_100Id; m.displayName = "7.62 탄띠 (100발)"; m.ammo = AmmoType.Rifle762;
                // TODO(GDD-?): 아래 전부 임시값 (GDD에 경기관총 수치가 없다)
                m.capacity = 100;
                m.size = new Vector2Int(2, 1); m.weightKg = 1.5f; m.basePrice = 0; m.rarity = Rarity.Scrap;
            }, Tag);

            SetupUtil.CreateAssetIfMissing<MagazineDefinitionSO>(M7Names.ShotgunTubePath, m =>
            {
                // 내장 튜브 (Q5) — 무기에 붙은 채로만 존재, 그리드에 놓이지 않는다
                m.id = M7Names.ShotgunTubeId; m.displayName = "12ga 튜브"; m.ammo = AmmoType.Gauge12;
                m.capacity = 6;      // §5.2 WPN-SHT-01 "6 (튜브)"
                m.size = new Vector2Int(1, 1); m.weightKg = 0f; m.basePrice = 0; m.rarity = Rarity.Scrap;
            }, Tag);

            SetupUtil.CreateAssetIfMissing<MagazineDefinitionSO>(M7Names.Box12Ga8Path, m =>
            {
                // 낱탄 = 탄 상자 (Q5·Q6). 무기에 넣을 수 없고, 빈 상자는 0발 아이템으로 남는다.
                m.id = M7Names.Box12Ga8Id; m.displayName = "12ga 탄 상자 (8발)"; m.ammo = AmmoType.Gauge12;
                m.isAmmoBox = true;
                // TODO(GDD-?): 아래 전부 임시값
                m.capacity = 8;
                m.size = new Vector2Int(1, 1); m.weightKg = 0.35f; m.basePrice = 200; m.rarity = Rarity.Common;
            }, Tag);
        }

        // ---------- ① 무기 (§3.1) ----------

        private static void CreateWeapons()
        {
            var tube = SetupUtil.Load<MagazineDefinitionSO>(M7Names.ShotgunTubePath);

            SetupUtil.CreateAssetIfMissing<WeaponDefinitionSO>(M7Names.ShotgunPath, w =>
            {
                w.id = M7Names.ShotgunId; w.displayName = "펌프 샷건"; w.cls = WeaponClass.Shotgun;
                AdsBallisticsSetup.FillNewWeapon(w);   // 정조준·탄도 AB-Q1·Q2
                w.ammo = AmmoType.Gauge12; w.rpm = 70; w.damage = 18f;   // §5.2 (피해 = 펠릿당, 펠릿 수는 AMMO-12GA)
                w.internalMagazine = tube;                                // §5.2 튜브 6
                // TODO(GDD-?): 아래는 GDD에 없는 임시값 (설계 §3.1)
                w.modes = new[] { FireMode.Semi };                        // 펌프 = RPM 간격 반자동
                w.recoilV = 3.5f; w.recoilH = 1.0f; w.adsTime = 0.30f;
                w.roundReloadTimeSec = 0.5f;                              // Q5 발당 장전
                w.reloadTimeSec = 0f; w.fastReloadTimeSec = 0f;           // 튜브 무기는 탄창 재장전이 없다
                w.hipSpreadDeg = 3.0f; w.pelletSpreadDeg = 2.5f;          // Q8
                w.shotNoiseRadiusM = 110f;
                w.size = new Vector2Int(4, 1); w.weightKg = 3.4f;
                w.basePrice = 2500; w.rarity = Rarity.Uncommon;
                w.durabilityLossPerShot = 0.12f;                          // Q11
                w.enemyOnly = false;
            }, Tag);

            SetupUtil.CreateAssetIfMissing<WeaponDefinitionSO>(M7Names.DmrPath, w =>
            {
                w.id = M7Names.DmrId; w.displayName = "볼트액션"; w.cls = WeaponClass.Sniper;
                AdsBallisticsSetup.FillNewWeapon(w);   // 정조준·탄도 AB-Q1·Q2
                w.ammo = AmmoType.Rifle762; w.rpm = 40; w.damage = 110f;   // §5.2 (탄창 5 = MAG-762-5)
                // TODO(GDD-?): 아래는 GDD에 없는 임시값 (설계 §3.1)
                w.modes = new[] { FireMode.Semi };                          // 볼트 동작 = RPM 40 간격
                w.recoilV = 2.8f; w.recoilH = 0.4f; w.adsTime = 0.40f;
                w.reloadTimeSec = 3.2f; w.fastReloadTimeSec = 2.2f;
                w.hipSpreadDeg = 4.0f; w.pelletSpreadDeg = 0f;
                w.shotNoiseRadiusM = 160f;
                w.size = new Vector2Int(5, 1); w.weightKg = 4.2f;
                w.basePrice = 5000; w.rarity = Rarity.Rare;
                w.durabilityLossPerShot = 0.10f;
                w.enemyOnly = false;
            }, Tag);

            SetupUtil.CreateAssetIfMissing<WeaponDefinitionSO>(M7Names.LmgPath, w =>
            {
                // §7 AI-BOS 무장 "경기관총" — §5.2 표에 없다. 적 전용(Q27): 시체·상인·루트에 나오지 않는다.
                w.id = M7Names.LmgId; w.displayName = "경기관총"; w.cls = WeaponClass.MachineGun;
                w.ammo = AmmoType.Rifle762;
                w.enemyOnly = true;
                // TODO(GDD-?): 아래 전부 임시값 (설계 §3.1). 반동·조준·지향 퍼짐은 적이 쓰지 않아 0.
                w.rpm = 600; w.damage = 40f;
                w.modes = new[] { FireMode.Auto };
                w.recoilV = 0f; w.recoilH = 0f; w.adsTime = 0f;
                w.reloadTimeSec = 5.0f; w.fastReloadTimeSec = 4.0f;
                w.hipSpreadDeg = 0f; w.pelletSpreadDeg = 0f;
                w.shotNoiseRadiusM = 160f;
                w.size = new Vector2Int(5, 2); w.weightKg = 9.0f;
                w.basePrice = 0; w.rarity = Rarity.Scrap;
                w.durabilityLossPerShot = 0f;   // 적 무기는 마모 없음 (Q18)
            }, Tag);
        }

        // ---------- ② 기존 무기 마모 (§3.1, TODO(GDD-?) Q11) ----------

        private static void WriteExistingWeaponWear()
        {
            foreach (var (path, id, loss) in ExistingWeaponWear)
            {
                var weapon = SetupUtil.Load<WeaponDefinitionSO>(path);
                if (weapon == null)
                    continue;
                if (weapon.id != id)
                {
                    Debug.LogError($"{Tag} {path}의 id가 {weapon.id}다 (기대 {id}). 마모를 기록하지 않는다.", weapon);
                    continue;
                }
                // 파일에 필드가 이미 있고 0보다 크면 조정했을 수 있으니 보존한다.
                // 0은 미설정으로 본다 — 다른 메뉴(뷰모델 연결 등)가 먼저 정의를 저장하면 코드 기본값 0이 직렬화되기 때문 (M7 리뷰 7A 낮음 4).
                // TODO(GDD-?) Q11: 기존 무기 마모를 의도적으로 0(마모 없음)으로 두려면 이 규칙이 덮어쓴다 — 아주 작은 값을 쓸 것.
                if (!ShouldWriteExistingWear(SetupUtil.IsFieldSerialized(path, nameof(WeaponDefinitionSO.durabilityLossPerShot)),
                        weapon.durabilityLossPerShot))
                    continue;
                weapon.durabilityLossPerShot = loss;
                EditorUtility.SetDirty(weapon);
                Debug.Log($"{Tag} {id}.durabilityLossPerShot = {loss}");
            }
        }

        // 기존 무기 마모를 기록할지: 파일에 필드가 없거나 값이 0 이하(미설정)면 true.
        public static bool ShouldWriteExistingWear(bool fieldSerialized, float current) => !fieldSerialized || !(current > 0f);

        // ---------- ③ WeaponHandlingConfig (§3.3) ----------

        private static void WriteHandlingConfig()
        {
            string path = M7Names.WeaponHandlingConfigPath;
            var c = SetupUtil.Load<WeaponHandlingConfigSO>(path);
            if (c == null)
                return;

            // 파일에 없는 필드만 설계값으로 (모두 판정한 뒤 기록 — 중간 저장 없음)
            bool changed = false;
            changed |= WriteIfMissing(path, nameof(WeaponHandlingConfigSO.jamDurabilityThreshold), () => c.jamDurabilityThreshold = 50f);        // §5.1
            changed |= WriteIfMissing(path, nameof(WeaponHandlingConfigSO.jamChancePerDurabilityPoint), () => c.jamChancePerDurabilityPoint = 0.001f); // §5.1 0.1%
            changed |= WriteIfMissing(path, nameof(WeaponHandlingConfigSO.jamClearHoldSec), () => c.jamClearHoldSec = 1.2f);                // TODO(GDD-?) Q9
            changed |= WriteIfMissing(path, nameof(WeaponHandlingConfigSO.weaponSwitchTimeSec), () => c.weaponSwitchTimeSec = 0.5f);        // TODO(GDD-?) Q1

            // 탄약 표: 빠진 탄 종류만 끝에 추가 (이미 있는 종류의 정의는 바꾸지 않는다)
            var table = new List<AmmoDefinitionSO>(c.ammoDefinitions ?? new AmmoDefinitionSO[0]);
            foreach (string ammoPath in AmmoTablePaths)
            {
                var ammo = SetupUtil.Load<AmmoDefinitionSO>(ammoPath);
                if (ammo == null || c.TryGetAmmo(ammo.type, out _))
                    continue;
                table.Add(ammo);
                c.ammoDefinitions = table.ToArray();
                changed = true;
                Debug.Log($"{Tag} WeaponHandlingConfig.ammoDefinitions += {ammo.id}");
            }

            if (changed)
            {
                EditorUtility.SetDirty(c);
                Debug.Log($"{Tag} WeaponHandlingConfig 기록: {path}");
            }
        }

        // ---------- ④ 상인·상자 (Q20, §3.9) ----------

        private static void AppendTraderCatalog()
        {
            var trader = SetupUtil.Load<TraderConfigSO>(M5DataSetup.TraderConfigPath);
            if (trader == null)
                return;
            var catalog = new List<ItemDefinitionSO>(trader.catalog ?? new ItemDefinitionSO[0]);
            bool changed = false;
            foreach (string path in TraderAdditionPaths)
            {
                var item = SetupUtil.Load<ItemDefinitionSO>(path);
                if (item == null || catalog.Contains(item))
                    continue;
                catalog.Add(item);
                changed = true;
                Debug.Log($"{Tag} TraderConfig.catalog += {item.id}");
            }
            if (!changed)
                return;
            trader.catalog = catalog.ToArray();
            EditorUtility.SetDirty(trader);
        }

        private static void AppendCrateLoot()
        {
            var crate = SetupUtil.Load<LootTableSO>(M4DataSetup.LootCratePath);
            if (crate == null)
                return;
            if (crate.entries == null)
                crate.entries = new List<LootEntry>();
            bool changed = false;
            foreach (var (path, weight) in CrateAdditions)
            {
                var item = SetupUtil.Load<ItemDefinitionSO>(path);
                if (item == null || crate.entries.Any(e => e.item == item))
                    continue;   // 이미 있으면 가중치를 조정했을 수 있으니 보존
                crate.entries.Add(new LootEntry { item = item, weight = weight });
                changed = true;
                Debug.Log($"{Tag} LT-CRATE += {item.id} ({weight})");
            }
            if (changed)
                EditorUtility.SetDirty(crate);
        }

        // ---------- 도우미 ----------

        // 필드가 파일에 없으면 set을 부르고 true. 있으면 아무것도 안 한다 (M6DataSetup과 같음).
        private static bool WriteIfMissing(string path, string fieldName, System.Action set)
        {
            if (SetupUtil.IsFieldSerialized(path, fieldName))
                return false;
            set();
            return true;
        }
    }
}
