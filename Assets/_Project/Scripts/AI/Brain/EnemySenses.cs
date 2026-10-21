using UnityEngine;

namespace Ashfall.AI
{
    // 감각 갱신 1회의 입력 (M3 설계 §5.1, §6). EnemyAgent가 채워 EnemyBrain.Tick에 넘긴다.
    public struct EnemySenses
    {
        public float Now;
        public Vector3 SelfPosition, SelfForward;
        public bool Arrived;                 // NavMeshAgent: 경로 없음 ∨ (pathPending 아님 ∧ remainingDistance ≤ stoppingDistance)
        public bool TargetExists;            // IPlayerTarget 있음 ∧ !IsDead
        public bool TargetVisible;           // 이번 감각 갱신에서 보임
        public Vector3 TargetPosition, TargetEyePosition, TargetCenter, TargetVelocity;   // 보일 때만 갱신
        public float TargetDistance, EffectiveSightRange;
        public bool HiddenFromTarget;        // RDR 엄폐 무효화 판정용(엄폐 중일 때만 계산, 아니면 true)
    }
}
