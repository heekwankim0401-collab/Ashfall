# GDD — 프로젝트 "ASHFALL" (가제)

> 이 문서는 Claude Code가 구현 기준으로 읽는 단일 진실 소스(Single Source of Truth)다.
> 모든 구현은 이 문서의 ID(예: `SYS-HP-01`)를 커밋 메시지·주석에 참조한다.

---

## 0. Claude Code 작업 규칙

- 구현 전 해당 섹션의 **수용 기준(AC)** 을 먼저 읽고, 완료 시 AC 체크리스트로 자가 검증한다.
- 수치는 코드에 하드코딩 금지. 전부 `ScriptableObject`(§14)로 뺀다.
- 문서와 코드가 충돌하면 문서가 우선. 문서 수정이 필요하면 `docs/CHANGELOG_GDD.md`에 사유 기록 후 수정.
- 한 번에 한 마일스톤(§15)만 작업. 다음 마일스톤 기능을 미리 구현하지 않는다.
- 모호한 부분은 `// TODO(GDD-?)` 주석으로 남기고 가장 단순한 구현을 택한다.
- 용어는 §18 용어집을 따른다. 코드 식별자는 영어, 주석·문서는 한국어.

---

## 1. 개요

| 항목 | 내용 |
|---|---|
| 장르 | 1인칭 싱글플레이 하드코어 익스트랙션 슈터 |
| 테마 | 포스트 아포칼립스 — 핵겨울 이후, 재가 내리는 붉은 하늘 |
| 메인 컬러 | 검붉은색 (§10 팔레트) |
| 공간 | 야외 전용 (실내 진입 없음, 폐허는 외곽 구조물로만 표현) |
| 엔진 | Unity 6 LTS, URP |
| 플랫폼 | Windows 10/11 64-bit 데스크탑 |
| 입력 | 키보드+마우스 (게임패드 미지원) |
| 세션 길이 | 1레이드 15~25분 |

**한 줄 요약:** 재로 덮인 붉은 황무지에 들어가 물자를 챙기고, 죽으면 전부 잃는다. 살아서 탈출 지점에 도달해야만 가져온 것이 내 것이 된다.

**디자인 기둥 (우선순위 순)**
1. **손실의 긴장감** — 사망 = 장착·소지품 전부 소실.
2. **정보의 희소성** — HUD 최소화, 탄수·체력은 직접 확인해야 안다.
3. **소리가 곧 정보** — 발소리·총성으로 위치를 판단한다.

---

## 2. 코어 루프

```
[은신처(Hideout)] → 장비 선택/반입 → [레이드(Raid)] → 탐색·루팅·전투
      ↑                                                    │
      │                                     ┌──────────────┴──────────────┐
      │                                  탈출 성공                       사망/시간초과
      │                                     │                              │
      └──── 루트 보관·판매·제작 ←───────────┘          반입 장비 전부 소실 ┘
```

---

## 3. 하드코어 규칙 (`RULE-*`)

| ID | 규칙 |
|---|---|
| RULE-01 | 사망 시 레이드에 가져간 모든 아이템 소실. 보험 없음. |
| RULE-02 | 레이드 타이머 25분. 초과 시 MIA 처리(=사망). 남은 시간(분:초)은 HUD 위 가운데에 상시 표시(2026-10-28 B안). |
| RULE-03 | 세이브는 은신처에서만. 레이드 중 강제 종료 = 사망 처리. |
| RULE-04 | 무기를 들면 HUD에 탄창의 탄 수/용량·예비 탄창 수·내구도 등급 표시(2026-10-28 B안). `T` 키 탄창 확인 애니메이션(0.8초) 후 대략치("가득/절반/거의 없음") 표시는 유지. |
| RULE-05 | HUD에 부위별 현재/최대 HP·상태이상·스태미나·수분·에너지·방사능 표시(2026-10-28 B안). `H` 키 부위 상태 화면(등급만)은 유지. |
| RULE-06 | 미니맵 없음. 종이 지도 아이템(`M`)으로만 확인, 내 위치 표시 없음. |
| RULE-07 | 크로스헤어 없음. 조준(ADS)만 정확. |

