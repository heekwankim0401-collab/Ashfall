using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // §13.3 이벤트 버스 (설계 D1)
    public class EventBusTests
    {
        private EventBus bus;

        [SetUp]
        public void SetUp() => bus = new EventBus();

        [Test]
        public void Publish_DeliversPayload_InSubscriptionOrder()
        {
            var calls = new List<string>();
            bus.Subscribe<PlayerDied>(e => calls.Add("a:" + e.Cause));
            bus.Subscribe<PlayerDied>(e => calls.Add("b:" + e.Cause));

            bus.Publish(new PlayerDied(BodyPart.Head));

            CollectionAssert.AreEqual(new[] { "a:Head", "b:Head" }, calls);
        }

        [Test]
        public void Publish_OnlyReachesSameEventType()
        {
            int died = 0, changed = 0;
            bus.Subscribe<PlayerDied>(_ => died++);
            bus.Subscribe<InventoryChanged>(_ => changed++);

            bus.Publish(new InventoryChanged());

            Assert.AreEqual(0, died);
            Assert.AreEqual(1, changed);
        }

        [Test]
        public void Publish_WithoutSubscribers_DoesNothing()
        {
            Assert.DoesNotThrow(() => bus.Publish(new InventoryChanged()));
        }

        [Test]
        public void Unsubscribe_StopsDelivery_AndCountDrops()
        {
            int count = 0;
            Action<InventoryChanged> handler = _ => count++;
            bus.Subscribe(handler);
            Assert.AreEqual(1, bus.SubscriberCount<InventoryChanged>());

            bus.Unsubscribe(handler);
            bus.Publish(new InventoryChanged());

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, bus.SubscriberCount<InventoryChanged>());
        }

        [Test]
        public void Subscribe_SameHandlerTwice_IsIgnored()
        {
            int count = 0;
            Action<InventoryChanged> handler = _ => count++;
            bus.Subscribe(handler);
            bus.Subscribe(handler);

            bus.Publish(new InventoryChanged());

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, bus.SubscriberCount<InventoryChanged>());
        }

        [Test]
        public void Unsubscribe_UnknownHandler_IsSafe()
        {
            Assert.DoesNotThrow(() => bus.Unsubscribe<InventoryChanged>(_ => { }));
            Assert.DoesNotThrow(() => bus.Unsubscribe<InventoryChanged>(null));
        }

        [Test]
        public void HandlerException_IsLogged_AndOthersStillRun()
        {
            int after = 0;
            bus.Subscribe<InventoryChanged>(_ => throw new InvalidOperationException("boom"));
            bus.Subscribe<InventoryChanged>(_ => after++);

            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            bus.Publish(new InventoryChanged());

            Assert.AreEqual(1, after);
        }

        [Test]
        public void UnsubscribeDuringPublish_CurrentPublishStillUsesSnapshot()
        {
            var calls = new List<string>();
            Action<InventoryChanged> second = _ => calls.Add("second");
            Action<InventoryChanged> first = null;
            first = _ =>
            {
                calls.Add("first");
                bus.Unsubscribe(first);
                bus.Unsubscribe(second);
            };
            bus.Subscribe(first);
            bus.Subscribe(second);

            bus.Publish(new InventoryChanged());
            CollectionAssert.AreEqual(new[] { "first", "second" }, calls, "이번 Publish는 스냅샷대로 호출");

            calls.Clear();
            bus.Publish(new InventoryChanged());
            Assert.IsEmpty(calls, "다음 Publish부터 해지 반영");
        }

        [Test]
        public void SubscribeDuringPublish_TakesEffectNextPublish()
        {
            int late = 0;
            bool added = false;
            bus.Subscribe<InventoryChanged>(_ =>
            {
                if (added) return;
                added = true;
                bus.Subscribe<InventoryChanged>(__ => late++);
            });

            bus.Publish(new InventoryChanged());
            Assert.AreEqual(0, late);

            bus.Publish(new InventoryChanged());
            Assert.AreEqual(1, late);
        }

        // M3 설계 §1.8: 발소리·총성이 자주 발행되므로 Publish는 할당하지 않는다 (쓰기 시 복사 배열).
        [Test]
        public void Publish_WithTwoSubscribers_DoesNotAllocate()
        {
            int a = 0, b = 0;
            bus.Subscribe<NoiseEmitted>(_ => a++);
            bus.Subscribe<NoiseEmitted>(_ => b++);
            var evt = new NoiseEmitted(new Vector3(1f, 0f, 2f), 20f, NoiseKind.Footstep);
            TestDelegate publishMany = () =>
            {
                for (int i = 0; i < 100; i++)
                    bus.Publish(evt);
            };
            // 워밍업(첫 호출의 JIT·트램펄린 할당 제외) + 측정 잡음 재시도 (M3 2b 메모 "0할당 측정 주의")
            AiTest.AssertNoAlloc(publishMany);
            // 측정 횟수는 재시도에 따라 달라진다: 워밍업 1회 + 측정 1회 이상, 두 구독자가 같은 횟수
            Assert.GreaterOrEqual(a, 200);
            Assert.AreEqual(a, b);
            Assert.AreEqual(0, a % 100);
        }

        [Test]
        public void Publish_PassesNoisePayload()
        {
            NoiseEmitted received = default;
            bus.Subscribe<NoiseEmitted>(e => received = e);

            bus.Publish(new NoiseEmitted(new Vector3(1f, 2f, 3f), 120f, NoiseKind.Gunshot));

            Assert.AreEqual(new Vector3(1f, 2f, 3f), received.Position);
            Assert.AreEqual(120f, received.RadiusM);
            Assert.AreEqual(NoiseKind.Gunshot, received.Kind);
        }

        [Test]
        public void UnsubscribeMiddle_KeepsOrderOfOthers()
        {
            var calls = new List<string>();
            Action<InventoryChanged> b = _ => calls.Add("b");
            bus.Subscribe<InventoryChanged>(_ => calls.Add("a"));
            bus.Subscribe(b);
            bus.Subscribe<InventoryChanged>(_ => calls.Add("c"));

            bus.Unsubscribe(b);
            bus.Publish(new InventoryChanged());

            CollectionAssert.AreEqual(new[] { "a", "c" }, calls);
            Assert.AreEqual(2, bus.SubscriberCount<InventoryChanged>());
        }

        [Test]
        public void Clear_RemovesAllSubscribers()
        {
            bus.Subscribe<InventoryChanged>(_ => { });
            bus.Subscribe<PlayerDied>(_ => { });

            bus.Clear();

            Assert.AreEqual(0, bus.SubscriberCount<InventoryChanged>());
            Assert.AreEqual(0, bus.SubscriberCount<PlayerDied>());
        }

        [Test]
        public void ConditionModifiers_EqualityDetectsChange()
        {
            Assert.AreEqual(ConditionModifiers.Healthy, new ConditionModifiers(1f, true, 1f));
            Assert.AreNotEqual(ConditionModifiers.Healthy, new ConditionModifiers(0.6f, false, 1f));
            Assert.IsTrue(ConditionModifiers.Healthy != new ConditionModifiers(1f, true, 2f));
        }
    }

    // 서비스 레지스트리 (설계 D2)
    public class ServiceRegistryTests
    {
        private interface IFoo { }
        private sealed class Foo : IFoo { }

        private ServiceRegistry registry;

        [SetUp]
        public void SetUp() => registry = new ServiceRegistry();

        [Test]
        public void TryGet_Missing_ReturnsFalseAndNull()
        {
            Assert.IsFalse(registry.TryGet(out IFoo found));
            Assert.IsNull(found);
        }

        [Test]
        public void Register_ThenTryGet_ReturnsSameInstance()
        {
            var foo = new Foo();
            registry.Register<IFoo>(foo);

            Assert.IsTrue(registry.TryGet(out IFoo found));
            Assert.AreSame(foo, found);
        }

        [Test]
        public void Register_IsKeyedByGenericType()
        {
            registry.Register<IFoo>(new Foo());
            Assert.IsFalse(registry.TryGet(out Foo _), "구체 타입으로는 찾지 않는다");
        }

        [Test]
        public void Register_Twice_OverwritesWithWarning()
        {
            var a = new Foo();
            var b = new Foo();
            registry.Register<IFoo>(a);

            LogAssert.Expect(LogType.Warning, new Regex("IFoo"));
            registry.Register<IFoo>(b);

            registry.TryGet(out IFoo found);
            Assert.AreSame(b, found);
        }

        [Test]
        public void Unregister_OnlyRemovesMatchingInstance()
        {
            var a = new Foo();
            var b = new Foo();
            registry.Register<IFoo>(a);
            LogAssert.Expect(LogType.Warning, new Regex("IFoo"));
            registry.Register<IFoo>(b);

            registry.Unregister<IFoo>(a);
            Assert.IsTrue(registry.TryGet(out IFoo found), "다른 인스턴스 해지는 무시");
            Assert.AreSame(b, found);

            registry.Unregister<IFoo>(b);
            Assert.IsFalse(registry.TryGet(out IFoo _));
        }

        [Test]
        public void Register_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => registry.Register<IFoo>(null));
        }
    }
}
