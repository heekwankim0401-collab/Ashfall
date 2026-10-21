# M3 "적 AI" 설계 (unity-architect)

기준: docs/GDD.md §4.1(소음 반경), §4.2, §5, §6, §7, §11, §13.3, §14(EnemyArchetypeSO·LootTableSO), §15 M3, §17.
M1·M2 결정(CHANGELOG_GDD)과 M2 설계·구현 메모를 전제로 한다. 구현 에이전트는 이 문서만 보고 작업한다.
여기 없는 것은 만들지 않는다. 모호하면 `// TODO(GDD-?)` + 가장 단순한 구현.

---

## 0. 범위

**만든다 (M3 AC)**
- AI-SCV(약탈자): 2~3인 분대 순찰, 소리 추적, 권총/SMG.
- AI-RDR(레이더): 엄폐 활용, 측면 우회, 소총.
- 상태 머신 `Idle → Patrol → Suspicious → Alert → Combat → Search → Patrol` (+ 종료 상태 `Dead`).
- 시야: 콘 110°, 60m, 플레이어 웅크림 ×0.6, 엎드림 ×0.3. 청각: §4.1 소음 반경(+ 사격 소음, SO 임시값).
- 엄폐: 레벨에 미리 배치한 `CoverPoint` 컴포넌트.
- 적 피격 3부위(머리/몸통/다리), 플레이어와 같은 피해 공식. 적 사망 → 시체 = 루트 컨테이너(M2 검색 연출 재사용), 내용물 = 무기(장착 탄창 포함) + 남은 예비 탄창 + LootTableSO 굴림.
- 동시 적 20명 성능 확인(자동 측정 + 사람 확인).
- 사용자 결정: WPN-PST-01·WPN-SMG-01, 9mm 탄(AMMO-9MM), 9mm 탄창 2종 데이터. 피해는 무기 SO에 두고 값이 있으면 탄약 값 대신 쓴다.

**만들지 않는다**
- AI-SNP·AI-MUT·AI-BOS, 레이드 루프·맵·탈출·스폰 시스템(M4), 플레이어 무기 교체 1/2/3(M7), 방어구 관통(§5.3), 고장(Jam).
- 적의 상태이상(출혈·골절) — §7 "단순화"로 해석, TODO. 적 방어구.
- 적이 적의 총성을 듣는 것, 소음 가림(벽 뒤 감쇠) — TODO.
- 애니메이션·사운드(M6). 적은 그레이박스 캡슐.
- 적 프리팹·스포너(M4). M3 씬은 메뉴 코드가 오브젝트를 직접 만든다.
- 새 레이어(TagManager 수정) — 만들지 않는다(§1.7).
- 플레이어가 권총·SMG를 들고 쏘는 것(M7). 시체에서 주워 인벤토리에 넣기·장착 슬롯에 넣기까지는 기존 인벤토리로 된다(`WeaponController`는 WPN-RIF-01만 다루므로 다른 무기를 Primary1에 넣으면 비무장 — M2 동작 그대로).

---

## 1. 아키텍처 핵심 결정

### 1.1 경계 넘기 요약 (AI ↔ Weapons / Inventory / Player)

AI는 `Ashfall.Core`만 참조한다. 필요한 것과 넘는 방법:

| AI가 필요한 것 | 제공자 | 방법 | 이유 |
|---|---|---|---|
| 플레이어 위치·눈·자세·속도·사망 | Player(`PlayerMotor`) | Core 인터페이스 `IPlayerTarget` + `ServiceRegistry` | 시야 판정은 감각 주기마다 동기로 읽어야 한다. 매 프레임 이벤트로 위치를 뿌리면 할당·구독 비용만 늘고 값은 같다. M2의 "조회 = 인터페이스, 변경 = 이벤트" 원칙과 같다 |
| 플레이어 소음(발소리·점프·사격) | Player, Weapons | 이벤트 `NoiseEmitted` | 드문 사건(초당 수 회)이고 받는 쪽이 여럿일 수 있다(M6 오디오). 반환값이 필요 없다 |
| 투사체 발사 | Weapons(`ProjectileSystem`) | Core 인터페이스 `IProjectileService` + `ServiceRegistry` | 탄도·피격 처리는 하나여야 한다(플레이어와 같은 공식 AC). 발사는 즉시 동기 호출이 자연스럽고 이벤트는 SMG 800rpm × 20명에서 불필요한 오버헤드 |
| 발사 간격·탄창·재장전 규칙 | Weapons(`WeaponModel`, `FireControl`, `LocalMagazineSource`) | **순수 클래스 3개를 Core로 이동** (`git mv`, .meta 동반 → GUID 유지) | 적도 실제 `WeaponItem`·`Magazine`을 들고 쏴야 시체에 남은 탄 수가 그대로 남는다. 로직 복제를 피한다. CLAUDE.md "여러 기능이 같이 쓰는 타입은 Core" |
| 피해 값 결정(무기 vs 탄약) | — | Core 정적 `DamageRules.ProjectileDamage(weapon, ammo)` | 플레이어·적이 같은 규칙을 쓰게 한 곳에 둔다 |
| 피격 받기 | Weapons → AI | 기존 `IDamageable`(Core) | 변경 없음. `EnemyAgent`가 구현 |
| 시체 루팅 | AI → Inventory | 이벤트 `EnemyKilled`(시체 GameObject + 아이템 배열) → Inventory가 `LootContainer`를 붙인다 | 컨테이너 생성은 Inventory 책임(IInteractable·검색 시간·그리드). 사망은 M4 레이드 통계·M6 오디오도 받을 사실이라 이벤트가 맞다. 반환값 불필요 |
| LootTableSO 굴림 | — | Core `LootTableSO` + 정적 `LootRoller` | AI(시체)와 Inventory(M4 상자)가 같이 쓴다 |
| 플레이어 사망 | Player | `IPlayerTarget.IsDead` 조회 | 감각 주기마다 읽으므로 별도 구독 불필요 |

- `IInteractable`·`LootContainer`·`PlayerInventory`·`ProjectileSystem`·`PlayerMotor`를 AI가 직접 보는 곳은 없다.
- 레지스트리 등록 대상은 여전히 **Core 인터페이스만**. 새로 `IPlayerTarget`, `IProjectileService` 2개.
- 싱글톤은 `GameManager` 하나. AI의 씬 관리자 `AiDirector`는 **정적 Instance가 없는** 일반 컴포넌트다(직렬화 참조 또는 `FindFirstObjectByType` 1회).

### 1.2 피해 필드 (§5.2 ↔ §14 충돌 해결, 사용자 결정 구체화)

- `WeaponDefinitionSO.damage` (float, ★추가) = §5.2 표의 "피해", **발당(산탄은 펠릿당)**. `0` = 미지정.
- `AmmoDefinitionSO.damage`는 **그대로 둔다**. 의미가 "무기 피해가 0일 때 쓰는 기본값"(무기 없이 탄약만으로 쏘는 발사원: `TestTurret`, 테스트)으로 바뀐다.
- 규칙(Core):
  ```csharp
  public static class DamageRules
  {
      // 무기 피해가 0보다 크면 무기 값, 아니면 탄약 값 (M3 결정, §5.2 무기별 피해)
      public static float ProjectileDamage(WeaponDefinitionSO weapon, AmmoDefinitionSO ammo);
  }
  ```
- 플레이어 `WeaponController.Shoot`와 적 `EnemyWeapon`이 이 값을 `IProjectileService.Fire(..., damage, ...)`로 넘긴다.
- 관통력·탄속·비행 시간·펠릿 수는 계속 탄약에서 읽는다.
- 이름을 `damageOverride`가 아니라 `damage`로 한 이유: §5.2 열 이름과 같고, 장기적으로 무기 값이 정본(탄약 값이 예외)이기 때문. Tooltip에 "0이면 탄약 damage"를 적는다.
- **데이터 이전**: `M3DataSetup`이 `WPN-RIF-01.asset`의 `damage <= 0`이면 45로 기록(SetDirty → SaveAssets). `AMMO-556.damage`(45)는 바꾸지 않는다(터렛·기존 테스트 호환). 결과적으로 플레이어 소총 피해는 45 그대로.
- 9mm: `WPN-PST-01.damage = 35`, `WPN-SMG-01.damage = 30`, `AMMO-9MM.damage = 30`(기본값, TODO(GDD-?) — 무기 없이 쏠 때만 쓰인다).

### 1.3 피격 → 3부위 (§7)

- 적은 **루트의 CapsuleCollider 하나**(+ kinematic Rigidbody, NavMeshAgent)로 맞는다. 부위 히트박스 콜라이더를 만들지 않는다.
- `EnemyAgent.ApplyDamage(in DamageInfo)`: `local = transform.InverseTransformPoint(info.Point)`, `ratio = local.y / 현재 캡슐 높이`(선 자세 1.8 / 웅크림 1.2) → `EnemyHitResolver.Resolve(ratio, config)`:
  `ratio ≥ headFrom(0.85)` 머리, `≥ bodyFrom(0.45)` 몸통, 그 아래 다리. 값은 `AiConfigSO`(TODO(GDD-?)).
  - CharacterController가 아니므로 M2의 0.8배 캡슐 보정이 필요 없다(일반 CapsuleCollider는 설정 크기 그대로).
- M2 `BodyPartResolver`(Player, 7부위·좌우 팔)는 **옮기지도 일반화하지도 않는다.** 적은 좌우가 없고 3구간뿐이라 공통화 이득보다 Player 변경 위험이 크다. AI에 3줄짜리 순수 함수를 새로 둔다.
- 같은 피해 공식(§4.2를 3부위로 축약, `EnemyBodyModel`):
  - 머리 0 또는 몸통 0 → 사망(머리 = §4.2 머리, 몸통 = 흉부 역할).
  - 다리 0 → 이동 ×0.6, 질주 불가(§4.2 다리).
  - 파괴된 부위가 받은 피해 → 70%를 몸통으로(§4.2 분산; 3부위에서 흉부·복부 = 몸통 하나).
  - 0 아래 초과분은 버림(M2 해석과 같음). 상태이상·복부 배율 없음(복부 부위가 없다).
- HP: `EnemyArchetypeSO.hp`(§14 단일값) = **몸통 HP**로 정의하고, `headHp`·`legsHp`(★)를 더한다. 임시값은 플레이어와 같게: 머리 35 / 몸통 80 / 다리 65 (두 다리를 하나로 묶음). TODO(GDD-?).
  → 소총(45): 머리 1발, 몸통 2발. 권총(35): 머리 1발, 몸통 3발.
- 피격 시 반응: 추정 사격 위치 `info.Point - info.Direction * info.DistanceM`(DamageInfo만으로 계산, 사수 참조 불필요) → `Brain.OnDamaged(source)`.

### 1.4 적 사격 경로

```
EnemyBrain(의도: Fire, 조준점) ─► EnemyAgent(매 프레임) ─► EnemyWeapon.Tick(...)
   EnemyWeapon: WeaponModel(Core, 이동) + LocalMagazineSource(Core, 이동)로 RPM·탄 소모·재장전
              + 점사/반자동 간격 + AimError로 조준점 흩뜨림
              ─► IProjectileService.Fire(muzzle, dir, ammo, DamageRules.ProjectileDamage(weapon, ammo), enemyRoot)
   ProjectileSystem(Weapons): 기존 탄도·레이캐스트·IDamageable → PlayerHealth(M2 7부위 공식) / 다른 적(아군 사격 허용)
```
- **명중 오차**(`AimError`, 순수): 사격선에 수직인 원판(반지름 r) 안 균일 분포 점을 조준점에 더한다.
  `r = (aimErrorBaseM + aimErrorPerMeterM × 거리) × 정착배율 × 이동배율 + burstErrorGrowthM × 점사 내 발 순번`
  - 정착배율: 표적을 연속으로 본 시간 0 → `aimSettleSec`에 걸쳐 `unsettledErrorMultiplier`(2) → 1.
  - 이동배율: 자신 이동 중 ×`selfMovingErrorMultiplier`, 표적 속도 > 0.5m/s면 ×`targetMovingErrorMultiplier`.
  - 리드(이동 표적 예측) 없음. 조준점 = 표적 `CenterPosition`.
  - 전부 `EnemyArchetypeSO` 값, TODO(GDD-?).
- **반동 단순화**: 카메라 반동 없음. 점사 중 발마다 오차 반지름이 `burstErrorGrowthM`씩 커지고 점사가 끝나면 초기화.
- **연사 패턴**: 무기 `modes[0]`이 Auto면 `burstRounds`(min~max) 발 점사 후 `burstPauseSec` 쉼. Semi면 `semiShotIntervalSec` 간격으로 방아쇠를 한 프레임씩 당겼다 놓음(`FireControl`의 반자동 규칙을 그대로 통과).
- **재장전**: 장착 탄창 0발이면 일반 재장전(`WeaponModel.PressReload()` 1회, 빠른 재장전 안 씀 → 탄창이 바닥에 떨어지지 않는다). 예비 탄창은 `LocalMagazineSource`(회수 항상 성공). RDR은 숨는 구간에서 탄 비율 < `tacticalReloadBelow`(0.3)면 미리 재장전. 탄이 다 떨어지면 사격 의도를 무시한다(TODO(GDD-?): 탄 없는 적 행동).
- **사격 허용 조건**: 마지막 감각 갱신에서 표적이 보였고(≤ 감각 주기), 몸 방향과 표적 방향 수평 각 차 ≤ `fireMaxYawErrorDeg`(10°), 거리 ≤ `engageMaxRangeM`. 시야 레이가 막힘 없이 표적까지 닿았다는 뜻이므로 아군이 사선에 있으면 쏘지 않는다(아군 사격 자체는 허용).
- 적 사격은 `NoiseEmitted`를 내지 않는다(적끼리 총성 반응 없음, TODO).

### 1.5 감각

**시야** (`SightCone` 순수 + `ILineOfSight`)
- 유효 거리 = `sightRange(60) × 자세배율`(Standing 1.0 / Crouching 0.6 / Prone 0.3, §7 → `AiConfigSO`). 모든 상태에서 같은 규칙(전투 중에도 엎드린 표적은 18m 밖이면 놓친다).
- 콘: `sightAngle`(110)은 **전체 각**. 눈 forward와 표적 방향 사이 각 ≤ 55°. 수평·수직 모두 같은 3D 각으로 판정(단순).
- 가림: 눈 → 표적 `EyePosition`, 실패하면 눈 → 표적 `CenterPosition` (최대 2레이). 레이가 아무것도 안 맞거나 처음 맞은 콜라이더의 `transform.root == target.Root`면 보임. 다른 적·시체·지형에 먼저 맞으면 가림.
  - 레이 시작점(눈)은 자기 캡슐 안이라 PhysX가 자기 콜라이더를 보고하지 않는다(레이 원점이 안에 있으면 무시).
  - 플레이어 눈 높이가 PhysX 캡슐 꼭대기보다 살짝 높을 수 있다(M2 0.8배 문제) → "아무것도 안 맞음 = 보임" 규칙으로 처리.
- 레이 빈도: 감각 갱신 주기(평시 0.2초, 경계 이상 0.1초)마다, **콘·거리 안일 때만** 레이를 쏜다. 프레임당 감각 갱신 수 상한(§1.8).

**청각** (`NoiseEmitted` → `AiDirector`가 분배)
- `NoiseEmitted { Position, RadiusM, Kind }`. 적은 `거리 ≤ RadiusM × hearingMultiplier(1.0)`이면 듣는다. 가림·감쇠 없음(TODO).
- 발행:
  - `PlayerMotor`: 이동 중(`CurrentNoiseRadius > 0`) `noisePulseIntervalSec`(0.5초, PlayerConfigSO ★, TODO(GDD-?))마다 `Footstep`(반경 = §4.1 걷기 8 / 질주 20 / 웅크림 3 / 엎드림 1). 점프 순간 `Jump`(15).
  - `WeaponController.Shoot`: `Gunshot`, 반경 = `WeaponDefinitionSO.shotNoiseRadiusM`(★, TODO(GDD-?): 소총 120 / SMG 90 / 권총 80). 0이면 발행 안 함.
- 반응: Footstep/Jump → 인지 게이지 + `noiseAwarenessGain`(발소리 0.25, 점프 0.4), 자극 위치 갱신. Gunshot → 바로 Alert.

**인지 게이지** (`AwarenessModel`, 0~1, 전부 `AiConfigSO`·TODO(GDD-?))
- 보일 때 초당 증가량 = `Lerp(sightGainNearPerSec 2.5, sightGainFarPerSec 0.35, 거리/유효거리)`. `instantDetectRangeM`(4m) 안이면 즉시 1.
- 자극이 없으면 `awarenessDecayPerSec`(0.15)로 감소.
- `suspiciousAt`(0.3) 이상 → Suspicious, 1 → Alert.

### 1.6 상태 머신 (순수 C# `EnemyBrain`)

해석(§7 화살표를 모두 쓰는 선형 흐름 + 되돌아가는 간선):
- **Idle**: 제자리 대기(경계 근무). 순찰 경로가 있으면 `idleBeforePatrolSec` 뒤 Patrol.
- **Patrol**: 경로 순회(웨이포인트마다 `patrolWaitSec` 대기). 분대원은 리더 대형을 따른다.
- **Suspicious**: 뭔가 감지(부분 시야·발소리). 멈추고 자극 쪽을 본다. SCV(`investigatesNoise`)는 자극 위치로 걸어간다. RDR은 제자리에서 본다.
- **Alert**: 적대 존재 확정(게이지 1, 총성, 피격, 분대 연락). 보이면 반응 시간(`reactionTimeSec`) 뒤 Combat. 안 보이면 마지막 위치(LKP)로 이동 — SCV는 LKP로 달려가고, RDR은 LKP를 위협으로 보는 엄폐로 간다. 분대에 연락.
- **Combat**: 표적이 보이는 교전. 유형별 전술(§1.6.2).
- **Search**: 놓친 뒤 LKP 주변 `searchRadiusM` 안 NavMesh 점 `searchPointCount`개를 돌며 둘러본다. `searchDurationSec` 뒤 Patrol(경로 없으면 Idle).
- **Dead**: 종료. GDD 목록에 없지만 필요한 종료 상태.

#### 1.6.1 전이 표

