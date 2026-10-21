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
    }
}
