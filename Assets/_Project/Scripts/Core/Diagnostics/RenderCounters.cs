using Unity.Profiling;

namespace Ashfall.Core
{
    // 개발 빌드 렌더 통계 (에셋 교체 성능 분석, AssetSwap 설계 §9). DevDiagnostics가 프레임마다 Sample, 로그 간격마다 Summary 후 Clear.
    // ProfilerRecorder 값은 개발 빌드 플레이어에서만 채워진다(릴리스에는 DevDiagnostics 자체가 없음). 표본 추가 할당 0.
    public sealed class RenderCounters : System.IDisposable
    {
        private static readonly string[] Names =
        {
            "SetPass Calls Count", "Draw Calls Count", "Batches Count", "Triangles Count", "Shadow Casters Count",
        };
        private static readonly string[] Labels = { "SetPass", "드로콜", "배치", "삼각형", "그림자 캐스터" };

        private readonly ProfilerRecorder[] render = new ProfilerRecorder[Names.Length];
        private readonly double[] sums = new double[Names.Length];
        private ProfilerRecorder mainThread;
        private ProfilerRecorder renderThread;
        private double mainSum, renderSum;
        private int frames;

        public RenderCounters()
        {
            for (int i = 0; i < Names.Length; i++)
                render[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, Names[i]);
            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread", 1);
        }

        public void Sample()
        {
            for (int i = 0; i < render.Length; i++)
                if (render[i].Valid)
                    sums[i] += render[i].LastValue;
            if (mainThread.Valid)
                mainSum += mainThread.LastValue;
            if (renderThread.Valid)
                renderSum += renderThread.LastValue;
            frames++;
        }

        public void Clear()
        {
            System.Array.Clear(sums, 0, sums.Length);
            mainSum = renderSum = 0;
            frames = 0;
        }

        // 프레임 평균. 메인 스레드 시간과 GPU 대기(Present 대기)로 CPU/GPU 병목을 가늠한다.
        public string Summary()
        {
            if (frames == 0)
                return "";
            var sb = new System.Text.StringBuilder("렌더 평균 —");
            for (int i = 0; i < render.Length; i++)
                sb.Append(' ').Append(Labels[i]).Append(' ').Append(render[i].Valid ? (sums[i] / frames).ToString("0") : "?");
            sb.Append(", 메인 스레드 ").Append(mainThread.Valid ? (mainSum / frames * 1e-6).ToString("0.00") + "ms" : "?");
            sb.Append(", Present 대기 ").Append(renderThread.Valid ? (renderSum / frames * 1e-6).ToString("0.00") + "ms" : "?");
            return sb.ToString();
        }

        public void Dispose()
        {
            for (int i = 0; i < render.Length; i++)
                render[i].Dispose();
            mainThread.Dispose();
            renderThread.Dispose();
        }
    }
}