| # | 현재 | 조건 | 다음 | 비고 |
|---|---|---|---|---|
| T1 | Idle | 경로 있음 ∧ 대기 ≥ `idleBeforePatrolSec` | Patrol | |
| T2 | Idle, Patrol | 게이지 ≥ `suspiciousAt` (시야) ∨ Footstep/Jump 들음 | Suspicious | 자극 위치 기록 |
| T3 | Idle, Patrol, Suspicious | 게이지 = 1 | Alert | LKP = 자극 위치 |
| T4 | Idle, Patrol, Suspicious, Search | Gunshot 들음 ∨ 피격 ∨ 분대 연락 | Alert | 게이지 = 1, LKP = 소리/추정 사격 위치/연락 위치 |
| T5 | Search | 표적 보임 | Alert | 게이지 = 1(이미 경계 중), 반응 시간 적용 |
| T6 | Search | Footstep/Jump 들음 | Alert | LKP 갱신 |
| T7 | Alert | 표적 보임 ∧ 반응 타이머 ≥ `reactionTimeSec` | Combat | 보인 순간부터 반응 타이머 시작 |
| T8 | Alert | LKP 갱신 후 `alertTimeoutSec` 동안 못 봄 | Search | |
| T9 | Combat | 마지막으로 본 뒤 `loseSightSec` 경과 | Search | RDR 측면 우회 중에는 우회 완료(도착/실패)까지 유예 |
| T10 | Search | `searchDurationSec` 경과 | Patrol (경로 없으면 Idle) | 경로상 가장 가까운 웨이포인트부터 |
| T11 | Suspicious | 게이지 0 ∧ `suspiciousLookSec` 경과(조사 도착 후) | Patrol (경로 없으면 Idle) | |
| T12 | Idle·Patrol·Suspicious 외 | 표적 없음/사망(`IPlayerTarget` 없음 또는 `IsDead`) | Patrol (경로 없으면 Idle) | 엄폐·우회 예약 해제 |
| T13 | 모든 상태 | 자기 사망 | Dead | 엄폐 예약·분대 우회권 해제, 분대에서 탈퇴 |

- Alert·Combat·Search에서 소리를 들으면 LKP만 갱신(상태 유지, T6 제외).
- 반응 시간 중에도 몸은 표적 쪽으로 돈다.
- 상태 진입 시 상태 타이머 초기화. 전이는 `Tick` 안에서 한 번에 하나만(같은 틱 연쇄 없음).

#### 1.6.2 유형별 차이 (`EnemyArchetypeSO` 플래그 + 전술 클래스)

| | AI-SCV (`ScavengerTactics`) | AI-RDR (`RaiderTactics`) |
|---|---|---|
| 플래그 | `usesCover=false`, `flanks=false`, `investigatesNoise=true` | `usesCover=true`, `flanks=true`, `investigatesNoise=false` |
| Suspicious | 자극 위치로 걷기(Walk) → 도착 후 둘러보기 | 제자리에서 자극 쪽을 봄, 웅크림 |
| Alert(안 보임) | LKP로 달리기(Run) — "소리 추적" | LKP를 위협으로 하는 엄폐 선택 → 달리기. 없으면 SCV처럼 |
| Combat | 거리 > `preferredRangeM.y`면 표적 쪽으로 Run, 범위 안이면 정지 사격. `strafeIntervalSec`마다 2~4m 옆 NavMesh 점으로 Walk(사격 계속, 이동 오차 배율) | 엄폐 없으면 엄폐 선택 → Run(이동 중 사격 안 함). 엄폐에서 Hide(`hideSec`, 웅크림/High는 숨는 점) ↔ Peek(`peekSec`, 선 자세/High는 `PeekPosition`) 반복, Peek 중 보이면 점사 |
| 엄폐 무효화 | — | 숨은 상태에서 표적에게 보이면(표적 눈 → 자기 눈 레이가 막히지 않음) 또는 표적이 엄폐 보호각 밖으로 이동 → 엄폐 다시 선택 |
| 측면 우회 | — | Peek 중 못 본 시간 누적 ≥ `flankAfterNoSightSec`(6s) ∧ `squad.TryClaimFlank` → 우회 지점 선택(§1.7) → Sprint. 도착하면 일반 Combat, 우회권 반납. 실패하면 엄폐 유지 |
| 무장 | `loadouts`: 권총 가중 2, SMG 가중 1 | 소총 1 |

전술 인터페이스(AI 내부):
```csharp
internal interface IEnemyTactics
{
    void Reset();                                                    // 상태 진입 시
    void TickSuspicious(TacticsContext c, in EnemySenses s, ref EnemyIntent i);
    void TickAlert(TacticsContext c, in EnemySenses s, ref EnemyIntent i);
    void TickCombat(TacticsContext c, in EnemySenses s, ref EnemyIntent i);
    void ReleaseReservations(TacticsContext c);                      // 엄폐·우회권 반납
}
```
`TacticsContext`는 brain이 들고 있는 것(아키타입, 설정, 분대, 월드, 난수, LKP, 상태 타이머)을 묶은 클래스 하나(할당 1회).

#### 1.6.3 분대 (`Squad`, 순수)

- 씬에서 `EnemyAgent.squadId`(int, 0 = 단독)가 같은 적이 한 분대. `AiDirector`가 등록 시 묶는다. 분대원 2~3(SCV), RDR은 1~2.
- 리더 = 분대 첫 등록자, 사망 시 다음 생존자. 리더의 `PatrolRoute`를 쓴다(분대원 모두 같은 경로를 연결해 둔다).
- 대형: 분대원 i(1부터)는 리더 뒤쪽 슬롯 `leaderPos + 회전(leaderForward) × (±formationSpacingM, 0, -formationSpacingM × 줄)`. 2Hz로 목적지 갱신. 리더가 대기·Suspicious로 멈추면 같이 멈춘다.
- 정보 공유: 분대원이 Alert에 들어가거나 Combat 중 표적을 보면 `squad.ReportContact(reporterId, pos, now)` → 거리 ≤ `squadShareRangeM`(50, TODO(GDD-?))인 다른 분대원 `brain.OnSquadContact(pos, now)` → T4(Alert) 또는 LKP 갱신. 같은 틱 반복 보고는 `squadReportMinIntervalSec`(0.5)로 줄인다.
- 측면 우회권: 분대에서 동시에 한 명만(`TryClaimFlank(agentId, now)`, 반납 후 `flankCooldownSec` 동안 다시 안 줌). 나머지는 엄폐에서 계속 사격 = 견제.

### 1.7 엄폐 (`CoverPoint` + 순수 `CoverIndex`·`CoverSelector`)

- `CoverPoint`(MonoBehaviour, AI): 위치 = 숨는 발 위치. `transform.forward` = **막아 주는 방향**(엄폐물 쪽 = 위협이 있어야 할 쪽).
  - `height`: `Low`(낮은 벽·차량, 웅크려 숨고 서서 사격) / `High`(높은 벽, 서서 숨고 `peekLocalOffset` 위치로 나와 사격).
  - 기즈모로 방향·사격 위치 표시(에디터 확인용).
- `AiDirector`가 Start에서 씬의 CoverPoint를 모아 `CoverIndex`에 `CoverSpot` 구조체로 넣는다(id = 순번). 예약(한 지점 한 명).
- **선택 기준** (`CoverSelector.TrySelect`, 할당 없음, `int[]` 스크래치):
  1. 거리: 자신에서 `coverSearchRadiusM`(25) 안, 비예약(자기 예약은 허용).
  2. 보호각: `forward`와 (위협 − 지점) 수평 각 ≤ `coverProtectionHalfAngleDeg`(70).
  3. 거리 선호: 위협까지 거리가 `preferredRangeM` 안이면 가점.
  4. 점수 = `자기→지점 거리 × coverDistanceWeight + |위협 거리 − 선호 중앙| × coverRangeWeight` (+ 위협 쪽으로 다가가는 지점 벌점 `coverAdvancePenalty`). 낮을수록 좋음.
  5. 점수 상위 `coverLosCandidates`(3)개만 레이 확인: **(a) 플레이어 시야 차단** — 위협 눈 → 숨는 눈 높이(Low `crouchEyeHeightM` 1.0 / High `standEyeHeightM` 1.6)가 막혀야 함. **(b) 사격 가능** — 사격 위치(Low: 지점 + 선 눈 높이, High: `PeekPosition` + 선 눈 높이) → 위협 눈이 뚫려야 함. 둘 다 통과한 첫 후보 선택·예약.
  - 레이 확인은 `ILineOfSight.IsBlocked(from, to, ignoreRootA, ignoreRootB)` — 지점에 이미 서 있는 자기 자신·표적 플레이어 콜라이더는 무시(`RaycastNonAlloc` + 고정 버퍼 8, 루트 비교).
- **측면 우회 지점**: 같은 함수에 `CoverQuery.flank = true` — 표적에서 본 (현재 위치 방향)과 (후보 방향) 사이 각이 `flankMinAngleDeg`(45) ~ `flankMaxAngleDeg`(120)이고 반경 `flankSearchRadiusM`(30) 안. 엄폐 후보가 없으면 실패(엄폐 유지). 이동 경로가 표적에게 보이는지는 확인하지 않는다(TODO).
- 공간 분할 없음(선형 탐색). M3 씬 엄폐 수십 개 × 저빈도 질의라 충분. M4 맵에서 수백 개가 되면 균일 격자로 바꾼다(TODO(M4)).
- 레이어를 만들지 않는 이유: TagManager 수정이 필요하고 기존 상호작용·투사체 마스크(Default 포함)에 영향이 간다. "루트 비교 + 무시 목록"으로 충분하다.

### 1.8 성능 (20명 60fps)

예산(에디터 배치모드 CPU, 목표): **AI 틱 평균 ≤ 1.0ms/프레임, 99% ≤ 3ms, ProjectileSystem 평균 ≤ 1.0ms, 매 프레임 AI GC 할당 0B**.

- **틱 분할** (`AiTickScheduler`, 순수): 적마다 다음 감각 시각. 평시(Idle·Patrol) `relaxedSensePeriodSec` 0.2초, 그 외 `engagedSensePeriodSec` 0.1초. 등록 순번으로 위상을 흩뜨린다(i/N × 주기). 프레임당 `maxSenseUpdatesPerFrame`(8)까지, 초과분은 다음 프레임(오래 기다린 순).
  - 감각 갱신 = 콘 판정(+ 레이 ≤ 2) + 게이지 + `brain.Tick` (결정). 매 프레임은 회전·사격·의도 적용만.
- **레이 예산**: 감각 ≤ 8 × 2 = 16레이/프레임 상한(실제는 콘 밖이면 0). 엄폐 질의 `maxCoverQueriesPerFrame`(1) × 최대 6레이. 초과 질의는 `CoverResult.Deferred`로 다음 틱 재시도.
- **NavMesh 경로**: 목적지가 `repathDistanceM`(1.0) 이상 바뀌고 직전 요청 뒤 `repathMinIntervalSec`(0.5) 지났을 때만 `SetDestination`. 프레임당 `maxPathRequestsPerFrame`(3), 초과는 다음 프레임. 대형 추종 2Hz. `obstacleAvoidanceType = LowQualityObstacleAvoidance`.
- **할당 없음**: LINQ·람다 캡처·foreach(인터페이스 열거)·문자열 포맷 금지(Update 경로). 리스트·배열 미리 할당. `RaycastNonAlloc`/`Linecast(out hit)`. 루트 비교는 `transform.root`(할당 없음). `GetComponent` 계열은 Awake/등록 때만. 로그는 상태 전이 디버그용 `[Conditional("ASHFALL_DEBUG_AI")]`.
- **EventBus 무할당화(Core 변경)**: 지금 `Publish`가 매번 `list.ToArray()`로 할당한다. 발소리(2/s)·총성(10.8/s)이 매 프레임 할당이 된다. → 타입별 `Delegate[]`를 **쓰기 시 복사(copy-on-write)** 로 바꾼다: Subscribe/Unsubscribe가 새 배열을 만들고, Publish는 현재 배열을 그대로 순회. 기존 보장(Publish 중 구독/해지 안전, 중복 무시, 예외 격리)은 유지.
- **ProjectileSystem**: 20명이 쏘면 살아 있는 탄이 수백 개가 될 수 있다(탄당 FixedUpdate 레이 1개). 측정 마커를 붙이고, 성능 씬에서는 `drawDebugTrails = false`. 9mm `maxFlightTimeSec` 1.5초(TODO). 예산 초과 시 `RaycastCommand` 일괄 처리로 바꾸는 것은 측정 후 판단(이번 설계에 포함하지 않음).
- **측정 방법**
  - `AiDirector`·`ProjectileSystem`이 자기 Update/FixedUpdate를 `ProfilerMarker`("Ashfall.AI.Tick", "Ashfall.Projectiles.Step")로 감싸고, 동시에 `Stopwatch.GetTimestamp()`로 잰 값을 `TickStats`(마지막·평균(EMA 아님, 누적 합/횟수)·최대, `Reset()`)로 노출한다. 배치모드에서 Profiler 창 없이도 테스트가 읽을 수 있게 하기 위함.
  - PlayMode 성능 테스트(4단계): `M3_AIPerf` 씬, 플레이어를 지우고 테스트용 무적 표적(테스트 어셈블리의 `IPlayerTarget` + `IDamageable` 무시 구현)을 등록 → 20명 전부 Combat 진입 확인 → 3초 워밍업 → 10초 측정. `Time.captureDeltaTime`은 쓰지 않는다(실시간 측정).
  - 판정: AI·투사체 마커 예산(위) + 프레임 `Time.unscaledDeltaTime` 중앙값 ≤ 16.6ms(참고용 정상성 확인). GC는 EditMode `Is.Not.AllocatingGCMemory()`로 brain·감각·엄폐·스케줄러·EventBus Publish를 각각 검증한다(PlayMode 프레임 GC는 에디터·UI 할당이 섞여 참고 로그만).
  - **한계**: 배치모드는 화면 렌더링을 하지 않아 GPU 비용이 빠지고, 에디터 오버헤드(안전 검사, Debug.DrawLine)가 더해진다. `-nographics`는 쓰지 않는다(M2와 같은 조건 유지, CPU 측정에는 차이 없음). GTX 1660 1080p 60fps 최종 확인은 사람이 에디터 Profiler 또는 개발 빌드로 한다(§15 M7 "성능 목표 달성"이 최종 관문).

### 1.9 시체 루팅

1. `EnemyBodyModel.Died` → `EnemyAgent.Die()`:
   - brain `OnDied()`(예약 반납), 진행 중 재장전 취소, NavMeshAgent `enabled = false`, `AiDirector.Unregister`.
   - 몸 눕히기: 시각용 자식(`Body`)을 90° 눕히고 CapsuleCollider `direction = 2(Z)`, `center.y = radius`. 콜라이더는 Default 레이어 그대로(상호작용 레이캐스트가 맞힌다).
   - 아이템: `CorpseLootBuilder.Build(weapon, archetype.loot, random, list)` → [무기(`WeaponItem`, 남은 탄 그대로의 장착 탄창 포함)] + [남은 예비 탄창(실제 탄 수)] + [`LootRoller.Roll` 결과] 순서.
   - `EnemyKilled(corpse: gameObject, displayName: archetype.corpseDisplayName, items: ItemInstance[], position, archetypeId)` 발행.
   - `EnemyAgent`는 `enabled = false`(Update 중지). `IDamageable.ApplyDamage`는 계속 불리지만 사망 후 무시. 시체는 탄을 막는다.
2. Inventory `PlayerInventoryBehaviour`가 `EnemyKilled`를 구독(기존 `ItemDropRequested → WorldItem`과 같은 패턴) →
   `LootContainer.AttachRuntime(evt.Corpse, evt.DisplayName, config.corpseGridSize, evt.Items, config.searchTimePerItemSec)`.
3. 플레이어 F → 기존 `IInteractable` 경로 → `OpenLoot` → 검색 0.5초/개(M2 그대로). 드러나는 순서 = 무기 → 탄창 → 루트.
- 그리드에 안 들어가는 아이템은 M2처럼 경고 후 제외. `corpseGridSize` 6×5(TODO(GDD-?)): 소총 4×2 + 탄창 1×2 ×3 + 잡동사니 여유.
- 프롬프트는 기존 `InteractionKind.OpenContainer`("열기: 약탈자 시체"). UI 변경 없음.

### 1.10 NavMesh

- AI 어셈블리는 `NavMeshAgent`(엔진 모듈 `UnityEngine.AIModule`, asmdef 참조 불필요)만 쓴다. `Unity.AI.Navigation`(NavMeshSurface)은 **EditorTools만** 참조한다(씬 생성 시 베이크).
- 베이크(M3SceneSetup, 배치모드에서 동기):
  ```csharp
  // 환경(바닥·벽·차량)을 모두 "Environment" 루트 아래 만든 뒤, 적·플레이어를 만들기 전에 굽는다.
  var surface = environment.AddComponent<NavMeshSurface>();          // Unity.AI.Navigation
  surface.collectObjects = CollectObjects.Children;
  surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;     // UnityEngine.AI
  surface.BuildNavMesh();
  // 저장 위치는 패키지 관례(씬 이름 폴더): Scenes/Test/M3_AI/NavMesh-Environment.asset
  var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
  if (existing == null) AssetDatabase.CreateAsset(surface.navMeshData, path);
  else { EditorUtility.CopySerialized(surface.navMeshData, existing); surface.navMeshData = existing; }  // GUID 유지
  EditorUtility.SetDirty(surface);   // 그다음 SaveScene
  ```
  - 기존 NavMeshData를 지우고 다시 만들면 .meta GUID가 바뀐다(규칙 위반) → `CopySerialized`로 덮어쓴다. 3단계 담당은 재실행 후 `.meta` 불변과 PlayMode에서 `NavMesh.SamplePosition` 성공을 확인한다. `CopySerialized`가 NavMeshData에 동작하지 않으면 **멈추고 보고**(대안: 씬 로드 시 런타임 베이크 — AI asmdef에 `Unity.AI.Navigation` 추가 필요).
  - NewScene(Single) 뒤에 굽고 저장한다(M2 함정: NewScene 전에 잡은 에셋 참조가 끊김).
- 에이전트 설정(그레이박스): 반지름 0.35, 높이 1.8, angularSpeed 360(직접 회전할 때는 `updateRotation = false`), 가속 12, stoppingDistance 0.5, 회피 LowQuality, `avoidancePriority` 40~60 분산. agent type은 기본 Humanoid.

---

## 2. 어셈블리 / asmdef 변경

| asmdef | references 변경 |
|---|---|
| `Ashfall.Core` | 없음 |
| `Ashfall.AI` | 없음 (`Ashfall.Core`만. NavMeshAgent·ProfilerMarker는 엔진 모듈) |
| `Ashfall.Player`, `Ashfall.Weapons`, `Ashfall.Inventory` | 없음 |
| `Ashfall.EditorTools` | `+ Ashfall.AI`, `+ Unity.AI.Navigation` |
| 테스트 asmdef 2개 | 없음 (이미 Ashfall 전체 참조. NavMesh API는 엔진 모듈) |

새 asmdef 없음. 기능 어셈블리끼리 참조 추가 금지. 입력 액션 변경 없음(새 키 없음).

---

## 3. Core 변경 (전부 `namespace Ashfall.Core`)

