# GDD 변경 기록

> GDD §0: 문서 수정이 필요하면 이 파일에 사유를 먼저 기록한 뒤 `docs/GDD.md`를 고친다.
> 형식: `## YYYY-MM-DD — 섹션 ID` 아래에 변경 내용과 사유.

## 2026-10-21 — 최초 반입
- `docs/GDD.md`를 원본 기획서 그대로 저장소에 추가. 내용 변경 없음.

## 2026-10-21 — §13.2 폴더 구조
- `Scripts/EditorTools/`(어셈블리 `Ashfall.EditorTools`, 에디터 전용) 추가.
- 사유: 씬·에셋은 YAML을 손으로 쓰지 않고 에디터 메뉴로 생성하는데(M0 Boot·MainMenu 씬), 에디터 전용 코드를 둘 폴더가 §13.2에 없었다.
  이름을 `Editor`로 하지 않은 이유: `Ashfall.Editor` 네임스페이스가 다른 Ashfall 코드에서 `UnityEditor.Editor`를 가린다.

## 2026-10-21 — M1 구현 중 결정 (§13.3, §14, §12.1, §5)
문서 본문은 바꾸지 않았다. 아래는 구현에서 정한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
- **§13.3·§14 아이템 SO 위치:** `ItemDefinitionSO`·`WeaponDefinitionSO`·`AmmoDefinitionSO`·`MagazineDefinitionSO`와 아이템 enum을 `Ashfall.Core`에 둔다.
  사유: Weapons·Inventory(M2)·AI(M3)가 함께 쓰는데 기능 어셈블리끼리 참조가 금지돼 있다. `PlayerConfigSO`는 Player만 쓰므로 Player에 둔다.
- **§14 스키마에 없는 필드 추가:**
  - `WeaponDefinitionSO.reloadTimeSec`, `fastReloadTimeSec`, `hipSpreadDeg` — 재장전(§5.1)·지향 사격(RULE-07)에 필요한 값.
  - `AmmoDefinitionSO.maxFlightTimeSec` — 탄도 투사체(§5.1)가 무한히 날지 않게.
  - `PlayerConfigSO`의 자세별 캡슐·눈높이, 시점 감도·상하 제한, 기울이기 이동량·속도, 접지 속도.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 반동 V/H(0.6°/0.25°), 조준 시간(0.25s), 재장전 2.5s / 빠른 재장전 1.5s, 지향 사격 퍼짐 2°,
  5.56 관통력 3 / 초기 속도 900m/s, 아이템 크기·무게·가격, 캡슐 높이(1.8/1.2/0.6m), 감도 등. 값은 `Assets/_Project/Data/` 에셋에서 조정한다.
- **§12.1 키 표에 없는 입력:** 점프 = Space. 빠른 재장전 = 일반 재장전 중 R 한 번 더(남은 시간은 빠른 재장전 기준). 기울이기 = Q/E 누르고 있는 동안.
- **§5.2 피해 위치 충돌:** 표의 "피해"는 무기별인데 §14에서 피해는 `AmmoDefinitionSO.damage`에 있다. 9mm를 쓰는 WPN-PST-01(35)과 WPN-SMG-01(30)이 같은 탄으로는 표현되지 않는다.
  M1은 5.56 = 45로 두고, M7(무기 5종) 전에 무기 배율 필드 등으로 정해야 한다.
- **§5.1 미구현(이후 마일스톤):** 고장(Jam)·내구도, 약실 탄(+1), 버린 탄창을 줍기 가능한 아이템으로 떨어뜨리기(M2 인벤토리).

## 2026-10-21 — M2 설계 결정 (§4.2, §4.3, §6, §12.1, §13.2, §13.3, §14, RULE-04, RULE-05)
문서 본문은 바꾸지 않았다. 아래는 M2 구현을 위한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
- **§13.3 이벤트 버스·GameManager:** `Ashfall.Core`에 `EventBus`(동기, `readonly struct` 이벤트)와 `ServiceRegistry`를 두고,
  유일한 싱글톤 `GameManager`가 둘을 가진다. `GameManager`는 `RuntimeInitializeOnLoadMethod`로 자동 생성된다(Boot를 거치지 않는 테스트 씬 대비).
  다른 기능의 상태를 읽고 조작할 때는 Core 인터페이스(`IInventoryService`, `IMagazineSource`, `IBodyStatusView`)를 레지스트리에서 얻고,
  변경 알림은 이벤트로 받는다. 사유: 드래그 미리보기처럼 동기 판정이 필요하고, 배치 규칙을 UI에 복제하지 않기 위해.
- **§4.2 부위 판정:** 플레이어는 히트박스 콜라이더 없이 캡슐 피격 지점의 높이·좌우로 7부위를 판정한다(그레이박스 근사, 엎드림 부정확).
- **§4.2 해석:** 피격으로 0 아래가 된 초과분은 버리고, 이미 파괴된 부위가 받는 피해만 70%를 흉부·복부에 균등 분산(복부 파괴 시 흉부로).
  복부 파괴 1.5배는 피격 피해에만 적용, 출혈 지속 피해에는 미적용. 팔 파괴 흔들림 +100%는 팔마다 더한다. 다리 패널티는 두 다리 중 나쁜 쪽 하나만.
  복부 파괴 "탈수 가속"은 탈수(M7)가 없어 미구현.
- **§4.3 해석:** 출혈·중출혈은 모든 부위, 골절은 팔·다리만. 피격마다 확률 굴림. 의약품 대상 부위는 자동 선택(해당 상태가 있는 첫 부위).
- **§14 스키마에 없는 추가:** `ContainerDefinitionSO { kind, gridSize }`(조끼·배낭), `ArmorDefinitionSO.kind`(헬멧/방탄복),
  `WeaponHandlingConfigSO`(탄창 확인 시간·구간, 조준 흔들림), `InventoryConfigSO`(주머니, 검색 시간, 상호작용 거리, 버리기),
  `HudConfigSO`(탄창 확인 문구 표시 시간). §4.2·§4.3 수치는 §14에 따라 `PlayerConfigSO`에 추가.
  런타임 아이템(`ItemInstance`, `Magazine`, `WeaponItem`, `MedicalItem`)은 `Ashfall.Core`에 둔다(`Magazine`은 Weapons에서 이동).
- **§13.2 방어구 위치:** `ArmorDefinitionSO`는 Inventory도 쓰므로 Weapons가 아니라 Core에 둔다(M1 아이템 SO 결정과 같은 사유). 관통 계산(§5.3)은 M2 범위 밖.
- **§6 해석:** 회전은 아이템이 아니라 그리드 배치에 저장. 내용물이 있는 조끼·배낭은 해제·버리기 불가. 차 있는 슬롯에 장착하면 실패(교환 없음).
  재장전은 전술조끼·주머니의 탄창만 쓴다(배낭 제외). 일반 재장전 시 회수 공간이 없으면 바닥에 떨어뜨린다.
  루트 검색은 열려 있는 동안만 진행, 검색 전 아이템은 옮길 수 없다. 줍기 자동 배치 순서: 조끼 → 주머니 → 배낭.
- **§12.1 키:** R은 화면이 닫혀 있으면 재장전, 인벤토리 드래그 중이면 회전. T 길게(고장 해제, §5.1)는 M7. 무기 교체 1/2/3은 무기가 1종이라 M2에서 미구현.
  퀵슬롯 4~7은 인벤토리에서 아이템 위에 마우스를 두고 숫자로 배정. 화면이 열려 있으면 이동을 포함한 게임플레이 입력을 막는다.
- **RULE-04:** 구간은 탄 수/용량 ≥ 0.67 "가득"(30발 탄창은 21발부터, 20발은 "절반"), ≥ 0.34 "절반", 그 아래 "거의 없음"(0발 포함). 결과 문구 2초 표시. 사격·재장전 시 확인 취소.
- **RULE-05:** 부위 상태 화면은 등급(정상/부상/중상/파괴)과 상태이상 이름만 표시. 등급 경계 HP 비율 0.75 / 0.35.
- **조준 흔들림:** M1에는 없던 기본 흔들림(0.15°, 0.5Hz)을 추가해 팔 파괴·골절 배율이 적용되게 했다.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 출혈 확률 0.35 / 중출혈 0.15 (피해 10 이상), 골절 확률 0.2 (피해 20 이상),
  골절 이동 ×0.8 / 조준 흔들림 +0.5, 부위 판정 높이 비율(0.87/0.62/0.45, 팔 0.6R), 의약품 사용 시간(붕대 3s, 지혈대 4s, 부목 5s)·크기·무게,
  조끼 그리드 4×2, 배낭 4×5, 헬멧·방탄복 등급 3, 상호작용 거리 2m, 아이템 ID(MED-*, RIG-01, BAG-01, ARM-*, JNK-*).
- **구현 중 추가 결정:**
  - `WeaponHandlingConfigSO.magazineDropSpeed`(버린 탄창 속도), `HudConfigSO.dragThresholdPx`(드래그 시작 거리) 추가. 둘 다 TODO(GDD-?).
  - UI 상태 로직은 순수 클래스 `GameUIPresenter`로 분리(EditMode 테스트용). UI Toolkit 입력은 EventSystem 없이 Input System의 UI 맵을 쓴다(Input System 1.20, Unity 2023.2+).
  - 그레이박스 피격원으로 `TestTurret`(Weapons, 테스트 전용)을 둔다. M2 확인 씬 `Scenes/Test/M2_HealthInventory.unity`는 `Ashfall/Setup/Create M2 Test Scene`이 매번 새로 만든다(빌드 설정에는 없음).
  - 희귀도·가격이 정해지지 않은 M2 아이템(의약품·장비·방어구)은 희귀도 Scrap, 가격 0으로 둔다(TODO(GDD-?)).
  - (리뷰 수정, SYS-INV) 인벤토리 이동·장착·버리기 명령은 지금 인벤토리(장착·저장 공간·열린 루트)에 있는 아이템만 받는다. 드래그 중
    재장전·의약품 소진으로 빠진 아이템이 놓을 때 복제·부활하던 문제 때문. 사용 중(잠긴) 의약품은 드래그 시작부터 거부하며, 이를 위해
    `IInventoryService.IsLocked(ItemInstance)`를 추가했다. 남은 횟수 0인 의약품은 사용 시작 거부(`MedicalUseFailReason.Depleted`). 사망 후 재장전은 완료되지 않는다.
  - (리뷰 수정, SYS-PLR) 씬 생성 메뉴가 플레이어 `CharacterController.minMoveDistance`를 0으로 둔다(기본 1mm는 고fps + 느린 이동에서 멈춤).
    M1 씬을 다시 만들려면 `Ashfall/Setup/Recreate M1 Greybox Scene`.

## 2026-10-21 — M3 설계 결정 (§4.1, §5.2, §6, §7, §13.2, §13.3, §14, §15 M3)
문서 본문은 바꾸지 않았다. 아래는 M3 구현을 위한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
- **§5.2 ↔ §14 피해 위치 (M1에서 미룬 충돌, 사용자 결정):** `WeaponDefinitionSO.damage`를 추가하고(§5.2 "피해", 발당·산탄은 펠릿당),
  값이 0보다 크면 탄약 피해 대신 쓴다(`DamageRules.ProjectileDamage`). `AmmoDefinitionSO.damage`는 남기되 무기 없이 탄약만으로 쏘는 경우
  (그레이박스 터렛·테스트)의 기본값으로 의미를 바꾼다. 사유: 같은 9mm를 쓰는 WPN-PST-01(35)·WPN-SMG-01(30)을 표현해야 하고, 탄약 필드를
  지우면 기존 에셋·테스트가 깨진다. 데이터 이전: WPN-RIF-01.damage = 45 기록(M3 데이터 메뉴), AMMO-556.damage 45 유지.
- **§5.2 데이터 추가 (사용자 결정):** AI-SCV 무장을 위해 M3에서 WPN-PST-01, WPN-SMG-01, AMMO-9MM, MAG-9MM-12, MAG-9MM-30을 만든다.
  플레이어가 이 무기를 들고 쏘는 무기 교체(1/2/3)는 M7. 시체에서 주워 인벤토리·장착 슬롯에 넣는 것까지는 기존 인벤토리로 허용.
- **§13.2·§13.3 무기 로직 위치:** `WeaponModel`·`FireControl`·`LocalMagazineSource`를 Weapons에서 Core로 옮긴다(파일·.meta 이동, GUID 유지).
  사유: AI가 같은 발사 간격·탄창·재장전 규칙과 실제 탄창 객체를 써야 하고(시체에 남은 탄 수 보존), 기능 어셈블리끼리 참조가 금지돼 있다.
