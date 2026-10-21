namespace Ashfall.Core
{
    // 부위 상태 읽기 전용 뷰 (RULE-05 H 화면). Player가 구현해 등록한다.
    // RULE-05를 구조로 강제한다: HP 수치를 내주지 않고 등급만 준다. 수치 멤버를 추가하지 않는다.
    public interface IBodyStatusView
    {
        bool IsDead { get; }
        HealthTier GetTier(BodyPart part);
        bool HasStatus(BodyPart part, StatusEffect effect);
    }
}