---

## 4. 플레이어 (`SYS-PLR-*`)

### 4.1 이동
| 상태 | 속도(m/s) | 소음 반경(m) | 비고 |
|---|---|---|---|
| 걷기 | 3.0 | 8 | 기본 |
| 질주 | 5.5 | 20 | 스태미나 소모 15/s |
| 웅크리기 | 1.6 | 3 | `C` 토글 |
| 엎드리기 | 0.8 | 1 | `Z` 토글 |
| 기울이기 | - | - | `Q`/`E`, 15° |
| 점프 | 높이 1.0m | 15 | 스태미나 20 |

- 무게 패널티: 총 무게 > 25kg부터 속도 -2%/kg, 40kg 초과 시 질주 불가.
- 스태미나 최대 100, 재생 10/s (정지 시 20/s), 0이 되면 3초 질주 잠금.

### 4.2 부위별 체력 (`SYS-HP-01`)
| 부위 | HP | 0이 되면 |
|---|---|---|
| 머리 | 35 | 즉사 |
| 흉부 | 80 | 즉사 |
| 복부 | 70 | 이후 피해 1.5배, 탈수 가속 |
| 팔(좌/우) | 60 | 조준 흔들림 +100% |
| 다리(좌/우) | 65 | 질주 불가, 이동 -40% |

- 파괴된 부위가 추가 피해를 받으면 피해의 70%를 흉부·복부로 분산.

### 4.3 상태이상 (`SYS-STAT-*`)
| ID | 상태 | 효과 | 해제 |
|---|---|---|---|
| STAT-BLEED | 출혈 | 해당 부위 -1 HP/s | 붕대 |
| STAT-HBLEED | 중출혈 | -3 HP/s | 지혈대 |
| STAT-FRAC | 골절 | 이동/조준 패널티 | 부목 |
| STAT-RAD | 방사능 | 누적 100 도달 시 최대 HP -20% | 해독제 |
| STAT-DEHY | 탈수 | 수분 0 시 전신 -0.5 HP/s | 물 |
| STAT-HUNG | 허기 | 에너지 0 시 스태미나 재생 -50% | 음식 |

- 수분 100(−0.1/s), 에너지 100(−0.07/s).

---

## 5. 무기 (`SYS-WPN-*`)

### 5.1 공통
- 히트스캔 아님. **탄도 투사체**(중력·초기속도·공기 저항 `AmmoDefinitionSO.dragPerM`). 50m 이내는 성능 위해 레이캐스트+낙차 근사 허용.
- 정조준(ADS): 무기별 가늠자 기준점(뷰모델 `SightAnchor`)이 화면 중앙에 온다(눈 거리 `adsEyeReliefM`). 무기별 영점 거리 `zeroRangeM`에서 탄착 = 조준점(수평 기준). 수치는 SO(CHANGELOG 2026-10-28 AB-Q1~Q4).
- 탄창은 개별 아이템. 탄창마다 탄 수를 실제로 저장.
- 재장전: 빠른 재장전(탄창 바닥에 버림) / 일반 재장전(탄창 인벤토리 회수).
- 고장(Jam): 내구도 < 50%일 때 발당 확률 `(50-내구도)*0.1%`. `T` 길게 눌러 해제.
- 내구도는 숫자 대신 대략 등급(양호 ≥ 50% / 낡음 / 위험 < 25%, 임시)으로만 보인다. 은신처 상인에게 탄피로 수리(100%로, 비용 = basePrice × 0.5 × 잃은 비율, 임시), 매도가는 내구도 비율만큼. 방어구도 같다.

### 5.2 초기 무기 목록 (MVP)
| ID | 이름 | 분류 | 탄 | RPM | 피해 | 탄창 |
|---|---|---|---|---|---|---|
| WPN-PST-01 | 녹슨 권총 | 권총 | 9mm | 400 | 35 | 12 |
| WPN-SMG-01 | 개조 SMG | 기관단총 | 9mm | 800 | 30 | 30 |
| WPN-RIF-01 | 군용 소총 | 돌격소총 | 5.56 | 650 | 45 | 30 |
| WPN-SHT-01 | 펌프 샷건 | 산탄총 | 12ga | 70 | 18×8 | 6 (튜브) |
| WPN-DMR-01 | 볼트액션 | 저격 | 7.62 | 40 | 110 | 5 |