- **§13.3 경계:** AI는 Core 인터페이스 `IPlayerTarget`(Player가 등록: 위치·눈·자세·속도·사망)과 `IProjectileService`(Weapons가 등록)를
  `ServiceRegistry`에서 얻는다. 플레이어 소음은 이벤트 `NoiseEmitted`, 적 사망·시체 루트는 이벤트 `EnemyKilled`(Inventory가 시체에 루트 컨테이너를 붙임).
  `EventBus.Publish`를 할당 없는 방식(쓰기 시 복사 배열)으로 바꿨다(공개 API 동일). 씬의 AI 관리자 `AiDirector`는 정적 인스턴스가 없다(싱글톤 아님).
- **§14 EnemyArchetypeSO:** `hp`는 몸통 HP로 해석하고 `headHp`·`legsHp`를 더했다. `weapon`은 `loadouts`(무기·탄·탄창·가중치 목록)로 바꿨다
  (AI-SCV "권총/SMG", 사격·시체에 탄·탄창 정의가 필요). 그 밖 추가: displayName, corpseDisplayName, hearingMultiplier, spareMagazines,
  이동 속도, 반응 시간, 명중 오차, 점사, 선호 거리, 행동 플래그(usesCover/flanks/investigatesNoise), 엄폐 peek/hide 시간.
  전역 수치는 새 `AiConfigSO`(AI). `sightAngle`(110)은 전체 각(반각 55°).
- **§14 LootTableSO:** 튜플 목록은 직렬화되지 않아 `List<LootEntry { item, weight }>`로 쓰고 `id`를 더했다. 굴림은 rollCount 최소~최대(포함) 중 하나를
  고른 뒤 가중치 비례 복원 추출(`LootRoller`, Core).
- **§7 3부위:** 적은 단일 캡슐 피격 지점의 높이 비율로 머리/몸통/다리를 판정한다(머리 ≥ 0.85, 몸통 ≥ 0.45, TODO(GDD-?)).
  §4.2를 3부위로 축약: 머리·몸통 0 = 사망, 다리 0 = 이동 -40%·질주 불가, 파괴 부위 추가 피해 70%는 몸통으로. 적에게 상태이상·방어구는 없다(단순화).
  HP 임시값은 플레이어와 같게 머리 35 / 몸통 80 / 다리 65.
- **§7 상태 머신 해석:** Idle(대기) → Patrol(순찰) → Suspicious(부분 감지·발소리: 멈추고 확인, 약탈자는 소리 위치로 이동) → Alert(존재 확정·총성·피격·분대 연락:
  반응 시간 뒤 교전, 안 보이면 마지막 위치로 접근) → Combat(교전) → Search(마지막 위치 주변 수색) → Patrol. 종료 상태 Dead를 더했다.
  되돌아가는 전이(수색 중 발견 → Alert, 의심 해소 → Patrol, 표적 사망 → Patrol)는 설계 문서 전이 표를 따른다.
- **§7 감각:** 시야 유효 거리 = 60m × 자세 배율(웅크림 0.6, 엎드림 0.3)을 모든 상태에 적용. 가림은 눈 → 플레이어 눈/중심 레이 2개.
  청각은 §4.1 소음 반경을 "그 거리 안의 적이 듣는다"로 해석(가림·감쇠 없음). 발소리는 이동 중 0.5초마다, 점프는 즉시 발행.
  사격 소음 반경은 GDD에 없어 무기 SO `shotNoiseRadiusM` 임시값(소총 120 / SMG 90 / 권총 80m). 적의 총성은 다른 적이 듣지 않는다.
  인지 게이지(근거리 2.5/s ~ 원거리 0.35/s, 4m 안 즉시, 의심 0.3, 감소 0.15/s, 발소리 +0.25, 점프 +0.4)는 GDD에 없는 임시값.
- **§7 엄폐·우회:** `CoverPoint`는 숨는 위치 + 막아 주는 방향(forward) + 높이(Low/High, High는 옆으로 나와 사격)를 가진다. 선택은 보호각 70°,
  거리·선호 사거리 점수 후 상위 3개만 "플레이어 눈에서 숨는 눈 높이가 가려짐 + 사격 위치에서 플레이어가 보임"을 레이로 확인한다.
  레이더 측면 우회는 엄폐에서 6초 이상 못 보면 분대에서 한 명만, 표적 기준 45~120° 옆의 엄폐로 이동한다. 새 물리 레이어는 만들지 않았다.
- **§6 시체:** 내용물 = 적 무기(남은 탄의 장착 탄창 포함) + 남은 예비 탄창 + 루트 테이블 굴림, 이 순서로 검색에 드러난다(0.5초/개).
  시체 그리드 6×5, 프롬프트는 상자와 같은 "열기"(TODO(GDD-?)).
- **§15 M3 성능 판정:** "20명 60fps"는 배치모드 PlayMode에서 20명 교전 중 AI 틱 평균 ≤ 1.0ms·99% ≤ 3ms, 투사체 평균 ≤ 1.0ms,
  프레임 중앙값 ≤ 16.6ms로 자동 확인하고, GPU 포함 실측은 사람이 에디터 Profiler/개발 빌드로 확인한다(배치모드는 렌더링 비용을 재지 않는다).
  감각은 평시 0.2초·경계 0.1초 주기로 분산(프레임당 최대 8명), 경로 재계산 0.5초·1m 조건, AI 갱신 경로 GC 할당 0.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 9mm 탄 관통 1·초속 360m/s·비행 1.5초, 권총·SMG 반동·조준·재장전 시간·크기·무게,
  9mm 탄창 크기·무게, 적 이동 속도·반응 시간·명중 오차·점사·선호 사거리, 루트 테이블 LT-SCV/LT-RDR 내용, 예비 탄창 수(2/3),
  분대 공유 거리 50m, 수색 반경 12m·25초, 엄폐·우회 각도·반경, 감각 주기·예산, 발소리 간격 0.5초, 시체 그리드.
- **M3 확인 씬:** `Scenes/Test/M3_AI.unity`(SCV 분대 3인·2인 순찰, RDR 2인 경계, 엄폐 구조물), `Scenes/Test/M3_AIPerf.unity`(적 20명).
  `Ashfall/Setup/Create M3 Test Scenes`가 두 씬을 매번 새로 만들고 NavMesh를 굽는다(`Scenes/Test/M3_AI/`, `M3_AIPerf/`의 `NavMesh-Environment.asset`, GUID 유지). 빌드 설정에는 없음.
  상태 색 표시(머리 위 구)는 그레이박스 확인용이며 M6에서 끈다(`AiConfigSO.showStateIndicator`).
- **구현 중 추가 결정:**
  - 레이더는 엄폐에서 표적을 못 본 지 6초 뒤 우회하므로, 우회를 아직 시도하지 않았으면 시야 상실 → Search(5초)를 미룬다.
  - 레이더는 Alert에서도 엄폐에 도착하면 숨기·내다보기를 반복하고, 내다볼 때 보이면 Combat으로 간다. Alert 시간 초과는 분대 연락이 아니라
    자신이 직접 보거나 들은 마지막 시각 기준으로 잰다(분대 연락은 마지막 위치만 갱신). 사유: 엄폐 뒤에서 Alert로 영원히 머무는 문제.
  - 총성·피격으로 추정한 위치는 NavMesh 바닥에 붙여 마지막 위치로 쓴다(총구 높이를 그대로 쓰면 엄폐 사격 위치 검사가 틀어짐).
    레이더는 min(시야, 사거리)보다 먼 엄폐를 고르지 않고, 방금 무효화한 엄폐는 5초 동안 후보에서 뺀다.
  - 분대 리더는 씬 순서로 정하고, 리더가 죽거나 사라지면 다음 분대원이 가장 가까운 웨이포인트부터 순찰을 잇는다.
    경로 없는 경계 분대의 분대원은 대형을 따르지 않고 배치 자리를 지킨다.
  - `AiConfigSO`에 도착 거리·이동 판정 속도·옆걸음 거리·NavMesh 샘플 반경·대형 따라잡기 거리·수색 시선 각·엄폐 재시도 간격·
    무효화 엄폐 제외 시간·바라볼 거리를 더했다(TODO(GDD-?)).
  - `M3_AIPerf` 바닥은 150m(4사분면 배치가 140m 끝에 걸림). 분대 리더는 경로 첫 지점, 분대원은 대형 자리에서 시작.
  - 플레이어 상호작용 레이캐스트는 자기 캡슐을 건너뛴다(눈 높이가 PhysX 캡슐 꼭대기보다 높아 발밑 시체를 못 열던 문제).

## 2026-10-21 — M4 설계 결정 (§2, §8, §12, §12.1, §13.2, §13.3, §14, §15 M4, RULE-01, RULE-02, RULE-06)
문서 본문은 바꾸지 않았다. 아래는 M4 구현을 위한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
사용자 지시로 GDD에 없거나 모호한 22개 항목(설계 문서 `docs/design/M4_design.md` §5의 Q1~Q22)은 사용자 답을 기다리지 않고
가장 단순한 추천안으로 정했다. 코드의 결정 지점에는 `TODO(GDD-?) Qn` 주석이 있다.
- **§13.3 씬 흐름·GameManager:** 유일한 싱글톤 `GameManager`에 `GameFlow`(Core, 순수)를 더한다. 씬 전환(Boot → MainMenu → Raid_CrimsonBasin → MainMenu),
  레이드 결과 보관(`PendingResult`), 세계 정지(timeScale)의 유일한 주체다. 레이드 결과는 `RaidResult`(Core)로 씬을 넘는다.
  M5 경계: `StartRaid`(반입 Loadout, raid_in_progress 기록 자리), `CompleteRaid`의 목적지(은신처).
- **§13.3 경계:** Raid는 Core만 참조한다. 스폰 배치는 `IPlayerPlacement`, 배낭·신호탄 확인·소모는 `IInventoryService`, 반입·반출 목록은
  `InventorySnapshot`, 전원 스위치 F 상호작용은 `IWorldInteractable`, UI로는 `RaidStarted`·`ExtractionStatusChanged`·`RaidEnded` 이벤트와
  `IRaidInfo`(지도), UI에서 Raid로는 `RaidAbandonRequested`.
- **§14 RaidConfigSO:** 탈출구는 조건 필드(대기 시간, 반경, 필요 스위치, 개방 시각, 소모 아이템, 배낭 금지)로 정의한다.
  추가 필드: 스폰별 활성 탈출구, 항상 활성 목록, 종료 지연, 높이 허용, 지도 범위.
- **§8.1 블록아웃 (Q1, Q20, Q21):** 1km 전체를 Terrain 1000×120×1000(해상도 513, 고정 시드)으로 만들고 POI 5곳을 저밀도로 블록아웃한다.
  경계는 가장자리 안쪽의 보이지 않는 벽. 방사능 정유소는 블록아웃만(효과·색 없음, M7). 실내 없음.
  `Ashfall/Setup/Create M4 Raid Scenes`가 레이드 씬·Terrain·NavMesh와 MainMenu를 매번 새로 만들고 GUID를 유지한다.
  지형 충돌을 위해 내장 패키지 `com.unity.modules.terrainphysics`를 켰다(꺼져 있으면 TerrainCollider가 지워져 플레이어가 지형을 뚫고 떨어진다).
- **§8.2 탈출 (Q2~Q9):** 스폰은 고정 3곳 중 무작위이고, 스폰마다 활성 탈출구 세트가 정해져 있다(EXT-01은 "항상 열림"이라 모든 세트에 포함).
  비활성 탈출구는 아무 표시 없이 작동하지 않는다. 구역 = 수평 반경 + 높이 차 3m. 대기 중 구역을 나가거나 조건이 깨지면 처음부터(이동·피격은 허용).
  같은 틱에 대기 완료와 시간 초과가 겹치면 탈출 우선. EXT-02 전원 스위치는 송전탑 언덕에 있고 F 한 번으로 레이드 끝까지 켜진다.
  EXT-03은 경과 900초 이후, 신호탄은 20초 대기를 마치는 순간 1개 소모(고정 상자 2곳 + 상자 루트 테이블의 낮은 가중치로 획득).
  EXT-04는 배낭만 금지(조끼 허용). 구역 안에서 조건이 안 맞으면 짧은 이유 문구를 표시한다(§12 HUD 목록 밖이지만 없으면 이유를 알 수 없음).
