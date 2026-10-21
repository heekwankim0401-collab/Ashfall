using System;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;

namespace Ashfall.Weapons
{
    // 무기 하나의 상태: 장착 탄창, 예비 탄창, 발사, 재장전 (§5.1).
    // TODO(GDD-?): 약실 탄(+1)은 다루지 않는다. 탄창 안의 탄만 쏜다.
    public sealed class WeaponModel
    {
        private readonly WeaponDefinitionSO definition;
        private readonly FireControl fireControl;
        private readonly List<Magazine> spares;
        private float reloadRemaining;
        private float reloadElapsed;

        public Magazine Current { get; private set; }
        public IReadOnlyList<Magazine> Spares => spares;
        public bool IsReloading { get; private set; }
        public bool IsFastReload { get; private set; }

        // 빠른 재장전으로 버려진 탄창. TODO(M2): 바닥에 줍기 가능한 아이템으로 떨어뜨린다.
        public event Action<Magazine> MagazineDropped;

        public WeaponModel(WeaponDefinitionSO definition, Magazine current, IEnumerable<Magazine> spareMagazines)
        {
            this.definition = definition;
            Current = current;
            spares = spareMagazines.ToList();
            fireControl = new FireControl(definition.rpm, definition.modes.Length > 0 ? definition.modes[0] : FireMode.Auto);
        }

        public bool CanReload => spares.Any(m => !m.IsEmpty);

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
                reloadRemaining = Math.Max(0f, definition.fastReloadTimeSec - reloadElapsed);
                return true;
            }

            if (!CanReload)
                return false;

            IsReloading = true;
            IsFastReload = false;
            reloadElapsed = 0f;
            reloadRemaining = definition.reloadTimeSec;
            return true;
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
            IsReloading = false;

            // 탄이 가장 많이 남은 예비 탄창을 꽂는다.
            Magazine next = spares.Where(m => !m.IsEmpty).OrderByDescending(m => m.Rounds).FirstOrDefault();
            if (next == null)
                return;
            spares.Remove(next);

            if (Current != null)
            {
                if (IsFastReload)
                    MagazineDropped?.Invoke(Current);
                else
                    spares.Add(Current);
            }
            Current = next;
            IsFastReload = false;
        }
    }
}
