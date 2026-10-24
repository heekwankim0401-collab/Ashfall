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

        // M6 설계 §3.6, Q12: 발소리(FootstepTaken) 리듬. AI 청각용 소음 펄스와 별개.
        [Header("M6 발소리 보폭 (m, TODO(GDD-?))")]
        [Tooltip("걷기 보폭 (걷기 3.0m/s → 2걸음/s)")]
        public float strideWalkM = 1.5f;
        public float strideSprintM = 2.2f;
        public float strideCrouchM = 0.8f;
        public float strideProneM = 0.6f;

        // ── M7: §4.3 생존 STAT-DEHY·HUNG·RAD (M7 설계 §3.6, 7C) ─────
        // 7C-1은 필드만 — 동작은 7C-2a(SurvivalModel·PlayerHealth·PlayerMotor). 레이드마다 최대/0에서 시작(이월 없음, Q32).
        [Header("M7 §4.3 수분 (STAT-DEHY)")]
        [Tooltip("수분 최대 = 레이드 시작 값 (GDD §4.3 100)")]
        [Min(0f)] public float hydrationMax = 100f;
        [Tooltip("초당 수분 감소 (GDD §4.3 0.1/s → 1000초에 0)")]
        [Min(0f)] public float hydrationDecayPerSec = 0.1f;
        [Tooltip("수분 0일 때 머리를 뺀 파괴되지 않은 부위마다 초당 HP 감소 (GDD §4.3 0.5 — 흉부 80 → 160초, 부위마다·머리 제외 해석 TODO(GDD-?) Q33)")]
        [Min(0f)] public float dehydratedHpPerSec = 0.5f;
        [Tooltip("복부 파괴 중 수분 감소 배율 (§4.2 '탈수 가속', TODO(GDD-?) Q34)")]
        [Min(0f)] public float stomachDestroyedHydrationMultiplier = 2f;

        [Header("M7 §4.3 에너지 (STAT-HUNG)")]
        [Tooltip("에너지 최대 = 레이드 시작 값 (GDD §4.3 100)")]
        [Min(0f)] public float energyMax = 100f;
        [Tooltip("초당 에너지 감소 (GDD §4.3 0.07/s → 약 1429초에 0)")]
        [Min(0f)] public float energyDecayPerSec = 0.07f;
        [Tooltip("에너지 0일 때 스태미나 재생 배율 (GDD §4.3 −50% = 0.5)")]
        [Range(0f, 1f)] public float starvingStaminaRegenMultiplier = 0.5f;

        [Header("M7 §4.3 방사능 (STAT-RAD)")]
        [Tooltip("이 선량 이상이면 방사선 피폭 — 모든 부위 최대 HP 배율 적용 (GDD §4.3 누적 100)")]
        [Min(0f)] public float radiationSicknessDose = 100f;
        [Tooltip("피폭 중 모든 부위 최대 HP 배율 (GDD §4.3 −20% = 0.8). 현재 HP는 새 최대로 자른다 (Q35)")]
        [Range(0f, 1f)] public float radiationSicknessMaxHpMultiplier = 0.8f;
        [Tooltip("이 선량 이상이면 '방사능 오염' 등급 Low (TODO(GDD-?) Q43)")]
        [Min(0f)] public float radiationWarnDose = 50f;

        [Header("M7 생존 등급 경계 (RULE-05, TODO(GDD-?) Q43)")]
        [Tooltip("수분·에너지가 최대 × 이 비율 이하면 Low(목마름·배고픔), 0이면 Critical(탈수·허기)")]
        [Range(0f, 1f)] public float survivalLowRatio = 0.25f;

        // MVP 마무리 F4 (설계 §2, MF-Q17·Q22). 적은 AiConfigSO의 같은 4필드를 쓴다(값이 같은지 EditMode 테스트가 고정).
        [Header("§5.3 방어구")]
        [Tooltip("관통 확률 기본값 (GDD §5.3 0.5). 관통확률 = clamp01(이 값 + (탄관통력 − 방어등급) × 등급당 변화)")]
        [Range(0f, 1f)] public float armorBasePenetrationChance = 0.5f;
        [Tooltip("관통력 − 방어등급 1당 관통 확률 변화 (GDD §5.3 0.25)")]
        [Min(0f)] public float armorPenetrationPerClassStep = 0.25f;
        [Tooltip("비관통 시 몸이 받는 피해 비율 (GDD §5.3 둔탁 피해 30% = 0.3)")]
        [Range(0f, 1f)] public float armorBluntDamageRatio = 0.3f;
        [Tooltip("비관통 시 방어구 내구도 감소 = 원피해 × 이 값 (TODO(GDD-?) MF-Q17 임시값 0.2). 관통은 마모 없음")]
        [Min(0f)] public float armorDurabilityLossPerDamage = 0.2f;

        // §5.3 판정 수치 묶음 (Core ArmorRules에 넘긴다)
        public ArmorRuleParams ArmorRuleParams => new ArmorRuleParams(
            armorBasePenetrationChance, armorPenetrationPerClassStep, armorBluntDamageRatio, armorDurabilityLossPerDamage);

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