### 5.3 방어구
- 방어 등급 1~5. 탄 관통력과 비교해 관통 확률 계산.
  `관통확률 = clamp01(0.5 + (탄관통력 - 방어등급) * 0.25)`
- 비관통 시 피해 30% 둔탁 피해, 방어구 내구도 감소.
- 방어구가 덮는 부위(`covers`)에만 적용. 내구도 0이면 막지 못한다. 비관통 시 내구도 감소 = 원피해 × 0.2(임시). 파괴 부위 분산 피해(§4.2)는 방어구를 거치지 않는다(임시).
- 플레이어와 적이 같은 공식을 쓴다. 적 방어구는 유형별(MVP: 보스 헬멧+방탄복, 호위 방탄복, 임시), 시체 루트에는 넣지 않는다.

---

## 6. 인벤토리 & 루트 (`SYS-INV-*`)

- **그리드 인벤토리**(타르코프식). 아이템마다 W×H 칸, 회전 가능(`R`).
- 장착 슬롯: 주무기×2, 보조무기, 헬멧, 방탄복, 전술조끼, 배낭.
- 저장 공간 = 전술조끼 + 배낭 + 주머니(1×1×4).
- 루트 컨테이너: 상자, 시체, 차량 트렁크. **열람 시간** 존재(아이템 1개당 0.5초 검색 연출).
- 아이템 희귀도 5단계: 고철(회색) / 일반(흰) / 우수(녹) / 희귀(청) / 유물(검붉은 테두리 발광).

---

## 7. 적 AI (`SYS-AI-*`)

| ID | 유형 | 행동 | 무장 |
|---|---|---|---|
| AI-SCV | 약탈자 | 2~3인 순찰, 소리 추적 | 권총/SMG |
| AI-RDR | 레이더 | 엄폐 활용, 측면 우회 | 소총 |
| AI-SNP | 저격수 | 고지대 고정, 레이저 반사 신호 | 볼트액션 |
| AI-MUT | 변이체 | 근접 돌진, 무리 행동 | 없음 |
| AI-BOS | 보스 "재의 군주" | 호위 3명 동반, 고정 거점 | 경기관총 |

- **상태 머신:** `Idle → Patrol → Suspicious → Alert → Combat → Search → Patrol`
- **감각:** 시야(콘 110°, 거리 60m, 웅크림 시 0.6배, 엎드림 0.3배), 청각(§4.1 소음 반경).
- NavMesh 기반. 엄폐 지점은 레벨에 `CoverPoint` 컴포넌트로 사전 배치.
- 적은 플레이어와 동일한 피해 공식·부위 체력 사용(단순화: 머리/몸통/다리 3부위). 방어구(§5.3)도 같은 공식 — 머리 = 헬멧, 몸통 = 방탄복.

---

## 8. 맵 & 익스트랙션 (`SYS-MAP-*`)

### 8.1 MVP 맵: "붉은 계곡 (Crimson Basin)"
- 크기 1km × 1km. Unity Terrain 사용.
- 구역(POI): 붕괴된 고속도로 / 방사능 정유소 / 매몰된 마을 외곽 / 송전탑 언덕 / 말라붙은 저수지.
- 실내 없음. 구조물은 엄폐용 외벽·잔해·차량 무더기(속을 채운 외곽 구조물).
- 랜드마크(멀리서 방향 잡기): 급수탑·기울어진 송전탑·추락한 항공기·녹슨 광고판·끊어진 고가·정유소 플레어 스택·라디오 마스트. POI 사이 길목에 중간 구조물(무너진 담·컨테이너·버스 잔해). 배치는 임시(`MapPolishLayout`).

