using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Core
{
    // 이벤트 표식. 모든 이벤트는 readonly struct로 만든다 (§13.3 이벤트 버스).
    public interface IGameEvent { }

    // 기능 어셈블리 사이의 동기 이벤트 버스 (§13.3). GameManager.Events로 하나를 공유한다.
    // - 구독 순서대로 즉시 호출한다.
    // - Publish 중 구독/해지해도 안전하다(이번 호출은 시작 시점의 목록으로 끝난다).
    // - 핸들러 예외는 로그로 남기고 다음 핸들러를 계속 호출한다.
    // - 같은 핸들러를 두 번 구독하면 무시한다.
    // 도메인 모델은 버스를 모른다. 버스는 얇은 MonoBehaviour 브리지와 UI 프레젠터에서만 쓴다.
    //
    // M3 (설계 §1.8 D12): 발소리·총성이 자주 발행되므로 Publish는 할당하지 않는다.
    // 타입별 핸들러 배열을 쓰기 시 복사(copy-on-write)로 관리한다 — 구독/해지가 새 배열로 바꾸고,
    // Publish는 그 순간의 배열을 그대로 순회한다(교체돼도 순회 중 배열은 그대로 = 스냅샷).
    public sealed class EventBus
    {
        private readonly Dictionary<Type, Delegate[]> handlers = new Dictionary<Type, Delegate[]>();

        public void Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (!handlers.TryGetValue(typeof(T), out Delegate[] current))
            {
                handlers.Add(typeof(T), new Delegate[] { handler });
                return;
            }
            if (IndexOf(current, handler) >= 0)
                return;

            var next = new Delegate[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[current.Length] = handler;
            handlers[typeof(T)] = next;
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null)
                return;
            if (!handlers.TryGetValue(typeof(T), out Delegate[] current))
                return;

            int index = IndexOf(current, handler);
            if (index < 0)
                return;

            var next = new Delegate[current.Length - 1];
            Array.Copy(current, 0, next, 0, index);
            Array.Copy(current, index + 1, next, index, current.Length - index - 1);
            handlers[typeof(T)] = next;
        }

        public void Publish<T>(in T evt) where T : struct, IGameEvent
        {
            if (!handlers.TryGetValue(typeof(T), out Delegate[] snapshot) || snapshot.Length == 0)
                return;

            // 배열은 바뀌지 않는다(구독/해지는 새 배열로 교체). 핸들러 안에서 구독/해지해도 이번 호출 목록은 그대로.
            T copy = evt;
            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    ((Action<T>)snapshot[i])(copy);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        // 누수 확인용
        public int SubscriberCount<T>() where T : struct, IGameEvent =>
            handlers.TryGetValue(typeof(T), out Delegate[] current) ? current.Length : 0;

        public void Clear() => handlers.Clear();

        private static int IndexOf(Delegate[] array, Delegate handler)
        {
            for (int i = 0; i < array.Length; i++)
                if (array[i].Equals(handler))
                    return i;
            return -1;
        }
    }
}
