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

        // T9 유예: 측면 우회 중에는 시야를 잃어도 Search로 가지 않는다 (RDR)
        bool DefersLoseSight { get; }
        // 숨은 상태라 EnemySenses.HiddenFromTarget을 계산해야 하는가 (RDR Hide)
        bool WantsHiddenCheck { get; }
    }
}