- **§12 HUD·화면 (Q10, Q11, Q15, Q16, Q18, Q22):** 남은 레이드 시간은 HUD에 없고 지도에 분 단위 대략치로만 나온다.
  지도(M)는 지도 아이템을 가졌을 때만 열리고 POI와 활성 탈출구만 보인다(RULE-06, 내 위치 없음). 지도는 세계를 멈추지 않는다.
  Esc 일시정지는 세계를 실제로 멈추고 [계속]/[레이드 포기]를 준다(포기 = 사망과 같은 RULE-01).
  레이드가 끝나면 3초 종료 오버레이(세계 정지) 뒤 MainMenu에서 결과 패널(결과·원인 또는 탈출구·시간·처치 수·반입/반출/소실 목록, 탄 수 없음)을 보여 준다.
  메인 메뉴 버튼은 [레이드 시작]/[종료]뿐이다. UI Toolkit 버튼은 EventSystem 없이, 일시정지 중에도 동작한다.
- **RULE-01 (Q13, Q14):** 판정 지점은 `RaidResultBuilder` 하나다. 탈출이 아니면 반출 0, 종료 시점 소지품 전부를 소실로 기록한다.
  보관함이 없으므로 탈출해도 소지품은 다음 레이드로 넘어가지 않고, 레이드는 항상 고정 키트(M2/M3 시작 장비 + 지도)로 시작한다. 실제 보관은 M5.
  레이드가 끝나면 인벤토리·재장전·의약품 사용을 멈춰, 결과 목록을 만든 뒤 소지품이 바뀌지 않게 한다.
- **RULE-02:** 경과 1500초(RaidConfigSO) = MIA(사망 처리, 몸은 그대로). 일시정지 중에는 시간이 흐르지 않는다.
- **RULE-03 (Q19):** 레이드 중 앱 강제 종료 처리는 M5(세이브 플래그)로 미룬다.
- **§7 레이드 적 (Q12):** M3 AI를 코드 변경 없이 고정 배치(SCV 12 + RDR 8, 스폰에서 190m 이상). 런타임 스포너는 만들지 않는다.
- **§6 상자 (Q17):** `LootContainer`가 `LootTableSO`(LT-CRATE)를 굴려 내용물을 만든다.
- **§8.3:** M4에는 동적 환경이 없다(재 폭풍 M6, 방사능 M7, 낮/밤 없음 = 시간 진행 없음).
- **§12.1 키:** 지도 = M(입력 액션 `Map`), 일시정지 = Esc(화면이 모두 닫혀 있을 때).
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 탈출 구역 반경(8/8/10/5m)·높이 허용 3m, 종료 오버레이 3초, POI·탈출·스폰 좌표,
  지형 높이, 적 배치, 신호탄·지도 아이템 크기·무게·희귀도, 상자 루트 테이블 LT-CRATE 내용.
- **설계 문서 이동:** M2~M4 설계·리뷰 노트를 `docs/design/`에 둔다(다음 마일스톤이 저장소만 읽고 이어 가도록).

## 2026-10-22 — M5 설계 결정 (§2, §6, §9, §12, §13.2, §13.3, §14, §15 M5, §18, RULE-01, RULE-03)
문서 본문은 바꾸지 않았다. 아래는 M5 구현을 위한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
사용자 지시로 GDD에 없거나 모호한 31개 항목(설계 문서 `docs/design/M5_design.md` §5의 Q1~Q31)은 가장 단순한 추천안으로 정했다.
코드의 결정 지점에는 `TODO(GDD-?) Qn` 주석이 있다.
- **§13.2·§13.3 세이브·프로필:** Core에 `ProfileService`(GameManager 소유)와 `PlayerProfile`(보관함·은신처 장비·탄피·키트 시각·raidInProgress)을 둔다.
  세이브는 JsonUtility로 `persistentDataPath/save.json`(version 1, 평평한 아이템 레코드, enum은 이름 문자열), 임시 파일 → `File.Replace`(직전 세대 .bak).
  §13.3의 `raid_in_progress`는 JSON 키 `raidInProgress`. 아이템 정의는 새 `ItemDatabaseSO`(Data/Items/ItemDatabase.asset, 모든 아이템 정의)로 id → SO를 찾는다.
  손상·지원하지 않는 버전은 save.corrupt.json으로 옮기고 새 프로필(Q22), 알 수 없는 id는 그 아이템만 버림(Q23). .bak 자동 복구는 RULE-03 우회 위험으로 하지 않는다.
  내장 모듈 jsonserialize·uielements를 매니페스트에 명시했다(Q24).
- **§13.3 ItemGrid 위치:** `ItemGrid`를 Inventory에서 Core로 옮겼다(파일·.meta 이동, GUID 유지). 사유: 상인·키트(Hideout)가 보관함 배치를 판정해야 하는데 기능끼리 참조가 금지돼 있다(Q2, M3 무기 로직 이동과 같은 사유).
- **§9·§18 반입(Loadout):** 반입 = 장착 슬롯 + 저장 공간 배치 + 퀵슬롯(Core `Loadout`). 은신처에는 "은신처 장비"가 유지되고 그것이 다음 반입이 된다(Q3, Q19).
  레이드는 흐름으로 시작했을 때만 `startingItems` 대신 반입으로 시작한다(확인 씬은 그대로).
- **RULE-01:** 반입은 레이드 시작 때 은신처 장비에서 빠지고, 탈출 시 종료 시점 소지품이 위치 그대로 은신처 장비로 돌아온다(`RaidResult.CarriedOutLoadout`, 판정은 계속 `RaidResultBuilder` 하나, 반영은 `ProfileService.ApplyRaidResult` 하나).
  사망·MIA·포기면 은신처 장비가 비고 보관함은 바뀌지 않는다. 흐름 밖(에디터에서 레이드 씬 직접 Play) 결과는 표시만 한다(Q25).
- **RULE-03·§13.3 저장 시점:** 레이드 중에는 아무것도 쓰지 않는다. 저장은 은신처 진입, 은신처를 나갈 때(메인 메뉴·레이드 시작), 은신처에서 앱 종료(Q7).
  레이드 시작 저장은 반입 장비를 그대로 두고 raidInProgress=true를 기록한다(Q4). 이 저장이 실패하면 레이드를 시작하지 않는다.
  다음 실행의 첫 은신처 진입에서 true면 은신처 장비를 지우고(보관함·탄피 유지) 알림 패널로 잃은 목록을 보여 준다(Q5, Q6).
  탈출 순간 ~ 은신처 진입 저장 사이의 강제 종료도 반입 삭제로 처리된다(Q31). 에디터에서 레이드 중 Play 중지도 강제 종료다.
- **§9 보관함:** 10×30(W×H) 고정, 업그레이드 없음(Q1). 은신처에서는 보관함을 인벤토리의 열린 외부 그리드 자리로 열어 기존 인벤토리 화면으로 장비 ↔ 보관함을 옮긴다(Q28, Q29). 은신처에서 버리기·의약품 사용 불가(Q27).
- **§9 상인:** 탄피는 프로필 정수(Q8), 시작 6000(Q9). 매수 = 올림(basePrice × 1.0), 매도 = 내림(basePrice × 0.4), 무기는 장착 탄창 포함, 탄 수·사용 횟수 미반영(Q10).
  판매 목록 고정·재고 무제한(소총·5.56 탄창·지도·붕대·지혈대·부목·조끼·배낭·헬멧·방탄복, 신호탄 제외)(Q11). 매도는 보관함 아이템만, 가격 0도 처분으로 판매(Q12).
- **§9 부랑자 키트:** 녹슨 권총(탄창 없음) + 권총 탄창 2개(가득) + 붕대를 보관함에 지급. 조건은 쿨다운만(새 프로필 즉시), 실제 시간 UTC 20분, 시계 역행 시 최대 20분(Q13, Q14, Q17).
  보관함에 전부 들어갈 때만 지급(Q15). 권총은 M7(무기 교체) 전까지 플레이어가 쏠 수 없다(Q16, M3 결정 유지).
- **§12 화면:** 은신처(메뉴형): 알림 → 레이드 결과(M4 결과 패널 이동, Q21) → 허브([장비]/[상인] 탭, 부랑자 키트, 레이드 시작, 메인 메뉴). 메인 메뉴는 [은신처]/[종료](Q20).
  레이드 시작 확인 없음·빈 장비 허용(Q26, Q30). 체력은 이월하지 않는다(Q18).
- **§13.2 씬:** `Hideout` 씬 추가, 빌드 순서 Boot → MainMenu → Hideout → Raid_CrimsonBasin. `Ashfall/Setup/Create M5 Hideout Scene`이 씬과 빌드 설정을 만든다.
  세이브 폴더 열기·삭제는 에디터 메뉴 `Ashfall/Save/...`.
- **§14 스키마에 없는 추가:** `HideoutConfigSO`(stashSize, startingCasings, vagrantKitCooldownSec, vagrantKitItems), `TraderConfigSO`(id, displayName, 매수·매도 배율, catalog), `ItemDatabaseSO`.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 모든 아이템 basePrice(설계 §4.6 표), 매수·매도 배율 1.0/0.4, 시작 탄피 6000, 상인 이름 "고물상", 판매 목록.
- **리뷰 수정 (M5_review.md):** 세이브 읽기 오류(잠금·권한)는 손상과 구분한다 — 짧게 재시도 후에도 실패하면 격리하지 않고 그 실행을 읽기 전용으로 연다
  (저장 전부 거부, 레이드 시작 거부, 알림). 해석 중 예외는 손상으로 격리 + 새 프로필, 은신처 진입이 실패하면 MainMenu로 돌아간다.
  save.json이 없고 .tmp만 있으면 .tmp를 읽는다(.bak 자동 복구는 여전히 안 함). 장착 탄창 탄약이 무기와 다르면 탄창 없는 무기로 읽는다.
  은신처를 나갈 때(W2) 저장이 실패하면 설계(알림만, 이동)와 달리 이동하지 않고 메시지를 띄우며, 한 번 더 누르면 다시 시도 후 실패해도 나간다.
  은신처 인벤토리는 Core의 진입 완료 표시(`ProfileService.HideoutEntryToken`)를 확인한 뒤 장비를 놓는다.
  재시도 3회·100ms는 기술 상수(TODO(GDD-?)).
- **기타:** Unity가 `Assets/Settings/Mobile_RPAsset.asset`을 URP 에셋 버전 13으로 자동 갱신했다(엔진 쪽 변경, 함께 커밋).

## 2026-10-22 — M6 설계 결정 (§4.3 STAT-RAD, §7, §8.3, §10, §11, §13.1, §13.2, §13.3, §14, §15 M6)
문서 본문은 바꾸지 않았다. 아래는 M6 구현을 위한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
사용자 지시로 GDD에 없거나 모호한 30개 항목(설계 문서 `docs/design/M6_design.md` §5의 Q1~Q30)은 가장 단순한 추천안(전부 A)으로 정했다.
코드의 결정 지점에는 `TODO(GDD-?) Qn` 주석이 있다.
- **§10.2·§11 에셋 (사용자 지시):** 외부 다운로드·Asset Store 에셋을 쓰지 않는다. 소리·텍스처·머티리얼·볼륨 프로필은 에디터 메뉴
  `Ashfall/Setup/Apply M6 Art & Audio`(배치 `M6SceneSetup.ApplyAll`)가 코드로 만든 자리표시다. 목록:
  WAV **27개**(발소리 흙·금속·잔해·물 × 3, 총성 권총·SMG·소총 × 근·중·원 9, 바람·재 폭풍 루프, 금속 붕괴 2, 가이거 클릭, 은신처 드론 BGM — 22kHz 모노 PCM16,
  `Audio/Placeholder/**`; 설계 §2.2 합계 "33"은 표 내용과 맞지 않아 표를 따랐다), 텍스처 3개(`Art/Generated/` 하늘 파노라마·재 점·지형 노이즈),
  팔레트 머티리얼 6개(`Art/Materials/Palette/M_*`) + 하늘·재 머티리얼, 지형 레이어 `TL_AshSoil`, 볼륨 프로필 `VP_RaidAtmosphere`.
  실제 에셋 교체 위치는 설계 §2.3. WAV는 파일이 없을 때만 쓰고(임포트 설정도 새로 쓴 파일에만), SO 클립 필드는 비었을 때만 채워 사용자 교체를 보존한다(Q21).
- **씬 적용 (Q1):** M6 메뉴가 기존 게임 씬(레이드·은신처·메인 메뉴)을 열어 멱등 수정·제자리 저장한다. M4·M5 씬 생성 메뉴도 저장 직전 같은 적용을 부른다.
  확인 씬(M1~M3)에는 적용하지 않는다(Q9). 빌드 설정 변경 없음.
- **§10.2 VFX Graph → Particle System (Q2):** 재 파티클은 Player 자식의 Particle System(World 시뮬레이션, 반경 30m 상자)이다. `.vfx`는 코드로 만들기 어렵다.
  VFX Graph 패키지(§13.1)는 설치된 채 쓰지 않는다.
