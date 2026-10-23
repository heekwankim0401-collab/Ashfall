using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // 방사능 구역 (§4.3 STAT-RAD, §8.1 정유소, M7 설계 §1.5·§3.10·§4.6, Q39~Q41). 레이드 씬 최상위 "RadiationField"에 하나. 정적 인스턴스 없음.
    // - OnEnable: IRadiationField 등록 / OnDisable: 해제. 레이드 밖(은신처·메인 메뉴)에는 없다 = 소비자는 구역 방사능 0.
    // - 구역 = RaidConfigSO.radiationZones (직렬화 config, 비었으면 raid의 config). 률 계산은 순수 RadiationZoneMath(할당 없음).
    // - 폭풍 방사능은 포함하지 않는다 — 합은 소비자(Player 선량 = 방독면이 폭풍 몫만 막음, Audio 가이거 = 환경 률)가 한다.
    // 표시(Toxic 드럼통·경고 기둥, 콜라이더 없음)는 EditorTools M7HazardSceneApply가 "M7_ToxicMarkers"에 따로 놓는다 — 이 컴포넌트와 무관.
    public sealed class RadiationField : MonoBehaviour, IRadiationField
    {
        [Tooltip("radiationZones를 읽을 레이드 설정. 비우면 raid.Config")]
        [SerializeField] private RaidConfigSO config;
        [Tooltip("config가 비었을 때만 쓴다")]
        [SerializeField] private RaidController raid;

        private bool registered;

        // 테스트·디버그
        public RaidConfigSO Config => config != null ? config : (raid != null ? raid.Config : null);
        public IReadOnlyList<RadiationZoneConfig> Zones => Config != null ? Config.radiationZones : null;
        public bool IsRegistered => registered;

        private void Awake()
        {
            if (Config == null)
                Debug.LogWarning("[RadiationField] RaidConfigSO가 연결되지 않았다 — 구역 방사능 없음.", this);
        }

        private void OnEnable()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null)
                return;
            gm.Services.Register<IRadiationField>(this);
            registered = true;
        }

        private void OnDisable()
        {
            registered = false;
            GameManager gm = GameManager.Instance;
            if (gm == null)
                return;
            gm.Services.Unregister<IRadiationField>(this);
        }

        // ── IRadiationField ── (매 틱 동기 호출, 할당 없음)
        public float ZoneRatePerSec(Vector3 position)
        {
            RaidConfigSO c = Config;
            return c != null ? RadiationZoneMath.RateAt(c.radiationZones, position) : 0f;
        }
    }
}
