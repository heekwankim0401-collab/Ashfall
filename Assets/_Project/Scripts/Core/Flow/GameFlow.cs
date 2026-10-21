using System;
using UnityEngine;

namespace Ashfall.Core
{
    // 게임 단계 (M4 설계 §1.4). M5: Hideout 추가.
    public enum GamePhase
    {
        None,       // 확인 씬·테스트 씬 등 흐름 밖
        MainMenu,
        Raid,
    }

    // 씬 흐름·레이드 결과 보관·세계 정지의 유일한 주체 (§13.3, M4 설계 §1.3·§1.4).
    // 순수 C#. 씬 로드와 timeScale은 생성자 대리자로 받아 EditMode에서 테스트한다. GameManager가 하나를 가진다.
    // 흐름: MainMenu ─StartRaid→ Raid_CrimsonBasin ─CompleteRaid(result)→ MainMenu(PendingResult 표시 → AcknowledgeResult)
    public sealed class GameFlow
    {
        // 씬 로드 요청. 요청이 받아들여지면 true (빌드 설정에 없는 씬 등은 false). 완료는 OnSceneLoaded로 온다.
        private readonly Func<string, bool> loadScene;
        private readonly Action<float> setTimeScale;

        // 이번 레이드 씬에서 CompleteRaid를 이미 받았나 (두 번째 호출 무시)
        private bool raidCompleted;
        // 요청한 뒤 아직 OnSceneLoaded가 오지 않은 씬 (연속 클릭으로 레이드를 두 번 불러오지 않게)
        private string loadingScene;

        public GameFlow(Func<string, bool> loadScene, Action<float> setTimeScale)
        {
            this.loadScene = loadScene ?? throw new ArgumentNullException(nameof(loadScene));
            this.setTimeScale = setTimeScale ?? throw new ArgumentNullException(nameof(setTimeScale));
        }

        public GamePhase Phase { get; private set; }
        public bool IsWorldPaused { get; private set; }

        // 레이드 결과. 결과 화면이 확인(AcknowledgeResult)할 때까지 남는다.
        // M5: 은신처가 보관함에 반영한 뒤 확인.
        public RaidResult PendingResult { get; private set; }

        // MainMenu → Raid_CrimsonBasin. 로드를 요청했으면 true.
        // M5 경계: StartRaid(RaidRequest request) — 반입 Loadout(보관함에서 꺼낸 ItemInstance 목록),
        //          raid_in_progress=true 세이브 기록(§13.3)이 여기 붙는다. M4는 인자 없음.
        // 이미 레이드 중이거나 레이드 씬을 불러오는 중이면 무시(경고, false). 로드 요청 실패도 false (M4 리뷰 수정:
        // 호출자(메인 메뉴)가 버튼을 다시 켤 수 있게).
        public bool StartRaid()
        {
            if (Phase == GamePhase.Raid || loadingScene == SceneNames.RaidCrimsonBasin)
            {
                Debug.LogWarning($"[GameFlow] StartRaid 무시 — 단계 {Phase}, 로드 중 {loadingScene ?? "없음"}.");
                return false;
            }
            SetWorldPaused(false);
            return Load(SceneNames.RaidCrimsonBasin);
        }

        // 레이드 종료 (RULE-01·02 결과). 결과를 보관하고 MainMenu를 불러온다. 로드를 요청했으면 true.
        // 세계 정지는 여기서 풀지 않는다 — MainMenu 로드 완료(OnSceneLoaded)에서 풀린다 (M4 리뷰 수정:
        // 결과 스냅샷 뒤 씬이 내려가기 전 프레임에 레이드 세계(재장전·의약품·AI·탄)가 다시 돌지 않게).
        // M5 경계: 목적지 Hideout, raid_in_progress=false.
        // Raid 단계가 아니거나 이 레이드에서 두 번째 호출이면 무시(경고). result null도 무시.
        // MainMenu 로드 요청이 실패하면 결과는 남기고 정지만 푼다(세계가 영구 정지하지 않게). 오버레이·잠금은 그대로라 레이드는 다시 진행되지 않는다.
        public bool CompleteRaid(RaidResult result)
        {
            if (result == null)
            {
                Debug.LogWarning("[GameFlow] CompleteRaid 무시 — 결과가 null.");
                return false;
            }
            if (Phase != GamePhase.Raid || raidCompleted)
            {
                Debug.LogWarning($"[GameFlow] CompleteRaid 무시 — 단계 {Phase}, 이미 완료 {raidCompleted}.");
                return false;
            }
            raidCompleted = true;
            PendingResult = result;
            if (Load(SceneNames.MainMenu))
                return true;
            SetWorldPaused(false);
            return false;
        }

        public void AcknowledgeResult() => PendingResult = null;

        // 세계 정지 (timeScale 0/1). 일시정지 메뉴·레이드 종료 연출이 쓴다. 값이 바뀔 때만 대리자를 부른다.
        public void SetWorldPaused(bool paused)
        {
            if (IsWorldPaused == paused)
                return;
            IsWorldPaused = paused;
            setTimeScale(paused ? 0f : 1f);
        }

        // 씬 로드 완료 (GameManager가 SceneManager.sceneLoaded로 부른다). 이름으로 단계를 정하고 정지를 푼다.
        // 흐름 밖 씬(확인 씬·테스트 씬)은 None.
        public void OnSceneLoaded(string sceneName)
        {
            loadingScene = null;
            if (sceneName == SceneNames.MainMenu)
                Phase = GamePhase.MainMenu;
            else if (sceneName == SceneNames.RaidCrimsonBasin)
                Phase = GamePhase.Raid;
            else
                Phase = GamePhase.None;

            if (Phase == GamePhase.Raid)
                raidCompleted = false;
            SetWorldPaused(false);
        }

        // 요청 실패(false·예외)면 로드 중 표시를 지우고 LogError.
        private bool Load(string sceneName)
        {
            loadingScene = sceneName;
            bool requested;
            try
            {
                requested = loadScene(sceneName);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                requested = false;
            }
            if (requested)
                return true;
            if (loadingScene == sceneName)
                loadingScene = null;
            Debug.LogError($"[GameFlow] 씬 {sceneName} 로드 요청 실패 (빌드 설정 확인).");
            return false;
        }
    }
}
