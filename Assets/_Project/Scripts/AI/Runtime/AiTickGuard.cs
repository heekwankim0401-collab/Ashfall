using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.AI
{
    // AiDirector가 매 프레임 부르는 적 1명 몫 (EnemyAgent가 명시적으로 구현).
    public interface IAiFrameTickable
    {
        void FrameTick(float now, float dt);
        // 틱 중 예외가 났다. 예외 로그는 AiTickGuard가 남긴다. 구현은 스스로 AI에서 빠진다(EnemyAgent: 비활성화).
        void OnTickFaulted();
    }

    // 적별 예외 격리 (리뷰 7). 한 적의 예외가 같은 프레임 뒤 순번 적들의 틱을 막지 않게 한다.
    // 예외가 없으면 할당 없음(try/catch 자체는 할당하지 않는다).
    public static class AiTickGuard
    {
        // 뒤에서 앞으로 돈다: 예외 난 적이 OnTickFaulted에서 목록에서 빠져도(자기 자리 제거) 나머지 순회가 안전하다.
        public static void FrameTickAll(IReadOnlyList<IAiFrameTickable> agents, float now, float dt)
        {
            for (int i = agents.Count - 1; i >= 0; i--)
            {
                if (i >= agents.Count)
                    continue;
                IAiFrameTickable agent = agents[i];
                try
                {
                    agent.FrameTick(now, dt);
                }
                catch (Exception e)
                {
                    Fault(agent, e);
                }
            }
        }

        public static void Fault(IAiFrameTickable agent, Exception e)
        {
            Debug.LogException(e, agent as UnityEngine.Object);
            try
            {
                agent.OnTickFaulted();
            }
            catch (Exception inner)
            {
                Debug.LogException(inner, agent as UnityEngine.Object);
            }
        }
    }
}
