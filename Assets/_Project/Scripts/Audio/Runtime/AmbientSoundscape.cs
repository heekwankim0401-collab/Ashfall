using Ashfall.Core;
using UnityEngine;
using Random = System.Random;

namespace Ashfall.Audio
{
    // 레이드 환경음 (§11 환경음, 설계 §1.4). 일반 클래스(AudioDirector 소유).
    // 2D 소스 3개: 바람 루프(상시, 폭풍 때 커짐), 재 폭풍 루프(Rising~Falling), 가이거 클릭(원샷, Q22 재 폭풍 방사능률에만).
    // 금속 붕괴는 IntervalScheduler가 시각만 정하고 재생은 AudioDirector가 3D voice 풀로 한다.
    public sealed class AmbientSoundscape
    {
        private readonly AudioConfigSO config;
        private readonly GeigerClickModel geiger;
        private readonly IntervalScheduler collapse;
        private bool stormActive;

        public AmbientSoundscape(Transform parent, AudioConfigSO config, Random random)
        {
            this.config = config;
            Wind = CreateSource(parent, "Ambient_Wind", true);
            Storm = CreateSource(parent, "Ambient_AshStorm", true);
            GeigerSource = CreateSource(parent, "Ambient_Geiger", false);

            geiger = new GeigerClickModel(config.geigerClicksPerRad, config.geigerMaxClicksPerSec, random);
            collapse = new IntervalScheduler(config.collapseIntervalSec, random);

            Wind.clip = config.windLoop;
            Wind.volume = AmbientMix.WindVolume(config, 0f);
            Storm.clip = config.stormLoop;
            Storm.volume = 0f;
            GeigerSource.volume = Mathf.Clamp01(config.geigerVolume);
        }

        public AudioSource Wind { get; }
        public AudioSource Storm { get; }
        public AudioSource GeigerSource { get; }
        public IntervalScheduler CollapseSchedule => collapse;

        // 바람 재생 요청을 했는지 / 폭풍 루프 재생 중(요청 기준 — 배치모드는 장치가 없을 수 있다)
        public bool WindRequested { get; private set; }
        public bool StormLoopActive => stormActive;
        public int GeigerClicks { get; private set; }

        public void Begin()
        {
            if (Wind.clip != null && !WindRequested)
            {
                Wind.Play();
                WindRequested = true;
            }
        }

        // 단계 전환 알림(AshStormChanged)
        public void OnStormPhase(AshStormPhase phase) => SetStormLoop(AmbientMix.StormLoopWanted(phase));

        // 매 프레임. 금속 붕괴를 울릴 때면 true.
        public bool Tick(float dt, float stormIntensity, float radiationPerSec)
        {
            Wind.volume = AmbientMix.WindVolume(config, stormIntensity);
            // 늦게 생긴 디렉터가 Rising 이벤트를 놓쳤어도 강도가 있으면 튼다
            if (stormIntensity > 0f && !stormActive)
                SetStormLoop(true);
            Storm.volume = AmbientMix.StormVolume(config, stormIntensity);

            int clicks = geiger.Tick(dt, radiationPerSec);
            if (clicks > 0 && config.geigerClick != null)
            {
                for (int i = 0; i < clicks; i++)
                    GeigerSource.PlayOneShot(config.geigerClick, 1f);
                GeigerClicks += clicks;
            }

            if (config.collapseClips == null || config.collapseClips.Length == 0)
                return false;
            return collapse.Tick(dt);
        }

        private void SetStormLoop(bool on)
        {
            if (on == stormActive)
                return;
            stormActive = on;
            if (on)
            {
                if (Storm.clip != null)
                    Storm.Play();
            }
            else
            {
                Storm.Stop();
            }
        }

        private static AudioSource CreateSource(Transform parent, string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;   // 2D
            s.dopplerLevel = 0f;
            return s;
        }
    }
}