### 3.1 파일 목록
| 파일 | 변경 |
|---|---|
| `Core/Combat/WeaponModel.cs` | **이동** `Weapons/WeaponModel.cs` → (git mv, .meta 동반), namespace만 `Ashfall.Core` |
| `Core/Combat/FireControl.cs` | **이동** 같은 방식 |
| `Core/Combat/LocalMagazineSource.cs` | **이동** 같은 방식(주석 "M1 씬과 테스트용"에 "적 예비 탄창" 추가) |
| `Core/Combat/DamageRules.cs` | 새 (§1.2) |
| `Core/Combat/BodyEnums.cs` | `TargetPosture` enum 추가 |
| `Core/Data/WeaponDefinitionSO.cs` | `damage`, `shotNoiseRadiusM` 추가 |
| `Core/Data/AmmoDefinitionSO.cs` | `damage` Tooltip만 변경("무기 damage가 0일 때 기본값") |
| `Core/Data/LootTableSO.cs` | 새 (`LootTableSO`, `LootEntry`) |
| `Core/Items/LootRoller.cs` | 새 |
| `Core/Services/IPlayerTarget.cs` | 새 |
| `Core/Services/IProjectileService.cs` | 새 |
| `Core/Events/EventBus.cs` | Publish 무할당화(§1.8). 공개 API 동일 |
| `Core/Events/GameEvents.cs` | `NoiseKind` enum, `NoiseEmitted`, `EnemyKilled` 추가 |
| `Core/Services/ServiceRegistry.cs` | 주석의 등록 대상 목록에 `IPlayerTarget`, `IProjectileService` 추가(코드 변경 없음) |

이동 후 기존 테스트는 이미 `using Ashfall.Core;`가 있어 그대로 컴파일된다(확인: WeaponTests, ItemInstanceTests, PlayerInventoryTests).

### 3.2 새 타입 시그니처

```csharp
// Combat/BodyEnums.cs
// AI 시야 배율(§7)용 플레이어 자세. Player의 Stance를 Core로 옮기지 않고 이것으로 옮겨 말한다.
public enum TargetPosture { Standing, Crouching, Prone }

// Combat/DamageRules.cs
public static class DamageRules
{
    public static float ProjectileDamage(WeaponDefinitionSO weapon, AmmoDefinitionSO ammo) =>
        weapon != null && weapon.damage > 0f ? weapon.damage : (ammo != null ? ammo.damage : 0f);
}

// Data/WeaponDefinitionSO.cs (추가 필드)
[Tooltip("§5.2 발당 피해 (산탄은 펠릿당). 0이면 탄약 damage를 쓴다 (M3 결정)")]
public float damage;
[Tooltip("사격 소음 반경 (m). 0이면 소음 없음. TODO(GDD-?): GDD에 없는 임시값")]
public float shotNoiseRadiusM;

// Data/LootTableSO.cs
[Serializable]
public struct LootEntry
{
    public ItemDefinitionSO item;
    [Tooltip("상대 가중치 (0 이하는 뽑히지 않음)")]
    public float weight;
}

// §14 LootTableSO. 튜플 리스트는 직렬화되지 않아 LootEntry로 쓴다. id는 데이터 검증용 ★.
[CreateAssetMenu(menuName = "Ashfall/Loot Table", fileName = "LootTable")]
public class LootTableSO : ScriptableObject
{
    public string id;
    public List<LootEntry> entries = new List<LootEntry>();
    [Tooltip("굴림 횟수 최소~최대 (포함)")]
    public Vector2Int rollCount = Vector2Int.one;
}

// Items/LootRoller.cs
public static class LootRoller
{
    // rollCount.x~y(포함) 중 하나를 고르고 그 횟수만큼 가중치 비례 복원 추출.
    // 뽑힌 정의마다 ItemFactory.Create로 새 인스턴스를 into에 더한다. 더한 개수를 돌려준다.
    // table null·항목 없음·가중치 합 0 → 0. random은 테스트에서 고정 시드.
    public static int Roll(LootTableSO table, System.Random random, List<ItemInstance> into);
}

// Services/IPlayerTarget.cs
// AI가 보는 플레이어 (§7 감각). Player가 구현해 GameManager.Services에 등록한다.
public interface IPlayerTarget
{
    Transform Root { get; }            // 시야 레이가 맞힌 콜라이더의 transform.root와 비교
    Vector3 Position { get; }          // 발
    Vector3 EyePosition { get; }       // 카메라(눈) 위치
    Vector3 CenterPosition { get; }    // 캡슐 중심 (조준점·두 번째 시야 레이)
    Vector3 Velocity { get; }          // 명중 오차(이동 표적)
    TargetPosture Posture { get; }
    bool IsDead { get; }
}

// Services/IProjectileService.cs
// 탄도 투사체 발사 (§5.1). Weapons의 ProjectileSystem이 구현해 등록한다. AI가 쓴다.
public interface IProjectileService
{
    // damage = DamageRules.ProjectileDamage(weapon, ammo). shooterRoot의 콜라이더는 맞지 않는다.
    void Fire(Vector3 origin, Vector3 direction, AmmoDefinitionSO ammo, float damage, Transform shooterRoot);
}

// Events/GameEvents.cs (추가)
public enum NoiseKind { Footstep, Jump, Gunshot }

// 플레이어가 낸 소리 (§4.1 소음 반경, 사격) → AI 청각. 반경 안의 적이 듣는다.
public readonly struct NoiseEmitted : IGameEvent
{
    public readonly Vector3 Position;
    public readonly float RadiusM;
    public readonly NoiseKind Kind;
    public NoiseEmitted(Vector3 position, float radiusM, NoiseKind kind);
}

// 적 사망 → Inventory가 시체에 루트 컨테이너를 붙인다 (§6 시체). 이후 M4 레이드 통계·M6 오디오도 받을 수 있다.
public readonly struct EnemyKilled : IGameEvent
{
    public readonly GameObject Corpse;       // 시체 루트 (콜라이더 유지)
    public readonly string DisplayName;      // 프롬프트·루트 패널 이름 (EnemyArchetypeSO.corpseDisplayName)
    public readonly ItemInstance[] Items;    // 배치 순서 = 검색으로 드러나는 순서
    public readonly Vector3 Position;
    public readonly string ArchetypeId;
    public EnemyKilled(GameObject corpse, string displayName, ItemInstance[] items, Vector3 position, string archetypeId);
}
```

### 3.3 EventBus 무할당화 (의도된 구현)
```csharp
private readonly Dictionary<Type, Delegate[]> handlers = ...;
Subscribe:   배열에 없으면 길이+1 새 배열로 교체 (중복 무시, null 예외)
Unsubscribe: 있으면 길이-1 새 배열로 교체 (null 무시)
Publish:     handlers.TryGetValue → 그 배열을 순회 (교체돼도 순회 중 배열은 그대로 = 스냅샷 보장)
SubscriberCount / Clear: 동일 의미
```
테스트: 기존 EventBusTests 전부 통과 + "구독자 2개 Publish 0할당"(`Is.Not.AllocatingGCMemory`).

---

## 4. 데이터 (SO 필드와 임시값)

모든 값 중 GDD에 있는 것은 출처 표시, 나머지는 TODO(GDD-?).
**규칙**: `AiConfigSO`는 필드 초기값 = 아래 값(`CreateInstance`만 해도 설계값, M2 관례). `EnemyArchetypeSO`는 유형마다 달라 초기값 없이 `M3DataSetup`이 채운다.

### 4.1 `EnemyArchetypeSO` (AI, `Data/AI/AI-SCV.asset`, `AI-RDR.asset`) — §14 + ★추가

```csharp
[Serializable]
public struct EnemyLoadout
{
    public WeaponDefinitionSO weapon;
    public AmmoDefinitionSO ammo;
    public MagazineDefinitionSO magazine;
    [Tooltip("스폰 시 고를 상대 가중치")]
    public float weight;
}

[CreateAssetMenu(menuName = "Ashfall/Enemy Archetype", fileName = "EnemyArchetype")]
public class EnemyArchetypeSO : ScriptableObject { ... }
```

| 필드 | 출처 | AI-SCV | AI-RDR |
|---|---|---|---|
| `id` | §14, §7 | AI-SCV | AI-RDR |
| `displayName` ★ | §7 | 약탈자 | 레이더 |
| `corpseDisplayName` ★ | TODO | 약탈자 시체 | 레이더 시체 |
| `hp` (= 몸통 HP) | §14, §4.2 흉부 기준 | 80 | 80 |
| `headHp` ★ | §4.2 머리 | 35 | 35 |
| `legsHp` ★ | §4.2 다리 하나 | 65 | 65 |
| `sightRange` | §7 | 60 | 60 |
| `sightAngle` (전체 각) | §7 | 110 | 110 |
| `hearingMultiplier` ★ | §4.1 해석 | 1.0 | 1.0 |
| `loot` | §14 | LT-SCV | LT-RDR |
| `loadouts` ★ (§14 `weapon` 대체) | §7 무장 | PST-01/9MM/MAG-9MM-12 ×2, SMG-01/9MM/MAG-9MM-30 ×1 | RIF-01/556/MAG-556-30 ×1 |
| `spareMagazines` ★ | TODO | 2 | 3 |
| `patrolSpeed` / `moveSpeed` / `sprintSpeed` ★ (m/s) | TODO (§4.1 참고) | 1.5 / 3.0 / 5.0 | 1.5 / 3.2 / 5.5 |
| `reactionTimeSec` ★ | TODO | 0.6 | 0.4 |
| `aimErrorBaseM` / `aimErrorPerMeterM` ★ | TODO | 0.3 / 0.04 | 0.2 / 0.025 |
| `aimSettleSec` / `unsettledErrorMultiplier` ★ | TODO | 1.5 / 2.0 | 1.0 / 2.0 |
| `selfMovingErrorMultiplier` / `targetMovingErrorMultiplier` ★ | TODO | 2.0 / 1.5 | 2.0 / 1.3 |
| `burstErrorGrowthM` ★ | TODO | 0.08 | 0.05 |
| `burstRounds` ★ (Vector2Int, Auto용) | TODO | (3, 6) | (3, 5) |
| `burstPauseSec` ★ (Vector2) | TODO | (0.6, 1.2) | (0.5, 1.0) |
| `semiShotIntervalSec` ★ (Vector2, Semi용) | TODO | (0.35, 0.6) | (0.35, 0.6) |
| `preferredRangeM` ★ (Vector2) | TODO | (8, 20) | (15, 40) |
| `engageMaxRangeM` ★ | TODO | 50 | 60 |
| `usesCover` / `flanks` / `investigatesNoise` ★ | §7 행동 | false / false / true | true / true / false |
| `peekSec` / `hideSec` ★ (Vector2) | TODO | — (0,0) | (1.5, 3) / (1, 2.5) |
| `tacticalReloadBelow` ★ | TODO | 0 (안 함) | 0.3 |
| `strafeIntervalSec` ★ (Vector2) | TODO | (2, 4) | — (0,0) |

### 4.2 `AiConfigSO` (AI, `Data/AI/AiConfig.asset`) — 전역, 초기값 = 아래

| 묶음 | 필드 = 값 | 출처 |
|---|---|---|
| §7 자세 배율 | `crouchSightMultiplier = 0.6`, `proneSightMultiplier = 0.3` | §7 |
| 인지 게이지 | `suspiciousAt = 0.3`, `sightGainNearPerSec = 2.5`, `sightGainFarPerSec = 0.35`, `instantDetectRangeM = 4`, `awarenessDecayPerSec = 0.15`, `footstepAwarenessGain = 0.25`, `jumpAwarenessGain = 0.4` | TODO |
| 타이머 | `idleBeforePatrolSec = 2`, `patrolWaitSec = (2, 4)`, `suspiciousLookSec = 5`, `alertTimeoutSec = 12`, `loseSightSec = 5`, `searchDurationSec = 25`, `searchRadiusM = 12`, `searchPointCount = 3`, `searchLookSec = 2` | TODO |
| 피해 공식 (§4.2 축약) | `destroyedPartRedistribution = 0.7`, `legsDestroyedSpeedMultiplier = 0.6` | §4.2 |
| 부위 판정 | `headFrom = 0.85`, `bodyFrom = 0.45` | TODO |
| 몸 | `standHeightM = 1.8`, `crouchHeightM = 1.2`, `standEyeHeightM = 1.6`, `crouchEyeHeightM = 1.0`, `turnSpeedDegPerSec = 360`, `fireMaxYawErrorDeg = 10` | TODO |
| 분대 | `squadShareRangeM = 50`, `squadReportMinIntervalSec = 0.5`, `formationSpacingM = 2`, `formationUpdateSec = 0.5`, `flankCooldownSec = 8` | TODO |
| 엄폐 | `coverSearchRadiusM = 25`, `coverProtectionHalfAngleDeg = 70`, `coverDistanceWeight = 1`, `coverRangeWeight = 0.5`, `coverAdvancePenalty = 5`, `coverLosCandidates = 3`, `coverArriveDistanceM = 0.6` | TODO |
| 측면 우회 | `flankAfterNoSightSec = 6`, `flankMinAngleDeg = 45`, `flankMaxAngleDeg = 120`, `flankSearchRadiusM = 30` | TODO |
| 성능 | `relaxedSensePeriodSec = 0.2`, `engagedSensePeriodSec = 0.1`, `maxSenseUpdatesPerFrame = 8`, `maxPathRequestsPerFrame = 3`, `repathMinIntervalSec = 0.5`, `repathDistanceM = 1.0`, `maxCoverQueriesPerFrame = 1`, `targetResolveRetrySec = 1` | TODO |
| 디버그 | `showStateIndicator = true` (M6에서 끔) | — |

### 4.3 무기·탄·탄창 (Core SO, `Data/Weapons/`)

| ID | 필드 |
|---|---|
| WPN-PST-01 녹슨 권총 | cls Pistol, ammo Pistol9mm, rpm 400, modes [Semi], **damage 35**, (§5.2) / TODO: size 2×1, 0.9kg, recoilV 0.9, recoilH 0.3, adsTime 0.2, reload 2.0 / fast 1.2, hipSpread 2.5, shotNoiseRadiusM 80, rarity Common, price 0 |
| WPN-SMG-01 개조 SMG | cls Smg, ammo Pistol9mm, rpm 800, modes [Auto], **damage 30** (§5.2) / TODO: size 3×2, 2.6kg, recoilV 0.45, recoilH 0.35, adsTime 0.22, reload 2.3 / fast 1.4, hipSpread 3.0, shotNoiseRadiusM 90, Common |
| WPN-RIF-01 (기존) | **damage 45**(§5.2), **shotNoiseRadiusM 120**(TODO) 기록. 나머지 그대로 |
| AMMO-9MM 9mm 탄 | type Pistol9mm, damage 30(기본값, TODO), penetration 1, velocity 360, maxFlightTimeSec 1.5, pelletCount 1, size 1×1, 0.01kg (TODO 전부) |
| MAG-9MM-12 권총 탄창 (12발) | ammo Pistol9mm, capacity **12**(§5.2 탄창), size 1×1, 0.15kg (TODO) |
| MAG-9MM-30 SMG 탄창 (30발) | ammo Pistol9mm, capacity **30**(§5.2), size 1×2, 0.35kg (TODO) |

- 탄창-무기 호환은 M1 규칙(탄 종류만) 그대로 → 9mm 30발 탄창이 권총에도 맞는다. TODO(M7): 무기별 탄창 호환.

### 4.4 루트 테이블 (Core SO, `Data/Loot/`) — 전부 TODO(GDD-?)
| ID | rollCount | entries (item, weight) |
|---|---|---|
| LT-SCV | (1, 2) | MED-BND-01 3, JNK-03 3, JNK-01 2, MED-TRQ-01 1, MAG-9MM-12 1 |
| LT-RDR | (1, 3) | MED-BND-01 2, MED-TRQ-01 2, MED-SPL-01 1, MAG-556-30 2, JNK-03 1 |

### 4.5 기존 SO 추가 필드
| SO | 필드 | 값 |
|---|---|---|
| `PlayerConfigSO` (Player) | `noisePulseIntervalSec` | 0.5 (초기값, TODO(GDD-?)) |
| `InventoryConfigSO` (Inventory) | `corpseGridSize` | (6, 5) (초기값, TODO(GDD-?)) |
| `WeaponDefinitionSO` (Core) | `damage`, `shotNoiseRadiusM` | 초기값 0 (= 미지정). 에셋은 M3DataSetup이 채움 |

---

## 5. 기능별 클래스와 공개 API

### 5.1 AI (`Ashfall.AI`, `Scripts/AI/`)

| 파일 | 종류 | 역할 |
|---|---|---|
| `Data/EnemyArchetypeSO.cs` | SO (+`EnemyLoadout`) | §4.1 |
| `Data/AiConfigSO.cs` | SO | §4.2 |
| `AiEnums.cs` | enum | `EnemyState`, `EnemyMoveMode`, `EnemyPosture`, `EnemyHitRegion`, `CoverHeight`, `CoverResult` |
| `Brain/EnemyBrain.cs` | 순수 | 상태 머신(§1.6) |
| `Brain/EnemySenses.cs`, `Brain/EnemyIntent.cs` | struct | 입력·출력 |
| `Brain/IEnemyTactics.cs`, `Brain/TacticsContext.cs`, `Brain/ScavengerTactics.cs`, `Brain/RaiderTactics.cs` | 순수 | 유형별 Alert/Combat(§1.6.2) |
| `Brain/Squad.cs` | 순수 | 분대(§1.6.3) |
| `Brain/IEnemyWorld.cs` | 인터페이스 | brain이 쓰는 월드 질의(엄폐·NavMesh 점) |
| `Sensing/SightCone.cs` | 순수 정적 | 유효 거리·콘 |
| `Sensing/AwarenessModel.cs` | 순수 | 게이지 |
| `Sensing/ILineOfSight.cs` | 인터페이스 | 가림 판정 |
| `Sensing/PhysicsLineOfSight.cs` | 클래스(물리 API) | `RaycastNonAlloc` 구현 |
| `Combat/EnemyBodyModel.cs`, `Combat/EnemyHitResolver.cs` | 순수 | 3부위(§1.3) |
| `Combat/AimError.cs` | 순수 정적 | 명중 오차(§1.4) |
| `Combat/EnemyWeapon.cs` | 순수 | 무장·연사·재장전·발사(§1.4) |
| `Combat/CorpseLootBuilder.cs` | 순수 정적 | 시체 아이템 목록(§1.9) |
| `Cover/CoverSpot.cs`, `Cover/CoverQuery.cs`, `Cover/CoverIndex.cs`, `Cover/CoverSelector.cs` | 순수 | 엄폐(§1.7) |
| `Cover/CoverPoint.cs` | MonoBehaviour | 레벨 배치 |
| `Navigation/PatrolRoute.cs` | MonoBehaviour | 웨이포인트(자식 Transform) |
| `Navigation/EnemyWorld.cs` | 클래스 | `IEnemyWorld` 구현(CoverIndex + CoverSelector + PhysicsLineOfSight + `NavMesh.SamplePosition`, 프레임 예산) |
| `Runtime/AiTickScheduler.cs` | 순수 | 틱 분할(§1.8) |
| `Runtime/TickStats.cs` | struct/클래스 | 측정값 |
| `Runtime/AiDirector.cs` | MonoBehaviour | 씬당 1개: 등록·분대·엄폐 수집·서비스 조회·소음 분배·틱 |
| `Runtime/EnemyAgent.cs` | MonoBehaviour, `IDamageable` | 적 1명 브리지 |
| `Runtime/EnemyDebugView.cs` | MonoBehaviour | 상태 색 표시(MaterialPropertyBlock, 할당 없음) |

