using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Core
{
    // 이벤트 표식. 모든 이벤트는 readonly struct로 만든다 (§13.3 이벤트 버스).
    public interface IGameEvent { }

    // 기능 어셈블리 사이의 동기 이벤트 버스 (§13.3). GameManager.Events로 하나를 공유한다.
    // - 구독 순서대로 즉시 호출한다.
    // - Publish 중 구독/해지해도 안전하다(호출 전 스냅샷).
    // - 핸들러 예외는 로그로 남기고 다음 핸들러를 계속 호출한다.
    // - 같은 핸들러를 두 번 구독하면 무시한다.
    // 도메인 모델은 버스를 모른다. 버스는 얇은 MonoBehaviour 브리지와 UI 프레젠터에서만 쓴다.
    public sealed class EventBus
    {
        private readonly Dictionary<Type, List<Delegate>> handlers = new Dictionary<Type, List<Delegate>>();

        public void Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (!handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                handlers.Add(typeof(T), list);
            }
            if (!list.Contains(handler))
                list.Add(handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null)
                return;
            if (handlers.TryGetValue(typeof(T), out var list))
                list.Remove(handler);
        }

        public void Publish<T>(in T evt) where T : struct, IGameEvent
        {
            if (!handlers.TryGetValue(typeof(T), out var list) || list.Count == 0)
                return;

            // 핸들러 안에서 구독/해지해도 이번 호출 목록은 바뀌지 않는다.
            Delegate[] snapshot = list.ToArray();
            T copy = evt;
            foreach (Delegate d in snapshot)
            {
                try
                {
                    ((Action<T>)d)(copy);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        // 누수 확인용
        public int SubscriberCount<T>() where T : struct, IGameEvent =>
            handlers.TryGetValue(typeof(T), out var list) ? list.Count : 0;

        public void Clear() => handlers.Clear();
    }
}
