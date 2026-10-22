using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    // 탄창 목록만으로 된 IMagazineSource. 인벤토리가 없는 M1 씬과 테스트용, M3 적 예비 탄창.
    // 회수(TryStore)는 공간 제한이 없어 항상 성공한다.
    // M7: 탄 상자(isAmmoBox)도 같은 목록에 둔다 — ILooseAmmoSource(튜브 장전)로만 꺼내고 탄창 재장전(Fits)에는 쓰지 않는다 (Q5·Q6).
    public sealed class LocalMagazineSource : IMagazineSource, ILooseAmmoSource
    {
        private readonly List<Magazine> magazines;

        public LocalMagazineSource(IEnumerable<Magazine> magazines)
        {
            this.magazines = magazines?.ToList() ?? new List<Magazine>();
        }

        public IReadOnlyList<Magazine> Magazines => magazines;

        // 적 사격 경로에서도 불리므로 람다·LINQ 없이 순회한다 (M3, 할당 없음).
        public bool HasLoadable(AmmoType ammo)
        {
            for (int i = 0; i < magazines.Count; i++)
                if (Fits(magazines[i], ammo))
                    return true;
            return false;
        }

        public Magazine TakeFullest(AmmoType ammo)
        {
            Magazine best = null;
            foreach (Magazine m in magazines)
                if (Fits(m, ammo) && (best == null || m.Rounds > best.Rounds))
                    best = m;
            if (best != null)
                magazines.Remove(best);
            return best;
        }

        public bool TryStore(Magazine magazine)
        {
            if (magazine == null || magazines.Contains(magazine))
                return false;
            magazines.Add(magazine);
            return true;
        }

        // ── ILooseAmmoSource (M7) ────────────────────────────

        public bool HasLooseRound(AmmoType ammo) => FindLeastBox(ammo) != null;

        // 탄이 가장 적은 상자부터(같으면 목록 앞쪽). 빈 상자는 목록에 남는다 (Q6).
        public bool TakeLooseRound(AmmoType ammo)
        {
            Magazine box = FindLeastBox(ammo);
            return box != null && box.TakeRound();
        }

        private Magazine FindLeastBox(AmmoType ammo)
        {
            Magazine best = null;
            for (int i = 0; i < magazines.Count; i++)
            {
                Magazine m = magazines[i];
                if (IsLooseBox(m, ammo) && (best == null || m.Rounds < best.Rounds))
                    best = m;
            }
            return best;
        }

        private static bool Fits(Magazine m, AmmoType ammo) => !m.IsEmpty && !m.Definition.isAmmoBox && m.Definition.ammo == ammo;

        private static bool IsLooseBox(Magazine m, AmmoType ammo) => !m.IsEmpty && m.Definition.isAmmoBox && m.Definition.ammo == ammo;
    }
}