### 8.2 탈출 지점 (`EXT-*`)
| ID | 이름 | 조건 | 소요 |
|---|---|---|---|
| EXT-01 | 남쪽 도로 | 항상 열림 | 10초 대기 |
| EXT-02 | 열차 신호소 | 전원 스위치 작동 필요 | 15초 |
| EXT-03 | 헬기 착륙장 | 레이드 15분 이후만 | 20초, 신호탄 소모 |
| EXT-04 | 하수 배출구 | 배낭 미착용 시만 | 7초 |

- 레이드 시작 시 스폰 위치 기준 2~3개 탈출구만 활성 표시(지도 아이템에서만 확인 가능).

### 8.3 동적 환경
- **재 폭풍:** 레이드 중 1회 랜덤 발생(3분). 시야 30m로 감소, 방사능 +1/s(방독면으로 무효).
- 낮/밤 없음. 고정된 붉은 황혼.

---

## 9. 은신처 & 메타 (`SYS-HUB-*`)

- 메뉴형 UI(3D 공간 아님, MVP 단계).
- **보관함:** 그리드 10×30에서 시작, 업그레이드로 확장 — 탄피로 10×40(5000) → 10×50(12000)(임시).
- **상인 1명:** 아이템 매매. 화폐 = "탄피(Casings)".
- **제작대:** 고철→탄약/의약품 제작.
- **진행도:** 레벨 없음. 오직 보유 아이템과 은신처 업그레이드가 진행도.
- 신규 플레이어 및 전멸 시 구제: "부랑자 키트"(녹슨 권총+탄창 2개+붕대) 20분 쿨다운 무료 지급.

---

## 10. 아트 디렉션 (`ART-*`)

### 10.1 컬러 팔레트
| 토큰 | HEX | 용도 |
|---|---|---|
| `Ash_Black` | `#0D0807` | 그림자, UI 배경 |
| `Dried_Blood` | `#3A0B0B` | 지형 기본, 안개 원경 |
| `Rust_Red` | `#6E1A14` | 하늘, 구조물 녹 |
| `Ember` | `#B2321C` | 강조, 위험 표시 |
| `Ash_Grey` | `#5A524E` | 재, 콘크리트 |
| `Bone` | `#C9BBA8` | UI 텍스트, 하이라이트 |
| `Toxic` | `#7A8A2A` | 방사능 구역 (유일한 비적색 강조) |

### 10.2 렌더링 (URP)
- 포스트 프로세싱: Color Adjustments(채도 -30, 붉은 틴트), Film Grain 0.3, Vignette 0.35, Bloom(낮음), 볼류메트릭 느낌의 Height Fog(`Dried_Blood`).
- 하늘: 그라데이션 스카이박스 `Ash_Black` → `Rust_Red`, 낮게 걸린 흐린 태양.
- 조명: 지형·적·엄폐물이 보일 만큼 밝게(태양·환경광·노출 — 수치는 `AtmosphereConfigSO`, 임시). POI마다 `Ember` 불·`Bone` 가로등 실시간 점광원 2~3개(그림자 없음, 레이드 전체 상한 16), 신호등은 발광면만.
- 재 파티클: 플레이어 주변 30m 상시 VFX Graph.
- 에셋: MVP는 Unity Asset Store/무료 에셋 + 프로빌더 블록아웃. 모든 머티리얼은 위 팔레트 토큰을 참조하는 공용 `PaletteSO` 사용.

---

## 11. 오디오 (`AUD-*`)

- 3D 공간음 필수. 발소리는 지면 재질(흙/금속/잔해/물)별 분리.
- 총성 거리별 3단계 레이어(근거리/중거리/원거리 메아리).
- 환경음: 바람, 멀리서 무너지는 금속, 가이거 계수기(방사능 구역).
- BGM 없음(은신처 제외). 전투 진입 시에도 음악 없음.

---

## 12. UI / UX (`UI-*`)

