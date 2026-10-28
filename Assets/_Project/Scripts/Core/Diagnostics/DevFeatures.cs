using UnityEngine;

namespace Ashfall.Core
{
    // 개발 빌드 전용 기능의 판정·상수를 한 곳에 모은다 (2026-10-28 사용자 결정, CHANGELOG_GDD "개발용 귀환").
    //
    // 개발용 귀환(DEV-RETURN): 일시정지 메뉴 [귀환(개발)] → 확인 → 지금 소지품을 잃지 않고 은신처로 = 탈출 성공과 같은 처리
    // (Outcome Extracted, 반출·세이브 반영 동일, 탈출 지점 id/이름만 아래 상수). 에디터와 개발 빌드(Debug.isDebugBuild)에서만,
    // 릴리스 빌드에서는 버튼을 만들지 않고(PauseScreenView) 요청도 무시한다(GameUIPresenter·RaidController 이중 확인).
    // 정식 릴리스에서 지울 때: 코드 전체에서 "DEV-RETURN"을 검색하면 손댄 곳이 모두 나온다
    // (이 파일, GameEvents.DevReturnRequested, RaidSession.DevReturn·IsDevReturn, RaidResultBuilder, RaidController 구독,
    //  GameUIPresenter·PauseScreenView·GameUIRoot·UiText, 테스트 DevReturnTests·HideoutLoopFlowTests·M5TestSupport).
    public static class DevFeatures
    {
        // 결과에 남는 탈출 지점 id·이름 (RaidResult.ExtractionId/ExtractionName). 실제 탈출구 id(EXT-*)와 겹치지 않는다.
        public const string DevReturnExtractionId = "DEV-RETURN";
        public const string DevReturnLabel = "귀환(개발)";

        // 순수 판정 (EditMode 테스트용). 에디터 Play 또는 개발 빌드면 true, 릴리스 빌드(둘 다 false)면 false.
        public static bool ShowDevReturn(bool isDebugBuild, bool isEditor) => isDebugBuild || isEditor;

        // 지금 실행 중인 플레이어/에디터 기준
        public static bool IsDevReturnAvailable => ShowDevReturn(Debug.isDebugBuild, Application.isEditor);
    }
}