#### 공개 API (구현 에이전트가 맞출 시그니처)

```csharp
namespace Ashfall.AI
{
    public enum EnemyState { Idle, Patrol, Suspicious, Alert, Combat, Search, Dead }
    public enum EnemyMoveMode { Hold, Walk, Run, Sprint }
    public enum EnemyPosture { Stand, Crouch }
    public enum EnemyHitRegion { Head, Body, Legs }      // 배열 인덱스로 쓴다. 순서 고정
    public enum CoverHeight { Low, High }
    public enum CoverResult { Found, None, Deferred }     // Deferred = 이번 프레임 예산 초과, 다음 틱 재시도

    // ── 감각·의도 ──
    public struct EnemySenses
    {
        public float Now;
        public Vector3 SelfPosition, SelfForward;
        public bool Arrived;                 // NavMeshAgent: 경로 없음 ∨ (pathPending 아님 ∧ remainingDistance ≤ stoppingDistance)
        public bool TargetExists;            // IPlayerTarget 있음 ∧ !IsDead
        public bool TargetVisible;           // 이번 감각 갱신에서 보임
        public Vector3 TargetPosition, TargetEyePosition, TargetCenter, TargetVelocity;   // 보일 때만 갱신
        public float TargetDistance, EffectiveSightRange;
        public bool HiddenFromTarget;        // RDR 엄폐 무효화 판정용(엄폐 중일 때만 계산, 아니면 true)
    }

    public struct EnemyIntent
    {
        public EnemyMoveMode Move;
        public bool HasDestination; public Vector3 Destination;
        public bool LookAtPoint; public Vector3 LookPoint;   // false면 이동 방향
        public EnemyPosture Posture;
        public bool Fire; public Vector3 FireAt;             // FireAt = 표적 중심(오차는 EnemyWeapon이 더함)
        public bool Reload;
    }

    public sealed class EnemyBrain
    {
        public EnemyBrain(int agentId, EnemyArchetypeSO archetype, AiConfigSO config, IEnemyWorld world,
                          IReadOnlyList<Vector3> patrolRoute, Squad squad, System.Random random);
        public int AgentId { get; }
        public EnemyState State { get; }
        public float Awareness { get; }
        public bool HasLastKnown { get; }
        public Vector3 LastKnownPosition { get; }
        public EnemyIntent Intent { get; }                 // 마지막 Tick 결과 (struct 복사)
        public float TimeInState { get; }
        public event Action<EnemyState, EnemyState> StateChanged;   // (이전, 다음). 디버그 뷰·테스트용

        public void Tick(in EnemySenses senses, float dt);          // 감각 주기마다 (dt = 직전 Tick 이후 시간)
        public void OnHeard(Vector3 position, NoiseKind kind, float now);
        public void OnDamaged(Vector3 estimatedSource, float now);
        public void OnSquadContact(Vector3 position, float now);
        public void OnDied();
    }

    public interface IEnemyWorld
    {
        CoverResult TryFindCover(in CoverQuery query, out CoverSpot spot);   // 성공 시 query.AgentId로 예약
        void ReleaseCover(int agentId);
        bool IsBlocked(Vector3 from, Vector3 to, int ignoreAgentId);         // 엄폐 무효화 등 (표적 루트는 무시)
        bool TrySampleNavPoint(Vector3 around, float radiusM, out Vector3 point);
        float Now { get; }
    }

    public sealed class Squad
    {
        public Squad(int id, AiConfigSO config);
        public int Id { get; }
        public int LeaderId { get; }                       // 없으면 -1
        public int Count { get; }
        public void Join(EnemyBrain brain);
        public void Leave(EnemyBrain brain);               // 리더면 다음 생존자로
        public int IndexOf(int agentId);                   // 대형 슬롯 (리더 0)
        public Vector3 FormationSlot(int index, Vector3 leaderPos, Vector3 leaderForward);
        public void ReportContact(int reporterId, Vector3 reporterPos, Vector3 contactPos, float now);
        public bool TryClaimFlank(int agentId, float now);
        public void ReleaseFlank(int agentId, float now);
        public int FlankHolder { get; }                    // 없으면 -1
    }

    // ── 감각 ──
    public static class SightCone
    {
        public static float EffectiveRange(float sightRangeM, TargetPosture posture, AiConfigSO config);
        // fullAngleDeg = 110 → 반각 55
        public static bool InCone(Vector3 eye, Vector3 forward, Vector3 point, float fullAngleDeg, float rangeM);
    }

    public sealed class AwarenessModel
    {
        public AwarenessModel(AiConfigSO config);
        public float Value { get; }
        public bool IsFull { get; }
        public void AddSight(float distanceM, float effectiveRangeM, float dt);
        public void AddNoise(NoiseKind kind);              // Gunshot은 SetFull
        public void Decay(float dt);
        public void SetFull();
        public void Clear();
    }

    public interface ILineOfSight
    {
        // from→to 첫 충돌이 없거나 targetRoot 소속이면 true. ignoreRoot 소속 콜라이더는 건너뛴다.
        bool CanSee(Vector3 from, Vector3 to, Transform targetRoot, Transform ignoreRoot);
        // from→to 사이에 ignoreA·ignoreB 소속이 아닌 콜라이더가 있으면 true.
        bool IsBlocked(Vector3 from, Vector3 to, Transform ignoreA, Transform ignoreB);
    }

    // ── 전투 ──
    public static class EnemyHitResolver
    {
        public static EnemyHitRegion Resolve(float localY, float capsuleHeight, AiConfigSO config);
    }

    public sealed class EnemyBodyModel
    {
        public EnemyBodyModel(EnemyArchetypeSO archetype, AiConfigSO config);
        public float GetHp(EnemyHitRegion region);
        public float GetMaxHp(EnemyHitRegion region);
        public bool IsDestroyed(EnemyHitRegion region);
        public bool IsDead { get; }
        public EnemyHitRegion? DeathCause { get; }
        public float MoveSpeedMultiplier { get; }          // 다리 파괴 0.6
        public bool SprintAllowed { get; }
        public void ApplyHit(EnemyHitRegion region, float damage);   // 파괴 부위 → 70% 몸통, 사망 후 무시
        public event Action<EnemyHitRegion> Died;
    }

    public static class AimError
    {
        public static float Radius(EnemyArchetypeSO a, float distanceM, float timeOnTargetSec,
                                   bool selfMoving, bool targetMoving, int shotIndexInBurst);
        // 사격선(muzzle→aimPoint)에 수직인 반지름 radiusM 원판 안 균일 점 → 방향(정규화)
        public static Vector3 Direction(Vector3 muzzle, Vector3 aimPoint, float radiusM, System.Random random);
    }

    public sealed class EnemyWeapon
    {
        public EnemyWeapon(EnemyLoadout loadout, int spareMagazines, EnemyArchetypeSO archetype,
                           IProjectileService projectiles, System.Random random);
        public EnemyLoadout Loadout { get; }
        public WeaponItem Item { get; }                    // 장착 탄창 포함 (Magazine.Full로 시작)
        public WeaponModel Model { get; }                  // Core(이동됨)
        public LocalMagazineSource Spares { get; }         // Core(이동됨)
        public float Damage { get; }                       // DamageRules.ProjectileDamage
        public bool HasAmmo { get; }
        public int ShotsInBurst { get; }
        // 매 프레임. wantsFire이면 점사/반자동 패턴으로 방아쇠. 이번 프레임 쏜 발 수.
        // errorRadiusM은 호출 측이 AimError.Radius로 계산(점사 순번은 ShotsInBurst).
        public int Tick(float now, float dt, bool wantsFire, Vector3 muzzle, Vector3 aimPoint, float errorRadiusM,
                        Transform shooterRoot);
        public bool TryReload();                           // 일반 재장전 시작 (재장전 중·예비 없음이면 false)
        public void CancelReload();
        public void CollectLoot(List<ItemInstance> into);  // [무기(장착 탄창 포함)] + [예비 탄창] (CancelReload 후 호출)
    }

    public static class CorpseLootBuilder
    {
        // weapon.CollectLoot + LootRoller.Roll(table). 순서: 무기 → 탄창 → 루트
        public static ItemInstance[] Build(EnemyWeapon weapon, LootTableSO table, System.Random random);
    }

    // ── 엄폐 ──
    public struct CoverSpot
    {
        public int Id;
        public Vector3 Position;       // 숨는 발 위치
        public Vector3 Forward;        // 막아 주는 방향(수평 정규화)
        public CoverHeight Height;
        public Vector3 PeekPosition;   // 사격 발 위치 (Low면 Position)
    }

    public struct CoverQuery
    {
        public int AgentId;
        public Vector3 Self;
        public Vector3 ThreatEye;      // 위협(표적 눈 또는 LKP + 선 눈 높이)
        public Vector2 PreferredRangeM;
        public bool Flank;             // true면 측면 각 조건 적용
        public Vector3 FlankFrom;      // 우회 전 자기 위치
    }

    public sealed class CoverIndex
    {
        public CoverIndex(int capacity);
        public int Count { get; }
        public int Add(Vector3 position, Vector3 forward, CoverHeight height, Vector3 peekPosition);   // id 반환
        public CoverSpot Get(int id);
        public int OccupantOf(int id);                     // 없으면 -1
        public bool TryReserve(int id, int agentId);
        public void ReleaseAll(int agentId);
        public int ReservedBy(int agentId);                // 없으면 -1
    }

    public static class CoverSelector
    {
        // §1.7 점수 → 상위 config.coverLosCandidates개 LOS 확인. scratch 길이 ≥ index.Count.
        // selfRoot·targetRoot는 LOS 무시 대상. 성공 시 예약까지 한다.
        public static bool TrySelect(CoverIndex index, in CoverQuery query, AiConfigSO config, ILineOfSight los,
                                     Transform selfRoot, Transform targetRoot, int[] scratchIds, float[] scratchScores,
                                     out CoverSpot spot);
    }

    [DisallowMultipleComponent]
    public sealed class CoverPoint : MonoBehaviour
    {
        [SerializeField] private CoverHeight height;
        [Tooltip("High 엄폐에서 사격하러 나오는 위치 (로컬). Low면 무시")]
        [SerializeField] private Vector3 peekLocalOffset;
        public CoverHeight Height { get; }
        public Vector3 PeekPosition { get; }               // 월드
        // forward = transform.forward 수평 성분
    }

    public sealed class PatrolRoute : MonoBehaviour
    {
        [SerializeField] private Transform[] waypoints;    // 비면 자식 순서
        public IReadOnlyList<Vector3> Points { get; }      // Awake에 캐시 (순환)
    }

    // ── 런타임 ──
    public sealed class AiTickScheduler
    {
        public AiTickScheduler(int capacity);
        public int Add(float firstDueTime);                // 슬롯 반환
        public void Remove(int slot);
        public void Reschedule(int slot, float nextDueTime);
        // now 기준 기한이 지난 슬롯을 오래 기다린 순으로 최대 budget개 slotsOut에 채운다. 개수 반환. 할당 없음.
        public int CollectDue(float now, int budget, int[] slotsOut);
    }

    public sealed class TickStats
    {
        public double LastMs { get; } public double AverageMs { get; } public double MaxMs { get; } public int Samples { get; }
        public void Add(double ms);
        public void Reset();
    }

    public sealed class AiDirector : MonoBehaviour
    {
        [SerializeField] private AiConfigSO config;
        public AiConfigSO Config { get; }
        public IReadOnlyList<EnemyAgent> Agents { get; }
        public TickStats Stats { get; }
        public CoverIndex Covers { get; }
        public bool TryGetSquad(int squadId, out Squad squad);
        public IPlayerTarget Target { get; }                // 없으면 null (targetResolveRetrySec마다 재조회)
        public IProjectileService Projectiles { get; }
        public void Register(EnemyAgent agent);
        public void Unregister(EnemyAgent agent);
        // 내부: OnEnable/OnDisable에서 NoiseEmitted 구독/해지. Start에서 CoverPoint 수집, 서비스 조회.
        // Update: ProfilerMarker "Ashfall.AI.Tick" + Stopwatch → Stats.
    }

    [RequireComponent(typeof(NavMeshAgent), typeof(CapsuleCollider), typeof(Rigidbody))]
    public sealed class EnemyAgent : MonoBehaviour, IDamageable
    {
        [SerializeField] private EnemyArchetypeSO archetype;
        [SerializeField] private AiDirector director;      // 비면 Awake에서 FindFirstObjectByType 1회
        [SerializeField] private PatrolRoute patrolRoute;  // 비면 경로 없음(Idle 경계)
        [SerializeField] private int squadId;              // 0 = 단독
        [SerializeField] private int loadoutIndex = -1;    // -1 = 가중치 무작위
        [SerializeField] private int seed;                 // 0 = 무작위 (테스트·씬은 고정값)
        [SerializeField] private Transform eye;
        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform bodyVisual;     // 사망 시 눕힐 시각 루트

        public int AgentId { get; }                        // director가 부여
        public EnemyArchetypeSO Archetype { get; }
        public EnemyBrain Brain { get; }
        public EnemyState State { get; }
        public EnemyBodyModel Body { get; }
        public EnemyWeapon Weapon { get; }                 // director의 IProjectileService가 생긴 뒤 생성(Start 이후)
        public bool IsDead { get; }
        public void ApplyDamage(in DamageInfo info);
        // director가 호출 (테스트에서 직접 부르지 않는다)
        internal void Sense(IPlayerTarget target, ILineOfSight los, float now);
        internal void FrameTick(float now, float dt);
        internal void OnHeard(in NoiseEmitted noise, float now);
    }
}
```
- `EnemyAgent` 필수 컴포넌트·설정: CapsuleCollider(높이 1.8, 반지름 0.35, center y 0.9, 트리거 아님), Rigidbody(`isKinematic = true`, 중력 끔), NavMeshAgent(§1.10).
- **적 루트는 씬 최상위 오브젝트여야 한다.** `ProjectileSystem`이 `transform.root`로 자기 탄을 거르므로, "Enemies" 같은 부모 아래 두면 모든 적이 한 루트가 돼 서로 맞지 않고 시야 루트 비교도 틀린다.
- 웅크림: CapsuleCollider 높이·center만 바꾼다(NavMeshAgent 높이 그대로).
- config null(`archetype`·director config) → `LogError` 후 `enabled = false`(M2 관례).

### 5.2 Weapons (`Ashfall.Weapons`)
| 파일 | 변경 |
|---|---|
| `ProjectileSystem.cs` | `IProjectileService` 구현. `Fire(origin, dir, ammo, damage, shooterRoot)` 추가, 기존 4인자 `Fire`는 `ammo.damage`로 위임(TestTurret·테스트 호환). OnEnable Register / OnDisable Unregister. FixedUpdate를 `ProfilerMarker("Ashfall.Projectiles.Step")` + Stopwatch로 감싸 `public TickStats`처럼 쓸 수 있게 하되 **AI의 TickStats를 참조할 수 없으므로** 같은 의미의 `public double LastStepMs, AverageStepMs, MaxStepMs; public void ResetStats()`를 직접 둔다 |
| `WeaponController.cs` | `Shoot`: `projectiles.Fire(origin, dir, ammo, DamageRules.ProjectileDamage(weapon, ammo), shooterRoot)`. 쏠 때 `weapon.shotNoiseRadiusM > 0`이면 `NoiseEmitted(muzzle 위치, 반경, Gunshot)` 발행(펠릿 수와 무관하게 발당 1회) |
| `WeaponModel.cs`, `FireControl.cs`, `LocalMagazineSource.cs` | Core로 이동(1단계) |

### 5.3 Player (`Ashfall.Player`)
| 파일 | 변경 |
|---|---|
| `PlayerMotor.cs` | `IPlayerTarget` 구현·등록(OnEnable Register / OnDisable Unregister). `Root = transform`, `EyePosition = eye.position`, `CenterPosition = transform.TransformPoint(controller.center)`, `Velocity = controller.velocity`, `Posture`(Stance → TargetPosture), `IsDead = health != null && health.IsDead`. Update에서 `FootstepNoiseModel.Tick` → 발소리 `NoiseEmitted`, 점프 성공 시 즉시 `Jump` 발행. `TODO(M3)` 주석 정리 |
| `FootstepNoiseModel.cs` (새, 순수) | `public FootstepNoiseModel(PlayerConfigSO config)`, `public bool Tick(float dt, float radiusM)` — radius > 0인 동안 누적, `noisePulseIntervalSec`마다 true. radius 0이 되면 누적 초기화(다시 움직이면 첫 펄스는 곧바로: 누적을 간격 값으로 시작) |
| `PlayerConfigSO.cs` | `noisePulseIntervalSec = 0.5f` (TODO(GDD-?)) |

### 5.4 Inventory (`Ashfall.Inventory`)
| 파일 | 변경 |
|---|---|
| `LootContainer.cs` | `public static LootContainer AttachRuntime(GameObject host, string displayName, Vector2Int gridSize, IEnumerable<ItemInstance> items, float searchTimePerItemSec)` — 컴포넌트 추가, displayName·gridSize 설정, `Model`을 즉시 생성. 이후 `Interact`는 기존 경로(Model이 있으면 그대로 연다) |
| `PlayerInventoryBehaviour.cs` | `EnemyKilled` 구독/해지 → `AttachRuntime(evt.Corpse, evt.DisplayName, config.corpseGridSize, evt.Items, config.searchTimePerItemSec)`. Corpse가 null/파괴됐으면 무시 |
| `InventoryConfigSO.cs` | `corpseGridSize = new Vector2Int(6, 5)` (TODO(GDD-?)) |

### 5.5 UI
변경 없음.

### 5.6 EditorTools (`Ashfall.EditorTools`)
| 파일 | 내용 |
|---|---|
| `M3DataSetup.cs` (새, 2c) | 메뉴 `Ashfall/Setup/Create M3 Data`, 배치 `Ashfall.EditorTools.M3DataSetup.CreateAll`. 먼저 `M2DataSetup.CreateData()`. §4.3 무기·탄·탄창, §4.4 루트 테이블, §4.1 아키타입 2개, `AiConfig.asset`(초기값 그대로) 생성(없을 때만). **이전**: `WPN-RIF-01.damage <= 0`이면 45, `shotNoiseRadiusM <= 0`이면 120 → SetDirty. `PlayerConfig`·`InventoryConfig`는 새 필드가 초기값이라 값 입력 불필요(파일 기록은 SaveAssets 시 생김, 필요하면 SetDirty만). 경로 상수 `public const`(`AiFolder = Data/AI`, `LootFolder = Data/Loot`, `ScvPath`, `RdrPath`, `AiConfigPath`, `PistolPath`, `SmgPath`, `Ammo9mmPath`, `Mag9mm12Path`, `Mag9mm30Path`, `LootScvPath`, `LootRdrPath`). 멱등 |
| `M3SceneSetup.cs` (새, 3단계) | 메뉴 `Ashfall/Setup/Create M3 AI Scene`(+ `Create M3 AI Perf Scene`), 배치 `Ashfall.EditorTools.M3SceneSetup.CreateAll`(두 씬 모두). 씬은 매번 새로 만들어 덮어씀(M2 방식, GUID 유지). §8 레이아웃 |
| `M2SceneSetup.cs` | 3단계에서 `CreatePlayer`, `CreateGameUI`, `CreateBlock`을 `internal static`으로 바꿔 재사용 가능(동작·M2 씬 결과 불변, M2SceneSmokeTests로 확인) |

