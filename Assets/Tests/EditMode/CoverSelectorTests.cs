using Ashfall.AI;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 엄폐 선택 (§7 CoverPoint, M3 설계 §1.7, M3 AC2)
    public class CoverSelectorTests
    {
        private AiConfigSO config;
        private CoverIndex index;
        private int[] ids;
        private float[] scores;

        // 위협(플레이어 눈)은 북쪽 30m. 자신은 원점.
        private static readonly Vector3 Threat = new Vector3(0f, 1.6f, 30f);
        private static readonly Vector2 Preferred = new Vector2(15f, 40f);

        // 위협 → 웅크린 눈(높이 < 1.2)은 막히고, 서서 쏘는 눈(1.6) ↔ 위협은 뚫리는 낮은 벽
        private static readonly System.Func<Vector3, Vector3, bool> LowWallRule =
            (from, to) => Mathf.Min(from.y, to.y) < 1.2f;

        [SetUp]
        public void SetUp()
        {
            config = M3TestData.AiConfig();
            index = new CoverIndex(16);
            ids = new int[16];
            scores = new float[16];
        }

        private CoverQuery Query(int agent = 1) => new CoverQuery
        {
            AgentId = agent, Self = Vector3.zero, ThreatEye = Threat, PreferredRangeM = Preferred,
        };

        private bool Select(ILineOfSight los, out CoverSpot spot, CoverQuery? q = null) =>
            CoverSelector.TrySelect(index, q ?? Query(), config, los, null, null, ids, scores, out spot);

        [Test]
        public void Index_AddReserveRelease()
        {
            int a = index.Add(new Vector3(1f, 0f, 1f), new Vector3(0f, 0.5f, 2f), CoverHeight.Low, Vector3.one * 9f);
            int b = index.Add(new Vector3(2f, 0f, 1f), Vector3.forward, CoverHeight.High, new Vector3(3f, 0f, 1f));
            Assert.AreEqual(2, index.Count);
            Assert.AreEqual(Vector3.forward, index.Get(a).Forward, "수평 정규화");
            Assert.AreEqual(index.Get(a).Position, index.Get(a).PeekPosition, "Low는 숨는 위치에서 사격");
            Assert.AreEqual(new Vector3(3f, 0f, 1f), index.Get(b).PeekPosition);

            Assert.IsTrue(index.TryReserve(a, 7));
            Assert.IsFalse(index.TryReserve(a, 8), "한 지점 한 명");
            Assert.IsTrue(index.TryReserve(b, 7), "한 명은 한 지점: 이전 예약은 풀림");
            Assert.AreEqual(-1, index.OccupantOf(a));
            Assert.AreEqual(b, index.ReservedBy(7));
            index.ReleaseAll(7);
            Assert.AreEqual(-1, index.ReservedBy(7));
        }

        [Test]
        public void ValidCover_SelectedAndReserved()
        {
            int id = index.Add(new Vector3(0f, 0f, 5f), Vector3.forward, CoverHeight.Low, Vector3.zero);
            Assert.IsTrue(Select(new FakeLineOfSight(LowWallRule), out CoverSpot spot));
            Assert.AreEqual(id, spot.Id);
            Assert.AreEqual(1, index.OccupantOf(id));
        }

        [Test]
        public void OutsideProtectionAngle_Excluded()
        {
            // 위협은 +z. 보호각 70° 밖(80°)·반대쪽은 제외, 60°는 허용
            index.Add(new Vector3(0f, 0f, 4f), Vector3.back, CoverHeight.Low, Vector3.zero);
            index.Add(new Vector3(1f, 0f, 4f), Quaternion.Euler(0f, 80f, 0f) * Vector3.forward, CoverHeight.Low, Vector3.zero);
            Assert.IsFalse(Select(new FakeLineOfSight(LowWallRule), out _));

            int ok = index.Add(new Vector3(2f, 0f, 6f), Quaternion.Euler(0f, 60f, 0f) * Vector3.forward, CoverHeight.Low, Vector3.zero);
            Assert.IsTrue(Select(new FakeLineOfSight(LowWallRule), out CoverSpot spot));
            Assert.AreEqual(ok, spot.Id);
        }

        [Test]
        public void TooFar_Excluded()
        {
            index.Add(new Vector3(0f, 0f, 26f), Vector3.forward, CoverHeight.Low, Vector3.zero);   // 25m 밖
            Assert.IsFalse(Select(new FakeLineOfSight(LowWallRule), out _));
        }

        [Test]
        public void ReservedByOther_Excluded_OwnAllowed()
        {
            int id = index.Add(new Vector3(0f, 0f, 5f), Vector3.forward, CoverHeight.Low, Vector3.zero);
            index.TryReserve(id, 99);
            Assert.IsFalse(Select(new FakeLineOfSight(LowWallRule), out _));

            index.ReleaseAll(99);
            index.TryReserve(id, 1);
            Assert.IsTrue(Select(new FakeLineOfSight(LowWallRule), out CoverSpot spot), "자기 예약은 허용");
            Assert.AreEqual(id, spot.Id);
        }

        [Test]
        public void HideNotBlocked_Excluded()
        {
            // (a) 위협 눈 → 숨는 눈이 뚫림 = 숨을 수 없음
            index.Add(new Vector3(0f, 0f, 5f), Vector3.forward, CoverHeight.Low, Vector3.zero);
            Assert.IsFalse(Select(new FakeLineOfSight(), out _));
        }

        [Test]
        public void CannotShoot_Excluded()
        {
            // (b) 사격 위치 → 위협 눈이 막힘 = 쏠 수 없음
            index.Add(new Vector3(0f, 0f, 5f), Vector3.forward, CoverHeight.Low, Vector3.zero);
            Assert.IsFalse(Select(new FakeLineOfSight((from, to) => true), out _));
        }

        [Test]
        public void HighCover_UsesStandingEyeToHide_PeekToShoot()
        {
            var hide = new Vector3(0f, 0f, 5f);
            var peek = new Vector3(1.9f, 0f, 5f);
            int id = index.Add(hide, Vector3.forward, CoverHeight.High, peek);
            // 높은 벽: x < 1(벽 뒤)로 가는/오는 레이는 높이와 상관없이 막힘
            var los = new FakeLineOfSight((from, to) => Mathf.Abs(from.x) < 1f && Mathf.Abs(to.x) < 1f ? true
                : (Mathf.Abs(from.x) < 1f || Mathf.Abs(to.x) < 1f) && Mathf.Abs(from.x - to.x) < 0.5f);
            Assert.IsTrue(Select(los, out CoverSpot spot));
            Assert.AreEqual(id, spot.Id);
        }

        [Test]
        public void Score_PrefersNearerAndPreferredRange()
        {
            int far = index.Add(new Vector3(0f, 0f, 12f), Vector3.forward, CoverHeight.Low, Vector3.zero);
            int near = index.Add(new Vector3(0f, 0f, 3f), Vector3.forward, CoverHeight.Low, Vector3.zero);
            Assert.IsTrue(Select(new FakeLineOfSight(LowWallRule), out CoverSpot spot));
            Assert.AreEqual(near, spot.Id, "가까운 지점 (점수 낮음)");
            index.ReleaseAll(1);

            // 같은 거리(5m)면 위협 거리가 선호 중앙(27.5)에 가까운 쪽 (다가감 벌점은 빼고 비교)
            config.coverAdvancePenalty = 0f;
            var index2 = new CoverIndex(4);
            int behind = index2.Add(new Vector3(0f, 0f, -5f), Vector3.forward, CoverHeight.Low, Vector3.zero);   // 위협 35 → 7.5
            int ahead = index2.Add(new Vector3(0f, 0f, 5f), Vector3.forward, CoverHeight.Low, Vector3.zero);     // 위협 25 → 2.5
            Assert.IsTrue(CoverSelector.TrySelect(index2, Query(), config, new FakeLineOfSight(LowWallRule), null, null,
                ids, scores, out CoverSpot s2));
            Assert.AreEqual(ahead, s2.Id);
            Assert.AreNotEqual(behind, s2.Id);
        }

        [Test]
        public void AdvancePenalty_AppliedTowardThreat()
        {
            // 자기→지점 거리는 같고(5), 위협 거리 차도 같게: 한쪽은 위협에 다가감(+5 벌점)
            int back = index.Add(new Vector3(0f, 0f, -5f), Vector3.forward, CoverHeight.Low, Vector3.zero);    // 위협 35 (|35−27.5|=7.5)
            int fwd = index.Add(new Vector3(0f, 0f, 5f), Vector3.forward, CoverHeight.Low, Vector3.zero);      // 위협 25 (|25−27.5|=2.5) +5
            Assert.IsTrue(Select(new FakeLineOfSight(LowWallRule), out CoverSpot spot));
            // back: 5 + 3.75 = 8.75, fwd: 5 + 1.25 + 5 = 11.25
            Assert.AreEqual(back, spot.Id);
            Assert.AreNotEqual(fwd, spot.Id);
        }

        [Test]
        public void OnlyTopCandidatesAreRaycast()
        {
            for (int i = 0; i < 6; i++)
                index.Add(new Vector3(i - 3f, 0f, 4f), Vector3.forward, CoverHeight.Low, Vector3.zero);
            var los = new FakeLineOfSight();   // 아무것도 안 막음 → (a)에서 전부 탈락
            Assert.IsFalse(Select(los, out _));
            Assert.AreEqual(3, los.IsBlockedCalls, "점수 상위 coverLosCandidates(3)개만 레이 확인");

            var allBlocked = new FakeLineOfSight((f, t) => true);   // (a) 통과, (b) 탈락 → 후보당 2레이
            Assert.IsFalse(Select(allBlocked, out _));
            Assert.AreEqual(6, allBlocked.IsBlockedCalls);
        }

        [Test]
        public void Flank_AngleBetween45And120()
        {
            // 위협 (0,·,20), 우회 전 자기 위치 원점(위협에서 남쪽)
            var threat = new Vector3(0f, 1.6f, 20f);
            var q = new CoverQuery
            {
                AgentId = 1, Self = Vector3.zero, ThreatEye = threat, PreferredRangeM = Preferred,
                Flank = true, FlankFrom = Vector3.zero,
            };
            // 0°: 같은 방향(제외)
            index.Add(new Vector3(0f, 0f, 10f), Vector3.forward, CoverHeight.Low, Vector3.zero);
            // ≈127°: 너무 돌아감(제외)
            index.Add(new Vector3(-8f, 0f, 26f), new Vector3(8f, 0f, -6f), CoverHeight.Low, Vector3.zero);
            Assert.IsFalse(Select(new FakeLineOfSight(AnchorRule), out _, q));

            // 90°: 허용
            int side = index.Add(new Vector3(15f, 0f, 20f), Vector3.left, CoverHeight.Low, Vector3.zero);
            Assert.IsTrue(Select(new FakeLineOfSight(AnchorRule), out CoverSpot spot, q));
            Assert.AreEqual(side, spot.Id);
        }

        [Test]
        public void EmptyIndex_ReturnsFalse()
        {
            Assert.IsFalse(Select(new FakeLineOfSight(), out CoverSpot spot));
            Assert.AreEqual(0, spot.Id);
        }

        // 낮은 벽 + x = 3 근처(High 지점의 숨는 눈)는 막힘
        private static readonly System.Func<Vector3, Vector3, bool> AnchorRule =
            (from, to) => Mathf.Min(from.y, to.y) < 1.2f || Mathf.Abs(from.x - 3f) < 0.5f || Mathf.Abs(to.x - 3f) < 0.5f;

        // M7 7B 리뷰 2 (Q29): 거점 질의 — 숨는 자리나 내다보는 자리가 반경 밖이면 점수가 더 좋아도 고르지 않는다. 거점 없으면 기존대로.
        [Test]
        public void Anchor_CandidatesOutsideRadius_Excluded()
        {
            int outside = index.Add(new Vector3(0f, 0f, 5f), Vector3.forward, CoverHeight.Low, Vector3.zero);       // 더 가까움(점수 좋음), 거점 밖
            int peekOut = index.Add(new Vector3(3f, 0f, 1f), Vector3.forward, CoverHeight.High, new Vector3(-2f, 0f, 1f)); // 숨는 자리 안(가장 가까움), 내다보는 자리 밖
            int inside = index.Add(new Vector3(6f, 0f, 2f), Vector3.forward, CoverHeight.Low, Vector3.zero);        // 거점 안
            CoverQuery q = Query();
            q.HasAnchor = true;
            q.AnchorCenter = new Vector3(5f, 0f, 0f);
            q.AnchorRadiusM = 3.5f;
            Assert.IsTrue(Select(new FakeLineOfSight(AnchorRule), out CoverSpot spot, q));
            Assert.AreEqual(inside, spot.Id, "반경 안 지점만");
            Assert.AreEqual(-1, index.OccupantOf(outside));
            Assert.AreEqual(-1, index.OccupantOf(peekOut));

            // 반경 안 후보가 없으면 실패(None) → 대체 행동
            index.ReleaseAll(1);
            q.AnchorCenter = new Vector3(40f, 0f, 0f);
            Assert.IsFalse(Select(new FakeLineOfSight(AnchorRule), out _, q));

            // 거점 없음 = 기존 선택(가장 가까운 지점)
            Assert.IsTrue(Select(new FakeLineOfSight(AnchorRule), out CoverSpot free));
            Assert.AreEqual(peekOut, free.Id, "거점 없으면 가장 가까운 High 지점");
            Assert.AreNotEqual(inside, free.Id);
        }
    }
}
