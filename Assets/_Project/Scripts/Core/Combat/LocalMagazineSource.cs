using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    // 탄창 목록만으로 된 IMagazineSource. 인벤토리가 없는 M1 씬과 테스트용, M3 적 예비 탄창.
    // 회수(TryStore)는 공간 제한이 없어 항상 성공한다.
    public sealed class LocalMagazineSource : IMagazineSource
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

        private static bool Fits(Magazine m, AmmoType ammo) => !m.IsEmpty && m.Definition.ammo == ammo;
    }
}
