using UnityEngine;

namespace Ashfall.Audio
{
    // 효과음 3D voice 풀 (설계 §1.4·§1.7). 일반 클래스(AudioDirector 소유). 생성 때 AudioSource N개를 자식으로 만들고
    // 이후 런타임 할당·AddComponent 없음. 배정은 VoiceAllocator(우선순위 + 가장 오래된 것 빼앗기).
    // 3D 공간음: spatialBlend 1, Doppler 0, 롤오프 = 평탄 사용자 곡선(감쇠 없음) — 음량은 순수 함수가 정하고 Unity는 방향(팬)만 준다.
    public sealed class AudioVoicePool
    {
        private readonly AudioSource[] sources;
        private readonly VoiceAllocator allocator;

        public AudioVoicePool(Transform parent, int count, float maxDistanceM)
        {
            count = Mathf.Max(0, count);
            sources = new AudioSource[count];
            allocator = new VoiceAllocator(count);
            // 평탄 곡선: 거리와 무관하게 1 (곡선은 maxDistance로 정규화된 거리에서 평가된다)
            var flat = AnimationCurve.Constant(0f, 1f, 1f);
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Voice_" + i.ToString("00"));
                go.transform.SetParent(parent, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 1f;
                s.dopplerLevel = 0f;
                s.spread = 0f;
                s.rolloffMode = AudioRolloffMode.Custom;
                s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, flat);
                s.minDistance = 1f;
                s.maxDistance = Mathf.Max(1f, maxDistanceM);
                sources[i] = s;
            }
        }

        public int Count => sources.Length;
        // 누적: 재생 요청 수, 빼앗기 수, 칸이 없어 버린 수
        public int PlayCount { get; private set; }
        public int StealCount { get; private set; }
        public int DroppedCount { get; private set; }
        // 마지막으로 쓴 칸 (없으면 −1)
        public int LastVoice { get; private set; } = -1;

        public AudioSource Source(int index) => index >= 0 && index < sources.Length ? sources[index] : null;
        public VoiceAllocator Allocator => allocator;

        // 재생 요청. 칸을 얻으면 true. clip null이면 false(버린 수에 넣지 않음).
        public bool Play(AudioClip clip, Vector3 position, float gain, float pitch, int priority, float now)
        {
            if (clip == null)
                return false;
            float safePitch = pitch > 0.01f ? pitch : 1f;
            int index = allocator.Acquire(priority, now, clip.length / safePitch);
            if (index < 0)
            {
                DroppedCount++;
                return false;
            }
            if (allocator.LastAcquireStole)
                StealCount++;

            AudioSource s = sources[index];
            s.Stop();
            s.transform.position = position;
            s.clip = clip;
            s.volume = Mathf.Clamp01(gain);
            s.pitch = safePitch;
            s.Play();
            LastVoice = index;
            PlayCount++;
            return true;
        }
    }
}
