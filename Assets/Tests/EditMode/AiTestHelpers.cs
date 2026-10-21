using System.Collections.Generic;
using Ashfall.AI;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M3 2b 테스트 도우미 (M3TestData.cs는 고치지 않는다).
    internal static class AiTest
    {
        public const float EyeHeight = 1.6f;
        public const float CenterHeight = 0.9f;

        // 0할당 검증. 1단계 메모대로 측정할 대리자를 먼저 한 번 실행(JIT·트램펄린 제외)한 뒤 잰다.
        // Unity Recorder("GC.Alloc")가 가끔 측정 바깥 샘플을 섞어 센다(2b 진단: 빈 대리자도 첫 측정에서 4로 나옴,
        // 이어지는 측정은 0. 전체 스위트에서는 매번 다른 할당 테스트가 실패하고 단독 실행은 매번 통과).
        // 그래서 최대 attempts번 재고 한 번이라도 0이면 통과. 매 호출 할당이 있으면 모두 실패하므로 검증력은 그대로다.
        // 잡음은 짧은 시간 구간에 몰려 오므로(연달아 잰 4번이 모두 실패한 적 있음) 시도 사이를 조금씩 띄운다.
        // (GC.GetAllocatedBytesForCurrentThread는 Unity Mono에서 항상 0이라 쓸 수 없다.)
        public static void AssertNoAlloc(NUnit.Framework.TestDelegate act, int attempts = 6)
        {
            act();
            for (int i = 0; i < attempts - 1; i++)
            {
                if (((NUnit.Framework.Constraints.IResolveConstraint)UnityEngine.TestTools.Constraints.ConstraintExtensions
                        .AllocatingGCMemory(NUnit.Framework.Is.Not)).Resolve().ApplyTo(act).IsSuccess)
                    return;
                System.Threading.Thread.Sleep(50 * (i + 1));
            }
            NUnit.Framework.Assert.That(act,
                UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(NUnit.Framework.Is.Not));
        }

        // 표적 없음 (IPlayerTarget 없음 또는 사망)
        public static EnemySenses NoTarget(float now, Vector3 self) => new EnemySenses
        {
            Now = now, SelfPosition = self, SelfForward = Vector3.forward, Arrived = false,
            TargetExists = false, TargetVisible = false, HiddenFromTarget = true,
        };

        // 표적은 있지만 안 보임
        public static EnemySenses Unseen(float now, Vector3 self, bool hidden = true) => new EnemySenses
        {
            Now = now, SelfPosition = self, SelfForward = Vector3.forward, Arrived = false,
            TargetExists = true, TargetVisible = false, EffectiveSightRange = 60f, HiddenFromTarget = hidden,
        };

        // 표적이 보임 (발 위치 target). 거리 = 내 눈 → 표적 중심.
        public static EnemySenses Seen(float now, Vector3 self, Vector3 target, float effectiveRange = 60f, bool hidden = true)
        {
            Vector3 eye = self + Vector3.up * EyeHeight;
            Vector3 center = target + Vector3.up * CenterHeight;
            return new EnemySenses
            {
                Now = now, SelfPosition = self, SelfForward = Vector3.forward, Arrived = false,
                TargetExists = true, TargetVisible = true,
                TargetPosition = target, TargetEyePosition = target + Vector3.up * EyeHeight, TargetCenter = center,
                TargetVelocity = Vector3.zero,
                TargetDistance = Vector3.Distance(eye, center), EffectiveSightRange = effectiveRange,
                HiddenFromTarget = hidden,
            };
        }
    }

    // 엄폐 질의 결과를 차례로 정해 줄 수 있는 IEnemyWorld (측면 우회처럼 Fake의 "가장 가까운 지점" 규칙으로
    // 만들 수 없는 경우). 대본이 비면 FakeEnemyWorld에 맡긴다.
    internal sealed class ScriptedCoverWorld : IEnemyWorld
    {
        public readonly FakeEnemyWorld Inner = new FakeEnemyWorld();
        public readonly Queue<(CoverResult result, int coverId)> Script = new Queue<(CoverResult, int)>();
        public readonly List<CoverQuery> Queries = new List<CoverQuery>();
        // 대본으로 준 지점의 예약 (agentId → coverId)
        public readonly Dictionary<int, int> Reserved = new Dictionary<int, int>();

        public float Now => Inner.Now;

        public CoverResult TryFindCover(in CoverQuery query, out CoverSpot spot)
        {
            Queries.Add(query);
            if (Script.Count == 0)
                return Inner.TryFindCover(query, out spot);

            (CoverResult result, int coverId) = Script.Dequeue();
            spot = default;
            if (result != CoverResult.Found)
                return result;
            Inner.ReleaseCover(query.AgentId);
            spot = Inner.GetCover(coverId);
            Reserved[query.AgentId] = coverId;
            return CoverResult.Found;
        }

        public void ReleaseCover(int agentId)
        {
            Reserved.Remove(agentId);
            Inner.ReleaseCover(agentId);
        }
        public bool IsBlocked(Vector3 from, Vector3 to, int ignoreAgentId) => Inner.IsBlocked(from, to, ignoreAgentId);
        public bool TrySampleNavPoint(Vector3 around, float radiusM, out Vector3 point) =>
            Inner.TrySampleNavPoint(around, radiusM, out point);
    }
}
