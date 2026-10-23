namespace Ashfall.Core
{
    // 부위 상태 읽기 전용 뷰 (RULE-05 H 화면). Player가 구현해 등록한다.
    // RULE-05를 구조로 강제한다: HP 수치를 내주지 않고 등급만 준다. 수치 멤버를 추가하지 않는다.
    public interface IBodyStatusView
    {
        bool IsDead { get; }
        HealthTier GetTier(BodyPart part);
        bool HasStatus(BodyPart part, StatusEffect effect);

        // M7 §4.3 전신 생존 상태 (수분·에너지·방사능) — 등급만. 수치(수분·에너지·선량)는 Player 내부에만 둔다 (M7 설계 §1.2, Q43).
        // 등급이 바뀌면 기존 PlayerBodyStatusChanged가 나간다(새 이벤트 없음).
        SurvivalTier GetSurvivalTier(SurvivalStat stat);
    }
}
