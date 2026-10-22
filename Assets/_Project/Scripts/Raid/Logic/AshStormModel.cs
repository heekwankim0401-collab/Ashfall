using System;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // 재 폭풍 한 번의 상태 (§8.3, M6 설계 §1.5, Q23·Q24·Q26·Q27). 순수 C#.
    // 경과 시간 t(레이드 경과 초)만으로 상태가 정해진다(되감아도 그 시각의 상태). 레이드당 한 번 — Over 뒤에는 다시 없다.
    //   t < 시작             → Pending  (강도 0)
    //   시작 ~ +rise         → Rising   (0 → 1 선형)
    //   ~ +peak              → Peak     (1, §8.3 "3분")
    //   ~ +fall              → Falling  (1 → 0 선형)
    //   그 뒤                → Over     (0)
    // 시야: 강도 0 = +∞, 아니면 lerp(맑을 때 가시거리, stormVisibilityM, 강도) → Peak = 정확히 stormVisibilityM(30m).
    // 방사능: radiationPerSec × 강도 (방독면은 M7 — 소비자가 처리, TODO(GDD-?) Q27).
    public sealed class AshStormModel
    {
        private readonly float riseSec;
        private readonly float peakSec;
        private readonly float fallSec;
        private readonly float stormVisibilityM;
        private readonly float radiationPerSec;
        private readonly float clearVisibilityM;

        public AshStormModel(AshStormConfigSO config, float clearVisibilityM, float startSec)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            riseSec = Mathf.Max(0f, config.riseSec);
            peakSec = Mathf.Max(0f, config.peakSec);
            fallSec = Mathf.Max(0f, config.fallSec);
            stormVisibilityM = Mathf.Max(0f, config.stormVisibilityM);
            radiationPerSec = Mathf.Max(0f, config.radiationPerSec);
            this.clearVisibilityM = clearVisibilityM;
            StartSec = Mathf.Max(0f, startSec);
            SetState(AshStormPhase.Pending, 0f);
        }

        public float StartSec { get; private set; }
        public float EndSec => StartSec + riseSec + peakSec + fallSec;
        public AshStormPhase Phase { get; private set; }
        public float Intensity { get; private set; }
        public float VisibilityM { get; private set; }
        public float RadiationPerSec { get; private set; }

        // 경과 시간으로 상태를 정한다. 단계가 바뀌었으면 true.
        public bool Evaluate(float elapsedSec)
        {
            AshStormPhase before = Phase;
            float t = elapsedSec - StartSec;
            if (float.IsNaN(t) || t < 0f)
                SetState(AshStormPhase.Pending, 0f);
            else if (t < riseSec)
                SetState(AshStormPhase.Rising, t / riseSec);
            else if ((t -= riseSec) < peakSec)
                SetState(AshStormPhase.Peak, 1f);
            else if ((t -= peakSec) < fallSec)
                SetState(AshStormPhase.Falling, 1f - t / fallSec);
            else
                SetState(AshStormPhase.Over, 0f);
            return Phase != before;
        }

        // 아직 Pending일 때만 시작 시각을 바꾼다(디버그·테스트 "지금 시작"). 시작했으면 false.
        // 다음 Evaluate부터 반영된다.
        public bool Reschedule(float startSec)
        {
            if (Phase != AshStormPhase.Pending)
                return false;
            StartSec = Mathf.Max(0f, startSec);
            return true;
        }

        private void SetState(AshStormPhase phase, float intensity)
        {
            Phase = phase;
            Intensity = Mathf.Clamp01(intensity);
            VisibilityM = VisibilityFor(Intensity);
            RadiationPerSec = radiationPerSec * Intensity;
        }

        private float VisibilityFor(float intensity)
        {
            if (intensity <= 0f)
                return float.PositiveInfinity;
            // 맑을 때 가시거리가 없거나(∞·0 이하) 폭풍 시야보다 짧으면 선형 보간 대신 시야/강도(강도 1 = 폭풍 시야)로 잇는다.
            if (float.IsNaN(clearVisibilityM) || float.IsInfinity(clearVisibilityM) || clearVisibilityM <= stormVisibilityM)
                return stormVisibilityM / intensity;
            return Mathf.Lerp(clearVisibilityM, stormVisibilityM, intensity);
        }
    }
}
