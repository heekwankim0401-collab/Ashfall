using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Core
{
    // 기능 간 조회·명령용 서비스 목록. GameManager.Services로 하나를 공유한다 (설계 D2).
    // 등록 대상은 Core에 정의된 인터페이스만 (IInventoryService, IMagazineSource, IBodyStatusView,
    // M3: IPlayerTarget, IProjectileService, M4: IPlayerPlacement(Player), IRaidInfo(Raid), M5: IHideoutService(Hideout),
    // M6: IAshStormInfo(Raid — 레이드 씬에만)).
    // 씬을 넘는 흐름(GameFlow)은 서비스가 아니라 GameManager.Flow로 쓴다.
    // 제공자: Awake/OnEnable에서 Register, OnDisable에서 Unregister. 소비자: Start 이후 TryGet.
    public sealed class ServiceRegistry
    {
        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();

        // 같은 T가 이미 있으면 덮어쓰고 경고한다.
        public void Register<T>(T service) where T : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            if (services.TryGetValue(typeof(T), out object existing) && !ReferenceEquals(existing, service))
                Debug.LogWarning($"[ServiceRegistry] {typeof(T).Name} 중복 등록 — 이전 서비스를 덮어쓴다.");
            services[typeof(T)] = service;
        }

        // 등록된 것과 같은 인스턴스일 때만 해제한다 (다른 제공자가 덮어쓴 뒤 늦게 해지해도 안전).
        public void Unregister<T>(T service) where T : class
        {
            if (service != null && services.TryGetValue(typeof(T), out object existing) && ReferenceEquals(existing, service))
                services.Remove(typeof(T));
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (services.TryGetValue(typeof(T), out object found))
            {
                service = (T)found;
                return true;
            }
            service = null;
            return false;
        }

        public void Clear() => services.Clear();
    }
}
