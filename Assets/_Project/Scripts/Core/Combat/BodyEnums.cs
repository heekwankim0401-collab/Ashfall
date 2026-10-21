namespace Ashfall.Core
{
    // §4.2 부위 7개 (SYS-HP-01). 배열 인덱스로 쓰므로 순서를 바꾸지 않는다.
    public enum BodyPart
    {
        Head,
        Thorax,
        Stomach,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg,
    }

    // §4.3 상태이상. STAT-RAD/DEHY/HUNG은 M7에 추가한다.
    public enum StatusEffect
    {
        Bleed,
        HeavyBleed,
        Fracture,
    }

    // RULE-05: 수치 대신 보여 주는 부위 상태 등급
    public enum HealthTier
    {
        Healthy,
        Injured,
        Critical,
        Destroyed,
    }

    // AI 시야 배율(§7)용 플레이어 자세. Player의 Stance를 Core로 옮기지 않고 이것으로 옮겨 말한다 (M3).
    public enum TargetPosture
    {
        Standing,
        Crouching,
        Prone,
    }
}
