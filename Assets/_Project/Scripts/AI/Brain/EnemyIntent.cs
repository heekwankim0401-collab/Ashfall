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

        // M7 (설계 §1.4). EnemyAgent.ApplyMelee·ApplyTelegraph가 읽는다.
        public bool Melee;       // 근접 공격 의사 (AI-MUT). 실제 타격 = EnemyMelee(사거리·준비·간격) + 시야
        public bool Telegraph;   // 저격 섬광 켜기 (AI-SNP). 표적이 안 보이면 EnemyBrain이 끈다
    }
}