- **§10.2 Height Fog → Exp² 거리 안개 (Q3):** URP 기본 거리 안개(Exponential², Dried_Blood, 맑을 때 가시거리 450m) + 하늘 지평선 아래 띠를 안개색으로 칠해
  높이 안개 "느낌"을 낸다. 볼류메트릭·높이 안개 셰이더는 만들지 않았다. 레이드 씬은 빌드 셰이더 변형 유지를 위해 항상 안개를 켠다.
- **§10.2 하늘 (Q4):** 생성 파노라마 PNG(천정 Ash_Black → 지평선 Rust_Red, 지평선 아래 Dried_Blood, 방위 245°·고도 8° 흐린 태양) + 내장 `Skybox/Panoramic`,
  같은 방향의 Directional Light "Sun", Trilight 환경광.
- **§10.2 포스트 (Q5, Q30):** 레이드 씬 Global Volume(파이프라인 기본 프로필은 그대로) + 플레이어 카메라 포스트 처리 켬. 채도 −30, Film Grain 0.3, Vignette 0.35는 GDD 값,
  붉은 틴트(Ember 15%)·대비 10·Bloom 0.15/임계 1.1·Tonemapping Neutral은 임시값. 수치 원본은 `AtmosphereConfigSO`이고 볼륨 프로필·하늘 텍스처·머티리얼은
  메뉴가 다시 쓰는 파생물이다. 은신처·메인 메뉴는 카메라 배경 Ash_Black만(Q29).
- **§10.2·§14 PaletteSO (Q6, Q7):** `PaletteSO`(Core, 토큰 7개). 머티리얼은 에디터 메뉴가 팔레트 색을 기록해 "토큰 참조"를 보장하고 테스트로 확인한다(런타임 바인더 없음).
  기존 M2~M4 머티리얼(적·탈출 구역·터렛)도 팔레트로 다시 칠했다(GUID 유지). 블록아웃 구조물 → 머티리얼·지면 재질 매핑은 `PaletteMaterialMap` 표 한 곳.
- **M3 결정 이행 (Q8):** 적 상태 색 표시를 껐다(`AiConfig.showStateIndicator=false`, 확인 씬 포함, 필드 기본값도 false).
- **내장 모듈 (Q10):** audio·particlesystem을 매니페스트에 명시했다(timeline 전이 의존만 있던 상태).
- **§11 발소리 (Q11~Q13):** 지면 재질 흙/금속/잔해/물은 콜라이더의 `GroundSurface` 컴포넌트(Audio)로 정한다. 지형 = 흙, 표시 없는 콜라이더 = 잔해,
  물 = 저수지 웅덩이 3개(트리거·Ignore Raycast). 판정 우선순위는 가장 가까운 표시 > 지형 > 기본. 걸음은 보폭 거리 기반(`StrideCadence`, Core)으로
  플레이어·적 모두 `FootstepTaken` 이벤트를 낸다(적: 웅크린 채 이동 = Crouch, 3.5m/s 초과 = Sprint). AI 청각용 발소리 소음(0.5초)은 그대로.
- **§11 총성 (Q14~Q17):** 새 이벤트 `ShotFired`(플레이어·적, 발당 1회). 거리 근 ≤ 50m / 중 ≤ 250m / 원 ≤ 1200m 중 한 레이어, 게인 구간 선형(1.0 → 0.7 → 0.35 → 0.1, 임시값),
  무기 분류별 세트. 적 총성은 계속 적 AI가 듣지 않는다(`NoiseEmitted`는 AI 전용, M3 규칙 유지). 소리 전달 지연·차폐 없음. 3D 공간음 = Unity 팬 + 순수 함수 게인.
- **§11 환경음·BGM (Q18~Q20, Q22):** 바람(상시, 폭풍 때 커짐), 멀리서 무너지는 금속(40~120초 무작위, 150~400m), 가이거 계수기는 M6에 방사능 구역이 없어
  재 폭풍 방사능률에만 반응한다. BGM은 은신처에만(자리표시 드론, `AudioConfig.hideoutMusic`). 일시정지·종료 오버레이 중 `AudioListener.pause`(timeScale은 그대로 GameFlow만).
  AudioMixer 없음. 효과음 voice 24개 풀(우선순위 총성 > 붕괴 > 적 발소리 > 내 발소리).
- **§8.3 재 폭풍 (Q23~Q26, Q28):** 레이드마다 1회, 경과 180~1200초 중 무작위 시작, 전환 20초 + 최대 강도 180초("3분") + 전환 20초. 예고 없음(Rising 20초가 예고).
  시간은 레이드 경과 시간(일시정지 중 멈춤). 한 프레임에 단계를 건너뛰어도 사이 단계를 순서대로 알린다. Peak 시야 30m = 렌더 안개 + AI 시야 상한 min(기존, 30)(청각은 그대로).
  Raid가 `IAshStormInfo`(Core 서비스)와 `AshStormChanged` 이벤트를 제공한다. 폭풍 중 안개는 Ash_Grey로, 하늘·태양은 어두워지고 재 파티클이 늘어난다.
  하늘은 공유 머티리얼을 바꾸지 않고 복제본만 바꾼다.
- **§4.3 STAT-RAD (Q27):** 재 폭풍 중 플레이어 방사능 누적량만 쌓는다(+1/s × 강도, 레이드마다 0에서 시작). 100 도달 효과·해독제·방독면·화면 표시·이벤트는 M7.
- **§13.2·§13.3:** `Ashfall.Audio`에 첫 코드(`AudioDirector` — 씬 오브젝트, 정적 인스턴스 없음). EditorTools가 Audio·URP 런타임을, 테스트 asmdef가 EditorTools·URP 런타임을 참조한다.
- **§14 스키마에 없는 추가:** `AtmosphereConfigSO`·`AshStormConfigSO`(Raid), `AudioConfigSO`(Audio), `PlayerConfigSO` 보폭 4개, `AiConfigSO` 보폭·속도 4개.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 맑을 때 가시거리 450m·안개 임계 0.05, 태양 방위·고도·흐림·번짐, 하늘 곡선, 환경광, 틴트·대비·Bloom,
  재 파티클 수·크기·속도·화면 크기 상한, 지형 노이즈·타일, 폭풍 시작 창·전환 시간·폭풍 색·하늘 교체 강도·배율, 블록아웃 머티리얼·재질 매핑, 걸음 음량·들리는 거리,
  총성 거리·게인, voice 24, 환경음 간격·거리·음량, 가이거 클릭률, 보폭, 서비스 재조회 주기, 모든 자리표시 소리·텍스처.
- **리뷰 수정 (M6_review.md):**
  재 폭풍 Peak에서 하늘 = 폭풍 안개 색(Q26 공정성 — 안개는 하늘에 걸리지 않아 30m 밖 윤곽이 하늘에 비쳤다). 강도가 `skyFogSwapIntensity`(0.5) 넘으면
  하늘 복제본을 안개 색 단색으로 바꾸고(교체 순간 검정 → 최대 강도에서 안개 색), 그 전에는 생성 하늘이 어두워진다.
  재 파티클 방출 배율은 폭풍 상한(12000)이 담는 만큼으로 자른다(설정 ×4 → 실제 ×2, 정상 상태 입자 수 = 방출 × 수명).
  메뉴 상수였던 보기 수치(하늘 곡선·태양 번짐·재 노이즈 주파수·상자 높이·화면 크기 상한·지형 노이즈 하한·타일)와 오디오 레이 시작 높이·서비스 재조회 주기를 SO로 옮겼다(값 동일).
  폭풍 시작 시각 로그는 개발 빌드·에디터에서만 남긴다. 지면 판정 캐시는 미리 등록한 GroundSurface만 담고(레이드 중 자라지 않음), 청취자 재탐색은 5초 간격.
  M6 적용이 실패하면 M4·M5 씬 메뉴도 예외로 멈춘다(반쯤 적용된 씬을 저장하지 않음). 단계별 확인용 메뉴는 통합 메뉴와 겹쳐 지웠다.

## 2026-10-22 — M7 설계 결정 (§5, §5.1, §5.2, §12.1, §13.3, §14, §15 M7) — 7A 무기
문서 본문은 바꾸지 않았다. 아래는 M7 구현을 위한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
사용자 지시로 GDD에 없거나 모호한 53개 항목(설계 문서 `docs/design/M7_design.md` §5의 Q1~Q53)은 가장 단순한 추천안으로 정했다(Q19 = B, 나머지 = A).
코드의 결정 지점에는 `TODO(GDD-?) Qn` 주석이 있다. M7은 하위 단계 7A(무기)·7B(AI)·7C(생존·제작)·7D(성능·빌드)로 나눠 커밋한다.
이 절은 7A(Q1~Q21)만 담는다. 7B·7C·7D 결정은 나중에 같은 절 아래에 추가한다.
- **§5·§5.2 무기 5종 (Q1~Q8, Q20, Q21):** WPN-SHT-01(12ga, 70RPM, 펠릿 18×8, 내장 튜브 6), WPN-DMR-01(7.62, 40RPM, 110, 탄창 5 MAG-762-5) 추가.
  - Q1 교체 1/2/3 = 주무기1·주무기2·보조무기(§12.1 키 1·2·3, 입력 액션 `Weapon1/2/3`), 든 슬롯·빈 슬롯 키 무시, 교체 0.5초 동안 사격·조준·재장전·탄창 확인 불가, 교체 시 재장전·탄창 확인 취소.
  - Q2 레이드 시작 무기 = 주무기1 → 주무기2 → 보조무기 중 첫 비지 않은 슬롯(교체 시간 없음, 든 슬롯은 저장 안 함). 키트 권총만 반입해도 무장(M5 Q16 해소).
  - Q3 무기 → 탄약 정의는 `WeaponHandlingConfigSO.ammoDefinitions`(AmmoType마다 1개). Q4 무기별 자리표시 1인칭 뷰모델 프리팹(`WeaponDefinitionSO.viewModel`), 처음 들 때 생성·캐시.
  - Q5 샷건 튜브 = 무기 정의의 내장 탄창(`internalMagazine`, 그리드에 없음), 낱탄 = 탄 상자 BOX-12GA-8(`isAmmoBox` 탄창). R = 한 발씩 0.5초, 빠른 재장전 없음.
    탄 상자는 무기에 넣을 수 없고 일반 재장전 탄창 선택에서도 빠진다. 낱탄 순서는 조끼 → 주머니 → (설정 시) 배낭, 탄이 가장 적은 상자부터. Q6 빈 탄 상자는 0발 아이템으로 남는다.
  - Q7 튜브 장전 중 사격 → 발사가 실제로 성립할 때(튜브 1발 이상·새 당김·연사 간격)만 장전을 멈추고 쏜다. Q8 산탄은 펠릿마다 `pelletSpreadDeg` 원뿔(조준 중에도), 지향 퍼짐은 묶음 방향에만.
  - Q20 상인 판매 목록 끝에 SHT·DMR·MAG-762-5·BOX-12GA-8(LMG·튜브 제외). Q21 약실 탄(+1) 없음(M1 유지).
- **§5.1 고장·내구도 (Q9~Q18):** `WeaponItem.Durability`(0~100). 발당 무기별 고정 감소(`durabilityLossPerShot`, Q11). 내구도 < 50이면 발당 (50 − 내구도) × 0.1%로 고장(§5.1 값).
  - Q9 고장 = 탄 소모·마모 없이 발사 안 됨, 고장 중 방아쇠 = 헛격발 소리, 재장전·탄창 확인 불가, T를 1.2초 계속 누르면 해제(떼면 처음부터). 고장 굴림은 발사가 성립하고 탄이 있을 때만.
  - Q10 알림은 소리만(`WeaponClicked`: 헛격발·고장·해제), HUD 없음. Q12 상인·제작·키트·상자 무기는 100, 적 무기는 유형별 스폰 내구도(7B).
  - Q13 내구도 저장 → save.json version 2(아래). Q14 고장 상태는 저장하지 않고 은신처 귀환 = 해제(탈출해 은신처 장비가 되는 무기는 `ProfileService.ApplyRaidResult`에서 푼다).
  - Q15 상인 가격에 내구도 미반영, Q16 수리 없음, Q17 내구도 표시 없음, Q18 적 무기는 마모·고장 없음(스폰 내구도만 — 시체에서 주운 무기는 고장 가능).
- **§13.3 세이브 (Q13):** save.json version 2 — `ItemRecord.durability`(기본 −1 = 해당 없음) 추가. v1은 읽을 때 모든 무기 내구도 100으로 이전하고 다음 저장이 v2로 쓴다(이전 파일은 `.bak`).
  튜브 무기는 내장 탄창 id·탄 수를 기존 장착 탄창 필드에 쓴다. 다른 탄창 id가 기록된 튜브 = 빈 튜브 + 버림 집계, 모르는 id = 빈 튜브 + 알림.
  v2 파일을 M6 빌드로 열면 "지원하지 않는 버전"으로 격리된다.
