using Ashfall.Core;
using Ashfall.Player;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 캡슐 피격 지점 → 부위 (설계 D3). 서 있는 캡슐 높이 1.8m, 반지름 0.3m.
    public class BodyPartResolverTests
    {
        private const float Height = 1.8f;
        private const float Radius = 0.3f;
        private PlayerConfigSO config;

        [SetUp]
        public void SetUp() => config = TestData.Player();

        private BodyPart At(float x, float y) => BodyPartResolver.Resolve(new Vector3(x, y, 0.2f), Height, Radius, config);

        [Test]
        public void HeightZones()
        {
            Assert.AreEqual(BodyPart.Head, At(0f, 1.75f));          // 0.97
            Assert.AreEqual(BodyPart.Head, At(0.25f, 1.6f));        // 0.89, 옆이어도 머리
            Assert.AreEqual(BodyPart.Thorax, At(0f, 1.4f));         // 0.78
            Assert.AreEqual(BodyPart.Stomach, At(0f, 1.0f));        // 0.56
            Assert.AreEqual(BodyPart.LeftLeg, At(-0.05f, 0.5f));    // 0.28
            Assert.AreEqual(BodyPart.RightLeg, At(0.05f, 0.1f));
        }

        [Test]
        public void ArmsOnTheSides_LeftIsNegativeX()
        {
            Assert.AreEqual(BodyPart.LeftArm, At(-0.25f, 1.4f));
            Assert.AreEqual(BodyPart.RightArm, At(0.25f, 1.4f));
            // 경계: |x| ≥ 0.3 × 0.6 = 0.18 (float 경계값 자체는 피한다)
            Assert.AreEqual(BodyPart.RightArm, At(0.19f, 1.0f));
            Assert.AreEqual(BodyPart.Stomach, At(0.17f, 1.0f));
        }

        [Test]
        public void OutOfRangeHeight_Clamped()
        {
            Assert.AreEqual(BodyPart.Head, At(0f, 2.5f));
            Assert.AreEqual(BodyPart.LeftLeg, At(-0.1f, -0.2f));
        }
    }
}
