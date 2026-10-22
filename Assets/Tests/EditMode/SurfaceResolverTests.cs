using Ashfall.Audio;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M6 2b: 지면 재질 판정 (Q11) — 가장 가까운 GroundSurface > 지형 > 기본, 태그 없는 트리거 건너뜀
    public class SurfaceResolverTests
    {
        private const SurfaceKind Terrain = SurfaceKind.Dirt;
        private const SurfaceKind Untagged = SurfaceKind.Debris;

        private static SurfaceHit Tagged(float d, SurfaceKind k, bool trigger = false) =>
            new SurfaceHit { Distance = d, HasTag = true, Tag = k, IsTrigger = trigger };

        private static SurfaceHit TerrainHit(float d) => new SurfaceHit { Distance = d, IsTerrain = true };
        private static SurfaceHit Plain(float d, bool trigger = false) => new SurfaceHit { Distance = d, IsTrigger = trigger };

        private static SurfaceKind Pick(params SurfaceHit[] hits) => SurfaceResolver.Pick(hits, hits.Length, Terrain, Untagged);

        [Test]
        public void NoHits_Untagged()
        {
            Assert.AreEqual(Untagged, Pick());
            Assert.AreEqual(Untagged, SurfaceResolver.Pick(null, 3, Terrain, Untagged));
        }

        [Test]
        public void TerrainOnly_TerrainSurface() => Assert.AreEqual(Terrain, Pick(TerrainHit(0.5f)));

        [Test]
        public void UntaggedCollider_UntaggedSurface() => Assert.AreEqual(Untagged, Pick(Plain(0.5f)));

        [Test]
        public void Tagged_BeatsCloserTerrainAndUntagged()
        {
            Assert.AreEqual(SurfaceKind.Metal, Pick(TerrainHit(0.4f), Tagged(0.6f, SurfaceKind.Metal)));
            Assert.AreEqual(SurfaceKind.Metal, Pick(Plain(0.3f), Tagged(0.6f, SurfaceKind.Metal)));
        }

        [Test]
        public void NearestTagged_Wins()
        {
            Assert.AreEqual(SurfaceKind.Water, Pick(Tagged(0.9f, SurfaceKind.Metal), Tagged(0.45f, SurfaceKind.Water, true)));
        }

        [Test]
        public void TaggedTriggerPuddle_OverTerrain_IsWater()
        {
            Assert.AreEqual(SurfaceKind.Water, Pick(TerrainHit(0.5f), Tagged(0.48f, SurfaceKind.Water, trigger: true)));
            Assert.AreEqual(SurfaceKind.Water, Pick(TerrainHit(0.5f), Tagged(0.55f, SurfaceKind.Water, trigger: true)), "지형 아래로 살짝 묻힌 웅덩이도");
        }

        [Test]
        public void UntaggedTrigger_Skipped()
        {
            Assert.AreEqual(Terrain, Pick(Plain(0.2f, trigger: true), TerrainHit(0.5f)));
            Assert.AreEqual(Untagged, Pick(Plain(0.2f, trigger: true)), "트리거만 = 맞은 것 없음과 같다");
        }

        [Test]
        public void TerrainBeatsUntaggedSolid()
        {
            Assert.AreEqual(Terrain, Pick(Plain(0.3f), TerrainHit(0.5f)));
        }

        [Test]
        public void Count_LimitsReadHits()
        {
            var hits = new[] { TerrainHit(0.5f), Tagged(0.4f, SurfaceKind.Metal) };
            Assert.AreEqual(Terrain, SurfaceResolver.Pick(hits, 1, Terrain, Untagged), "개수 밖(재사용 배열의 지난 값)은 보지 않는다");
            Assert.AreEqual(SurfaceKind.Metal, SurfaceResolver.Pick(hits, 5, Terrain, Untagged), "개수가 배열보다 크면 배열 길이까지");
        }
    }
}