- **§5.3 (Q19 = B):** 방어구 관통·둔탁 피해·방어구 내구도는 M7 범위 밖(어느 마일스톤 AC에도 없음) — 미구현으로 남긴다(MVP 남은 목록).
- **§13.3 경계:** Core 추가 — `ILooseAmmoSource`(Inventory 등록), `JamRules`·`WeaponWear`, 이벤트 `WeaponClicked`(`WeaponClickKind`), enum `WeaponClass.MachineGun`.
  `WeaponModel`·`FireControl`에 "방아쇠 상태는 기록하되 쏘지 않음" 인자 추가(교체 중·튜브 장전 중 누른 채인 방아쇠가 끝난 뒤 새 당김이 되지 않게).
- **§14 스키마에 없는 추가:** `WeaponDefinitionSO`(internalMagazine, roundReloadTimeSec, pelletSpreadDeg, durabilityLossPerShot, enemyOnly), `MagazineDefinitionSO.isAmmoBox`,
  `WeaponHandlingConfigSO`(jamDurabilityThreshold 50, jamChancePerDurabilityPoint 0.001, jamClearHoldSec 1.2, weaponSwitchTimeSec 0.5, ammoDefinitions),
  `AudioConfigSO`(dryFireClip, jamClearClip, dryFireVolume, jamClearVolume).
- **7A 구현 중 설계와 달라진 점:**
  - `WeaponClass.MachineGun`·WPN-LMG-01(적 전용 경기관총, `enemyOnly`)·MAG-762-100(탄띠)·오디오 파일 이름 규칙 `"Lmg"`를 7B 대신 **7A-1b에서 미리 추가**했다(데이터는 "없을 때만" 생성이라 임시 분류로 만들면 남는다). 7B는 다시 추가하지 않는다.
  - 뷰모델 프리팹 이름은 설계 §2.2 `VM_{분류}`가 아니라 `Prefabs/Weapons/VM_{무기 id}.prefab`(예: `VM_WPN-SHT-01`). 헛격발 WAV는 `Weapon/WPN_DryClick.wav`가 아니라 `Audio/Placeholder/Weapon/WPN_DryFire.wav`.
  - `AudioConfigSO`의 코드 기본 `gunshots`에는 산탄·저격 세트를 넣지 않았다(설계 §4.9와 다름 — 기존 "세트 없는 분류 → 대체" 테스트 유지). 실제 에셋은 메뉴가 세트를 끝에 추가한다.
  - T 짧게/길게는 누른 시간이 아니라 고장 여부로 가른다: 고장 중 T = 누르는 동안 해제 진행, 고장이 아니면 T = RULE-04 탄창 확인(오래 눌러도 1회).
  - `WeaponModel` 생성자는 오버로드가 아니라 선택 인자 `(item, source, looseAmmo = null, wear = default)` — 기존 2인자 호출(적 무기) = 마모 없음 그대로.
  - `M5DataSetup.CreateItemDatabase`를 공개로 앞당겼다(설계 §4.10에선 7C).
  - 세이브 내구도 읽기: 설계 §1.6 "0~100으로 자름"과 달리 **음수(−1 포함)·NaN은 0이 아니라 100**으로 읽는다(손으로 고친 값·필드 누락이 고장 연발 무기가 되지 않게). 100 초과 → 100, 0~100 그대로.
  - LT-CRATE에는 7A 몫으로 BOX-12GA-8(가중치 1.5)·MAG-762-5(0.5)만 더했다. **무기(SHT·DMR)는 넣지 않았다**(상자에서 무기가 나오지 않는 M4 규칙 유지, §3.9 표에 없음).
  - 기존 무기 3종 `durabilityLossPerShot`은 파일에 없거나 0일 때만 쓴다(0 = 미설정 — 뷰모델 메뉴가 먼저 정의를 저장해도 채워진다).
- **자리표시 에셋 (사용자 지시 — 외부 에셋 없음):** 새 WAV **8개**(`Audio/Placeholder/Gunshots/GS_Shotgun_{Near,Mid,Far}`·`GS_Sniper_{Near,Mid,Far}`, `Audio/Placeholder/Weapon/WPN_DryFire`·`WPN_JamClear`,
  22kHz 모노 PCM16 — 합계 WAV 27 → 35), 뷰모델 프리팹 **5개**(`Prefabs/Weapons/VM_WPN-PST-01`·`SMG-01`·`RIF-01`·`SHT-01`·`DMR-01`, 프리미티브 + 팔레트 머티리얼, LMG 없음).
  메뉴 `Ashfall/Setup/Apply M7 Content`(배치 `M7SceneSetup.ApplyAll`)가 데이터 → 프리팹 → 오디오 순으로 만들고, M4 레이드 씬 메뉴도 저장 전에 부른다. 파일은 없을 때만, SO 필드는 비었을 때만 쓴다(사용자 교체 보존).
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 새 무기 반동·조준 시간·발당 장전 0.5초·재장전·지향 퍼짐·펠릿 퍼짐 2.5°·소음 반경·크기·무게·가격·희귀도, 무기 5종 발당 마모
  (PST 0.06·SMG 0.04·RIF 0.05·SHT 0.12·DMR 0.10), 12ga·7.62 탄 관통·초속·비행 시간, 새 탄창·탄 상자 크기·무게·가격, LMG 수치 전부(600RPM·피해 40 등), 고장 해제 1.2초, 교체 0.5초,
  M1 씬 튜브 무기 시작 탄(튜브 가득 + 탄 상자), 펌프·볼트 = 반자동, 상인 목록·LT-CRATE 가중치, 무기 클릭 음량 0.8·우선순위(= 적 발소리), 모든 자리표시 소리·프리팹(크기·위치는 hip/ads 공용 값).
- **리뷰 수정 (M7_review.md "7A 무기"):** 튜브 장전 중 방아쇠가 발사로 이어지지 않으면(누른 채 R, 연사 간격 안 클릭) 장전을 끊지 않는다(Q7).
  고장 난 채 탈출한 무기는 은신처 장비가 되면서 해제된다(Q14 — 같은 실행의 다음 레이드에 남지 않음). 좌클릭을 누른 채 교체가 끝나도 반자동 무기는 새로 당겨야 쏜다.

### 7B AI (2026-10-23, §7, §4.2, §13.3, §14 — Q22~Q31)
문서 본문은 바꾸지 않았다(설계 문서 §1.4 근접 부위 문장만 이번 리뷰 수정에 맞춰 고침 — 아래 "리뷰 수정" 1). 결정 지점에는 `TODO(GDD-?) Qn` 주석이 있다.
- **§7 결정 (Q22~Q31, 전부 추천안 A):**
  - Q22 AI-SNP = 고지대 퍼치 고정(이동 없음), 시야 150m·사거리 200m, 소리 → 돌아보기만, 놓치면 자리에서 LKP 쪽 훑기.
  - Q23 "레이저 반사 신호" = 조준 중에만 켜지는 렌즈 섬광 쿼드(Unlit 발광 Bone × 4 HDR, 크기 = 거리 × tan 0.35°, 표적 눈 쪽), 처음·다시 볼 때 1.5초 경고 뒤 사격. 한 번이라도 놓치면 경고를 처음부터.
  - Q24 AI-MUT 근접 = 사거리 1.8m·준비 0.3초·간격 1.1초(준비 시작 기준)·피해 14, 기존 `IDamageable`, 부위 = 흉부·복부·팔(머리·다리 없음). 준비 중 사거리·시야를 잃으면 헛침(간격 소모).
  - Q25 무리 = 분대 연락(기존 50m) + 표적 둘레 분산 접근(순번 k 각도 = 360°/분대 수 × k, 반경 2.5m, 가까우면 사거리 × 0.5로 좁힘, 사거리 × 0.75 안쪽이면 멈춤).
  - Q26 변이체 소리 = `EnemyVocalized`(첫 경계·근접 준비, 같은 적 쿨다운 4초 — 두 종류 공용, 경계 울음은 분대에서 한 명만) + 자리표시 울음·공격 WAV(들리는 거리 60m 선형).
  - Q27 경기관총 = 적 전용 WPN-LMG-01(`enemyOnly`, `WeaponClass.MachineGun`) — 시체·상인·루트 표에 없음. Q28 호위 3명 = 새 유형 AI-BGD(RDR 값 + 거점, 우회 끔).
  - Q29 고정 거점 = 보스·호위 `anchorRadiusM` 25m, 매몰된 마을 외곽 기존 구조물·CoverPoint(NavMesh 재굽기 없음). 모든 이동 목적지를 거점 반경 안으로 자른다(LKP 자체는 자르지 않음).
  - Q30 배치 = 기존 20명 유지 + SNP 2 + MUT 4 × 2 + BOS 1 + BGD 3 = **적 34명**(SCV 12, RDR 8, SNP 2, MUT 8, BOS 1, BGD 3), 모든 시작·순찰 점이 스폰에서 190m 이상(저격수는 시야 150 + 50m 밖).
  - Q31 보스 시체 = LT-BOS(유물 ITM-REL-01 1개 보장 + MAG-762-5). 해독제·물병·방독면은 7C 아이템이라 7C가 LT-SNP·MUT·BOS 끝에 붙인다.
- **§13.3 경계:** Core 추가 — 이벤트 `EnemyVocalized`(`EnemyVocalKind`), `LootTableSO.guaranteedFirstRoll`(null = 기존 난수 순서 그대로), `DamageInfo` 부위 힌트(아래). AI·Audio는 Core만 참조, 새 asmdef 없음.
- **§14 스키마에 없는 추가:** `EnemyArchetypeSO`(tactics, anchorRadiusM, weaponDurabilityRange, melee*, surroundRadiusM, telegraphSec, scanArcDeg — 기존 SCV·RDR 에셋은 필드가 없어 초기값 = 기존 동작),
  `AiConfigSO`(glintAngularSizeDeg 0.35, vocalCooldownSec 4, 근접 부위 가중치 흉부 1·복부 1·왼팔 0.5·오른팔 0.5, meleeMaxHeightDiffM 1.2, mutantCloseRadiusRatio 0.5, mutantHoldInsideRangeRatio 0.75),
  `AudioConfigSO`(mutantAlertClips·mutantAttackClips, vocalAudibleM 60, mutantAlertVolume 1, mutantAttackVolume 0.9, MachineGun 총성 세트).
- **7B 구현 중 설계와 달라진 점·정한 것:**
  - 근접 부위: 설계의 "캡슐 높이 무작위 지점 → PlayerHealth 기존 판정"은 액터 배율·자세 변화로 머리에 맞아 **Core `DamageInfo` 부위 힌트**(`HasPartHint`·`PartHint`, 새 6인자 생성자 — 기존 5인자 호출은 그대로)로 바꿨다.
    PlayerHealth는 힌트가 있으면 지점 판정 대신 그 부위에 `ApplyHit`(출혈·골절 굴림·복부 파괴 배율 등 기존 규칙 그대로, 방어구 없음). 적·과녁은 힌트를 무시한다. 부위는 `EnemyMelee.PickStrikePart`(AiConfigSO 가중치).
  - 전술 훅 `IEnemyTactics.TryTickRelaxed`(Idle·Patrol·Search를 전술이 대신 채움 — 저격수는 이동 없음)·`TacticsContext.LookAround`(수색 둘러보기를 옮김, 동작 동일)를 더했다. SCV·RDR은 기본 행동 그대로.
  - 저격 훑기 폭 `scanArcDeg` = 전체 폭(좌우 각 30°), 주기 = 기존 `searchLookSec` 재사용, 평시 기준 방향 = 첫 평시 Tick의 몸 방향(배치 yaw). 섬광은 Alert(반응 시간 중)에도 켜고 사격만 Combat.
  - 근접 시야 = 감각 주기의 시야 결과(매 프레임 레이 없음). 근접 사거리는 수평 + 발 높이 차 ≤ 1.2m(리뷰 5).
  - 스폰 무기 내구도(Q12)는 생성자가 아니라 **시체가 될 때**(`CorpseLootBuilder`, 루트 굴림 뒤) 굴린다 — 생성자에서 공유 난수를 쓰면 SCV·RDR 전술 난수 순서가 밀린다. 살아 있는 적의 무기 내구도는 100.
  - `weaponDurabilityRange` (100,100) = 코드 초기값 = 미설정으로 본다(데이터 메뉴가 그때만 기록). 의도적으로 100~100이면 99.9~100처럼 쓴다.
  - 울음 WAV 경로 = `Audio/Placeholder/Enemy/MUT_Alert.wav`·`MUT_Attack.wav`(종류마다 1개 — 설계 §2.2 `Creatures/MUT_*_01~02`와 다름). MUT_REF 배회 경로는 스폰 거리 때문에 설계 (300, 40)에서 서쪽 (232~275, 15~45)으로 옮겼다.
  - 적끼리 시야 가림(탄도와 같은 규칙)은 바꾸지 않았다.
