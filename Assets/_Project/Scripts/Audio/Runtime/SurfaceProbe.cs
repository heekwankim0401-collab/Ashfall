using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Audio
{
    // 발 밑 지면 재질 판정 (M6 Q11, 설계 §1.4). 일반 클래스(AudioDirector 소유).
    // 발 위치 + AudioConfigSO.surfaceProbeLiftM에서 아래로 RaycastNonAlloc(트리거 포함, AudioConfigSO.surfaceMask) → SurfaceResolver.Pick.
    // 캐시: Prewarm이 씬의 GroundSurface 콜라이더(레벨 지오메트리)를 미리 넣는다. 그 밖 콜라이더(Terrain·적 캡슐·시체 등)는
    // 사전에 넣지 않고 만날 때마다 판정한다(TryGetComponent·is — 할당 없음). 그래서 사전이 레이드 중 자라지 않고
    // 이벤트 경로에서 재할당되지 않는다(M6 리뷰 낮음 8). 레이드 중 새로 붙은 GroundSurface도 매번 판정으로 맞게 나온다.
    public sealed class SurfaceProbe
    {
        // 한 번에 받는 맞음 수 (설계 §1.7 "4칸")
        public const int BufferSize = 4;
        // 사전 초기 크기 (Prewarm 전 — Prewarm이 실제 개수에 맞춰 늘린다, 시작 때 1회)
        private const int InitialCacheCapacity = 256;

        private struct Entry
        {
            public bool HasTag;
            public SurfaceKind Tag;
            public bool IsTerrain;
        }

        private readonly AudioConfigSO config;
        private readonly RaycastHit[] rayHits = new RaycastHit[BufferSize];
        private readonly SurfaceHit[] hits = new SurfaceHit[BufferSize];
        private readonly Dictionary<Collider, Entry> cache = new Dictionary<Collider, Entry>(InitialCacheCapacity);

        public SurfaceProbe(AudioConfigSO config)
        {
            this.config = config;
        }

        // 마지막 Probe의 맞음 수·결과 (테스트·디버그)
        public int LastHitCount { get; private set; }
        public SurfaceKind LastKind { get; private set; }
        public int CachedColliders => cache.Count;

        // 씬의 GroundSurface 콜라이더를 미리 등록한다(시작 때 1회, 할당 허용). 이전 등록은 지운다.
        public void Prewarm()
        {
            cache.Clear();
            GroundSurface[] surfaces = Object.FindObjectsByType<GroundSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (GroundSurface s in surfaces)
            {
                foreach (Collider c in s.GetComponents<Collider>())
                    cache[c] = Describe(c);
            }
        }

        public SurfaceKind Probe(Vector3 feet)
        {
            SurfaceKind terrain = config != null ? config.terrainSurface : SurfaceKind.Dirt;
            SurfaceKind untagged = config != null ? config.untaggedSurface : SurfaceKind.Debris;
            if (config == null)
            {
                LastHitCount = 0;
                LastKind = untagged;
                return untagged;
            }

            Vector3 origin = feet + Vector3.up * Mathf.Max(0f, config.surfaceProbeLiftM);
            float length = Mathf.Max(0f, config.surfaceProbeLengthM);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, length, config.surfaceMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider c = rayHits[i].collider;
                Entry e = Lookup(c);
                hits[i] = new SurfaceHit
                {
                    Distance = rayHits[i].distance,
                    HasTag = e.HasTag,
                    Tag = e.Tag,
                    IsTerrain = e.IsTerrain,
                    IsTrigger = c != null && c.isTrigger,
                };
            }
            LastHitCount = n;
            LastKind = SurfaceResolver.Pick(hits, n, terrain, untagged);
            return LastKind;
        }

        // 미리 등록한 GroundSurface면 캐시, 아니면 그때 판정(사전에 추가하지 않음)
        private Entry Lookup(Collider c)
        {
            if (c == null)
                return default;
            if (cache.TryGetValue(c, out Entry e))
                return e;
            return Describe(c);
        }

        private static Entry Describe(Collider c)
        {
            var e = new Entry { IsTerrain = c is TerrainCollider };
            if (c.TryGetComponent(out GroundSurface surface))
            {
                e.HasTag = true;
                e.Tag = surface.Kind;
            }
            return e;
        }
    }
}
