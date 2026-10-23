using Ashfall.Core;
using UnityEngine;
using Random = System.Random;

namespace Ashfall.Audio
{
    // 레이드 환경음 (§11 환경음, 설계 §1.4). 일반 클래스(AudioDirector 소유).
    // 2D 소스 3개: 바람 루프(상시, 폭풍 때 커짐), 재 폭풍 루프(Rising~Falling), 가이거 클릭(원샷).
    // 가이거 입력 = 환경 방사능률 = 재 폭풍 률(Q22) + 청취자 위치 구역 률(M7 Q41, 방독면 무관 — 계수기는 환경을 잰다). GeigerClickModel은 그대로.
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
        // 마지막 Tick의 가이거 입력 (선량/초) = 폭풍 + 구역 (M7 Q41, 테스트용)
        public float GeigerInputPerSec { get; private set; }

        // 환경 방사능률 = 폭풍 률 + 구역 률. 음수·NaN·무한은 0으로 본다(가이거 모델에 이상값을 넣지 않게).
        public static float EnvironmentRadiationPerSec(float stormRadiationPerSec, float zoneRadiationPerSec) =>
            NonNegative(stormRadiationPerSec) + NonNegative(zoneRadiationPerSec);

        private static float NonNegative(float v) => v > 0f && !float.IsInfinity(v) ? v : 0f;

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

        // 매 프레임. 금속 붕괴를 울릴 때면 true. 가이거 = 폭풍 률 + 구역 률(zoneRadiationPerSec — 레이드 밖·구역 밖 0).
        public bool Tick(float dt, float stormIntensity, float stormRadiationPerSec, float zoneRadiationPerSec = 0f)
        {
            Wind.volume = AmbientMix.WindVolume(config, stormIntensity);
            // 늦게 생긴 디렉터가 Rising 이벤트를 놓쳤어도 강도가 있으면 튼다
            if (stormIntensity > 0f && !stormActive)
                SetStormLoop(true);
            Storm.volume = AmbientMix.StormVolume(config, stormIntensity);

            GeigerInputPerSec = EnvironmentRadiationPerSec(stormRadiationPerSec, zoneRadiationPerSec);
            int clicks = geiger.Tick(dt, GeigerInputPerSec);
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
