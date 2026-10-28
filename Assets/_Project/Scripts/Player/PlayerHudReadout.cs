using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Player
{
    // 상시 HUD(B안, 2026-10-28 — RULE-05 개정)용 읽기 전용 수치 창구. PlayerHealth가 만들어 IPlayerHudSource로 등록한다.
    // 모델을 그대로 읽기만 한다(할당 없음). 스태미나는 같은 오브젝트의 PlayerMotor에서 읽는다(없으면 가득 찬 것으로 본다).
    public sealed class PlayerHudReadout : IPlayerHudSource
    {
        private readonly BodyHealthModel body;
        private readonly SurvivalModel survival;
        private readonly RadiationDoseModel radiation;
        private readonly PlayerConfigSO config;
        private readonly PlayerMotor motor;

        public PlayerHudReadout(BodyHealthModel body, SurvivalModel survival, RadiationDoseModel radiation, PlayerConfigSO config, PlayerMotor motor)
        {
            this.body = body;
            this.survival = survival;
            this.radiation = radiation;
            this.config = config;
            this.motor = motor;
        }

        public float GetPartHp(BodyPart part) => Mathf.Max(0f, body.GetHp(part));
        public float GetPartMaxHp(BodyPart part) => body.GetMaxHp(part);
        public bool HasStatus(BodyPart part, StatusEffect effect) => body.HasStatus(part, effect);
        public SurvivalTier GetSurvivalTier(SurvivalStat stat) => body.GetSurvivalTier(stat);

        // PlayerMotor.Stamina는 그쪽 Awake에서 만들어진다 — 매번 읽는다(생성 순서 무관)
        public float Stamina => motor != null && motor.Stamina != null ? motor.Stamina.Current : config.staminaMax;
        public float StaminaMax => config.staminaMax;
        public float Hydration => survival != null ? survival.Hydration : 0f;
        public float HydrationMax => survival != null ? survival.HydrationMax : 0f;
        public float Energy => survival != null ? survival.Energy : 0f;
        public float EnergyMax => survival != null ? survival.EnergyMax : 0f;
        public float RadiationDose => radiation != null ? radiation.Dose : 0f;
        public float WeightPenaltyStartKg => config.weightPenaltyStartKg;
        public float SprintMaxWeightKg => config.sprintMaxWeightKg;

        // 피격 방향 (§12 붉은 가장자리): 탄이 날아온 쪽(이동 방향의 반대)을 플레이어 정면 기준 수평 각도로. + = 오른쪽.
        // 이동 방향이 없거나 수직이면 false.
        public static bool TryRelativeYawDeg(Vector3 forward, Vector3 travelDirection, out float yawDeg)
        {
            yawDeg = 0f;
            Vector3 toSource = new Vector3(-travelDirection.x, 0f, -travelDirection.z);
            Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
            if (toSource.sqrMagnitude < 1e-6f || flatForward.sqrMagnitude < 1e-6f)
                return false;
            yawDeg = Vector3.SignedAngle(flatForward, toSource, Vector3.up);
            return true;
        }
    }
}
