using Ashfall.Core;
using Ashfall.Player;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 테스트용 설정. §4.1·§5.2 표 값을 그대로 넣는다 (에셋 값이 바뀌어도 규칙 검증은 표 기준).
    internal static class TestData
    {
        public static PlayerConfigSO Player()
        {
            var c = ScriptableObject.CreateInstance<PlayerConfigSO>();
            c.walkSpeed = 3.0f; c.sprintSpeed = 5.5f; c.crouchSpeed = 1.6f; c.proneSpeed = 0.8f;
            c.walkNoise = 8f; c.sprintNoise = 20f; c.crouchNoise = 3f; c.proneNoise = 1f; c.jumpNoise = 15f;
            c.jumpHeightM = 1.0f; c.jumpStaminaCost = 20f; c.leanAngleDeg = 15f;
            c.staminaMax = 100f; c.sprintStaminaPerSec = 15f;
            c.staminaRegenMovingPerSec = 10f; c.staminaRegenStationaryPerSec = 20f; c.exhaustedSprintLockSec = 3f;
            c.weightPenaltyStartKg = 25f; c.weightPenaltyPerKg = 0.02f; c.sprintMaxWeightKg = 40f;
            c.standHeight = 1.8f; c.crouchHeight = 1.2f; c.proneHeight = 0.6f; c.eyeBelowTop = 0.12f;
            return c;
        }

        public static WeaponDefinitionSO Rifle()
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            w.id = "WPN-RIF-01"; w.ammo = AmmoType.Rifle556; w.rpm = 650; w.modes = new[] { FireMode.Auto };
            w.reloadTimeSec = 2.5f; w.fastReloadTimeSec = 1.5f;
            return w;
        }

        public static MagazineDefinitionSO Magazine30()
        {
            var m = ScriptableObject.CreateInstance<MagazineDefinitionSO>();
            m.ammo = AmmoType.Rifle556; m.capacity = 30;
            return m;
        }
    }
}