---

## 6. 감각·전투 세부 규칙 (구현 메모)

- **감각 갱신 1회** (`EnemyAgent.Sense`):
  1. target 없음/사망 → `TargetExists=false`.
  2. `range = SightCone.EffectiveRange(archetype.sightRange, target.Posture, config)`, `InCone(eye, eye.forward, target.CenterPosition, sightAngle, range)`(눈 forward = 몸 forward + 수평. 고개 회전 없음).
  3. 콘 안이면 `los.CanSee(eye, target.EyePosition, target.Root, transform)` → 실패 시 `CenterPosition`으로 한 번 더.
  4. 엄폐 중(RDR, Hide 단계)이면 `HiddenFromTarget = !los.CanSee(target.EyePosition, eye.position, transform, target.Root)`(표적 눈에서 내 눈). 아니면 true.
  5. `AwarenessModel`: 보이면 `AddSight`, 아니면 `Decay`. → `EnemySenses` 채워 `brain.Tick`.
- **매 프레임** (`EnemyAgent.FrameTick`): 의도 적용 — 목적지(재경로 규칙·예산), 속도(Move 모드별 속도 × `Body.MoveSpeedMultiplier`, Sprint 불가면 Run), 회전(LookAtPoint면 `updateRotation=false` + `turnSpeedDegPerSec`), 자세(캡슐), 사격(`Weapon.Tick`), 재장전, `WeaponModel.Tick`(EnemyWeapon 안).
- **소리**: `AiDirector`가 `NoiseEmitted`마다 등록된 살아 있는 적을 돌며 반경 확인 → `agent.OnHeard` → `brain.OnHeard`.
- **피격**: `ApplyDamage` → 부위 → `Body.ApplyHit` → 살아 있으면 `brain.OnDamaged(추정 위치)`.

---

## 7. 작업 분할과 순서

배치모드 Unity는 한 번에 하나만 돌 수 있다. **원본 저장소에서 배치모드를 돌리는 것은 1단계 끝, 3단계, 4단계뿐(순차).**
2단계 병렬 에이전트는 1단계 완료 상태를 각자 scratchpad로 복사(`Assets`, `Packages`, `ProjectSettings` + 가능하면 `Library` — 없으면 첫 임포트가 수 분 걸림)한 사본에서만 배치모드로 컴파일·테스트하고, 원본에는 **자기 소유 파일만** 쓴다(M2 2a가 쓴 방식). 사본에서 다른 에이전트 파일이 필요하면 1단계 계약(시그니처)만 믿는다.

| 단계 | 담당 | 작업 | 마감 조건 |
|---|---|---|---|
| **1. 계약** | gameplay-programmer | §3 Core 전부(이동 3개는 `git mv` 파일+.meta), EventBus 무할당화, `ProjectileSystem`의 `IProjectileService` 구현(5인자 Fire·등록만, 통계는 2a), AI의 **타입 껍데기**: `AiEnums.cs`, `EnemyArchetypeSO`(+`EnemyLoadout`), `AiConfigSO`(필드+초기값 §4.2), `ILineOfSight`, `IEnemyWorld`, `CoverSpot`, `CoverQuery`, `EnemySenses`, `EnemyIntent`(필드만). §4.5 SO 필드 3개. §2 asmdef 변경. 테스트 도우미 `Tests/EditMode/M3TestData.cs`(새: `Pistol9()`, `Smg9()`, `Ammo9()`, `Mag9_12()`, `Mag9_30()`, `RifleWithDamage()`, `Scv()`, `Rdr()`, `AiConfig()`, `LootTable(params (ItemDefinitionSO, float)[])`, `FakeProjectileService`(발사 기록), `FakeLineOfSight`(막힘 규칙 람다는 테스트 준비 단계에서만), `FakeEnemyWorld`). 테스트: `DamageRulesTests`, `LootRollerTests`, EventBus 0할당 1개 | 원본에서 EditMode 전체 통과(기존 250 + 새) |
| **2a** | gameplay-programmer | §5.2 Weapons(통계 포함), §5.3 Player(`FootstepNoiseModel`, `IPlayerTarget`, 소음 발행), §5.4 Inventory(`AttachRuntime`, `EnemyKilled` 구독). 테스트 새 파일: `NoiseEmitTests.cs`(EditMode: FootstepNoiseModel 간격·정지 시 없음), `CorpseLootContainerTests.cs`(AttachRuntime 모델·검색 순서) | 사본에서 EditMode·PlayMode(M1·M2 기존) 통과 |
| **2b** | ai-programmer | §5.1 AI 전부(1단계 껍데기 파일은 이어서 채움, 필드 추가 시 초기값 필수). EditMode 테스트 새 파일: `AiSightTests`, `AwarenessTests`, `EnemyBrainTests`(전이 표 T1~T13), `SquadTests`, `CoverSelectorTests`, `EnemyBodyTests`, `AimErrorTests`, `EnemyWeaponTests`, `CorpseLootBuilderTests`, `AiSchedulerTests`, `AiAllocationTests` | 사본에서 EditMode 통과 |
| **2c** | data-designer | `M3DataSetup.cs`, `Tests/EditMode/M3DataAssetTests.cs`(에셋 값 = §4 표, WPN-RIF-01 damage 45, 9mm 무기 damage 35/30, 탄창 12/30, 아키타입 §7 값, 루트 테이블 항목 존재, 멱등). 사본에서 메뉴 실행해 확인. **원본에는 에셋을 만들지 않는다**(3단계가 만든다) | 사본에서 메뉴 실행 + 데이터 테스트 통과 |
| **3. 씬** | ai-programmer | `M3SceneSetup.cs`(§8), `M2SceneSetup` 도우미 internal화, NavMesh 베이크(§1.10), 원본에서 `-executeMethod Ashfall.EditorTools.M3SceneSetup.CreateAll`(데이터 포함) 실행, 재실행 시 `.meta` 불변 확인. `Tests/PlayMode/M3SceneSmokeTests.cs` | 원본에서 EditMode·PlayMode 전체 통과 |
| **4. 검증** | test-engineer | §9 PlayMode 통합 테스트 + `M3TestSupport.cs`(`M3Scene` 로더, 플레이어 순간이동, `SimMouse`(LMB), 무적 표적 `TestTarget`), 성능 테스트, AC 체크 | 원본 EditMode·PlayMode 전체 통과, 성능 수치 보고 |
| 5 | code-reviewer | 의존 방향(AI가 Core만), 싱글톤 1개(AiDirector에 static Instance 없음), 하드코딩 수치, Update 경로 할당, 이벤트·서비스 해지 누락, 적 루트 최상위 | |

파일 소유 겹침 방지
- `TestData.cs`는 아무도 고치지 않는다. M3 도우미는 1단계의 `M3TestData.cs`, 이후 추가는 각자 새 파일.
- `ProjectileSystem.cs`: 1단계(인터페이스·Fire) → 2a(통계·마커). 순차라 겹치지 않음.
- AI 껍데기 파일(1단계) → 2b만 수정. `EnemyArchetypeSO`/`AiConfigSO` 필드는 2b만 추가 가능(초기값 필수), 2c는 읽기만.
- `M2SceneSetup.cs`는 3단계만.
- 각 단계는 끝에 이 문서 맨 아래에 "N단계 구현 메모"를 덧붙인다(M2와 같은 방식).

---

## 8. 테스트 씬 (3단계 `M3SceneSetup`)

공통: 원점 = 바닥 중앙, 바닥 윗면 y 0. 환경은 전부 `Environment` 루트 아래(NavMeshSurface 대상). 적·플레이어·UI·관리자는 최상위.
씬 오브젝트: `Environment`(+NavMeshSurface), `ProjectileSystem`, `AiDirector`(config = AiConfig.asset), `Player`(M2와 같은 구성·시작 장비, `M2SceneSetup.CreatePlayer` 재사용), `GameUI`, 적들, `PatrolRoute`들.

**적 오브젝트** (`CreateEnemy(name, archetype, position, yaw, route, squadId, loadoutIndex, seed)`)
```
Enemy_<id>   (최상위) EnemyAgent, NavMeshAgent, CapsuleCollider(h1.8 r0.35 c(0,0.9,0)), Rigidbody(kinematic), EnemyDebugView
  Body       시각 캡슐(콜라이더 제거), 유형 색: SCV 갈색 / RDR 짙은 회색 (그레이박스 재질, 없을 때만 생성)
  Eye        (0, 1.6, 0.1)
  Muzzle     (0.2, 1.35, 0.5)
  Gun        큐브(콜라이더 제거)
  Indicator  머리 위 작은 구(콜라이더 제거) — EnemyDebugView가 상태 색
```
seed = 씬 내 순번 + 1(결정적). loadoutIndex는 아래 명시.

### 8.1 `Scenes/Test/M3_AI.unity` (사람 확인 + 기능 PlayMode 테스트)
- 바닥 100×100.
- 플레이어 (0, 0.05, -40), +z.
- 엄폐 구조물(각각 양쪽에 CoverPoint, forward = 벽 쪽):
  - 낮은 벽(높이 1.1m, 4×0.4) 8개: (-20,-15), (-10,0), (10,0), (20,-15), (-5,15), (5,15), (-15,28), (15,28) — 벽 앞뒤 0.7m에 Low CoverPoint 1개씩.
  - 높은 벽(높이 2.5m, 3×0.4) 4개: (-25,10), (25,10), (0,30), (0,-20) — 앞뒤 0.7m에 High CoverPoint, `peekLocalOffset = (±1.9, 0, 0)`(벽 끝 밖으로).
  - 차량 블록(4×1.5×2) 2개: (-30,-25), (30,-25) — 둘레 4방향 Low CoverPoint.
- 분대 A: SCV 3명(loadout 0,0,1 = 권총,권총,SMG), squadId 1, 경로 A 순환 (-30,-5)→(-30,25)→(-10,25)→(-10,-5).
- 분대 B: SCV 2명(loadout 0,1), squadId 2, 경로 B 순환 (15,-5)→(35,-5)→(35,20)→(15,20).
- 분대 C: RDR 2명(loadout 0), squadId 3, 경로 없음(Idle 경계), (−3,35)·(3,35)에서 -z(플레이어 쪽) 방향.
- 시작 시 플레이어에서 가장 가까운 적은 25m 이상 떨어지게 둔다.

### 8.2 `Scenes/Test/M3_AIPerf.unity` (성능)
- 바닥 140×140, 8.1의 구조물 배치를 2×2로 반복(오프셋 ±35), CoverPoint 같은 규칙.
- 적 20명: SCV 분대 4개 × 3명(12, 경로 4개), RDR 분대 4개 × 2명(8, 경로 없음). 플레이어 중앙 남쪽.
- `ProjectileSystem.drawDebugTrails = false`, `AiConfig.showStateIndicator`는 그대로(측정 포함).
- 둘 다 빌드 설정에 넣지 않는다.

---

## 9. AC별 테스트 계획

### AC1 — AI-SCV, AI-RDR 상태 머신·시야·청각
| 종류 | 테스트 |
|---|---|
| EditMode `AiSightTests` | 콘: 54° 안 / 56° 밖, 60m 안 / 61m 밖. 자세: 서기 59m 보임, 웅크림 35m 보임·37m 안 보임, 엎드림 17m 보임·19m 안 보임. 가림: FakeLineOfSight 막힘 → 안 보임 |
| EditMode `AwarenessTests` | 근거리 빠름·원거리 느림(시간 비교), 4m 안 즉시 1, 감소, 발소리 +0.25, 총성 즉시 1 |
| EditMode `EnemyBrainTests` | 전이 표 T1~T13 각 1개 이상(가짜 감각 시퀀스). SCV Suspicious → 자극 위치로 Walk 의도, RDR Suspicious → Hold + LookAt. Alert 반응 시간 0.6/0.4 경계. 분대 연락 → Alert. 표적 사망 → Patrol. Dead 이후 모든 입력 무시 |
| EditMode `SquadTests` | 리더 승계, 대형 슬롯 위치, 공유 거리 50m 경계, 보고 간격, 우회권 1명·쿨다운 |
| EditMode `NoiseEmitTests` (2a) | FootstepNoiseModel 0.5초 간격, 정지 시 없음, 반경 = 자세별 §4.1 값 |
| PlayMode `AiSenseFlowTests` (4) | 플레이어를 RDR 정면 20m에 세움 → 2초 안에 Alert→Combat. 웅크려 40m → 5초간 Suspicious 미만. 높은 벽 뒤 → 못 봄. SCV 등 뒤 15m에서 질주(SimKeyboard W+Shift) → Suspicious + 소리 위치로 이동(거리 감소). 사격(SimMouse LMB) → 반경 120m 안 적 Alert, `NoiseEmitted(Gunshot)` 기록. 분대 A 한 명이 보면 0.5초 안에 나머지 Alert |
| 사람 | 순찰 대형이 자연스러운지, 웅크림·엎드림 접근이 체감되는지, 상태 색 변화 |

### AC2 — 엄폐 사용, 적 사망 시 시체 루팅
| 종류 | 테스트 |
|---|---|
| EditMode `CoverSelectorTests` | 보호각 밖 제외, 예약된 지점 제외, 시야 차단 실패 후보 제외, 사격 불가 후보 제외, 점수 순서, 상위 3개만 LOS 질의(Fake 호출 수), 측면 각 45~120 조건 |
| EditMode `EnemyBodyTests` / `EnemyHitResolverTests` | 머리 35 → 즉사 1발(45), 몸통 45×2 사망, 다리 0 → ×0.6·질주 불가, 파괴 다리 추가 피해 70% 몸통, 초과분 버림, 사망 후 무시. 높이 비율 0.86/0.84/0.46/0.44 |
| EditMode `EnemyWeaponTests` | 권총 반자동 간격 ≥ 60/400, SMG 점사 발 수 범위, 탄 소모·0발 재장전·예비 감소, Damage = 무기 값(35/30/45), FakeProjectileService 기록(사수 루트·피해) |
| EditMode `CorpseLootBuilderTests` / `LootRollerTests` | 순서(무기→탄창→루트), 장착 탄창 남은 탄 수 보존, rollCount 범위, 가중치 0 제외, 시드 고정 재현 |
| EditMode `CorpseLootContainerTests` (2a) | AttachRuntime → Model 즉시 생성, 0.5초당 1개 드러남, 그리드 초과 제외 |
| PlayMode `AiCombatFlowTests` (4) | RDR 경계 상태에서 플레이어 위치 총성 → 10초 안에 CoverPoint 도착·예약, 그 위치(웅크림 눈)가 플레이어 눈에서 가려짐(Physics 레이). 플레이어가 높은 벽 뒤에 계속 숨어 있으면 `flankAfterNoSightSec` + 15초 안에 한 RDR이 표적 기준 45° 이상 이동. 적 Combat 시 투사체 발사·플레이어 부위 HP 감소(15m, 10초) |
| PlayMode `CorpseLootFlowTests` (4) | SCV를 투사체로 사살(머리 높이 1발) → `EnemyKilled` 1회, 시체에 `LootContainer`, 플레이어를 시체 옆으로 옮겨 F → 루트 열림, 0.5초 간격 공개, 첫 아이템이 그 적의 무기이고 장착 탄창 탄 수 = 사망 시점 값, 가져오기(TryQuickMove) 성공 |
| 사람 | RDR이 엄폐 뒤 웅크림↔일어나 사격, 측면 우회, 시체 검색 연출 |

### AC3 — 동시 적 20명 60fps
| 종류 | 테스트 |
|---|---|
| EditMode `AiAllocationTests` | `EnemyBrain.Tick`(각 상태), `SightCone`, `AwarenessModel`, `CoverSelector.TrySelect`(Fake LOS), `AiTickScheduler.CollectDue`, `EnemyWeapon.Tick`(Fake 발사), `EventBus.Publish<NoiseEmitted>` → 전부 `Is.Not.AllocatingGCMemory()` |
| EditMode `AiSchedulerTests` | 예산 8 초과분 다음 프레임, 오래 기다린 순, 위상 분산(20명 0.2초 주기 → 프레임당 고르게) |
| PlayMode `AiPerformanceTests` (4) | §1.8: M3_AIPerf, 플레이어 제거 + `TestTarget`(무적) 등록, 20명 Combat 확인, 워밍업 3초 + 측정 10초. AI 평균 ≤ 1.0ms·최대 99%(정렬 후) ≤ 3ms, Projectiles 평균 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms. 결과 수치를 `Debug.Log`로 남김(보고용). 실패 시 수치와 함께 보고(기준 조정은 오케스트레이터 판단) |
| 사람 | 에디터에서 M3_AIPerf Play + Profiler(CPU: `Ashfall.AI.Tick`, `Ashfall.Projectiles.Step`), Stats 창 FPS. 가능하면 개발 빌드로 1회 |

### AC4 — 같은 피해 공식, 3부위
- EditMode: `DamageRulesTests`(무기 > 0 → 무기, 0 → 탄약, null 무기 → 탄약), `EnemyBodyTests`(위).
- PlayMode: 적이 쏜 탄이 플레이어 `PlayerHealth`(M2 7부위)로 들어가 피해 = 무기 값(권총 35)인지 — FakeProjectileService가 아니라 실제 ProjectileSystem, SCV 권총 고정 시드로 명중 1발 확인(오차 반지름 0인 테스트 아키타입 사용).

### 회귀
- M1·M2 PlayMode 20개, EditMode 250개 그대로 통과(이동한 클래스, EventBus, ProjectileSystem 4인자 Fire 유지).
- `M3SceneSmokeTests`: 두 씬 로드, NavMeshSurface 데이터 = `Scenes/Test/M3_AI/NavMesh-Environment.asset`, 모든 적 위치 `NavMesh.SamplePosition` 성공, 적이 최상위, CoverPoint 수 = director `Covers.Count`, `IPlayerTarget`·`IProjectileService` 등록, 1초 뒤 모든 적 Idle/Patrol, 콘솔 오류 없음.

---

## 10. 결정 표 (요약)

