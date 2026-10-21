using System;

namespace Ashfall.AI
{
    // 감각 갱신 분할 (M3 설계 §1.8). 슬롯마다 다음 기한을 들고, 프레임마다 기한이 지난 것 중
    // 오래 기다린 순으로 예산만큼만 내준다(초과분은 다음 프레임). 할당 없음(Add로 용량을 넘길 때만 늘림).
    public sealed class AiTickScheduler
    {
        private float[] due;
        private bool[] active;
        private int[] freeSlots;
        private int freeCount;
        private int slotCount;

        public AiTickScheduler(int capacity)
        {
            capacity = Math.Max(1, capacity);
            due = new float[capacity];
            active = new bool[capacity];
            freeSlots = new int[capacity];
        }

        public int ActiveCount { get; private set; }

        // 등록 순번으로 위상을 흩뜨리는 첫 기한 오프셋 (0 ≤ 값 < period). 등록 수를 미리 몰라도 고르게 퍼지도록
        // i/N 대신 황금비 수열을 쓴다 (설계 "i/N × 주기"의 구현).
        public static float PhaseOffset(int index, float period)
        {
            const double golden = 0.6180339887498949;
            double frac = (index * golden) % 1.0;
            return (float)(frac * period);
        }

        public int Add(float firstDueTime)
        {
            int slot;
            if (freeCount > 0)
            {
                slot = freeSlots[--freeCount];
            }
            else
            {
                if (slotCount == due.Length)
                {
                    int size = due.Length * 2;
                    Array.Resize(ref due, size);
                    Array.Resize(ref active, size);
                    Array.Resize(ref freeSlots, size);
                }
                slot = slotCount++;
            }
            due[slot] = firstDueTime;
            active[slot] = true;
            ActiveCount++;
            return slot;
        }

        public void Remove(int slot)
        {
            if (slot < 0 || slot >= slotCount || !active[slot])
                return;
            active[slot] = false;
            freeSlots[freeCount++] = slot;
            ActiveCount--;
        }

        public void Reschedule(int slot, float nextDueTime)
        {
            if (slot >= 0 && slot < slotCount && active[slot])
                due[slot] = nextDueTime;
        }

        public float DueTime(int slot) => due[slot];

        // 다음 기한 = 이번 기한 + period (위상 유지). "now + period"로 다시 잡으면 프레임 양자화로 위상이 흘러
        // 여러 슬롯이 한 프레임에 뭉친다. 한 주기 넘게 밀렸으면 now + period.
        public void Advance(int slot, float period, float now)
        {
            if (slot < 0 || slot >= slotCount || !active[slot])
                return;
            float next = due[slot] + period;
            due[slot] = next > now ? next : now + period;
        }

        // now 기준 기한이 지난 슬롯을 오래 기다린 순(기한 오름차순)으로 최대 budget개 slotsOut에 채운다. 개수 반환.
        public int CollectDue(float now, int budget, int[] slotsOut)
        {
            if (slotsOut == null)
                return 0;
            int keep = Math.Min(budget, slotsOut.Length);
            if (keep <= 0)
                return 0;

            int count = 0;
            for (int slot = 0; slot < slotCount; slot++)
            {
                if (!active[slot] || due[slot] > now)
                    continue;
                float d = due[slot];
                if (count == keep && d >= due[slotsOut[count - 1]])
                    continue;

                int pos = count < keep ? count : keep - 1;
                while (pos > 0 && due[slotsOut[pos - 1]] > d)
                {
                    slotsOut[pos] = slotsOut[pos - 1];
                    pos--;
                }
                slotsOut[pos] = slot;
                if (count < keep)
                    count++;
            }
            return count;
        }
    }
}
