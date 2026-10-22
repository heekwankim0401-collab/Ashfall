using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashfall.Core
{
    // 프로젝트의 유일한 싱글톤 (§13.3). 이벤트 버스, 서비스 레지스트리, 씬 흐름(GameFlow, M4)을 가진다 (설계 D1).
    // Boot를 거치지 않는 테스트 씬·PlayMode 테스트에서도 존재하도록 첫 씬 로드 전에 자동 생성한다.
    // 씬을 넘어 살아야 하는 상태는 여기에만 둔다. M5: 세이브·프로필(보관함·은신처 장비·탄피) = Profiles.
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public EventBus Events { get; } = new EventBus();
        public ServiceRegistry Services { get; } = new ServiceRegistry();
        // M4: 씬 전환·레이드 결과 보관·세계 정지(timeScale)의 유일한 주체
        public GameFlow Flow { get; private set; }
        // M5: 세이브·프로필 (§13.3). Awake에서는 파일을 읽지 않는다(경로만) — 첫 은신처 진입이 읽는다.
        public ProfileService Profiles { get; private set; }

        // 도메인 리로드를 끈 경우 이전 플레이의 정적 참조·정지 상태가 남지 않게 한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            Time.timeScale = 1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateInstance()
        {
            if (Instance != null)
                return;
            var go = new GameObject(nameof(GameManager));
            go.AddComponent<GameManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Profiles = new ProfileService(new FileSaveStorage(Application.persistentDataPath), () => DateTime.UtcNow);
            Flow = new GameFlow(
                // 빌드 설정에 없는 씬은 LoadSceneAsync가 null을 돌려준다(오류 로그) → 요청 실패
                sceneName => SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single) != null,
                scale => Time.timeScale = scale,
                Profiles);
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        // 테스트·도구: 세이브 위치 교체. 로드한 프로필을 버린다(다음 EnsureLoaded가 새 위치에서 읽음 = "다음 실행" 흉내).
        // PlayMode 테스트는 첫 은신처 진입 전에 OS 임시 폴더로 바꾼다(M5 설계 §2.6).
        public void UseSaveStorage(ISaveStorage storage) => Profiles.UseStorage(storage);

        private void OnDestroy()
        {
            if (Instance != this)
                return;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Events.Clear();
            Services.Clear();
            Instance = null;
        }

        // 추가(Additive) 로드는 흐름 단계를 바꾸지 않는다 (테스트가 씬을 겹쳐 올리는 경우).
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
                Flow.OnSceneLoaded(scene.name);
        }
    }
}
