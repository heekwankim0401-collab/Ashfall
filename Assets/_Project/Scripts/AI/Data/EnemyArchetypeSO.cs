using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.AI
{
    // 적 무장 하나 (M3 설계 D7). §14의 weapon 단일 필드 대신 무기·탄·탄창·가중치 목록으로 쓴다.
    [Serializable]
    public struct EnemyLoadout
    {
        public WeaponDefinitionSO weapon;
        public AmmoDefinitionSO ammo;
        public MagazineDefinitionSO magazine;
        [Tooltip("스폰 시 고를 상대 가중치")]
        public float weight;
    }

    // §14 EnemyArchetypeSO (SYS-AI-*, AI-SCV·AI-RDR). 에셋: Data/AI/AI-SCV.asset, AI-RDR.asset.
    // 유형마다 값이 달라 필드 초기값을 두지 않는다. 값은 M3 데이터 메뉴가 채운다 (M3 설계 §4.1).
    // ★ = §14에 없는 필드 (CHANGELOG_GDD "M3 설계 결정"). 출처 없는 수치는 전부 TODO(GDD-?) 임시값.
    [CreateAssetMenu(menuName = "Ashfall/Enemy Archetype", fileName = "EnemyArchetype")]
    public class EnemyArchetypeSO : ScriptableObject
    {
        [Header("§14 기본")]
        public string id;
        [Tooltip("★ 표시 이름 (§7 유형 이름)")]
        public string displayName;
        [Tooltip("★ 시체 루트 컨테이너 이름. TODO(GDD-?)")]
        public string corpseDisplayName;

        [Header("§7 3부위 체력 (§4.2 축약)")]
        [Tooltip("몸통 HP (§14 hp를 몸통으로 해석, §4.2 흉부 기준)")]
        public float hp;
        [Tooltip("★ 머리 HP (§4.2 머리)")]
        public float headHp;
        [Tooltip("★ 다리 HP (§4.2 다리 하나 값, 두 다리를 하나로 묶음)")]
        public float legsHp;

        [Header("§7 감각")]
        [Tooltip("시야 거리 (m)")]
        public float sightRange;
        [Tooltip("시야 콘 전체 각 (도). 반각 = 이 값의 절반")]
        public float sightAngle;
        [Tooltip("★ 소음 반경 배율 (§4.1 반경 × 이 값 안이면 듣는다)")]
        public float hearingMultiplier;

        [Header("§14 루트·무장")]
        public LootTableSO loot;
        [Tooltip("★ 무장 후보 (§14 weapon 대체). 가중치로 하나를 고른다")]
        public List<EnemyLoadout> loadouts = new List<EnemyLoadout>();
        [Tooltip("★ 예비 탄창 수. TODO(GDD-?)")]
        public int spareMagazines;

        [Header("★ 이동 (m/s, TODO(GDD-?))")]
        public float patrolSpeed;
        public float moveSpeed;
        public float sprintSpeed;

        [Header("★ 반응·명중 (TODO(GDD-?))")]
        [Tooltip("표적을 본 뒤 교전까지 걸리는 시간 (초)")]
        public float reactionTimeSec;
        [Tooltip("명중 오차 원판 기본 반지름 (m)")]
        public float aimErrorBaseM;
        [Tooltip("거리 1m당 더하는 오차 반지름 (m)")]
        public float aimErrorPerMeterM;
        [Tooltip("표적을 연속으로 본 뒤 조준이 안정되는 시간 (초)")]
        public float aimSettleSec;
        [Tooltip("조준 안정 전 오차 배율 (시간에 따라 1로 감소)")]
        public float unsettledErrorMultiplier;
        [Tooltip("자신이 이동 중일 때 오차 배율")]
        public float selfMovingErrorMultiplier;
        [Tooltip("표적이 이동 중일 때 오차 배율")]
        public float targetMovingErrorMultiplier;
        [Tooltip("점사 중 발마다 늘어나는 오차 반지름 (m) — 반동 단순화")]
        public float burstErrorGrowthM;

        [Header("★ 연사 패턴 (TODO(GDD-?))")]
        [Tooltip("자동 무기 점사 발 수 최소~최대 (포함)")]
        public Vector2Int burstRounds;
        [Tooltip("점사 사이 쉬는 시간 최소~최대 (초)")]
        public Vector2 burstPauseSec;
        [Tooltip("반자동 무기 사격 간격 최소~최대 (초)")]
        public Vector2 semiShotIntervalSec;

        [Header("★ 교전 거리 (m, TODO(GDD-?))")]
        [Tooltip("선호 교전 거리 최소~최대")]
        public Vector2 preferredRangeM;
        [Tooltip("이 거리보다 멀면 쏘지 않는다")]
        public float engageMaxRangeM;

        [Header("★ 행동 플래그 (§7 행동)")]
        [Tooltip("엄폐 활용 (AI-RDR)")]
        public bool usesCover;
        [Tooltip("측면 우회 (AI-RDR)")]
        public bool flanks;
        [Tooltip("소리 위치로 조사하러 간다 (AI-SCV 소리 추적)")]
        public bool investigatesNoise;

        [Header("★ 엄폐·전술 시간 (TODO(GDD-?))")]
        [Tooltip("엄폐에서 나와 사격하는 시간 최소~최대 (초)")]
        public Vector2 peekSec;
        [Tooltip("엄폐에 숨는 시간 최소~최대 (초)")]
        public Vector2 hideSec;
        [Tooltip("숨는 동안 탄 비율이 이 값 미만이면 미리 재장전 (0 = 안 함)")]
        public float tacticalReloadBelow;
        [Tooltip("교전 중 옆으로 움직이는 간격 최소~최대 (초, 0 = 안 함)")]
        public Vector2 strafeIntervalSec;

        // ── M7 (설계 §3.4, Q20~Q29) ── 기존 에셋(AI-SCV·RDR)은 이 필드가 없어 아래 초기값으로 읽힌다 = M3~M6 동작 그대로.
        // 그래서 M7 필드만은 "기존 동작" 초기값을 둔다(위 M3 필드의 "초기값 없음" 규칙의 예외). 수치는 전부 TODO(GDD-?).

        [Header("★ M7 전술 (TODO(GDD-?) Q20·Q28)")]
        [Tooltip("전술 종류. Auto = 기존 규칙(usesCover ? Raider : Scavenger)")]
        public EnemyTacticsKind tactics = EnemyTacticsKind.Auto;
        [Tooltip("시작 위치에서 이 수평 반경(m) 밖으로 이동 목적지를 잡지 않는다 (보스·호위 거점). 0 = 무제한")]
        public float anchorRadiusM;

        [Header("★ M7 무기 내구도 (TODO(GDD-?) Q12·Q18)")]
        [Tooltip("스폰 무기 내구도 최소~최대 (%, 0~100). 적 무기는 마모·고장이 없고 시체에서 주운 무기에만 의미가 있다")]
        public Vector2 weaponDurabilityRange = new Vector2(100f, 100f);

        [Header("★ M7 근접 (AI-MUT, TODO(GDD-?) Q24·Q25)")]
        [Tooltip("근접 타격 피해 (0 = 근접 없음)")]
        public float meleeDamage;
        [Tooltip("근접 사거리 (표적까지 수평 거리, m)")]
        public float meleeRangeM;
        [Tooltip("타격 사이 간격 (초)")]
        public float meleeIntervalSec;
        [Tooltip("준비 시간 (초) — 이 시간 뒤 사거리 안이면 타격, 밖이면 헛침")]
        public float meleeWindupSec;
        [Tooltip("무리 둘레 분산 접근 반경 (m, 분대 순번별 각도)")]
        public float surroundRadiusM;

        [Header("★ M7 저격 (AI-SNP, TODO(GDD-?) Q22·Q23)")]
        [Tooltip("처음·다시 볼 때 사격 전 섬광 경고 시간 (초)")]
        public float telegraphSec;
        [Tooltip("평시·수색 중 시선을 좌우로 훑는 전체 각 (도)")]
        public float scanArcDeg;
    }
}
