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

    // §4.3 부위 상태이상. 전신 상태(STAT-RAD/DEHY/HUNG)는 여기 넣지 않고 SurvivalStat으로 따로 둔다 (M7 설계 §1.5 —
    // 부위 상태 화면이 StatusEffect를 부위마다 순회하므로 여기 넣으면 7줄 모두에 붙는다).
    public enum StatusEffect
    {
        Bleed,
        HeavyBleed,
        Fracture,
    }

    // MVP 마무리 F1 전신 사인 (MF-Q1~Q4): 사망을 낸 피해 종류. 부위 사인(BodyPart)과 함께 전달한다.
    // Wound = 피격(결과 "사망 — 흉부/머리"), Bleeding = 출혈·중출혈 틱(파괴 부위 분산 포함, "사망 — 출혈"),
    // Dehydration = 수분 0 전신 피해("사망 — 탈수"). 방사능은 HP를 0으로 만들지 않아 종류가 없다 (TODO(GDD-?) MF-Q3).
    public enum DeathCauseKind
    {
        Wound,
        Bleeding,
        Dehydration,
    }

    // M7 §4.3 전신 생존 수치 종류 (STAT-DEHY 수분, STAT-HUNG 에너지, STAT-RAD 방사능 선량). 값 자체는 Player 내부에만 있다.
    public enum SurvivalStat
    {
        Hydration,
        Energy,
        Radiation,
    }

    // RULE-05: 전신 생존 수치 대신 보여 주는 등급 (M7 설계 §1.5, Q43). 수치는 UI로 내보내지 않는다.
    // 수분·에너지: Low = 최대 × survivalLowRatio 이하, Critical = 0. 방사능: Low = 선량 ≥ radiationWarnDose,
    // Critical = 선량 ≥ radiationSicknessDose(최대 HP 감소 중). 경계는 PlayerConfigSO (TODO(GDD-?) Q43).
    public enum SurvivalTier
    {
        Normal,
        Low,
        Critical,
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
