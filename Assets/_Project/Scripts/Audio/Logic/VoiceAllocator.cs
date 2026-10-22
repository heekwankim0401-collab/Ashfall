using System;

namespace Ashfall.Audio
{
    // 효과음 voice 칸 배정 (설계 §1.4). 순수, 할당 없음(생성자 제외).
    // 우선순위는 작을수록 높다(FootstepMix.Priority* 상수). 빈 칸(재생 끝남·해제) → 없으면 요청보다 우선순위가 낮거나 같은 칸 중
    // 가장 오래 전에 시작한 칸을 빼앗는다 → 그것도 없으면 −1(이번 소리는 버린다).
    public sealed class VoiceAllocator
    {
        private readonly int[] priority;
        private readonly float[] startTime;
        private readonly float[] endTime;
        private readonly bool[] busy;

        public VoiceAllocator(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            priority = new int[count];
            startTime = new float[count];
            endTime = new float[count];
            busy = new bool[count];
        }

        public int Count => busy.Length;
        // 마지막 Acquire가 재생 중인 칸을 빼앗았는지
        public bool LastAcquireStole { get; private set; }

        public int Acquire(int requestPriority, float now, float durationSec)
        {
            LastAcquireStole = false;
            int chosen = -1;
            for (int i = 0; i < busy.Length; i++)
            {
                if (!IsBusy(i, now))
                {
                    chosen = i;
                    break;
                }
            }
            if (chosen < 0)
            {
                float oldest = float.PositiveInfinity;
                for (int i = 0; i < busy.Length; i++)
                {
                    if (priority[i] >= requestPriority && startTime[i] < oldest)
                    {
                        oldest = startTime[i];
                        chosen = i;
                    }
                }
                if (chosen < 0)
                    return -1;
                LastAcquireStole = true;
            }

            busy[chosen] = true;
            priority[chosen] = requestPriority;
            startTime[chosen] = now;
            endTime[chosen] = now + (durationSec > 0f && !float.IsNaN(durationSec) ? durationSec : 0f);
            return chosen;
        }

        public void Release(int index)
        {
            if (index >= 0 && index < busy.Length)
                busy[index] = false;
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < busy.Length; i++)
                busy[i] = false;
        }

        // 재생 중 = 해제되지 않았고 끝나는 시각 전
        public bool IsBusy(int index, float now) =>
            index >= 0 && index < busy.Length && busy[index] && now < endTime[index];

        public int PriorityOf(int index) => index >= 0 && index < priority.Length ? priority[index] : int.MaxValue;

        public int BusyCount(float now)
        {
            int n = 0;
            for (int i = 0; i < busy.Length; i++)
                if (IsBusy(i, now))
                    n++;
            return n;
        }
    }
}
