namespace Ashfall.AI
{
    // 유형별 Suspicious/Alert/Combat 행동 (M3 설계 §1.6.2). EnemyBrain이 상태 전이를 정하고 이것이 의도를 채운다.
    internal interface IEnemyTactics
    {
        void Reset();                                                    // 상태 진입 시
        void TickSuspicious(TacticsContext c, in EnemySenses s, ref EnemyIntent i);
        void TickAlert(TacticsContext c, in EnemySenses s, ref EnemyIntent i);
        void TickCombat(TacticsContext c, in EnemySenses s, ref EnemyIntent i);
        void ReleaseReservations(TacticsContext c);                      // 엄폐·우회권 반납

        // M7 (설계 §1.4): Idle·Patrol·Search 의도를 전술이 대신 채울 수 있다(저격수 퍼치 훑기 — 이동 없음).
        // state = 지금 상태(Idle·Patrol·Search 중 하나). true = 채웠음(EnemyBrain 기본 행동을 건너뜀), false = 기본 행동.
        // 상태 전이(T1·T10 등)는 그대로 EnemyBrain이 정한다. SCV·RDR은 항상 false.
        bool TryTickRelaxed(EnemyState state, TacticsContext c, in EnemySenses s, ref EnemyIntent i);

        // T9 유예: 측면 우회 중에는 시야를 잃어도 Search로 가지 않는다 (RDR)
        bool DefersLoseSight { get; }
        // 숨은 상태라 EnemySenses.HiddenFromTarget을 계산해야 하는가 (RDR Hide)
        bool WantsHiddenCheck { get; }
    }
}
