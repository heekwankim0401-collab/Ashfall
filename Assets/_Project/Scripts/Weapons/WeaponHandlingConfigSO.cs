using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Weapons
{
    // 무기 조작 공통 수치: RULE-04 탄창 확인, 조준 흔들림, M7 고장·교체·탄약 표. §14에 없는 SO (설계 D6, M7 §3.3).
    // 에셋: Data/Weapons/WeaponHandlingConfig.asset
    [CreateAssetMenu(menuName = "Ashfall/Weapon Handling Config", fileName = "WeaponHandlingConfig")]
    public class WeaponHandlingConfigSO : ScriptableObject
    {
        [Header("RULE-04 탄창 확인")]
        [Tooltip("T를 누르고 결과가 나올 때까지 (초)")]
        public float magCheckTimeSec = 0.8f;
        // TODO(GDD-?): RULE-04 구간 경계가 문서에 없다. 탄 수/용량 비율 임시값.
        [Tooltip("이 비율 이상이면 '가득'")]
        public float magCheckFullAtOrAbove = 0.67f;
        [Tooltip("이 비율 이상이면 '절반', 그 아래는 '거의 없음' (0발 포함)")]
        public float magCheckHalfAtOrAbove = 0.34f;

        // TODO(GDD-?): M1에 조준 흔들림이 없었다. 팔 파괴 +100%(§4.2)가 의미 있도록 기본값을 둔다.
        [Header("조준 흔들림 (임시값)")]
        [Tooltip("정상 상태 흔들림 최대 각 (도)")]
        public float aimSwayBaseDeg = 0.15f;
        [Tooltip("흔들림 빠르기 (Hz)")]
        public float aimSwayFrequencyHz = 0.5f;

        // TODO(GDD-?): 문서에 없음. 2a에서 추가 (설계 §5.2 "카메라 아래쪽 속도").
        [Header("탄창 버리기 (임시값)")]
        [Tooltip("빠른 재장전·회수 공간 없음으로 버린 탄창의 아래쪽 초기 속도 (m/s)")]
        public float magazineDropSpeed = 1.0f;

        // ── M7 (설계 §3.3) ──────────────────────────────────
        [Header("고장 (§5.1)")]
        [Tooltip("내구도가 이 값(%) 미만이면 고장 확률이 생긴다 (GDD §5.1 50%)")]
        public float jamDurabilityThreshold = 50f;
        [Tooltip("임계 아래 내구도 1점당 발당 고장 확률 (GDD §5.1 0.1% = 0.001)")]
        public float jamChancePerDurabilityPoint = 0.001f;
        // TODO(GDD-?) Q9: §5.1 "T 길게"의 길이가 문서에 없다.
        [Tooltip("고장 해제에 T를 계속 누르는 시간 (초). 떼면 처음부터")]
        public float jamClearHoldSec = 1.2f;

        // TODO(GDD-?) Q1: 교체 시간이 문서에 없다.
        [Header("무기 교체 (§12.1 1/2/3)")]
        [Tooltip("교체 시간 (초). 이 동안 사격·조준·재장전·T 불가")]
        public float weaponSwitchTimeSec = 0.5f;

        // TODO(GDD-?) Q3: 구경마다 탄 1종(MVP). 무기 ammo(AmmoType) → 이 표의 정의.
        [Header("탄약 정의 표")]
        [Tooltip("AmmoType마다 AmmoDefinitionSO 하나 (AMMO-9MM·556·762·12GA)")]
        public AmmoDefinitionSO[] ammoDefinitions = new AmmoDefinitionSO[0];

        // 표에서 그 탄 종류의 첫 정의. 없으면 false. 할당 없음.
        public bool TryGetAmmo(AmmoType type, out AmmoDefinitionSO ammo)
        {
            if (ammoDefinitions != null)
            {
                for (int i = 0; i < ammoDefinitions.Length; i++)
                {
                    AmmoDefinitionSO a = ammoDefinitions[i];
                    if (a != null && a.type == type)
                    {
                        ammo = a;
                        return true;
                    }
                }
            }
            ammo = null;
            return false;
        }

        // 플레이어 무기 마모·고장 옵션 (WeaponModel). 적 무기는 쓰지 않는다 (Q18).
        public WeaponWear CreateWear(System.Random random) =>
            new WeaponWear(jamDurabilityThreshold, jamChancePerDurabilityPoint, random);
    }
}
