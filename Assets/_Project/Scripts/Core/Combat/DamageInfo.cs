using UnityEngine;

namespace Ashfall.Core
{
    // 피격 한 번의 정보. 기능 어셈블리끼리 직접 참조하지 않도록 Core에 둔다 (§13.3).
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly int Penetration;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;
        public readonly float DistanceM;

        public DamageInfo(float amount, int penetration, Vector3 point, Vector3 direction, float distanceM)
        {
            Amount = amount;
            Penetration = penetration;
            Point = point;
            Direction = direction;
            DistanceM = distanceM;
        }
    }

    // 피해를 받을 수 있는 대상. 부위 판정(§4.2)은 M2에서 이 위에 얹는다.
    public interface IDamageable
    {
        void ApplyDamage(in DamageInfo info);
    }

    // 무기 반동을 시선에 더해 주는 쪽(플레이어 시점). Weapons가 Player를 직접 참조하지 않게 한다.
    public interface IRecoilReceiver
    {
        void AddRecoil(float pitchDeg, float yawDeg);
    }
}
