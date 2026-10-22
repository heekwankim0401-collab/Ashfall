using System;
using UnityEngine;

namespace Ashfall.Core
{
    // 게임 단계 (M4 설계 §1.4). M5: Hideout을 끝에 추가.
    public enum GamePhase
    {
        None,       // 확인 씬·테스트 씬 등 흐름 밖
        MainMenu,
        Raid,
        Hideout,
    }

    // 은신처 진입에서 꺼내는 흐름 레이드 표 (M5 설계 §1.4)
    public enum RaidReturn
    {
        None,          // 흐름 레이드가 없었다 (첫 진입·메뉴에서 옴·에디터에서 레이드 직접 Play)
        Finished,      // 흐름 레이드가 결과와 함께 끝났다 → ProfileService.ApplyRaidResult
        Interrupted,   // 흐름 레이드를 시작했지만 결과 없이 돌아왔다 → ProfileService.ApplyInterruptedRaid
    }

    // 씬 흐름·레이드 결과 보관·세계 정지의 유일한 주체 (§13.3, M4 설계 §1.3·§1.4, M5 설계 §1.2).
    // 순수 C#. 씬 로드와 timeScale은 생성자 대리자로 받아 EditMode에서 테스트한다. GameManager가 하나를 가진다.
    // 흐름: MainMenu ─EnterHideout→ Hideout ─StartRaid(Loadout)→ Raid_CrimsonBasin ─CompleteRaid(result)→ Hideout
    //       Hideout ─LeaveHideout→ MainMenu
    // 주의: sceneLoaded(→ OnSceneLoaded)는 새 씬 Awake·OnEnable 뒤에 온다. 그래서 TakeRaidReturn·TryGetRaidLoadout은
    //       Phase를 보지 않는다(새 씬 Awake에서 Phase는 아직 이전 단계).
    public sealed class GameFlow
    {
        // 씬 로드 요청. 요청이 받아들여지면 true (빌드 설정에 없는 씬 등은 false). 완료는 OnSceneLoaded로 온다.
        private readonly Func<string, bool> loadScene;
        private readonly Action<float> setTimeScale;
        // M5: 세이브·RULE-03 기록 (없으면 StartRaid 불가)
        private readonly ProfileService profiles;

        // 이번 레이드 씬에서 CompleteRaid를 이미 받았나 (두 번째 호출 무시)
        private bool raidCompleted;
        // 요청한 뒤 아직 OnSceneLoaded가 오지 않은 씬 (연속 클릭으로 같은 씬을 두 번 불러오지 않게)
        private string loadingScene;

        // M5 흐름 레이드 표: StartRaid 성공 ~ TakeRaidReturn
        private bool raidInFlow;
        private Loadout raidLoadout;
        private RaidResult flowResult;

        public GameFlow(Func<string, bool> loadScene, Action<float> setTimeScale, ProfileService profiles = null)
        {
            this.loadScene = loadScene ?? throw new ArgumentNullException(nameof(loadScene));
            this.setTimeScale = setTimeScale ?? throw new ArgumentNullException(nameof(setTimeScale));
            this.profiles = profiles;
        }

        public GamePhase Phase { get; private set; }
        public bool IsWorldPaused { get; private set; }

        // 레이드 결과. 은신처 결과 패널이 확인(AcknowledgeResult)할 때까지 남는다.
        public RaidResult PendingResult { get; private set; }

        // StartRaid 성공 ~ TakeRaidReturn (흐름으로 시작한 레이드가 아직 은신처에 반영되지 않음)
        public bool HasRaidInFlow => raidInFlow;

        // MainMenu·None → Hideout. 로드 중이면 거부. 로드를 요청했으면 true.
        public bool EnterHideout()
        {
            if ((Phase != GamePhase.MainMenu && Phase != GamePhase.None) || loadingScene != null)
            {
                Debug.LogWarning($"[GameFlow] EnterHideout 무시 — 단계 {Phase}, 로드 중 {loadingScene ?? "없음"}.");
                return false;
            }
            SetWorldPaused(false);
            return Load(SceneNames.Hideout);
        }

        // Hideout → MainMenu. 저장은 호출자(HideoutService)가 먼저 한다(W2).
        public bool LeaveHideout()
        {
            if (Phase != GamePhase.Hideout || loadingScene != null)
            {
                Debug.LogWarning($"[GameFlow] LeaveHideout 무시 — 단계 {Phase}, 로드 중 {loadingScene ?? "없음"}.");
                return false;
            }
            return Load(SceneNames.MainMenu);
        }

