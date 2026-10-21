namespace Ashfall.AI
{
    // §7 상태 머신 + 종료 상태 Dead (M3 설계 §1.6, D9).
    public enum EnemyState
    {
        Idle,
        Patrol,
        Suspicious,
        Alert,
        Combat,
        Search,
        Dead,
    }

    // 이동 의도. 실제 속도는 EnemyArchetypeSO(patrolSpeed·moveSpeed·sprintSpeed)에서 읽는다.
    public enum EnemyMoveMode
    {
        Hold,
        Walk,
        Run,
        Sprint,
    }

    // 적 자세 (웅크림은 캡슐 높이만 바꾼다).
    public enum EnemyPosture
    {
        Stand,
        Crouch,
    }

    // §7 적 3부위. 배열 인덱스로 쓴다. 순서 고정.
    public enum EnemyHitRegion
    {
        Head,
        Body,
        Legs,
    }

    // CoverPoint 높이 (M3 설계 §1.7). Low = 웅크려 숨고 서서 사격, High = 서서 숨고 옆으로 나와 사격.
    public enum CoverHeight
    {
        Low,
        High,
    }

    // 엄폐 질의 결과. Deferred = 이번 프레임 예산 초과, 다음 틱 재시도.
    public enum CoverResult
    {
        Found,
        None,
        Deferred,
    }
}
