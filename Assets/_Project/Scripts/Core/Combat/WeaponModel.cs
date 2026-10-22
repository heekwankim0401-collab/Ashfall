using System;

namespace Ashfall.Core
{
    // 무기 하나의 상태: 발사, 재장전 (§5.1). 장착 탄창은 WeaponItem이, 예비 탄창은 IMagazineSource가 가진다.
    // TODO(GDD-?) Q21: 약실 탄(+1)은 다루지 않는다. 탄창 안의 탄만 쏜다.
    // M7:
    // - 내장 튜브 무기(WeaponDefinitionSO.internalMagazine)는 R = 한 발씩 장전(ILooseAmmoSource, roundReloadTimeSec마다 1발). 빠른 재장전 없음 (Q5).
    //   장전 중 사격 입력 → 발사가 성립하면(튜브 1발 이상·새 당김·간격) 장전 중단 후 사격(진행 중인 한 발은 꺼낸 적 없음) (Q7).
    // - 마모·고장(WeaponWear, 플레이어만): 발사가 성립하는 순간 JamRules로 굴려 고장이면 탄 소모 없이 발사 안 됨 (Q9).
    //   발사되면 내구도 −durabilityLossPerShot (Q11). 고장 중 재장전 불가, ClearJam(T 길게 — 컨트롤러)으로만 해제.
    //   옵션 없음(기존 생성자 호출) = 마모·고장 없음 — 적 무기(EnemyWeapon)는 지금과 같다 (Q18).
    public sealed class WeaponModel
    {
        private readonly IMagazineSource source;
        private readonly ILooseAmmoSource looseAmmo;
        private readonly WeaponWear wear;
        private readonly FireControl fireControl;
        private float reloadRemaining;
        private float reloadElapsed;

        public WeaponItem Item { get; }
        public Magazine Current => Item.LoadedMagazine;
        public bool IsReloading { get; private set; }
        public bool IsFastReload { get; private set; }
        public bool IsJammed => Item.IsJammed;
        // 이 무기의 R이 한 발씩 장전인가 (내장 튜브)
        public bool IsTubeReload => Item.HasInternalMagazine;
        // 진행 중(또는 마지막) 튜브 장전에서 넣은 탄 수. 탄창 무기는 0
        public int RoundsLoadedThisReload { get; private set; }
        public bool WearEnabled => wear.Enabled;

        // 무기에서 빠진 탄창이 바닥으로 가야 할 때: 빠른 재장전, 또는 일반 재장전인데 회수 공간이 없을 때.
        public event Action<Magazine> MagazineDropped;
        // 고장 난 순간 (탄 소모 없음). 컨트롤러가 WeaponClicked(Jammed)를 낸다.
        public event Action Jammed;

        // looseAmmo가 null이면 source가 ILooseAmmoSource를 구현할 때 그것을 쓴다 (LocalMagazineSource·PlayerInventory).
        public WeaponModel(WeaponItem item, IMagazineSource source, ILooseAmmoSource looseAmmo = null, WeaponWear wear = default)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.looseAmmo = looseAmmo ?? source as ILooseAmmoSource;
            this.wear = wear;
            WeaponDefinitionSO definition = item.Definition;
            fireControl = new FireControl(definition.rpm, definition.modes.Length > 0 ? definition.modes[0] : FireMode.Auto);
        }

        // 고장 중이면 false. 튜브 = 튜브에 빈 자리 + 낱탄 있음, 탄창 무기 = 넣을 탄창 있음.
        public bool CanReload
        {
            get
            {
                if (IsJammed)
                    return false;
                if (IsTubeReload)
                    return Current != null && !Current.IsFull && looseAmmo != null && looseAmmo.HasLooseRound(Item.Definition.ammo);
                return source.HasLoadable(Item.Definition.ammo);
            }
        }

        // 발사되면 true (탄 1발 소모).
        public bool TryFire(float now, bool triggerHeld) => TryFire(now, triggerHeld, true);

