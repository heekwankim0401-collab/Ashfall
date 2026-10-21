using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // 탈출구 하나의 진행 상태 (§8.2, M4 설계 §3.1).
    public enum ExtractionPhase
    {
        Inactive,       // 이번 레이드에서 열리지 않음 (TODO(GDD-?) Q3·Q4: 들어가도 아무 표시 없음)
        Idle,           // 구역 밖
        Blocked,        // 구역 안, 조건 미충족
        CountingDown,   // 구역 안, 대기 중
        Completed,      // 대기 완료 (탈출)
    }

    // 탈출구 하나 (§8.2 EXT-*). 순수 C#.
    // TODO(GDD-?) Q8: 구역을 나가거나 조건이 깨지면 대기를 처음부터 다시. 이동·사격·피격은 취소하지 않는다.
    public sealed class ExtractionModel
    {
        // 누적 float 오차 허용 (0.1 × 200 = 20초 경계에서 한 틱 늦지 않게, 1ms). 튜닝 수치 아님.
        private const float TimeEpsilon = 1e-3f;

        public ExtractionModel(ExtractionPointConfig config, Vector3 position, bool active)
        {
            Config = config;
            Position = position;
            IsActive = active;
            Phase = active ? ExtractionPhase.Idle : ExtractionPhase.Inactive;
            RemainingSec = config.waitSec;
        }

        public ExtractionPointConfig Config { get; }
        public string Id => Config.id;
        public Vector3 Position { get; }
        public bool IsActive { get; }
        public ExtractionPhase Phase { get; private set; }
        public ExtractionBlock Block { get; private set; }
        public float RemainingSec { get; private set; }

        // 구역 밖 → Idle(초기화), 안 + block != None → Blocked(초기화), 안 + None → 감소.
        // 0에 도달한 틱에만 true(Completed). 비활성·완료 이후에는 아무것도 하지 않는다.
        public bool Tick(float dt, bool inZone, ExtractionBlock block)
        {
            if (!IsActive || Phase == ExtractionPhase.Completed)
                return false;

            if (!inZone)
            {
                ResetProgress(ExtractionPhase.Idle, ExtractionBlock.None);
                return false;
            }
            if (block != ExtractionBlock.None)
            {
                ResetProgress(ExtractionPhase.Blocked, block);
                return false;
            }

            Block = ExtractionBlock.None;
            Phase = ExtractionPhase.CountingDown;
            if (dt > 0f)
                RemainingSec -= dt;
            if (RemainingSec > TimeEpsilon)
                return false;

            RemainingSec = 0f;
            Phase = ExtractionPhase.Completed;
            return true;
        }

        // 완료 직후 소모 실패 등으로 탈출을 무르고 처음부터 (RaidSession). 비활성이면 무시.
        public void Interrupt(ExtractionBlock block)
        {
            if (!IsActive)
                return;
            ResetProgress(block == ExtractionBlock.None ? ExtractionPhase.Idle : ExtractionPhase.Blocked, block);
        }

        private void ResetProgress(ExtractionPhase phase, ExtractionBlock block)
        {
            Phase = phase;
            Block = block;
            RemainingSec = Config.waitSec;
        }
    }
}
