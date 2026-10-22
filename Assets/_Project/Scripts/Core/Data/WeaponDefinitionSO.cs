using UnityEngine;

namespace Ashfall.Core
{
    // §14 무기 정의.
    [CreateAssetMenu(menuName = "Ashfall/Weapon", fileName = "Weapon")]
    public class WeaponDefinitionSO : ItemDefinitionSO
    {
        public WeaponClass cls;
        public AmmoType ammo;
        public int rpm;
        public FireMode[] modes = { FireMode.Auto };
        [Tooltip("발당 수직 반동 (도)")]
        public float recoilV;
        [Tooltip("발당 수평 반동 최대치 (도, 좌우 무작위)")]
        public float recoilH;
        [Tooltip("지향 사격 → 조준 전환 시간 (초)")]
        public float adsTime;
        public GameObject viewModel;

        // §14에 없는 필드 (CHANGELOG_GDD 2026-10-21).
        [Tooltip("일반 재장전 시간 (초) — 빼낸 탄창을 회수")]
        public float reloadTimeSec;
        [Tooltip("빠른 재장전 시간 (초) — 빼낸 탄창을 버림")]
        public float fastReloadTimeSec;
        [Tooltip("지향 사격 탄 퍼짐 원뿔 반각 (도). 조준(ADS) 시 0 (RULE-07)")]
        public float hipSpreadDeg;

        // M3 결정 (§5.2 ↔ §14 피해 위치). 투사체 피해는 DamageRules.ProjectileDamage로 정한다.
        [Tooltip("§5.2 발당 피해 (산탄은 펠릿당). 0이면 탄약 damage를 쓴다 (M3 결정)")]
        public float damage;
        [Tooltip("사격 소음 반경 (m). 0이면 소음 없음. TODO(GDD-?): GDD에 없는 임시값")]
        public float shotNoiseRadiusM;

        // §14에 없는 필드 (M7 설계 §1.3·§3.1).
        // TODO(GDD-?) Q5: 튜브 = 내장 탄창 정의. 있으면 무기 생성·세이브 복원이 빈 튜브를 LoadedMagazine으로 만든다(떼어낼 수 없음).
        [Tooltip("내장 탄창(튜브). 비면 분리형 탄창 무기. 있으면 R = 한 발씩 장전(ILooseAmmoSource)")]
        public MagazineDefinitionSO internalMagazine;
        [Tooltip("튜브 장전 한 발당 시간 (초). TODO(GDD-?) Q5 임시값")]
        public float roundReloadTimeSec;
        [Tooltip("펠릿마다 퍼지는 원뿔 반각 (도, 조준 중에도). 0이면 펠릿 퍼짐 없음. TODO(GDD-?) Q8 임시값")]
        public float pelletSpreadDeg;
        [Tooltip("발사 1회당 내구도 감소 (%p, 플레이어 무기만). TODO(GDD-?) Q11 임시값")]
        public float durabilityLossPerShot;
        [Tooltip("적 전용 무기 — 시체·상인·루트에 나오지 않는다 (TODO(GDD-?) Q27)")]
        public bool enemyOnly;
    }
}