        // allowShot = false: 방아쇠 상태만 FireControl에 넘기고 쏘지 않는다 (무기 교체 중 — M7 리뷰 7A 낮음 5).
        public bool TryFire(float now, bool triggerHeld, bool allowShot)
        {
            // Q7: 튜브 장전 중 방아쇠 → 실제로 발사가 성립할 때(새 당김·간격·1발 이상)만 장전 중단 후 사격.
            // 발사가 안 되면(누른 채 R, 간격 안 클릭, 0발) 장전은 계속된다 (M7 리뷰 7A 중간 1).
            // 튜브 장전 중에도 방아쇠 상태는 기록한다 — 누른 채 시작한 장전이 첫 발 뒤 "새 당김"으로 쏘지 않게.
            bool tubeReloading = IsReloading && IsTubeReload;
            bool tracksTrigger = !IsJammed && Current != null && (!IsReloading || tubeReloading);
            bool canFire = allowShot && (!tubeReloading || (Current != null && !Current.IsEmpty));
            // 탄창 재장전 중·고장 중에는 기존대로 방아쇠를 넘기지 않는다.
            bool wantsShot = tracksTrigger
                ? fireControl.Tick(now, triggerHeld, canFire)
                : fireControl.Tick(now, false);
            if (!wantsShot || Current.IsEmpty)
                return false;
            if (tubeReloading)
                CancelReload();

            if (wear.Enabled && JamRules.Roll(JamRules.Chance(Item.Durability, wear.Threshold, wear.ChancePerPoint), wear.Random))
            {
                // Q9: 탄을 쓰지 않고 발사 없음
                Item.IsJammed = true;
                Jammed?.Invoke();
                return false;
            }

            Current.TakeRound();
            if (wear.Enabled)
                Item.Durability -= Item.Definition.durabilityLossPerShot;
            return true;
        }

        // R: 일반 재장전. 일반 재장전 중 R을 한 번 더 누르면 빠른 재장전(탄창 버림)으로 바뀐다.
        // TODO(GDD-?): §12.1에 빠른/일반 재장전 키 구분이 없어 "재장전 중 R 한 번 더"로 정했다.
        // 튜브 무기: 한 발씩 장전 시작. 빠른 재장전 없음 — 장전 중 R은 무시 (Q5). 고장 중에는 거부 (Q9).
        public bool PressReload()
        {
            if (IsReloading)
            {
                if (IsFastReload || IsTubeReload)
                    return false;
                IsFastReload = true;
                reloadRemaining = Math.Max(0f, Item.Definition.fastReloadTimeSec - reloadElapsed);
                return true;
            }

            if (!CanReload)
                return false;

            IsReloading = true;
            IsFastReload = false;
            reloadElapsed = 0f;
            if (IsTubeReload)
            {
                RoundsLoadedThisReload = 0;
                reloadRemaining = Item.Definition.roundReloadTimeSec;
            }
            else
            {
                reloadRemaining = Item.Definition.reloadTimeSec;
            }
            return true;
        }

        // 진행 중인 재장전을 버린다 (무기를 슬롯에서 뺄 때). 탄창은 그대로. 튜브는 이미 넣은 탄을 유지한다.
        public void CancelReload()
        {
            IsReloading = false;
            IsFastReload = false;
        }

        // 고장 해제 (T 길게 — 진행 시간은 컨트롤러가 잰다). 고장이 아니었으면 false.
        public bool ClearJam()
        {
            if (!Item.IsJammed)
                return false;
            Item.IsJammed = false;
            return true;
        }

        public void Tick(float dt)
        {
            if (!IsReloading)
                return;

            reloadElapsed += dt;
            reloadRemaining -= dt;
            if (IsTubeReload)
            {
                TickTube();
                return;
            }
            if (reloadRemaining <= 0f)
                CompleteReload();
        }

        // 한 발 시간이 지날 때마다 1발. 가득 차거나 낱탄이 없으면 끝. 큰 dt면 여러 발.
        private void TickTube()
        {
            float step = Item.Definition.roundReloadTimeSec;
            AmmoType ammo = Item.Definition.ammo;
            while (IsReloading && reloadRemaining <= 0f)
            {
                Magazine tube = Current;
                if (tube == null || tube.IsFull || looseAmmo == null || !looseAmmo.TakeLooseRound(ammo))
                {
                    CancelReload();
                    return;
                }
                tube.TryAddRound();
                RoundsLoadedThisReload++;
                if (tube.IsFull || !looseAmmo.HasLooseRound(ammo))
                {
                    CancelReload();
                    return;
                }
                reloadRemaining += step;
            }
        }

        private void CompleteReload()
        {
            bool fast = IsFastReload;
            IsReloading = false;
            IsFastReload = false;

            // 탄이 가장 많이 남은 탄창을 저장 공간에서 꺼낸다. 그사이 없어졌으면 아무것도 안 바뀐다.
            Magazine next = source.TakeFullest(Item.Definition.ammo);
            if (next == null)
                return;

            Magazine old = Item.LoadedMagazine;
            Item.LoadedMagazine = next;

            if (old == null)
                return;
            // 꺼낸 다음 회수하므로 꺼낸 자리를 회수에 쓸 수 있다.
            if (fast || !source.TryStore(old))
                MagazineDropped?.Invoke(old);
        }
    }
}
