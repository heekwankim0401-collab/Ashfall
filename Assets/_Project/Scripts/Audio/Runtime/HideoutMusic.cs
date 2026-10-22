using UnityEngine;

namespace Ashfall.Audio
{
    // 은신처 BGM (§11 "BGM 없음(은신처 제외)", M6 Q18). 은신처 씬의 "HideoutMusic"(EditorTools가 배치).
    // 2D 루프, ignoreListenerPause(은신처에는 세계 정지 개념이 없다). 클립이 없으면 조용히 아무것도 하지 않는다.
    // 레이드·메인 메뉴에는 두지 않는다(음악 없음).
    public sealed class HideoutMusic : MonoBehaviour
    {
        [SerializeField] private AudioConfigSO config;

        public AudioSource Source { get; private set; }
        // 재생을 요청했는지 (배치모드는 오디오 장치가 없을 수 있어 isPlaying 대신)
        public bool PlayRequested { get; private set; }
        public AudioConfigSO Config => config;

        // 테스트·도구: Awake 전에(비활성 상태에서) 설정을 넣는다.
        public void Configure(AudioConfigSO audioConfig) => config = audioConfig;

        private void Awake()
        {
            Source = GetComponent<AudioSource>();
            if (Source == null)
                Source = gameObject.AddComponent<AudioSource>();
            Source.playOnAwake = false;
            Source.loop = true;
            Source.spatialBlend = 0f;
            Source.dopplerLevel = 0f;
            Source.ignoreListenerPause = true;
            if (config != null)
            {
                Source.clip = config.hideoutMusic;
                Source.volume = Mathf.Clamp01(config.hideoutMusicVolume);
            }
        }

        private void OnEnable()
        {
            if (Source != null && Source.clip != null && !PlayRequested)
            {
                Source.Play();
                PlayRequested = true;
            }
        }

        private void OnDisable()
        {
            if (Source != null)
                Source.Stop();
            PlayRequested = false;
        }
    }
}