- UI Toolkit 사용.
- HUD 요소(2026-10-28 B안, 게임 UI가 있는 씬, 화면이 열리면 숨김). 각 항목은 데이터 원천이 있을 때 표시하고, 레이드 남은 시간(위 가운데)은 레이드 중에만 —
  부위별 체력·상태이상(왼쪽 아래, 상태이상은 걸렸을 때), 스태미나·수분·에너지·방사능 누적(왼쪽 아래, 방사능은 > 0일 때),
  무기 이름·발사 모드·탄 수/용량·예비 탄창·내구도 등급(오른쪽 아래, 무기 들었을 때), 자세·총 무게(오른쪽 아래, §4.1 무게 경계로 경고색), 의약품 퀵슬롯 4~7(아래 가운데).
  일시 — 탈출 카운트다운, 상호작용 프롬프트(`F`), 피격 방향 붉은 가장자리. 크로스헤어·나침반·미니맵은 없음(RULE-06·07).
- 화면: 메인 메뉴 / 은신처 / 인벤토리 / 부위 상태 / 지도 / 일시정지 / 레이드 결과(생존·사망 요약).
- 개발 빌드 전용(릴리스 빌드에는 없음): 일시정지 [귀환(개발)] → 확인 → 소지품을 잃지 않고 은신처로(탈출 성공과 같은 처리). `DevFeatures`, 주석 `DEV-RETURN`.
- 레이드 결과의 사망 사인 = 피격 부위, 또는 전신 사인(출혈·탈수).
- 폰트: 스텐실·타자기 계열, 색 `Bone`.

### 12.1 키 바인딩
| 동작 | 키 |
|---|---|
| 이동 | WASD |
| 질주 | Shift |
| 웅크리기/엎드리기 | C / Z |
| 기울이기 | Q / E |
| 조준 / 사격 | RMB / LMB |
| 재장전 / 탄 확인 | R / T |
| 상호작용 | F |
| 인벤토리 | Tab |
| 부위 상태 | H |
| 지도 | M |
| 무기 교체 | 1 / 2 / 3 |
| 의약품 퀵슬롯 | 4~7 |

---

## 13. 기술 스펙 (`TECH-*`)

### 13.1 환경
- Unity 6 LTS (6000.x 최신 패치), URP, .NET Standard 2.1, IL2CPP 빌드.
- 패키지: Input System, Cinemachine, AI Navigation, UI Toolkit, VFX Graph, Addressables, Unity Test Framework.
- 목표 성능: GTX 1660 / 16GB RAM 기준 1080p 60fps.

### 13.2 폴더 구조
```
Assets/
  _Project/
    Scripts/
      Core/          # GameManager, 이벤트 버스, 세이브
      Player/        # 이동, 카메라, 체력, 상태이상
      Weapons/       # 무기, 탄도, 탄창, 방어구
      Inventory/     # 그리드, 아이템, 컨테이너
      AI/            # 상태머신, 감각, 엄폐
      Raid/          # 레이드 타이머, 탈출, 스폰, 재폭풍
      Hideout/       # 보관함, 상인, 제작
      UI/
      Audio/
      EditorTools/   # 에디터 전용 도구(씬·에셋 생성 메뉴). 빌드 제외
    Data/            # ScriptableObject 에셋(.asset)
    Prefabs/
    Scenes/          # Boot, MainMenu, Hideout, Raid_CrimsonBasin
    Art/
    Audio/
  Tests/
    EditMode/
    PlayMode/
docs/
  GDD.md
  CHANGELOG_GDD.md
```

### 13.3 코드 규칙
- 폴더별 Assembly Definition(`Ashfall.Core`, `Ashfall.Player` …). 의존 방향: 기능 → Core, 기능끼리 직접 참조 금지(이벤트 버스 사용).
- 네임스페이스 = `Ashfall.<폴더명>`.
- `MonoBehaviour`는 얇게, 로직은 순수 C# 클래스로 분리해 EditMode 테스트 가능하게.
- 싱글톤 최소화. `GameManager` 하나만 허용.
- 세이브: JSON(`Application.persistentDataPath/save.json`), 은신처 진입 시에만 기록(RULE-03).
- 강제 종료 처리: 레이드 진입 시 `raid_in_progress=true` 기록 → 다음 실행 시 true면 반입 장비 삭제.

---

