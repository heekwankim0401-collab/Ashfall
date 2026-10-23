using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7B-2b: AI-MUT 근접 규칙 EnemyMelee (M7 설계 §1.4, Q24). 준비 0.3초 → 타격/헛침, 간격 1.1초(준비 시작 기준).
    public class EnemyMeleeTests
    {
        private const float Windup = 0.3f;
        private const float Interval = 1.1f;
        private const float Dt = 1f / 60f;

        private static EnemyMelee Make() => new EnemyMelee(Windup, Interval);

        // ── 사거리·시야 ──

        [Test]
        public void OutOfRange_NeverStarts()
        {
            EnemyMelee m = Make();
            for (int f = 0; f < 300; f++)
                Assert.AreEqual(MeleeEvent.None, m.Tick(f * Dt, false, true));
            Assert.IsFalse(m.IsWindingUp);
        }

        [Test]
        public void NoLineOfSight_NeverStarts()
        {
            EnemyMelee m = Make();
            for (int f = 0; f < 300; f++)
                Assert.AreEqual(MeleeEvent.None, m.Tick(f * Dt, true, false));
        }

        // ── 준비 → 타격 ──

        [Test]
        public void InRange_WindupThenStrikeAfterWindupSec()
        {
            EnemyMelee m = Make();
            Assert.AreEqual(MeleeEvent.WindupStarted, m.Tick(1f, true, true));
            Assert.IsTrue(m.IsWindingUp);
            Assert.AreEqual(MeleeEvent.None, m.Tick(1f + Windup - 0.01f, true, true), "준비 중에는 아무 일 없음");
            Assert.AreEqual(MeleeEvent.Strike, m.Tick(1f + Windup, true, true));
            Assert.IsFalse(m.IsWindingUp);
            Assert.AreEqual(MeleeEvent.None, m.Tick(1f + Windup + 0.01f, true, true), "타격은 한 번만");
        }

        // ── 간격 ──

        [Test]
        public void StaysInRange_StrikesEveryIntervalSec()
        {
            EnemyMelee m = Make();
            var strikes = new System.Collections.Generic.List<float>();
            for (int f = 0; f * Dt < 6f; f++)
            {
                float now = f * Dt;
                if (m.Tick(now, true, true) == MeleeEvent.Strike)
                    strikes.Add(now);
            }
            Assert.GreaterOrEqual(strikes.Count, 5);
            Assert.AreEqual(Windup, strikes[0], Dt + 1e-4f, "첫 타격 = 준비 시간 뒤");
            for (int k = 1; k < strikes.Count; k++)
                Assert.AreEqual(Interval, strikes[k] - strikes[k - 1], Dt + 1e-4f, $"타격 {k} 간격");
        }

        [Test]
        public void IntervalShorterThanWindup_PeriodIsWindup()
        {
            var m = new EnemyMelee(0.5f, 0.2f);
            Assert.AreEqual(MeleeEvent.WindupStarted, m.Tick(0f, true, true));
            Assert.AreEqual(MeleeEvent.Strike, m.Tick(0.5f, true, true));
            Assert.AreEqual(MeleeEvent.WindupStarted, m.Tick(0.5f, true, true), "간격이 이미 지났으면 같은 시각에 다시 준비");
            Assert.AreEqual(MeleeEvent.Strike, m.Tick(1.0f, true, true));
        }

        // ── 헛침 ──

        [Test]
        public void LeavesRangeDuringWindup_Whiff_ConsumesInterval()
        {
            EnemyMelee m = Make();
            Assert.AreEqual(MeleeEvent.WindupStarted, m.Tick(0f, true, true));
            Assert.AreEqual(MeleeEvent.None, m.Tick(0.1f, false, true), "준비 중 사거리 밖 — 판정은 준비가 끝날 때");
            Assert.AreEqual(MeleeEvent.Whiff, m.Tick(Windup, false, true));
            Assert.IsFalse(m.IsWindingUp);

            // 바로 다시 사거리 안이어도 간격(준비 시작 + 1.1초) 전에는 준비하지 않는다
            Assert.AreEqual(MeleeEvent.None, m.Tick(Windup + 0.05f, true, true));
            Assert.AreEqual(MeleeEvent.None, m.Tick(Interval - 0.01f, true, true));
            Assert.AreEqual(MeleeEvent.WindupStarted, m.Tick(Interval, true, true));
        }

        [Test]
        public void LosesSightAtStrikeTime_Whiff()
        {
            EnemyMelee m = Make();
            m.Tick(0f, true, true);
            Assert.AreEqual(MeleeEvent.Whiff, m.Tick(Windup, true, false));
        }

        [Test]
        public void BackInRangeBeforeWindupEnds_StillStrikes()
        {
            EnemyMelee m = Make();
            m.Tick(0f, true, true);
            m.Tick(0.1f, false, true);
            Assert.AreEqual(MeleeEvent.Strike, m.Tick(Windup, true, true), "판정은 준비가 끝나는 순간의 사거리");
        }

        [Test]
        public void Reset_CancelsWindupAndInterval()
        {
            EnemyMelee m = Make();
            m.Tick(0f, true, true);
            m.Reset();
            Assert.IsFalse(m.IsWindingUp);
            Assert.AreEqual(MeleeEvent.WindupStarted, m.Tick(0.05f, true, true), "간격도 초기화");
        }

        [Test]
        public void Tick_NoAllocation()
        {
            EnemyMelee m = Make();
            float now = 0f;
            AiTest.AssertNoAlloc(() =>
            {
                for (int f = 0; f < 200; f++)
                {
                    now += Dt;
                    m.Tick(now, (f / 30) % 2 == 0, true);
                }
            });
        }

        // ── 맞을 부위 (7B 리뷰 1: 부위 힌트, AiConfigSO 가중치) ──

        [Test]
        public void StrikePart_DefaultWeights_OnlyThoraxStomachArms_Distribution()
        {
            AiConfigSO config = M3TestData.AiConfig();
            Assert.AreEqual(1f, config.meleePartWeightThorax, "설계 기본값 (흉부·복부 1/3씩, 팔 좌우 1/6씩)");
            Assert.AreEqual(1f, config.meleePartWeightStomach);
            Assert.AreEqual(0.5f, config.meleePartWeightLeftArm);
            Assert.AreEqual(0.5f, config.meleePartWeightRightArm);

            var random = new System.Random(5);
            var counts = new int[7];
            const int n = 6000;
            for (int k = 0; k < n; k++)
                counts[(int)EnemyMelee.PickStrikePart(random, config)]++;

            Assert.AreEqual(0, counts[(int)BodyPart.Head], "머리 0회");
            Assert.AreEqual(0, counts[(int)BodyPart.LeftLeg], "왼다리 0회");
            Assert.AreEqual(0, counts[(int)BodyPart.RightLeg], "오른다리 0회");
            Assert.That(counts[(int)BodyPart.Thorax], Is.InRange(n * 0.30f, n * 0.37f), "흉부 ≈ 1/3");
            Assert.That(counts[(int)BodyPart.Stomach], Is.InRange(n * 0.30f, n * 0.37f), "복부 ≈ 1/3");
            Assert.That(counts[(int)BodyPart.LeftArm], Is.InRange(n * 0.14f, n * 0.19f), "왼팔 ≈ 1/6");
            Assert.That(counts[(int)BodyPart.RightArm], Is.InRange(n * 0.14f, n * 0.19f), "오른팔 ≈ 1/6");
        }

        [Test]
        public void StrikePart_CustomWeights_FollowsWeights_ZeroNeverPicked()
        {
            AiConfigSO config = M3TestData.AiConfig();
            config.meleePartWeightThorax = 0f;
            config.meleePartWeightStomach = 3f;
            config.meleePartWeightLeftArm = -1f;          // 음수 = 0
            config.meleePartWeightRightArm = 1f;
            var random = new System.Random(11);
            int stomach = 0, right = 0;
            const int n = 4000;
            for (int k = 0; k < n; k++)
            {
                BodyPart p = EnemyMelee.PickStrikePart(random, config);
                Assert.That(p, Is.EqualTo(BodyPart.Stomach).Or.EqualTo(BodyPart.RightArm), $"가중치 0 부위가 나옴: {p}");
                if (p == BodyPart.Stomach) stomach++; else right++;
            }
            Assert.That(stomach, Is.InRange(n * 0.71f, n * 0.79f), "복부 3/4");
            Assert.That(right, Is.InRange(n * 0.21f, n * 0.29f), "오른팔 1/4");
        }

        [Test]
        public void StrikePart_AllZeroOrNoConfig_Thorax_NoRandomUse()
        {
            AiConfigSO config = M3TestData.AiConfig();
            config.meleePartWeightThorax = 0f;
            config.meleePartWeightStomach = 0f;
            config.meleePartWeightLeftArm = float.NaN;
            config.meleePartWeightRightArm = 0f;
            var a = new System.Random(3);
            var b = new System.Random(3);
            Assert.AreEqual(BodyPart.Thorax, EnemyMelee.PickStrikePart(a, config));
            Assert.AreEqual(BodyPart.Thorax, EnemyMelee.PickStrikePart(a, null));
            Assert.AreEqual(BodyPart.Thorax, EnemyMelee.PickStrikePart(null, config));
            Assert.AreEqual(b.Next(), a.Next(), "난수를 쓰지 않았다");
        }

        [Test]
        public void StrikePart_SeedReproducible_NoAllocation()
        {
            AiConfigSO config = M3TestData.AiConfig();
            for (int seed = 1; seed <= 20; seed++)
                Assert.AreEqual(EnemyMelee.PickStrikePart(new System.Random(seed), config),
                    EnemyMelee.PickStrikePart(new System.Random(seed), config));
            var random = new System.Random(3);
            AiTest.AssertNoAlloc(() =>
            {
                for (int k = 0; k < 100; k++)
                    EnemyMelee.PickStrikePart(random, config);
            });
        }

        // ── 사거리 (수평 + 높이 차 상한, 7B 리뷰 5) ──

        [Test]
        public void InReach_HorizontalRangeAndHeightLimit()
        {
            Vector3 self = Vector3.zero;
            Assert.IsTrue(EnemyMelee.InReach(self, new Vector3(1.8f, 0f, 0f), 1.8f, 1.2f), "경계 = 사거리 안");
            Assert.IsFalse(EnemyMelee.InReach(self, new Vector3(1.0f, 0f, 1.6f), 1.8f, 1.2f), "수평 1.89m = 밖");
            Assert.IsTrue(EnemyMelee.InReach(self, new Vector3(1f, 1.2f, 0f), 1.8f, 1.2f), "높이 차 = 상한");
            Assert.IsFalse(EnemyMelee.InReach(self, new Vector3(1f, 2.5f, 0f), 1.8f, 1.2f), "지붕 위 표적 = 밖");
            Assert.IsFalse(EnemyMelee.InReach(self, new Vector3(1f, -1.5f, 0f), 1.8f, 1.2f), "아래 표적 = 밖");
            Assert.IsTrue(EnemyMelee.InReach(self, new Vector3(1f, 5f, 0f), 1.8f, 0f), "상한 0 = 높이 무제한");
            Assert.IsTrue(EnemyMelee.InReach(self, new Vector3(1f, 5f, 0f), 1.8f, float.NaN), "NaN = 높이 무제한");
            Assert.IsFalse(EnemyMelee.InReach(self, new Vector3(float.NaN, 0f, 0f), 1.8f, 1.2f), "NaN 위치 = 밖");
        }
    }
}
