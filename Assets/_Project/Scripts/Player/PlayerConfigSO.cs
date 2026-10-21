using UnityEngine;

namespace Ashfall.Player
{
    // §14 PlayerConfigSO — §4 플레이어 수치. 값은 에셋(Data/Player/PlayerConfig)에서 관리한다.
    [CreateAssetMenu(menuName = "Ashfall/Player Config", fileName = "PlayerConfig")]
    public class PlayerConfigSO : ScriptableObject
    {
        [Header("§4.1 속도 (m/s)")]
        public float walkSpeed;
        public float sprintSpeed;
        public float crouchSpeed;
        public float proneSpeed;

        [Header("§4.1 소음 반경 (m)")]
        public float walkNoise;
        public float sprintNoise;
        public float crouchNoise;
        public float proneNoise;
        public float jumpNoise;

        [Header("§4.1 점프·기울이기")]
        public float jumpHeightM;
        public float jumpStaminaCost;
        public float leanAngleDeg;

        [Header("§4.1 스태미나")]
        public float staminaMax;
        public float sprintStaminaPerSec;
        public float staminaRegenMovingPerSec;
        public float staminaRegenStationaryPerSec;
        public float exhaustedSprintLockSec;

        [Header("§4.1 무게 패널티")]
        public float weightPenaltyStartKg;
        [Tooltip("초과 1kg당 속도 감소 비율 (0.02 = 2%)")]
        public float weightPenaltyPerKg;
        public float sprintMaxWeightKg;

        // 아래는 §4에 없는 수치 (CHANGELOG_GDD 2026-10-21). TODO(GDD-?): 기획 확정 필요.
        [Header("자세별 캡슐 높이·눈높이 (m)")]
        public float standHeight;
        public float crouchHeight;
        public float proneHeight;
        [Tooltip("캡슐 꼭대기에서 눈까지 거리")]
        public float eyeBelowTop;
        [Tooltip("자세 전환 시 눈높이 이동 속도 (m/s)")]
        public float stanceChangeSpeed;

        [Header("시점")]
        [Tooltip("마우스 1픽셀당 회전 (도)")]
        public float lookSensitivity;
        public float pitchLimitDeg;
        [Tooltip("기울일 때 눈 위치 좌우 이동 (m)")]
        public float leanOffsetM;
        [Tooltip("기울이기 전환 속도 (초당 0→1)")]
        public float leanSpeed;

        [Header("물리")]
        [Tooltip("접지 중 아래로 누르는 속도 (m/s, 음수)")]
        public float groundStickSpeed;
    }
}
