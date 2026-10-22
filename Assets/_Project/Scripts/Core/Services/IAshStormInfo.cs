namespace Ashfall.Core
{
    // 재 폭풍 현재 상태 (§8.3, M6 설계 §1.2·§1.5). Raid(AshStormController)가 OnEnable에 등록, OnDisable에 해제한다.
    // 레이드 밖(은신처·메인 메뉴)에는 없다 = 폭풍 없음. 소비자(AI 시야·Player 방사능·Audio 환경음)는 매 틱 동기로 읽는다.
    public interface IAshStormInfo
    {
        AshStormPhase Phase { get; }
        float Intensity { get; }          // 0~1 (전환 중 선형)
        float VisibilityM { get; }        // 폭풍 아님(Intensity 0) = float.PositiveInfinity, Peak = 30
        float RadiationPerSec { get; }    // §8.3 +1/s × Intensity. 방독면은 M7(소비자가 처리) — TODO(GDD-?) Q27
    }
}
