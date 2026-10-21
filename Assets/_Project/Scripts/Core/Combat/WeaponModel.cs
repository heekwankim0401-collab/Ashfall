using System;

namespace Ashfall.Core
{
    // 무기 하나의 상태: 발사, 재장전 (§5.1). 장착 탄창은 WeaponItem이, 예비 탄창은 IMagazineSource가 가진다.
    // TODO(GDD-?): 약실 탄(+1)은 다루지 않는다. 탄창 안의 탄만 쏜다.
    public sealed class WeaponModel
    {
        private readonly IMagazineSource source;
        private readonly FireControl fireControl;
        private float reloadRemaining;
        private float reloadElapsed;

        public WeaponItem Item { get; }
        public Magazine Current => Item.LoadedMagazine;
        public bool IsReloading { get; private set; }
        public bool IsFastReload { get; private set; }

        // 무기에서 빠진 탄창이 바닥으로 가야 할 때: 빠른 재장전, 또는 일반 재장전인데 회수 공간이 없을 때.
        public event Action<Magazine> MagazineDropped;

        public WeaponModel(WeaponItem item, IMagazineSource source)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            WeaponDefinitionSO definition = item.Definition;
            fireControl = new FireControl(definition.rpm, definition.modes.Length > 0 ? definition.modes[0] : FireMode.Auto);
        }

        public bool CanReload => source.HasLoadable(Item.Definition.ammo);

        // 발사되면 true (탄 1발 소모).
        public bool TryFire(float now, bool triggerHeld)
        {
            bool wantsShot = fireControl.Tick(now, triggerHeld && !IsReloading && Current != null);
            return wantsShot && Current.TakeRound();
        }

        // R: 일반 재장전. 일반 재장전 중 R을 한 번 더 누르면 빠른 재장전(탄창 버림)으로 바뀐다.
        // TODO(GDD-?): §12.1에 빠른/일반 재장전 키 구분이 없어 "재장전 중 R 한 번 더"로 정했다.
        public bool PressReload()
        {
            if (IsReloading)
            {
                if (IsFastReload)
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
            reloadRemaining = Item.Definition.reloadTimeSec;
            return true;
        }

        // 진행 중인 재장전을 버린다 (무기를 슬롯에서 뺄 때). 탄창은 그대로.
        public void CancelReload()
        {
            IsReloading = false;
            IsFastReload = false;
        }

        public void Tick(float dt)
        {
            if (!IsReloading)
                return;

            reloadElapsed += dt;
            reloadRemaining -= dt;
            if (reloadRemaining <= 0f)
                CompleteReload();
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
