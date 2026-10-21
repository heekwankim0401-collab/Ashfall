namespace Ashfall.Core
{
    // 열린 전체 화면. None = 모두 닫힘 (게임플레이 입력 허용).
    public enum ScreenKind
    {
        None,
        Inventory,
        BodyStatus,
    }

    // RULE-04 탄창 확인 결과 (탄 수 대신 대략적 구간)
    public enum MagazineFillLevel
    {
        Full,
        Half,
        AlmostEmpty,
        NoMagazine,
    }

    // 의약품 사용 거부 사유
    public enum MedicalUseFailReason
    {
        NotNeeded,
        Busy,
        Dead,
        Depleted,   // 남은 사용 횟수 0
    }
}
