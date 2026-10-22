using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M6 회귀 (M6 설계 §2.4, Q10): 오디오(AudioSource·AudioClip)와 재 파티클(ParticleSystem)이 기대는 내장 모듈을 매니페스트에 명시한다.
    // 두 모듈은 com.unity.timeline의 전이 의존으로만 켜져 있었다 — timeline이 빠지면 통째로 사라진다(M4 terrainphysics와 같은 종류).
    public class M6ModuleTests
    {
        [TestCase("com.unity.modules.audio")]
        [TestCase("com.unity.modules.particlesystem")]
        public void Manifest_DeclaresModule(string package)
        {
            string manifest = File.ReadAllText(Path.Combine("Packages", "manifest.json"));
            StringAssert.Contains("\"" + package + "\"", manifest, $"Packages/manifest.json에 {package}가 없다");
        }

        [TestCase("UnityEngine.AudioSource")]
        [TestCase("UnityEngine.AudioClip")]
        [TestCase("UnityEngine.AudioListener")]
        [TestCase("UnityEngine.ParticleSystem")]
        public void ModuleType_IsLoaded(string typeName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName, false))
                .FirstOrDefault(t => t != null);
            Assert.IsNotNull(type, $"{typeName} 형식이 로드되지 않았다 (내장 모듈 꺼짐)");
        }
    }
}
