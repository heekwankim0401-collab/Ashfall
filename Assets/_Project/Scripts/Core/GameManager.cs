using UnityEngine;

namespace Ashfall.Core
{
    // 프로젝트의 유일한 싱글톤 (§13.3). 이벤트 버스와 서비스 레지스트리를 가진다 (설계 D1).
    // Boot를 거치지 않는 테스트 씬·PlayMode 테스트에서도 존재하도록 첫 씬 로드 전에 자동 생성한다.
    // 세이브·씬 흐름은 M4/M5에서 여기에 더한다.
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public EventBus Events { get; } = new EventBus();
        public ServiceRegistry Services { get; } = new ServiceRegistry();

        // 도메인 리로드를 끈 경우 이전 플레이의 정적 참조가 남지 않게 한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
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
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;
            Events.Clear();
            Services.Clear();
            Instance = null;
        }
    }
}
