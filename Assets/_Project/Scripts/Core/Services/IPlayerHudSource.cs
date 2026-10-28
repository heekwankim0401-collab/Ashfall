namespace Ashfall.Core
{
    // 상시 HUD(B안, 2026-10-28 — RULE-05 개정)가 읽는 플레이어 수치. Player(PlayerHealth)가 OnEnable에 등록, OnDisable에 해제한다.
    // H 부위 상태 화면용 IBodyStatusView(등급만)와 따로 둔다 — 그쪽은 계속 수치를 내지 않는다.
    // 소비자(UI HUD)는 매 프레임 동기로 읽는다. 구현은 할당 없이 값만 돌려준다.
    public interface IPlayerHudSource
    {
        float GetPartHp(BodyPart part);
        float GetPartMaxHp(BodyPart part);       // 방사능 피폭 배율 반영
        bool HasStatus(BodyPart part, StatusEffect effect);
        SurvivalTier GetSurvivalTier(SurvivalStat stat);   // 탈수·허기·방사능 아이콘 (Critical = 수분/에너지 0, 방사능 Low 이상 = 피폭 경고)
        float Stamina { get; }
        float StaminaMax { get; }
        float Hydration { get; }
        float HydrationMax { get; }
        float Energy { get; }
        float EnergyMax { get; }
        float RadiationDose { get; }             // 이번 레이드 누적 선량 (0~, HUD는 0~100으로 표시)
        // §4.1 무게 패널티 경계 (PlayerConfigSO weightPenaltyStartKg·sprintMaxWeightKg). HUD 무게 경고색 단계를 같은 값으로 정한다.
        float WeightPenaltyStartKg { get; }
        float SprintMaxWeightKg { get; }
    }
}
