using UnityEngine;

namespace Ashfall.Weapons
{
    // 무기 조작 공통 수치: RULE-04 탄창 확인, 조준 흔들림. §14에 없는 SO (설계 D6).
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
    }
}
