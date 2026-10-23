using UnityEngine;

namespace Ashfall.AI
{
    // 엄폐·측면 우회 지점 선택 (M3 설계 §1.7). 할당 없음(호출 측 스크래치 배열).
    // 1. 거리: 자신에서 coverSearchRadiusM(우회는 flankSearchRadiusM) 안, 비예약(자기 예약 허용), 제외 지점(query.ExcludedCoverId) 아님
    // 2. 보호각: forward와 (위협 − 지점) 수평 각 ≤ coverProtectionHalfAngleDeg
    // 3·4. 점수 = 자기→지점 거리 × coverDistanceWeight + |위협 거리 − 선호 중앙| × coverRangeWeight
    //      (+ 자기보다 위협에 가까워지는 지점 coverAdvancePenalty). 낮을수록 좋음.
    // 5. 점수 상위 coverLosCandidates개만 레이 확인: (a) 위협 눈 → 숨는 눈이 막힘, (b) 사격 눈 → 위협 눈이 뚫림.
    // 우회(query.Flank): 표적에서 본 (FlankFrom 방향)과 (후보 방향) 사이 각이 flankMinAngleDeg~flankMaxAngleDeg.
    // 거점(query.HasAnchor, M7 Q29): 숨는 자리·내다보는 자리 중 하나라도 거점 반경 밖이면 후보에서 뺀다.
    public static class CoverSelector
    {
        public static bool TrySelect(CoverIndex index, in CoverQuery query, AiConfigSO config, ILineOfSight los,
                                     Transform selfRoot, Transform targetRoot, int[] scratchIds, float[] scratchScores,
                                     out CoverSpot spot)
        {
            spot = default;
            if (index == null || config == null || scratchIds == null || scratchScores == null)
                return false;

            int keep = Mathf.Min(config.coverLosCandidates, Mathf.Min(scratchIds.Length, scratchScores.Length));
            if (keep <= 0)
                return false;

            Vector3 threat = query.ThreatEye;
            Vector3 threatFlat = new Vector3(threat.x, 0f, threat.z);
            Vector3 selfFlat = new Vector3(query.Self.x, 0f, query.Self.z);
            float selfToThreat = Vector3.Distance(selfFlat, threatFlat);
            float radius = query.Flank ? config.flankSearchRadiusM : config.coverSearchRadiusM;
            float preferredCenter = (query.PreferredRangeM.x + query.PreferredRangeM.y) * 0.5f;
            bool anchored = query.HasAnchor && query.AnchorRadiusM > 0f;

            Vector3 flankFromDir = Vector3.zero;
            if (query.Flank)
            {
                flankFromDir = new Vector3(query.FlankFrom.x - threat.x, 0f, query.FlankFrom.z - threat.z);
                if (flankFromDir.sqrMagnitude < 1e-6f)
                    return false;
            }

            int count = 0;
            for (int id = 0; id < index.Count; id++)
            {
                int occupant = index.OccupantOf(id);
                if (occupant != -1 && occupant != query.AgentId)
                    continue;
                if (query.HasExcludedCover && id == query.ExcludedCoverId)
                    continue;

                CoverSpot s = index.Get(id);
                Vector3 posFlat = new Vector3(s.Position.x, 0f, s.Position.z);
                float selfDistance = Vector3.Distance(selfFlat, posFlat);
                if (selfDistance > radius)
                    continue;
                if (anchored && (OutsideAnchor(s.Position, query) || OutsideAnchor(s.PeekPosition, query)))
                    continue;

                Vector3 toThreat = threatFlat - posFlat;
                float threatDistance = toThreat.magnitude;
                if (threatDistance < 1e-3f)
                    continue;
                if (query.MaxThreatDistanceM > 0f && threatDistance > query.MaxThreatDistanceM)
                    continue;
                if (Vector3.Angle(s.Forward, toThreat) > config.coverProtectionHalfAngleDeg)
                    continue;

                if (query.Flank)
                {
                    Vector3 candidateDir = posFlat - threatFlat;
                    float flankAngle = Vector3.Angle(flankFromDir, candidateDir);
                    if (flankAngle < config.flankMinAngleDeg || flankAngle > config.flankMaxAngleDeg)
                        continue;
                }

                float score = selfDistance * config.coverDistanceWeight
                              + Mathf.Abs(threatDistance - preferredCenter) * config.coverRangeWeight;
                if (threatDistance < selfToThreat)
                    score += config.coverAdvancePenalty;

                count = InsertTopK(scratchIds, scratchScores, count, keep, id, score);
            }

            for (int k = 0; k < count; k++)
            {
                CoverSpot s = index.Get(scratchIds[k]);
                float hideEyeHeight = s.Height == CoverHeight.Low ? config.crouchEyeHeightM : config.standEyeHeightM;
                Vector3 hideEye = s.Position + Vector3.up * hideEyeHeight;
                // (a) 위협 눈에서 숨는 눈이 가려져야 한다
                if (los != null && !los.IsBlocked(threat, hideEye, selfRoot, targetRoot))
                    continue;
                // (b) 사격 위치에서 위협 눈이 보여야 한다
                Vector3 fireEye = (s.Height == CoverHeight.Low ? s.Position : s.PeekPosition) + Vector3.up * config.standEyeHeightM;
                if (los != null && los.IsBlocked(fireEye, threat, selfRoot, targetRoot))
                    continue;

                if (!index.TryReserve(s.Id, query.AgentId))
                    continue;
                spot = s;
                return true;
            }
            return false;
        }

        private static bool OutsideAnchor(Vector3 point, in CoverQuery query)
        {
            float dx = point.x - query.AnchorCenter.x, dz = point.z - query.AnchorCenter.z;
            return dx * dx + dz * dz > query.AnchorRadiusM * query.AnchorRadiusM;
        }

        // 점수 오름차순 상위 keep개만 유지하는 삽입. 새 개수 반환.
        private static int InsertTopK(int[] ids, float[] scores, int count, int keep, int id, float score)
        {
            if (count == keep && score >= scores[count - 1])
                return count;

            int pos = count < keep ? count : keep - 1;
            while (pos > 0 && scores[pos - 1] > score)
            {
                ids[pos] = ids[pos - 1];
                scores[pos] = scores[pos - 1];
                pos--;
            }
            ids[pos] = id;
            scores[pos] = score;
            return count < keep ? count + 1 : count;
        }
    }
}