- **자리표시 에셋 (사용자 지시 — 외부 에셋 없음):** 새 WAV **5개**(`Audio/Placeholder/Gunshots/GS_Lmg_{Near,Mid,Far}`, `Audio/Placeholder/Enemy/MUT_Alert`·`MUT_Attack`, 22kHz 모노 PCM16 — 합계 WAV 35 → 40),
  머티리얼 **3개**(`Art/Materials/Palette/M_Mutant`(Ash_Grey)·`M_Boss`(Ember, 발광 ×0.3), `Art/Materials/M_SniperGlint`(URP Unlit, Bone × 4 HDR)). 레이드 씬에 M7 적 14명(`Enemy_M7_*`)·순찰 경로 2개.
  메뉴 `Ashfall/Setup/Apply M7 Content`(배치 `M7SceneSetup.ApplyAll`) — 재실행 바이트 동일.
- **성능 테스트 교전 전제 (7B-4, 오케스트레이터 결정 (b)):** 예산·단언은 그대로. "90초 안에 전원 교전 진입"은 M4 고정 배치 20명(SCV·RDR)에만 건다. M7 14명은 씬·AI 틱(34명)에 남는다.
  측정 직전 모든 적 탄을 테스트 도우미로 보충하고(무적 표적에 쏘아 탄이 바닥나는 문제), 표적 근처 변이체 무리는 시작 때 표적에서 멀리 옮긴다. M7 유형의 최악 부하는 7D `M7PerformanceTests`.
  `RaidPerformanceTests`의 테스트 이름을 `RaidScene_34Enemies_NaturalAndM4FullCombat_…`으로 바꿨다(리뷰 12).
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 설계 §3.4 표 전체(SNP·MUT·BOS·BGD HP·시야·청각·이동·반응·오차·연사·사거리·내구도 범위·근접·둘레 반경·경고·훑기), 표에 없는 칸(SNP 오차 배율 = RDR, BOS "RDR 오차 × 1.3"은 기본·거리 오차에만),
  섬광 각 크기 0.35°·HDR ×4·눈 앞 0.12m, 울음 쿨다운 4초·들리는 거리 60m·음량 1/0.9·우선순위(= 금속 붕괴), 근접 부위 가중치 1/1/0.5/0.5·높이 차 1.2m·둘레 비율 0.5/0.75, 배치 좌표·분대 번호 10~14·퍼치 높이 차 ≥ 5m,
  LT-SNP/MUT/BOS 내용·굴림 수, ITM-REL-01 값(1×1·0.2kg·8000), M_Mutant·M_Boss 색 토큰, 모든 자리표시 소리.
- **리뷰 수정 (M7_review.md "7B AI" 1~15):**
  1. (높음) 변이체 타격이 선 자세 약 10%, 웅크림·엎드림 대부분 머리 — `DamageInfo` 부위 힌트 + `PickStrikePart`로 바꿨다(위). 타격 분포 상수·표적 캡슐 높이 캐시·반지름 0.2배 추정은 없앴다. 지점 = 표적 중심.
  2. 거점 반경 밖 엄폐 선택 — `CoverQuery`에 거점 중심·반경(숨는 자리·내다보는 자리 둘 다 반경 안만, Q29 TODO 해소). 거점 유형이 엄폐를 못 찾은 대체 행동(약탈자 Combat)은 선호 거리 밖이어도 다가가지 않고 보이고 사거리 안이면 제자리 사격.
  3. 남은 하드코딩(변이체 둘레 0.5·0.75, 타격 분포·반지름)을 AiConfigSO로 옮기거나 없앴다. 4. PlayMode 성능 — 7B-4에서 처리(위 교전 전제).
  5. 근접 사거리 높이 차 상한 `meleeMaxHeightDiffM`. 6. 거점 중심 = 분대 리더(보스) 거점 공유(리더가 거점 유형이 아니거나 없으면 자기 첫 Tick 위치, 리더가 정하기 전에는 기다림).
  7. 거점 경계에서 자른 목적지를 NavMesh에 붙인다(샘플이 반경 밖이면 다시 자름, 같은 입력은 캐시). 8. `CorpseLootBuilder` 주석을 동작에 맞춤(루트 굴림에서 빼는 탄창 = 이 시체의 적 전용 무기 탄창만).
  9. `M7EnemySceneApply`가 모든 NavMesh 스냅을 씬 변경 전에 계산(중간 실패로 씬이 반쯤 바뀌지 않음). 10. 경계 울음 = 분대에서 먼저 경계에 든 한 명만(`Squad.TryClaimAlertVocal`, 같은 쿨다운). 공격 울음은 대원마다.
  11. 커밋 에셋 재실행 테스트 — 커밋 전 git status·md5 확인 절차로 처리(코드 변경 없음). 12. 테스트 이름 34명 반영. 13. `AiDirector` 등록 목록·스케줄러 초기 용량 32 → 64.
  14. 이 소절. 15. 변이체가 표적과 0m까지 겹침 — 안 보일 때도 LKP로 곧장 가지 않고 LKP 둘레 자리로 가며 LKP와 사거리 × 0.75 안쪽이면 멈춘다. EnemyAgent는 근접 유형이 표적과 수평 사거리 × 0.75 안이면 매 프레임 멈추고 멈추는 순간 NavMeshAgent 속도를 0으로(감속 미끄러짐 방지).

### 7C 생존·제작 데이터 (2026-10-23, §4.3, §6, §8.1, §8.3, §9, §14 — Q32~Q48, 7C-1b)
문서 본문은 바꾸지 않았다. 아래는 데이터 메뉴 `Ashfall/Setup/Create M7 Survival Data`(`M7SurvivalDataSetup`)가 쓰는 값이며, 결정 지점에는 `TODO(GDD-?) Qn` 주석이 있다.
- **GDD 값 (§4.3):** `PlayerConfig.asset`에 수분 100·−0.1/s, 수분 0 → −0.5 HP/s(부위마다, Q33), 에너지 100·−0.07/s, 에너지 0 → 스태미나 재생 × 0.5, 방사능 누적 100 → 최대 HP × 0.8을 명시 기록(파일에 없는 필드만).
  임시값: 복부 파괴 수분 감소 × 2(Q34), 방사능 오염 경고 50·Low 경계 25%(Q43).
- **새 아이템 (설계 §3.7, 전부 TODO(GDD-?)):** ITM-WTR-01 물병(1×2·0.6kg·수분 +50·3초·2회·150), ITM-FOD-01 통조림(1×1·0.4kg·에너지 +60·4초·1회·200),
  MED-ANT-01 해독제(1×1·0.1kg·방사능 해제·3초·1회·600) — 셋 다 `MedicalDefinitionSO`(Q42). GEAR-GSM-01 방독면(2×2·0.8kg·1500) = `ArmorDefinitionSO { Helmet, armorClass 0, covers 빈, blocksStormRadiation }`(Q37·Q38).
- **정유소 구역 (§8.1, Q39):** `RaidConfig.radiationZones` RAD-REF = 중심 POI-REF (300, −60), 반경 70m, 0.5/s, 가장자리 감쇠 15m.
- **제작 레시피 (§9, Q47):** `Data/Hideout/CraftingConfig.asset` 8개(설계 §3.8). **설계와 다른 점:** RCP-AMMO-12GA(BOX-12GA-8)·RCP-MED-SPL(부목)의 재료를 JNK-01 × 1 → **× 2**로 올렸다.
  사유: 설계의 가치 확인 규칙 "산출 매도 합 ≤ 재료 매수 합"(매도 0.4·매수 1.0)에서 × 1이면 산출 매도 80·48 > 재료 매수 40이라 제작으로 탄피가 늘어난다. × 2면 80 ≤ 80, 48 ≤ 80.
- **희귀도 (§6, Q48):** 설계 §3.11 표를 데이터로 기록 — 기존 에셋은 희귀도가 Scrap(0 = M2 메뉴 기본값, 미설정)일 때만 쓴다.
  **설계와 다른 점:** WPN-PST-01·WPN-SMG-01·ITM-FLR-01은 M3·M4 메뉴가 이미 Common으로 만들어 "미설정"이 아니므로 Common으로 남는다(§3.11은 우수). 바꾸려면 에셋을 직접 고칠 것.
- **상인·루트 (TODO(GDD-?)):** 상인 목록 끝에 물·음식·해독제·방독면(12ga 탄 상자는 7A가 이미 넣음).
  LT-CRATE += 물 2·음식 2·해독제 0.5·방독면 **0.5**·JNK-02 0.5, LT-SCV += 물 1·음식 1·BOX-12GA-8 0.5, LT-RDR += 물 1·음식 1, LT-SNP += 해독제 1·물 1, LT-MUT += 해독제 0.3, LT-BOS += 방독면 1·해독제 1.
  **설계와 다른 점:** LT-CRATE 방독면 가중치 0.3 → 0.5. 사유: M4 Q7-A "신호탄(0.5)이 상자 테이블의 최저 가중치"(EXT-03 희소성) 규칙을 유지한다.
- **7C 구현 결정 (7C-2a·2b·2c·3, Q32~Q48):**
  - Q33 수분 0 → **머리를 뺀** 파괴되지 않은 부위마다 −0.5 HP/s(복부 배율·상태이상 굴림·파괴 부위 분산 없음, 사망하면 그 호출에서 멈춤). 머리를 빼서 흉부 80이 **160초**에 먼저 0 → 사망(사인 흉부) — Q33 A 문구에 맞춤.
    7C-2a는 모든 부위를 깎아 머리 35가 70초(피폭 중 56초)에 먼저 0이 됐다(리뷰 7C 1, 오케스트레이터 결정 (b)). 전신 사인 문구("탈수")는 MVP 남은 목록(TODO(GDD-?) Q33).
  - 수분·에너지 수치는 `SurvivalModel`에서 **public 읽기**(설계 "internal"과 다름 — 테스트 어셈블리가 읽고 InternalsVisibleTo를 늘리지 않음). UI는 Player를 참조하지 않고 `IBodyStatusView`는 등급만이라 RULE-05 구조는 그대로.
  - 물·음식·해독제(부위 대상 없음) = 전신 대상 `MedicalUseModel.SystemicTarget = Thorax`. 부위 대상이 있는 아이템은 부위가 우선이고 완료 때 전신 효과도 함께. 해독제는 선량 > 0일 때만 시작. 현재 UI는 이 Target을 표시하지 않는다.
  - 생존 감소는 레이드가 아닌 씬(M1~M3 테스트 씬)에서도 돈다(1000초에 0이라 기존 테스트 무관). 은신처에는 PlayerHealth가 없다.
  - 제작: 무효 레시피(재료 없음·정의 없음·개수 < 1 등)·중복 id는 목록에서 빼고 `UnknownRecipe`(데이터 오류 취급, TODO(GDD-?) Q47). 재료는 보관함 배치 순(위→아래·왼→오른쪽, 상인 매도와 같음)으로 고른다.
    같은 정의가 여러 줄이면 합계로 판정하고, 재료 칸 충족 표시도 버튼과 같은 합계 기준(리뷰 7C 4). 결과 문구는 허브 메시지 줄.
  - 읽기 전용 프로필(M5)에서 상인 거래는 되고 **제작만 `Unavailable`**(버튼 비활성) — 의도한 차이(리뷰 7C 3, 유지). 사유: 제작은 보관함 재료를 소비하고 새 아이템을 만들어 저장 실패 시 무엇을 잃었는지 혼동이 크다.
  - 가이거 입력 = 폭풍 방사능률 + 구역 률(방독면 무관, Q41). 플레이어 선량 = 폭풍(방독면이면 0) + 구역(발 위치), 가이거 조회 위치 = 청취자 눈. 지도에 구역 표시 없음(Q40).
  - 희귀도: WPN-PST-01·WPN-SMG-01·ITM-FLR-01은 **Common 유지**(설계 §3.11은 우수 — M3·M4 결정·테스트 보존, 오케스트레이터 결정, 리뷰 7C 9). 바꾸려면 에셋을 직접 고친다.
  - 생존 설정 필드에 인스펙터 범위 가드: 최대·감소율·피해·배율(복부)·선량 경계 `[Min(0)]`, 스태미나 재생 배율·피폭 최대 HP 배율·Low 경계 비율 `[Range(0,1)]`(직렬화 값 영향 없음, 리뷰 7C 6).
