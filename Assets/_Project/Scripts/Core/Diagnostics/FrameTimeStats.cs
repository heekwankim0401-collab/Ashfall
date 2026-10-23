using System;

namespace Ashfall.Core
{
    // 프레임 시간 요약 한 번 (M7 설계 §1.9, Q51). Count 0이면 나머지는 0.
    public readonly struct FrameTimeSummary
    {
        public readonly int Count;
        public readonly float MedianMs;
        public readonly float P99Ms;
        public readonly float MaxMs;
        public readonly float MeanMs;
        // 평균 프레임 시간으로 추정한 fps (1000 / MeanMs). 표본 없음·0ms = 0
        public readonly float Fps;

        public FrameTimeSummary(int count, float medianMs, float p99Ms, float maxMs, float meanMs)
        {
            Count = count;
            MedianMs = medianMs;
            P99Ms = p99Ms;
            MaxMs = maxMs;
            MeanMs = meanMs;
            Fps = meanMs > 0f ? 1000f / meanMs : 0f;
        }
    }

    // 프레임 시간 고정 링 버퍼 (M7 설계 §1.9, Q51). 순수 C#. 생성자 밖에서는 할당하지 않는다(Add·Compute·Clear 할당 0).
    // 가득 차면 가장 오래된 표본을 덮어쓴다. Compute는 미리 잡은 작업 배열에 복사해 힙 정렬한다(Array.Sort의 비교자 할당을 피함).
    // - 중앙값: 짝수 개면 가운데 두 값의 평균
    // - 99%: 가장 가까운 순위(nearest-rank) — 정렬된 값의 ceil(0.99 × n)번째
    public sealed class FrameTimeStats
    {
        // 99% 순위는 정수로 계산한다 (0.99f × n의 부동소수 오차로 한 칸 밀리지 않게)
        public const int P99Percent = 99;

        private readonly float[] samples;
        private readonly float[] scratch;
        private int next;

        public FrameTimeStats(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "용량은 1 이상이어야 한다.");
            samples = new float[capacity];
            scratch = new float[capacity];
        }

        public int Capacity => samples.Length;
        public int Count { get; private set; }
        // Clear 이후 Add한 전체 횟수 (덮어쓴 것 포함)
        public long TotalAdded { get; private set; }

        // 음수·NaN은 무시한다 (잘못된 시간 값이 통계를 망치지 않게).
        public void Add(float frameMs)
        {
            if (!(frameMs >= 0f))
                return;
            samples[next] = frameMs;
            next = (next + 1) % samples.Length;
            if (Count < samples.Length)
                Count++;
            TotalAdded++;
        }

        public void Clear()
        {
            next = 0;
            Count = 0;
            TotalAdded = 0;
        }

        public FrameTimeSummary Compute()
        {
            int n = Count;
            if (n == 0)
                return new FrameTimeSummary(0, 0f, 0f, 0f, 0f);

            // 차기 전에는 0..n-1칸, 찬 뒤에는 링 전체(n == 용량)가 유효 표본이다. 순서는 통계에 상관없다.
            double sum = 0d;
            for (int i = 0; i < n; i++)
            {
                float v = samples[i];
                scratch[i] = v;
                sum += v;
            }
            HeapSort(scratch, n);

            float median = (n & 1) == 1
                ? scratch[n / 2]
                : (scratch[n / 2 - 1] + scratch[n / 2]) * 0.5f;
            int p99Rank = (P99Percent * n + 99) / 100;   // ceil(0.99 × n), 1..n
            if (p99Rank < 1)
                p99Rank = 1;
            if (p99Rank > n)
                p99Rank = n;
            float p99 = scratch[p99Rank - 1];
            float max = scratch[n - 1];
            return new FrameTimeSummary(n, median, p99, max, (float)(sum / n));
        }

        // 오름차순 힙 정렬 (제자리, 할당 없음)
        private static void HeapSort(float[] a, int n)
        {
            for (int i = n / 2 - 1; i >= 0; i--)
                SiftDown(a, i, n);
            for (int end = n - 1; end > 0; end--)
            {
                float t = a[0];
                a[0] = a[end];
                a[end] = t;
                SiftDown(a, 0, end);
            }
        }

        private static void SiftDown(float[] a, int root, int n)
        {
            while (true)
            {
                int child = root * 2 + 1;
                if (child >= n)
                    return;
                if (child + 1 < n && a[child + 1] > a[child])
                    child++;
                if (a[root] >= a[child])
                    return;
                float t = a[root];
                a[root] = a[child];
                a[child] = t;
                root = child;
            }
        }
    }
}