| # | 결정 | 사유 |
|---|---|---|
| D1 | AI ↔ 다른 기능: 조회·발사는 Core 인터페이스(`IPlayerTarget`, `IProjectileService`) + 레지스트리, 소음·사망은 이벤트(`NoiseEmitted`, `EnemyKilled`) | M2 원칙(조회=인터페이스, 알림=이벤트). 발사·시야는 동기·고빈도 |
| D2 | `WeaponModel`·`FireControl`·`LocalMagazineSource`를 Core로 이동 | 적도 실제 탄창으로 쏘고 시체에 남은 탄이 남게. 로직 복제 방지 |
| D3 | `WeaponDefinitionSO.damage`(0 = 미지정) + `DamageRules`, 탄약 damage는 기본값으로 유지 | 사용자 결정. §5.2 무기별 피해(9mm 35/30) 표현 |
| D4 | 적 피격 = 단일 캡슐 + 높이 비율 3구간(AI 전용 resolver), M2 resolver 미변경 | 콜라이더 수·레이어 작업 없음, Player 회귀 위험 없음 |
| D5 | `EnemyArchetypeSO.hp` = 몸통 HP, `headHp`·`legsHp` 추가, 값 35/80/65 | §14 단일 hp와 §7 3부위 양립. 플레이어 수치와 같게 |
| D6 | 적 상태이상 없음, 적끼리 총성 반응 없음, 소음 가림 없음 | §7 단순화, 범위 최소화 |
| D7 | `EnemyArchetypeSO.weapon` → `loadouts[]`(무기·탄·탄창·가중치) | SCV "권총/SMG" 두 무장, 탄·탄창이 사격·시체에 필요 |
| D8 | `LootTableSO.entries` = `List<LootEntry>` | 튜플은 직렬화 불가 |
| D9 | Alert = "확정·반응/접근" 단계, Dead 상태 추가, 전이 표 T1~T13 | §7 화살표를 모두 의미 있게 사용 |
| D10 | 시체 = `EnemyKilled` → Inventory가 `LootContainer.AttachRuntime` | 컨테이너는 Inventory 책임, 사망은 여러 시스템이 받을 사실 |
| D11 | 레이어 추가 없음, LOS는 루트 비교 + 무시 목록 | TagManager·기존 마스크 영향 회피 |
| D12 | 틱 분할(0.2/0.1초, 프레임당 8), 경로·엄폐 예산, EventBus 무할당화 | 20명 60fps, 할당 0 |
| D13 | 성능 판정 = 배치모드 CPU 마커 예산 + 사람 Profiler 확인 | 배치모드는 GPU 미측정 |
| D14 | NavMesh는 에디터 메뉴에서 베이크해 에셋 저장(CopySerialized로 GUID 유지), AI asmdef는 패키지 미참조 | M4 큰 맵도 사전 베이크가 필요, 런타임 의존 최소 |
| D15 | 적 루트는 씬 최상위 | ProjectileSystem 자기 탄 판정·시야 루트 비교 |
| D16 | 사격 소음 반경은 무기 SO(`shotNoiseRadiusM`) | 무기마다 다르고 M7 소음기 대비. GDD 값 없음 → TODO |

---

## 11. CHANGELOG_GDD.md 문안 초안 ("M3 설계 결정")

```markdown
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
  `Ashfall/Setup/Create M3 AI Scene`이 매번 새로 만들고 NavMesh를 굽는다(`Scenes/Test/M3_AI/NavMesh-Environment.asset`). 빌드 설정에는 없음.
  상태 색 표시(머리 위 구)는 그레이박스 확인용이며 M6에서 끈다(`AiConfigSO.showStateIndicator`).
```

---

## 12. 사람이 에디터에서 할 일 (구현 완료 후 안내용)
1. Unity Hub 켜고 프로젝트 열기 → 배치모드로 만들어졌으면 생략, 아니면 `Ashfall/Setup/Create M3 AI Scene`.
2. `Scenes/Test/M3_AI.unity` Play. 확인:
   - 분대 A(3명)·B(2명)가 대형으로 순찰, 머리 위 색(회색 Idle / 흰 Patrol / 노랑 Suspicious / 주황 Alert / 빨강 Combat / 파랑 Search).
   - 걷기·질주로 SCV 뒤에 다가가면 노랑 → 소리 쪽으로 옴. 웅크림·엎드림으로는 더 가까이 접근 가능.
   - 총을 쏘면 반경 안 적이 주황 → 접근. RDR은 벽 뒤로 숨어 웅크렸다 일어나 쏨. 오래 숨어 있으면 한 명이 옆으로 돈다.
   - 적 사살 → 시체에 F → 0.5초마다 무기·탄창·잡동사니 공개 → 가져오기. 권총은 보조무기 슬롯에 장착 가능(쏠 수는 없음, M7).
3. `Scenes/Test/M3_AIPerf.unity` Play + Window > Analysis > Profiler(CPU: `Ashfall.AI.Tick`, `Ashfall.Projectiles.Step`), Game 뷰 Stats로 FPS 확인.
4. 조작감·난이도(명중률, 반응 시간, 감지 속도)는 `Data/AI/AI-SCV.asset`, `AI-RDR.asset`, `AiConfig.asset` 값으로 조정.
5. 씬 NavMesh가 이상하면(파란 영역 확인: Scene 뷰 AI Navigation 오버레이) 메뉴 재실행.

---

---

## 1단계 구현 메모

- **결과**: 원본 저장소 배치모드 EditMode 269/269 통과(기존 250 + 새 19: DamageRules 7, LootRoller 9, EventBus 3), PlayMode 20/20 통과. 이동한 3개 파일은 `git mv`(.cs+.meta, R100 → GUID 그대로), namespace만 `Ashfall.Core`로 바꿈.
- **설계와 달라진 점·보충**
  1. `LocalMagazineSource.HasLoadable`을 LINQ `Any(람다)`에서 for 루프로 바꿨다(동작 같음). 적 `EnemyWeapon`이 재장전 판단에 부르면 매번 클로저를 할당하므로 2b의 `AiAllocationTests`를 위해 미리 고쳤다. `TakeFullest`는 `List<T>` foreach(구조체 열거자)라 그대로.
  2. `IMagazineSource` 주석의 "Weapons의 LocalMagazineSource"를 "Core의 LocalMagazineSource"로 고침.
  3. `LootRoller.Roll`: `item == null`인 항목은 가중치와 상관없이 제외(가중치 합에도 넣지 않음). `rollCount.x < 0`은 0, `y < x`면 x로 본다. `random`·`into`가 null이면 0.
  4. `EnemyArchetypeSO.loadouts`는 `List<EnemyLoadout>`(빈 리스트로 초기화, `LootTableSO.entries`와 같은 방식). 그 밖 필드는 설계대로 초기값 없음. 모든 ★ 필드에 Tooltip.
  5. `ProjectileSystem`: 기존 4인자 `Fire`는 5인자로 위임(`ammo.damage`). OnEnable에서 `GameManager.Instance`가 있을 때만 `IProjectileService` 등록(M2 제공자 관례). 통계·마커는 넣지 않음(2a 몫). `WeaponController.Shoot`는 아직 4인자 호출 그대로(2a가 `DamageRules`로 바꿈) — WPN-RIF-01 에셋 damage가 0인 지금은 결과가 같다.
  6. EventBus 0할당 테스트: 측정할 `TestDelegate`를 **먼저 한 번 실행한 뒤** 측정해야 한다. `Publish`만 미리 불러 두고 처음 실행하는 람다를 측정하면 첫 호출(JIT·트램펄린)에서 할당이 잡혀 실패한다(진단으로 확인). 2b `AiAllocationTests`도 같은 패턴을 쓸 것:
     ```csharp
     TestDelegate act = () => { for (int i = 0; i < 100; i++) target.Tick(...); };
     act();   // 워밍업
     Assert.That(act, UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(Is.Not));
     ```
  7. EventBus 테스트를 별도 파일이 아니라 기존 `EventBusTests.cs`에 3개 추가(0할당, NoiseEmitted 페이로드, 가운데 해지 후 순서 유지).
- **M3TestData.cs (2b·2a·2c용 계약)**
  - 데이터: `Pistol9()`, `Smg9()`, `RifleWithDamage()`(TestData.Rifle + damage 45·소음 120), `Ammo9()`, `Ammo556()`(★추가, M1 에셋 값), `Mag9_12()`, `Mag9_30()`, `Scv()`·`Rdr()`(§4.1 값 전부 + `loot` = §4.4 항목을 TestData 아이템으로 만든 테이블, id LT-SCV/LT-RDR), `AiConfig()`(= `CreateInstance`, 초기값이 설계값), `LootTable(params (item, weight)[])`와 ★`LootTable(Vector2Int rollCount, params ...)`.
  - `FakeProjectileService`: `Shots`(List, 기본 용량 4096 미리 할당 → 그 안에서는 기록해도 할당 없음), `Count`, `Last`, `Clear()`. `Shot { Origin, Direction, Ammo, Damage, ShooterRoot }`.
  - `FakeLineOfSight`: `BlockedRule`(Func<from,to,bool>, null = 막힘 없음), `CanSee = !blocked`, `IsBlocked = blocked`, 호출 수 `CanSeeCalls`·`IsBlockedCalls`·`TotalCalls`, `ResetCounts()`. 루트 인자는 무시.
  - `FakeEnemyWorld`: `Now`(set 가능), `AddCover(pos, forward, height = Low, peek = null)` → id, `TryFindCover`는 자기에게 가장 가까운 비예약(자기 예약 허용) 지점을 골라 예약(점수·LOS 없음), `ForcedCoverResult`(None/Deferred 강제), `ReleaseCover`, `OccupantOf`·`ReservedBy`·`GetCover`, `BlockedRule`, `NavSampleSucceeds`·`NavPointRule`(null이면 around 그대로), 호출 수·`LastCoverQuery` 기록.
- **건드리지 않은 것**: `ProjectSettings/URPProjectSettings.asset`이 작업 전부터 수정돼 있었다(내 변경 아님, 그대로 둠). CLAUDE.md 그대로. 커밋 안 함(`git mv`로 이동 3쌍만 스테이징된 상태).

---

## 2c 구현 메모

- **결과**: 사본(1단계 + 그 시점 작업 트리)에서 `-executeMethod Ashfall.EditorTools.M3DataSetup.CreateAll` 성공(에셋 10개 생성 + 기존 3개 기록, 오류 없음) → EditMode 290/290 통과(새 `M3DataAssetTests` 21개 포함, 멱등 테스트는 실제로 메뉴를 다시 실행해 13개 파일이 바이트 단위로 같음을 확인). 원본에는 에셋을 만들지 않았다. 사본은 지웠다. 로그: scratchpad `m3s2c_setup.log`, `m3s2c_editmode.log/.xml`.
- **파일**
  - 새 `Assets/_Project/Scripts/EditorTools/M3DataSetup.cs` — 메뉴 `Ashfall/Setup/Create M3 Data` = `CreateData()`, 배치모드 `CreateAll()`(같음). 먼저 `M2DataSetup.CreateData()`(→ M1). 경로 상수 `public const`: `DataFolder`, `WeaponsFolder`, `AiFolder`, `LootFolder`, `RiflePath`, `Ammo556Path`, `Mag556Path`, `PistolPath`, `SmgPath`, `Ammo9mmPath`, `Mag9mm12Path`, `Mag9mm30Path`, `LootScvPath`, `LootRdrPath`, `ScvPath`, `RdrPath`, `AiConfigPath`.
  - 새 `Assets/Tests/EditMode/M3DataAssetTests.cs`.
  - `SetupUtil.cs`: 공용 `IsFieldSerialized(assetPath, fieldName)` 추가만(M2DataSetup의 private 함수와 같은 동작, M2DataSetup은 건드리지 않음).
- **생성 순서**: RIF-01 이전 → PlayerConfig/InventoryConfig 새 필드 기록 → 9mm 탄·탄창·무기 → 루트 테이블 → AiConfig → 아키타입(루트·무장 에셋 참조). 새 에셋은 전부 `CreateAssetIfMissing`(있으면 그대로).
- **설계와 달라진 점·보충**
  1. `PlayerConfig.asset`(`noisePulseIntervalSec`), `InventoryConfig.asset`(`corpseGridSize`)은 **파일에 필드가 없을 때만** SetDirty → 저장(값은 초기값 그대로 0.5 / (6,5)). M2 메모 5처럼 로드 값과 파일 값을 맞추기 위함. 이미 기록돼 있으면 건드리지 않음 → 멱등.
  2. WPN-RIF-01 이전은 `damage <= 0`이면 45, `shotNoiseRadiusM <= 0`이면 120, 바뀐 것이 있을 때만 SetDirty. AMMO-556 그대로(45).
  3. AMMO-9MM·MAG-9MM-12/30의 희귀도·가격은 설계에 없어 기본값(Scrap, 0) — M1 AMMO-556·MAG-556-30과 같은 처리. 권총·SMG는 설계대로 Common, 가격 0.
  4. 권총 `modes = [Semi]`, SMG `[Auto]`는 §5에 무기별 사격 모드가 없어 TODO(GDD-?) 주석.
- **3단계(원본 실행) 시 생기는 변경**: 새 에셋 10개(+.meta, 폴더 `Data/AI`, `Data/Loot` .meta) — `Data/Weapons/{WPN-PST-01, WPN-SMG-01, AMMO-9MM, MAG-9MM-12, MAG-9MM-30}.asset`, `Data/Loot/{LT-SCV, LT-RDR}.asset`, `Data/AI/{AiConfig, AI-SCV, AI-RDR}.asset`. 기존 파일 3개에 줄 추가: `WPN-RIF-01.asset`(`damage: 45`, `shotNoiseRadiusM: 120`), `PlayerConfig.asset`(`noisePulseIntervalSec: 0.5`), `InventoryConfig.asset`(`corpseGridSize`). 그 밖 기존 에셋 무변경(사본 diff로 확인).
- **주의**
  - 원본에서 3단계 실행 전에는 `M3DataAssetTests`가 "에셋이 없다"로 실패한다(M2DataAssetTests와 같은 방식, 멱등 테스트만 Assume로 Inconclusive). 3단계 `M3SceneSetup.CreateAll`이 맨 처음 `M3DataSetup.CreateData()`를 부르면 된다.
  - 기존 `M2DataAssetTests.ItemIds_AreUniqueAndMatchFileNames`가 새 아이템 5개도 검사한다(파일 이름 = id, 통과).
  - 다른 에이전트가 `EnemyArchetypeSO`/`AiConfigSO`에 필드를 더하면(2b) 이미 만든 에셋에는 초기값으로 보인다. `EnemyArchetypeSO`는 초기값이 없으니 2b가 아키타입 필드를 추가하면 M3DataSetup에 값 입력을 추가해야 한다(그때 2c 요청으로 알려 줄 것).
  - CHANGELOG_GDD는 고치지 않았다(§11 초안이 이 데이터 결정을 모두 포함).

## 2c 요청

- 없음. 1단계 SO 필드로 §4.1~§4.5 값을 모두 표현할 수 있었다.

---

## 2a 구현 메모

- **결과**: 사본(scratchpad `copy_2a`, 원본 Assets·Packages·ProjectSettings·Library 복사)에서 EditMode **289/289** 통과(1단계 269 + 새 20: NoiseEmitTests 11, CorpseLootContainerTests 9), PlayMode **20/20** 통과(M1·M2 기존). 결과·로그: scratchpad `m3s2a_editmode.xml/.log`, `m3s2a_playmode.xml/.log`. 사본은 삭제함.
  - 사본에는 data-designer의 작업 중 파일 `EditorTools/M3DataSetup.cs`, `Tests/EditMode/M3DataAssetTests.cs`가 섞여 있어 **사본에서만 제외**(1단계 상태). AI 폴더는 복사 시점에 1단계 껍데기 그대로였다.
  - 주의(환경): 복사 직후 첫 세 번의 실행에서 기존 `EventBusTests.Publish_WithTwoSubscribers_DoesNotAllocate`가 실패했다. 내 변경을 되돌린 사본에서도 같았고, 진단 결과 `Publish`의 실제 할당은 0바이트(`GC.GetAllocatedBytesForCurrentThread`)인데 `Is.Not.AllocatingGCMemory()`가 **빈 대리자에도** 실패했다 → 코드가 아니라 측정 환경(복사 직후 에디터 백그라운드 작업 추정) 문제. 이후 전체 실행에서는 통과. 4단계·2b의 0할당 테스트도 같은 현상을 만나면 재실행으로 확인할 것.
- **바뀐 파일 (원본)**
  - Weapons: `ProjectileSystem.cs`(통계·마커), `WeaponController.cs`(5인자 Fire + DamageRules, 사격 소음), 새 `WeaponNoise.cs`.
  - Player: `PlayerMotor.cs`(IPlayerTarget 구현·등록, 발소리·점프 NoiseEmitted), `LocomotionModel.cs`(`Posture` 속성), 새 `FootstepNoiseModel.cs`.
  - Inventory: `LootContainer.cs`(`AttachRuntime`, `AttachCorpse`), `PlayerInventoryBehaviour.cs`(EnemyKilled 구독/해지).
  - 테스트 새 파일: `Tests/EditMode/NoiseEmitTests.cs`, `Tests/EditMode/CorpseLootContainerTests.cs`. (`.meta` 없음 — 원본에서 유니티가 만든다)
  - Core·asmdef·TestData·M3TestData 변경 없음.
- **설계와 달라진 점·보충**
  1. `ProjectileSystem` 통계는 필드가 아니라 읽기 전용 속성: `LastStepMs`, `AverageStepMs`(누적 합/횟수), `MaxStepMs`, ★`StepSamples`, `ResetStats()`. FixedUpdate 한 번 = 샘플 1개(탄이 없어도 기록). `ProfilerMarker("Ashfall.Projectiles.Step")`는 스텝 본문을 감싼다. `drawDebugTrails`는 여전히 private 직렬화 필드 — 3단계 씬 메뉴는 `SerializedObject`로 끈다.
  2. 사격 소음은 순수 정적 `WeaponNoise.TryCreateShot(weapon, position, out NoiseEmitted)`(테스트용으로 분리). `WeaponController.Shoot`가 총구 위치에서 발당 1회 발행(펠릿 수 무관). 투사체 시스템이 없어도 소음은 낸다.
  3. `FootstepNoiseModel`: "누적을 간격 값으로 시작" 대신 "멈춘 뒤 첫 이동 틱에 즉시 true, 이후 누적이 간격에 닿을 때마다 true"로 구현(결과 같음: 0, 0.5, 1.0초…). 긴 프레임에서도 한 번만 낸다(밀린 펄스 몰아 내기 없음). 누적 오차 허용 1e-4(LootContainerModel과 같은 방식). `Reset()` 공개, `PlayerMotor.OnDisable`에서 호출.
  4. `PlayerMotor`: `Root = transform.root`(설계는 `transform`. 플레이어가 최상위면 같고, 부모 아래 둬도 AI 루트 비교가 맞게). `EyePosition`은 eye가 비면 발 위치. `Posture`는 `LocomotionModel.Posture`(Stance → TargetPosture 변환, 새 속성). 발소리 반경 = `CurrentNoiseRadius`(점프 반영 전 이동 반경), 위치 = 발. 점프 성공 시 `Jump`(jumpNoise) 즉시. 입력 차단(화면·사망) 중에는 이동 입력이 0이라 발소리 없음. `IPlayerTarget` 등록/해지는 기존 이벤트 구독과 같은 OnEnable/OnDisable(GameManager 있을 때만).
  5. `LootContainer.AttachRuntime`: host null → null. host에 이미 `LootContainer`가 있으면 경고 후 **기존 것을 그대로 반환**(내용물 덮어쓰기 없음). ★`AttachCorpse(in EnemyKilled, InventoryConfigSO)` 정적 도우미 추가 — 시체 null/파괴·config null이면 null, 아니면 `AttachRuntime(corpse, displayName, config.corpseGridSize, items, config.searchTimePerItemSec)`. `PlayerInventoryBehaviour.OnEnemyKilled`는 이것만 부른다(EditMode에서 이벤트 경로 검증용).
  6. ★`PlayerInventoryBehaviour.OnEnemyKilled`는 시체를 붙인 뒤 **상호작용 레이캐스트 캐시(lastHitCollider/lastHitInteractable)를 비운다**. 쏘는 동안 적 콜라이더를 바라보고 있었다면 "상호작용 없음"으로 캐시돼 있어, 비우지 않으면 시선을 옮기기 전까지 F 프롬프트가 안 뜬다(4단계 `CorpseLootFlowTests` 대상 버그).
