using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.AI
{
    // 적 무장 하나 (M3 설계 §1.4). 플레이어와 같은 Core 규칙(WeaponModel·FireControl·LocalMagazineSource)으로
    // 발사 간격·탄 소모·재장전을 하고, 실제 WeaponItem·Magazine을 들고 있어 시체에 남은 탄 수가 그대로 남는다.
    // - 자동(modes[0] = Auto): burstRounds 발 점사 → burstPauseSec 쉼
    // - 반자동: semiShotIntervalSec 간격으로 방아쇠를 한 프레임씩 당겼다 놓음 (FireControl 반자동 규칙 통과)
    // - 장착 탄창 0발이면 일반 재장전(빠른 재장전 안 씀 → 탄창이 바닥에 떨어지지 않는다)
    // - 탄이 다 떨어지면 사격 의도를 무시한다. TODO(GDD-?): 탄 없는 적 행동
    // Tick은 할당 없음.
    public sealed class EnemyWeapon
    {
        private readonly EnemyArchetypeSO archetype;
        private readonly System.Random random;
        private readonly FireMode mode;

        private int burstTarget;
        private float nextBurstTime = float.NegativeInfinity;
        private float nextSemiTime = float.NegativeInfinity;
        private bool semiHeld;

        public EnemyWeapon(EnemyLoadout loadout, int spareMagazines, EnemyArchetypeSO archetype,
                           IProjectileService projectiles, System.Random random)
        {
            if (!IsValidLoadout(loadout, out string problem))
                throw new ArgumentException("EnemyLoadout이 잘못됐다: " + problem, nameof(loadout));
            this.archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
            Projectiles = projectiles;
            this.random = random ?? new System.Random();

            Loadout = loadout;
            Item = new WeaponItem(loadout.weapon, Magazine.Full(loadout.magazine));
            var spares = new List<Magazine>(Math.Max(0, spareMagazines) + 1);
            for (int i = 0; i < spareMagazines; i++)
                spares.Add(Magazine.Full(loadout.magazine));
            Spares = new LocalMagazineSource(spares);
            Model = new WeaponModel(Item, Spares);
            Damage = DamageRules.ProjectileDamage(loadout.weapon, loadout.ammo);
            mode = loadout.weapon.modes != null && loadout.weapon.modes.Length > 0 ? loadout.weapon.modes[0] : FireMode.Auto;
        }

        // 발사 서비스. 없으면(null) 쏘지 않고 탄도 쓰지 않는다. EnemyAgent가 매 프레임 director의 현재 값으로 맞춘다
        // (서비스 해제 뒤 낡은 참조로 쏘지 않게, 리뷰 9).
        public IProjectileService Projectiles { get; set; }

        public EnemyLoadout Loadout { get; }
        public WeaponItem Item { get; }
        public WeaponModel Model { get; }
        public LocalMagazineSource Spares { get; }
        public float Damage { get; }
        public FireMode Mode => mode;
        public int ShotsInBurst { get; private set; }

        public bool HasLoadedRounds => Item.LoadedMagazine != null && !Item.LoadedMagazine.IsEmpty;
        public bool HasAmmo => HasLoadedRounds || Spares.HasLoadable(Item.Definition.ammo);

        // 장착 탄창 탄 비율 (0~1). 탄창 없으면 0. RDR 전술 재장전 판단용.
        public float AmmoRatio
        {
            get
            {
                Magazine m = Item.LoadedMagazine;
                return m == null || m.Capacity <= 0 ? 0f : (float)m.Rounds / m.Capacity;
            }
        }

        // 매 프레임. wantsFire이면 점사/반자동 패턴으로 방아쇠. 이번 프레임 쏜 발 수.
        // errorRadiusM은 호출 측이 AimError.Radius로 계산(점사 순번은 ShotsInBurst).
        public int Tick(float now, float dt, bool wantsFire, Vector3 muzzle, Vector3 aimPoint, float errorRadiusM,
                        Transform shooterRoot)
        {
            Model.Tick(dt);

            if (!HasLoadedRounds && !Model.IsReloading)
                TryReload();

            if (!wantsFire || Projectiles == null || Model.IsReloading || !HasLoadedRounds)
            {
                EndBurst(now, false);
                semiHeld = false;
                Model.TryFire(now, false);   // 방아쇠 놓음을 FireControl에 알린다 (반자동 재발사 조건)
                return 0;
            }

            bool trigger;
            if (mode == FireMode.Semi)
            {
                // 한 프레임만 당긴다. RPM 간격 때문에 안 나갔으면 다음 프레임에 놓았다가 다시 당긴다.
                trigger = now >= nextSemiTime && !semiHeld;
                semiHeld = trigger;
            }
            else
            {
                trigger = now >= nextBurstTime;
                if (trigger && burstTarget <= 0)
                    burstTarget = RandomInclusive(archetype.burstRounds.x, archetype.burstRounds.y);
            }

            if (!Model.TryFire(now, trigger))
                return 0;

            Vector3 direction = AimError.Direction(muzzle, aimPoint, errorRadiusM, random);
            Projectiles.Fire(muzzle, direction, Loadout.ammo, Damage, shooterRoot);

            if (mode == FireMode.Semi)
            {
                nextSemiTime = now + RandomRange(archetype.semiShotIntervalSec);
            }
            else
            {
                ShotsInBurst++;
                if (ShotsInBurst >= burstTarget)
                    EndBurst(now, true);
            }
            return 1;
        }

        // 일반 재장전 시작 (재장전 중·예비 없음·탄창 가득이면 false)
        public bool TryReload()
        {
            if (Model.IsReloading || !Model.CanReload)
                return false;
            Magazine loaded = Item.LoadedMagazine;
            if (loaded != null && loaded.Rounds >= loaded.Capacity)
                return false;
            EndBurst(float.NegativeInfinity, false);
            return Model.PressReload();
        }

        public void CancelReload() => Model.CancelReload();

        // [무기(장착 탄창 포함)] + [예비 탄창(빈 탄창 포함, 실제 탄 수)] (CancelReload 후 호출)
        public void CollectLoot(List<ItemInstance> into)
        {
            if (into == null)
                return;
            into.Add(Item);
            IReadOnlyList<Magazine> spares = Spares.Magazines;
            for (int i = 0; i < spares.Count; i++)
                into.Add(spares[i]);
        }

        // 무장 정의 검사 (리뷰 7): 무기·탄·탄창이 모두 있고 탄 종류가 무기와 맞아야 한다.
        // 탄(ammo)이 비면 ProjectileSystem.Fire에서 NullReferenceException이 났다.
        public static bool IsValidLoadout(in EnemyLoadout loadout, out string problem)
        {
            if (loadout.weapon == null)
                problem = "무기(weapon) 없음";
            else if (loadout.ammo == null)
                problem = "탄(ammo) 없음";
            else if (loadout.magazine == null)
                problem = "탄창(magazine) 없음";
            else if (loadout.ammo.type != loadout.weapon.ammo)
                problem = $"탄 종류 {loadout.ammo.type} ≠ 무기 {loadout.weapon.ammo}";
            else if (loadout.magazine.ammo != loadout.weapon.ammo)
                problem = $"탄창 탄 종류 {loadout.magazine.ammo} ≠ 무기 {loadout.weapon.ammo}";
            else
                problem = null;
            return problem == null;
        }

        // 스폰 시 무장 고르기: forcedIndex가 범위 안이면 그것, 아니면 가중치 비례. 후보가 없으면 -1.
        public static int PickLoadout(IReadOnlyList<EnemyLoadout> loadouts, int forcedIndex, System.Random random)
        {
            if (loadouts == null || loadouts.Count == 0)
                return -1;
            if (forcedIndex >= 0 && forcedIndex < loadouts.Count)
                return forcedIndex;

            float total = 0f;
            for (int i = 0; i < loadouts.Count; i++)
                if (loadouts[i].weapon != null && loadouts[i].weight > 0f)
                    total += loadouts[i].weight;
            if (total <= 0f)
                return 0;

            float roll = (float)((random ?? new System.Random()).NextDouble() * total);
            int last = 0;
            for (int i = 0; i < loadouts.Count; i++)
            {
                if (loadouts[i].weapon == null || loadouts[i].weight <= 0f)
                    continue;
                last = i;
                if (roll < loadouts[i].weight)
                    return i;
                roll -= loadouts[i].weight;
            }
            return last;
        }

        private void EndBurst(float now, bool startPause)
        {
            if (startPause)
                nextBurstTime = now + RandomRange(archetype.burstPauseSec);
            ShotsInBurst = 0;
            burstTarget = 0;
        }

        private int RandomInclusive(int min, int max)
        {
            if (min < 1)
                min = 1;
            if (max < min)
                max = min;
            return random.Next(min, max + 1);
        }

        private float RandomRange(Vector2 range)
        {
            float min = Mathf.Max(0f, range.x);
            float max = Mathf.Max(min, range.y);
            return min + (float)random.NextDouble() * (max - min);
        }
    }
}