## 14. 데이터 스키마 (ScriptableObject)

```csharp
// 모든 SO는 Assets/_Project/Data/ 하위, CreateAssetMenu 경로 "Ashfall/..."
class ItemDefinitionSO   { string id; string displayName; Vector2Int size; float weightKg; Rarity rarity; int basePrice; Sprite icon; }
class WeaponDefinitionSO : ItemDefinitionSO { WeaponClass cls; AmmoType ammo; int rpm; FireMode[] modes; float recoilV; float recoilH; float adsTime; GameObject viewModel; float zeroRangeM; float adsEyeReliefM; }
class AmmoDefinitionSO   : ItemDefinitionSO { AmmoType type; float damage; int penetration; float velocity; int pelletCount; float dragPerM; }
class MagazineDefinitionSO : ItemDefinitionSO { AmmoType ammo; int capacity; }
class ArmorDefinitionSO  : ItemDefinitionSO { int armorClass; float maxDurability; BodyPart[] covers; }
class MedicalDefinitionSO : ItemDefinitionSO { StatusEffect[] cures; float healAmount; float useTime; int uses; }
class EnemyArchetypeSO   { string id; float hp; float sightRange; float sightAngle; LootTableSO loot; WeaponDefinitionSO weapon; }
class LootTableSO        { List<(ItemDefinitionSO item, float weight)> entries; Vector2Int rollCount; }
class PlayerConfigSO     { /* §4 수치 전부 */ }
class RaidConfigSO       { float timeLimitSec = 1500; ExtractionPointConfig[] extractions; }
class PaletteSO          { /* §10.1 토큰 */ }
```

---

## 15. 마일스톤 & 수용 기준

> 각 마일스톤 종료 시 빌드 가능 상태 + 테스트 통과 필수.
> 체크 = 구현·자동 테스트·빌드로 확인(2026-10-24 MVP 최종 정리). 사람 플레이 확인은 미실시 — 목록은 `docs/design/MVP_final.md` §4.

### M0 — 프로젝트 셋업
- [x] §13.2 폴더·asmdef·패키지 구성
- [x] Boot 씬에서 MainMenu 로드
- [x] CI용 빈 EditMode 테스트 1개 통과

### M1 — 이동 & 사격 (그레이박스)
- [x] §4.1 이동 전 상태 동작, 스태미나 작동
- [x] WPN-RIF-01 1종 사격·ADS·반동·재장전
- [x] 탄도 투사체 + 표적 피격
- [x] 200m×200m 테스트 Terrain

### M2 — 체력 & 인벤토리
- [x] §4.2 부위 체력, §4.3 출혈·골절
- [x] 그리드 인벤토리, 아이템 회전, 장착 슬롯
- [x] 루트 컨테이너 검색 연출
- [x] RULE-04, RULE-05 구현

### M3 — 적 AI
- [x] AI-SCV, AI-RDR 상태 머신·시야·청각
- [x] 엄폐 사용, 적 사망 시 시체 루팅
- [ ] 동시 적 20명에서 60fps 유지 — CPU 예산은 자동 통과, GTX 1660 실측 전(이 PC HD 620 1080p 약 31fps, `docs/design/MVP_final.md` §2)

### M4 — 레이드 루프
- [x] Crimson Basin 블록아웃(§8.1)
- [x] 탈출 지점 EXT-01~04, 레이드 타이머
- [x] 사망/MIA/탈출 결과 화면, RULE-01 적용

### M5 — 은신처 & 세이브
- [x] 보관함·상인·부랑자 키트
- [x] 세이브/로드, 강제 종료 처리(§13.3)
- [x] 은신처 ↔ 레이드 전체 루프 1회 완주 가능

### M6 — 아트 & 오디오 패스
- [x] §10 팔레트·포스트프로세싱·재 파티클
- [x] §11 지면별 발소리, 총성 거리 레이어
- [x] 재 폭풍 이벤트

### M7 — 콘텐츠 확장 & 폴리시
- [x] 무기 5종 전체, AI-SNP·AI-MUT·AI-BOS
- [x] 제작대, 방사능·탈수·허기
- [ ] Windows 빌드, 성능 목표 달성 — Windows IL2CPP 빌드 완료, 성능은 GTX 1660 실측 전(위 M3과 같음)

