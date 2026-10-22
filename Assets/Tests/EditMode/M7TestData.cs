using Ashfall.Core;
using Ashfall.Weapons;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7A 테스트용 정의 (설계 §3.1·§3.2 값). GDD = RPM·피해·펠릿·튜브 6·탄창 5, 나머지는 TODO(GDD-?) 임시값.
    // 7A-1 계약 파일 — 병렬 단계(7A-2)는 고치지 않는다. 추가 도우미는 각 테스트 파일 안에.
    internal static class M7TestData
    {
        // MAG-12GA-TUBE 내장 튜브 (용량 6, GDD)
        public static MagazineDefinitionSO ShotgunTube()
        {
            var m = ScriptableObject.CreateInstance<MagazineDefinitionSO>();
            m.id = "MAG-12GA-TUBE"; m.displayName = "12ga 튜브"; m.size = Vector2Int.one;
            m.ammo = AmmoType.Gauge12; m.capacity = 6;
            return m;
        }

        // BOX-12GA-8 탄 상자 (isAmmoBox)
        public static MagazineDefinitionSO Box12Ga8()
        {
            var m = ScriptableObject.CreateInstance<MagazineDefinitionSO>();
            m.id = "BOX-12GA-8"; m.displayName = "12ga 탄 상자"; m.size = Vector2Int.one; m.weightKg = 0.35f;
            m.ammo = AmmoType.Gauge12; m.capacity = 8; m.isAmmoBox = true;
            return m;
        }

        // MAG-762-5 (DMR 탄창, 용량 5 GDD)
        public static MagazineDefinitionSO Mag762_5()
        {
            var m = ScriptableObject.CreateInstance<MagazineDefinitionSO>();
            m.id = "MAG-762-5"; m.displayName = "7.62 탄창"; m.size = Vector2Int.one; m.weightKg = 0.15f;
            m.ammo = AmmoType.Rifle762; m.capacity = 5;
            return m;
        }

        // WPN-SHT-01 펌프 샷건: 12ga, 70rpm, 펠릿당 18, 튜브 6, 한 발 0.5초
        public static WeaponDefinitionSO Shotgun(MagazineDefinitionSO tube = null)
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            w.id = "WPN-SHT-01"; w.displayName = "펌프 샷건"; w.size = new Vector2Int(4, 1); w.weightKg = 3.4f;
            w.cls = WeaponClass.Shotgun; w.ammo = AmmoType.Gauge12; w.rpm = 70; w.modes = new[] { FireMode.Semi };
            w.damage = 18f;
            w.recoilV = 3.5f; w.recoilH = 1.0f; w.adsTime = 0.30f;
            w.hipSpreadDeg = 3.0f; w.pelletSpreadDeg = 2.5f; w.shotNoiseRadiusM = 110f;
            w.internalMagazine = tube != null ? tube : ShotgunTube();
            w.roundReloadTimeSec = 0.5f;
            w.durabilityLossPerShot = 0.12f;
            return w;
        }

        // WPN-DMR-01 볼트액션: 7.62, 40rpm, 110, 탄창 5
        public static WeaponDefinitionSO Dmr()
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            w.id = "WPN-DMR-01"; w.displayName = "볼트액션"; w.size = new Vector2Int(5, 1); w.weightKg = 4.2f;
            w.cls = WeaponClass.Sniper; w.ammo = AmmoType.Rifle762; w.rpm = 40; w.modes = new[] { FireMode.Semi };
            w.damage = 110f;
            w.recoilV = 2.8f; w.recoilH = 0.4f; w.adsTime = 0.40f;
            w.reloadTimeSec = 3.2f; w.fastReloadTimeSec = 2.2f; w.hipSpreadDeg = 4.0f; w.shotNoiseRadiusM = 160f;
            w.durabilityLossPerShot = 0.10f;
            return w;
        }

        // WeaponHandlingConfigSO 기본값 (§3.3: 임계 50·0.1%/점 = GDD, 해제 1.2초·교체 0.5초 = TODO)
        public static WeaponHandlingConfigSO Handling() => ScriptableObject.CreateInstance<WeaponHandlingConfigSO>();
    }
}
