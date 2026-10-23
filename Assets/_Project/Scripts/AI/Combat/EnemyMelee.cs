using Ashfall.Core;
using UnityEngine;

namespace Ashfall.AI
{
    // AI-MUT 근접 공격 규칙 (§7, M7 설계 §1.4, Q24). 순수 C#: 사거리·시야 판정 결과를 받아 준비 → 타격/헛침 → 간격을 센다.
    // EnemyAgent가 매 프레임 Tick을 부르고 결과(MeleeEvent)를 적용한다(Strike = IDamageable.ApplyDamage, WindupStarted = 울음).
    // - 사거리 안 ∧ 시야 ∧ 간격이 지났으면 준비 시작(WindupStarted). 준비 WindupSec 뒤 첫 Tick에서
    //   사거리 안 ∧ 시야면 Strike, 아니면 Whiff(피해 없음). 둘 다 한 번만 돌려준다.
    // - 간격: 준비 시작 시각부터 IntervalSec(준비 시간 포함) 동안 다음 준비를 시작하지 않는다
    //   → 계속 붙어 있으면 타격 사이 = IntervalSec("다음 타격까지 1.1초"). 헛침도 같은 간격을 쓴다(쿨다운 소모).
    //   TODO(GDD-?) Q24: 간격 기준을 "준비 시작"으로 해석. IntervalSec < WindupSec이면 주기 = WindupSec.
    // - 맞을 부위(7B 리뷰 1): 지점이 아니라 부위를 직접 고른다(PickStrikePart, AiConfigSO 가중치) → DamageInfo 부위 힌트.
    //   플레이어 자세(선·웅크림·엎드림)·캡슐 크기와 무관하게 흉부·복부·팔만 맞는다.
    // Tick·PickStrikePart는 할당 없음.
    public sealed class EnemyMelee
    {
        private float windupEndsAt;
        private float nextWindupAt = float.NegativeInfinity;

        public EnemyMelee(float windupSec, float intervalSec)
        {
            WindupSec = Mathf.Max(0f, windupSec);
            IntervalSec = Mathf.Max(0f, intervalSec);
        }

        public float WindupSec { get; }
        public float IntervalSec { get; }

        // 준비 중인가 (테스트·디버그용)
        public bool IsWindingUp { get; private set; }

        // 다음 준비를 시작할 수 있는 시각 (테스트·디버그용)
        public float NextWindupAt => nextWindupAt;

        // inRange = 의도(EnemyIntent.Melee)가 있고 표적이 meleeRangeM 안, hasLos = 마지막 감각 갱신에서 표적이 보였음.
        // 할당 없음.
        public MeleeEvent Tick(float now, bool inRange, bool hasLos)
        {
            bool canHit = inRange && hasLos;
            if (IsWindingUp)
            {
                if (now < windupEndsAt)
                    return MeleeEvent.None;
                IsWindingUp = false;
                return canHit ? MeleeEvent.Strike : MeleeEvent.Whiff;
            }

            if (!canHit || now < nextWindupAt)
                return MeleeEvent.None;

            IsWindingUp = true;
            windupEndsAt = now + WindupSec;
            nextWindupAt = now + Mathf.Max(IntervalSec, WindupSec);
            return MeleeEvent.WindupStarted;
        }

        // 사망 등: 준비를 버리고 간격도 처음으로
        public void Reset()
        {
            IsWindingUp = false;
            nextWindupAt = float.NegativeInfinity;
        }

        // 맞을 부위 = 흉부·복부·왼팔·오른팔 중 AiConfigSO 가중치(meleePartWeight*)로 하나. 난수 1회.
        // 음수·NaN 가중치 = 0. 합이 0 이하이거나 config·random이 없으면 흉부(난수 안 씀). 머리·다리는 나오지 않는다(Q24).
        public static BodyPart PickStrikePart(System.Random random, AiConfigSO config)
        {
            if (config == null || random == null)
                return BodyPart.Thorax;
            float thorax = Weight(config.meleePartWeightThorax);
            float stomach = Weight(config.meleePartWeightStomach);
            float leftArm = Weight(config.meleePartWeightLeftArm);
            float rightArm = Weight(config.meleePartWeightRightArm);
            float total = thorax + stomach + leftArm + rightArm;
            if (!(total > 0f))
                return BodyPart.Thorax;

            float roll = (float)random.NextDouble() * total;
            if (roll < thorax)
                return BodyPart.Thorax;
            roll -= thorax;
            if (roll < stomach)
                return BodyPart.Stomach;
            roll -= stomach;
            if (roll < leftArm)
                return BodyPart.LeftArm;
            return rightArm > 0f ? BodyPart.RightArm : (leftArm > 0f ? BodyPart.LeftArm : (stomach > 0f ? BodyPart.Stomach : BodyPart.Thorax));
        }

        private static float Weight(float w) => w > 0f ? w : 0f;

        // 근접 사거리 (리뷰 5): 수평 거리 ≤ rangeM ∧ 발 높이 차 |Δy| ≤ maxHeightDiffM. maxHeightDiffM ≤ 0·NaN = 높이 무제한(기존 동작).
        public static bool InReach(Vector3 self, Vector3 target, float rangeM, float maxHeightDiffM)
        {
            float dx = target.x - self.x, dz = target.z - self.z;
            if (!(dx * dx + dz * dz <= rangeM * rangeM))
                return false;
            return !(maxHeightDiffM > 0f) || Mathf.Abs(target.y - self.y) <= maxHeightDiffM;
        }
    }
}