- **§13.3 경계 (7C):** Core 추가 — `SurvivalStat`·`SurvivalTier`, `IBodyStatusView.GetSurvivalTier`(등급만), `IRadiationField`(Raid `RadiationField` 등록), `IHideoutService.Recipes`·`TryCraft`(`CraftResult`·`CraftingRecipeView`·`CraftingInputView`),
  `MedicalDefinitionSO`(hydrationRestore·energyRestore·curesRadiation), `ArmorDefinitionSO.blocksStormRadiation`. 기능 → Core만, 새 asmdef 없음, 정적 인스턴스 없음.
- **자리표시 에셋 (사용자 지시 — 외부 에셋 없음):** 새 머티리얼 **1개** `Art/Materials/Palette/M_Toxic.mat`(URP Lit, Toxic #7A8A2A, 매끄러움 0.3, 발광 ×0.25, `PaletteMaterialMap.Key.Toxic`),
  레이드 씬 최상위 `RadiationField`·`M7_ToxicMarkers`(자식 **54개** — 둘레 기둥·판·드럼통 각 12, 구역 안 무더기 3 × 6, 콜라이더 없음, NavMesh 굽기 제외), 은신처 씬 `HideoutController.crafting` = CraftingConfig.
  WAV 변경 없음. 메뉴 `Ashfall/Setup/Apply M7 Content`(데이터 → 레이드 → 은신처, 재실행 바이트 동일), 단독 `Ashfall/Setup/M7/Apply Radiation Zone (Raid Scene)`.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?), 7C):** 수분 0 해석(머리 제외 부위마다, Q33), 복부 파괴 수분 감소 × 2(Q34), 등급 경계(Low = 최대 × 0.25, 방사능 오염 50, Q43),
  물병·통조림·해독제·방독면 값(크기·무게·회복량·사용 시간·횟수·가격·희귀도), RAD-REF 위치·반경 70m·0.5/s·감쇠 15m, 레시피 8개 재료·이름(Q47), 상인 추가분·루트 가중치,
  Toxic 표시물 배치(둘레 = 반경 − 0.5m, 기둥 각도 19° + 30k, 드럼통 각도 표, 무더기 오프셋), M_Toxic 매끄러움·발광, 제작 탭 이름 칸 너비 220px, 제작 결과 문구.
- **리뷰 수정 (M7_review.md "7C 생존·제작" 1~9):** 1. 탈수에서 머리 제외(위). 2. 이 소절. 3. 읽기 전용 제작 Unavailable 유지·사유 기록(위). 4. 재료 칸 충족 = 정의 합계 기준.
  5. 탈수 피해는 사망하면 남은 부위를 깎지 않는다. 6. 생존 필드 `[Min]`/`[Range]`. 7. `GeigerClickModel` 주석(폭풍 + 구역). 8. 커밋 에셋 재실행 테스트 — 커밋 전 git status·md5 확인 절차로 처리(코드 변경 없음).
  9. 희귀도 Common 유지(위). CLAUDE.md "구조"의 7C 타입은 마일스톤 끝에 갱신한다.

### 7D 성능·빌드·폴리시 (2026-10-23, §6, §10.1, §13.1, §13.3, §14, §15 M7 AC3 — Q49~Q53)
문서 본문은 바꾸지 않았다. 결정 지점에는 `TODO(GDD-?) Qn` 주석이 있다.
- **결정 (Q49~Q53, 전부 추천안 A):**
  - Q49 §6 희귀도 색 = 칸 테두리 USS(`UI/RarityStyle` + `Ashfall.uss`): 고철 Ash_Grey·일반 Bone·우수(녹) = **Toxic 토큰 재사용**·희귀(청) = `--rarity-rare-blue: #4E6C8A`(**§10.1 팔레트 밖 유일한 색**)·유물 = Ember.
    인벤토리·보관함 칸·장비 슬롯·퀵슬롯·드래그 고스트·상인 줄 이름 칸(왼쪽 테두리). 검색 전 "?" 아이템에는 붙이지 않는다(§6 검색 연출).
    **설계와 다른 점:** UI Toolkit에 box-shadow가 없어 유물 "발광"은 **Ember 2px 테두리 + Rust_Red 바탕**(발광 없음)으로 근사. 우수 Toxic 재사용으로 "Toxic = 유일한 비적색 강조"(§10.1)가 인벤토리에서 약해진다 — 기획 확인 대상(§10 목록).
  - Q50 성능 판정 = 배치모드 자동(CPU 예산 그대로: AI 평균 ≤ 1.0ms·99% ≤ 3ms, 투사체 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms(렌더 제외), Audio + 폭풍 + 방사능 ≤ 0.3ms, 정상 상태 GC 0) + **1080p 60fps는 사람이 개발 빌드로 측정**(§7.1 P1, `Player.log`의 `[DevDiagnostics]`).
  - Q51 개발 빌드 진단 = Core `DevDiagnostics`(개발 빌드 플레이어에만, 릴리스·에디터 Play에 없음): 10초마다 프레임 시간 한 줄(링 4096), `-ashfallSmoke <폴더>` 스모크(8단계, 60초, 종료 코드 0/1).
    기술 상수(게임 수치 아님): 로그 간격 10초·링 4096·스모크 제한 60초·레이드 10초. 진단 코드는 릴리스 바이너리에도 컴파일되지만 `ShouldAttach`·`Debug.isDebugBuild` 가드로 실행되지 않는다(릴리스 exe에 `-ashfallSmoke`를 줘도 로그 0줄·폴더 없음 — 7D-1 확인, 리뷰 11 유지).
  - Q52 IL2CPP = 지금은 Mono 검증 + `BuildWindows64Il2Cpp(Development)` 메서드(모듈 없으면 "IL2CPP 모듈 설치 필요" 예외 = 종료 1, 백엔드는 빌드 동안만 바꾸고 finally에서 되돌림). **이 PC = 모듈·C++ 툴체인 없음 → 사용자 설치 대기**,
    설치 확인 뒤 7D-IL2CPP에서 ProjectSettings를 IL2CPP로 영구 전환·커밋. 임시 전환 뒤 ProjectSettings.asset에 새 키가 남을 수 있어 빌드 전후 `git status ProjectSettings`·md5를 확인하고 임시 diff는 되돌린다(M7 설계 §6 7D-IL2CPP 절차, 리뷰 중간 2).
  - Q53 스트리핑 = `Assets/_Project/link.xml`이 런타임 Ashfall 어셈블리 9개 `preserve="all"`(EditorTools 제외), Managed Stripping Level 기본 유지. `BuildToolsTests`가 asmdef(includePlatforms) ↔ link.xml 양방향 확인.
- **§13.3 경계 (7D):** Core 추가 — `Diagnostics/`(`FrameTimeStats`·`SmokeRunner`·`SmokeScenario`·`DevDiagnostics`), 이벤트 **`AshStormStartRequested`**(스모크 "폭풍 강제 시작" — Core에 폭풍 명령 경로가 없어서. Raid `AshStormController`가 구독, 개발 빌드·에디터에서만 `ForceStartNow`).
  `GameManager` Awake 한 줄(진단 부착). Diagnostics는 Core 서비스·이벤트·GameFlow만 쓴다. 새 asmdef·정적 인스턴스 없음.
- **스모크 명령과 정책:** `Builds/Windows64Dev/Ashfall.exe -batchmode -nographics -ashfallSmoke <폴더> -logFile <로그>` → Boot → MainMenu → Hideout(저장·다시 읽기, version 2) → Raid(raidInProgress 파일 확인, 폭풍 강제 시작, 10초) → 포기 → Hideout(결과 Abandoned, 저장) → 종료 0.
  실패·예외 로그(다른 스레드 포함 — `logMessageReceivedThreaded`)·60초 초과·값 없는 인자 = 종료 1. **세이브 폴더 정책(리뷰 낮음 4):** 폴더가 실제 `persistentDataPath`와 같으면 종료 1, 시작 때 그 폴더의 `save.json`·`.bak`·`.tmp`·`save.corrupt.json`을 지우고 빈 프로필로 시작(다른 파일은 그대로).
  실행하는 쪽은 바깥 제한 시간(180초)을 둔다 — 메인 스레드가 멈추면 플레이어 안의 60초 판정이 돌지 않는다(리뷰 낮음 5).
- **7D-3 측정 (PlayMode `M7PerformanceTests`, 실제 레이드 씬):** 34명 무적, 폭풍 Peak(시야 30m), 표적 = 보스 거점 5m(지면), 변이체 8명 2~6m 둘레, 저격수 2명 22m, M4 20명 12~24m 고리, 플레이어 펌프 샷건 연사.
  7D-3(표적 0.8m 띄움): AI 평균 0.175~0.187ms·99% 0.46~0.56ms, 투사체 0.05ms, 프레임 중앙값 1.9~2.1ms(렌더 제외), Audio + 폭풍 + 방사능 0.005ms, GC 0(6경로, 2400여 프레임), 동시 탄 최대 30 / 상한 624 — 전부 예산 안.
  7D-4(발견 1 수정 뒤, 표적 0.8m): 결과는 M7 설계 "7D 리뷰 수정 메모".
- **7D-3 발견 1 수정 — 적 접근 정지 거리:** 변이체 무리가 표적을 감싸 시야가 막히면 SCV·RDR·호위가 LKP(= 표적 발)까지 걸어 들어와 표적과 0~2m로 겹쳤다(NavMeshAgent는 플레이어 CharacterController와 부딪히지 않음).
  약탈자 경로(SCV, 엄폐를 못 찾은 RDR·보스·호위)가 LKP·보이는 표적으로 다가갈 때 그 점에서 자기 쪽으로 **`AiConfigSO.approachStopDistanceM` 2.0m**(TODO(GDD-?)) 앞 점(NavMesh 샘플, 같은 LKP는 캐시)을 목적지·도착 판정으로 쓴다. 이미 그 안이면 제자리.
  변이체는 기존 둘레 규칙, 수색 지점·엄폐 이동은 그대로. 기존 EditMode 단언 3개(목적지 = LKP)를 정지 점으로 고쳤다. `M7PerformanceTests`에 "사수 최근접 ≥ 1.0m" 단언을 더했다.
  표적 0.8m 띄움은 유지: 지면으로 되돌리면 3회 중 1회 보스가 90초 안에 교전하지 못했다 — 테스트의 300m 소음으로 사수 20여 명이 LKP 둘레 2m 고리에 한꺼번에 모여(회피로 일부 0.6~1.1m까지 밀림) 변이체와 함께 보스 시선을 막는다(적끼리 시야 가림은 7B 규칙 그대로). 실제 플레이의 군집 밀도·시야 가림은 §10 목록.
- **GC 판정 (7D-3 발견 2, 유지):** 탄·시야 레이가 처음 맞힌 콜라이더의 관리 래퍼를 엔진이 만드는 1회 할당(예열 전 6회 중 3회 1블록, 예열 뒤 0)은 정상 상태 프레임 할당이 아니므로 GC 구간 전에 씬 콜라이더 래퍼를 미리 만든다(`WarmColliderWrappers`).
  `GC.GetAllocatedBytesForCurrentThread`는 이 Mono에서 0을 돌려줘 바이트 판정은 쓰지 않는다. 재발행 이벤트 기록 상한 512 → 1024(발견 3).
