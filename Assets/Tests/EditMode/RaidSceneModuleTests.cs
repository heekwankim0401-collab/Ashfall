using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M4 4단계 회귀: Raid_CrimsonBasin의 Terrain은 TerrainCollider로 충돌한다(플레이어·탄·적 시야).
    // 내장 패키지 "Terrain Physics"(com.unity.modules.terrainphysics)가 꺼져 있으면 Unity가 씬을 불러올 때
    // "TerrainCollider component deleted: Component belongs to a disabled built-in package."를 남기고 컴포넌트를 지운다
    // → 플레이어가 지형을 뚫고 떨어진다(4단계 PlayMode에서 발견). 매니페스트에 모듈이 있는지, 형식이 로드됐는지 본다.
    public class RaidSceneModuleTests
    {
        private const string TerrainPhysicsPackage = "com.unity.modules.terrainphysics";

        [Test]
        public void Manifest_EnablesTerrainPhysicsModule()
        {
            string manifest = File.ReadAllText(Path.Combine("Packages", "manifest.json"));
            StringAssert.Contains("\"" + TerrainPhysicsPackage + "\"", manifest,
                "Packages/manifest.json에 Terrain Physics 내장 패키지가 없다 — 레이드 지형 충돌이 사라진다");
        }

        [Test]
        public void TerrainColliderType_IsLoaded()
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEngine.TerrainCollider", false))
                .FirstOrDefault(t => t != null);
            Assert.IsNotNull(type, "UnityEngine.TerrainCollider 형식이 로드되지 않았다 (Terrain Physics 모듈 꺼짐)");
        }
    }
}
