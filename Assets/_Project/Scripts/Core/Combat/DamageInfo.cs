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
        // 부위 힌트 (M7 7B 리뷰 1, Q24): 지점이 아니라 맞을 부위를 직접 정하는 피해(변이체 근접). 받는 쪽이 플레이어면
        // 지점 판정 대신 이 부위를 쓴다(출혈·골절 굴림 등 나머지 규칙은 같다). 플레이어 부위가 없는 대상(적·과녁)은 무시한다.
        // HasPartHint = false면 PartHint는 의미 없다(기존 생성자 = 기존 동작). Nullable 대신 bool + enum(IL2CPP·할당 0).
        public readonly bool HasPartHint;
        public readonly BodyPart PartHint;

        public DamageInfo(float amount, int penetration, Vector3 point, Vector3 direction, float distanceM)
        {
            Amount = amount;
            Penetration = penetration;
            Point = point;
            Direction = direction;
            DistanceM = distanceM;
            HasPartHint = false;
            PartHint = BodyPart.Thorax;
        }

        public DamageInfo(float amount, int penetration, Vector3 point, Vector3 direction, float distanceM, BodyPart partHint)
        {
            Amount = amount;
            Penetration = penetration;
            Point = point;
            Direction = direction;
            DistanceM = distanceM;
            HasPartHint = true;
            PartHint = partHint;
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