- **리뷰 수정 (M7_review.md "7D" 1~11):** 1. 드래그 고스트 "패널 밖 = 버리기" Ember 테두리가 희귀도 색에 덮이던 M2 회귀 — `.inv-item.inv-ghost.inv-ghost--outside`(클래스 3개, 희귀도 규칙 뒤) + USS 우선순위 검사 테스트. 유물 고스트는 안/밖 색이 같다(Ember).
  2. IL2CPP 임시 전환 뒤 ProjectSettings 확인 절차를 설계 §6 7D-IL2CPP에 기록. 3. 프레임 로그에 전체 표본 수(`TotalAdded`)·버퍼 용량, 링 버퍼 주석(약 410fps 이상이면 최근 구간만). 4. 스모크 폴더 정책(위).
  5. 바깥 제한 시간 기록, 값 없는 `-ashfallSmoke` → 종료 1 실제 확인. 6. `logMessageReceivedThreaded` + Interlocked 카운터·첫 예외 플래그(메인 스레드가 읽어 실패 처리). 7. `AshStormFlowTests`에 이벤트 → Pending 벗어남 케이스.
  8. `RarityStyle.ClassFor` 범위 밖 = null(기본 테두리, 예외 없음). 9. 이 소절. 10. `BuildToolsTests` — 설치 확인을 구현과 다른 계산(고정 경로 + 파일 패턴 + 빌드 대상 지원)으로, link.xml 역방향·중복 검사, 에디터 전용 = includePlatforms만.
  11. 진단 코드가 릴리스에 남는 것은 유지(가드로 실행 안 됨, 위 Q51).
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?), 7D):** 희귀 청색 `#4E6C8A`, 유물 2px·상인 줄 왼쪽 테두리 3px/4px, 적 접근 정지 거리 2.0m, Q51 기술 상수(10초·4096·60초·10초), 스모크 바깥 제한 180초.
- **7D-IL2CPP 전환 (2026-10-23, §13.1 "IL2CPP 빌드", Q52):** 사용자가 Hub 모듈 "Windows Build Support (IL2CPP)"와 **Visual Studio Community 2026**(18, MSVC 14.51, Windows SDK 10.0.26100)을 설치 → Unity 6000.3.26f1이 VS 2026 툴체인을 그대로 인식(`C_Win_x64_VS2026`). VS 2022·MSVC v143 따로 필요 없음.
  - 임시 전환 빌드(전환 전): `BuildWindows64Il2CppDevelopment` 21분 51초(첫 C++ 컴파일), `BuildWindows64Il2Cpp` 17분 8초 — 둘 다 Succeeded·오류 0·경고 0(C++ 컴파일러 경고 C4717 1건은 Cinemachine 패키지 생성 코드). 빌드 뒤 ProjectSettings에 남은 임시 키(`scriptingBackend: Standalone: 0`·`il2cppStacktraceInformation: Standalone: 0`)는 매번 HEAD로 되돌림(md5 동일 확인).
  - 영구 전환: 메뉴 `Ashfall/Build/Set Standalone Backend IL2CPP`(`BuildTools.SetStandaloneBackendIl2Cpp`, `PlayerSettings` API) → ProjectSettings diff = `scriptingBackend: Standalone: 1` + `il2cppStacktraceInformation: Standalone: 0`(MethodOnly 명시 — IL2CPP 개발 빌드의 스택 추적 임시 변경·되돌림 뒤에도 파일이 같다) 두 키뿐.
  - 빌드 메서드: `BuildWindows64`(릴리스)·`BuildWindows64Development`(개발)가 이제 IL2CPP. 이전 `BuildWindows64Il2Cpp(·Development)`는 같은 동작의 다른 출력 폴더로 남김(백엔드 되돌림 무동작). **Mono 대체 경로** `BuildWindows64Mono(·Development)`(`Builds/Windows64Mono*/`, 이 빌드 동안만 Mono).
    빌드는 출력 폴더에 다른 백엔드의 이전 빌드(Mono = `MonoBleedingEdge`, IL2CPP = `GameAssembly.dll`)가 있으면 그 폴더(Builds/ 아래만)를 먼저 지운다 — 영구 전환 뒤 첫 `BuildWindows64`가 "Build path contains a project previously built with the Mono2x scripting backend"로 실패했다(이 실패한 빌드는 Input System의 `preloadedAssets` 항목과 Performance Testing의 `Assets/Resources/` 임시 파일을 남겨 둘 다 되돌림).
  - 결과(영구 전환 뒤): `BuildWindows64` Succeeded·경고 0, 20초(Bee 캐시), 배포 크기 약 122MB(`totalSize` 1,340,621,650 bytes는 `*_BackUpThisFolder_ButDontShipItWithYourGame` C++·심볼 포함 — Mono 릴리스 110MB). 15초 실행(창 1280×720): Boot → MainMenu, 예외 없음.
    `BuildWindows64Development` Succeeded·경고 0, 17분 12초, 배포 약 251MB. 스모크(바깥 `timeout 180`) 종료 0·8/8 단계 13.8초·오류·예외 로그 0, 세이브 version 2·raidInProgress false, 실제 persistentDataPath `save.json`·`.bak` md5 그대로. 값 없는 `-ashfallSmoke` → 종료 1. 스택 추적에 파일:줄 표시.
    `BuildWindows64Mono` Succeeded·경고 0, 110,253,574 bytes, 백엔드 IL2CPP로 되돌림·ProjectSettings 동일.
  - **link.xml·제품 코드 변경 없음**(스트리핑·AOT·JsonUtility 문제 없음 — 스모크로 세이브 쓰기·읽기 확인). 바뀐 것은 `EditorTools/BuildTools.cs`·`BuildToolsTests`(백엔드 Mono 고정 → IL2CPP 고정, 다른 백엔드 출력 판정)·ProjectSettings 두 키.
  - 테스트: EditMode 1797/1797, PlayMode 181/181 × 2회 연속(종료 0). 영구 전환 뒤 테스트 전후 ProjectSettings md5 동일.

## 2026-10-24 — MVP 마무리 설계 결정 (§5.1, §5.3, §7, §9, §12, §13.3, §14 — MF-Q1~Q24)
사용자 결정(2026-10-24): §5.3 방어구를 MVP에 넣고(M7 Q19 B 뒤집기), 무기 내구도 대략 표시·상인 수리·가격 반영(M7 Q15·Q16·Q17 뒤집기),
전신 사인 문구(M7 Q33 TODO 해소), 보관함 업그레이드(M5 Q1 뒤집기). 나머지 모호한 항목(`docs/design/MVP_finish_design.md` §5 MF-Q1~Q24)은 ★추천안,
단 MF-Q16(적 방어구)은 사용자 결정 "플레이어와 적 모두"에 따라 B. 코드 결정 지점에는 `TODO(GDD-?) MF-Qn`. 하위 단계 F1 → F2 → F3 → F4, 단계마다 커밋.
- **F1 §12 사인 (Q1~Q4):** Core `DeathCauseKind { Wound, Bleeding, Dehydration }`, `PlayerDied.Kind`·`RaidResult.DeathKind`(선택 인자, 기존 = Wound).
  결과 제목 "사망 — 탈수"/"사망 — 출혈"(출혈·중출혈 틱 사망, 파괴 부위 분산 포함)/피격은 기존 "사망 — 부위". 방사능은 최대 HP만 줄여 사인이 될 수 없다 — 문구 없음.
- **F2 §9 보관함 업그레이드 (Q5~Q9):** 너비 10 고정, 30 → 40줄(5000 탄피) → 50줄(12000)(`HideoutConfigSO.stashUpgrades`, TODO). 허브 공통 줄 버튼, 확인 없음,
  읽기 전용 프로필에서도 허용(상인과 같음). 그리드는 제자리 확장(`ItemGrid.TryGrow`), 확장 실패 시 탄피를 깎지 않는다. 줄 수가 이전 단계 이하인 설정 단계부터는 무시.
- **F2 §13.3 세이브 version 3:** `SaveData.stashLevel`(v1·v2 → 0, 음수 → 0, 설정 최대보다 크면 최대로 자름·환불 없음). v4 이상 = 지원 안 함(격리, 기존 규칙). v3 파일을 이전 빌드로 열면 격리 + 새 프로필.
  에셋은 `Ashfall/Setup/Apply MVP Finish Data`(`MvpFinishDataSetup.ApplyAll`, `Apply M7 Content` 끝에서도 호출, 파일에 없는 필드만)가 기록.
- **F3 §5.1 내구도 표시·수리·가격 (Q10~Q15):** 무기·방어구(`IDurableItem`) 이름 뒤 "(양호/낡음/위험)" — 비율 ≥ 0.5 / 0.25~0.5 / < 0.25(`HudConfigSO`, TODO), 숫자 없음(RULE-04).
  인벤토리 칸·장비 슬롯·상인 매도 줄. 매도 = 내림(base × 0.4 × 비율) + 장착 탄창, 매수는 새 물건(비율 1)이라 공식은 같고 결과가 같다.
  수리 = 보관함 아이템만, 상인 매도 줄 [수리 (N 탄피)], max(1, 올림(base × 0.5 × 잃은 비율)), 100%로(`TraderConfigSO.repairCostRatio`, TODO, OnValidate가 매도 배율 이상으로 올림). 수리 후 되팔기 이득 없음(0.5 ≥ 0.4).
- **F4 §5.3 방어구 (Q16~Q24):** 관통 확률 clamp01(0.5 + (관통력 − 등급) × 0.25), 비관통 = 피해 × 0.3(감소된 피해로 기존 부위 규칙·상태이상 굴림), 방어구 내구도 −= 원피해 × 0.2(비관통만, TODO).
  `covers` 부위만, 등급 < 1·covers 빈(방독면)·내구도 0이면 막지 않음, 내구도는 등급에 영향 없음. 근접(관통 0)도 같은 공식. 피격음·HUD 없음.
  순수 판정 Core `ArmorRules`(난수 `System.Random` 주입, 확률 0·1이면 난수 미소비, `Absorb` = 판정 + 내구도 감소), 런타임 `ArmorItem`(Core, `ItemFactory`가 내구도 최대로 생성).
  세이브: 방어구 내구도는 `ItemRecord.durability` 재사용(절대값, v < 3·음수·NaN → 최대) — 버전은 F2의 v3 한 번만 올렸다.
  수치는 `PlayerConfigSO`·`AiConfigSO` 같은 4필드(0.5·0.25·0.3 = §5.3, 감소율 TODO), 두 에셋 값이 같은지 테스트로 고정.
- **F4 §7 적 방어구 (MF-Q16 B — M3 "적에게 방어구 없음" 뒤집기):** 사유 = 사용자 결정 "플레이어와 적 모두(§7 동일한 피해 공식)".
  `EnemyArchetypeSO.helmet`·`bodyArmor`(AI 어셈블리, null = 없음), 적마다 런타임 내구도(`EnemyArmorModel`), 머리 ↔ Head, 몸통 ↔ Thorax/Stomach, 다리 = 없음.
  데이터: AI-BOS = ARM-HLM-01 + ARM-BDY-01, AI-BGD = ARM-BDY-01, 나머지 유형 없음(TODO). 시체 루트에는 넣지 않는다(TODO). 방어구 난수는 별도 인스턴스(기존 AI 난수 순서 불변).
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 보관함 40/50줄·5000/12000 탄피, 등급 경계 0.5/0.25, 수리비 비율 0.5, 방어구 내구도 감소 × 0.2,
  매도 내구도 비율(하한 없음), 적 방어구 배치(BOS·BGD). 밸런스 확인: 9mm·12ga는 등급 3에 관통 0%(§5.3 공식 결과).

## 2026-10-24 — MVP 마무리 리뷰 수정·GDD 본문 반영 (§5.1, §5.3, §7, §9, §12, §15)
리뷰 `docs/design/MVP_finish_review.md`(높음 0, 중간 2, 낮음 8)를 고치고, 위 "MVP 마무리 설계 결정"을 GDD 본문에 반영했다.
- **리뷰 1 (MF-Q25, 현재 동작 유지):** 파괴된 부위가 받은 피해의 70% 분산 몫(§4.2)은 방어구를 거치지 않는다 — 방어구 판정은 처음 맞은 부위로 한 번만(가장 단순). 테스트로 고정. 기획 확인 목록.
- **리뷰 2:** 방어구 전용 난수 seed를 기존 난수와 분리(고정 seed는 소금 섞은 별도 seed, seed 없음은 Guid). 관통 굴림과 출혈 굴림이 같이 움직이지 않게.
- **리뷰 3 (의도):** 은신처 상인의 정확한 수리비·매도가로 내구도를 거꾸로 추정할 수 있다 — "가격 반영" 결정의 결과로 받아들인다. RULE-04(탄 수)와 무관. 레이드 안에서는 대략 등급만.
- **리뷰 4:** 레이드 인벤토리를 열어 둔 채 맞으면 방어구 등급 문구는 다음 인벤토리 갱신 때 반영된다(의도, 문서화만).
- **리뷰 5:** 보관함 10×50은 다시 그릴 때마다 칸 500개를 새로 만든다 — 은신처 전용, 문제가 되면 이후 최적화.
- **리뷰 8(b):** 설정에서 보관함 단계를 줄이면 저장된 단계를 최대로 자르고, 범위 밖 아이템은 자동 배치 후 자리가 없으면 **버려진다**(탄피 환불 없음, `DroppedItemCount` 알림).
- **GDD 본문 수정 사유:** 사용자 결정(2026-10-24)으로 §5.3 방어구·무기 내구도 표시/수리·전신 사인·보관함 업그레이드가 MVP에 들어갔다. §5.1(내구도 표시·수리), §5.3(적용 범위·내구도 0·감소율·적 방어구), §7(적 방어구),
  §9(보관함 단계 수치), §12(결과 화면 사인), §15(MVP 마무리 AC)를 고쳤다. 임시값은 본문에 "(임시)"로 표시.