- **2b·3·4단계 참고**
  - `EnemyAgent`는 시체 루트 오브젝트(콜라이더 가진 최상위)를 `EnemyKilled.Corpse`로 넘기면 된다. `LootContainer`는 그 오브젝트에 붙고, 상호작용은 `GetComponentInParent<IInteractable>()`라 자식 콜라이더에 맞아도 잡힌다.
  - `IPlayerTarget`은 `PlayerMotor`가 OnEnable에서 등록 → AI는 Start 이후 `TryGet`(M2 소비자 관례).
  - 사격 소음은 플레이어 `WeaponController`만 낸다(적 `EnemyWeapon`은 내지 않음, 설계 §1.4).

---

## 2b 구현 메모

- **결과**: 사본(1단계 상태 + 2b 파일)에서 배치모드 EditMode 399/399 통과(기존 269 + 새 130). 최종 실행 종료 코드 0, AI 코드 경고 없음. Core·다른 기능 파일은 건드리지 않았다(Core 추가도 없음). 커밋 안 함, 새 `.meta` 없음.
- **파일** (`Scripts/AI/`): 1단계 껍데기에 이어 `Brain/`(EnemyBrain, Squad, IEnemyTactics, TacticsContext, ScavengerTactics, RaiderTactics), `Sensing/`(SightCone, AwarenessModel, PhysicsLineOfSight), `Combat/`(EnemyBodyModel, EnemyHitResolver, AimError, EnemyWeapon, CorpseLootBuilder), `Cover/`(CoverIndex, CoverSelector, CoverPoint), `Navigation/`(PatrolRoute, EnemyWorld), `Runtime/`(AiTickScheduler, TickStats, AiDirector, EnemyAgent, EnemyDebugView). `AiConfigSO`에 필드 7개 추가(아래).
  테스트(`Tests/EditMode/`): AiSightTests, AwarenessTests, EnemyBrainTests(T1~T13 + 유형별 전술), SquadTests, CoverSelectorTests, EnemyBodyTests, AimErrorTests, EnemyWeaponTests, CorpseLootBuilderTests, AiSchedulerTests, AiAllocationTests + 도우미 `AiTestHelpers.cs`(감각 빌더 `AiTest.NoTarget/Unseen/Seen`, `AiTest.AssertNoAlloc`, 엄폐 결과 대본 `ScriptedCoverWorld`).
- **설계와 달라진 점·보충**
  1. `AiConfigSO` 추가(전부 초기값, TODO(GDD-?)): `pointArriveDistanceM 1.0`(순찰·조사·수색 지점 도착), `movingSpeedThresholdMps 0.5`(§1.4 이동 판정), `strafeDistanceM (2,4)`, `navSampleRadiusM 3`, `formationCatchUpDistanceM 6`(분대원이 이보다 멀면 Run), `searchLookYawDeg 90`, `coverRetrySec 1`(엄폐 None 뒤 재질의 간격).
  2. **T9 유예 확장**: 설계 값 `loseSightSec 5 < flankAfterNoSightSec 6`이라 그대로면 RDR은 우회 전에 늘 Search로 간다. RDR이 엄폐(MovingToCover·Hide·Peek)에 있고 "마지막으로 본 뒤 우회 시도가 아직 끝나지 않았으면" T9를 미룬다. 우회 도착·실패로 끝나면 유예 종료, 표적을 다시 보면 초기화. 우회권을 다른 분대원이 쥐고 있으면 계속 견제(유예 유지).
  3. 소리·피격·분대 연락은 받는 즉시 상태를 바꾸지 않고 다음 `Tick`에서 처리(전이는 Tick 안에서 하나만). 설계 §6-5의 게이지 갱신(AddSight/Decay)도 brain이 `Tick` 안에서 한다(EnemyAgent는 감각만 채움).
  4. `IEnemyTactics`에 `DefersLoseSight`·`WantsHiddenCheck` 속성 추가. `EnemyBrain.WantsHiddenCheck`가 true일 때만 EnemyAgent가 표적 눈→내 눈 레이를 쏜다.
  5. `EnemyIntent.Reload` 의미 = "지금 재장전해도 좋다(숨음)". 실제 재장전은 EnemyAgent가 `Weapon.AmmoRatio < tacticalReloadBelow`일 때만.
  6. `AiTickScheduler`: 위상 = 황금비 수열(`PhaseOffset`, 등록 수를 몰라도 고르게). `Advance(slot, period, now)` 추가 — 다음 기한 = 이번 기한 + 주기(위상 유지). `now + period`로 다시 잡으면 프레임 양자화로 위상이 흘러 한 프레임에 7명까지 뭉쳤다(테스트로 확인). director는 `Advance`를 쓴다.
  7. `EnemyWeapon` 추가: `Mode`, `HasLoadedRounds`, `AmmoRatio`, `static PickLoadout(loadouts, forcedIndex, random)`. 반자동은 한 프레임만 당기고 RPM 간격으로 안 나갔으면 다음 프레임에 놓았다 다시 당긴다. `CollectLoot`는 회수된 빈 예비 탄창도 넣는다(실제 탄 수).
  8. `EnemyBrain` 공개 추가: `Position`, `Forward`, `StimulusPosition`, `Squad`, `HasRoute`, `IsEngaged`, `WantsHiddenCheck`. 생성자에서 `squad.Join(this)`. `OnDied`가 엄폐·우회권 반납 + 분대 탈퇴.
  9. `AiDirector`: CoverPoint 수집을 Start가 아니라 **Awake**에서(적의 Start 등록보다 먼저). 정적 Instance 없음. 등록은 `Register(agent)` → `agent.AssignRegistration(id, slot)`(internal). internal `World`, `LineOfSight`, `SquadFor`, `TryConsumePathRequest`. `NoiseEmitted` 구독은 OnEnable(+Start에서 한 번 더 시도).
  10. 사격 조준점·사격 중 회전은 감각 주기의 `Intent.FireAt` 대신 **살아 있는 `IPlayerTarget.CenterPosition`**(최대 0.1초 묵은 값 방지). 리드 없음은 그대로. 회전은 항상 직접(`updateRotation=false`).
  11. 엄폐 점수의 "3. 거리 선호 가점"은 별도 가점 없이 `|위협 거리 − 선호 중앙| × coverRangeWeight` 항으로만 반영. 측면 질의에서 표적과 FlankFrom이 같으면 실패. RDR이 우회 질의로 자기 현재 지점을 받으면 실패로 본다.
  12. SCV Combat: 선호 거리 밖이면 Run이고 그동안 쏘지 않는다. 분대원은 Idle에서도 대형 자리로 간다(리더가 아직 Tick 안 했으면 대기).
  13. 시체: `GameManager.Instance`가 없으면 `EnemyKilled`를 발행하지 않는다(EditMode 등). 무기가 아직 안 만들어졌으면(서비스 없음) 사망 때 발사 서비스 없이 만들어 루트에 넣는다.
- **0할당 측정 주의 (중요)**: Unity `Is.Not.AllocatingGCMemory()`(Recorder "GC.Alloc")는 전체 스위트에서 가끔 측정 바깥 샘플을 센다. 진단: 빈 대리자도 첫 측정에서 4로 나오고 이어지는 측정은 0. 단독 실행은 매번 통과, 전체 실행은 매번 다른 할당 테스트가 실패. `GC.GetAllocatedBytesForCurrentThread`는 Unity Mono에서 항상 0이라 대안이 안 된다. 그래서 `AiTest.AssertNoAlloc`은 워밍업 뒤 최대 6번 재고(시도 사이 50·100·… ms) 한 번이라도 0이면 통과한다. 매 호출 할당은 모든 시도에서 잡힌다(`Helper_RejectsAllocatingDelegate`로 확인). 이 방식 도입 뒤 2b 테스트 130개는 모든 전체 실행에서 통과했다.
  **기존 테스트도 같은 잡음으로 가끔 실패한다**: `EventBusTests.Publish_WithTwoSubscribers_DoesNotAllocate`(1단계), `PlayerInventoryTests.TotalWeight_DoesNotAllocate`(M2). 2b 테스트를 뺀 사본에서도 2번 중 1번 실패(재현). 내 소유가 아니라 고치지 않았다. **4단계(test-engineer)는 이 두 테스트를 `AiTest.AssertNoAlloc` 같은 재시도 방식으로 바꾸는 것을 권장**(원본 3·4단계 실행에서 우연히 걸릴 수 있음).
- **3단계 씬 생성에 필요한 것**
  - 직렬화 필드 이름(SerializedObject용): `AiDirector.config`; `EnemyAgent.archetype, director, patrolRoute, squadId, loadoutIndex, seed, eye, muzzle, bodyVisual`; `EnemyDebugView.indicator, stateColors`; `CoverPoint.height, peekLocalOffset`; `PatrolRoute.waypoints`(비우면 자식 순서).
  - EnemyAgent는 Awake에서 Rigidbody를 kinematic·중력 끔, `NavMeshAgent.updateRotation=false`, `speed`를 직접 설정한다. NavMeshAgent 반지름·높이·가속·stoppingDistance·회피 품질·avoidancePriority는 씬 생성에서(§1.10).
  - `bodyVisual`은 캡슐 중심에 피벗(예: 로컬 (0,0.9,0), 프리미티브 캡슐 스케일 (0.7,0.9,0.7)). 웅크림은 그 y 스케일·y 위치를 높이 비율만큼 줄이고, 사망 시 X축 90° 눕혀 y = 캡슐 반지름에 둔다. Eye·Muzzle은 웅크림 때 (crouchEye − standEye)만큼 내려간다.
  - CoverPoint는 씬에 처음부터 있어야 한다(AiDirector Awake에서 한 번 수집). 적 루트는 최상위, 시작 위치가 NavMesh 위여야 한다(아니면 이동을 조용히 건너뜀).
  - AiDirector가 없으면 EnemyAgent가 LogError 후 꺼진다. 서비스(`IPlayerTarget`, `IProjectileService`)는 `targetResolveRetrySec`마다 다시 찾는다. 무기는 `IProjectileService`가 잡힌 뒤 첫 프레임에 생긴다.
  - 디버그 로그: `ASHFALL_DEBUG_AI` 정의 시 상태 전이 로그.

---

## 3단계 구현 메모

- **결과 (원본 저장소)**: 통합 컴파일 오류 없음(1단계+2a+2b+2c를 원본에서 처음 함께 컴파일, 고친 코드 없음 — 첫 EditMode 실행 421/440, 실패 18은 모두 M3DataAssetTests "에셋 없음", 1 Inconclusive = 멱등 테스트 Assume). `-executeMethod Ashfall.EditorTools.M3SceneSetup.CreateAll` 3회 실행(모두 오류 없음) 후 **EditMode 440/440**(연속 3회), **PlayMode 22/22**(기존 20 + 새 2). 로그: scratchpad `m3s3_*`(setup1~3, editmode1~7, playmode1~2, guids_run1, md5_run1).
- **NavMesh 베이크**: 설계 §1.10 코드 그대로 동작. `EditorUtility.CopySerialized`가 NavMeshData에 동작한다 — 재실행 후 `NavMesh-Environment.asset` 두 개가 **바이트 단위로 같고**, 씬 2개·NavMesh 에셋 2개·폴더 2개의 `.meta` GUID 불변(확인 파일 `m3s3_guids_run1.txt`). 씬 `.unity`는 재생성 때 오브젝트 fileID가 새로 잡혀 내용이 바뀐다(M2와 같음, GUID는 유지). sourceBounds: M3_AI 100×3.6×100, M3_AIPerf 150×3.6×150. 굽기 직후 원점 `NavMesh.SamplePosition`으로 검사(실패 시 LogError 후 중단).
- **바뀐 파일**
  - 새 `EditorTools/M3SceneSetup.cs` — 메뉴 `Ashfall/Setup/Create M3 Test Scenes`, 배치 `CreateAll`(두 씬). 맨 앞에서 `M3DataSetup.CreateData()` + `M2SceneSetup.CreatePanelSettings()` + 적 몸 재질 `Art/Materials/M3_EnemySCV.mat`(갈색)·`M3_EnemyRDR.mat`(짙은 회색, 없을 때만).
  - `EditorTools/M2SceneSetup.cs` — `CreatePanelSettings`, `CreatePlayer`, `CreateBlock`, `CreateGameUI`를 `internal static`으로, `CreateBlock`·`CreateGameUI`는 만든 GameObject를 반환(호출 결과·M2 씬 불변, M2SceneSmokeTests 통과).
  - 새 `Tests/PlayMode/M3SceneSmokeTests.cs` — 두 씬 각각: 로드 → 적 수(7/20)·최상위 → 3초 → GameManager·`IPlayerTarget`·`IProjectileService` 등록, AiDirector 1개·켜짐·등록 수·Covers·Target/Projectiles 잡힘, 플레이어 위치 NavMesh 샘플, 모든 적 Brain·Weapon 있음·`isOnNavMesh` → 순찰 분대 **리더**(Squad.LeaderId)가 3m 이상 이동할 때까지 대기(최대 20초) → 이동 후에도 `isOnNavMesh`. 실측: 리더 이동까지 3초 뒤 추가 약 5초(Idle 2초 + 첫 웨이포인트 대기 2~4초).
  - `Tests/EditMode/EventBusTests.cs`(`Publish_WithTwoSubscribers_DoesNotAllocate`), `Tests/EditMode/PlayerInventoryTests.cs`(`TotalWeight_DoesNotAllocate`) — `AiTest.AssertNoAlloc`(재시도) 사용. 첫 원본 실행 중 한 번 EventBus 테스트가 잡음으로 실패해서 바꿨다. EventBus 테스트의 횟수 검사는 재시도 횟수에 따라 달라지므로 `a ≥ 200, a == b, a % 100 == 0`으로.
  - 생성물: `Scenes/Test/M3_AI.unity`, `M3_AIPerf.unity`, `Scenes/Test/M3_AI/NavMesh-Environment.asset`, `Scenes/Test/M3_AIPerf/NavMesh-Environment.asset`, 재질 2개, 2c 메모의 데이터 에셋 10개 + 기존 3개 기록. 빌드 설정 변경 없음.
- **설계와 달라진 점·보충**
  1. **M3_AIPerf 바닥 150m**(설계 140m): 오프셋 ±35에서 RDR(로컬 z 35)이 z 70 = 바닥 끝에 놓인다. 구조물·경로 배치는 그대로, 바닥만 5m씩 넓혔다(TODO(GDD-?) 주석).
  2. M3_AIPerf: 4사분면 모두 경로 A 모양(오프셋만 다름), SCV 분대 id 1~4(무장 0,0,1), RDR 분대 id 5~8(무장 0). 플레이어 (0, 0.05, -70) +z — 가장 가까운 적(Q2 분대원) 약 28m. `drawDebugTrails = false`는 SerializedObject로.
  3. 엄폐 지점 거리 0.7m는 **구조물 면 기준**(중심 기준이면 면에서 0.5m = Humanoid 굽기 반지름 경계). 구조물은 스케일 없는 루트(`LowWall_01` 등) + 스케일한 `Block` 자식 + `Cover_*` 자식(스케일한 오브젝트 아래 두면 peekLocalOffset도 늘어남). High 엄폐 peek는 지점마다 하나라 둘 다 로컬 (+1.9,0,0)(월드로는 서로 반대쪽 벽 끝). 차량은 4방향 Low.
  4. 분대 시작 위치: 리더 = 경로 첫 웨이포인트, 첫 구간 방향을 봄. 분대원은 `Squad.FormationSlot`과 같은 자리(로컬 (∓2, -2), formationSpacingM 초기값 2)라 시작하자마자 대형을 맞추러 뛰지 않는다. RDR은 (±3, 35) yaw 180.
  5. 적 트리에서 `Gun` 큐브는 `Muzzle`의 자식(웅크림 때 총구와 함께 내려감). Indicator 구 (0, 2.15, 0) 스케일 0.25, 기본 재질(EnemyDebugView가 PropertyBlock으로 색). NavMeshAgent `baseOffset 0`(피벗 = 발), `avoidancePriority = 40 + (순번×7) mod 21`, `speed`는 patrolSpeed(실행 중 EnemyAgent가 덮어씀).
  6. NavMesh는 기본 Humanoid 에이전트 설정(반지름 0.5)으로 굽는다. 적 NavMeshAgent 반지름은 0.35.
- **4단계 참고**
  - 씬 오브젝트 이름: `Environment`(NavMeshSurface), `ProjectileSystem`, `AiDirector`, `Player`, `GameUI`, `PatrolRoute_A/B`(perf `PatrolRoute_Q1~Q4`), 적 `Enemy_A1~A3`, `Enemy_B1~B2`, `Enemy_C1~C2`(perf `Enemy_Q{n}S1~3`, `Enemy_Q{n}R1~2`). seed = 생성 순번 + 1.
  - 리더는 첫 웨이포인트에 서 있어 순찰 첫 이동이 Idle 2초 + 대기 2~4초 뒤다. 순찰 이동을 기다리는 테스트는 8초 이상 여유를 둘 것.
  - 0할당 측정 잡음은 원본에서도 재현됐다(EditMode 4회 중 1회 EventBus 테스트 실패 → 재시도 방식으로 바꾼 뒤 3회 연속 통과).

---

## 4단계 구현 메모