        // Hideout → Raid_CrimsonBasin (W4, M5 설계 §1.4). Hideout 단계에서만(RULE-03 기록을 건너뛰는 길이 없게).
        // profiles 없음·미로드 → false. profiles.BeginRaid(저장) 실패 → false(시작하지 않음).
        // 로드 요청 실패 → CancelRaid(장비 되돌림·저장) + false. 로드를 요청했으면 true.
        public bool StartRaid(Loadout loadout)
        {
            if (Phase != GamePhase.Hideout || loadingScene != null || raidInFlow)
            {
                Debug.LogWarning($"[GameFlow] StartRaid 무시 — 단계 {Phase}, 로드 중 {loadingScene ?? "없음"}, 흐름 레이드 {raidInFlow}.");
                return false;
            }
            if (profiles == null || !profiles.IsLoaded)
            {
                Debug.LogWarning("[GameFlow] StartRaid 무시 — 프로필이 없다(은신처 진입 전).");
                return false;
            }

            Loadout brought = loadout ?? Loadout.Empty;
            if (!profiles.BeginRaid(brought))
                return false;

            raidInFlow = true;
            raidLoadout = brought;
            flowResult = null;
            raidCompleted = false;   // 새 레이드 씬 Awake(TryGetRaidLoadout)가 이전 레이드 완료 표시를 보지 않게
            SetWorldPaused(false);
            if (Load(SceneNames.RaidCrimsonBasin))
                return true;

            raidInFlow = false;
            raidLoadout = null;
            profiles.CancelRaid(brought);
            return false;
        }

        // 레이드 종료 (RULE-01·02 결과). 결과를 보관하고 Hideout을 불러온다. 로드를 요청했으면 true.
        // 세계 정지는 여기서 풀지 않는다 — Hideout 로드 완료(OnSceneLoaded)에서 풀린다 (M4 리뷰 수정:
        // 결과 스냅샷 뒤 씬이 내려가기 전 프레임에 레이드 세계(재장전·의약품·AI·탄)가 다시 돌지 않게).
        // 레이드 중에는 세이브를 쓰지 않는다(RULE-03). 반영·저장은 은신처 진입(W1).
        // Raid 단계가 아니거나 이 레이드에서 두 번째 호출이면 무시(경고). result null도 무시.
        // Hideout 로드 요청이 실패하면 결과는 남기고 정지만 푼다(세계가 영구 정지하지 않게). 오버레이·잠금은 그대로라 레이드는 다시 진행되지 않는다.
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
            if (raidInFlow)
                flowResult = result;
            if (Load(SceneNames.Hideout))
                return true;
            SetWorldPaused(false);
            return false;
        }

        public void AcknowledgeResult() => PendingResult = null;

        // 흐름 레이드가 진행 중이고 sceneName == Raid_CrimsonBasin이며 아직 완료 전일 때만 반입을 준다.
        // 확인 씬·에디터에서 레이드 직접 Play는 false(→ startingItems). 레이드 인벤토리가 Awake에서 부른다.
        public bool TryGetRaidLoadout(string sceneName, out Loadout loadout)
        {
            if (raidInFlow && !raidCompleted && sceneName == SceneNames.RaidCrimsonBasin)
            {
                loadout = raidLoadout ?? Loadout.Empty;
                return true;
            }
            loadout = null;
            return false;
        }

        // 은신처 진입에서 한 번: 흐름 레이드 표를 꺼낸다(두 번째 호출은 None — 같은 결과가 두 번 반영되지 않게).
        // 결과 있음 → Finished(result), 결과 없음 → Interrupted, 표 없음 → None. PendingResult(표시)는 건드리지 않는다.
        public RaidReturn TakeRaidReturn(out RaidResult result)
        {
            result = null;
            if (!raidInFlow)
                return RaidReturn.None;

            raidInFlow = false;
            raidLoadout = null;
            result = flowResult;
            flowResult = null;
            return result != null ? RaidReturn.Finished : RaidReturn.Interrupted;
        }

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
            else if (sceneName == SceneNames.Hideout)
                Phase = GamePhase.Hideout;
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
