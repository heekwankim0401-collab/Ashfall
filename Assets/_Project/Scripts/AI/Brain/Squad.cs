using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.AI
{
    // 분대 (M3 설계 §1.6.3). squadId가 같은 적끼리 묶는다(AiDirector). 할당은 Join 때만.
    // - 리더 = 순서 키(씬 계층 순서)가 가장 작은 분대원, 탈퇴(사망·파괴) 시 다음 생존자
    // - 대형: 분대원 i(1부터)는 리더 뒤 (±spacing, 0, -spacing × 줄) — 홀수 왼쪽, 짝수 오른쪽
    // - 정보 공유: ReportContact → 거리 ≤ squadShareRangeM인 다른 분대원에게 OnSquadContact.
    //   같은 보고자는 squadReportMinIntervalSec 간격으로만 보고한다.
    // - 측면 우회권: 동시에 한 명, 반납 후 flankCooldownSec 동안 다시 안 줌
    // - M7 경계 울음권: 분대에서 한 명만, vocalCooldownSec 간격 (TryClaimAlertVocal)
    public sealed class Squad
    {
        private readonly AiConfigSO config;
        private readonly List<EnemyBrain> members = new List<EnemyBrain>(4);
        private readonly List<float> lastReportTime = new List<float>(4);
        private readonly List<int> joinOrder = new List<int>(4);
        private float flankAvailableAt = float.NegativeInfinity;
        private float alertVocalAvailableAt = float.NegativeInfinity;

        public Squad(int id, AiConfigSO config)
        {
            Id = id;
            this.config = config;
        }

        public int Id { get; }
        public int LeaderId => members.Count > 0 ? members[0].AgentId : -1;
        public int Count => members.Count;
        public int FlankHolder { get; private set; } = -1;

        internal EnemyBrain Leader => members.Count > 0 ? members[0] : null;
        internal IReadOnlyList<EnemyBrain> Members => members;

        // order가 작을수록 앞(0번 = 리더). 같으면 먼저 들어온 쪽이 앞. 생략하면 들어온 순서 그대로.
        // 등록(Start) 순서는 실행마다 다를 수 있어 EnemyAgent는 씬 계층 순서를 넘긴다(리뷰 12).
        public void Join(EnemyBrain brain, int order = 0)
        {
            if (brain == null || members.Contains(brain))
                return;
            int index = members.Count;
            while (index > 0 && joinOrder[index - 1] > order)
                index--;
            members.Insert(index, brain);
            lastReportTime.Insert(index, float.NegativeInfinity);
            joinOrder.Insert(index, order);
        }

        // 리더면 다음 생존자가 리더가 된다(목록 순서 유지).
        public void Leave(EnemyBrain brain)
        {
            int index = members.IndexOf(brain);
            if (index < 0)
                return;
            members.RemoveAt(index);
            lastReportTime.RemoveAt(index);
            joinOrder.RemoveAt(index);
            if (FlankHolder == brain.AgentId)
                FlankHolder = -1;
        }

        // 대형 슬롯 (리더 0). 없으면 -1.
        public int IndexOf(int agentId)
        {
            for (int i = 0; i < members.Count; i++)
                if (members[i].AgentId == agentId)
                    return i;
            return -1;
        }

        public Vector3 FormationSlot(int index, Vector3 leaderPos, Vector3 leaderForward)
        {
            if (index <= 0)
                return leaderPos;
            Vector3 forward = new Vector3(leaderForward.x, 0f, leaderForward.z);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);

            int row = (index + 1) / 2;
            float side = (index % 2 == 1) ? -1f : 1f;
            float spacing = config.formationSpacingM;
            return leaderPos + right * (side * spacing) - forward * (spacing * row);
        }

        public void ReportContact(int reporterId, Vector3 reporterPos, Vector3 contactPos, float now)
        {
            int reporter = IndexOf(reporterId);
            if (reporter >= 0)
            {
                if (now - lastReportTime[reporter] < config.squadReportMinIntervalSec)
                    return;
                lastReportTime[reporter] = now;
            }

            float rangeSqr = config.squadShareRangeM * config.squadShareRangeM;
            for (int i = 0; i < members.Count; i++)
            {
                EnemyBrain member = members[i];
                if (member.AgentId == reporterId || member.State == EnemyState.Dead)
                    continue;
                if ((member.Position - reporterPos).sqrMagnitude > rangeSqr)
                    continue;
                member.OnSquadContact(contactPos, now);
            }
        }

        public bool TryClaimFlank(int agentId, float now)
        {
            if (FlankHolder == agentId)
                return true;
            if (FlankHolder != -1 || now < flankAvailableAt)
                return false;
            FlankHolder = agentId;
            return true;
        }

        public void ReleaseFlank(int agentId, float now)
        {
            if (FlankHolder != agentId)
                return;
            FlankHolder = -1;
            flankAvailableAt = now + config.flankCooldownSec;
        }

        // 분대 경계 울음 (M7 7B 리뷰 10, Q26): 분대가 함께 경계에 드는 순간 첫 대원만 운다(무리 4명 동시 재생 방지).
        // 한 번 주면 cooldownSec 동안 다른 대원에게 주지 않는다. TODO(GDD-?) Q26.
        public bool TryClaimAlertVocal(float now, float cooldownSec)
        {
            if (now < alertVocalAvailableAt)
                return false;
            alertVocalAvailableAt = now + Mathf.Max(0f, cooldownSec);
            return true;
        }
    }
}
