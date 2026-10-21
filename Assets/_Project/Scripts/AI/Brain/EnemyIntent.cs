using UnityEngine;

namespace Ashfall.AI
{
    // EnemyBrain의 결정 결과 (M3 설계 §5.1). EnemyAgent가 매 프레임 적용한다.
    public struct EnemyIntent
    {
        public EnemyMoveMode Move;
        public bool HasDestination; public Vector3 Destination;
        public bool LookAtPoint; public Vector3 LookPoint;   // false면 이동 방향
        public EnemyPosture Posture;
        public bool Fire; public Vector3 FireAt;             // FireAt = 표적 중심(오차는 EnemyWeapon이 더함)
        public bool Reload;
    }
}
