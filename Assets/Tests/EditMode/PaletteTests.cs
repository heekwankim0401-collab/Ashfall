using System;
using System.Linq;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 1a: §10.1 팔레트 토큰 7개 (M6 설계 §3.1). 에셋 값 확인은 1b M6DataAssetTests.
    public class PaletteTests
    {
        private PaletteSO palette;

        [SetUp]
        public void SetUp() => palette = ScriptableObject.CreateInstance<PaletteSO>();

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(palette);

        [Test]
        public void HasSevenTokens()
        {
            Assert.AreEqual(7, Enum.GetValues(typeof(PaletteToken)).Length);
        }

        [TestCase(PaletteToken.AshBlack, "Ash_Black", "#0D0807")]
        [TestCase(PaletteToken.DriedBlood, "Dried_Blood", "#3A0B0B")]
        [TestCase(PaletteToken.RustRed, "Rust_Red", "#6E1A14")]
        [TestCase(PaletteToken.Ember, "Ember", "#B2321C")]
        [TestCase(PaletteToken.AshGrey, "Ash_Grey", "#5A524E")]
        [TestCase(PaletteToken.Bone, "Bone", "#C9BBA8")]
        [TestCase(PaletteToken.Toxic, "Toxic", "#7A8A2A")]
        public void Token_NameHexAndDefaultColor_MatchGdd(PaletteToken token, string gddName, string hex)
        {
            Assert.AreEqual(gddName, PaletteSO.GddName(token));
            Assert.AreEqual(hex, PaletteSO.GddHex(token));

            Color c = palette.Get(token);
            Assert.AreEqual(hex.Substring(1), ColorUtility.ToHtmlStringRGB(c), $"{gddName} 기본 색");
            Assert.AreEqual(1f, c.a);
        }

        [Test]
        public void Get_ReturnsMatchingField()
        {
            palette.ember = new Color(0.1f, 0.2f, 0.3f, 1f);
            palette.toxic = new Color(0.4f, 0.5f, 0.6f, 1f);

            Assert.AreEqual(palette.ember, palette.Get(PaletteToken.Ember));
            Assert.AreEqual(palette.toxic, palette.Get(PaletteToken.Toxic));
            Assert.AreEqual(palette.ashBlack, palette.Get(PaletteToken.AshBlack));
        }

        [Test]
        public void AllTokens_HaveDistinctNamesAndColors()
        {
            PaletteToken[] tokens = (PaletteToken[])Enum.GetValues(typeof(PaletteToken));

            Assert.AreEqual(tokens.Length, tokens.Select(PaletteSO.GddName).Distinct().Count());
            Assert.AreEqual(tokens.Length, tokens.Select(t => ColorUtility.ToHtmlStringRGB(palette.Get(t))).Distinct().Count());
        }
    }
}
