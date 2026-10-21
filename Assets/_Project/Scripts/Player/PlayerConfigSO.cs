using Ashfall.Core;
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

        // ── M2: §4.2 부위별 체력 (SYS-HP-01) ─────────────────
        [Header("§4.2 부위 최대 HP")]
        public float headMaxHp = 35f;
        public float thoraxMaxHp = 80f;
        public float stomachMaxHp = 70f;
        [Tooltip("팔 하나")]
        public float armMaxHp = 60f;
        [Tooltip("다리 하나")]
        public float legMaxHp = 65f;

        [Header("§4.2 파괴 효과")]
        [Tooltip("파괴된 부위가 받은 피해 중 흉부·복부로 분산되는 비율")]
        public float destroyedPartRedistribution = 0.7f;
        [Tooltip("복부 파괴 후 받는 피격 피해 배율 (출혈 지속 피해에는 미적용)")]
        public float stomachDestroyedDamageMultiplier = 1.5f;
        [Tooltip("팔 하나 파괴당 조준 흔들림 배율 증가 (1 = +100%)")]
        public float armDestroyedAimSwayAdd = 1.0f;
        [Tooltip("다리 파괴 시 이동 속도 배율 (0.6 = -40%), 질주 불가")]
        public float legDestroyedSpeedMultiplier = 0.6f;

        [Header("§4.3 출혈 (HP/s)")]
        public float bleedHpPerSec = 1f;
        public float heavyBleedHpPerSec = 3f;

        // TODO(GDD-?): §4.3에 발생 조건·확률이 없다. 아래는 M2 임시값.
        [Header("§4.3 상태이상 발생 (임시값)")]
        [Tooltip("출혈·중출혈 굴림 최소 피격 피해")]
        public float bleedMinDamage = 10f;
        [Tooltip("중출혈 확률 (먼저 굴림)")]
        public float heavyBleedChance = 0.15f;
        [Tooltip("출혈 확률 (중출혈이 안 나왔을 때)")]
        public float bleedChance = 0.35f;
        [Tooltip("골절 굴림 최소 피격 피해 (팔·다리만)")]
        public float fractureMinDamage = 20f;
        public float fractureChance = 0.2f;
        [Tooltip("골절 다리 이동 속도 배율. TODO(GDD-?): §4.3 '이동 패널티' 크기 없음")]
        public float fractureLegSpeedMultiplier = 0.8f;
        [Tooltip("골절 팔 하나당 조준 흔들림 배율 증가. TODO(GDD-?): §4.3 '조준 패널티' 크기 없음")]
        public float fractureArmAimSwayAdd = 0.5f;

        // TODO(GDD-?): RULE-05 등급 경계가 문서에 없다. HP 비율 기준 임시값.
        [Header("RULE-05 등급 경계 (HP 비율, 임시값)")]
        [Tooltip("이 비율 미만이면 부상")]
        public float tierInjuredBelow = 0.75f;
        [Tooltip("이 비율 미만이면 중상")]
        public float tierCriticalBelow = 0.35f;

        // TODO(GDD-?): 캡슐 피격 지점 → 부위 판정 (설계 D3, 그레이박스 근사). 엎드림에서는 부정확.
        [Header("부위 판정 (캡슐 높이 비율, 임시값)")]
        [Tooltip("이 높이 비율 이상 = 머리")]
        public float headZoneFrom = 0.87f;
        [Tooltip("이 높이 비율 이상 = 흉부")]
        public float thoraxZoneFrom = 0.62f;
        [Tooltip("이 높이 비율 이상 = 복부, 그 아래 = 다리")]
        public float stomachZoneFrom = 0.45f;
        [Tooltip("흉부·복부 구간에서 |x| ≥ 반지름 × 이 값이면 팔")]
        public float armZoneSideRatio = 0.6f;

        [Header("소음 발행 (M3 임시값)")]
        [Tooltip("이동 중 발소리 소음(NoiseEmitted)을 내는 간격 (초). TODO(GDD-?)")]
        public float noisePulseIntervalSec = 0.5f;

        // 부위 최대 HP (§4.2)
        public float MaxHp(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.Head: return headMaxHp;
                case BodyPart.Thorax: return thoraxMaxHp;
                case BodyPart.Stomach: return stomachMaxHp;
                case BodyPart.LeftArm:
                case BodyPart.RightArm: return armMaxHp;
                default: return legMaxHp;
            }
        }
    }
}