- **결과 (원본 저장소)**: EditMode **440/440**(종료 코드 0), PlayMode **39/39**(기존 22 + 새 17) 3회 연속 통과(종료 코드 0, 마지막 2회는 보고 문구만 다른 같은 코드), Windows 빌드 `BuildTools.BuildWindows64` 종료 코드 0(Succeeded, 오류 0, 경고 3 = 알려진 "will not be compiled"). 로그·결과: scratchpad `m3s4_editmode.*`, `m3s4_playmode1~3.*`, `m3s4_pm_new1~5.*`(개발 중 부분 실행), `m3s4_build.log`.
- **새 테스트 파일** (`Assets/Tests/PlayMode/`, .meta는 유니티가 생성)
  - `M3TestSupport.cs`: `M3Scene`(씬 로드 + 모든 적 Brain·Weapon·director 서비스 대기, `KeepOnly`(나머지 적 파괴 + `Squad.Leave`), `WarpEnemy`(Warp + ResetPath + `Physics.SyncTransforms`), `TeleportPlayer`, `SetPitch`/`AimAt`(공개 `AddRecoil`로 시선 상하), `IsBlocked`, `RecordStates`, `SwapTargetTo`, `StopAi`, 메시지 있는 `WaitUntil`), `TestTarget`(무적 `IPlayerTarget`+`IDamageable`, 자세 전환), `SimMouse`(LMB/RMB), `SimInput`(키보드+마우스, 역순 해제).
  - `AiSenseFlowTests`(8), `AiCombatFlowTests`(2), `EnemyDamageFlowTests`(4), `CorpseLootFlowTests`(1), `AiPerformanceTests`(1).
- **제품 코드 수정 (1건)**: `Inventory/PlayerInventoryBehaviour.cs` — F 상호작용 레이캐스트가 **플레이어 자기 캡슐**을 먼저 맞혀 발밑 시체를 열 수 없었다(눈 1.68m > PhysX 캐릭터 캡슐 꼭대기 1.62m(컨트롤러 0.8배), 시선 약 35° 이상 아래를 보면 자기 몸이 첫 충돌. 진단: "시선 레이 첫 충돌 Player 0.08m"). `Physics.Raycast` → `RaycastNonAlloc`(고정 버퍼 8) + 자기 루트 건너뛴 가장 가까운 충돌(`TryRaycastIgnoringSelf`). 벽이 먼저 맞으면 막히는 동작은 그대로, 할당 없음. M2 테스트 포함 전체 통과.
- **테스트 작성 중 알게 된 것 (테스트 쪽 대응)**
  1. `Physics.autoSyncTransforms`가 꺼져 있어 `NavMeshAgent.Warp` 직후 같은 프레임의 투사체 레이는 옛 콜라이더 위치를 본다 → `WarpEnemy`에서 `Physics.SyncTransforms()`.
  2. 분대 리더는 Start 순서(첫 등록자)라 이름(A1)으로 고정되지 않는다 → 테스트는 `Squad.LeaderId`로 고르고, 적을 파괴할 때 `Squad.Leave`로 분대에서 뺀다(안 빼면 남은 적이 죽은 리더 대형을 따라 걸어감).
  3. 1초 질주는 적이 소리 쪽으로 돌아 플레이어를 보고 바로 Alert가 된다 → 0.25초만 달리고 시야 밖으로 치움(첫 발소리 펄스는 이동 첫 틱).
  4. `TestTarget`을 파괴하면 적 `EnemyAgent.currentTarget`(감각 때 캐시)이 파괴된 객체를 가리켜 다음 프레임 `ApplyRotation`에서 MissingReferenceException → 테스트는 파괴 전에 `AiDirector.enabled=false`. 실제 게임에서는 플레이어(IPlayerTarget)가 씬 언로드 때만 파괴돼 문제 없음 — **M4에서 플레이어 GameObject를 레이드 중 파괴/교체하게 되면 `EnemyAgent.Sense/FrameTick`에서 `currentTarget`의 Unity null 확인 필요**(이번에는 수정 안 함).
  5. 성능 씬: 맵 정중앙 표적은 북쪽 RDR 4명에게서 70m(시야 60m 밖) → 표적을 (0,35)에 둠. 아군 사격으로 측정 중 20명 중 4명이 죽어 → 테스트에서만 각 적 `Body`를 HP 1e9 복제 아키타입의 `EnemyBodyModel`로 교체(리플렉션, 에셋 불변).
- **관찰 (설계 판단 필요, 수정 안 함)**: 엄폐에 들어간 RDR이 그 자리에서 표적을 못 보면, 분대원이 Combat 중 0.5초마다 `ReportContact` → `OnSquadContact`로 LKP가 계속 갱신돼 **Alert 타임아웃(T8)이 영원히 안 오고, Alert에는 Peek가 없어 교전에 끼지 못한다**. M3_AIPerf에서 `Enemy_Q4R2`가 매 실행 재현(60초 내내 Alert). 성능 테스트는 "모두 경계 이상 + 18명 이상 Combat 진입"으로 완화하고 미진입 적 이름을 로그에 남긴다. 대안 후보: Alert에서도 엄폐 도착 후 Peek, 또는 분대 연락은 LKP만 갱신하고 `LastKnownUpdatedAt`은 직접 본 정보로만 갱신.
- **성능 (배치모드 에디터, 렌더 없음, 20명 생존·18명 교전 중 10초, 3회)**: AI 틱 평균 0.092ms / 99% 0.24~0.32ms / 최대 0.95~1.6ms, 투사체 스텝 평균 0.022~0.025ms / 최대 0.30~0.64ms(동시 탄 최대 27~33), 프레임 중앙값 0.91~0.94ms, `AiDirector.Update` 직접 호출 120프레임 GC.Alloc 0회. GPU·렌더 비용은 포함되지 않으므로 GTX 1660 1080p 60fps는 사람이 Profiler/개발 빌드로 확인해야 한다.

---

## 리뷰 수정 메모

- **결과 (원본 저장소)**: EditMode **464/464**(기존 440 + 새 24, `AiReviewFixTests`), PlayMode **39/39** 2회 연속(종료 코드 0), Windows 빌드 `BuildTools.BuildWindows64` 종료 코드 0(Succeeded, 오류 0, 경고 3 = 알려진 것). 새 AiConfig 필드를 기존 에셋에 기록하려고 `M3DataSetup.CreateAll`을 1회 실행(AiConfig.asset에 2줄 추가, 그 밖 에셋 무변경). 로그·결과: scratchpad `m3s5_*`(datasetup, editmode1~3, playmode1~3, pm_perf1~3, build).
- **전이 표 해석 변경 (CHANGELOG 반영 필요)**
  1. **T8**: "LKP 갱신 후 `alertTimeoutSec` 동안 못 봄" → "**자신이 직접 감지(봄·총성/발소리 들음·피격)한 마지막 시각 또는 Alert 진입 시각** 뒤 `alertTimeoutSec` 동안 못 봄". 분대 연락(`OnSquadContact`)은 Idle·Patrol·Suspicious·Search에서는 그대로 T4, Alert·Combat에서는 **LKP 위치만** 갱신하고 T8 타이머는 건드리지 않는다. (`TacticsContext.LastSensedAt`, `EnemyBrain.pendingSquad`를 총성·피격 `pendingAlert`와 분리.) Alert에서 표적이 보이는 틱에는 소리·연락으로 LKP를 덮지 않는다(본 위치 우선).
  2. **RDR Alert(§1.6.2 표)**: "LKP를 위협으로 하는 엄폐 선택 → 달리기"에 더해 **엄폐 도착 후 Combat과 같은 Hide(hideSec) ↔ Peek(peekSec) 주기**. Peek 때 Low는 일어서고 High는 `PeekPosition`으로 나가 LKP 쪽을 본다. Alert에서는 사격·측면 우회·숨음 무효화(HiddenFromTarget) 없음, 보호각 무효화만(분대 연락으로 LKP가 옮겨 갈 수 있어서). 보이면 brain이 T7(반응 시간)로 Combat. Peek 중 보이는 동안은 peekSec가 지나도 숨지 않는다. Alert에서 Peek·Hide 중 Combat이 되면 그 단계를 잇는다(Peek이면 Peek 시간을 새로 잡음) — 전에는 MovingToCover로 돌아가 한 번 숨은 뒤에야 내다봤다.
  3. **LKP 높이 (높음 1 재현 원인 보충)**: 총성 위치(총구 ≈ 1.4m)·피격 추정 위치는 **NavMesh 바닥에 붙여** LKP로 쓴다(`EnemyBrain`이 Tick에서 `IEnemyWorld.TrySampleNavPoint(…, navSampleRadiusM)`, 실패하면 그대로). 시야로 얻은 LKP(표적 발)와 기준을 맞춰 `ThreatEye = LKP + standEyeHeightM`이 실제 눈 높이가 되게 한다. 전에는 총성 LKP의 ThreatEye가 약 3m라 엄폐 (b) 사격 가능 검사가 3m 점으로 통과 → Peek 자리에서 실제 표적(1.68m)은 막혀 있었다(M3_AIPerf `Enemy_Q4R2`: High 엄폐 PeekPosition → 표적 눈 막힘을 진단으로 확인).
  4. **엄폐 거리 상한 (높음 1 보충)**: `CoverQuery.MaxThreatDistanceM`(0 = 제한 없음) 추가, RDR은 `min(sightRange, engageMaxRangeM)`(60). 위협까지 수평 거리가 이보다 먼 지점은 후보에서 뺀다. (`coverAdvancePenalty`가 위협에서 멀어지는 지점을 선호해 `Enemy_Q2R2`가 표적에서 65.8m 엄폐를 고르고, Peek해도 시야 60m 밖이라 Alert에 남았다.) TODO(GDD-?): 자세 배율(웅크린 표적 시야 0.6배)은 반영하지 않음.
- **항목별 수정**
  - 높음 2: Hide 단계가 숨는 자리(`cover.Position`)가 아니면 Walk로 돌아간다(Combat·Alert 공통 `HoldHideSpot`). Peek 시간이 끝나는 틱에 바로 돌아가기 시작하고 사격을 멈춘다. `WantsHiddenCheck`는 숨는 자리에 도착한 뒤(`hideSettled`)만 true — PeekPosition에서 돌아오는 중에 보인다고 엄폐를 버리지 않게.
  - 중간 3: `TryAcquireCover`가 `CoverResult`를 돌려준다. Deferred = 제자리 Hold + 위협 쪽을 봄(Combat에서 보이면 서서 사격), 약탈자 행동은 None(또는 coverRetrySec 대기 중)에서만. → Deferred 때 fallback의 `MoveTo`로 `SetDestination`이 repathMinIntervalSec 동안 묶이던 문제 없음.
  - 중간 4: 엄폐 무효화(숨은 자리에서 보임·보호각 밖) 시 그 지점 id를 `coverInvalidatedExcludeSec`(5초, ★AiConfig, TODO(GDD-?)) 동안 `CoverQuery.HasExcludedCover/ExcludedCoverId`로 후보에서 뺀다(CoverSelector·FakeEnemyWorld 모두). ThreatEye를 현재 표적 눈으로 바꾸는 대안은 쓰지 않았다(숨은 동안엔 표적이 안 보여 의미 없음).
  - 중간 5: `EnemyBrain.Detach()`(엄폐·우회권 반납, 분대 탈퇴 → 리더 승계, 상태는 그대로, 이후 Tick·On* 무시, `IsDetached`). `EnemyAgent.OnDisable`/`OnDestroy` → `DetachFromAi`(살아 있으면 Detach + director 등록 해제, 한 번만). 사망 경로(Die)는 그대로. TODO(GDD-?): 비활성화 후 재활성화(재등록)는 지원하지 않는다.
  - 중간 6: Idle 분대원은 **리더에게 경로가 있을 때만** 대형 자리로 간다(경계 분대 RDR C·Q*R는 배치 자리 유지). Patrol도 같은 조건(리더 경로 없으면 자기 경로).
  - 중간 7: `EnemyWeapon.IsValidLoadout(loadout, out problem)` — 무기·탄·탄창 모두 있고 `ammo.type == weapon.ammo`, `magazine.ammo == weapon.ammo`. 생성자는 어긋나면 ArgumentException, `EnemyAgent.Start`는 경고 + 비무장. 새 `Runtime/AiTickGuard.cs`(`IAiFrameTickable`, `AiTickGuard.FrameTickAll/Fault`): director의 감각 루프와 FrameTick 루프를 적별 try/catch로 감싸 `Debug.LogException` 후 그 적만 `OnTickFaulted`(EnemyAgent: `enabled=false` → Detach·등록 해제). 예외 없을 때 할당 없음(테스트). EditMode 테스트가 쓰도록 public(InternalsVisibleTo 없음).
  - 낮음 8: `FrameTick` 시작에 `currentTarget`의 Unity null 확인(파괴됐으면 놓고 lastVisible=false).
  - 낮음 9: `AiDirector.ResolveServices`에서 `IProjectileService`를 못 찾으면 null. `EnemyWeapon.Projectiles`(설정 가능 속성)를 EnemyAgent가 매 프레임 `director.Projectiles`로 맞춘다. 서비스가 없으면 방아쇠를 당기지 않는다(탄 소모 없음).
  - 낮음 10: NavMesh 밖이면 첫 `ApplyMovement`에서 1회 `LogWarning`(문자열 할당은 그 1회).
  - 낮음 11: `EnemyAgent.ApplyDamage`에 TODO(GDD-?) 주석만(아군 탄도 그 총구를 LKP로).
  - 낮음 12: `Squad.Join(brain, order)` — order 오름차순(같으면 들어온 순) 삽입, 0번 = 리더. `EnemyBrain` 생성자에 선택 인자 `squadOrder`(기본 0), `EnemyAgent`는 `transform.GetSiblingIndex()`(적 루트는 최상위 → 씬 루트 순서, 씬 생성에서 리더가 먼저 만들어짐). 리더 승계: Patrol에서 직전 틱에 대형을 따르다 리더가 되면 `BeginPatrol()`(현재 위치에서 가장 가까운 웨이포인트).
  - 낮음 14: `EnemyAgent` 회전 속도 문턱 0.04f → `movingSpeedThresholdMps²`. 바라볼 거리 10f(EnemyBrain 대형 정렬·둘러보기, TacticsContext.ThreatEye) → ★`AiConfigSO.lookAheadDistanceM`(10, TODO(GDD-?)).
  - 낮음 15: 사망 시 `CorpseLootBuilder.Build` 뒤 `Weapon = null`.
  - 13·16·17은 그대로(지시).
  - AiConfigSO 새 필드 2개는 `M3DataSetup.CreateAiConfig`가 파일에 없을 때만 기록(`WriteIfMissing`) → 멱등 테스트 유지. `M3DataAssetTests.AiConfig_MatchesGddAndDesign`에 값·파일 기록 검사 추가.
- **새 테스트** `Tests/EditMode/AiReviewFixTests.cs`(24): R1 Alert 엄폐 + 분대 연락 0.5초마다 → Peek로 일어서 4초 안에 Combat / Alert Hide 웅크림 → Peek 일어섬·LKP 봄·사격 안 함 / High Alert Peek → PeekPosition / 분대 연락만으로 T8 연장 안 됨(LKP는 갱신) / 분대 연락 Alert는 진입 시각부터 T8 / 직접 총성은 T8 연장 / CoverSelector 거리 상한 / RDR 질의 상한 = min(시야, 사거리) / 총성 LKP 바닥 붙임(+NavMesh 실패 시 그대로); R2 High Peek→Hide 위치 = cover.Position(걷기, 도착 전 숨음 판정 안 함); R3 Deferred 제자리(Alert·Combat)·다음 틱 재질의 / None은 약탈자; R4 무효화 지점 제외·만료·CoverSelector 제외; R5 Detach 정리(엄폐·우회권·분대·리더 승계·이후 입력 무시); R6 경로 없는 리더 분대원 제자리 / 경로 있으면 대형; R7 IsValidLoadout(ammo null·탄 불일치·탄창 불일치·무기 없음, 생성자 거부) / 한 적 예외가 다른 적 틱을 막지 않음(LogAssert) / 무예외 0할당; R9 발사 서비스 null이면 안 쏘고 탄 유지; R12 순서 키 리더 / 승계 후 가장 가까운 웨이포인트부터.
  - 기존 테스트 수정: `SquadTests.Follower_WalksToFormationSlot`는 경로 없는 분대로 대형 추종을 검사하고 있어 중간 6과 충돌 → 같은 경로를 가진 순찰 분대로 바꿈(검사 내용 동일).
- **되돌린 테스트 완화 (4단계)**
  - `AiPerformanceTests`: "30초 뒤 모두 경계 이상 + 18명 이상 Combat" 완화 제거 → **20명 모두 Combat 진입(60초 제한)**, 총성 간격 15초 → **2초**(원래 의도: 계속 쏘는 상황). 수정 직후 1차 실행에서는 Q2R2(65.8m 엄폐)·Q4R2(3m ThreatEye)가 남아 실패 → 위 보충 3·4로 해결, 이후 3회 모두 20명 진입(15.5~15.6초). 실패 시 남은 적의 단계·엄폐·사격 눈 LOS를 출력하는 진단(`Diagnose`)을 남겼다.
  - `M3TestSupport.StopAi`(TestTarget 삭제 전 AI 정지) 제거 — 낮음 8로 불필요. `AiCombatFlowTests` 우회 테스트 끝에 **AI가 돌아가는 채로 TestTarget을 파괴**하고 3초 안에 분대 전원 Idle(T12)·우회권 반납을 확인하는 단계를 추가(MissingReferenceException이 나면 로그 오류로 실패).
  - `M3TestSupport.KeepOnly`의 수동 `Squad.Leave` 제거 — 중간 5로 불필요. 대신 파괴된 적이 Detach·분대 탈퇴·엄폐 예약 해제됐고 남은 적의 분대 인원이 맞는지 검사한다(PlayMode에서 중간 5 확인).
  - 그대로 둔 것: `CorpseLootFlowTests`·`EnemyDamageFlowTests`의 `Director.enabled=false`(조준 중 적이 움직이지 않게 하는 시험 조건, 높음 1과 무관), 성능 테스트의 적 HP 1e9 복제(아군 사격으로 적 수가 줄지 않게, 무관).
- **성능 (배치모드 에디터, 렌더 없음, 20명 모두 Combat 중 10초, 3회)**: AI 틱 평균 0.091~0.097ms / 99% 0.259~0.279ms / 최대 0.93~0.97ms, 투사체 스텝 평균 0.022~0.024ms / 최대 0.12~0.29ms(동시 탄 최대 26~27), 프레임 중앙값 0.93~0.97ms, `AiDirector.Update` 120프레임 GC.Alloc 0회. Combat 평균 20.0명(최소 20) — 4단계(18명 교전)보다 조건이 엄격해졌지만 수치는 같은 수준.
- **사람 확인 항목 추가**: M3_AI 씬에서 RDR이 Alert 상태로 엄폐에 들어간 뒤 일어서거나(Low) 벽 끝으로 나와(High) LKP 쪽을 보는지, High 엄폐에서 Peek 뒤 숨는 자리로 걸어 돌아오는지(머리 위 표시 색: Alert/Combat).
