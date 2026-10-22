using System;
using System.IO;
using System.Linq;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M5 회귀 (M5 설계 §2.7, Q24): 세이브(JsonUtility)와 은신처 UI(UI Toolkit)가 기대는 내장 모듈을 매니페스트에 명시한다.
    // 두 모듈은 다른 패키지의 전이 의존으로만 켜져 있었다(uielements는 inputsystem 하나) — 그 패키지가 빠지면 통째로 사라진다.
    public class SaveModuleTests
    {
        private const string JsonSerializePackage = "com.unity.modules.jsonserialize";
        private const string UiElementsPackage = "com.unity.modules.uielements";

        [TestCase(JsonSerializePackage)]
        [TestCase(UiElementsPackage)]
        public void Manifest_DeclaresModule(string package)
        {
            string manifest = File.ReadAllText(Path.Combine("Packages", "manifest.json"));
            StringAssert.Contains("\"" + package + "\"", manifest, $"Packages/manifest.json에 {package}가 없다");
        }

        [Test]
        public void JsonUtility_RoundTrip()
        {
            var data = new SaveData { version = 1, casings = 12, raidInProgress = true, quickSlots = new[] { 0, -1, -1, -1 } };
            data.stash.Add(new ItemRecord { id = "JNK-03", container = ItemRecord.StashContainer, x = 2, y = 5, rotated = true });

            SaveData back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data, true));

            Assert.AreEqual(1, back.version);
            Assert.AreEqual(12, back.casings);
            Assert.IsTrue(back.raidInProgress);
            Assert.AreEqual(1, back.stash.Count);
            Assert.AreEqual("JNK-03", back.stash[0].id);
            Assert.AreEqual(5, back.stash[0].y);
            Assert.IsTrue(back.stash[0].rotated);
            Assert.AreEqual(-1, back.stash[0].rounds);
            CollectionAssert.AreEqual(new[] { 0, -1, -1, -1 }, back.quickSlots);
        }

        [Test]
        public void UIDocumentType_IsLoaded()
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEngine.UIElements.UIDocument", false))
                .FirstOrDefault(t => t != null);
            Assert.IsNotNull(type, "UnityEngine.UIElements.UIDocument 형식이 로드되지 않았다 (UI Toolkit 모듈 꺼짐)");
        }
    }
}
