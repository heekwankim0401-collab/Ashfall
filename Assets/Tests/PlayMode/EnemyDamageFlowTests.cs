#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M3 AC4 — 적도 플레이어와 같은 피해 공식 (§7 "동일한 피해 공식·부위 체력, 3부위", §5.2 무기별 피해, M3 설계 §1.2·§1.3).
    // - 플레이어 소총(WPN-RIF-01 45) 탄 → 적 머리 35 / 몸통 80 / 다리 65, 파괴 부위 70% → 몸통
    // - 적 9mm 탄(실제 ProjectileSystem) → 플레이어 7부위: 권총 35, SMG 30
    public class EnemyDamageFlowTests
    {
        private const float RifleDamage = 45f;
        private const float ShotDistanceM = 5f;

        private M3Scene scene;
        private SimInput input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M3Scene();
            yield return M3Scene.Load(scene);
        }

        [TearDown]
        public void TearDown()
        {
            input?.Dispose();
            input = null;
        }

        // 플레이어가 LMB로 쏜 소총 탄이 적 몸통에 맞으면 45 (무기 damage, DamageRules)
        [UnityTest]
        public IEnumerator PlayerRifle_Lmb_BodyHit_Deals45ToEnemyBody()
        {
            yield return scene.KeepOnly("A1");
            EnemyAgent scv = scene.Enemy("A1");
            scene.Director.enabled = false;   // 적 반응(이동·반격) 없이 피해만 본다
            M3Scene.WarpEnemy(scv, new Vector3(M3Scene.OpenLaneX, 0f, 10f), 0f);
            scene.TeleportPlayer(new Vector3(M3Scene.OpenLaneX, 0.05f, 10f - ShotDistanceM), scv.transform.position);
            input = new SimInput();

            Assert.AreEqual(RifleDamage, DamageRules.ProjectileDamage(RifleDefinition(), RifleAmmo()), 1e-4f);
            yield return ShootWithMouse(scv.transform.position + Vector3.up * 1.1f);   // 비율 0.61 → 몸통

            Assert.AreEqual(80f - RifleDamage, scv.Body.GetHp(EnemyHitRegion.Body), 1e-3f, "몸통 80 - 45");
            Assert.AreEqual(35f, scv.Body.GetHp(EnemyHitRegion.Head), 1e-3f);
            Assert.AreEqual(65f, scv.Body.GetHp(EnemyHitRegion.Legs), 1e-3f);
            Assert.IsFalse(scv.IsDead);
        }

        // 3부위 규칙 (소총 탄, 실제 ProjectileSystem 경로): 다리 45 → 20, 다리 45 → 0(초과분 버림, 이동 ×0.6·질주 불가),
        // 파괴된 다리 45 → 70% = 31.5가 몸통으로(80 → 48.5), 몸통 45 → 3.5, 몸통 45 → 0 사망(원인 몸통). 머리 1발 즉사는 CorpseLootFlowTests.
        [UnityTest]
        public IEnumerator RifleProjectiles_ThreeRegionRule_LegsDestroyedRedistributeThenBodyKill()
        {
            yield return scene.KeepOnly("A1");
            EnemyAgent scv = scene.Enemy("A1");
            scene.Director.enabled = false;
            M3Scene.WarpEnemy(scv, new Vector3(M3Scene.OpenLaneX, 0f, 10f), 0f);
            yield return null;
            using var killed = new EventLog<EnemyKilled>(scene.Events);

            yield return ShootProjectile(scv, 0.4f);   // 비율 0.22 → 다리
            AssertHp(scv, 35f, 80f, 20f);
            Assert.AreEqual(1f, scv.Body.MoveSpeedMultiplier, 1e-4f);

            yield return ShootProjectile(scv, 0.4f);
            AssertHp(scv, 35f, 80f, 0f);
            Assert.IsTrue(scv.Body.IsDestroyed(EnemyHitRegion.Legs));
            Assert.AreEqual(0.6f, scv.Body.MoveSpeedMultiplier, 1e-4f, "다리 파괴 → 이동 ×0.6");
            Assert.IsFalse(scv.Body.SprintAllowed, "다리 파괴 → 질주 불가");
            Assert.IsFalse(scv.IsDead);

            yield return ShootProjectile(scv, 0.4f);
            AssertHp(scv, 35f, 80f - RifleDamage * 0.7f, 0f);

            yield return ShootProjectile(scv, 1.1f);   // 몸통 48.5 - 45
            AssertHp(scv, 35f, 80f - RifleDamage * 0.7f - RifleDamage, 0f);
            Assert.IsFalse(scv.IsDead);

            yield return ShootProjectile(scv, 1.1f);   // 몸통 3.5 - 45 → 0 (초과분 버림)
            Assert.IsTrue(scv.IsDead, "몸통 0 → 사망");
            Assert.AreEqual(0f, scv.Body.GetHp(EnemyHitRegion.Body), 1e-4f);
            Assert.AreEqual(EnemyHitRegion.Body, scv.Body.DeathCause);
            Assert.AreEqual(1, killed.Count, "EnemyKilled 1회");
        }

        // 적 권총(WPN-PST-01) 탄이 플레이어에게 35 (실제 AI 사격 + ProjectileSystem + PlayerHealth 7부위)
        [UnityTest]
        public IEnumerator ScvPistol_ShotHitsPlayer_Deals35()
        {
            yield return EnemyShotDamagesPlayer("A1", "WPN-PST-01", 35f);
        }

        // 적 SMG(WPN-SMG-01) 탄이 플레이어에게 30
        [UnityTest]
        public IEnumerator ScvSmg_ShotHitsPlayer_Deals30()
        {
            yield return EnemyShotDamagesPlayer("A3", "WPN-SMG-01", 30f);
        }

        private IEnumerator EnemyShotDamagesPlayer(string id, string weaponId, float expected)
        {
            yield return scene.KeepOnly(id);
            EnemyAgent scv = scene.Enemy(id);
            Assert.AreEqual(weaponId, scv.Weapon.Item.Definition.id, "씬 무장");
            Assert.AreEqual(expected, scv.Weapon.Damage, 1e-4f, "EnemyWeapon.Damage = 무기 damage");
            M3Scene.WarpEnemy(scv, new Vector3(M3Scene.OpenLaneX, 0f, 10f), 180f);
            scene.TeleportPlayer(new Vector3(M3Scene.OpenLaneX, 0.05f, 0f), scv.transform.position);
            // 총성으로 확실히 깨운다 (분대 리더가 아니어도 바로 경계)
            scene.Events.Publish(new NoiseEmitted(scene.Motor.transform.position, 50f, NoiseKind.Gunshot));
            int startRounds = scv.Weapon.Item.LoadedMagazine.Rounds;

            // 물리 스텝마다 HP 합을 본다. 첫 감소(= 탄 1발, 출혈 틱은 Update에서 그 뒤에 시작)가 무기 피해와 같아야 한다.
            float before = scene.PlayerTotalHp();
            float deadline = Time.realtimeSinceStartup + 25f;
            float drop = 0f;
            bool fired = false;
            while (drop <= 0f)
            {
                fired |= (scv.Weapon.Item.LoadedMagazine?.Rounds ?? 0) < startRounds;
                Assert.Less(Time.realtimeSinceStartup, deadline,
                    $"25초 안에 명중 없음 (상태 {scv.State}, 남은 탄 {scv.Weapon.Item.LoadedMagazine.Rounds}/{startRounds})");
                yield return new WaitForFixedUpdate();
                float now = scene.PlayerTotalHp();
                drop = before - now;
                before = now;
            }
            fired |= (scv.Weapon.Item.LoadedMagazine?.Rounds ?? 0) < startRounds;
            Debug.Log($"[EnemyDamageFlowTests] {weaponId}: 첫 명중 피해 {drop}");
            Assert.AreEqual(expected, drop, 1e-3f, $"{weaponId} 탄 1발 피해");
            Assert.IsTrue(fired, "적이 자기 탄창에서 쐈다");
        }

        // ── 도우미 ──

        // 플레이어 LMB 한 번(조준 RMB 유지로 퍼짐 0). point를 총구에서 겨눈다.
        private IEnumerator ShootWithMouse(Vector3 point)
        {
            input.Mouse.SetRight(true);
            yield return M2Scene.WaitSeconds(0.4f);   // adsTime 0.25
            yield return scene.AimAt(point, fromMuzzle: true);
            using var shots = new EventLog<NoiseEmitted>(scene.Events);
            yield return input.Mouse.ClickLeft();
            input.Mouse.SetRight(false);
            Assert.AreEqual(1, shots.Received.Count(n => n.Kind == NoiseKind.Gunshot), "LMB 한 번 = 1발");
            yield return M3Scene.WaitUntil(() => scene.Projectiles.LiveCount == 0, 2f, () => "LMB 탄이 아무것도 맞히지 못했다");
        }

        // 적 정면 ShotDistanceM에서 높이 heightM(발 기준)로 수평 사격. 피해 = DamageRules(WPN-RIF-01, AMMO-556) — WeaponController와 같은 값.
        private IEnumerator ShootProjectile(EnemyAgent enemy, float heightM)
        {
            Vector3 target = enemy.transform.position + Vector3.up * heightM;
            Vector3 origin = target + enemy.transform.forward * ShotDistanceM;
            float damage = DamageRules.ProjectileDamage(RifleDefinition(), RifleAmmo());
            scene.Projectiles.Fire(origin, -enemy.transform.forward, RifleAmmo(), damage, null);
            yield return M3Scene.WaitUntil(() => scene.Projectiles.LiveCount == 0, 2f,
                () => $"탄이 {enemy.name}(위치 {enemy.transform.position})를 맞히지 못했다: {origin} → {target}");
        }

        private WeaponDefinitionSO RifleDefinition() => scene.Weapon.Model.Item.Definition;

        private AmmoDefinitionSO RifleAmmo()
        {
            var field = typeof(Ashfall.Weapons.WeaponController).GetField("ammo",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var ammo = (AmmoDefinitionSO)field.GetValue(scene.Weapon);
            Assert.IsNotNull(ammo);
            Assert.AreEqual(RifleDamage, ammo.damage, 1e-4f, "AMMO-556 기본 피해 45 유지");
            return ammo;
        }

        private static void AssertHp(EnemyAgent e, float head, float body, float legs)
        {
            Assert.AreEqual(head, e.Body.GetHp(EnemyHitRegion.Head), 1e-3f, "머리");
            Assert.AreEqual(body, e.Body.GetHp(EnemyHitRegion.Body), 1e-3f, "몸통");
            Assert.AreEqual(legs, e.Body.GetHp(EnemyHitRegion.Legs), 1e-3f, "다리");
        }
    }
}
#endif
