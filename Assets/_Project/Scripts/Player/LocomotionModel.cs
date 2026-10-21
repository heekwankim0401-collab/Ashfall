using UnityEngine;

namespace Ashfall.Player
{
    public enum Stance
    {
        Stand,
        Crouch,
        Prone,
    }

    // §4.1 이동 규칙: 자세 토글, 질주 가능 여부, 속도(무게 패널티 포함), 소음 반경.
    public sealed class LocomotionModel
    {
        private readonly PlayerConfigSO config;

        public Stance Stance { get; private set; } = Stance.Stand;

        public LocomotionModel(PlayerConfigSO config)
        {
            this.config = config;
        }

        // C: 웅크리기 토글. 엎드린 상태에서 누르면 웅크리기로.
        public void ToggleCrouch() => Stance = Stance == Stance.Crouch ? Stance.Stand : Stance.Crouch;

        // Z: 엎드리기 토글. 웅크린 상태에서 누르면 엎드리기로.
        // TODO(GDD-?): 머리 위가 막혔을 때 일어서기 막기는 미구현.
        public void ToggleProne() => Stance = Stance == Stance.Prone ? Stance.Stand : Stance.Prone;

        // 25kg 초과 1kg당 -2%. 0 아래로는 내려가지 않는다.
        public float WeightMultiplier(float carriedKg)
        {
            float over = Mathf.Max(0f, carriedKg - config.weightPenaltyStartKg);
            return Mathf.Clamp01(1f - over * config.weightPenaltyPerKg);
        }

        // 질주는 선 자세에서 움직일 때만, 40kg 이하, 스태미나가 남고 잠금이 아닐 때.
        public bool CanSprint(bool moving, float carriedKg, StaminaModel stamina)
        {
            return Stance == Stance.Stand
                && moving
                && carriedKg <= config.sprintMaxWeightKg
                && !stamina.SprintLocked
                && stamina.Current > 0f;
        }

        // TODO(GDD-?): 키 표 §12.1에 점프 키가 없어 Space를 쓴다. 웅크림·엎드림 중 점프는 막는다.
        public bool CanJump(bool grounded) => grounded && Stance == Stance.Stand;

        public float Speed(bool sprinting, float carriedKg)
        {
            float baseSpeed = Stance switch
            {
                Stance.Crouch => config.crouchSpeed,
                Stance.Prone => config.proneSpeed,
                _ => sprinting ? config.sprintSpeed : config.walkSpeed,
            };
            return baseSpeed * WeightMultiplier(carriedKg);
        }

        // 서 있으면(이동 없음) 소음 0. 점프 소음은 JumpNoise로 따로 낸다.
        public float NoiseRadius(bool moving, bool sprinting)
        {
            if (!moving)
                return 0f;
            return Stance switch
            {
                Stance.Crouch => config.crouchNoise,
                Stance.Prone => config.proneNoise,
                _ => sprinting ? config.sprintNoise : config.walkNoise,
            };
        }

        public float JumpNoise => config.jumpNoise;

        public float CapsuleHeight => Stance switch
        {
            Stance.Crouch => config.crouchHeight,
            Stance.Prone => config.proneHeight,
            _ => config.standHeight,
        };

        public float EyeHeight => CapsuleHeight - config.eyeBelowTop;

        // v = √(2gh)
        public float JumpVelocity(float gravity) => Mathf.Sqrt(2f * Mathf.Abs(gravity) * config.jumpHeightM);
    }
}