### MVP 마무리 (M7 후속)
- [x] 방어구 관통·둔탁 피해(§5.3), 플레이어·적
- [x] 무기·방어구 내구도 대략 표시, 상인 수리·가격 반영(§5.1)
- [x] 전신 사인 문구(§12)
- [x] 보관함 업그레이드(§9)

---

## 16. Claude Code 에이전트 구성

`.claude/agents/` 에 아래 파일로 생성한다. 메인 세션은 오케스트레이터 역할, 구현은 에이전트에 위임.

| 에이전트 | 파일 | 담당 | 사용 시점 |
|---|---|---|---|
| `unity-architect` | `unity-architect.md` | asmdef 구조, 시스템 간 인터페이스, 이벤트 설계 | 각 마일스톤 시작 시 설계 |
| `gameplay-programmer` | `gameplay-programmer.md` | Player/Weapons/Inventory/Raid 구현 | M1~M5 |
| `ai-programmer` | `ai-programmer.md` | §7 상태 머신, 감각, NavMesh | M3, M7 |
| `data-designer` | `data-designer.md` | §14 SO 정의 및 .asset 수치 입력, 밸런스 표 | 신규 아이템/적 추가 시 |
| `ui-programmer` | `ui-programmer.md` | UI Toolkit 화면(§12) | M2, M4, M5 |
| `test-engineer` | `test-engineer.md` | EditMode/PlayMode 테스트 작성, AC 검증 | 매 마일스톤 종료 시 |
| `code-reviewer` | `code-reviewer.md` | §13.3 규칙 위반, 성능 문제 검토(읽기 전용) | PR/커밋 전 |

### 16.1 에이전트 파일 템플릿
```markdown
---
name: gameplay-programmer
description: Ashfall의 플레이어·무기·인벤토리·레이드 시스템을 Unity C#으로 구현한다. GDD §4~6, §8 작업 시 사용.
tools: Read, Write, Edit, Glob, Grep, Bash
---
너는 Unity 6 URP 게임플레이 프로그래머다.
- 작업 전 docs/GDD.md의 해당 섹션과 §13.3 코드 규칙을 읽는다.
- 수치는 ScriptableObject로만 다룬다.
- 로직은 순수 C# 클래스로 분리하고 EditMode 테스트를 함께 작성한다.
- 완료 시 해당 마일스톤 AC 체크 결과를 보고한다.
```
- `code-reviewer`는 `tools: Read, Glob, Grep`만 허용.
- `test-engineer`는 Unity CLI 배치모드 테스트 실행 권한 필요:
  `Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml`

### 16.2 권장 MCP
- Unity Editor 연동 MCP(씬/프리팹 조작용)가 있으면 연결. 없으면 에디터 작업은 사람이 수행하고 Claude Code는 스크립트·SO·테스트만 담당.

---

## 17. 범위 밖 (하지 말 것)

- 멀티플레이, 네트워크 코드
- 실내 맵, 건물 진입
- 낮/밤 사이클, 날씨 다양화(재 폭풍 제외)
- 게임패드, 콘솔, 맥/리눅스
- 스토리 컷신, 퀘스트 시스템(MVP 이후 검토)
- 캐릭터 레벨·스킬 트리

---

## 18. 용어집

| 용어 | 코드명 | 정의 |
|---|---|---|
| 레이드 | `Raid` | 맵에 진입해 탈출/사망할 때까지의 한 판 |
| 탈출 | `Extraction` | 탈출 지점에서 대기 완료 → 소지품 보존 |
| MIA | `MissingInAction` | 시간 초과로 인한 사망 처리 |
| 반입 | `Loadout` | 레이드에 가져간 장비 전체 |
| 은신처 | `Hideout` | 레이드 사이의 메타 허브 |
| 탄피 | `Casings` | 게임 내 화폐 |
| 재 폭풍 | `AshStorm` | 시야·방사능 동적 이벤트 |
