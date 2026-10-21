using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M3 결정: 무기 피해 > 0이면 무기 값, 아니면 탄약 값 (§5.2 무기별 피해, SYS-WPN, SYS-AI AC4)
    public class DamageRulesTests
    {
        [Test]
        public void WeaponDamagePositive_UsesWeaponValue()
        {
            Assert.AreEqual(35f, DamageRules.ProjectileDamage(M3TestData.Pistol9(), M3TestData.Ammo9()));
            Assert.AreEqual(30f, DamageRules.ProjectileDamage(M3TestData.Smg9(), M3TestData.Ammo9()));
            Assert.AreEqual(45f, DamageRules.ProjectileDamage(M3TestData.RifleWithDamage(), M3TestData.Ammo556()));
        }

        [Test]
        public void WeaponDamageZero_FallsBackToAmmo()
        {
            WeaponDefinitionSO rifle = TestData.Rifle();   // damage 미지정(0) = M2까지의 에셋 상태
            Assert.AreEqual(0f, rifle.damage);
            Assert.AreEqual(45f, DamageRules.ProjectileDamage(rifle, M3TestData.Ammo556()));
        }

        [Test]
        public void WeaponDamageNegative_FallsBackToAmmo()
        {
            WeaponDefinitionSO pistol = M3TestData.Pistol9();
            pistol.damage = -5f;
            Assert.AreEqual(30f, DamageRules.ProjectileDamage(pistol, M3TestData.Ammo9()));
        }

        [Test]
        public void NullWeapon_UsesAmmo()
        {
            Assert.AreEqual(30f, DamageRules.ProjectileDamage(null, M3TestData.Ammo9()));
        }

        [Test]
        public void NullWeaponAndAmmo_IsZero()
        {
            Assert.AreEqual(0f, DamageRules.ProjectileDamage(null, null));
        }

        [Test]
        public void NullAmmo_WithWeaponDamage_UsesWeapon()
        {
            Assert.AreEqual(35f, DamageRules.ProjectileDamage(M3TestData.Pistol9(), null));
        }

        [Test]
        public void NewWeaponDefinition_DefaultsAreUnset()
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            Assert.AreEqual(0f, w.damage, "0 = 미지정");
            Assert.AreEqual(0f, w.shotNoiseRadiusM, "0 = 소음 없음");
        }
    }
}
