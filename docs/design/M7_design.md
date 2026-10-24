# M7 "콘텐츠 확장 & 폴리시" 설계 (unity-architect) — 준비안

기준: docs/GDD.md §0, §3(RULE-04·05·07), §4.2·§4.3(STAT-RAD·DEHY·HUNG), §5(무기 5종·고장), §6, §7(AI-SNP·MUT·BOS), §8.1·§8.3, §9(제작대), §11, §12·§12.1, §13, §14, §15 M7, §17.
M1~M6 결정(CHANGELOG_GDD — 특히 M1 "§5.1 미구현: 고장·내구도, 약실 탄", M2 "T 길게 M7·무기 교체 미구현·복부 파괴 탈수 가속 미구현", M3 "무기 교체 M7",
M4 "방사능 정유소 효과 M7", M5 Q16 "권총은 M7 전까지 못 쏨", M6 Q22·Q27 "가이거는 폭풍만, STAT-RAD 누적만"), `M5_design.md`·`M6_design.md`(형식·단계·구현 메모), `M6_review.md`, 현재 코드(46857a7)를 전제로 한다.
**상태: 준비안.** §5 질문은 오케스트레이터가 ★추천안으로 확정한다는 전제로 썼다. 답이 다르면 표시된 곳(⟨Qn⟩)만 바꾼다.
여기 없는 것은 만들지 않는다. 모호하면 `// TODO(GDD-?) Qn` + 가장 단순한 구현. **M7은 MVP 마지막 마일스톤이다** — 끝에 남은 임시값 목록(§10)을 붙인다.

**범위가 커서 하위 단계 4개로 나눈다(★):** 7A 무기 → 7B AI → 7C 생존·제작 → 7D 성능·빌드·폴리시. 각 하위 단계는 자체로 계약 → 구현 → 테스트 통과 → 커밋한다(하위 단계별 커밋, §6).
**외부 에셋 금지(사용자 지시, M6과 같음).** 새 소리·모델은 EditorTools가 만드는 자리표시(합성 WAV, 프리미티브 프리팹)다. 실제 에셋 목록은 §2.3.
**M6 사용자 에디터 확인 전이다** — M6 동작(재 폭풍·오디오·비주얼) 회귀 테스트는 그대로 두고, M6 파일을 고칠 때는 기존 단언을 바꾸지 않는다.

현재 코드에서 M7이 기대는 사실(확인함):
- **무기 데이터:** `WeaponClass`에 `Shotgun`·`Sniper`, `AmmoType`에 `Rifle762`·`Gauge12`가 이미 있다. 무기 정의는 WPN-PST-01·SMG-01·RIF-01, 탄약 AMMO-9MM·556, 탄창 MAG-9MM-12·9MM-30·556-30.
  `AmmoDefinitionSO.pelletCount`가 있고, `WeaponDefinitionSO.damage`(발당·펠릿당, M3 결정)가 탄약 피해보다 우선한다(`DamageRules`).
- **플레이어 무기:** `WeaponController`(Weapons)는 **Primary1만** 따르고, 직렬화된 `weapon`·`ammo` 정의와 **같은 정의일 때만** 든다(다르면 비무장 + "TODO(M7)" 로그). 그래서 키트 권총·주운 SMG를 쏠 수 없다(M5 Q16).
  산탄은 `ammo.pelletCount`만큼 `Fire`를 부르지만 **방향을 한 번만 흩뜨려 모든 펠릿이 같은 방향**으로 간다(M7에서 고칠 것). 뷰모델은 직렬화 Transform 하나.
- **무기 규칙(Core):** `WeaponModel`(발사·재장전, 플레이어·적 공용)은 고장·내구도·약실 탄이 없고, 재장전은 "탄창 통째 교체"뿐이며 재장전 중 `TryFire`는 막힌다. `FireControl` 반자동/자동 + RPM 간격.
  `IMagazineSource`(HasLoadable·TakeFullest·TryStore) 구현체: `PlayerInventory`, `LocalMagazineSource`(Core), 테스트 `NoSpaceSource`(ItemInstanceTests).
- **T 키:** `CheckMagazine` 액션(T) 누름 = RULE-04 탄창 확인(0.8초). `MagazineCheckModel`에 "T 길게(고장 해제)는 M7" TODO. 입력 액션 파일(JSON 텍스트, M4에서 직접 수정한 전례)에 무기 교체 1/2/3 액션이 없다.
- **장착 규칙:** `EquipRules` — Primary1/2 = 권총이 아닌 무기, Secondary = 권총만, Helmet = `ArmorKind.Helmet`. 장착 슬롯은 §6 목록 7개 그대로.
- **방어구:** `ArmorDefinitionSO.armorClass`는 정의·장착·무게만 있고 **어디서도 쓰지 않는다** — §5.3 관통·둔탁 피해·내구도는 어느 마일스톤에서도 구현되지 않았다(⟨Q19⟩).
- **세이브:** `SaveSerializer.CurrentVersion = 1`, JsonUtility 평평한 `ItemRecord`(id·자리·rounds·usesLeft·loadedMagazineId·loadedMagazineRounds). version > 현재 → 지원 안 함(격리), 알 수 없는 id → 그 아이템만 버림.
  "version < 현재 변환은 지금 없다" 주석. `ItemDatabase`는 메뉴가 `Assets/` 전체의 `ItemDefinitionSO`를 id 순으로 매번 다시 모은다(새 정의는 메뉴 재실행만으로 등록).
- **체력:** `BodyHealthModel`의 상태이상은 **부위별 배열**(`StatusEffect` 3개 × 7부위)이고 `BodyStatusPresenter`가 `StatusEffect`의 모든 값을 부위마다 순회한다 → 전신 상태(방사능·탈수·허기)를 `StatusEffect`에 넣으면 7줄 모두에 붙는다.
  `IBodyStatusView`는 "수치 멤버 추가 금지"(RULE-05 구조적 강제). 구현체: `BodyHealthModel`, 테스트 `FakeBodyStatusView`(UiTestFakes).
  최대 HP는 `PlayerConfigSO.MaxHp(part)` 고정값을 매번 읽는다(배율 없음).
- **방사능:** `PlayerHealth.TickRadiation`이 `IAshStormInfo.RadiationPerSec`만 `RadiationDoseModel`(누적만)에 쌓는다. 방독면·구역·효과 없음(Q27 TODO). 가이거(`AudioDirector`/`GeigerClickModel`)는 폭풍 방사능률에만 반응(M6 Q22).
- **의약품 흐름:** 인벤토리 `TryUse` → `MedicalUseRequested` → `PlayerHealth`/`MedicalUseModel`(부위 자동 선택, `FindTarget`이 없으면 NotNeeded) → `MedicalUseCompleted` → Inventory가 횟수 차감. 퀵슬롯 4~7은 의약품만.
- **스태미나:** `StaminaModel`은 `PlayerMotor` 안(재생 배율 없음). `PlayerMotor`는 같은 오브젝트의 `PlayerHealth`를 참조한다(같은 어셈블리). 플레이어 루트에 `PlayerMotor`·`PlayerHealth`·`CharacterController`가 함께 있다(M2SceneSetup).
- **AI:** 전술은 `EnemyBrain` 생성자에서 `archetype.usesCover ? RaiderTactics : ScavengerTactics` 한 줄로 고른다. 무장 없는 유형은 `loadouts`가 비면 `EnemyAgent.Weapon == null`로 동작한다(사격 의도 무시).
  `CorpseLootBuilder` = 무기(장착 탄창 포함) + 예비 탄창 + 루트 테이블. 적 사격은 `ShotFired(Enemy)`만 내고 `NoiseEmitted`는 내지 않는다(M3 규칙). 적은 `IPlayerTarget`(Root·CenterPosition 등)만 안다.
  AI 시야 상한(폭풍 30m)은 `SightCone.EffectiveRange(..., cap)` 한 곳. 적 3부위 `EnemyBodyModel`.
- **레이드 배치:** `CrimsonBasinLayout`(EditorTools) — POI 5곳 좌표(정유소 POI-REF 중심 (300, −60), 150×120m), 고정 분대 SCV 12 + RDR 8(`ExpectedEnemyCount = 20`, 테스트가 이 상수를 쓴다), 스폰 3곳·적 최소 거리 150m.
  적 배치는 `M4EnemySetup`이 레이드 씬 생성 중(NavMesh 굽기 포함) 한다. M6은 기존 씬을 열어 멱등 적용하는 방식(`M6SceneSetup.ApplyAll`, M4·M5 메뉴가 저장 직전 `Apply*IfAssetsExist` 호출).
- **루트·희귀도:** LT-CRATE는 `CreateAssetIfMissing`(이미 있으면 안 바꿈). 데이터 에셋 거의 전부가 `rarity = Scrap`(기본값 0)이고 **UI는 희귀도를 전혀 쓰지 않는다** — §6 희귀도 색(유물 검붉은 테두리 발광)은 미구현.
  "고철"로 쓸 만한 아이템은 잡동사니 JNK-01 녹슨 파이프·JNK-02 엔진 부품(20kg, 무게 확인용)·JNK-03 전선 뭉치.
- **은신처:** `HideoutTab { Loadout, Trader }`, `IHideoutService`(구현체 `HideoutService`, 테스트 `FakeHideoutService`). 거래·키트는 메모리에서 바뀌고 W2/W3/W4에서 함께 저장. 매도는 보관함 아이템만.
- **오디오:** `AudioConfigSO.gunshots[]`는 분류(`WeaponClass`)별 세트, 없는 분류는 `fallbackGunshotClass`(AssaultRifle). `M6AudioSetup.GunshotClasses = {Pistol, Smg, AssaultRifle}`이지만 `SfxSynth`에는 이미 Shotgun·Sniper 음색 파라미터가 있다.
  WAV는 없을 때만 쓰고 SO 클립은 빈 필드만 채운다(M6 Q21).
- **빌드:** 스크립팅 백엔드 설정 없음 = Mono. 설치된 Windows 플레이어 변형은 `win64_player_*_mono`뿐(IL2CPP 모듈 없음). `stripEngineCode: 1`, `link.xml` 없음.
  런타임 코드의 리플렉션은 `RuntimeInitializeOnLoadMethod`(GameManager)·`Enum.TryParse<T>`(SaveSerializer)뿐. JsonUtility DTO는 `[Serializable]` 공개 필드.
- **성능 측정:** `RaidPerformanceTests`·`M6PerformanceTests` — 적 20명 교전(무적화) + 폭풍 Peak, AI 틱 평균 ≤ 1.0ms·99% ≤ 3ms, 투사체 평균 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms(렌더 제외), Audio+폭풍 ≤ 0.2ms, GC 0.
  현재 원본 EditMode 1280 · PlayMode 115(PlayMode 1회 약 10분).

---

## 0. 범위

**만든다 (M7 AC)**

AC1 "무기 5종 전체, AI-SNP·AI-MUT·AI-BOS"
- **7A 무기**
  - WPN-SHT-01 펌프 샷건(12ga, 70RPM, 펠릿 18×8, 내장 튜브 6, 한 발씩 장전 ⟨Q5~Q7⟩), WPN-DMR-01 볼트액션(7.62, 40RPM, 110, 탄창 5). 탄약 AMMO-12GA·AMMO-762, 탄창 MAG-762-5, 12ga 탄 상자 BOX-12GA-8.
  - 플레이어 무기 교체 1/2/3 = 주무기1·주무기2·보조무기 ⟨Q1·Q2⟩. 5종 모두 플레이어가 쏠 수 있다(권총 = 보조무기 슬롯).
  - 산탄 펠릿별 퍼짐 ⟨Q8⟩. 무기별 자리표시 뷰모델 프리팹 ⟨Q4⟩.
  - 고장(Jam)·내구도(§5.1): 내구도 < 50%면 발당 확률 (50 − 내구도) × 0.1%, `T` 길게 눌러 해제 ⟨Q9~Q18⟩. 내구도 저장(save.json v2 ⟨Q13⟩).
  - 총성 세트 확장(산탄·저격), 공이 헛격발·고장 해제 소리(`WeaponClicked`).
- **7B AI**
  - AI-SNP 저격수: 고지대 고정, 조준 중 렌즈 반사 섬광(플레이어가 보는 경고) ⟨Q22·Q23⟩, 볼트액션(WPN-DMR-01).
  - AI-MUT 변이체: 무장 없음, 근접 돌진·근접 피해 ⟨Q24⟩, 무리(분대 연락 + 표적 둘레 분산 접근 ⟨Q25⟩), 울음·공격 소리 ⟨Q26⟩.
  - AI-BOS "재의 군주": 호위 3명(AI-BGD) 동반, 고정 거점 ⟨Q28·Q29⟩, 적 전용 경기관총 WPN-LMG-01 ⟨Q27⟩.
  - Crimson Basin 배치(기존 20명 유지 + 14명 ⟨Q30⟩), 루트 테이블 LT-SNP·LT-MUT·LT-BOS ⟨Q31⟩, LMG 총성 세트.

AC2 "제작대, 방사능·탈수·허기"
- **7C 생존·제작**
  - STAT-RAD: 누적 100 → 모든 부위 최대 HP −20% ⟨Q35⟩, 해독제 ⟨Q36⟩, 방독면(헬멧 슬롯 공유 ⟨Q37⟩)이 재 폭풍 방사능을 무효 ⟨Q38⟩.
  - 방사능 정유소 구역(원형, 0.5/s ⟨Q39⟩), Toxic 자리표시 표시 ⟨Q40⟩, 가이거가 구역에도 반응 ⟨Q41⟩.
  - STAT-DEHY: 수분 100, −0.1/s, 0이면 전신 −0.5 HP/s ⟨Q33⟩, 복부 파괴 시 수분 감소 가속 ⟨Q34⟩. STAT-HUNG: 에너지 100, −0.07/s, 0이면 스태미나 재생 −50%.
  - 물·음식·해독제 아이템(의약품 흐름 재사용 ⟨Q42⟩), 부위 상태 화면(H)에 "전신" 줄(등급 문구만, RULE-05 ⟨Q43⟩). 레이드 사이 이월 없음 ⟨Q32⟩.
  - 제작대: 은신처 [제작] 탭, `CraftingConfigSO` 레시피, 보관함 재료 소비 ⟨Q44~Q47⟩. 고철 = 잡동사니(JNK-*) ⟨Q48⟩.

AC3 "Windows 빌드, 성능 목표 달성"
- **7D 성능·빌드·폴리시**
  - 최악 상황 자동 성능 판정: 적 34명 + 폭풍 Peak + 변이체 무리 돌진 + 보스 LMG ⟨Q50⟩. 1080p 60fps(GTX 1660)는 사람이 확인(§7.1).
  - 개발 빌드 진단: 프레임 시간 로그, 명령줄 스모크 실행(빌드 플레이어에서 세이브·씬 흐름 확인) ⟨Q51⟩.
  - Windows 빌드: Mono 빌드 검증(지금), **IL2CPP는 사용자 설치 대기** — 설치 확인 뒤 전환 단계 ⟨Q52·Q53⟩. 릴리스·개발 빌드 메서드.
  - 희귀도 색 테두리(§6, 인벤토리·상인·보관함 칸) ⟨Q49⟩. CHANGELOG, MVP 남은 임시값 목록.

**만들지 않는다**
- §5.3 방어구 관통·둔탁 피해·방어구 내구도 ⟨Q19⟩(어느 마일스톤 AC에도 없음 — §10 목록), 약실 탄 +1 ⟨Q21⟩, 무기 수리 ⟨Q16⟩, 탄종 선택(같은 구경 다른 탄), 사격 모드 전환 키.
- 탄 상자로 탄창 채우기(낱탄 시스템), 제작 시간·제작 대기열, 은신처 업그레이드(§9 "업그레이드로 확장" — M5 Q1 유지), 상인 재고·시세.
- 적 샷건·적 고장, 적 방어구·상태이상, 런타임 적 스포너, 새 블록아웃 구조물·NavMesh 재굽기 ⟨Q29⟩, 변이체 등 새 실내/동굴.
- 플레이어 무기 애니메이션(장전·펌프·볼트 동작은 시간만), 장전·펌프·볼트·먹기·마시기 효과음, 내구도·생존 수치 UI 숫자(RULE-04·05).
- 실제 모델·텍스처·소리(§2.3 목록에만), 방사능 구역 안개·화면 효과, 지도에 방사능 구역 표시 ⟨Q40⟩.
- 게임패드·설정 화면·키 재설정(§17·§12 밖), 그래픽 품질 옵션.

---

## 1. 아키텍처

### 1.1 경계 요약

새 asmdef 없음. 기능끼리 참조 추가 없음. 싱글톤은 계속 `GameManager` 하나(새 런타임 관리자 `RadiationField`는 씬 오브젝트, 정적 인스턴스 없음).

| 필요한 것 / 주는 것 | 상대 | 방법 | 이유 |
|---|---|---|---|
| 내구도·고장·튜브 장전 규칙 | Weapons(플레이어)·AI(적) | ★Core `WeaponModel` 확장 + `WeaponItem.Durability/IsJammed` + 순수 `JamRules` | M3 결정(무기 로직은 Core 공용) 유지. 적은 마모·고장 옵션을 끈다 ⟨Q18⟩ |
| 낱탄 꺼내기(샷건 탄 상자) | Inventory → Weapons | ★Core 새 인터페이스 `ILooseAmmoSource`(`IMagazineSource`와 별개) | 기존 `IMagazineSource` 구현·테스트 가짜를 깨지 않는다. PlayerInventory·LocalMagazineSource가 둘 다 구현 |
| 고장·헛격발 소리 | Weapons → Audio | ★이벤트 `WeaponClicked(위치, 종류)` | 알림 1:N. HUD 문구 없음 ⟨Q10⟩ |
| 무기 교체 입력 | Weapons 내부 | 입력 액션 `Weapon1/2/3`(Player 맵), `IInventoryService.GetEquipped` 조회 + `EquipmentChanged` | 장착 상태의 진실은 Inventory. 들고 있는 슬롯은 Weapons 상태(저장 안 함) |
| 적 전술 종류 | AI 내부 | `EnemyArchetypeSO.tactics`(`EnemyTacticsKind`, Auto = 기존 usesCover 규칙) | 기존 에셋·테스트 호환 ⟨Q20⟩ |
| 변이체 근접 피해 | AI → Player | ★기존 Core `IDamageable`(플레이어 루트에서 `TryGetComponent`) + `DamageInfo` | 새 인터페이스 불필요. 부위는 PlayerHealth의 기존 피격 지점 판정 그대로 |
| 저격 섬광 방향 | AI 내부 | `IPlayerTarget.EyePosition` | 카메라를 몰라도 된다 |
| 변이체 소리 | AI → Audio | ★이벤트 `EnemyVocalized(위치, 종류)` | "소리가 곧 정보"(기둥 3). 무리 접근을 듣고 알게 |
| 생존 상태(등급만) | Player → UI | ★`IBodyStatusView.GetSurvivalTier(SurvivalStat)` 추가 + 기존 `PlayerBodyStatusChanged` | RULE-05 — 수치 멤버 없이 등급만. 새 이벤트 없음 |
| 방사능 구역률 | Raid → Player·Audio | ★Core 서비스 `IRadiationField.ZoneRatePerSec(Vector3)` (Raid `RadiationField` 등록) | 위치별 동기 조회. 레이드 밖엔 없음 = 0 |
| 방독면 착용 | Inventory → Player | `IInventoryService.GetEquipped(Helmet)`의 정의가 `ArmorDefinitionSO { blocksStormRadiation }` | 매 프레임 조회(사전 조회, 할당 없음) — M6 폭풍 조회와 같은 방식 |
| 물·음식·해독제 사용 | Inventory → Player | 기존 `MedicalUseRequested/Started/Completed` + `MedicalDefinitionSO` 필드 추가 ⟨Q42⟩ | 퀵슬롯·잠금·횟수·세이브(usesLeft)를 그대로 쓴다 |
| 제작 | UI → Hideout | ★`IHideoutService.Recipes`·`TryCraft(recipeId)` + `HideoutChanged` | 상인과 같은 서비스 방식(결과 동기) |
| 진단(개발 빌드) | Core | `DevDiagnostics`(GameManager 오브젝트의 컴포넌트, `Debug.isDebugBuild`일 때만) | 프레임 시간 로그·스모크. 싱글톤 추가 없음 ⟨Q51⟩ |

### 1.2 이벤트·서비스 목록 (M7 추가분, 전부 Core)

| 타입 | 종류 | 발행/등록 | 구독/조회 | 단계 |
|---|---|---|---|---|
| `WeaponClicked { Vector3 Position; WeaponClickKind Kind; }` / enum `WeaponClickKind { DryFire, Jammed, JamCleared }` | 이벤트 | Weapons `WeaponController`(빈 탄창·고장 상태에서 방아쇠를 새로 당길 때 DryFire, 고장 순간 Jammed, 해제 완료 JamCleared) | Audio | 7A |
| `EnemyVocalized { Vector3 Position; EnemyVocalKind Kind; }` / enum `EnemyVocalKind { MutantAlert, MutantAttack }` | 이벤트 | AI `EnemyAgent`(변이체가 Alert/Combat에 처음 들어갈 때 1회, 근접 공격마다) | Audio | 7B |
| `IRadiationField { float ZoneRatePerSec(Vector3 position); }` | 서비스 | Raid `RadiationField`(OnEnable/OnDisable) | Player `PlayerHealth`, Audio `AudioDirector` | 7C |
| `IBodyStatusView.GetSurvivalTier(SurvivalStat)` / enum `SurvivalStat { Hydration, Energy, Radiation }`, `SurvivalTier { Normal, Low, Critical }` | 서비스 멤버 추가 | Player `BodyHealthModel`(또는 묶음 뷰) | UI `BodyStatusPresenter` | 7C |
| `IHideoutService.Recipes`(`IReadOnlyList<CraftingRecipeView>`), `CraftResult TryCraft(string recipeId)` / `CraftingRecipeView`, `CraftingInputView`, enum `CraftResult { Ok, MissingMaterials, NoSpace, UnknownRecipe, Unavailable }` | 서비스 멤버 추가 | Hideout `HideoutService` | UI | 7C |
| `ILooseAmmoSource { bool HasLooseRound(AmmoType); bool TakeLooseRound(AmmoType); }` | 인터페이스 | Inventory `PlayerInventory`, Core `LocalMagazineSource` | Core `WeaponModel`(튜브 장전) | 7A |

- 모두 `readonly struct`, 할당 없는 `EventBus.Publish`. 새 이벤트는 쓰는 곳(Audio)이 있을 때만 만든다(M6 Q27 원칙). 무기 교체 이벤트는 만들지 않는다(구독자 없음 — 테스트는 `WeaponController`를 직접 읽는다).
- `IBodyStatusView`의 RULE-05 규칙 주석은 그대로 — `GetSurvivalTier`는 등급 enum만 돌려준다. 수치는 Player 내부(`SurvivalModel`)에만 있다.

### 1.3 무기 (7A)

```
입력 Weapon1/2/3 ─► WeaponController(Weapons) ── ActiveSlot ─► IInventoryService.GetEquipped(slot) ─► WeaponModel(Core, 무기마다 하나, 캐시)
                         │                                                                         ├─ FireControl (RPM, Semi/Auto)
                         ├─ 뷰모델: WeaponDefinitionSO.viewModel 프리팹(처음 들 때 Instantiate, 캐시)     ├─ 탄창 무기: 기존 재장전 (IMagazineSource)
                         ├─ T 누름: 고장 아님 → 탄창 확인(RULE-04) / 고장 → 길게 눌러 해제 진행         ├─ 튜브 무기: 한 발씩 (ILooseAmmoSource) ⟨Q5⟩
                         └─ Shoot: 펠릿마다 퍼짐 ⟨Q8⟩ → IProjectileService·NoiseEmitted·ShotFired        └─ 마모·고장(WeaponWear 옵션, 플레이어만) ⟨Q9·Q18⟩
```

- **무기 교체** ⟨Q1·Q2⟩: 1 = Primary1, 2 = Primary2, 3 = Secondary. 이미 든 슬롯 키·빈 슬롯 키는 무시. 교체 시간 `weaponSwitchTimeSec`(0.5초, TODO) 동안 사격·조준·재장전·T 불가.
  교체 순간 진행 중 재장전 취소(탄창 그대로), 탄창 확인·고장 해제 진행 취소, 조준 0. 레이드 시작 때 Primary1 → Primary2 → Secondary 중 첫 비지 않은 슬롯(모두 비면 Primary1을 가리키고 비무장).
  인벤토리에서 든 슬롯의 무기를 빼면 비무장, 그 슬롯에 무기를 넣으면 바로 든다(교체 시간 적용). 든 슬롯은 저장하지 않는다.
- **무기마다 모델**: 컨트롤러가 `WeaponItem` → `WeaponModel` 사전을 레이드 동안 캐시(최대 3개)한다. 다시 들어도 `FireControl` 간격 상태가 유지되어 교체로 연사 간격을 건너뛰지 못한다.
- **탄약 정의**: 무기 `ammo`(AmmoType) → `WeaponHandlingConfigSO.ammoDefinitions`(AmmoType마다 `AmmoDefinitionSO` 하나) ⟨Q3⟩. 직렬화 `weapon`·`ammo`·`magazine`은 인벤토리 없는 M1 씬 전용으로 남긴다.
- **뷰모델** ⟨Q4⟩: `WeaponDefinitionSO.viewModel`(§14 기존 필드)에 EditorTools가 만든 자리표시 프리팹(`Prefabs/Weapons/VM_*.prefab`, 프리미티브 조합·팔레트 머티리얼). 비면 기존 직렬화 뷰모델.
  프리팹의 자식 "Muzzle"이 총구(없으면 기존 muzzle). 조준 위치(hip/ads)는 컨트롤러 값 공용(무기별 조정 없음, TODO).
- **산탄** ⟨Q8⟩: 조준 방향 = 기존 지향 퍼짐(조준 시 0) 적용 1회 → 펠릿마다 `pelletSpreadDeg` 원뿔(조준 중에도) 각각 `ApplySpread`. 반동·소음·`ShotFired`는 발당 1회(펠릿 수 무관, M6 규칙).
- **튜브 무기** ⟨Q5~Q7⟩: `WeaponDefinitionSO.internalMagazine`(MAG-12GA-TUBE, 용량 6)이 있으면 `ItemFactory`·세이브 복원이 그 정의의 빈 `Magazine`을 `LoadedMagazine`으로 만든다(떼어낼 UI 없음 — 지금도 무기에서 탄창을 빼는 인벤토리 명령은 없다).
  R = 튜브 장전 시작: `roundReloadTimeSec`(0.5초)마다 `ILooseAmmoSource.TakeLooseRound(Gauge12)`로 1발씩 넣고, 가득 차거나 낱탄이 없으면 끝. 빠른 재장전 없음(R 재입력 무시).
  장전 중 사격 입력 → 튜브에 1발 이상이면 장전 중단 후 사격(진행 중인 한 발은 버리지 않고 넣지도 않음 = 꺼낸 적 없음). 0발이면 무시.
  낱탄 = 탄 상자 `MagazineDefinitionSO { isAmmoBox = true }`(BOX-12GA-8, 1×1). 상자는 무기에 넣을 수 없고(`Fits`·`IsLoadable`에서 제외) 빈 상자는 0발 아이템으로 남는다 ⟨Q6⟩. 꺼내는 순서 = 조끼 → 주머니 → (설정 시) 배낭, 탄이 가장 **적은** 상자부터(상자 정리).
  RULE-04 T 확인은 튜브를 탄창처럼 분류한다(6발 기준 구간). DMR은 일반 분리형 탄창(MAG-762-5), 볼트 동작 = RPM 40 간격(Semi).
- **내구도·고장** ⟨Q9~Q18⟩: `WeaponItem.Durability`(0~100 %, 기본 100). 플레이어 `WeaponModel`은 `WeaponWear` 옵션(임계 50·확률 0.1%/점 = GDD, `WeaponHandlingConfigSO`) + `System.Random`.
  발사가 성립하는 순간(`FireControl`이 true, 탄 있음) `JamRules.Chance(durability) = max(0, 임계 − 내구도) × 확률/점`으로 굴림 → 고장이면 **탄을 쓰지 않고 발사 없음**, `IsJammed = true`, `WeaponClicked(Jammed)`.
  발사되면 내구도 −`durabilityLossPerShot`(무기별, 0 아래로 안 감). 고장 중: 방아쇠 = `WeaponClicked(DryFire)`(새로 당길 때만), 재장전·탄창 확인 불가, T를 `jamClearHoldSec`(1.2초) 계속 누르면 해제(떼면 처음부터) → `JamCleared`.
  적 무기는 마모·고장 없음(옵션 끔), 스폰 내구도만 `archetype.weaponDurabilityRange`에서 무작위(시체에서 주운 무기는 고장 날 수 있다) ⟨Q12·Q18⟩. 상인·제작·키트·상자 무기는 100.
  가격 미반영(M5 Q10과 같음) ⟨Q15⟩, 표시 없음 ⟨Q17⟩, 수리 없음 ⟨Q16⟩, 고장 상태는 저장 안 함 ⟨Q14⟩.
- **적 공용 경계**: `EnemyWeapon`은 `new WeaponModel(item, spares)`(마모 옵션 없음)로 지금과 같다. 튜브·낱탄은 적이 쓰지 않는다(적 샷건 없음).

### 1.4 AI (7B)

```
EnemyArchetypeSO.tactics ─► EnemyBrain 생성자 팩토리
   Auto(기존: usesCover ? Raider : Scavenger) │ Scavenger │ Raider │ Sniper(새) │ Mutant(새)
   + anchorRadiusM > 0 → 모든 이동 목적지를 시작 위치 반경 안으로 자름(TacticsContext.ClampToAnchor) — 보스·호위 ⟨Q28⟩
EnemyIntent 추가: Melee(bool), Telegraph(bool)  ─► EnemyAgent: ApplyMelee → EnemyMelee(순수: 사거리·쿨다운·준비 시간) → IDamageable
                                                ─► EnemyAgent: ApplyTelegraph → SniperGlint(섬광 쿼드, 표적 눈 쪽으로, 거리 비례 크기)
```

- **AI-SNP** ⟨Q22·Q23⟩: 순찰·이동 없음(Hold, 시작 자리 = 고지대 "퍼치"). Idle/Patrol = 시선 좌우 훑기(`scanArcDeg`, 기존 수색 시선 로직 재사용).
  Alert/Combat: 표적 쪽으로 돌고, **처음 보거나 다시 볼 때마다 `telegraphSec`(1.5초) 동안 섬광 켜짐(Telegraph) 후** 사격, 사격 사이에도 조준 중이면 섬광 유지. 표적을 놓치면 Search = 자리에서 LKP 쪽 훑기.
  사거리·시야 150m(TODO), 폭풍 상한은 그대로 적용(Peak 30m면 사실상 못 봄). 소리를 들으면 그쪽으로 돈다(이동 안 함). 볼트액션 반자동 간격 = 무기 RPM + `semiShotIntervalSec`.
- **섬광**: 저격수 자식 "SniperGlint"(Quad, `M_SniperGlint` Unlit 발광 Bone × HDR — Bloom이 받는다, 그림자·콜라이더 없음). Telegraph일 때만 켜고 매 프레임 `IPlayerTarget.EyePosition`을 향하며 크기 = 거리 × tan(`glintAngularSizeDeg`)(먼 거리에서도 몇 픽셀).
  안개에 가려지는 것은 그대로(폭풍 중에는 안 보임 — 저격수도 못 봄).
- **AI-MUT** ⟨Q24·Q25⟩: 무장 없음(`loadouts` 비움), 엄폐·우회 없음. Patrol = 무리(분대) 대형으로 느리게 배회. Suspicious = 소리 위치로 걷기. Alert/Combat = 질주로 표적 쪽, 목적지 = 표적 위치 + 분대 순번별 둘레 오프셋(`surroundRadiusM`, 각도 = 360°/분대 수) — 한 줄로 몰리지 않게.
  표적과 수평 거리 ≤ `meleeRangeM`(1.8m)이고 눈 → 표적 중심 시야가 트였으면 `meleeWindupSec`(0.3초) 뒤 타격, 다음 타격까지 `meleeIntervalSec`(1.1초). 준비 중 표적이 사거리 밖으로 나가면 헛침(피해 없음, 쿨다운 소모).
  피해 = `IDamageable.ApplyDamage(DamageInfo(meleeDamage, 관통 0, 표적 중심, 방향, 거리, 부위 힌트))`, 부위 = 흉부·복부·왼팔·오른팔 중 AiConfigSO 가중치(`EnemyMelee.PickStrikePart`) — PlayerHealth는 힌트 부위에 그대로 맞힌다(7B 리뷰 수정 1, CHANGELOG 7B). 출혈·골절 굴림은 기존 규칙.
  분대 연락(기존 50m 공유) = 무리가 함께 돈다. 첫 Alert/Combat 진입·타격마다 `EnemyVocalized` ⟨Q26⟩. 발소리는 기존 `FootstepTaken`(질주).
- **AI-BOS·AI-BGD** ⟨Q27~Q29⟩: 보스 = Raider 전술(엄폐 사용, `flanks = false`) + `anchorRadiusM` 25 + 적 전용 WPN-LMG-01(Auto, 긴 점사). 호위 AI-BGD 3명 = Raider 전술(소총, `flanks = false`, 같은 anchor), 보스와 같은 분대.
  거점 = 매몰된 마을 외곽의 기존 구조물·CoverPoint(새 구조물·NavMesh 재굽기 없음). 시체: 보스 무기·탄창은 `enemyOnly`라 시체에 넣지 않고 LT-BOS만 굴린다(호위는 일반 시체).
- **적 전용 무기** ⟨Q27⟩: `WeaponDefinitionSO.enemyOnly = true`(WPN-LMG-01, MAG-762-100). `CorpseLootBuilder`가 enemyOnly 무기·그 무기 탄창을 빼고, 상인 목록·루트 테이블에 넣지 않는다(데이터 테스트로 확인). `WeaponClass.MachineGun`을 enum 끝에 추가(세이브는 id라 무관, 오디오 세트용).
- **배치** ⟨Q30⟩: 기존 20명은 그대로. 새 `M7EnemyLayout`(EditorTools 새 파일 — `CrimsonBasinLayout`은 고치지 않음): SNP 2(송전탑 언덕 정상, 고속도로 남동 능선), MUT 무리 2 × 4(정유소 북쪽 외곽, 고속도로 동쪽 끝), BOS 1 + BGD 3(마을 외곽 북동 구조물). 모두 스폰에서 190m 이상(저격수는 스폰이 시야 150m + 여유 50m 밖).
  `M7EnemySceneApply`가 기존 레이드 씬을 열어 `M4EnemySetup`의 배치 함수로 이름 접두어 `M7_`로 멱등 추가(있으면 위치·참조만 다시 설정). 퍼치 높이는 Terrain 샘플 + NavMesh 스냅(3m) — 실패하면 메뉴가 예외.

### 1.5 생존·제작 (7C)

```
PlayerHealth.Update ─► SurvivalModel.Tick(dt, stomachDestroyed)   수분 −0.1/s(×2 복부 파괴) · 에너지 −0.07/s  (순수, Player)
                  │       수분 0 → BodyHealthModel.ApplySystemicDamage(0.5/s, 파괴되지 않은 부위마다) ⟨Q33⟩
                  │       에너지 0 → PlayerMotor가 StaminaModel 재생 × 0.5 (같은 어셈블리, 직접 읽기)
                  ├─► 방사능률 = (IAshStormInfo.RadiationPerSec × (방독면 ? 0 : 1)) + IRadiationField.ZoneRatePerSec(위치) ⟨Q38⟩
                  │       RadiationDoseModel.Tick → Dose ≥ 100 → BodyHealthModel.MaxHpMultiplier = 0.8 (현재 HP는 새 최대로 자름) ⟨Q35⟩
                  └─► 등급이 바뀌면 PlayerBodyStatusChanged (UI가 GetSurvivalTier를 다시 읽음)
의약품 흐름 확장 ⟨Q42⟩: MedicalDefinitionSO { hydrationRestore, energyRestore, curesRadiation }
   MedicalUseModel.FindTarget: 부위 대상이 없고 전신 효과가 필요하면(수분<최대 / 에너지<최대 / 선량>0) Started, Target = Thorax(전신 표시)
   완료 → SurvivalModel.Restore / RadiationDoseModel.Reset(+ MaxHpMultiplier 1) ⟨Q36⟩
```

- **이월 없음** ⟨Q32⟩: 레이드마다 수분 100·에너지 100·선량 0(플레이어가 레이드 씬과 함께 새로 생김, M5 Q18). 세이브 변경 없음.
- **방독면** ⟨Q37·Q38⟩: GEAR-GSM-01 = `ArmorDefinitionSO { kind = Helmet, armorClass = 0, covers = [], blocksStormRadiation = true }` → 헬멧 슬롯을 공유(헬멧과 동시 착용 불가 — §6 슬롯 목록 그대로). 폭풍 방사능만 막는다. 필터 소모 없음.
  PlayerHealth가 매 프레임 `IInventoryService.GetEquipped(Helmet)`을 조회(레이드 밖·인벤토리 없음 = 미착용).
- **정유소 구역** ⟨Q39~Q41⟩: `RaidConfigSO.radiationZones`(id·중심 XZ·반경·초당·가장자리 감쇠 폭). 순수 `RadiationZoneMath.RateAt(zones, position)` = 구역마다 중심 수평 거리로 (반경 − 감쇠폭) 안 = 최대, 가장자리까지 선형 0, 겹치면 합.
  씬 최상위 "RadiationField"(Raid, `IRadiationField` 등록). 표시 = 가장자리 둘레의 Toxic 자리표시(드럼통·경고 기둥 프리미티브, 콜라이더 없음 → NavMesh·전투 무관) + 구역 안 드럼통 무더기. 지도 표시 없음.
  가이거 = **환경 방사능률**(폭풍 률 + 청취자 위치 구역 률, 방독면 무관) ⟨Q41⟩ — `GeigerClickModel`은 그대로, 입력만 합.
- **부위 상태 화면** ⟨Q43⟩: 7부위 줄 아래 "전신" 한 줄 — 수분 Low "목마름" / Critical "탈수", 에너지 Low "배고픔" / Critical "허기", 방사능 Low "방사능 오염"(선량 ≥ 50) / Critical "방사선 피폭"(≥ 100, 최대 HP 감소 중). Normal이면 비움. 숫자·막대 없음.
  Low 경계 = 최대의 25%(수분·에너지), Critical = 0(TODO). `IBodyStatusView.GetSurvivalTier`는 경계 판정만 돌려준다.
- **제작대** ⟨Q44~Q48⟩: 은신처 허브 탭 [장비]/[상인]/**[제작]**. `CraftingConfigSO.recipes`(id·이름·입력(정의, 개수)[]·산출(정의, 개수)). 재료 = **보관함만**, 같은 정의의 아무 인스턴스(보관함 배치 순). 즉시 완료, 탄피 비용 없음.
  순수 `CraftingModel.TryCraft(stash, recipe)`: 재료 수 확인 → 재료 제거(자리 기억) → 산출물 전부 `StashRules.TryPlaceAll` → 실패면 재료를 원래 자리로 되돌리고 `NoSpace`. 성공 = 메모리에서만 바뀌고 W2/W3/W4 저장(상인과 같음).
  산출 탄약 = 가득 찬 탄창 또는 12ga 탄 상자(정의를 `ItemFactory.Create`) ⟨Q46⟩. 제작 무기(내구도 100)는 레시피에 없다.
  UI: 레시피 목록(이름·필요 재료 "가진 수/필요 수" — 수치는 재료 개수뿐, 체력·탄수 아님)·[제작] 버튼(재료 부족이면 비활성)·결과 문구(재료 부족/보관함 공간 없음).

### 1.6 세이브 v2 (7A) ⟨Q13⟩

- `SaveSerializer.CurrentVersion = 2`. `ItemRecord.durability`(float, 기본 −1 = 해당 없음) 추가 — `WeaponItem`은 0~100 기록.
- 읽기: v2 → 무기 레코드의 durability를 0~100으로 자름(−1·NaN이면 100). **v1 → 이전**: 모든 무기 durability = 100(필드 기본값에 기대지 않고 버전으로 판정 — JsonUtility의 누락 필드 처리에 의존하지 않음). 그 밖 필드는 같다.
  이전은 읽는 순간 메모리에서만, 다음 저장이 v2로 쓴다. `.bak`은 이전 세대 그대로(자동 복구 안 함, M5 유지).
- 튜브 무기: 레코드는 기존처럼 `loadedMagazineId = MAG-12GA-TUBE` + 탄 수(튜브 정의도 ItemDatabase에 등록 — 메뉴가 Assets 전체를 모으므로 자동). id가 비거나 탄약이 다르면 **빈 튜브**로 복원(분리형 무기는 기존대로 탄창 없는 무기).
- 7B·7C는 스키마를 바꾸지 않는다(새 아이템 id만 — 알 수 없는 id 규칙 그대로). 적 전용 정의는 플레이어 손에 들어오지 않으므로 세이브에 나타나지 않는다.
- 다운그레이드(v2 파일을 M6 빌드로 열기) = 기존 규칙대로 지원하지 않는 버전 → 격리 + 새 프로필(문서화만).

### 1.7 씬·에셋 적용 방식

- M6 방식 그대로: 새 통합 메뉴 **`Ashfall/Setup/Apply M7 Content`**(배치 `Ashfall.EditorTools.M7SceneSetup.ApplyAll`)가 데이터 → 자리표시 에셋 → 레이드·은신처 씬 열어 멱등 적용 → 저장. 각 하위 단계는 자기 적용 함수만 더한다:
  - 7A `M7WeaponDataSetup.CreateData` · `M7WeaponAssets.CreateViewModels`(프리팹) · 오디오 WAV(M6AudioSetup 레시피 확장).
  - 7B `M7AiDataSetup.CreateData` · `M7EnemySceneApply.ApplyRaid`(적·섬광·머티리얼).
  - 7C `M7SurvivalDataSetup.CreateData` · `M7HazardSceneApply.ApplyRaid`(RadiationField·Toxic 표시).
- `M6SceneSetup.ApplyRaidIfAssetsExist`처럼 `M7SceneSetup.ApplyRaidIfAssetsExist`를 `M4SceneSetup.CreateRaidScene` 저장 직전(M6 호출 다음)에 한 줄 추가 — M4 메뉴로 씬을 다시 만들어도 M7이 빠지지 않는다.
- 플레이어 오브젝트 변경(입력·컨트롤러 필드)은 씬 적용이 아니라 코드 기본값·SO 참조로(예: `WeaponHandlingConfig`의 탄약 표) — 씬 수정 최소화.
- 데이터 에셋: 새 에셋은 없을 때만 생성, 기존 에셋(LT-CRATE·TraderConfig·AudioConfig·기존 무기 정의)은 "없으면 추가·빈 필드만"(M6 Q21 원칙). 두 번째 실행 바이트 동일.

### 1.8 성능·할당

- 무기: 펠릿 8개는 `ProjectileSystem` 목록(구조체) 추가뿐. 교체·뷰모델 Instantiate는 처음 들 때 1회(무기당), 이후 SetActive. 발사·고장 굴림·튜브 장전 경로 할당 0.
- AI: 새 전술·`EnemyMelee`·섬광 갱신 할당 0(Tick 규칙 유지). 섬광은 Telegraph 중인 저격수만 transform 갱신. 근접 시야 확인은 기존 `ILineOfSight` 1회/타격.
- 생존: `SurvivalModel.Tick`·구역 률(구역 수 ≤ 4 선형 탐색)·방독면 조회 할당 0. UI는 등급이 바뀔 때만 다시 그림.
- 적 34명: AI 틱 분산(`AiTickScheduler`)·감각 예산(프레임당 8명)은 그대로. 4단계 자동 판정(§6 7D): 기존 예산 유지(AI 평균 ≤ 1.0ms·99% ≤ 3ms, 투사체 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms(렌더 제외), Audio+폭풍+방사능 ≤ 0.3ms, GC 0) ⟨Q50⟩.

### 1.9 빌드·IL2CPP (7D)

- **지금(Mono)**: `BuildTools.BuildWindows64`(릴리스) 유지 + `BuildWindows64Development`(개발 빌드: `Development`, 출력 `Builds/Windows64Dev/`). 둘 다 스크립팅 백엔드를 바꾸지 않는다.
- **IL2CPP 준비(코드만, 실행은 설치 뒤)** ⟨Q52·Q53⟩:
  - `BuildTools.BuildWindows64Il2Cpp`: 먼저 IL2CPP 플레이어 변형 폴더(`PlaybackEngines/windowsstandalonesupport/Variations/win64_*_il2cpp`)와 `BuildPipeline.IsBuildTargetSupported`를 확인해 없으면 "IL2CPP 모듈 설치 필요" 예외(종료 코드 ≠ 0). 있으면 이 빌드 동안만 백엔드를 IL2CPP로 바꾸고 끝나면 되돌린다.
  - 사용자 설치 확인 뒤 별도 단계 "7D-IL2CPP"에서 `ProjectSettings`를 IL2CPP로 영구 전환(§13.1)하고 커밋, Mono 메서드는 대체 경로로 남긴다(`BuildWindows64Mono`).
  - `Assets/_Project/link.xml`(텍스트): `Ashfall.*` 어셈블리 `preserve="all"` ⟨Q53⟩ — 작은 어셈블리라 크기 영향 작고, JsonUtility DTO·`RuntimeInitializeOnLoadMethod`·UI Toolkit 바인딩 이름 스트리핑 위험을 없앤다. Managed Stripping Level은 Minimal(기본) 유지.
- **IL2CPP 위험 정리(미리)**: (1) 관리 코드 스트리핑 — 리플렉션 없음 확인됨, link.xml로 보강. (2) JsonUtility는 IL2CPP 지원(공개 `[Serializable]` 필드) — 스모크 실행으로 실제 쓰기·읽기 확인.
  (3) `Enum.TryParse<T>`·제네릭 이벤트 버스(`Subscribe<T>` struct T) — AOT 제네릭은 컴파일 시점에 알려져 문제 없음(스모크로 확인). (4) 기본 인터페이스 메서드·`dynamic` 쓰지 않기(이 설계는 `ILooseAmmoSource`를 별도 인터페이스로 둠).
  (5) 빌드 시간 증가(10~20분+), C++ 툴체인(VS 2022 "C++를 사용한 데스크톱 개발" — MSVC v143·Windows SDK) 필요. (6) 스택 추적 줄 번호 없음 — 개발 빌드는 `il2cppStacktraceInformation`을 MethodFileLineMapping으로.
  (7) 입력 시스템·URP·UI Toolkit 패키지의 자체 link.xml은 패키지에 있다. (8) 부동소수 결과 차이 — 게임 로직 결정성에 의존하는 곳 없음(합성 WAV는 에디터에서만).
- **개발 빌드 진단** ⟨Q51⟩: Core `DevDiagnostics`(GameManager가 `Debug.isDebugBuild`일 때만 붙임, 릴리스·에디터 Play에는 없음):
  - 프레임 시간: 10초마다 중앙값·99%·최대 ms와 fps 추정을 `Player.log`에 한 줄(GC 할당 없는 고정 링 버퍼). 사람이 1080p로 플레이하며 로그를 읽는다(§7.1 P1~P3).
  - 스모크: 명령줄 `-ashfallSmoke <폴더>`일 때만 — 세이브 경로를 그 폴더로 바꾸고(`FileSaveStorage` 경로 주입, 실제 persistentDataPath 안 씀) Boot → MainMenu → Hideout(저장·읽기) → Raid(10초, 폭풍 강제 시작) → 포기 → Hideout(결과 반영·저장) → 종료.
    단계마다 로그, 실패·예외·60초 초과면 종료 코드 1, 성공 0. 배치모드 플레이어(`-batchmode -nographics`)로 돌려 Mono·IL2CPP 빌드 둘 다 자동 확인.

---

## 2. 자리표시 에셋

### 2.1 만드는 방법 (M6과 같음, 전부 EditorTools·결정적)
- WAV: `SfxSynth` 레시피 추가 → `WavWriter` PCM16 모노 22050Hz. **파일이 없을 때만** 쓰고, 새로 쓴 파일에만 임포트 설정. `AudioConfigSO` 클립은 빈 필드만 채움(세트가 없으면 배열에 추가).
- 프리팹: `PrefabUtility.SaveAsPrefabAsset`(프리미티브 조합, 콜라이더 제거, 팔레트 머티리얼). 같은 경로에 있으면 **덮지 않는다**(사용자 교체 보존 — WAV 규칙과 같음). 다시 만들려면 파일을 지운다.
- 머티리얼: `PaletteMaterialMap`에 새 키(Toxic·Mutant·Boss·SniperGlint) — 색 = 팔레트 토큰, 있으면 색만 다시 기록(GUID 유지). `PaletteConsistencyTests`가 새 머티리얼도 본다.

### 2.2 자리표시 에셋 목록 (모두 `TODO(GDD-?)` 자리표시)

| 경로 (`Assets/_Project/...`) | 내용 / 합성 | 쓰는 곳 | 단계 |
|---|---|---|---|
| `Audio/Placeholder/Gunshots/GS_Shotgun_{Near,Mid,Far}.wav` | 기존 Gunshot 레시피 Shotgun 음색(낮고 넓은 폭발) | `gunshots[Shotgun]` | 7A |
| `Audio/Placeholder/Gunshots/GS_Sniper_{Near,Mid,Far}.wav` | Sniper 음색(날카로운 크랙 + 긴 메아리) | `gunshots[Sniper]` | 7A |
| `Audio/Placeholder/Weapons/WPN_DryClick.wav` | 3ms 금속 클릭 + 짧은 공진(0.08s) | `AudioConfigSO.dryFireClip`(DryFire·Jammed) | 7A |
| `Audio/Placeholder/Weapons/WPN_JamClear.wav` | 슬라이드 긁힘 노이즈 + 클릭 2개(0.4s) | `jamClearClip` | 7A |
| `Prefabs/Weapons/VM_{Pistol,Smg,Rifle,Shotgun,Dmr}.prefab` | 큐브·실린더 조합(길이·굵기로 구분), M_RustMetal·M_Debris, 자식 "Muzzle" | `WeaponDefinitionSO.viewModel` | 7A |
| `Audio/Placeholder/Gunshots/GS_Lmg_{Near,Mid,Far}.wav` | 새 MachineGun 음색(Rifle보다 낮고 짧게) | `gunshots[MachineGun]` | 7B |
| `Audio/Placeholder/Creatures/MUT_Alert_01~02.wav` | 하강 톤 그르렁(성문 노이즈 × 저역 공진, 1.2s) | `AudioConfigSO.mutantAlertClips` | 7B |
| `Audio/Placeholder/Creatures/MUT_Attack_01~02.wav` | 짧은 비명 + 휘두름 노이즈(0.5s) | `mutantAttackClips` | 7B |
| `Art/Materials/Palette/M_Mutant.mat`·`M_Boss.mat`·`M_SniperGlint.mat` | 변이체 Ash_Grey 거친 면 / 보스 Ember 강조 / 섬광 Bone 발광(Unlit, HDR ×4) | 적 몸체·섬광 | 7B |
| `Art/Materials/Palette/M_Toxic.mat` | Toxic(#7A8A2A) 단색 + 약한 발광 | 정유소 드럼통·경고 기둥 | 7C |

합계 WAV 15개(7A 8, 7B 7) — 기존 27개 + 15 = 42개. 프리팹 5개, 머티리얼 4개. 7C 소리 없음(먹기·마시기 효과음은 만들지 않는다).

**실제 생성 결과(M7 완료 시점, 위 표와 다른 곳 — 계약 `M7Names` 경로를 따름):**
- WAV 13개(기존 27 + 13 = **40개**): `Gunshots/GS_{Shotgun,Sniper,Lmg}_{Near,Mid,Far}.wav` 9개, `Weapon/WPN_DryFire.wav`·`WPN_JamClear.wav`, `Enemy/MUT_Alert.wav`·`MUT_Attack.wav`(종류마다 1개).
- 프리팹 5개: `Prefabs/Weapons/VM_WPN-{PST,SMG,RIF,SHT,DMR}-01.prefab`(무기 id 기준 이름).
- 머티리얼 4개: `Art/Materials/Palette/M_Mutant.mat`·`M_Boss.mat`·`M_Toxic.mat`, `Art/Materials/M_SniperGlint.mat`.
- 씬 자리표시: 레이드 `M7_ToxicMarkers` 아래 프리미티브 54개(기둥·판·드럼통), 저격수 자식 `SniperGlint` 쿼드.

### 2.3 사용자에게 필요한 실제 에셋 (교체 위치)

| 에셋 | 권장 형식 | 교체 방법 |
|---|---|---|
| 무기 5종 1인칭 모델 + 장전·펌프·볼트·고장 해제 애니메이션 | FBX + Animator(조준 위치는 컨트롤러 값) | 같은 경로 프리팹을 덮거나 `WeaponDefinitionSO.viewModel` 교체. 애니메이션 길이는 SO 시간 값과 맞춘다 |
| 총성 산탄·저격·LMG 근/중/원 | WAV 44.1kHz, 원거리 메아리 스테레오 가능 | `GS_*` 같은 경로 덮어쓰기(M6 Q21 보존) |
| 무기 기계음: 헛격발·고장 해제·펌프·볼트·장전 | WAV 원샷 | `WPN_*` 덮어쓰기(펌프·볼트·장전 슬롯은 아직 없음 — TODO) |
| 변이체 모델·애니메이션(달리기·공격)·울음 2종 이상 | FBX + WAV | 적 루트 자식 시각 오브젝트 교체(`bodyVisual`), `MUT_*` 덮어쓰기 |
| 저격수·보스·호위 모델, 저격 렌즈 섬광 텍스처 | FBX, 별 모양 PNG(알파) | `bodyVisual`, `M_SniperGlint`에 `_BaseMap` |
| 방독면·물병·통조림·해독제·탄 상자 아이콘/모델 | PNG 아이콘 | `ItemDefinitionSO.icon`(UI가 아이콘을 쓰기 시작하면) |
| 정유소 방사능 표시물(드럼통·경고판·오염 데칼) | FBX·데칼 | `M7HazardSceneApply`가 만드는 "RadiationField/Markers" 자식 교체 |
| 가이거 구역 환경음, 먹기·마시기·해독제 주사 효과음 | WAV | 새 슬롯 필요(TODO, M7 밖) |

---

## 3. 수치 (SO 기본값 — GDD에 없는 것은 전부 `TODO(GDD-?)`)

### 3.1 무기 (`WeaponDefinitionSO`, Data/Weapons/) — GDD = §5.2 표 값

| id | 이름 | cls | 탄 | RPM(GDD) | 피해(GDD) | 탄창 | 모드 | 반동 V/H° | ADS s | 재장전 s(일반/빠른, 튜브 발당) | 지향 퍼짐° | 펠릿 퍼짐° | 소음 m | 크기 | kg | basePrice | 마모 %/발 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| WPN-SHT-01 | 펌프 샷건 | Shotgun | Gauge12 | 70 | 18(펠릿당) | 튜브 MAG-12GA-TUBE(6) | Semi | 3.5 / 1.0 | 0.30 | 발당 0.5 | 3.0 | 2.5 | 110 | 4×1 | 3.4 | 2500 | 0.12 |
| WPN-DMR-01 | 볼트액션 | Sniper | Rifle762 | 40 | 110 | MAG-762-5 | Semi | 2.8 / 0.4 | 0.40 | 3.2 / 2.2 | 4.0 | — | 160 | 5×1 | 4.2 | 5000 | 0.10 |
| WPN-LMG-01 | 경기관총(적 전용) | MachineGun | Rifle762 | 600 | 40 | MAG-762-100 | Auto | — | — | 5.0 / 4.0 | — | — | 160 | 5×2 | 9.0 | 0 | 0 |
| 기존 PST/SMG/RIF | | | | | | | | | | | | | | | | | 0.06 / 0.04 / 0.05 (값만 추가) |

모두 TODO(GDD-?) 임시값(RPM·피해·탄창 수 제외). `enemyOnly`: LMG만 true. RIF·SMG·PST 기존 값은 바꾸지 않는다.

### 3.2 탄약·탄창

| id | 종류 | 값 |
|---|---|---|
| AMMO-12GA | 탄약 Gauge12 | damage 18, penetration 1, velocity 400, **pelletCount 8**(GDD 18×8), maxFlight 0.8s |
| AMMO-762 | 탄약 Rifle762 | damage 110, penetration 4, velocity 820, maxFlight 3.0s |
| MAG-762-5 | 탄창 | capacity 5(GDD), 1×1, 0.15kg, 300 |
| MAG-762-100 | 탄창(적 전용, LMG) | capacity 100, 2×1, 1.5kg, 0 |
| MAG-12GA-TUBE | 내장 튜브(그리드에 놓이지 않음) | capacity 6(GDD), 1×1, 0kg, 0 |
| BOX-12GA-8 | 탄 상자 `isAmmoBox` | capacity 8, 1×1, 0.35kg, 200 |

### 3.3 `WeaponHandlingConfigSO` 추가 (Weapons)
| 필드 | 값 | 출처 |
|---|---|---|
| `jamDurabilityThreshold` | 50 | GDD §5.1 |
| `jamChancePerDurabilityPoint` | 0.001 (0.1%) | GDD §5.1 |
| `jamClearHoldSec` | 1.2 | TODO ⟨Q9⟩ |
| `weaponSwitchTimeSec` | 0.5 | TODO ⟨Q1⟩ |
| `ammoDefinitions` | AMMO-9MM·556·762·12GA | ⟨Q3⟩ |

### 3.4 적 유형 (`EnemyArchetypeSO`, Data/AI/) — 전부 TODO(GDD-?) (§7 행동·무장만 GDD)

| 필드 | AI-SNP 저격수 | AI-MUT 변이체 | AI-BOS 재의 군주 | AI-BGD 호위병 |
|---|---|---|---|---|
| tactics | Sniper | Mutant | Raider | Raider |
| 머리/몸통/다리 HP | 35/80/65 | 30/60/50 | 50/160/110 | 35/80/65 |
| sightRange / sightAngle / hearing× | 150 / 70 / 1.0 | 40 / 140 / 1.5 | 70 / 110 / 1.0 | 60 / 110 / 1.0 |
| 무장 | WPN-DMR-01 + MAG-762-5, 예비 3 | 없음 | WPN-LMG-01 + MAG-762-100, 예비 2 | WPN-RIF-01 + MAG-556-30, 예비 3 |
| 이동 patrol/move/sprint | 0/0/0 | 1.2 / 4.0 / 5.0 | 1.2 / 2.5 / 3.0 | RDR 값 |
| 반응·명중 | 반응 1.0s, 오차 0.05m + 0.004m/m, 안정 2.0s | — | 반응 0.6s, RDR 오차 × 1.3 | RDR 값 |
| 연사 | semi 간격 2.0~3.5s | — | 점사 8~15발, 쉼 0.6~1.2s | RDR 값 |
| engageMaxRangeM | 200 | — | 90 | RDR 값 |
| usesCover / flanks / investigatesNoise | false / false / false | false / false / true | true / false / false | true / false / false |
| anchorRadiusM | 0(이동 없음) | 0 | 25 | 25 |
| weaponDurabilityRange(%) | 50~90 | — | — | 35~85 |
| 근접 dmg / range / interval / windup | — | 14 / 1.8m / 1.1s / 0.3s | — | — |
| surroundRadiusM | — | 2.5 | — | — |
| telegraphSec / scanArcDeg | 1.5 / 60 | — | — | — |
| loot | LT-SNP | LT-MUT | LT-BOS | LT-RDR |
| corpseDisplayName | 저격수의 시체 | 변이체 사체 | 재의 군주의 시체 | 호위병의 시체 |

- 기존 AI-SCV·RDR: `weaponDurabilityRange` 35~85만 추가(나머지 불변). `AiConfigSO`: `glintAngularSizeDeg` 0.35, `vocalCooldownSec` 4(같은 변이체 울음 간격).

### 3.5 배치 (`M7EnemyLayout`, 레벨 데이터 — TODO(GDD-?))
| 분대 | 유형 × 수 | 위치(XZ, 대략) | 비고 |
|---|---|---|---|
| SNP_PYL | SNP × 1 | 송전탑 언덕 정상 (230, 300) 부근 최고점 | 시선 남서 |
| SNP_HWY | SNP × 1 | 고속도로 남동 능선 (120, −380) 부근 고점 | 시선 북 |
| MUT_REF | MUT × 4 | 정유소 북쪽 외곽 (300, 40) 배회 경로 | 방사능 구역 가장자리 |
| MUT_HWY | MUT × 4 | 고속도로 동쪽 끝 (160, −300) 배회 경로 | |
| BOS_VIL | BOS × 1 + BGD × 3 | 마을 외곽 북동 구조물 (−270, 120) | 기존 CoverPoint, anchor 25m |
합계 14명 → 레이드 34명(SCV 12, RDR 8, SNP 2, MUT 8, BOS 1, BGD 3). 실제 좌표는 7B 데이터 단계가 Terrain 높이·스폰 거리(≥ 190m)·NavMesh 스냅을 만족하게 정한다.

### 3.6 생존 (`PlayerConfigSO` 추가, Player)
| 필드 | 값 | 출처 |
|---|---|---|
| `hydrationMax` / `hydrationDecayPerSec` | 100 / 0.1 | GDD §4.3 |
| `dehydratedHpPerSec` | 0.5 (부위마다 ⟨Q33⟩) | GDD 값, 해석 TODO |
| `stomachDestroyedHydrationMultiplier` | 2.0 | TODO ⟨Q34⟩ |
| `energyMax` / `energyDecayPerSec` | 100 / 0.07 | GDD §4.3 |
| `starvingStaminaRegenMultiplier` | 0.5 | GDD "−50%" |
| `radiationSicknessDose` / `radiationSicknessMaxHpMultiplier` | 100 / 0.8 | GDD "누적 100 → 최대 HP −20%" |
| `radiationWarnDose` | 50 | TODO ⟨Q43⟩ |
| `survivalLowRatio` | 0.25 | TODO ⟨Q43⟩ |
수분 0까지 1000초(16.7분), 에너지 0까지 약 1429초(23.8분) — 25분 레이드 후반에 의미가 생긴다. 폭풍 전체(220초)를 방독면 없이 맞으면 선량 ≈ 200 → 피폭.

### 3.7 새 아이템 (Data/Items/…) — 전부 TODO(GDD-?)
| id | 이름 | SO | 크기 | kg | 효과 | 사용 s | 횟수 | basePrice | 희귀도 |
|---|---|---|---|---|---|---|---|---|---|
| ITM-WTR-01 | 물병 | Medical | 1×2 | 0.6 | 수분 +50 | 3 | 2 | 150 | Common |
| ITM-FOD-01 | 통조림 | Medical | 1×1 | 0.4 | 에너지 +60 | 4 | 1 | 200 | Common |
| MED-ANT-01 | 해독제 | Medical | 1×1 | 0.1 | 방사능 해제 | 3 | 1 | 600 | Uncommon |
| GEAR-GSM-01 | 방독면 | Armor(Helmet, 0등급) | 2×2 | 0.8 | 폭풍 방사능 무효 | — | — | 1500 | Uncommon |
| ITM-REL-01 | 재의 군주 인장 | Item | 1×1 | 0.2 | (판매용) | — | — | 8000 | Relic |

### 3.8 레시피 (`CraftingConfigSO`, Data/Hideout/CraftingConfig.asset) — 전부 TODO(GDD-?) ⟨Q47⟩
| id | 산출 | 재료(보관함) |
|---|---|---|
| RCP-AMMO-9MM12 | MAG-9MM-12 (가득) × 1 | JNK-01 파이프 × 1 + JNK-03 전선 × 1 |
| RCP-AMMO-556 | MAG-556-30 (가득) × 1 | JNK-01 × 2 + JNK-03 × 1 |
| RCP-AMMO-762 | MAG-762-5 (가득) × 1 | JNK-01 × 1 + JNK-02 엔진 부품 × 1 |
| RCP-AMMO-12GA | BOX-12GA-8 × 1 | JNK-01 × 1 |
| RCP-MED-BND | MED-BND-01 × 2 | JNK-03 × 1 |
| RCP-MED-TRQ | MED-TRQ-01 × 1 | JNK-03 × 2 |
| RCP-MED-SPL | MED-SPL-01 × 1 | JNK-01 × 1 |
| RCP-MED-ANT | MED-ANT-01 × 1 | JNK-02 × 1 + JNK-03 × 1 |
(가치 확인: 재료 매도가 ≈ 산출 매도가 — 제작이 무한 탄피 생성기가 되지 않게. 데이터 테스트로 "산출 매도 합 ≤ 재료 매수 합"을 확인한다.)

### 3.9 루트 테이블 (전부 TODO(GDD-?))
- LT-CRATE에 추가(없으면만): 물병 2, 통조림 2, BOX-12GA-8 1.5, MAG-762-5 0.5, 해독제 0.5, 방독면 0.3, JNK-02 0.5.
- LT-SCV·LT-RDR에 추가: 물병 1, 통조림 1, BOX-12GA-8 0.5(SCV).
- LT-SNP(1~2회): MAG-762-5 3, 해독제 1, 물병 1. LT-MUT(0~2회): JNK-01 2, JNK-03 2, 해독제 0.3. LT-BOS(3~4회): ITM-REL-01 1(정확히 1회 보장 — 첫 굴림 고정 ⟨Q31⟩), 방독면 1, MAG-762-5 2, 해독제 1.

### 3.10 방사능 구역 (`RaidConfigSO.radiationZones`) — TODO(GDD-?) ⟨Q39⟩
| id | 중심 XZ | 반경 m | 초당 | 가장자리 감쇠 m |
|---|---|---|---|---|
| RAD-REF | (300, −60) (POI-REF 중심) | 70 | 0.5 | 15 |
구역 안 200초 → 선량 100. 폭풍과 겹치면 합.

### 3.11 희귀도 (전 아이템, 데이터만 + 7D UI 색) ⟨Q48·Q49⟩
고철(Scrap) = JNK-01~03 / 일반 = 9mm·12ga 탄창·상자, 붕대, 물·음식, 지도, 조끼 / 우수 = SMG, 샷건, 5.56·7.62 탄창, 지혈대·부목, 신호탄, 배낭, 헬멧, 해독제, 방독면, 권총 / 희귀 = 소총, DMR, 방탄복 / 유물 = ITM-REL-01.
내장 튜브·적 전용 정의 = Scrap(표시 안 됨). 전부 TODO(GDD-?).

---

## 4. 파일·클래스·공개 API

경로는 `Assets/_Project/Scripts/` 기준. [7A] 등은 소유 하위 단계. "계약" = 그 하위 단계 1단계에서 원본에 만드는 것.

### 4.1 Core (`Ashfall.Core`)
- [7A 계약] `Data/WeaponDefinitionSO.cs` 필드: `MagazineDefinitionSO internalMagazine`, `float roundReloadTimeSec`, `float pelletSpreadDeg`, `float durabilityLossPerShot`, `bool enemyOnly`. `Data/MagazineDefinitionSO.cs`: `bool isAmmoBox`.
- [7A 계약] `Items/WeaponItem.cs`: `float Durability { get; set; }`(0~100으로 자름, 기본 100), `bool IsJammed { get; set; }`, `bool HasInternalMagazine`. 생성자에서 `internalMagazine`이 있고 loaded가 null이면 빈 튜브.
- [7A 계약] `Items/ItemInstance.cs` `ItemFactory.Create`: 무기 = 내장 튜브 포함(위 생성자 규칙).
- [7A 계약] `Combat/JamRules.cs`(새, 순수): `static float Chance(float durability, float threshold, float chancePerPoint)`, `static bool Roll(float chance, System.Random)`.
- [7A 계약] `Combat/WeaponWear.cs`(새): `readonly struct WeaponWear { bool Enabled; float Threshold; float ChancePerPoint; System.Random Random; static WeaponWear None; }`.
- [7A 계약] `Combat/WeaponModel.cs`: 생성자 오버로드 `(WeaponItem, IMagazineSource, ILooseAmmoSource looseAmmo = null, WeaponWear wear = default)`. 추가 `bool IsJammed`, `bool IsTubeReload`, `int RoundsLoadedThisReload`,
  `bool TryFire(now, trigger)`(고장 굴림·마모 포함, 튜브 장전 중 사격 중단), `event Action Jammed`, `bool ClearJam()`, `bool CanReload`(튜브 = 낱탄 있음 + 빈 자리). 기존 시그니처·동작(마모 없음)은 그대로 — EnemyWeapon 무변경.
- [7A 계약] `Services/ILooseAmmoSource.cs`(새). `Combat/LocalMagazineSource.cs`: `ILooseAmmoSource` 구현(상자 = isAmmoBox 탄창), `Fits`에서 상자 제외.
- [7A 계약] `Save/SaveData.cs` `ItemRecord.durability = -1f`. `Save/SaveSerializer.cs`: `CurrentVersion = 2`, v1 이전, 튜브 복원(§1.6).
- [7A 계약] `Events/GameEvents.cs`: `WeaponClickKind`, `WeaponClicked`.
- [7B 계약] `Events/GameEvents.cs`: `EnemyVocalKind`, `EnemyVocalized`. (`Data/ItemEnums.cs` `WeaponClass.MachineGun`은 **7A-1b에서 추가됨**(끝에) — 7B는 다시 추가하지 않는다. WPN-LMG-01·MAG-762-100 데이터와 `M6AudioSetup` 파일 이름 규칙 `"Lmg"`도 7A에 있다.)
- [7C 계약] `Combat/BodyEnums.cs`: `SurvivalStat`, `SurvivalTier`(`StatusEffect`는 그대로). `Services/IBodyStatusView.cs`: `SurvivalTier GetSurvivalTier(SurvivalStat stat)`.
  `Services/IRadiationField.cs`(새). `Data/MedicalDefinitionSO.cs`: `float hydrationRestore`, `float energyRestore`, `bool curesRadiation`. `Data/ArmorDefinitionSO.cs`: `bool blocksStormRadiation`.
  `Services/IHideoutService.cs`: `Recipes`, `TryCraft`, `CraftingRecipeView { string Id; string DisplayName; ItemDefinitionSO Output; int OutputCount; IReadOnlyList<CraftingInputView> Inputs; }`, `CraftingInputView { ItemDefinitionSO Item; int Required; int Owned; }`, `CraftResult`.
- [7D] `Diagnostics/DevDiagnostics.cs`(새, MonoBehaviour — GameManager가 개발 빌드에서만 AddComponent), `Diagnostics/FrameTimeStats.cs`(순수 링 버퍼·중앙값·99%), `Diagnostics/SmokeRunner.cs`(명령줄 파싱 + 흐름 단계, 순수 단계 표).
  `Save/FileSaveStorage.cs`: 디렉터리 주입 생성자가 이미 없으면 추가(스모크·테스트용). `GameManager.cs`: 개발 빌드 진단 부착 한 줄 + 스모크 시 세이브 폴더 교체.

### 4.2 Weapons (`Ashfall.Weapons`) — 7A
- `WeaponController.cs`: 슬롯 교체(1/2/3), 무기별 모델 캐시, 뷰모델 프리팹, 탄약 표, 펠릿 퍼짐, T 길게(고장 해제)/짧게(탄창 확인), 튜브 장전, `WeaponClicked` 발행. 공개 읽기 `ActiveSlot`, `IsSwitching`, `Model`(현재), `JamClearProgress`(0~1, 테스트용).
  M1 씬(인벤토리 없음) 동작 유지.
- `WeaponSwitchModel.cs`(새, 순수): `ActiveSlot`, `Request(EquipSlot, Func<EquipSlot, bool> hasWeapon)`, `Tick(dt)`, `IsSwitching`, `PickInitial(...)`.
- `JamClearModel.cs`(새, 순수): `Tick(dt, held, jammed) → bool cleared`, `Progress`, `Reset()`.
- `Ballistics.cs`: `PelletDirections(forward, spreadDeg, count, Random, Span/배열 버퍼)`(할당 없는 버퍼 채우기).
- `MagazineCheckModel.cs`: TODO 주석 갱신만.
- `WeaponHandlingConfigSO.cs`: §3.3 필드.

### 4.3 Inventory (`Ashfall.Inventory`) — 7A
- `PlayerInventory.cs`: `ILooseAmmoSource` 구현(조끼 → 주머니 → 배낭(설정), 탄이 가장 적은 상자부터, 잠긴 것 제외, 사망 후 거부), `IsLoadable`에서 상자 제외. 브리지(`PlayerInventoryBehaviour`)가 `ILooseAmmoSource`도 등록/해제.

### 4.4 AI (`Ashfall.AI`) — 7B
- [계약] `AiEnums.cs`: `EnemyTacticsKind { Auto, Scavenger, Raider, Sniper, Mutant }`. `Data/EnemyArchetypeSO.cs`: §3.4 필드. `Data/AiConfigSO.cs`: `glintAngularSizeDeg`, `vocalCooldownSec`.
  `Brain/EnemyIntent.cs`: `bool Melee`, `bool Telegraph`. `Brain/EnemyBrain.cs`: 전술 팩토리(`CreateTactics(archetype)`), anchor 자르기(`TacticsContext.ClampToAnchor`).
  `Runtime/EnemyAgent.cs`: `ApplyMelee`·`ApplyTelegraph`·`EnemyVocalized` 발행 자리(빈 구현 + 호출) — 2단계는 이 파일을 고치지 않는다.
- [2a] `Brain/SniperTactics.cs`, `Runtime/SniperGlint.cs`(섬광 컴포넌트: `SetActive(bool)`, `Face(Vector3 eye)`).
- [2b] `Brain/MutantTactics.cs`, `Combat/EnemyMelee.cs`(순수: `Tick(now, inRange, hasLos) → MeleeEvent { None, WindupStarted, Strike, Whiff }`, `PickStrikeHeight(Random, capsuleHeight)`).
- [2b] `Combat/CorpseLootBuilder.cs`: enemyOnly 무기·탄창 제외. `Combat/EnemyWeapon.cs`: 스폰 내구도(`weaponDurabilityRange`).

### 4.5 Player (`Ashfall.Player`) — 7C
- `SurvivalModel.cs`(새, 순수): `Hydration`·`Energy`(internal 읽기), `Tick(dt, stomachDestroyed)`, `Restore(hydration, energy)`, `GetTier(SurvivalStat)`, `IsDehydrated`, `IsStarving`, `event Action TierChanged`.
- `RadiationDoseModel.cs`: `IsSick(threshold)`(기존 `Tick`·`Reset` 유지 — M6 테스트 불변).
- `BodyHealthModel.cs`: `MaxHpMultiplier { get; set; }`(현재 HP 자름, 등급 재계산), `ApplySystemicDamage(float perPart)`(파괴 부위 제외, 사망 판정 포함), `GetSurvivalTier`(생존 뷰 위임 — 생성 후 `AttachSurvival(SurvivalModel, RadiationDoseModel)`).
- `MedicalUseModel.cs`: 전신 효과 판정(대상 없음 → 필요 여부), 완료 시 Restore·Reset.
- `PlayerHealth.cs`: 생존 Tick, 방사능률 합(방독면·구역), 피폭 → 최대 HP 배율. `PlayerMotor.cs`: 스태미나 재생 배율(에너지 0). `StaminaModel.cs`: `Tick(dt, sprinting, moving, regenMultiplier = 1)`.
- `PlayerConfigSO.cs`: §3.6 필드(1단계 계약).

### 4.6 Raid (`Ashfall.Raid`) — 7C
- `Data/RaidConfigSO.cs`: `List<RadiationZoneConfig> radiationZones`(계약). `Data/RadiationZoneConfig.cs`(새, `[Serializable]`).
- `Logic/RadiationZoneMath.cs`(새, 순수). `Runtime/RadiationField.cs`(새: `IRadiationField` 등록, 구역 = `RaidController`의 config 또는 직렬화 config).

### 4.7 Hideout (`Ashfall.Hideout`) — 7C
- `Data/CraftingConfigSO.cs`(새, 계약: `CraftingRecipe { id, displayName, inputs: CraftingIngredient[], output, outputCount }`). `Logic/CraftingModel.cs`(새, 순수).
- `Logic/HideoutService.cs`: `Recipes`·`TryCraft`(+ `HideoutChanged`). `Runtime/HideoutController.cs`: `CraftingConfigSO` 직렬화 참조.

### 4.8 UI (`Ashfall.UI`) — 7C(제작·부위 상태), 7D(희귀도 색)
- `HideoutPresenter.cs`: `HideoutTab.Crafting`, `Craft(recipeId)`, 결과 문구. `CraftingViewModel.cs`(새, 순수), `CraftingView.cs`(새). `HideoutHubView.cs`: 탭 버튼. `Art/UI/HideoutUI.uxml`·`Ashfall.uss`: 제작 탭.
- `BodyStatusPresenter.cs`: "전신" 줄(`SurvivalRow`), `BodyStatusScreenView.cs`. `UiText.cs`: 문구.
- [7D] `RarityStyle.cs`(새, 순수: Rarity → USS 클래스), 인벤토리·보관함·상인 칸에 클래스 추가, `Ashfall.uss` 희귀도 테두리(고철 Ash_Grey, 일반 Bone, 우수 Toxic, 희귀 청색 USS 상수 1개(팔레트 밖), 유물 Ember 발광 → ⟨Q49⟩).

### 4.9 Audio (`Ashfall.Audio`)
- [7A] `Data/AudioConfigSO.cs`: `dryFireClip`, `jamClearClip`, 기본 `gunshots`에 Shotgun·Sniper. `Runtime/AudioDirector.cs`: `WeaponClicked` 구독·재생(플레이어 자신 3D, 우선순위 = 내 발소리보다 높음).
- [7B] `AudioConfigSO`: `mutantAlertClips`, `mutantAttackClips`, gunshots에 MachineGun. `AudioDirector`: `EnemyVocalized`(들리는 거리 `vocalAudibleM` 60, 우선순위 = 적 발소리보다 높음).
- [7C] `AudioDirector`/`AmbientSoundscape`: 가이거 입력 = 폭풍 률 + `IRadiationField.ZoneRatePerSec(청취자)`.

### 4.10 EditorTools (`Ashfall.EditorTools`)
- [7A] `M7Names.cs`(계약: 경로·오브젝트 이름 — 7B·7C도 여기 추가만, 각 계약 단계에서), `M7WeaponDataSetup.cs`, `M7WeaponAssets.cs`(뷰모델 프리팹), `M6AudioSetup.cs`·`SfxSynth.cs`(레시피·클래스 추가), `M7SceneSetup.cs`(통합 메뉴 — 7A가 만들고 7B·7C가 한 줄씩 추가), `M4SceneSetup.cs` 한 줄.
- [7B] `M7AiDataSetup.cs`, `M7EnemyLayout.cs`, `M7EnemySceneApply.cs`, `PaletteMaterialMap.cs`(Mutant·Boss·SniperGlint 키).
- [7C] `M7SurvivalDataSetup.cs`(아이템·레시피·루트·카탈로그·희귀도), `M7HazardLayout.cs`, `M7HazardSceneApply.cs`, `PaletteMaterialMap.cs`(Toxic 키), `M5DataSetup.cs`(ItemDatabase 재수집 호출만 공개).
- [7D] `BuildTools.cs`(개발·IL2CPP 메서드), `Assets/_Project/link.xml`.

### 4.11 입력·설정
- [7A 계약] `Assets/InputSystem_Actions.inputactions`(JSON 텍스트): Player 맵에 `Weapon1`(`<Keyboard>/1`), `Weapon2`(`/2`), `Weapon3`(`/3`) Button. §12.1 키 표 그대로.
- [7D-IL2CPP, 설치 뒤] `ProjectSettings/ProjectSettings.asset` 스크립팅 백엔드 — 에디터 API(`PlayerSettings.SetScriptingBackend`)로 메뉴가 바꾼다(YAML 손작성 금지).

---

## 5. GDD에 없거나 모호한 질문 (오케스트레이터가 ★추천안을 그대로 채택)

| # | 질문 | 선택지 | ★추천 |
|---|---|---|---|
| **7A 무기** | | | |
| Q1 | 무기 교체 1/2/3 규칙 | A 1/2/3 = Primary1/Primary2/Secondary, 든 슬롯·빈 슬롯 키 무시, 교체 0.5초(사격·조준·재장전 불가), 교체 시 재장전·탄창 확인 취소 / B 즉시 교체 / C 같은 키 재입력 = 무기 집어넣기 | **A** |
| Q2 | 레이드 시작 무기 | A Primary1 → Primary2 → Secondary 첫 비지 않은 슬롯, 든 슬롯 저장 안 함 / B 항상 Primary1 | **A** — 권총만 든 키트 반입도 바로 쏜다 |
| Q3 | 무기 → 탄약 정의 찾기 | A `WeaponHandlingConfigSO.ammoDefinitions`(AmmoType별 1개) / B `WeaponDefinitionSO`에 탄약 SO 필드 추가 | **A** — Core 스키마 그대로, 구경마다 탄 1종(MVP) |
| Q4 | 무기별 1인칭 모델 | A EditorTools 자리표시 프리팹 → §14 `viewModel` 필드, 처음 들 때 생성·캐시 / B 공용 상자 하나 | **A** — 손에 든 무기를 구분해야 교체를 확인할 수 있다 |
| Q5 | 샷건 튜브(탄창 아이템 아님) 표현 | A 내장 탄창 정의(`internalMagazine`, 그리드에 없음) + 낱탄 = 탄 상자(`isAmmoBox` 탄창), R = 한 발씩 0.5초 / B 6발 탄창 아이템으로 단순화 / C 낱탄 스택 아이템 새 체계 | **A** — 기존 Magazine·세이브·RULE-04 재사용, GDD "튜브" 유지 |
| Q6 | 빈 탄 상자 | A 0발 아이템으로 남음(빈 탄창과 같음) / B 자동 삭제 | **A** |
| Q7 | 튜브 장전 중 사격 | A 튜브에 1발 이상이면 장전 중단 후 사격 / B 장전 끝날 때까지 불가 | **A** |
| Q8 | 산탄 퍼짐 | A 펠릿마다 `pelletSpreadDeg` 원뿔(조준 중에도), 지향 퍼짐은 묶음 방향에만 / B 지향 퍼짐만(현재 — 모든 펠릿 한 방향) | **A** — 현재 동작은 결함 |
| Q9 | 고장 순간·고장 중 동작 | A 탄 소모 없이 발사 안 됨, 고장 중 방아쇠 = 헛격발 소리, 재장전·탄창 확인 불가, T 1.2초 계속 누름 = 해제(떼면 처음부터) / B 고장 = 탄 1발 버림 / C 재장전으로도 해제 | **A** — "T 길게"만 해제 |
| Q10 | 고장 알림 | A 소리만(`WeaponClicked`), HUD 없음 / B HUD 문구 | **A** — §12 HUD 최소 |
| Q11 | 내구도 감소 규칙 | A 발당 무기별 고정값(`durabilityLossPerShot`) / B 탄종·연사 열 / C 시간 | **A** |
| Q12 | 내구도 초기값 | A 상인·제작·키트·상자 100, 적 무기는 유형별 범위 무작위(시체 무기는 고장 가능) / B 전부 100 | **A** — 고장이 실제로 나타나는 경로 |
| Q13 | 내구도 저장·세이브 버전 | A `ItemRecord.durability` + version 2, v1 → 무기 100 이전 / B 저장 안 함(매 로드 100) | **A** |
| Q14 | 고장 상태 저장 | A 안 함(은신처 귀환 = 해제된 것으로) / B 저장 | **A** |
| Q15 | 상인 가격에 내구도 반영 | A 안 함(M5 Q10과 같음) / B 매도가 × 내구도 | **A** |
| Q16 | 수리 | A 없음(새로 산다) / B 상인 수리·제작 수리 키트 | **A** — GDD에 없음, §10 목록 |
| Q17 | 내구도 표시 | A 없음 / B 인벤토리 대략치 문구 | **A** — 정보의 희소성(기둥 2), §10 목록 |
| Q18 | 적 무기 마모·고장 | A 없음(스폰 내구도만) / B 플레이어와 같음 | **A** — 적 AI에 고장 처리 행동이 필요해짐 |
| Q19 | §5.3 방어구 관통·둔탁 피해(어느 AC에도 없고 미구현) | A M7 7A에 플레이어 방어구만 추가 / **B M7 범위 밖, MVP 남은 목록(§10)** | **B** — M7 AC 밖, 범위를 늘리지 않는다 |
| Q20 | 상인 판매 목록·전술 필드 | A SHT·DMR·MAG-762-5·BOX-12GA-8 추가(LMG·튜브 제외), 적 전술은 `EnemyTacticsKind`(Auto = 기존 usesCover 규칙) / B 판매 안 함 | **A** |
| Q21 | 약실 탄 +1 (M1 TODO) | A 없음(M1 유지) / B 추가 | **A** |
| **7B AI** | | | |
| Q22 | 저격수 행동 | A 퍼치 고정(이동 없음), 시야 150m·사거리 200m, 소리 → 돌아봄만, 놓치면 자리에서 훑기 / B 사격 후 자리 옮김 | **A** — "고지대 고정" |
| Q23 | "레이저 반사 신호" 표현 | A 조준 중에만 켜지는 렌즈 섬광 쿼드(발광, 거리 비례 크기, 표적 쪽), 처음·다시 볼 때 1.5초 경고 후 사격 / B 레이저 빔(LineRenderer) / C 둘 다 | **A** — 경고를 주되 위치만 드러냄 |
| Q24 | 변이체 근접 | A 사거리 1.8m·준비 0.3초·간격 1.1초·피해 14, 기존 `IDamageable`, 부위 = 흉부·복부·팔 높이 무작위 지점(기존 판정) / B 부위 고정(흉부) | **A** |
| Q25 | "무리 행동" | A 분대 연락(기존 50m) + 표적 둘레 분산 접근(순번별 각도) / B 분대 연락만 | **A** |
| Q26 | 변이체 소리 | A `EnemyVocalized`(첫 경계·타격, 쿨다운 4초) + 자리표시 울음·공격 / B 소리 없음(발소리만) | **A** — 기둥 3 |
| Q27 | 경기관총(플레이어 5종 밖) | A 적 전용 정의 WPN-LMG-01(`enemyOnly`, 시체·상인·루트 제외), `WeaponClass.MachineGun` 추가 / B 시체에서 루팅 가능(6번째 무기) / C 보스도 소총 | **A** |
| Q28 | 호위 3명 | A 새 유형 AI-BGD(RDR 값 + anchor, flanks 끔) / B AI-RDR 재사용 | **A** — 기존 RDR 수·테스트 불변 |
| Q29 | 고정 거점 | A 보스·호위 `anchorRadiusM` 25, 마을 외곽 기존 구조물·CoverPoint, 새 구조물·NavMesh 재굽기 없음 / B 새 요새 블록아웃 + 재굽기 | **A** |
| Q30 | 배치 수 | A 기존 20 유지 + SNP 2 + MUT 4×2 + BOS 1 + BGD 3 = 34 / B 일부를 바꿔 20 유지 | **A** — 기존 배치·테스트 보존, 성능은 7D에서 34명 판정 |
| Q31 | 보스 루트 | A LT-BOS: 유물 ITM-REL-01 1개 보장 + 방독면·7.62 탄창·해독제 / B 일반 테이블 | **A** |
| **7C 생존·제작** | | | |
| Q32 | 수분·에너지·선량 이월 | A 안 함(레이드마다 100/100/0, M5 Q18) / B 프로필에 저장·이월 | **A** — 세이브 변경 없음 |
| Q33 | "수분 0 → 전신 −0.5 HP/s" | A 파괴되지 않은 부위마다 −0.5/s(흉부 80 → 160초 뒤 사망) / B 합계 0.5를 부위에 나눔 | **A** — 문구 그대로 |
| Q34 | 복부 파괴 "탈수 가속" | A 수분 감소 × 2.0 / B 탈수 피해 × 2 | **A** |
| Q35 | 방사능 100 효과 | A 모든 부위 최대 HP × 0.8(현재 HP는 새 최대로 자름), 선량은 계속 쌓이되 추가 효과 없음 / B 흉부만 | **A** |
| Q36 | 해독제 | A 선량 0 + 최대 HP 원래대로(HP 회복 없음), 선량 > 0일 때만 사용 / B 선량 −50 | **A** |
| Q37 | 방독면 장착 위치 | A 헬멧 슬롯 공유(`ArmorDefinitionSO.blocksStormRadiation`, 헬멧과 동시 불가) / B 새 슬롯 `FaceMask`(UI·Loadout·세이브 슬롯 추가) | **A** — §6 슬롯 목록 그대로 |
| Q38 | 방독면이 막는 것 | A 재 폭풍 방사능만(§8.3 문구), 구역 방사능은 못 막음, 필터 소모 없음 / B 둘 다 | **A** |
| Q39 | 정유소 구역 | A 원형 반경 70m·0.5/s·가장자리 15m 선형, RaidConfigSO 목록 / B 상자 트리거 | **A** |
| Q40 | Toxic 표시 | A 구역 둘레 경고 기둥·드럼통 자리표시(Toxic 머티리얼, 콜라이더 없음), 지도 표시 없음 / B 지도에도 / C 바닥 데칼 | **A** — 지도는 RULE-06 정보 최소 |
| Q41 | 가이거 반응 | A 환경 방사능률 = 폭풍 + 청취자 위치 구역(방독면 무관) / B 플레이어가 실제로 받는 률 | **A** — 계수기는 환경을 잰다 |
| Q42 | 물·음식·해독제 데이터 | A `MedicalDefinitionSO`에 hydration/energy/curesRadiation 필드(의약품 흐름·퀵슬롯·세이브 재사용, 전신 대상 = Thorax 표시) / B 새 `ConsumableDefinitionSO`·`ConsumableItem` | **A** |
| Q43 | 부위 상태 화면(H) 표시 | A "전신" 줄 2단계 문구(목마름/탈수, 배고픔/허기, 방사능 오염(≥50)/피폭(≥100)), Low = 25% / B 0일 때만 / C 막대 | **A** — RULE-05 수치 비표시 유지, 미리 알 수 있게 |
| Q44 | 제작 재료 범위 | A 보관함만 / B 보관함 + 은신처 장비 | **A** — 상인 매도와 같음 |
| Q45 | 제작 시간·비용 | A 즉시, 탄피 비용 없음 / B 실제 시간 대기 / C 탄피 비용 | **A** |
| Q46 | 제작 "탄약" 산출물 | A 가득 찬 탄창(9mm 12·5.56 30·7.62 5) + 12ga 탄 상자 / B 모든 구경 탄 상자 + 탄창 채우기 기능 | **A** — 낱탄 체계를 만들지 않는다 |
| Q47 | 레시피 내용 | A §3.8 표(임시, 산출 매도 ≤ 재료 매수) / B 사용자 확정 대기 | **A** |
| Q48 | "고철" 정의·희귀도 | A 전 아이템 희귀도 표(§3.11) 지정, 고철 = JNK-01~03, 레시피는 아이템 id로 지정 / B 희귀도 그대로(거의 전부 Scrap) | **A** — 데이터만 |
| **7D 성능·빌드·폴리시** | | | |
| Q49 | §6 희귀도 색(미구현) | A 칸 테두리 USS(고철 Ash_Grey·일반 Bone·우수 = Toxic 토큰 재사용·희귀 = 청색 USS 상수 1개(팔레트 밖, TODO)·유물 Ember 발광) / B 안 함 | **A** — §6 문구(녹·청은 §6 색) |
| Q50 | 성능 판정 | A 배치모드: 34명 + 폭풍 Peak + 변이체 무리 돌진 + 보스 LMG에서 기존 예산 유지, 60fps는 사람이 개발 빌드 1080p로 / B 예산 완화 | **A** |
| Q51 | 개발 빌드 진단 | A 개발 빌드에만 프레임 시간 로그 + `-ashfallSmoke <폴더>` 스모크(세이브 폴더 교체, 종료 코드) / B 없음(사람 확인만) | **A** — IL2CPP 설치 뒤 자동 확인 경로 |
| Q52 | IL2CPP 전환 시점 | A 지금은 Mono 검증 + IL2CPP 메서드(모듈 없으면 명확한 실패), 설치 확인 뒤 ProjectSettings 영구 전환·커밋(7D-IL2CPP) / B 지금 전환(빌드 불가) | **A** |
| Q53 | 코드 스트리핑 대비 | A `link.xml`로 Ashfall.* preserve all, Stripping Minimal / B 없음(문제 생기면) | **A** |

(Q19는 범위를 늘리지 않는 쪽이 단순해 B, 나머지는 A.)

---

## 6. 작업 단계

규칙(M3~M6과 같음): **원본에서 배치모드 Unity는 한 번에 하나**(계약·통합·검증·리뷰 수정 — 순차). 병렬 단계는 계약 완료 상태를 scratchpad로 복사(`Assets`·`Packages`·`ProjectSettings`·`Library`)한 **사본에서만** 돌리고 원본에는 자기 소유 파일만 쓴다(에셋·씬은 원본에 만들지 않음 — 사본에서 메뉴 실행).
각 단계는 이 문서 끝에 "7X-n 구현 메모"를 덧붙인다. 새 `.meta`는 만들지 않는다. 테스트는 실제 persistentDataPath를 쓰지 않는다(`MemorySaveStorage`·`SaveSandbox`). **하위 단계마다 마지막에 원본 EditMode·PlayMode(2회)·Mono 빌드 통과 → 한국어 커밋.**
`TestData.cs`·`M3~M5TestData.cs`·`UiTestFakes.cs`·Core·`M7Names.cs`·데이터 메뉴는 병렬 단계에서 아무도 고치지 않는다(계약 변경은 오케스트레이터 경유). 가짜 서비스가 더 필요하면 각 테스트 파일 안에 둔다.

### 하위 단계 의존·병렬 판단

| 관계 | 판단 |
|---|---|
| 7A → 7B | **순차 필수.** 7B가 WPN-DMR-01·AMMO-762·`enemyOnly`·`WeaponModel` 변경·총성 세트 확장 방식(LMG 세트)·`WeaponClass` 사용을 쓴다. |
| 7A → 7C | 코드상 대부분 독립. 같은 파일: `GameEvents.cs`(7A만 추가), `ItemRecord`/`SaveSerializer`(7A만), `AudioDirector.cs`(7A 클릭, 7C 가이거), `TraderConfig`·`LT-CRATE` 에셋(둘 다 추가), `PaletteMaterialMap.cs`, `M7Names.cs`·`M7SceneSetup.cs` → **7A 커밋 뒤 시작.** |
| 7B ∥ 7C | **기본은 순차(7B → 7C).** 겹치는 파일: `AudioDirector.cs`·`AudioConfigSO.cs`(7B 울음, 7C 가이거), 레이드 씬, `PaletteMaterialMap.cs`, `M7Names.cs`·`M7SceneSetup.cs`, `M5DataSetup` ItemDatabase, LT-SCV/RDR(7C 추가). |
| 선택 가속 | 7B 계약(7B-1) 커밋 뒤 **7C-1 계약을 원본에서 이어서** 하고, 7C의 순수 로직 병렬(7C-2a Player·7C-2b Hideout/UI)을 7B-2와 같은 시간에 사본에서 돌려도 된다(새 파일·Player/Hideout/UI만 — 7B와 겹침 없음). 7C-2c(Raid·Audio·EditorTools·씬)와 7C 통합은 7B 커밋 뒤. 커밋 순서는 7B → 7C. 절약 약 1.5~2시간, 대신 오케스트레이터가 사본 5개를 관리. |
| 7D | 7A·7B·7C 커밋 뒤(최악 상황 판정에 전부 필요). 희귀도 UI(Q49)만은 7C 뒤 언제든. 7D-IL2CPP는 사용자 설치 확인 뒤 별도. |

### 7A — 무기 (예상 5~6시간)
**7A-1 계약 (gameplay-programmer, 원본) — 60~75분**
- Core §4.1 [7A] 전부 **구현 포함**(WeaponModel 튜브·마모·고장, JamRules, WeaponItem, ItemFactory, LocalMagazineSource, ILooseAmmoSource, SaveSerializer v2·이전, GameEvents), `WeaponHandlingConfigSO` 필드, 입력 액션 3개, `M7Names.cs`.
- 테스트(새): `EditMode/JamRulesTests`(경계 50·0·100, 확률식, 시드 굴림 빈도), `WeaponModelWearTests`(발당 마모, 고장 = 탄 소모 없음·발사 없음, 고장 중 재장전 거부, ClearJam, 마모 옵션 없음 = 기존과 같음), `TubeReloadTests`(한 발씩·가득·낱탄 없음·사격 중단·빠른 재장전 없음·RULE-04 분류),
  `LooseAmmoSourceTests`(LocalMagazineSource: 상자만, 가장 적은 상자부터, `Fits`에서 상자 제외), `SaveSerializerV2Tests`(v2 왕복 durability, v1 이전 = 100, 튜브 복원·빈 튜브, 잘못된 값 자르기, v3 = 지원 안 함). 기존 `SaveSerializerTests`의 버전 단언만 2로.
- 마감: 원본 EditMode 전체 통과(기존 단언 변경은 버전 1 → 2만), PlayMode는 7A-1b 뒤.
**7A-1b 데이터 (data-designer, 원본) — 30~40분**
- `M7WeaponDataSetup.cs`: §3.1·§3.2·§3.3 에셋, 기존 무기 3종 `durabilityLossPerShot`(없을 때만), 상인 목록·LT-CRATE 추가(없으면만), ItemDatabase 재수집. 메뉴 2회 → 바이트 동일.
- 테스트: `EditMode/M7WeaponDataAssetTests`(GDD 값: RPM 70/40, 피해 18/110, 펠릿 8, 튜브 6, 탄창 5, 분류·탄 종류, 고장 임계·확률, 탄약 표 4종, LMG enemyOnly·상인/루트에 없음, 재실행 동일). PlayMode 전체 통과.
**7A-2 병렬 (사본)**
| 단계 | 담당 | 소유 파일 | 테스트 | 예상 |
|---|---|---|---|---|
| **2a** 플레이어 무기 | gameplay-programmer | `Weapons/*`(WeaponController·WeaponSwitchModel·JamClearModel·Ballistics·MagazineCheckModel), `EditorTools/M7WeaponAssets.cs` | EditMode `WeaponSwitchModelTests`·`JamClearModelTests`·`PelletSpreadTests`(펠릿 8개 각기 다른 방향, 원뿔 안, 조준 시 묶음 방향 = 정면), PlayMode `WeaponSwitchFlowTests`(M2 확인 씬 방식 + 코드로 장착: 1/2/3 교체·교체 시간 중 사격 불가·재장전 취소·빈 슬롯 무시·권총·SMG·소총·샷건·DMR 각 1발 = `ShotFired` 정의·투사체 수(샷건 8)·`NoiseEmitted` 1), `JamFlowTests`(내구도 0 + 시드 → 고장·`WeaponClicked(Jammed)`·헛격발·T 짧게 = 해제 안 됨·1.2초 = 해제·탄 수 보존), `TubeReloadFlowTests`(상자 2개 → 6발, 사격으로 중단) | 90~110분 |
| **2b** 인벤토리 낱탄 | gameplay-programmer(두 번째) | `Inventory/PlayerInventory.cs`·`PlayerInventoryBehaviour.cs` | EditMode `PlayerInventoryLooseAmmoTests`(조끼 → 주머니 → 배낭 순, 적은 상자부터, 잠김·사망 거부, 상자로 일반 재장전 안 됨) | 30~40분 |
| **2c** 오디오 | gameplay-programmer(세 번째) | `Audio/*`(AudioConfigSO·AudioDirector), `EditorTools/M6AudioSetup.cs`·`SfxSynth.cs` | EditMode `AudioConfigAssetTests` 확장(세트 5개·클릭 2개), `SfxSynthTests`(새 레시피), PlayMode `AudioFlowTests` 확장(`WeaponClicked` → 재생, 샷건·DMR `ShotFired` → 해당 세트) | 40~50분 |
- 접점: 2a 테스트는 탄 상자를 `LocalMagazineSource`(계약)로, 2b는 Core만. 2c는 `WeaponClicked`(계약)만.
**7A-3 통합 (gameplay-programmer, 원본) — 40~50분**: `M7SceneSetup.ApplyAll`(7A 몫: 데이터 → 프리팹 → 오디오 WAV·설정) + `M4SceneSetup` 한 줄, 원본 메뉴 2회(바이트 동일), PlayMode `M7WeaponSceneSmokeTests`(레이드 씬 Player에 무기 컨트롤러·탄약 표·프리팹 연결). EditMode·PlayMode 전체.
**7A-4 검증 (test-engineer) — 50~60분**: PlayMode `M7WeaponRaidFlowTests`(은신처 → 키트 권총만 반입 → 3으로 사격 가능(M5 Q16 해소) → 샷건·DMR 장착 반입 → 탈출 → 은신처 내구도 감소 값이 저장·다시 읽힘, v1 세이브 파일을 SaveSandbox에 두고 진입 → v2로 다시 저장), Mono 빌드, PlayMode 2회.
**7A-5 리뷰 + 수정 — 45분** → 커밋 `M7A: 무기 5종·교체·고장/내구도 — 펌프 샷건·볼트액션, save v2 (SYS-WPN, WPN-SHT-01, WPN-DMR-01, §5.1)`.

### 7B — AI (예상 5.5~6.5시간)
**7B-1 계약 (ai-programmer, 원본) — 50~60분**: Core [7B](EnemyVocalized — `WeaponClass.MachineGun`·WPN-LMG-01·MAG-762-100은 7A-1b에서 추가됨, 다시 추가하지 않음), AI 계약(§4.4 [계약] — 팩토리·anchor·Intent 필드·EnemyAgent 훅·SO 필드), `M7Names` 추가.
- 테스트: `EditMode/EnemyTacticsFactoryTests`(Auto = 기존 규칙, 명시 종류), `AnchorTests`(목적지 자르기, 0 = 무제한), 기존 `EnemyBrainTests` 무변경 통과.
**7B-1b 데이터 (data-designer) — 40~50분**: `M7AiDataSetup.cs`(§3.4 유형 4개, LMG·탄창(7A-1b에서 추가됨 — 만들지 않고 참조만), LT-SNP/MUT/BOS, ITM-REL-01, 기존 SCV·RDR `weaponDurabilityRange`·`tactics`(Auto 유지)), `M7EnemyLayout.cs`(§3.5, 스폰 거리·높이 검사 함수). 테스트 `M7AiDataAssetTests`.
**7B-2 병렬 (사본)**
| 단계 | 담당 | 소유 파일 | 테스트 | 예상 |
|---|---|---|---|---|
| **2a** 저격수 | ai-programmer | `AI/Brain/SniperTactics.cs`, `AI/Runtime/SniperGlint.cs` | EditMode `SniperTacticsTests`(이동 의도 없음, 경고 1.5초 전 사격 없음, 다시 보면 경고 재시작, 놓치면 훑기), PlayMode `SniperFlowTests`(코드 씬: 120m 서 있는 표적 → 섬광 켜짐·표적 쪽·1.5초 뒤 `ShotFired`, 폭풍 cap 30 → 못 봄·섬광 꺼짐, 위치 불변) | 70~90분 |
| **2b** 변이체 + 시체 | ai-programmer(두 번째) | `AI/Brain/MutantTactics.cs`, `AI/Combat/EnemyMelee.cs`·`CorpseLootBuilder.cs`·`EnemyWeapon.cs` | EditMode `EnemyMeleeTests`(사거리·준비·간격·헛침·높이 분포), `MutantTacticsTests`(둘레 오프셋 서로 다름, 질주, 엄폐 없음), `CorpseLootBuilderTests` 확장(enemyOnly 제외), `EnemyWeaponTests` 확장(스폰 내구도 범위), PlayMode `MutantFlowTests`(무리 4 → 한 명 발견 = 전원 Combat, 표적 피해(`IDamageable`)·간격, `EnemyVocalized`, 표적 사망 후 공격 없음, GC 0) | 80~100분 |
| **2c** 배치·오디오·머티리얼 | gameplay-programmer | `EditorTools/M7EnemySceneApply.cs`·`PaletteMaterialMap.cs`, `Audio/*`(울음·LMG), `EditorTools/M6AudioSetup.cs`·`SfxSynth.cs` | PlayMode `M7EnemySceneSmokeTests`(사본 씬: 34명, 유형별 수, NavMesh 위, 스폰 거리 ≥ 190, 섬광 자식, 머티리얼 = 팔레트), `AudioFlowTests` 확장(울음·LMG 세트), `PaletteConsistencyTests` 자동 포함 | 60~75분 |
- 접점: 2a·2b는 계약의 Intent 필드·EnemyAgent 훅만. 2c는 2a의 섬광 컴포넌트 이름(`M7Names.SniperGlintObject`)만 알고, 사본에 2a가 없으면 빈 오브젝트 + 경고.
**7B-3 통합 (원본) — 50~60분**: `M7SceneSetup`에 7B 한 줄, 메뉴 2회, 기존 테스트 중 적 총수 단언 갱신(`CrimsonBasinLayout.ExpectedEnemyCount`는 "M4 고정 배치 20"으로 의미 유지 — 씬 총수 단언은 20 + `M7EnemyLayout.Count`로), PlayMode 전체.
**7B-4 검증 — 60~75분**: `M7AiRaidFlowTests`(레이드 씬: 보스 처치 → 시체에 LMG 없음·유물 있음, 호위 anchor 25m 안 유지, 저격수 처치 → DMR(내구도 범위) 루팅 후 쏠 수 있음), 기존 `RaidPerformanceTests`(34명 기준 재측정 — 실패 시 7D 전에 보고), Mono 빌드.
**7B-5 리뷰 + 수정 — 45분** → 커밋 `M7B: AI-SNP·AI-MUT·AI-BOS, Crimson Basin 배치·루트 (SYS-AI)`.

### 7C — 생존·제작 (예상 5.5~6.5시간)
**7C-1 계약 (gameplay-programmer, 원본) — 50~60분**: Core [7C], `PlayerConfigSO`·`RaidConfigSO`·`CraftingConfigSO` 필드, `IHideoutService`·`IBodyStatusView` 변경에 맞춰 **테스트 가짜**(`FakeHideoutService`·`FakeBodyStatusView`) 갱신, `M7Names` 추가.
- 테스트: `EditMode/RadiationZoneMathTests`(중심 최대·가장자리 선형·밖 0·겹침 합), 기존 전체 통과.
**7C-1b 데이터 (data-designer) — 35~45분**: `M7SurvivalDataSetup.cs`(§3.6~§3.11: 아이템·레시피·루트 추가·상인 목록(물·음식·해독제·방독면·탄 상자)·희귀도·구역), 테스트 `M7SurvivalDataAssetTests`(GDD 값 100/0.1/0.5/100/0.07/0.5/100/0.8, 레시피 산출 매도 ≤ 재료 매수, 방독면 = Helmet·blocksStormRadiation, 희귀도 표).
**7C-2 병렬 (사본)**
| 단계 | 담당 | 소유 파일 | 테스트 | 예상 |
|---|---|---|---|---|
| **2a** 생존(Player) | gameplay-programmer | `Player/*`(SurvivalModel·RadiationDoseModel·BodyHealthModel·MedicalUseModel·PlayerHealth·PlayerMotor·StaminaModel) | EditMode `SurvivalModelTests`(감소율, 복부 ×2, 0에서 멈춤, 등급 경계, Restore 상한), `BodyHealthSystemicTests`(부위마다 0.5/s·파괴 부위 제외·흉부 0 = 사망, 최대 HP ×0.8 자르기·등급), `MedicalSurvivalTests`(물·음식·해독제 필요 여부·효과), `StaminaModelTests` 확장(재생 배율), 기존 `RadiationDoseTests` 무변경, PlayMode `SurvivalFlowTests`(가짜 IAshStormInfo·IRadiationField: 방독면 착용 → 폭풍 률 0·구역 률 그대로, 선량 100 → 최대 HP 감소, 해독제 → 복구, 수분 0 → 체력 감소, 에너지 0 → 스태미나 재생 절반, 등급 변화 = `PlayerBodyStatusChanged`) | 90~110분 |
| **2b** 제작·UI | ui-programmer | `Hideout/*`(CraftingModel·HideoutService·HideoutController), `UI/*`(제작 탭·부위 상태 전신 줄·UiText), `Art/UI/HideoutUI.uxml`·`GameUI.uxml`·`Ashfall.uss` | EditMode `CraftingModelTests`(재료 부족, 보관함만, 공간 없음 → 재료 원위치·`NoSpace`, 성공 → 재료 제거·산출 배치, 탄창 가득), `HideoutServiceTests` 확장(TryCraft → HideoutChanged, 떠나는 중 Unavailable), `HideoutUiModelTests`·`UiModelTests` 확장(제작 탭·버튼 활성·결과 문구, 전신 줄 문구·숫자 없음(RULE-05 정규식)), PlayMode `HideoutCraftingFlowTests`(은신처 씬: 재료 지급 → [제작] → 보관함 산출 → 나가기 저장 → 다시 읽기) | 90~110분 |
| **2c** 구역·가이거·씬 | gameplay-programmer(두 번째) | `Raid/Logic/RadiationZoneMath.cs`·`Raid/Runtime/RadiationField.cs`, `Audio/Runtime/AudioDirector.cs`·`AmbientSoundscape.cs`, `EditorTools/M7HazardLayout.cs`·`M7HazardSceneApply.cs`·`PaletteMaterialMap.cs` | PlayMode `RadiationFieldFlowTests`(레이드 씬 사본: 서비스 등록, 구역 중심 률 0.5·밖 0, Toxic 표시 콜라이더 없음, 가이거 클릭률 = 폭풍 + 구역), `AudioFlowTests` 확장 | 50~60분 |
- 접점: 2a는 `IRadiationField`·`IInventoryService`(계약)만 — 가짜는 테스트 파일 안. 2b는 계약의 `IHideoutService` 타입·`IBodyStatusView.GetSurvivalTier`만. 2c는 2a 없이도 서비스 값만 단언.
**7C-3 통합 — 45~55분**: `M7SceneSetup` 7C 한 줄, 메뉴 2회, PlayMode 전체(M6 `AshStormFlowTests` Peak 선량 10 ± 0.5 그대로 — 방독면 없음·구역 밖).
**7C-4 검증 — 50~60분**: `M7SurvivalRaidFlowTests`(흐름 레이드: 방독면 반입 + 폭풍 Peak → 선량 0 유지, 정유소 구역 이동(PlaceAt) → 선량 증가·가이거, 탈출 → 은신처 → 다음 레이드 수분·에너지·선량 초기화, 제작한 탄창으로 레이드 재장전), Mono 빌드.
**7C-5 리뷰 + 수정 — 45분** → 커밋 `M7C: 방사능·탈수·허기, 방독면·해독제·물/음식, 제작대 (SYS-STAT, SYS-HUB, §8.3)`.

### 7D — 성능·빌드·폴리시 (예상 3~4시간 + IL2CPP 1.5~2시간은 설치 뒤)
**7D-1 (gameplay-programmer, 원본) — 60~75분**: `Diagnostics/*`, `GameManager` 한 줄, `FileSaveStorage` 폴더 주입, `BuildTools`(개발·IL2CPP 메서드), `link.xml`. 테스트 `EditMode/FrameTimeStatsTests`(중앙값·99%·링 버퍼·할당 0), `SmokeRunnerTests`(인자 파싱, 단계 표, 시간 초과), `BuildToolsTests`(IL2CPP 모듈 없으면 명확한 예외 — 현재 PC에서 실제로 확인).
**7D-2 (ui-programmer, 병렬 가능) — 40~50분**: 희귀도 색(`RarityStyle`·USS·칸 클래스), `PaletteConsistencyTests` 확장(새 색이 팔레트 밖이면 문서화된 예외 1개만).
**7D-3 검증 (test-engineer, 원본) — 75~90분**:
- `PlayMode/M7PerformanceTests`: 실제 레이드 씬, 적 34명 무적화, 무적 표적 = 청취자, 폭풍 Peak 강제, 변이체 8명이 표적 2~6m 둘레에서 근접 공격 반복, 보스 LMG 연사, 저격수 경고 → 10초 측정: AI 평균 ≤ 1.0ms·99% ≤ 3ms, 투사체 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms, Audio+폭풍+RadiationField ≤ 0.3ms, AI·근접·무기·오디오·생존 경로 GC 0, 플레이어 샷건 연사 중 투사체 수 상한 확인.
- Mono 릴리스·개발 빌드, **개발 빌드 스모크**: `Builds/Windows64Dev/Ashfall.exe -batchmode -nographics -ashfallSmoke <scratchpad 폴더> -logFile smoke.log` → 종료 코드 0, 로그 단계 전부, 세이브 v2 파일 생성.
- 전체 EditMode·PlayMode 2회, AC 체크.
**7D-4 리뷰 + 수정 + 문서 — 45~60분**: CHANGELOG "M7 설계 결정", CLAUDE.md "현재 진행"(오케스트레이터), 이 문서 §10 확정 → 커밋 `M7D: 성능 판정·개발 빌드 진단·빌드 메서드·희귀도 색 (TECH, §15 M7)`.
**7D-IL2CPP (사용자 설치 확인 뒤) — 90~120분**: 사람이 Unity Hub에서 6000.3.26f1에 "Windows Build Support (IL2CPP)" 추가 + VS 2022 "C++를 사용한 데스크톱 개발" 설치 → `BuildWindows64Il2Cpp` 성공 확인 → 메뉴로 ProjectSettings 백엔드 IL2CPP 영구 전환 → 릴리스·개발 IL2CPP 빌드 + 스모크 → 실패 시 link.xml·코드 수정 → 커밋 `M7: IL2CPP 빌드 전환 (TECH-§13.1)`.
- **ProjectSettings 확인(7D 리뷰 중간 2):** `BuildWindows64Il2Cpp*`는 빌드 동안만 백엔드를 IL2CPP로 바꾸고 finally에서 되돌리지만, 값을 되돌려도 `ProjectSettings/ProjectSettings.asset`에 새 키(예: `scriptingBackend: Standalone: 0`, `il2cppStacktraceInformation`)가 남을 수 있다.
  ① 임시 빌드 전에 `git status --porcelain ProjectSettings` + `md5sum ProjectSettings/*.asset`을 기록 → ② 빌드 → ③ 같은 두 명령으로 비교 → ④ 영구 전환 전 임시 빌드에서 생긴 diff면 `git checkout -- ProjectSettings/ProjectSettings.asset`로 되돌리고(에디터·Unity 꺼진 상태에서) 다시 md5 확인.
  영구 전환 단계에서는 메뉴(`PlayerSettings.SetScriptingBackend`)가 만든 diff만 커밋하고, 백엔드 키 외의 변경이 섞였으면 커밋 전에 보고한다.
- **스모크 바깥 제한 시간(7D 리뷰 낮음 5):** 플레이어의 60초 제한(`SmokeRunner.DefaultTimeoutSec`)은 메인 스레드가 돌아야 판정된다(멈추면 판정 불가). 실행하는 쪽이 프로세스 대기에 **바깥 제한 180초**를 둔다
  (PowerShell `$p = Start-Process … -PassThru; if (-not $p.WaitForExit(180000)) { $p.Kill(); 실패 }`, 또는 bash `timeout 180 …` — 종료 코드 124 = 멈춤 = 실패). IL2CPP 첫 실행은 셰이더·초기화가 느릴 수 있어 60초가 아니라 180초.
  값 없는 `-ashfallSmoke`(다음 인자 없음·`-`로 시작)는 종료 1 — Mono 개발 빌드에서 7D-4가 실제로 확인했다(아래 "7D 리뷰 수정 메모"). IL2CPP 개발 빌드에서도 한 번 확인한다.

**예상 총 소요: 약 20~23시간**(7A 5.5 + 7B 6 + 7C 6 + 7D 3.5, IL2CPP 별도 2). 선택 가속(7B ∥ 7C 일부) 시 약 18~21시간. PlayMode 전체 실행이 약 10분 → M7 끝에는 13~15분 예상(하위 단계마다 2회 + 통합).

---

## 7. AC별 테스트 계획

### AC1 — 무기 5종 전체, AI-SNP·AI-MUT·AI-BOS
| 종류 | 테스트 |
|---|---|
| EditMode 데이터 | `M7WeaponDataAssetTests`(GDD §5.2 값·분류·탄), `M7AiDataAssetTests`(§7 행동 플래그·무장: SNP = DMR, MUT 무장 없음, BOS = LMG + 호위 3) |
| EditMode 규칙 | `JamRulesTests`·`WeaponModelWearTests`·`TubeReloadTests`·`LooseAmmoSourceTests`·`PlayerInventoryLooseAmmoTests`·`WeaponSwitchModelTests`·`JamClearModelTests`·`PelletSpreadTests`·`SaveSerializerV2Tests`, `EnemyTacticsFactoryTests`·`AnchorTests`·`SniperTacticsTests`·`EnemyMeleeTests`·`MutantTacticsTests`·`CorpseLootBuilderTests`·`EnemyWeaponTests` |
| PlayMode | `WeaponSwitchFlowTests`·`JamFlowTests`·`TubeReloadFlowTests`(5종 모두 플레이어 사격), `SniperFlowTests`·`MutantFlowTests`·`M7EnemySceneSmokeTests`·`M7AiRaidFlowTests`, `M7WeaponRaidFlowTests`(세이브 v2·이전) |
| 오디오 | `AudioFlowTests`·`AudioConfigAssetTests` 확장(세트 6개, 클릭·울음) |
| 에디터 확인 | §7.1 W1~W8, E1~E7 |

### AC2 — 제작대, 방사능·탈수·허기
| 종류 | 테스트 |
|---|---|
| EditMode | `SurvivalModelTests`·`BodyHealthSystemicTests`·`MedicalSurvivalTests`·`StaminaModelTests`·`RadiationZoneMathTests`·`CraftingModelTests`·`HideoutServiceTests`·`HideoutUiModelTests`·`UiModelTests`(RULE-05 숫자 없음)·`M7SurvivalDataAssetTests` |
| PlayMode | `SurvivalFlowTests`·`RadiationFieldFlowTests`·`HideoutCraftingFlowTests`·`M7SurvivalRaidFlowTests` |
| 에디터 확인 | §7.1 C1~C8 |

### AC3 — Windows 빌드, 성능 목표 달성
| 종류 | 테스트 |
|---|---|
| EditMode | `FrameTimeStatsTests`·`SmokeRunnerTests`·`BuildToolsTests`·`BuildSettingsTests`(기존) |
| PlayMode | `M7PerformanceTests`(최악 상황 예산·GC), 기존 `RaidPerformanceTests`·`M6PerformanceTests`(34명 씬에서 재통과) |
| 빌드 | Mono 릴리스·개발 빌드 성공, 개발 빌드 스모크 종료 코드 0. IL2CPP = **사용자 설치 대기**(현재 PC에서 IL2CPP 메서드가 "모듈 없음"으로 실패하는 것까지 확인) |
| 에디터·빌드 확인 | §7.1 P1~P4 (1080p 60fps는 사람만 판정) |

### 회귀
- 기존 EditMode 1280 · PlayMode 115 통과. 바뀌는 단언: 세이브 버전 1 → 2(`SaveSerializerTests`), 레이드 씬 적 총수(20 → 34, 유형별 SCV 12·RDR 8은 그대로), 상인 목록 수(M5DataAssetTests — 추가분 반영). 그 밖 기존 단언은 고치지 않는다.
- **M6 회귀(사용자 확인 전)**: `AshStormFlowTests`(Peak 10초 ≈ 10 선량 — 방독면·구역 없는 위치), `M6RaidFlowTests`, `M6PerformanceTests`, `AudioFlowTests`, `RaidVisualSmokeTests`, `AiStormSightTests` 무변경 통과. M6 데이터·자리표시 에셋 바이트 불변(M7 메뉴 실행 전후 md5 — 새 파일·추가 필드 제외).
- `WeaponModel` 기존 생성자 = 마모 없음 → `EnemyWeaponTests`·`WeaponTests` 무변경. `IMagazineSource` 시그니처 불변.
- 확인 씬 M1~M3: 무기 컨트롤러 직렬화 경로(인벤토리 없는 M1)·M2 Primary1 시작이 그대로인지 `GreyboxSceneTests`·`M2SceneSmokeTests` 무변경 통과.

### 7.1 에디터 확인 항목 (자동 테스트로 못 보는 것 — 사람이 에디터·빌드에서)
무기 (레이드 씬 또는 은신처 → 레이드, 은신처에서 상인으로 SHT·DMR·탄 상자 구매)
- W1 1/2/3 교체 체감(0.5초가 너무 긴지/짧은지), 손에 든 자리표시 모델로 무기 구분 가능.
- W2 펌프 샷건: 근거리 펠릿 퍼짐(벽에 8개 탄흔이 흩어짐 — 디버그 궤적), 한 발씩 장전 리듬, 장전 중 사격 중단.
- W3 볼트액션: 1.5초 간격, 100m+ 낙차, 조준 감.
- W4 키트 권총으로 3번 사격(M5 Q16 해소), SMG·소총 그대로.
- W5 내구도 낮은 무기(시체에서 주운 것 또는 인스펙터로 0) → 고장: 헛격발 소리로 알 수 있는지, T 길게(1.2초) 해제 감, T 짧게는 탄창 확인.
- W6 총성 산탄·저격·LMG가 다른 무기와 구분되는지(근·중·원).
- W7 T 확인 문구가 샷건 튜브에서도 자연스러운지.
- W8 은신처 상인 목록에 새 무기·탄 상자, 내구도가 가격에 안 보이는 것.
적 (레이드)
- E1 저격수 섬광이 150m에서 보이는지(너무 잘/안 보이는지), 경고 1.5초가 반응 가능한지, 섬광 크기·색.
- E2 저격수 위치가 공정한지(스폰을 내려다보지 않음), 고지대 퍼치에서 떨어지거나 끼지 않는지.
- E3 변이체 무리: 울음 소리로 접근을 알 수 있는지, 둘레로 흩어져 오는지, 근접 피해량·간격 체감, 도망 가능성(질주 5.0 vs 플레이어 5.5).
- E4 보스·호위가 거점(25m)을 지키는지, LMG 압박감, 보스 처치 후 유물 루팅.
- E5 머티리얼: 변이체·보스가 다른 적과 구분되는지.
- E6 34명에서 레이드 전체 난이도·밀도(특히 마을·정유소).
- E7 Peak 폭풍 중 저격수·변이체 동작(못 봄, 소리로만).
생존·제작
- C1 H 화면 "전신" 줄 문구(목마름/탈수 등)와 RULE-05(숫자 없음).
- C2 방독면 착용 → 폭풍 중 피폭 없음(디버그: `PlayerHealth.RadiationDose` 인스펙터), 헬멧과 같이 못 쓰는 것이 이해되는지.
- C3 정유소 Toxic 표시가 구역을 알려 주는지, 가이거가 구역에 들어가며 빨라지는지.
- C4 선량 100 → 부위 등급이 한 단계씩 나빠 보이는 체감, 해독제로 복구.
- C5 수분·에너지 0까지의 시간(16.7분·23.8분)이 레이드 흐름에서 적당한지, 물·음식 사용 시간.
- C6 은신처 [제작] 탭: 재료 표시·버튼·결과 문구, 보관함에 산출물.
- C7 제작 경제(재료 매도 ≈ 산출 매도)가 거슬리지 않는지.
- C8 희귀도 테두리 색(유물 발광)이 팔레트와 어울리는지.
성능·빌드
- P1 개발 빌드(`Builds/Windows64Dev/`) 1080p 전체 화면, Peak 폭풍 + 교전 + 변이체 무리: `Player.log`의 프레임 시간 줄로 60fps(중앙값 ≤ 16.6ms) 판정 — GTX 1660급에서. 다른 GPU면 결과에 GPU 이름을 적는다.
- P2 Profiler(개발 빌드 연결)로 GPU 시간(재 파티클·포스트·그림자) 확인, 병목이면 `AtmosphereConfigSO` 파티클 수·그림자 거리로 조정(사람 판단).
- P3 릴리스 빌드 실행: 메인 메뉴 → 은신처 → 레이드 → 탈출 → 은신처 → 종료 → 다시 실행 시 세이브 유지(v2).
- P4 **IL2CPP(설치 뒤)**: 같은 P1·P3를 IL2CPP 빌드로.
- M6 미확인 항목(M6_design §7.1 V1~V8·A1~A9·S1~S5·P1)도 이번 확인 때 함께.

### 7.2 위험
- R1 **PlayMode 실행 시간 증가**: 하위 단계마다 2회 + 통합 → 하루 수 시간이 테스트에. 각 단계는 새 테스트만 먼저(`-testFilter`), 마감에 전체 2회. 34명 성능 테스트는 7D에 한 번.
- R2 **적 34명·LMG 600RPM·샷건 펠릿**으로 AI·투사체 예산 초과 가능 → 7B-4에서 먼저 재측정해 보고. 넘치면 감각 예산(프레임당 8명)·LMG 점사 패턴 조정(SO), 그래도 넘치면 배치 수 축소(Q30 B)를 오케스트레이터에 제안.
- R3 **세이브 v2 이전**: JsonUtility의 누락 필드 처리에 기대면 v1 무기가 내구도 0(고장 연발)이 될 수 있다 → 버전으로 명시 이전 + v1 파일 고정본 테스트. v2 파일을 M6 빌드로 열면 격리(문서화).
- R4 **`IBodyStatusView`·`IHideoutService` 확장**이 테스트 가짜를 깨뜨림 → 7C-1 계약이 가짜까지 같이 고친다(병렬 단계 금지 파일).
- R5 **IL2CPP 미설치**: AC "Windows 빌드"를 Mono로만 증명 — IL2CPP 전용 문제(스트리핑·AOT·JsonUtility)는 설치 뒤에야 드러난다. 스모크 실행기·link.xml로 준비하고, 7D-IL2CPP를 별도 커밋으로 남긴다. 사용자 설치 전 M7 완료 보고는 "IL2CPP 대기"로.
- R6 **60fps 실측 불가(배치모드)**: 자동 판정은 CPU 예산뿐. GTX 1660이 없는 PC면 사람 확인 결과가 기준 하드웨어가 아니다 — 보고에 GPU를 적는다.
- R7 **저격수 공정성**: 150m 시야 + 정확한 사격이 스폰·주요 길목을 덮으면 좌절스럽다 → 배치 검사(스폰 거리, 길목 시선)·경고 1.5초, E1·E2 사람 확인. 수치는 SO.
- R8 **뷰모델 프리팹 생성**: 배치모드 `SaveAsPrefabAsset` 멱등성(두 번째 실행 바이트 동일) — "없을 때만" 정책으로 회피.
- R9 **변이체 근접 판정**: 플레이어 캡슐 높이 판정이 엎드림에서 부정확(M2 근사) → 엎드린 플레이어는 대부분 다리·복부. 수용(TODO).
- R10 **M6 미확인 상태에서의 회귀**: M6 동작을 사람이 아직 못 봤으므로 M7 변경(오디오 디렉터·플레이어 체력)이 M6 문제를 가릴 수 있다 → M6 회귀 테스트 무변경 통과를 각 하위 단계 마감 조건으로.

---

## 8. CHANGELOG_GDD.md "M7 설계 결정" 초안

```markdown
## 2026-10-?? — M7 설계 결정 (§4.2, §4.3, §5, §6, §7, §8.3, §9, §12, §13.1, §13.3, §14, §15 M7)
문서 본문은 바꾸지 않았다. 아래는 M7 구현을 위한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
사용자 지시로 GDD에 없거나 모호한 53개 항목(설계 문서 `docs/design/M7_design.md` §5의 Q1~Q53)은 가장 단순한 추천안으로 정했다.
코드의 결정 지점에는 `TODO(GDD-?) Qn` 주석이 있다. M7은 하위 단계 7A(무기)·7B(AI)·7C(생존·제작)·7D(성능·빌드)로 나눠 커밋했다.
- **§5 무기 5종 (Q1~Q8, Q20, Q21):** WPN-SHT-01(12ga, 70RPM, 펠릿 18×8, 내장 튜브 6), WPN-DMR-01(7.62, 40RPM, 110, 탄창 5) 추가. 플레이어 무기 교체 1/2/3 = 주무기1·주무기2·보조무기(교체 0.5초),
  레이드 시작은 첫 비지 않은 슬롯. 튜브는 무기 정의의 내장 탄창(그리드에 없음)이고 낱탄은 탄 상자(BOX-12GA-8) — R로 한 발씩(0.5초), 장전 중 사격하면 중단.
  산탄은 펠릿마다 퍼진다. 탄약 정의는 `WeaponHandlingConfigSO.ammoDefinitions`. 무기별 자리표시 뷰모델 프리팹. 약실 탄 없음(M1 유지). 상인 목록에 새 무기·탄 추가.
- **§5.1 고장·내구도 (Q9~Q18):** `WeaponItem.Durability`(0~100). 발당 무기별 고정 감소. 내구도 < 50이면 발당 (50 − 내구도) × 0.1%로 고장 — 탄 소모 없이 발사 안 됨, 재장전·탄창 확인 불가,
  T 1.2초 계속 누르면 해제(짧게는 탄창 확인). 알림은 소리만(`WeaponClicked`). 적 무기는 마모·고장 없고 스폰 내구도만 무작위(시체 무기는 고장 가능). 상인·제작·키트·상자는 100.
  가격 미반영·표시 없음·수리 없음·고장 상태 저장 안 함.
- **§13.3 세이브 (Q13):** save.json version 2 — `ItemRecord.durability` 추가. v1은 읽을 때 모든 무기 내구도 100으로 이전한다. 튜브 무기는 내장 탄창 id·탄 수를 기존 장착 탄창 필드에 쓴다.
- **§5.3 (Q19):** 방어구 관통·둔탁 피해·방어구 내구도는 M7 범위 밖(어느 마일스톤 AC에도 없음) — 미구현으로 남긴다.
- **§7 AI (Q22~Q31):** AI-SNP = 고지대 퍼치 고정(이동 없음, 시야 150m), "레이저 반사 신호" = 조준 중 렌즈 섬광(처음·다시 볼 때 1.5초 경고 후 사격). AI-MUT = 무장 없음, 근접(1.8m·0.3초 준비·1.1초 간격·14),
  무리 = 분대 연락 + 표적 둘레 분산 접근, 울음·공격 소리(`EnemyVocalized`). AI-BOS = Raider 전술 + 거점 반경 25m + 적 전용 경기관총 WPN-LMG-01(`enemyOnly`, `WeaponClass.MachineGun`, 시체·상인·루트에 없음),
  호위 3명 = 새 유형 AI-BGD. 전술은 `EnemyArchetypeSO.tactics`(Auto = 기존 usesCover 규칙). Crimson Basin = 기존 20명 + SNP 2 + MUT 8 + BOS 1 + BGD 3 = 34명, 기존 구조물·CoverPoint 재사용(NavMesh 재굽기 없음).
  보스 시체 = LT-BOS(유물 ITM-REL-01 보장).
- **§4.3 생존 (Q32~Q36, Q42, Q43):** 수분 100(−0.1/s, 복부 파괴 ×2), 0이면 파괴되지 않은 부위마다 −0.5 HP/s. 에너지 100(−0.07/s), 0이면 스태미나 재생 ×0.5.
  방사능 선량 100 이상이면 모든 부위 최대 HP ×0.8(현재 HP 자름), 해독제 = 선량 0·최대 HP 복구. 레이드 사이 이월 없음(세이브 변경 없음).
  물·음식·해독제는 `MedicalDefinitionSO` 필드로 의약품 흐름을 쓴다(전신 대상은 흉부로 표시). 부위 상태 화면에 "전신" 줄(목마름/탈수, 배고픔/허기, 방사능 오염/피폭 — 수치 없음, RULE-05).
- **§8.3·§6 방독면·방사능 구역 (Q37~Q41):** 방독면 GEAR-GSM-01은 헬멧 슬롯을 공유하는 방어구 정의(`blocksStormRadiation`), 재 폭풍 방사능만 막는다(필터 소모 없음).
  방사능 정유소 = 원형 반경 70m·0.5/s·가장자리 15m(`RaidConfigSO.radiationZones`, Raid `IRadiationField`), Toxic 자리표시 표시물(지도 표시 없음). 가이거 = 폭풍 + 구역 환경 률.
- **§9 제작대 (Q44~Q48):** 은신처 [제작] 탭, `CraftingConfigSO` 레시피 8개(임시), 재료는 보관함만, 즉시·무료, 산출 탄약 = 가득 찬 탄창 또는 12ga 탄 상자. 고철 = 잡동사니 JNK-01~03(전 아이템 희귀도 표 지정).
- **§6 희귀도 색 (Q49):** 인벤토리·보관함·상인 칸 테두리. 고철 Ash_Grey·일반 Bone·우수(녹) = Toxic 토큰 재사용·유물 Ember 발광, 희귀(청)는 §10.1 팔레트에 없어 USS 상수 1개(TODO).
- **§13.1·§15 빌드·성능 (Q50~Q53):** 자동 성능 판정 = 적 34명 + 재 폭풍 Peak + 변이체 무리 + 보스 LMG에서 기존 예산. 1080p 60fps는 개발 빌드 프레임 시간 로그로 사람이 판정.
  개발 빌드 전용 진단(`DevDiagnostics`: 프레임 시간 로그, `-ashfallSmoke <폴더>` 스모크). Windows 빌드는 Mono로 검증했고 IL2CPP는 사용자 설치 후 전환한다(`BuildWindows64Il2Cpp`, `link.xml` Ashfall.* 보존).
- **§13.3 경계:** Core 추가 — `ILooseAmmoSource`, `JamRules`·`WeaponWear`, `IRadiationField`, `IBodyStatusView.GetSurvivalTier`(등급만), `IHideoutService` 제작 멤버, 이벤트 `WeaponClicked`·`EnemyVocalized`, enum `SurvivalStat`·`SurvivalTier`·`WeaponClass.MachineGun`.
- **§14 스키마에 없는 추가:** `WeaponDefinitionSO`(internalMagazine, roundReloadTimeSec, pelletSpreadDeg, durabilityLossPerShot, enemyOnly), `MagazineDefinitionSO.isAmmoBox`, `MedicalDefinitionSO`(hydrationRestore, energyRestore, curesRadiation),
  `ArmorDefinitionSO.blocksStormRadiation`, `EnemyArchetypeSO`(tactics, anchorRadiusM, weaponDurabilityRange, 근접·경고·둘레 값), `CraftingConfigSO`, `RaidConfigSO.radiationZones`, `PlayerConfigSO` 생존 값, `WeaponHandlingConfigSO` 고장·교체·탄약 표.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 새 무기 반동·조준·재장전·퍼짐·소음·크기·무게·가격·마모율, 12ga·7.62 탄 관통·초속, 고장 해제 1.2초, 교체 0.5초, 적 유형 4개 수치 전부(HP·시야·속도·명중·근접·경고),
  배치 좌표, 루트 테이블, 새 아이템 크기·무게·효과량·가격·희귀도, 레시피, 구역 반경·률, 생존 등급 경계(25%, 선량 50), 복부 파괴 탈수 ×2, 희귀도 색, 모든 자리표시 소리·프리팹.
```

---

## 9. 계약 파일 목록 (요약)

| 하위 단계 | 원본에 계약이 만들거나 고치는 파일 |
|---|---|
| 7A-1 | Core: `Data/WeaponDefinitionSO.cs`, `Data/MagazineDefinitionSO.cs`, `Items/WeaponItem.cs`, `Items/ItemInstance.cs`(ItemFactory), `Combat/WeaponModel.cs`, 새 `Combat/JamRules.cs`·`Combat/WeaponWear.cs`·`Services/ILooseAmmoSource.cs`, `Combat/LocalMagazineSource.cs`, `Save/SaveData.cs`, `Save/SaveSerializer.cs`, `Events/GameEvents.cs` / Weapons: `WeaponHandlingConfigSO.cs` / `Assets/InputSystem_Actions.inputactions` / EditorTools: 새 `M7Names.cs` / 테스트 새 5개 + `SaveSerializerTests` 버전 단언 / (1b) `M7WeaponDataSetup.cs`·`M7WeaponDataAssetTests.cs`·데이터 에셋 |
| 7B-1 | Core: `Data/ItemEnums.cs`, `Events/GameEvents.cs` / AI: `AiEnums.cs`, `Data/EnemyArchetypeSO.cs`, `Data/AiConfigSO.cs`, `Brain/EnemyIntent.cs`, `Brain/EnemyBrain.cs`, `Brain/TacticsContext.cs`, `Runtime/EnemyAgent.cs` / `M7Names.cs` 추가 / 테스트 새 2개 / (1b) `M7AiDataSetup.cs`·`M7EnemyLayout.cs`·`M7AiDataAssetTests.cs`·데이터 에셋 |
| 7C-1 | Core: `Combat/BodyEnums.cs`, `Services/IBodyStatusView.cs`, 새 `Services/IRadiationField.cs`, `Data/MedicalDefinitionSO.cs`, `Data/ArmorDefinitionSO.cs`, `Services/IHideoutService.cs` / Player: `PlayerConfigSO.cs` / Raid: `Data/RaidConfigSO.cs`, 새 `Data/RadiationZoneConfig.cs` / Hideout: 새 `Data/CraftingConfigSO.cs` / 테스트 가짜 `UiTestFakes.cs`·`HideoutUiModelTests.cs`(FakeHideoutService) / `M7Names.cs` 추가 / 테스트 새 1개 / (1b) `M7SurvivalDataSetup.cs`·`M7SurvivalDataAssetTests.cs`·데이터 에셋 |
| 7D-1 | Core: 새 `Diagnostics/*`, `GameManager.cs`, `Save/FileSaveStorage.cs` / EditorTools: `BuildTools.cs` / `Assets/_Project/link.xml` / 테스트 새 3개 |

---

## 10. MVP 전체(M0~M7) 남은 TODO(GDD-?)·임시값 중 기획 확정이 필요한 것 (7D-4 확정)

7A~7D 진행 결과로 확정한 목록이다. 코드 위치는 각 `TODO(GDD-?) Qn` 주석, 결정 근거는 `docs/CHANGELOG_GDD.md` M1~M7 절.

1. ~~§5.3 방어구~~ → **MVP 마무리 F4로 해소**(플레이어·적 BOS·BGD). 남은 임시값: 내구도 감소 × 0.2, 적 방어구 배치, 분산 피해는 방어구 무시(MF-Q25), 9mm·12ga는 등급 3에 관통 0%(`MVP_finish_design.md`).
2. **무기 수치**: 5종 반동·조준·재장전·퍼짐·소음 반경·마모율, 펌프·볼트 동작 시간, 탄 관통력·초속(M1·M3·M7). 9mm 35 / SMG 30 = 무기 피해 필드 해석(M3).
3. **고장·내구도**: 표시·수리·가격은 **MVP 마무리 F3로 해소**(등급 경계 0.5/0.25·수리비 0.5 임시), 고장 확률 곡선, 적 무기 스폰 내구도 35~85(시체가 될 때 굴림)(M7 Q12~Q17).
4. **상태이상**: 출혈 0.35·중출혈 0.15(피해 10+), 골절 0.2(피해 20+), 골절 패널티, 의약품 사용 시간(M2).
5. **RULE-04·05 경계**: 탄창 확인 0.67/0.34, 부위 등급 0.75/0.35, 생존 등급 Low = 최대 × 0.25·방사능 오염 50(M2·M7 Q43).
6. **생존 해석**: 수분 0 = 머리 제외 부위마다 −0.5 HP/s(흉부 160초에 사망), 전신 사인 문구는 **MVP 마무리 F1로 해소**("사망 — 탈수/출혈"), 복부 파괴 수분 × 2(Q34), 방사능 100 → 최대 HP × 0.8, 레이드 사이 이월 없음(Q32·Q35).
7. **방독면·방사능 구역**: 헬멧 슬롯 공유, 폭풍 방사능만 차단, RAD-REF 반경 70m·0.5/s·감쇠 15m, 지도에 구역 표시 없음(Q37~Q41).
8. **적 AI 수치**: 인지 게이지·반응·명중·점사·선호 거리(M3), 저격수 시야 150m·경고 1.5초·섬광 0.35°, 변이체 근접 14·속도 5.0·둘레 2.5m, 보스 HP·거점 25m, 호위 3명(M7 Q22~Q29).
9. **적 접근 정지 거리 `approachStopDistanceM` 2.0m**(7D-4, 7D-3 발견 1): 약탈자·습격자·보스·호위가 LKP·표적에서 멈추는 거리. 수색 지점(LKP 둘레 12m 무작위)은 그대로. 여러 명이 한 LKP로 몰리면 2m 고리가 차서 서로 밀리고, 적끼리 시야 가림(7B 규칙)으로 뒤쪽 사수가 못 쏜다 — 군집 분산 규칙 필요 여부.
10. **레이드 배치·밀도**: 적 34명 배치·분대, 탈출 반경, 스폰 3곳, 상자 수, 재 폭풍 시작 창 180~1200초·전환 20초(M4·M6·M7 Q30).
11. **루트 테이블 전부**: LT-CRATE·SCV·RDR·SNP·MUT·BOS 내용·굴림 횟수, 방독면 0.5(신호탄 최저 가중치 규칙 유지), 유물 1개 보장(M3·M4·M7 Q31).
12. **경제**: 모든 basePrice, 매수·매도 1.0/0.4, 시작 탄피 6000, 상인 이름·판매 목록(M5·M7).
13. **제작 레시피 8개**: 재료·이름 임시, **12ga 탄 상자·부목은 JNK-01 × 2**(설계 × 1이면 "산출 매도 ≤ 재료 매수" 위반), 즉시·비용 없음, 재료 = 보관함만(Q44~Q47).
14. **보관함 업그레이드**: **MVP 마무리 F2로 해소** — 10×30 → 40(5000) → 50(12000) 임시값, 50줄 재구성 비용(리뷰 5).
15. **희귀도 표**: 전 아이템 희귀도(Q48). **WPN-PST-01·WPN-SMG-01·ITM-FLR-01은 Common으로 남음**(§3.11은 우수 — 에셋을 직접 고쳐야 바뀜).
16. **희귀도 색(§6·§10.1)**: 우수(녹) = Toxic 재사용 → "Toxic = 유일한 비적색 강조"가 인벤토리에서 약해짐, 희귀(청) `#4E6C8A`는 팔레트 밖 유일한 색, 유물 "발광" = Ember 2px + Rust_Red 바탕(UI Toolkit에 그림자 없음), 유물 고스트는 패널 안/밖 테두리 색이 같다(Q49).
17. **HUD·UI 해석**: 남은 시간은 지도 분 단위만, 탈출 조건 이유 문구, 지도는 세계를 멈추지 않음, 일시정지 [레이드 포기], 제작 결과 문구(M4·M7).
18. **소리 수치**: 총성 거리·게인, 걸음 들리는 거리, voice 24(교전 중 발소리 누락·빼앗기 관찰), 울음 60m·쿨다운 4초, 환경음 간격, 전달 지연·차폐 없음(M6 Q15~Q17·M7 Q26).
19. **아트 수치**: 맑을 때 가시거리 450m, 틴트·Bloom, 재 파티클 수, 태양 위치, Toxic 표시물 배치(M6·M7) — 실제 에셋 교체 때 함께.
20. **이동 해석**: 점프 = Space, 빠른 재장전 = 재장전 중 R 한 번 더, 무기 교체 키, 무게 패널티 해석(M1·M7).
21. **성능 판정 전제(Q50)**: 자동 판정은 배치모드 CPU 예산(렌더 제외, 34명 + Peak + 변이체 둘레 + 보스 LMG, 표적 = 보스 거점 5m·0.8m 띄움)과 정상 상태 GC 0(콜라이더 래퍼 1회 생성 제외)이다. **1080p 60fps(GTX 1660급)는 사람이 개발 빌드 `[DevDiagnostics]`로 측정**해야 확정된다(§7.1 P1).
22. **개발 빌드 기술 상수(Q51)**: 프레임 로그 10초·링 4096, 스모크 60초·레이드 10초 — 게임 수치 아님(바꿀 일 없으면 확정).
23. **자리표시 에셋 전체**: WAV 40개(§2.2 실제 생성 결과)·프리팹·머티리얼·하늘/재/지형 텍스처 — 실제 에셋 목록(M6 §2.3, M7 §2.3). IL2CPP 빌드는 사용자 설치 대기(7D-IL2CPP).

## 질문 결정 (오케스트레이터, 2026-10-22)
- §5 Q1~Q53은 ★추천안을 그대로 채택한다(Q19 = B, 나머지 = A). 사용자 지시: GDD에 없거나 모호한 것은 가장 단순한 쪽으로 정하고 `TODO(GDD-?) Qn`·CHANGELOG에 기록.
- 진행 순서: 7A → 7B → 7C → 7D(하위 단계마다 커밋). 7B ∥ 7C 선택 가속은 쓰지 않는다(사본 관리·충돌 위험 대비 이득이 작음).
- IL2CPP: 이 PC에 모듈·C++ 툴체인 없음 → 7D-IL2CPP는 "사용자 설치 대기". ProjectSettings 백엔드는 바꾸지 않는다.

## 7A-1 구현 메모 (gameplay-programmer, 2026-10-22)

원본 EditMode 1362/1362 통과(기존 1280 + 새 82). 기존 단언 변경은 `SaveSerializerTests`의 버전만(`"version": 1` → 2, "더 새 버전" 입력 2 → 3·오류 문구 "3"). PlayMode는 7A-1b 뒤.

**설계와 다른 점·구체화**
- `WeaponModel` 생성자는 오버로드가 아니라 **선택 인자 하나**로 했다: `(WeaponItem, IMagazineSource, ILooseAmmoSource looseAmmo = null, WeaponWear wear = default)` — 기존 2인자 호출(EnemyWeapon·테스트)은 그대로 컴파일·동작(마모 없음).
  `looseAmmo`가 null이면 `source as ILooseAmmoSource`를 쓴다(LocalMagazineSource·7A-2b PlayerInventory는 둘 다 구현하므로 보통 넘길 필요 없음).
- `IsTubeReload` = "이 무기의 R이 한 발씩"(`Item.HasInternalMagazine`), 진행 중 여부는 기존 `IsReloading`. `WearEnabled`(읽기) 추가.
- 고장 굴림은 `FireControl`이 true + 탄 1발 이상일 때만. 빈 탄창 방아쇠는 굴리지 않고 마모도 없다. 고장 중에는 `FireControl`에 방아쇠를 넘기지 않는다(다시 굴리지 않음).
  마모(내구도 −`durabilityLossPerShot`)는 `WeaponWear.Enabled`일 때만 — 옵션 없는 적 무기는 내구도가 바뀌지 않는다(스폰 내구도는 7B `EnemyWeapon`).
- 고장 상태는 `WeaponItem.IsJammed`(아이템)에 있다 — 같은 아이템으로 모델을 다시 만들어도 고장 유지(교체로 해제 불가). 저장하지 않는다(Q14).
- 튜브 장전: 큰 dt면 한 Tick에 여러 발. 장전 중 사격 중단은 `TryFire`가 방아쇠를 누른 프레임에 처리하고 같은 프레임에 쏜다(반자동 새 당김 판정은 FireControl 그대로).
  튜브 무기의 R 재입력은 false(빠른 재장전 없음). 고장 중 `CanReload`·`PressReload`는 탄창 무기도 false.
- `Magazine.IsFull`·`TryAddRound()` 추가(튜브 장전용).
- 세이브 내구도 읽기: v1(`version < 2`) → 항상 100. v2 → **음수 전부**(-1 = 해당 없음)·NaN → 100, 100 초과 → 100, 0~100 그대로. 설계의 "자름"을 음수에는 0이 아니라 100으로 해석(손으로 고친 값이 고장 연발 무기가 되지 않게). `SaveSerializer.ReadDurability(float, int)` 공개.
  확인: JsonUtility는 누락 필드에 초기값(-1)을 남긴다(v2 파일에 필드가 없어도 100).
- `FromRecord(record, catalog, report)` 3인자는 현재 버전으로 읽고, 4인자 `(…, int version)`이 새로 생겼다. `ToProfile`은 `data.version`을 넘긴다. `FirstVersion = 1` 상수 추가.
- 튜브 복원: 기록 id가 무기의 `internalMagazine.id`면 카탈로그를 거치지 않고 그 정의로 복원(탄 수는 용량으로 자름). id가 비면 빈 튜브(알림 없음).
  다른 탄창 id(탄약이 같든 다르든, 탄 상자 포함) → 빈 튜브 + `DroppedItemCount++`, 모르는 id → 빈 튜브 + `UnknownItemIds`. 분리형 무기에 기록된 탄 상자(`isAmmoBox`)도 탄약 불일치와 같이 버림 집계.
- `WeaponHandlingConfigSO`: §3.3 필드 + `TryGetAmmo(AmmoType, out AmmoDefinitionSO)`(할당 없음, 표의 첫 항목) + `CreateWear(System.Random)`.
- 입력: Player 맵 `Weapon1/2/3` = `<Keyboard>/1·2·3`, 그룹 `;Keyboard&Mouse`. 기존 퀵슬롯은 4~7이라 충돌 없음(1~3 바인딩은 원래 없었다). 파일 줄 끝은 원래대로 LF.
- 테스트 도우미 `Assets/Tests/EditMode/M7TestData.cs`(새, 계약 파일 — 7A-2 병렬 단계는 고치지 않음): `ShotgunTube()`·`Box12Ga8()`·`Mag762_5()`·`Shotgun(tube)`·`Dmr()`·`Handling()`.
- `MagazineCheckModel` TODO 주석은 7A-2a 몫이라 건드리지 않았다.

**7A-1b·7A-2가 쓰는 공개 API (Core 등)**
- `WeaponDefinitionSO`: `internalMagazine`, `roundReloadTimeSec`, `pelletSpreadDeg`, `durabilityLossPerShot`, `enemyOnly`. `MagazineDefinitionSO.isAmmoBox`.
- `WeaponItem`: `const MaxDurability = 100`, `float Durability {get;set;}`(0~100, NaN → 100), `bool IsJammed {get;set;}`, `bool HasInternalMagazine`. 생성자: 내장 튜브 무기 + loaded null → 빈 튜브. `ItemFactory.Create`도 같음.
- `Magazine`: `IsFull`, `TryAddRound()`.
- `JamRules.Chance(durability, threshold, chancePerPoint)`(0~1), `JamRules.Roll(chance, System.Random)`(확률 ≤ 0이면 난수를 뽑지 않음).
- `WeaponWear(threshold, chancePerPoint, System.Random)`(Enabled = true, random null → 예외), `WeaponWear.None`/`default` = 끔.
- `WeaponModel`: 위 생성자, `IsJammed`, `IsTubeReload`, `RoundsLoadedThisReload`, `WearEnabled`, `CanReload`(고장·튜브 반영), `TryFire(now, trigger)`, `PressReload()`, `CancelReload()`, `ClearJam()`(고장 아니면 false), `Tick(dt)`, `event Action Jammed`, 기존 `MagazineDropped`.
  컨트롤러(7A-2a)는 `handling.CreateWear(random)`으로 플레이어 무기만 마모를 켜고, `Jammed` → `WeaponClicked(Jammed)`, T 길게 완료 → `ClearJam()` + `WeaponClicked(JamCleared)`, 빈 탄창·고장 중 새 방아쇠 → `WeaponClicked(DryFire)`.
- `ILooseAmmoSource { bool HasLooseRound(AmmoType); bool TakeLooseRound(AmmoType); }`. `LocalMagazineSource`가 구현(탄이 가장 적은 상자부터, 같으면 목록 앞, 빈 상자는 남김), `HasLoadable`·`TakeFullest`는 상자를 제외.
- `GameEvents`: `enum WeaponClickKind { DryFire, Jammed, JamCleared }`, `readonly struct WeaponClicked { Vector3 Position; WeaponClickKind Kind; }`.
- `SaveSerializer`: `CurrentVersion = 2`, `FirstVersion = 1`, `FromRecord(…, int version)`, `ReadDurability(float, int)`. `ItemRecord.durability = -1f`.
- `WeaponHandlingConfigSO`: `jamDurabilityThreshold = 50`, `jamChancePerDurabilityPoint = 0.001`, `jamClearHoldSec = 1.2`, `weaponSwitchTimeSec = 0.5`, `AmmoDefinitionSO[] ammoDefinitions`, `TryGetAmmo`, `CreateWear`.
  기존 `WeaponHandlingConfig.asset`에는 아직 새 필드가 직렬화되지 않았다 — 로드 시 코드 기본값, 표는 빈 배열. 7A-1b 데이터 메뉴가 표(4종)를 채우고 저장한다.
- `EditorTools/M7Names`: 7A 데이터 id·경로(`ShotgunPath`·`DmrPath`·`LmgPath`·`Ammo12GaPath`·`Ammo762Path`·`Mag762_5Path`·`Mag762_100Path`·`ShotgunTubePath`·`Box12Ga8Path`·`WeaponHandlingConfigPath`),
  프리팹 `WeaponPrefabFolder`·`ViewModelPrefabPath(id)`·`MuzzleChild = "Muzzle"`, 소리 `DryFireClipPath`·`JamClearClipPath`(`Audio/Placeholder/Weapon/`). 7B·7C 구역은 비워 둠.
- 7A-2b 주의: `PlayerInventory.IsLoadable`은 아직 탄 상자를 제외하지 않는다(2b 몫). 그전까지 상자는 12ga 무기(튜브 = 탄창 재장전 경로를 안 탐)에만 맞으므로 실제 문제는 없다.

## 7A-1b 구현 메모 (data-designer, 2026-10-22)

메뉴 `Ashfall/Setup/Create M7 Weapon Data`(배치 `Ashfall.EditorTools.M7WeaponDataSetup.CreateData`, 7A 몫만은 `WriteM7WeaponData()`). `CreateData`는 M6처럼 `M6DataSetup.CreateData()`(→ M5 → … M1)를 먼저 부른다.
원본에서 2회 실행 → 두 번째 실행 후 `Data/`·`Audio/`·`Scenes/`·`Settings/` 214개 파일 md5 동일(GUID·.meta 포함). EditMode 1392/1392 통과(새 30 포함), PlayMode 115/115 통과(기존 그대로, 572초).

**만든 에셋 (`Assets/_Project/Data/Weapons/`, 없을 때만)**: `AMMO-12GA`·`AMMO-762`, `MAG-762-5`·`MAG-762-100`·`MAG-12GA-TUBE`·`BOX-12GA-8`, `WPN-SHT-01`·`WPN-DMR-01`·`WPN-LMG-01`. 값 = §3.1·§3.2 표 그대로.
**기존 에셋 (없으면 추가·빈 필드만)**: PST/SMG/RIF `durabilityLossPerShot` 0.06/0.04/0.05(파일에 필드가 없을 때만 — 다른 M7 필드는 기본값으로 함께 직렬화됨),
`WeaponHandlingConfig` §3.3 4필드(파일에 없을 때만) + `ammoDefinitions`(빠진 탄 종류만 끝에: AMMO-9MM·556·762·12GA), `TraderConfig.catalog` 끝에 SHT·DMR·MAG-762-5·BOX-12GA-8, `LT-CRATE` 끝에 BOX-12GA-8 1.5·MAG-762-5 0.5, ItemDatabase 재수집(20 → 29개).

**정한 것·설계와 다른 점**
- `WeaponClass.MachineGun`을 Core `ItemEnums.cs` 끝에 추가했다(LMG `cls`가 설계상 MachineGun, 데이터는 "없을 때만" 생성이라 임시 분류로 만들면 남는다). 설계 §4.1에선 [7B 계약] 몫 — **7B-1은 다시 추가하지 말 것**.
- `M5DataSetup.CreateItemDatabase`를 private → public(동작 같음, 설계 §4.10 [7C]의 "재수집 호출만 공개"를 앞당김).
- LT-CRATE에는 §3.9의 7A 몫(탄 상자·7.62 탄창)만 넣었다. 지시의 "LT-CRATE에 SHT·DMR"은 §3.9 표에 없어 넣지 않았다(무기는 상자에서 나오지 않는 M4 규칙 유지). 물·음식·해독제 등은 7C.
- 희귀도는 새 에셋에만 §3.11대로(SHT 우수, DMR 희귀, MAG-762-5 우수, BOX 일반, 튜브·LMG·탄띠·탄약 정의 Scrap). 기존 아이템 희귀도는 7C 몫.
- 표에 없던 값(전부 TODO(GDD-?)): 탄약 정의 무게 0·가격 0(기존 탄약과 같음), 샷건 `reloadTimeSec`/`fastReloadTimeSec` 0(튜브라 안 씀), LMG 반동·ADS·지향 퍼짐 0(표 "—"), displayName "7.62 탄창 (5발)"·"7.62 탄띠 (100발)"·"12ga 튜브"·"12ga 탄 상자 (8발)"·"12ga 탄"·"7.62 탄"·"경기관총".
- GDD 표와 다른 값은 없다(CHANGELOG 기록 대상 없음 — M7 설계 결정 절은 §8 초안대로 오케스트레이터가 쓴다).

**테스트**: 새 `EditMode/M7WeaponDataAssetTests`(30: GDD RPM 70/40·피해 18/110·펠릿 8·튜브 6·탄창 5·분류·탄, 고장 50·0.1%/점과 공식, 탄약 표 4종·전 무기 해석, LMG만 enemyOnly·튜브는 샷건만, LMG·탄띠·튜브가 상인·모든 LootTableSO에 없음, 샷건 아이템 = 빈 튜브, 재실행 바이트·GUID 동일).
기존 데이터 테스트 갱신(정확 목록 단언에 M7 추가분을 이어 붙임 — 값 단언 변경 없음): `M5DataAssetTests`(가격 표 범위·상인 목록·M5TestData 비교는 앞부분), `M4DataAssetTests.LootCrate_MatchesDesign`.
7C가 상인·LT-CRATE에 더 넣으면 이 세 단언(`TraderAdditionIds`·`CrateAdditions` 이어 붙이기)도 같이 고친다.

## 7A-2b 구현 메모 (gameplay-programmer, 2026-10-22)

사본 EditMode 1414/1414 통과(새 `PlayerInventoryLooseAmmoTests` 23 포함), 사본 PlayMode 인벤토리 관련 필터(InventoryFlow·LootSearchFlow·LoadoutInjection·RaidEndFreeze·CorpseLootFlow·Greybox·M2SceneSmoke) 13/13 통과. 기존 테스트 단언 변경 없음.

**바꾼 파일**: `Inventory/PlayerInventory.cs`, `Inventory/PlayerInventoryBehaviour.cs`, 새 `Tests/EditMode/PlayerInventoryLooseAmmoTests.cs`. Core·테스트 도우미·데이터 메뉴는 그대로.

**정한 것·설계와 다른 점**
- `PlayerInventory : …, ILooseAmmoSource`. 범위·순서는 일반 재장전과 같다 — 조끼 → 주머니 → (`InventoryConfigSO.reloadFromBackpack`일 때) 배낭. 열린 루트·장착 슬롯은 제외.
  "탄이 가장 적은 상자부터"는 **범위 전체**에서 고르고, 같으면 먼저 찾은 것(조끼 → 주머니 번호 → 배낭, 그리드 안은 배치 순). 그리드 우선이 아니다(주머니 2발 상자가 조끼 6발 상자보다 먼저). `LocalMagazineSource`·`TakeFullest`와 같은 방식.
- 잠긴 상자(`SetItemLocked`) 제외, `IsFrozen`(사망·레이드 종료)이면 `HasLooseRound` false·`TakeLooseRound` false(변경 없음·알림 없음). 할당 없는 순회.
- `TakeLooseRound` 성공마다 `Changed` → 브리지가 `InventoryChanged`(무게 변화면 `CarriedWeightChanged`도). 0.5초마다 1회라 이벤트를 묶지 않았다.
- 빈 상자는 그리드에 0발로 남는다(Q6). `IsLoadable`(= `HasLoadable`·`TakeFullest`)에서 `isAmmoBox` 제외 — 같은 탄의 상자가 탄창보다 많이 들어 있어도 탄창을 고른다.
- "상자는 무기에 장착 불가": 인벤토리에 탄창을 무기에 넣는 명령은 없고 장착 슬롯은 `EquipRules`가 이미 거부한다(테스트로 확인). 추가로 브리지의 `startingItems.loadedMagazine`이 탄 상자이거나 무기가 튜브 무기면 넣지 않고 경고(튜브 무기는 빈 튜브로 시작).
- 브리지가 `ILooseAmmoSource`를 같은 인스턴스로 등록/해제(설계 §4.3). 컨트롤러는 7A-1 메모대로 `IMagazineSource` 하나만 넘겨도 `source as ILooseAmmoSource`로 같은 인스턴스를 얻는다 — 레지스트리 조회는 선택.
  Core `ServiceRegistry` 주석의 등록 목록에는 아직 `ILooseAmmoSource`가 없다(Core 소유라 고치지 않음 — 7A 통합 때 한 줄 추가 권장).
- `HideoutInventoryBehaviour`는 바꾸지 않았다(은신처엔 재장전이 없어 `IMagazineSource`도 등록하지 않음 — 낱탄도 같게).

**테스트(23)**: 같은 인스턴스(`as` 경로), 상자 없음, 1발 차감 + Changed 1회, 빈 상자 남음·다시 꺼내기 실패, 다른 탄 거부, 일반 12ga 탄창은 낱탄 아님, 적은 상자부터·빈 상자 건너뛰기,
같으면 조끼 > 주머니·앞 주머니 먼저, 주머니 적은 상자가 조끼보다 먼저, 배낭 기본 제외·설정 시 조끼·주머니 뒤, 열린 루트 제외, 잠김·사망·레이드 종료 거부,
상자로 일반 재장전 안 됨(12ga·같은 탄 5.56 상자 vs 탄창), 상자 장착 불가(모든 슬롯), `WeaponModel`(샷건) + PlayerInventory 튜브 6발 장전(2발 상자 먼저 비우고 남음), 사망 후 튜브 장전 거부.

## 7A-2c 구현 메모 (gameplay-programmer, 2026-10-22)

사본(`scratchpad/copy_7A2c`)에서만 메뉴 실행·테스트. 원본에는 아래 코드·테스트 파일만 썼고 WAV·에셋은 만들지 않았다(3단계 통합이 원본에서 메뉴 실행).
사본 `-executeMethod Ashfall.EditorTools.M6AudioSetup.CreateAssets` 2회: 1회째 "WAV 새로 씀 8, 임포트 설정 변경 8, 빈 필드 채움 10"(세트 2개 추가 포함),
2회째 "0, 0, 0" + `Assets`·`ProjectSettings`(테스트 폴더 제외) 전체 md5 동일(WAV 바이트·.meta GUID·AudioConfig 바이트 불변).
EditMode 1430 중 1429 통과 — 실패 1개는 예상된 것(아래 "통합이 고칠 것"). PlayMode 오디오 필터(AudioFlowTests 20·AudioSceneMusic·M6RaidFlow·M6RaidSceneSmoke·HideoutSceneSmoke·M6Performance) 26/26, PlayMode 전체 122/122(592초, 사본 — 2a·2b 작업 중인 원본을 복사한 시점 기준).

**새 자리표시 WAV (8개, 22050Hz 모노 PCM16, 전부 TODO(GDD-?))**
- `Audio/Placeholder/Gunshots/GS_Shotgun_{Near,Mid,Far}.wav` — 산탄: 낮고 넓은 폭발(크랙 ×0.6, 몸통·펀치 ×1.6)
- `Audio/Placeholder/Gunshots/GS_Sniper_{Near,Mid,Far}.wav` — 저격: 날카로운 크랙(×2.2) + 긴 메아리(크기 ×1.3, 간격 ×1.25)
- `Audio/Placeholder/Weapon/WPN_DryFire.wav` — 3ms 금속 클릭 + 짧은 공진(0.1초). `dryFireClip`(DryFire·Jammed 공용)
- `Audio/Placeholder/Weapon/WPN_JamClear.wav` — 슬라이드 긁힘 노이즈 + 클릭 2개(0.4초). `jamClearClip`
- 경로는 `M7Names.DryFireClipPath`·`JamClearClipPath`(폴더 `Weapon/`, 파일 `WPN_DryFire`) — 설계 §2.2 표의 `Weapons/WPN_DryClick.wav`와 이름이 다르다(계약 M7Names 우선). 합계 WAV 27 → 35.

**구현**
- `SfxSynth`: `GunParams`에 `Crack`·`Body`·`Echo`·`EchoSpread` 배율(기본 1 — M6 분류 권총·SMG·소총은 비트 단위로 같음, 커밋된 WAV와 바이트 비교 테스트로 확인). Shotgun·Sniper 값만 바꿈.
  새 레시피 `DryFireClick()`(시드 6001)·`JamClear()`(6002), 길이 상수 `DryFireSec` 0.1·`JamClearSec` 0.4.
- `M6AudioSetup`: `GunshotClasses`(M6 3개, 그대로)와 별도로 `M7GunshotClasses = { Shotgun, Sniper }`, `AllGunshotClasses()`, `M7Recipes()`(8개), `AllRecipes()` = `Recipes()`(M6 27개, 그대로) + M7.
  `CreateAssets`는 `AllRecipes()`를 쓴다. `FillAudioConfig`: M6 세트는 기존대로(없으면 경고), **M7 세트는 없으면 `gunshots` 끝에 추가**(기존 세트 객체·순서·사용자 클립 보존) 후 빈 near/mid/far만, `dryFireClip`·`jamClearClip`은 빈 필드만.
  파일 이름 규칙 `ClassFileName`: AssaultRifle → Rifle, **MachineGun → Lmg**(7B용 이름만 미리), 그 밖 = enum 이름.
  **7B LMG 세트 추가 방법(데이터만)**: `M7GunshotClasses`에 `WeaponClass.MachineGun` 한 항목 → WAV `GS_Lmg_{Near,Mid,Far}` 생성·세트 추가·클립 연결이 같이 된다. 음색은 `SfxSynth.ClassParams`에 case 하나(없으면 기본 = 소총 음색).
- `AudioConfigSO`: `dryFireClip`·`jamClearClip`, `dryFireVolume` 0.8·`jamClearVolume` 0.8(TODO(GDD-?)). **코드 기본 `gunshots`에는 Shotgun·Sniper를 넣지 않았다**(설계 §4.9와 다름) —
  기존 `GunshotMixTests.FindSet_ByClass_FallbackForNullOrMissing`이 기본 인스턴스로 "세트 없는 분류(Shotgun) → 대체"를 단언한다. 실제 에셋은 메뉴가 세트를 추가한다.
- `FootstepMix.PriorityWeaponClick = PriorityEnemyFootstep`(2): 총성 > 금속 붕괴 > 적 발소리 = 무기 클릭 > 내 발소리. TODO(GDD-?)
- `AudioDirector`: `WeaponClicked` 구독(기존 Subscribe/Unsubscribe 짝 — OnEnable/Update 재시도/OnDisable/OnDestroy), 캐시한 델리게이트(할당 0).
  DryFire·Jammed → `dryFireClip`, JamCleared → `jamClearClip`, 이벤트 위치에서 3D voice, 게인 = 음량 필드, 피치 1, 거리 판정 없음(플레이어 자신), 클립이 비면 무음.
  `PlayedSoundKind.WeaponClick`(끝에 추가), 공개 읽기 `LastWeaponClick`. 내부 `Play`가 bool을 돌려준다.

**테스트(추가만, 기존 단언 변경 없음)**
- EditMode `AudioConfigAssetTests` +6: M7 레시피 8개·경로·M6과 안 겹침·`AllRecipes` 순서·`GS_Lmg_*` 이름, 에셋 세트 5개(M6 순서 앞부분 유지·세트 중복 없음·근 < 원), 클릭 클립 연결·길이, M7 WAV 임포트(모노·Vorbis·DecompressOnLoad/Compressed),
  재실행 M7 WAV·AudioConfig 바이트·GUID 불변, M6 모양 설정(세트 3개)에 Fill → 끝에 2개 추가·사용자 클립 보존·두 번째 0.
- EditMode `SfxSynthTests` +10: M7 레시피 8개(길이·피크·결정적·무음 아님), 산탄 ≠ 저격·소총보다 길다·저격 크랙이 더 날카롭다, M6 총성 합성 = 커밋된 WAV 바이트(교체된 WAV는 건너뜀).
- PlayMode `AudioFlowTests` +7: 클릭 3종 → 클립·위치·게인·3D·우선순위 < 내 발소리, 클립 없음 → 무음, 내 발소리로 찬 풀에서 빼앗기, 총성으로 찬 풀에서 버림, 비활성 → 구독 해제·재활성 → 재구독,
  산탄·DMR `ShotFired` → 각 세트 레이어, 클릭 300 이벤트 GC 0.

**통합(7A-3)이 할 일**
- 원본에서 `Ashfall/Setup/M6/Create Audio Assets`(배치 `-executeMethod Ashfall.EditorTools.M6AudioSetup.CreateAssets`, 또는 이를 부르는 `M6SceneSetup.ApplyAll`/7A-3 `M7SceneSetup.ApplyAll`) 2회 — 두 번째 0/0/0·바이트 동일.
  새 WAV 8개·.meta·`Weapon/` 폴더 .meta, `Data/Audio/AudioConfig.asset`(세트 2개·클릭 2필드·음량 2필드) 변경이 생긴다.
- **`EditMode/M6DataAssetTests.AudioConfig_NumbersMatchDesign` 한 줄 갱신 필요**(7A-1b가 M4/M5 테스트에 한 것과 같은 "이어 붙이기"): 기대 목록 `{ Pistol, Smg, AssaultRifle }` →
  `{ Pistol, Smg, AssaultRifle, Shotgun, Sniper }`(또는 앞 3개 비교). 메뉴 실행 뒤 에셋 세트가 5개라 지금은 실패한다(2c 소유 파일이 아니라 고치지 않음). 7B가 MachineGun을 더하면 다시 이어 붙인다.
- 사람 확인(에디터): 샷건·DMR 사격 시 근/중/원 소리 구분, 빈 탄창·고장 헛격발 클릭과 T 길게 해제 소리가 총구 쪽에서 들리는지.

## 7A-2a 구현 메모 (gameplay-programmer, 2026-10-22)

사본(`scratchpad/copy_7A2a`)에서만 컴파일·메뉴·테스트. EditMode 1445/1445(새 31: `WeaponSwitchModelTests` 16·`JamClearModelTests` 9·`PelletSpreadTests` 6), PlayMode 129/129(새 14: `WeaponSwitchFlowTests` 6·`JamFlowTests` 3·`TubeReloadFlowTests` 5, 575초).
사본 격리: 복사 시점에 섞인 2c(Audio) 진행 중 파일 8개·생성 WAV·AudioConfig.asset은 사본에서 HEAD로 되돌리고 돌렸다(2b Inventory 파일은 복사 시점 상태 그대로 포함). 기존 테스트 단언 변경 없음.

**원본에 쓴 파일**: `Weapons/WeaponController.cs`(재작성)·`WeaponSwitchModel.cs`(새)·`JamClearModel.cs`(새)·`Ballistics.cs`(`PelletDirections`·`ShotDirections`)·`MagazineCheckModel.cs`(주석만), `EditorTools/M7WeaponAssets.cs`(새),
`Tests/EditMode/WeaponSwitchModelTests.cs`·`JamClearModelTests.cs`·`PelletSpreadTests.cs`, `Tests/PlayMode/WeaponSwitchFlowTests.cs`(+ 공용 `M7WeaponTestAssets`·`WeaponCodeScene`)·`JamFlowTests.cs`·`TubeReloadFlowTests.cs`. .meta·프리팹은 원본에 없음.

**설계와 다른 점·구체화**
- 프리팹 이름은 §2.2 `VM_{Pistol,…}`가 아니라 계약 `M7Names.ViewModelPrefabPath(id)` = `Prefabs/Weapons/VM_WPN-PST-01.prefab`·`VM_WPN-SMG-01`·`VM_WPN-RIF-01`·`VM_WPN-SHT-01`·`VM_WPN-DMR-01`(LMG 없음).
  메뉴 `Ashfall/Setup/M7/Create Weapon View Models`(배치 `Ashfall.EditorTools.M7WeaponAssets.CreateViewModels`, bool — 무기 정의가 없으면 false). 프리팹은 없을 때만 만들고, **무기 정의 5개의 `viewModel`을 비어 있을 때만 채운다**(데이터 에셋이 바뀐다).
  사본 2회 실행 → 프리팹 5·WPN 6개 md5 동일. 팔레트 머티리얼(M_RustMetal·M_Debris)이 없으면 기본 머티리얼 + 경고. 그림자·콜라이더 없음. 소총 프리팹 몸통·총구(z 0.3)는 M1/M2 직렬화 큐브와 같다.
- 컨트롤러는 직렬화 `weapon`과 다른 정의도 든다(기존 "다르면 비무장 + TODO(M7) 로그" 제거). `weapon`이 비어도 Awake에서 꺼지지 않는다(인벤토리 씬은 안 씀, M1 경로만 경고 후 비무장).
- 탄약: `handling.TryGetAmmo` → 없으면 직렬화 `ammo`(같은 종류일 때) → 둘 다 없으면 투사체 없이 사격(소리·소음은 냄, 경고 1회). `ammo` 필드 이름은 그대로(EnemyDamageFlowTests가 리플렉션으로 읽음).
- 시작 무기(Q2): 컨트롤러 Start, 또는 인벤토리 Start의 `EquipmentChanged`가 먼저 오면 그때 `PickInitial` — 교체 시간 없음. 든 슬롯이 아닌 슬롯의 `EquipmentChanged`는 무시(키를 누를 때 `GetEquipped`). 교체 중 다른 슬롯 키 = 그 슬롯으로 다시(시간 처음부터).
- 낱탄 공급원: 레지스트리 `ILooseAmmoSource`(없으면 `IMagazineSource` 캐스트). 인벤토리 상자 → 튜브 경로는 2b `PlayerInventory`에 기대며, 2a PlayMode는 설계대로 `LocalMagazineSource` 코드 장면으로만 확인했다(통합 PlayMode에서 확인 필요).
- 인벤토리 없는 씬(M1 경로)의 튜브 무기: 튜브 가득 + `magazine`(탄 상자) × `startingMagazines` (TODO(GDD-?) Q5). 코드 장면·테스트용 `WeaponController.Configure(weapon, ammo, magazine, magazines, handling, projectiles, seed)` 공개(AudioDirector.Configure 방식).
- T: 고장 중이면 누르는 동안 해제 진행(탄창 확인 안 함), 고장이 아니면 누름 = RULE-04 탄창 확인. 짧게/길게를 시간으로 가르지 않고 고장 여부로 가른다(고장 아닐 때 T를 오래 눌러도 확인 1회). 교체·화면 열림·사망·레이드 종료 = 해제 진행 리셋.
- 헛격발 `DryFire`: 새로 당김 + (고장 또는 탄창 없음/0발) + 재장전 중 아님. 튜브 장전 중 0발 방아쇠는 소리 없음. 고장 순간 `Jammed`와 같은 프레임에 겹치지 않는다.
- 무기 모델 캐시는 지우지 않는다(레이드 씬 수명, 보통 3개). 같은 아이템을 다시 들면 같은 `WeaponModel`.
- 든 무기 뷰모델을 무기 루트의 첫 자식으로 옮긴다 — 계층 순서로 첫 "Muzzle"을 찾는 `M3TestSupport.PlayerMuzzle`이 숨은 직렬화 큐브의 총구(조준 위치로 안 움직임)를 잡아 `CorpseLootFlowTests` 헤드샷이 빗나가던 문제를 이렇게 막았다. 직렬화 뷰모델은 프리팹 무기를 들면 비활성.
- 공개 읽기 추가: `ActiveSlot`·`IsSwitching`·`JamClearProgress`(설계대로) + `CurrentAmmo`·`CurrentViewModel`·`MuzzlePosition`.
- `JamFlowTests`는 결정적 고장을 위해 확률/점만 0.02로 올린 handling 사본(내구도 0 → 100%)을 쓴다(설계 "내구도 0 + 시드" 대신). 실제 값(50·0.1%)은 7A-1·1b 테스트가 본다.

**7A-3 통합이 할 일**
- `M7SceneSetup.ApplyAll`(7A 몫): `M7WeaponDataSetup.CreateData()` → (M6 팔레트 머티리얼이 있는 상태에서) `M7WeaponAssets.CreateViewModels()` → 오디오. 원본에서 실행하면 `Assets/_Project/Prefabs/`·`Prefabs/Weapons/` 폴더·프리팹 5개·.meta와 WPN-PST/SMG/RIF/SHT/DMR.asset `viewModel` 변경이 생긴다.
- 씬 연결은 필요 없다: 컨트롤러가 무기 정의의 `viewModel`을 런타임에 읽고, 기존 `handling` 참조로 탄약 표·교체·고장 수치를 읽는다. M1/M2/M3 확인 씬도 메뉴 실행 뒤 프리팹 모델을 보여 준다(직렬화 큐브는 숨김).
- 통합 PlayMode(`M7WeaponSceneSmokeTests`): 레이드 씬 Player의 `WeaponController.handling` = WeaponHandlingConfig(탄약 표 4종), 무기 5종 `viewModel` 연결. 7A-4: 인벤토리 탄 상자 → 샷건 R(2b 경로).
- 사람 확인(§7.1 W1·W2·W5·W7): 1/2/3 체감·모델 구분, 펠릿 흩어짐, 한 발씩 장전·사격 중단, 헛격발·T 길게 해제 감, 자리표시 모델 크기·위치(hip/ads 공용 값 — 무기별 조정 없음, TODO).

## 7A-3 구현 메모 (gameplay-programmer, 2026-10-22)

원본에서 7A-1·1b·2a·2b·2c를 처음 함께 컴파일 — **컴파일 오류·충돌 없음**. 첫 EditMode 1461 중 1453 통과, 실패 8개는 전부 "원본에 7A-2c WAV·AudioConfig 세트가 아직 없음"(AudioConfigAssetTests 7 + `M6DataAssetTests.AudioConfig_NumbersMatchDesign`) — 코드 문제 아님.
(주의: 그 실행에서 `AudioConfigAssetTests.Rerun_*`가 실제 `M6AudioSetup.CreateAssets`를 불러 원본에 새 WAV 8개·AudioConfig 변경을 먼저 만들었다. 결과는 메뉴 실행과 같은 함수·같은 바이트.)

**바꾼·새 파일**
- 새 `EditorTools/M7SceneSetup.cs`: 메뉴 `Ashfall/Setup/Apply M7 Content`(배치 `Ashfall.EditorTools.M7SceneSetup.ApplyAll`).
  순서: (`M6SceneSetup.RaidAssetsExist()`가 false면 `M6SceneSetup.ApplyAll()` 먼저 — 팔레트 머티리얼·AudioConfig) → `CreateAssets()` = `M7WeaponDataSetup.CreateData` → `M7WeaponAssets.CreateViewModels`(false면 예외) → `M6AudioSetup.CreateAssets`
  → 레이드 씬 열기 → `ApplyRaid`(7A는 빈 몸통 — 씬 연결 없음) → **씬이 dirty일 때만 저장**(지금은 저장 안 함 → 재실행 바이트 동일) → SaveAssets.
  7B·7C 확장 자리: `CreateAssets`·`ApplyRaid`에 한 줄씩(주석으로 표시), `RaidAssetsExist`에 `&&` 조건. 은신처 적용이 필요하면 7C가 `ApplyHideout`을 같은 모양으로 더하고 `M5SceneSetup`에 한 줄.
  실패는 전부 예외(`ApplyRaidIfAssetsExist` → `Require`, 씬 없음은 경고 후 건너뜀 — M6과 같음).
- `M4SceneSetup.CreateRaidScene`: 저장 직전 `M6SceneSetup.ApplyRaidIfAssetsExist` 다음 줄에 `M7SceneSetup.ApplyRaidIfAssetsExist(scene)` (M4 메뉴는 이번에 실행하지 않음 — 컴파일만 확인).
- Core `ServiceRegistry` 주석 등록 목록에 `M7: ILooseAmmoSource(Inventory)` 한 줄(7A-2b).
- `EditMode/M6DataAssetTests.AudioConfig_NumbersMatchDesign`: 기대 분류 목록 끝에 `Shotgun, Sniper` 이어 붙임(7A-2c). 다른 단언 변경 없음.
- 새 `PlayMode/M7WeaponSceneSmokeTests.cs` 2케이스: ① 레이드 씬 Player의 WeaponController 1개·`handling` = WeaponHandlingConfig(SerializedObject로 읽음)·탄약 표 4종(9mm·5.56·7.62·12ga),
  무기 5종 `viewModel` = `Prefabs/Weapons/VM_{id}.prefab`·"Muzzle" 자식·탄약 해석, 시작 무기가 프리팹 뷰모델(Muzzle 있음)을 듦, 1초 실행 오류 없음.
  ② 실제 인벤토리 경로: 레지스트리 `ILooseAmmoSource` = 플레이어 인벤토리, 빈 튜브 샷건 → Primary2(`TryEquipNew`), BOX-12GA-8 → `TryAutoStore`, 키 2로 교체 → R → 6발(상자 8 → 2, 상자는 그리드에 남음).
  7A-4 `M7WeaponRaidFlowTests`의 "인벤토리 탄 상자 → 샷건 R"은 ②로 이미 확인됨(7A-4는 은신처 반입 경로로 다시 봐도 된다).

**원본 ApplyAll 2회**: 1회째 = 프리팹 5개·`Prefabs/Weapons.meta`·WPN-PST/SMG/RIF/SHT/DMR `viewModel` 채움(데이터·WAV는 이미 있음 → "WAV 0·임포트 0·채움 0", 레이드 씬 "변경 없음").
2회째 = `Assets`·`ProjectSettings` 전체 1141 파일 md5 동일(.meta GUID·데이터·WAV·프리팹·씬). EditMode·PlayMode 전체 실행 뒤에도 동일.

**결과**: EditMode 1461/1461, PlayMode 138/138(579초, 새 M7WeaponSceneSmoke 2 포함).

**사람 확인(에디터)**: `Ashfall/Setup/Apply M7 Content` 1회(이미 원본에 적용됨 — 다시 돌려도 변경 없음) → Raid 씬에서 1/2/3 교체 시 프리팹 모델이 보이는지, §7.1 W1·W2·W5·W7 및 7A-2c 소리 항목.

## 7A-4 구현 메모 (test-engineer, 2026-10-22)

원본에서 실행(Unity 한 번에 하나). **EditMode 1461/1461, PlayMode 141/141 × 2회 연속**(611초·606초, 새 M7WeaponRaidFlow 3 포함), **Mono 빌드 성공**(씬 4개 Boot·MainMenu·Hideout·Raid_CrimsonBasin, 오류 0·경고 0, 110,083,110 bytes, 38초).
테스트 전후 `Assets`·`Packages`·`ProjectSettings`(테스트 폴더 제외) 커밋 대상 변화 없음(git status 기준 — 빌드 중 ProjectSettings.asset이 잠시 바뀌었다가 원래대로 돌아옴). 제품 코드·공용 테스트 도우미는 고치지 않았다.

**새 `PlayMode/M7WeaponRaidFlowTests.cs`** (M5Hideout·M4Raid·M5Save·M5Flow 도우미 그대로 사용, SaveSandbox 새 폴더)
- `KitPistolOnly_FlowRaid_Key3_ReloadAndFire`: 메인 메뉴 → 은신처 → [부랑자 키트] → 권총 Secondary + 9mm 탄창 2 주머니 → [레이드 시작] → 시작 무기 = Secondary(Q2, 무장 — M5 Q16 해소), 키 3 = 같은 슬롯(교체 없음), R(주머니 탄창 12발) → LMB → `ShotFired`(권총·Player) 1, 투사체 1, `NoiseEmitted` 1, 탄 11, 내구도 −0.06.
- `ShotgunDmr_FlowRaid_FireExtract_DurabilitySavedAndReloadedFromFile`: `DebugAddCasings(5000)` 후 [상인]에서 SHT·DMR·MAG-762-5·BOX-12GA-8 구매(UI 버튼) → SHT Primary1·DMR Primary2·탄창/상자 주머니 → 레이드(진입 기록 내구도 100) → 샷건 R(상자 8 → 2, 튜브 6) → 2발(첫 발 투사체 8, ShotFired = 발 수) → 키 2 → DMR R → 2발(투사체 1) → 내구도 100 − n × 손실 → EXT-01 탈출 → 결과 [확인] → 은신처 장비 내구도 = 레이드 끝 값,
  save.json v2 loadout 기록(durability·`MAG-12GA-TUBE` 4발·`MAG-762-5` 3발) → [메인 메뉴](W2) → 같은 폴더 저장소 다시 꽂기(다음 실행 흉내) → [은신처] = 파일에서 새 인스턴스로 같은 내구도·탄 수, 고장 없음.
- `V1SaveFile_HideoutEntry_WeaponsMigratedTo100_ResavedAsV2`: v1 save.json(소총에 `durability: 0.0`을 넣어 버전이 정함을 확인)을 샌드박스에 두고 첫 진입 → Loaded·알림 없음·탄피 777, 소총·권총 내구도 100, 탄 수 그대로 → 진입 저장(W1) = version 2(무기 100, 탄창 −1), `.bak` = 원래 v1 → 다음 실행 다시 읽기도 100.
- "인벤토리 탄 상자 → 샷건 R"은 7A-3 `M7WeaponSceneSmokeTests`에 더해 은신처 반입 경로(위 ②)로 다시 확인됨.

**AC1(무기 부분) 근거**
| 항목 | 판정 | 근거 테스트 |
|---|---|---|
| 5종 모두 플레이어가 사격 | 자동 | `WeaponSwitchFlowTests.FiveWeapons_OneShotEach_ShotFiredPelletsNoise`(PST·SMG·RIF·SHT·DMR 각 1발·투사체 수·소음), `M7WeaponRaidFlowTests`(흐름 레이드: PST·SHT·DMR), `M7WeaponDataAssetTests`(GDD §5.2 값) |
| 1/2/3 교체 | 자동 + 사람(W1 체감) | `WeaponSwitchFlowTests`(교체 시간·빈/같은 슬롯 무시·재장전 취소·비무장), `WeaponSwitchModelTests`, `M7WeaponRaidFlowTests`(키 2·3) |
| 고장·T 길게 해제 | 자동 + 사람(W5 소리·감) | `JamFlowTests`(고장·헛격발·T 짧게 해제 안 됨·1.2초 해제·탄 보존, 빈 탄창 헛격발, 고내구도 무고장), `JamRulesTests`·`JamClearModelTests`·`WeaponModelWearTests`, `AudioFlowTests`(클릭 소리) |
| 내구도 저장 | 자동 | `SaveSerializerV2Tests`(왕복·v1 이전·튜브), `M7WeaponRaidFlowTests`(실제 파일 저장·다시 읽기·v1 이전) |
| 튜브·펠릿 | 자동 + 사람(W2) | `TubeReloadTests`·`TubeReloadFlowTests`·`PelletSpreadTests`·`LooseAmmoSourceTests`·`PlayerInventoryLooseAmmoTests`·`M7WeaponSceneSmokeTests` |
| 체감·소리·모델 | 사람 | §7.1 W1~W8 |

**발견한 결함**: 없음.

## 7A 리뷰 수정 메모 (gameplay-programmer, 2026-10-22)

`docs/design/M7_review.md` "7A 무기" 지적 처리. 원본에서 실행(Unity 한 번에 하나).

**중간**
1. 튜브 장전 중 사격(Q7) — 수정. `WeaponModel.TryFire`가 발사 판정(`FireControl`)을 먼저 하고 true일 때만 `CancelReload()` 후 발사한다.
   튜브 장전 중에도 실제 방아쇠 상태를 `FireControl`에 넘겨(0발이어도) 누른 채 시작한 장전이 첫 발 뒤 "새 당김"으로 끊기지 않는다. 탄창 재장전 중·고장 중은 기존대로 방아쇠를 넘기지 않는다.
   Core `FireControl.Tick(now, trigger, canFire)`(canFire = false면 당김 상태만 기록, 간격 안 씀)·`WeaponModel.TryFire(now, trigger, allowShot)` 오버로드 추가(기존 2인자 = true).
   테스트 `TubeReloadTests`: `FireDuringReload_TriggerHeldSinceBeforeR_KeepsReloading`, `FireDuringReload_ClickWithinFireInterval_KeepsReloading`, `TryFire_NotAllowed_TracksTriggerWithoutFiring`.
2. 고장 상태가 은신처 귀환 뒤에도 남음(Q14) — 수정. `ProfileService.ApplyRaidResult`가 은신처 장비가 되는 Loadout의 모든 `WeaponItem.IsJammed = false`(장착·저장 공간 모두, 내구도 그대로).
   테스트: EditMode `ProfileServiceTests.ApplyRaidResult_Extracted_JammedWeaponCleared_DurabilityKept`, PlayMode `M7WeaponRaidFlowTests.ShotgunDmr_…`에 "DMR 고장 상태로 탈출 → 은신처 장비 해제" 단언(같은 인스턴스가 다음 레이드 반입품).

**낮음**
3. 재실행 테스트 위험 — 수정. `AudioConfigAssetTests`의 두 재실행 테스트는 `AssumeRerunWritesNothing()`(M6+M7 레시피 WAV 전부 있음 + AudioConfig 메모리 사본에 `FillAudioConfig` = 0)일 때만 돌고 아니면 Inconclusive. 머리 주석에 위험 기록.
   `M7WeaponDataAssetTests` 멱등 테스트는 메뉴(M1~M6 CreateData 포함) 대신 `WriteM7WeaponData()`만 부르고(메뉴 경로는 MenuItem 특성으로 확인), 머리 주석에 위험 기록.
4. 기존 무기 마모 0 고정 — 수정("0이면 미설정"). `M7WeaponDataSetup.ShouldWriteExistingWear(serialized, current)` = 필드 없음 또는 값 ≤ 0(NaN 포함)이면 기록. 테스트 7케이스. 지금 에셋 값은 그대로(0.06/0.04/0.05).
5. 누른 채 교체 끝 → 반자동 1발 — 수정. 컨트롤러가 교체 중에도 실제 방아쇠(화면·사망·레이드 종료는 false 그대로)를 `TryFire(now, held, allowShot: canAct)`로 넘긴다.
   테스트 PlayMode `WeaponSwitchFlowTests.TriggerHeldThroughSwitch_SemiWeapon_NoShotUntilNewPull` + EditMode `TryFire_NotAllowed_…`.
6. 7B 선행(MachineGun 등) — 수정(문서). §4.1 [7B 계약]·§6 7B-1·7B-1b 문구를 "7A-1b에서 추가됨, 다시 추가하지 않음"으로. CHANGELOG에도 기록.
7. `SetAsFirstSibling` — 수정. `M3TestSupport.PlayerMuzzle`(Transform, 첫 "Muzzle" 검색) → `PlayerMuzzlePosition => Weapon.MuzzlePosition`(호출 2곳 갱신), 컨트롤러의 형제 순서 변경 줄 제거.
   `WeaponController.MuzzleChildName`·`M7Names.MuzzleChild`에 "같이 바꿀 것" 주석.
8. 무기 모델 캐시 — 주석만("의도적으로 유지": 다시 들면 같은 모델, 항목 수 = 든 무기 수, 씬과 함께 사라짐).
9. SaveSerializer 낡은 주석 — 수정(`FirstVersion(1) ≤ version < CurrentVersion(2)`), 음수 내구도 → 100 해석은 주석과 CHANGELOG에 기록.
10. CHANGELOG — 수정. `docs/CHANGELOG_GDD.md` 끝에 "M7 설계 결정 … — 7A 무기" 절(§8 초안 7A 부분, Q1~Q21, 설계와 달라진 점, 자리표시 WAV 8·프리팹 5, 임시값 목록, 리뷰 수정). 7B·7C·7D는 같은 절 아래에 추가.
11. 테스트 계획 — 처리됨(7A-4). `M7WeaponRaidFlowTests.V1SaveFile_HideoutEntry_WeaponsMigratedTo100_ResavedAsV2`가 실제 ProfileService + SaveSandbox 파일 경로로 v1 → v2 이전을 확인한다. 추가 작업 없음.

**결과**: EditMode 1472/1472(새 11: 튜브 3·ProfileService 1·마모 기록 판정 7, Inconclusive 0), PlayMode 142/142 × 2회 연속(607초·606초, 새 `TriggerHeldThroughSwitch` 1), Mono 빌드 성공(오류 0·경고 0, 110,083,622 bytes, 30초).
테스트·빌드 전후 `Assets`·`Packages`·`ProjectSettings`의 .cs 제외 파일 770개 md5 동일, git status 변화 없음. 커밋하지 않음.

## 7B-1 구현 메모 (ai-programmer, 2026-10-23)

계약만(원본). 새 asmdef·기능 간 참조 없음. 새 `.meta` 안 만듦. EditMode 1498/1498(새 26: `EnemyTacticsFactoryTests` 14, `AnchorTests` 12), 기존 테스트 무변경. 커밋하지 않음.

**Core**
- `Events/GameEvents.cs`: `enum EnemyVocalKind { MutantAlert, MutantAttack }`, `readonly struct EnemyVocalized(Vector3 Position, EnemyVocalKind Kind)`(Position = 적 눈). `WeaponClass.MachineGun`은 다시 추가하지 않음.

**AI 공개 API**
- `AiEnums.cs`: `EnemyTacticsKind { Auto, Scavenger, Raider, Sniper, Mutant }`, `MeleeEvent { None, WindupStarted, Strike, Whiff }`.
- `EnemyArchetypeSO`의 M7 필드(초기값 = 기존 동작, 필드가 없는 기존 SCV·RDR 에셋도 이 값으로 읽힘): `tactics = Auto`, `anchorRadiusM = 0`, `weaponDurabilityRange = (100, 100)`(%),
  `meleeDamage`·`meleeRangeM`·`meleeIntervalSec`·`meleeWindupSec`·`surroundRadiusM`(0), `telegraphSec`·`scanArcDeg`(0). `AiConfigSO`: `glintAngularSizeDeg = 0.35`, `vocalCooldownSec = 4`.
- `EnemyIntent`: `bool Melee`, `bool Telegraph`. `EnemyBrain`은 표적이 안 보이면 `Fire`와 함께 `Telegraph`도 false로 만든다(`Melee`는 건드리지 않음).
- `EnemyBrain`: `static EnemyTacticsKind ResolveTacticsKind(archetype)`(Auto나 정의되지 않은 값 = `usesCover ? Raider : Scavenger`), `internal static IEnemyTactics CreateTactics(archetype)`, 공개 `TacticsKind`, `HasAnchor`, `AnchorCenter`.
- 거점: 순수 `AnchorRules.Clamp(point, center, radiusM)`(수평만 자르고 y 유지, radius ≤ 0 또는 NaN = 무제한). `TacticsContext.SetAnchor`/`ClampToAnchor`/`HasAnchor`/`AnchorCenter`.
  중심 = **첫 Tick 위치**(anchorRadiusM > 0일 때만). `MoveTo`와 `Reached`가 둘 다 자른 점을 쓰므로 반경 밖 목적지(순찰·수색·엄폐·LKP)는 경계에 닿으면 도착으로 본다. LKP 자체는 자르지 않는다.
- `IEnemyTactics.TryTickRelaxed(EnemyState state, c, s, ref i)`(새 훅): Idle·Patrol·Search 의도를 전술이 대신 채운다(true = EnemyBrain 기본 행동 건너뜀). 전이는 여전히 EnemyBrain 몫. SCV·RDR은 false.
- `TacticsContext.LookAround(ref i, s, focus, fallbackForward, yawDeg, period, elapsed, lookAheadM)`(static): EnemyBrain 수색 둘러보기를 옮긴 것(동작 동일). 저격수 훑기에 쓴다(`c.Brain.TimeInState`).
- `EnemyAgent` 훅(7B-2는 이 파일을 고치지 않는다):
  - `ApplyMelee`: `archetype.meleeDamage > 0`이면 `new EnemyMelee(meleeWindupSec, meleeIntervalSec)`. 매 프레임 `melee.Tick(now, inRange, hasLos)`를 부른다.
    inRange = `intent.Melee` && 표적이 살아 있음 && 수평 거리 ≤ `meleeRangeM`, hasLos = 표적이 살아 있음 && 마지막 감각에서 보였음.
    `WindupStarted`면 공격 울음, `Strike`면 표적 Root의 `IDamageable.ApplyDamage(DamageInfo(meleeDamage, 0, 지점, 눈→지점 방향, 거리))`.
    지점 = 표적 발 + Root.right × off.x + up × off.y, 이때 off = `EnemyMelee.PickStrikeOffset(random, 높이, 반지름)`. 높이·반지름은 Root의 `CharacterController`에서 읽고, 없으면 (중심 − 발) × 2와 그 0.2배를 쓴다.
  - `ApplyTelegraph`: Awake에서 `GetComponentInChildren<SniperGlint>(true)`로 한 번 찾는다(이름 검색 없음). `intent.Telegraph` && 보임 && 표적이 살아 있으면 `SetActive(true)` + `Face(target.EyePosition, config.glintAngularSizeDeg)`, 아니면 `SetActive(false)`. 사망 시 끈다.
  - `TickVocal`: 전술이 Mutant일 때만. Alert·Combat이 아니다가 들어가는 순간 `MutantAlert`, 근접 준비 시작마다 `MutantAttack`. 두 종류가 한 쿨다운(`vocalCooldownSec`)을 같이 쓴다.
- 빈 껍데기(시그니처 고정, 내용은 7B-2가 채운다): `Brain/SniperTactics.cs`(전부 Hold + 위협 쪽 보기, `TryTickRelaxed` = Hold·true), `Brain/MutantTactics.cs`(Hold + 보기, 평시는 기본 행동),
  `Combat/EnemyMelee.cs`(`EnemyMelee(windupSec, intervalSec)`, `MeleeEvent Tick(now, inRange, hasLos)` → None, `Reset()`, `IsWindingUp`, `static Vector2 PickStrikeOffset(Random, capsuleHeight, capsuleRadius)`),
  `Runtime/SniperGlint.cs`(MonoBehaviour: `IsOn`, `SetActive(bool)`(같은 값이면 무시), `Face(Vector3 eye, float angularSizeDeg)`).
- `EditorTools/M7Names.cs` 7B 구역: AI·Loot·Relics 폴더, `AI-SNP/MUT/BOS/BGD` id·경로, `LT-SNP/MUT/BOS` id·경로, `ITM-REL-01`(`Data/Items/Relics/`), `SniperGlintObject = "SniperGlint"`, `M7EnemyPrefix = "M7_"`,
  `SniperGlintMaterialPath`(`Art/Materials/M_SniperGlint.mat`), 울음 WAV `Audio/Placeholder/Enemy/MUT_Alert.wav`·`MUT_Attack.wav`.

**설계와 다른 점**
- `EnemyMelee.PickStrikeHeight(Random, capsuleHeight)` 대신 `PickStrikeOffset(Random, capsuleHeight, capsuleRadius)`를 둔다. 반환값 Vector2 = (옆, 높이). PlayerHealth의 부위 판정은 팔을 옆 오프셋(|x| ≥ 반지름 × armZoneSideRatio)으로 가르므로 높이만으로는 팔을 맞힐 수 없다.
- `SniperGlint.Face(eye)` 대신 `Face(eye, angularSizeDeg)`를 둔다. 컴포넌트가 AiConfig를 몰라도 된다.
- 위의 `IEnemyTactics.TryTickRelaxed`·`TacticsContext.LookAround` 추가. 이게 없으면 저격수가 순찰·수색에서 움직이고, 2a가 EnemyBrain.cs를 고쳐야 한다.
- 근접 시야 판정은 매 프레임 레이를 쏘지 않고 감각 주기의 시야 결과를 쓴다(`TODO(GDD-?) Q24`).
- 엄폐 선택은 거점 반경을 모른다. 반경 밖 엄폐를 고르면 경계에서 멈춘다(`TODO(GDD-?) Q29`).

**7B-1b·7B-2가 알아야 할 것**
- 7B-1b: 기존 SCV·RDR은 `tactics`를 쓰지 않아도 Auto다. `weaponDurabilityRange`는 초기값이 (100,100)이라 35~85를 명시해서 넣어야 한다. 섬광은 저격수 자식 "SniperGlint"에 `SniperGlint` 컴포넌트로 붙인다(배치 2c).
- 2a: `SniperTactics`는 `TryTickRelaxed`(Idle·Patrol·Search 훑기, 이동 없음)와 `Telegraph`를 채운다. 표적이 안 보이면 Telegraph는 EnemyBrain이 끈다. 사격 간격은 기존 EnemyWeapon(semiShotIntervalSec)이다.
- 2b: `EnemyMelee.Tick`은 Strike/Whiff를 한 번만 돌려주고 간격을 직접 센다. `MutantTactics`는 `i.Melee = true`(사거리 판정은 Agent가 한다)를 쓰고, 둘레 오프셋에는 `c.Squad.IndexOf`·`Count`를 쓴다.
  Agent는 `intent.Fire`일 때만 표적 쪽으로 돈다. 그러니 MutantTactics는 `LookAt(표적 중심)`을 꼭 넣어야 한다(시야 콘 140°).

## 7B-1b 구현 메모 (data-designer, 2026-10-23)

메뉴 `Ashfall/Setup/Create M7 AI Data`(배치 `Ashfall.EditorTools.M7AiDataSetup.CreateData`, 7B 몫만은 `WriteM7AiData()`). `CreateData`는 `M7WeaponDataSetup.CreateData()`(→ M6 → … M1)를 먼저 부른다.
원본에서 2회 실행 → 두 번째 실행 후 `Data/`·`Audio/`·`Scenes/`·`Settings/`·`Prefabs/`·`Art/` 313개 파일 md5 동일(GUID·.meta 포함). EditMode 1524/1524(새 26). `M7SceneSetup.CreateAssets`의 7B 줄은 7B-3 몫이라 넣지 않았다.

**만든 에셋 (없을 때만)**: `Data/Items/Relics/ITM-REL-01`(재의 군주 인장, Relic, 1×1, 0.2kg, 8000), `Data/Loot/LT-SNP`(1~2회: MAG-762-5 3)·`LT-MUT`(0~2회: JNK-01 2·JNK-03 2)·`LT-BOS`(3~4회: 첫 굴림 = 유물 고정, MAG-762-5 2),
`Data/AI/AI-SNP`·`AI-MUT`·`AI-BOS`·`AI-BGD`(§3.4 표 값, 무장 SNP = DMR + 7.62 + MAG-762-5, MUT = 없음, BOS = LMG + 7.62 + MAG-762-100, BGD = RIF + 5.56 + MAG-556-30).
**기존 에셋 (빈 필드만)**: AI-SCV·AI-RDR `weaponDurabilityRange` 35~85(파일에 없거나 (100,100)일 때만 — 이때 다른 M7 필드도 초기값으로 직렬화됨, `tactics` = Auto 그대로), AiConfig `glintAngularSizeDeg`·`vocalCooldownSec`(파일에 없을 때만 초기값 0.35·4 기록), ItemDatabase 재수집(29 → 30).

**설계와 다른 점·정한 것**
- **Core 변경(작음)**: Q31 "첫 굴림 고정"을 데이터만으로 할 수 없어 `LootTableSO.guaranteedFirstRoll`(ItemDefinitionSO, null = 없음)을 추가하고 `LootRoller.Roll`이 그것을 첫 굴림으로 넣게 했다.
  굴림 1회를 대신하고, 굴림 0회·항목 없음·가중치 합 0이어도 정확히 1개(난수 안 씀). null이면 기존 동작·난수 순서 그대로(테스트로 확인). 시체·상자 모두 LootRoller를 쓰므로 `CorpseLootBuilder`(2b)는 따로 할 일이 없다.
  보장 아이템은 `entries`에 넣지 않는다(넣으면 2개 이상 나올 수 있음 — 설계 §3.9의 "ITM-REL-01 1"은 보장분으로 해석).
- §3.9의 해독제·물병·방독면(7C 아이템)은 아직 없어서 LT-SNP·MUT·BOS에 넣지 않았다. **7C가 이 세 표에 "없으면 끝에 추가"로 붙인다**(LT-SNP: 해독제 1·물병 1 / LT-MUT: 해독제 0.3 / LT-BOS: 방독면 1·해독제 1). `M7AiDataAssetTests`의 `AssertEntries` 정확 목록도 이어 붙일 것.
  §3.9 "LT-SCV·LT-RDR에 물병 1·통조림 1·BOX-12GA-8 0.5(SCV)"도 같은 문장이라 7C 몫으로 남겼다(이번에 SCV에 탄 상자도 넣지 않음).
- 표에 행이 없는 M3 필드(전부 TODO(GDD-?)): SNP 오차 배율(unsettled/selfMoving/targetMoving) = RDR 값, 점사 = 0(반자동), preferredRange = 0, 엄폐 시간·tacticalReload·strafe = 0.
  BOS = 표에 없는 칸은 RDR 값(aimSettle·배율·burstErrorGrowth·semi 간격·preferredRange 15~40·peek/hide·tacticalReload 0.3·strafe 0), "RDR 오차 × 1.3"은 aimErrorBaseM·aimErrorPerMeterM에만(0.26·0.0325).
  BGD = 생성 시 AI-RDR 에셋 값을 복사(수동 조정이 있으면 그 값). MUT의 사격·반응 칸("—") = 0(reactionTimeSec 0 → Alert에서 보이는 즉시 Combat). MUT·BOS `weaponDurabilityRange` = (100,100)(표 "—").
- 내구도 미설정 규칙: (100,100)은 코드 초기값이라 미설정으로 본다(7A 마모 0 규칙과 같음). 기존 유형을 의도적으로 100~100으로 두려면 99.9~100처럼 쓸 것(`TODO(GDD-?) Q12`).
- displayName: "저격수"·"변이체"·"재의 군주"·"호위병"(§7 + Q28).

**`M7EnemyLayout` (EditorTools, 데이터 + 순수 검사, 씬 적용 없음)**
- `Kind { Sniper, Mutant, Boss, Bodyguard }`·`ArchetypePath(kind)`. `SquadPlan { Name(M7_ 포함), PoiId, SquadId, Members[], StartsXZ[](대원별), RouteXZ(null = 정지), WatchXZ?(퍼치만), YawDeg, MemberName(i) = Name + (i+1), IsPerch }`.
  M4 `SquadSlots`는 3자리라 4명 무리가 겹친다 — 그래서 시작 위치를 대원마다 명시했다(2c는 경로가 있어도 `StartsXZ`를 쓸 것). 장비 인덱스는 모두 0.
- 배치(TODO(GDD-?) Q30): M7_SNP_PYL (240, 296) → 감시 (150, 210) / M7_SNP_HWY (120, −430)(맵 가장자리 상승 비탈) → 감시 (120, −330) /
  M7_MUT_REF 시작 (232, 15) 주변, 배회 (232,15)–(275,15)–(275,45)–(232,45)(설계 (300, 40)은 SP-E에서 179m라 서쪽으로 옮김) / M7_MUT_HWY (142, −290) 주변, 배회 (142,−290)–(185,−290)–(185,−320)–(142,−320) /
  M7_BOS_VIL 보스 (−270, 120), 호위 (−266, 116)·(−274, 116)·(−270, 125). 분대 번호 10~14(기존 1~9와 안 겹침, 보스·호위 한 분대 14).
- 검사: `CheckCounts`(14 = SNP 2·MUT 8·BOS 1·BGD 3, `ExpectedTotalEnemyCount` = 20 + 14 = 34), `CheckSpawnDistances([sniperSight])`(모든 시작·순찰 점 ≥ 190m, 저격수 ≥ 시야 150 + 50), `CheckInsideMap`(경계 485m 안),
  `CheckSquadIds`, `CheckBossGroup(anchor)`(호위 3명, 보스에서 25m 안), `CheckPerchHeights(ground)`(퍼치 지면 − 감시 지점 지면 ≥ 5m, TODO), `CheckAll(ground, sniperSight, anchor, out report)`, `GroundFromTerrainData(TerrainData)`.
  현재 지형 에셋으로 전부 통과(스폰과 가장 가까운 점 = MUT_REF (275, 45) ↔ SP-E 약 200m).

**7B-2가 알아야 할 것**
- 2c `M7EnemySceneApply`: `M7EnemyLayout.CheckAll(xz => M4MapSetup.GroundY(terrain, xz.x, xz.y), 저격수 에셋 sightRange, 보스 에셋 anchorRadiusM, out report)`이 false면 예외. `M3SceneSetup.CreateEnemy`는 이름 앞에 `Enemy_`를 붙인다 — 오브젝트 이름은 `Enemy_M7_…`이 되므로 멱등 찾기 규칙을 그에 맞출 것.
  `M3SceneSetup.SceneContext`·`CreateEnemy`는 SCV/RDR 머티리얼 두 개만 안다(`archetype == ctx.Rdr ? Rdr : Scv`) — 새 유형 머티리얼은 2c 몫. `NavMeshSnapDistanceM = 3`.
- 2b: 시체 = `CorpseLootBuilder`가 `LootRoller.Roll`을 부르면 LT-BOS 유물이 자동으로 1개 들어간다. 보스 무기·탄띠 제외(enemyOnly)만 하면 된다. 스폰 내구도: SNP 50~90, BGD·SCV·RDR 35~85, MUT·BOS (100,100).
- 2a: SNP `telegraphSec` 1.5, `scanArcDeg` 60, `semiShotIntervalSec` 2.0~3.5, `engageMaxRangeM` 200, `preferredRangeM` 0.

## 7B-2c 구현 메모 (gameplay-programmer, 2026-10-23)

사본(`scratchpad/copy_7B2c`)에서만 메뉴 실행·테스트. 원본에는 아래 코드·테스트 파일만 썼고 WAV·머티리얼·씬은 만들지 않았다(7B-3 통합이 원본에서 메뉴 실행).
사본 격리: 복사 시점에 2b가 쓰던 `MutantTactics`·`EnemyMelee`(7B-1 껍데기로)·`CorpseLootBuilder`·`EnemyWeapon`(HEAD로)을 되돌리고, 2a·2b 새 테스트(SniperTactics·EnemyMelee·SniperFlow)는 사본에서 뺐다.
2a의 `SniperTactics`·`SniperGlint`(07:42 판)는 껍데기 원본이 남아 있지 않아 그대로 두었다 — 섬광 연결이 실제 2a 컴포넌트(같은 오브젝트 `Renderer.enabled`로 켜고 끔)와 맞는지 이걸로 확인했다.

**새 자리표시 에셋 (사본에서 생성, 원본은 통합이 만든다 — 전부 TODO(GDD-?))**
- WAV 5개(22050Hz 모노 PCM16): `Audio/Placeholder/Gunshots/GS_Lmg_{Near,Mid,Far}.wav`(경기관총: 소총보다 낮고 짧게 — Pitch 0.78·길이 ×0.88·몸통 ×1.25·크랙 ×0.9),
  `Audio/Placeholder/Enemy/MUT_Alert.wav`(1.2초, 180→70Hz 하강 톱니 성문 × 저역 공진 + 거친 노이즈, 시드 7001), `MUT_Attack.wav`(0.5초, 짧은 비명 + 휘두름 대역 노이즈, 시드 7002) + `Enemy/` 폴더 .meta.
  경로는 계약 `M7Names.MutantAlertClipPath`·`MutantAttackClipPath`(종류마다 1개) — 설계 §2.2 표의 `Creatures/MUT_*_01~02`와 다르다(계약 우선). 합계 WAV 35 → 40.
- 머티리얼 3개: `Art/Materials/Palette/M_Mutant.mat`(Lit, Ash_Grey, 매끄러움 0.02), `M_Boss.mat`(Lit, Ember, 0.35, 발광 ×0.3),
  `Art/Materials/M_SniperGlint.mat`(URP Unlit, `_BaseColor` = Bone × 4 HDR — Bloom이 받는다. 경로는 계약 `M7Names.SniperGlintMaterialPath`, 팔레트 폴더 밖).
- `Data/Audio/AudioConfig.asset`: `gunshots` 끝에 MachineGun 세트(근/중/원 = GS_Lmg_*), `mutantAlertClips`·`mutantAttackClips`(각 1개), 새 필드 `vocalAudibleM` 60·`mutantAlertVolume` 1·`mutantAttackVolume` 0.9.
- `Scenes/Raid_CrimsonBasin.unity`: 최상위 적 14명 `Enemy_M7_{SNP_PYL1, SNP_HWY1, MUT_REF1~4, MUT_HWY1~4, BOS_VIL1~4}` + 순찰 경로 `PatrolRoute_M7_MUT_REF`·`PatrolRoute_M7_MUT_HWY`(루트 16개 추가).

**구현**
- 새 `EditorTools/M7EnemySceneApply.cs`: `ApplyRaid(Scene)`(true 또는 예외), 단독 메뉴 `Ashfall/Setup/M7/Apply Enemies (Raid Scene)`(배치 `Ashfall.EditorTools.M7EnemySceneApply.ApplyRaidSceneMenu` — 레이드 씬 열기·적용·바뀌었을 때만 저장), `EnsureMaterials()`, `Validate(...)`.
  순서: 유형 에셋 4개·Terrain·AiDirector 확인 → `M7EnemyLayout.CheckAll(GroundY, AI-SNP sightRange, AI-BOS anchorRadiusM)` 실패 시 예외 → NavMeshSurface 데이터 확인(없으면 예외) → 머티리얼 → 분대별로
  경로(있으면 웨이포인트 스냅만, 수·참조가 다르면 다시 만듦) → 대원마다 `StartsXZ` 지면 + NavMesh 스냅 3m(실패 시 예외) → `Enemy_` + 이름으로 찾아 없으면 `M3SceneSetup.CreateEnemy`(seed 21~34 = 기존 20명 다음 순번),
  있으면 위치·회전·직렬화 참조(archetype·director·patrolRoute·eye·muzzle·bodyVisual·squadId·loadoutIndex 0·seed)만 값이 다를 때 다시 맞춤(구성이 깨졌으면 지우고 같은 이름으로 다시 만듦) → 몸 머티리얼 → 무장 없는 유형(변이체)은 `Muzzle/Gun` 큐브 제거 → 저격수 섬광.
  섬광 = 자식 "SniperGlint" Quad(눈 앞 0.12m, 처음 크기 0.05m, 콜라이더 제거, 그림자·받기·프로브 끔, **렌더러 꺼 둠·오브젝트 켜 둠**) + `SniperGlint` 컴포넌트 + M_SniperGlint. 계약 껍데기에도 타입이 있어 "2a 없으면 빈 자식" 분기는 필요 없었다.
  바뀐 것이 있을 때만 `MarkSceneDirty` — `M7SceneSetup.ApplyToSceneFile`의 "dirty일 때만 저장"과 맞물려 재실행 바이트 동일.
- `PaletteMaterialMap`: `Key.Mutant`·`Key.Boss`(Materials 표 = `PaletteConsistencyTests`·`AtmosphereAssetTests`·M6 아트 메뉴에 자동 포함), 표 밖 `SniperGlint` Spec(경로가 팔레트 폴더 밖이라), `EnemyBodySpec(M7EnemyLayout.Kind)`(저격수·호위 = 기존 M3_EnemyRDR(Bone), 변이체·보스 = 새 키).
  M_Mutant·M_Boss는 M6ArtSetup과 같은 생성(`new Material` + `ApplySpec`)이라 나중에 M6 아트 메뉴를 돌려도 바이트가 같다(사본 EditMode `AtmosphereAssetTests` 재실행 통과).
- `M6AudioSetup`: `M7GunshotClasses`에 `MachineGun`(7A-2c 메모의 확장 방식 그대로), `M7Recipes()`에 MUT 2개(13개), `FillAudioConfig`가 울음 배열을 **비었을 때만**(null·길이 0·전부 null) 채움. `SfxSynth`: `ClassParams` MachineGun case, `MutantAlert()`·`MutantAttack()`(길이 상수 `MutantAlertSec` 1.2·`MutantAttackSec` 0.5). 기존 분류 합성은 비트 단위로 같다.
- Audio: `AudioConfigSO` 울음 필드(위), 순수 `Logic/VocalMix`(게인 = 종류 음량 × (1 − d/vocalAudibleM) 선형, 밖·NaN·들리는 거리 ≤ 0 = 재생 없음, 종류별 배열), `FootstepMix.PriorityEnemyVocal = PriorityCollapse`(1: 총성 > 울음 = 금속 붕괴 > 적 발소리 = 무기 클릭 > 내 발소리, TODO(GDD-?)),
  `AudioDirector`: `EnemyVocalized` 구독(기존 Subscribe/Unsubscribe 짝, 캐시 델리게이트), 종류별 `ClipPicker`(직전과 다르게)·피치 ± pitchJitter, 적 눈 위치 3D voice, 클립 없으면 무음, 밖이면 `CulledCount`. `PlayedSoundKind.EnemyVocal`(끝에 추가), 공개 읽기 `LastEnemyVocal`. 이벤트 경로 할당 0.

**테스트**
- 기존 단언 변경(이어 붙이기만, MachineGun 추가로 불가피): `M6DataAssetTests.AudioConfig_NumbersMatchDesign` 분류 목록 끝 MachineGun, `AudioConfigAssetTests`의 `M7Recipes_8Files_…` 개수 8 → 13(식 `3 * 3 + 2 + 2`)·`Fill_M6Config_…` 기대 목록 끝 MachineGun·세트 수 5 → 6. 그 밖 기존 단언 변경 없음.
- EditMode 새 `VocalMixTests` 5(설계 기본값·선형 감쇠·종류 음량·밖/NaN/0/null·음량 자르기·종류별 배열), `AudioConfigAssetTests` +4(LMG 세트 = GS_Lmg_*·소총보다 짧음, 울음 연결·울음 > 공격, 레시피 경로, 빈 배열만 채움·사용자 배열 보존·두 번째 0),
  `SfxSynthTests` +4(MUT 2 레시피 길이·피크·결정적, LMG가 소총보다 짧고 낮으며 다른 분류와 다름, 울음 > 공격·로드 방식 경계·울음이 더 낮음; LMG 근/중/원 레시피 3개는 기존 M7Recipes 케이스에 자동 포함).
- PlayMode `AudioFlowTests` +8(울음·공격 → 클립·위치·선형 게인·피치 범위·3D·우선순위 < 적 발소리, 밖 = 버림·클립 없음 = 무음, 울음 2개 연속 같지 않음, 적 발소리로 찬 풀 빼앗기, 총성으로 찬 풀 버림(각각 따로), 비활성 → 해제·재구독, 300 이벤트 GC 0, LMG `ShotFired` → 세트 근/중/원).
- PlayMode 새 `M7EnemySceneSmokeTests` 1(레이드 씬: 적 34·유형별 SCV 12/RDR 8/SNP 2/MUT 8/BOS 1/BGD 3·AiDirector 등록 34, `Enemy_M7_*` 14명 최상위·NavMesh 위·모든 스폰 ≥ 190m,
  몸 머티리얼 = 유형별 에셋·`_BaseColor` = 팔레트 토큰, 변이체만 총 큐브 없음, 저격수 2명 섬광 자식(SniperGlint·콜라이더 없음·그림자 끔·처음 꺼짐·M_SniperGlint = Bone × 4 HDR), 3초 실행 후에도 NavMesh 위).
  씬 적용 전 원본에서는 실패한다(의도 — 통합이 메뉴를 돌려야 통과).

**사본 결과**
- 메뉴: `M6AudioSetup.CreateAssets` 1회째 "WAV 새로 씀 5, 임포트 설정 변경 5, 빈 필드 채움 6"(MachineGun 세트 추가 1 + 클립 3 + 울음 배열 2), `M7EnemySceneApply.ApplyRaidSceneMenu` 1회째 "새로 14"·머티리얼 3개 생성·씬 저장(검사 6항목 통과, 스폰과 가장 가까운 M7 점 199.6m = `PatrolRoute_M7_MUT_REF/2` ↔ SP-E, 가장 가까운 M7 적 251m).
  2회째 "0/0/0"·"새로 0, 다시 맞춤 0 — 변경 없음"(씬 저장 안 함) → `Assets`·`ProjectSettings` 1205 파일 md5 동일(WAV 바이트·.meta GUID·AudioConfig·머티리얼·씬, 씬 루트 57개 그대로). EditMode·PlayMode 실행 뒤에도 동일(테스트 파일 제외).
- EditMode 1540/1540(2a·2b 새 테스트는 사본에서 뺀 상태, `PaletteConsistencyTests`가 M_Mutant·M_Boss를 자동으로 봄, `AtmosphereAssetTests` 재실행 통과).
- PlayMode 전체 150 중 146 통과(608초). 실패 4 = **예상 3**(씬 적 20명 단언: `RaidSceneSmokeTests.RaidScene_Loads_…`·`RaidPerformanceTests.RaidScene_TwentyEnemies_…`·`M6PerformanceTests.RaidScene_StormPeak_…` — 34명이라 수 단언에서 바로 실패, 예산은 측정 전) +
  내 테스트 1개(같은 프레임 빼앗기 규칙을 잘못 가정 — 둘로 나눠 고침). 고친 뒤 `AudioFlowTests`+`M7EnemySceneSmokeTests` 29/29.

**7B-3 통합이 할 일 (원본)**
- `M7SceneSetup`: `CreateAssets`의 7B 줄 `M7AiDataSetup.CreateData();`(M6AudioSetup 앞이든 뒤든 무관 — WAV·세트는 이미 `M6AudioSetup.CreateAssets`가 만든다), `ApplyRaid`의 주석 줄 `if (!M7EnemySceneApply.ApplyRaid(scene)) return false;` 살리기(실패는 예외로 나온다),
  `RaidAssetsExist`에 `&& Exists<EnemyArchetypeSO>(M7Names.SniperArchetypePath)`(나머지 3개 유형도). 별도 머티리얼 단계는 필요 없다(`ApplyRaid`가 `EnsureMaterials`를 부른다).
- 원본에서 `Ashfall/Setup/Apply M7 Content` 2회 → 위 사본 결과와 같은 새 파일(WAV 5 + `Audio/Placeholder/Enemy.meta`, 머티리얼 3)·변경(AudioConfig.asset, Raid_CrimsonBasin.unity), 2회째 바이트 동일 확인.
- 기존 테스트 적 총수 단언 갱신(설계 §6 7B-3): `RaidSceneSmokeTests.ExpectedEnemies`, `RaidPerformanceTests.ExpectedEnemies`(+ "20명 생존" 문구), `M6PerformanceTests.ExpectedEnemies` → 34(= `CrimsonBasinLayout.ExpectedEnemyCount + M7EnemyLayout.Count`; PlayMode asmdef는 EditorTools를 안 보므로 상수 34 + 주석). 성능 예산 34명 재측정은 7B-4.
- 메뉴 실행 전 원본에서는 `M7EnemySceneSmokeTests`·`AudioConfigAssetTests` 새 케이스·`PaletteConsistencyTests`(M_Mutant 없음)·`M6DataAssetTests.AudioConfig_NumbersMatchDesign`(세트 6개)·`M7Wavs_Exist_…`가 실패하는 것이 정상이다.
- 2a `SniperGlint`와 맞춘 점: 섬광 오브젝트는 켜 두고 렌더러만 꺼 둔 채 저장한다(2a는 같은 오브젝트의 `Renderer.enabled`로 켜고 끈다). 2a가 이 방식을 바꾸면 `EnsureGlint`·스모크 테스트의 "처음 꺼짐" 단언을 같이 볼 것.
- 사람 확인(에디터): §7.1 E1(섬광 크기·색 — HDR ×4·Bloom), E3(변이체 울음 60m·공격 소리로 접근을 알 수 있는지), E5(M_Mutant Ash_Grey가 SCV(같은 Ash_Grey 토큰)와 구분되는지 — 안 되면 토큰을 바꿀 것, TODO(GDD-?)), W6(LMG 근/중/원).

## 7B-2b 구현 메모 (ai-programmer, 2026-10-23)

변이체 + 시체·적 무기. 소유 파일만 고침(EnemyBrain·EnemyAgent·Core·M7Names·테스트 도우미·데이터 메뉴 무변경, 새 asmdef·참조 없음, 새 `.meta` 안 만듦). 커밋하지 않음.
사본(2a·2c 진행 중 파일은 사본에서만 HEAD 또는 7B-1 껍데기로 격리) EditMode 1559/1559(새 35), PlayMode 전체 146/146(새 `MutantFlowTests` 4).

**동작**
- `Brain/MutantTactics.cs`: Idle·Patrol·Search = EnemyBrain 기본(분대 대형 순찰, 걷기 = patrolSpeed 1.2). Suspicious = investigatesNoise면 자극 위치로 걷고 도착 후 둘러봄(SCV와 같음).
  Alert·Combat: `i.Melee = true`, `Fire = false`. 보이면 `LookAt(표적 중심)` + 질주(Sprint)로 둘레 자리, 수평 거리 ≤ 사거리 × 0.75면 Hold(밀고 들어가지 않음). 안 보이면 LKP로 질주, 도착하면 LKP 쪽을 봄. 엄폐·우회권 질의 없음.
- 같은 파일 `public static MutantSurround.ApproachPoint(target, self, basePosition, index, count, surroundRadiusM, meleeRangeM)`: 기준 방향 = 표적 → 분대 리더(리더가 아직 Tick 전이면 자기), 순번 k 각도 = 360°/분대 수 × k.
  멀면(수평 > surroundRadiusM + meleeRangeM) 반경 surroundRadiusM, 가까우면 meleeRangeM × 0.5. 단독·순번 없음 = 자기 쪽. 자리는 `TrySampleNavPoint`(실패 = 표적 위치).
- `Combat/EnemyMelee.cs`: 사거리 ∧ 시야 ∧ 간격 지남 → `WindupStarted`. 준비 끝 첫 Tick에 사거리 ∧ 시야면 `Strike`, 아니면 `Whiff`(한 번만). 간격은 준비 시작 기준(`max(interval, windup)`) → 계속 붙어 있으면 타격 사이 = 1.1초. 헛침도 같은 간격 소모. `Reset` = 준비·간격 초기화.
  `PickStrikeOffset`: 흉부·복부·팔 1/3씩(팔 좌우 반반). 높이 비율 흉부 0.66~0.83·복부 0.48~0.59·팔 0.50~0.83, 옆 = 몸통 |x| ≤ r × 0.35, 팔 r × 0.75~0.95(PlayerConfigSO 기본 구간 0.87/0.62/0.45/0.6 안쪽).
- `Combat/CorpseLootBuilder.cs`: 무기 정의 `enemyOnly`면 무기·장착 탄창·예비 탄창을 넣지 않는다. 루트 굴림 결과에서도 enemyOnly 무기와 그 무기 탄창 정의를 뺀다. 루트 굴림 뒤 `weapon.RollSpawnDurability(random)`(enemyOnly 무기는 안 굴림).
- `Combat/EnemyWeapon.cs`: `static PickSpawnDurability(range, random)`(0~100으로 자름, max < min = min, 폭 0·난수 없음 = min, NaN = 100), `RollSpawnDurability(random)`(한 번만), `SpawnDurabilityRolled`.

**설계와 다른 점·정한 것 (전부 TODO(GDD-?))**
- **스폰 내구도는 생성자가 아니라 시체가 될 때 정한다**(Q12·Q18): 생성자에서 공유 난수(EnemyAgent의 `random` = 전술과 같은 스트림)를 쓰면 SCV·RDR(35~85)의 전술 결정 순서가 밀려 기존 `EnemySoundEventTests.EngagedEnemy_ShotFiredPerRound_OtherEnemyNotAlerted`가 결정적으로 실패했다.
  적 무기는 마모·고장이 없어 살아 있는 동안 값이 쓰이지 않으므로 `CorpseLootBuilder`가 루트 굴림 뒤에 굴린다(루트 시드 재현도 그대로). **살아 있는 적의 `Weapon.Item.Durability`는 100**이다.
- 근접 간격 기준 = 준비 시작(Q24). 준비 중 시야를 잃어도 헛침.
- 타격 지점 분포 비율은 `EnemyMelee` 내부 상수(AiConfigSO는 이번 소유 밖, AI는 PlayerConfigSO를 모름) — 필요하면 AiConfigSO로 옮긴다(Q24).
- 2.5m 둘레에 서면 1.8m 사거리에 닿지 않아 가까우면 반경을 사거리 × 0.5로 좁히고 사거리 × 0.75 안쪽이면 멈춘다(`MutantSurround` 상수, Q25).
- `PickStrikeHeight` 대신 7B-1의 `PickStrikeOffset` 그대로.

**통합(7B-3 이후)이 할 일**
- `EnemyAgent.Start`가 무장 없는 변이체마다 "쓸 수 있는 무장이 없다" 경고를 낸다(레이드 8회). `meleeDamage > 0`이면 경고를 생략할지 EnemyAgent 담당이 정할 것(이번엔 안 고침).
- 7B-4 `M7AiRaidFlowTests`: 저격수 DMR 내구도(50~90)는 시체(`EnemyKilled.Items`·시체 컨테이너)에서 확인 — 살아 있는 적의 무기는 100.
- 2c 오디오: `MutantAlert`(Alert/Combat 진입)와 `MutantAttack`(준비 시작)이 쿨다운 4초를 같이 쓰므로 경계 직후 4초 안의 첫 공격 울음은 나지 않는다(PlayMode에서 확인).
- `MutantFlowTests`는 M3_AI 씬 + `Data/AI/AI-MUT.asset`(Create M7 AI Data) 복제본을 쓰고, 기존 적 하나를 틀로 `SerializedObject`로 아키타입·분대를 바꿔 복제한다(씬·에셋 무변경).

## 7B-2a 구현 메모 (ai-programmer, 2026-10-23)

저격수(AI-SNP) 전술·섬광. 원본에 쓴 파일 4개만: `AI/Brain/SniperTactics.cs`, `AI/Runtime/SniperGlint.cs`(7B-1 껍데기 채움, 시그니처 그대로), 새 테스트 `EditMode/SniperTacticsTests.cs`(10), `PlayMode/SniperFlowTests.cs`(3).
EnemyBrain·EnemyAgent·Core·M7Names·테스트 도우미·데이터 메뉴는 고치지 않았다. 새 `.meta` 안 만듦. 커밋하지 않음.
사본(2b·2c 진행 중 파일은 HEAD/7B-1 껍데기로 되돌려 격리): EditMode 1534/1534, PlayMode 145/145(첫 전체 실행에서 `ExtractionFlowTests.Ext04_…` 1회 실패 — 저격수 테스트보다 앞서 도는 테스트, 단독 재실행·전체 재실행 통과 → 기존 흔들림으로 봄).

**SniperTactics**
- 모든 상태에서 Hold(이동 의도·목적지 없음). `TryTickRelaxed`는 Idle·Patrol·Search 모두 true.
- 훑기: `TacticsContext.LookAround`, 폭 = `scanArcDeg` 전체(좌우 각 `scanArcDeg/2`), 주기 = `AiConfigSO.searchLookSec`, 경과 = `Brain.TimeInState`.
  Idle·Patrol 기준 = **첫 평시 Tick의 몸 방향**(배치 yaw = 감시 방향, 이후 고정 — `TODO(GDD-?) Q22`), Search 기준 = LKP(없으면 감시 방향).
- Suspicious(발소리): 자극 쪽으로 돌기만. Alert·Combat 안 보임: 위협 눈(`ThreatEye` = LKP + 선 눈 높이) 쪽을 봄.
- 경고: Alert·Combat에서 보이는 감각 Tick이 시작되면 그 시각을 기록하고 `Telegraph = true`(보이는 동안 계속, 사격 사이 포함).
  `Fire` = Combat ∧ (now − 시작 ≥ `telegraphSec`) ∧ `TargetDistance ≤ engageMaxRangeM`. 안 보이는 Tick이 **한 번이라도** 있으면 경고를 처음부터(Combat 유지 중이어도).
  경고 타이머는 `Reset()`에서 지우지 않는다 — Alert → Combat 전이에도 `Reset`이 불리기 때문(반응 1.0초 < 경고 1.5초라 Alert에서 시작한 경고가 Combat으로 이어진다). 평시·Suspicious·안 보이는 Tick이 끊는다.
- 경고 시간 판정은 감각 주기(교전 0.1초) 단위라 실제 첫 사격은 1.5~1.6초 뒤(+ EnemyWeapon 반자동 간격, 첫 발은 즉시).

**SniperGlint**
- 켜기/끄기 = 같은 오브젝트의 `Renderer.enabled`(오브젝트는 켜 둠). `SetActive`는 같은 값이면 무시하되, EnemyAgent.Awake가 이 컴포넌트 Awake보다 먼저 `SetActive(false)`를 불러도 렌더러를 맞춘다.
- 첫 사용 때 렌더러 그림자 드리우기·받기 끔, 같은 오브젝트의 콜라이더가 있으면 **끔**(파괴 안 함). 배치(2c)는 Quad의 MeshCollider를 지우는 것이 원칙.
- `Face(eye, angularSizeDeg)`: `rotation = LookRotation(자기 위치 − 눈)`(Unity Quad의 보이는 면 −Z가 눈 쪽), 월드 한 변 = 거리 × tan(각), 부모 `lossyScale`로 나눠 localScale. 위치는 배치된 로컬 위치 그대로. `WorldSize`(디버그·테스트용 공개 읽기) 추가.

**테스트**
- EditMode `SniperTacticsTests`: 전 상태 이동 없음, 첫 발견 1.5초 전 사격 없음·경고 뒤 바로 사격, 사격 사이 섬광 유지, 한 번 놓친 뒤 다시 보면 경고 재시작(Combat 중·Search에서 T5 둘 다), Search = LKP 기준 ±30° 훑기, Idle = 시작 방향 ±30° 훑기, 발소리 → 돌아봄만, 총성 → Alert·LKP 쪽, 교전 Tick 할당 0.
- PlayMode `SniperFlowTests`(코드 씬: 새 빈 씬을 만들고 다른 씬은 내림, 높이 3000m, 퍼치 상자 NavMesh 런타임 굽기, `AI-SNP`·`AiConfig` 에셋, `TestTarget`, 가짜 `IProjectileService`·`IAshStormInfo`, 자식 Quad "SniperGlint" + 기본 머티리얼):
  120m 표적 → 섬광 켜짐(렌더러·그림자 끔·콜라이더 꺼짐, −Z가 표적 눈 쪽, 크기 = 거리 × tan 0.35°) → 첫 `ShotFired(Enemy)`까지 ≥ 1.5초(≤ 2.0초), 사격 사이 섬광 유지 /
  폭풍 상한 30m → 5초 동안 Idle·게이지 0·섬광 꺼짐·사격 없음, 상한 해제 → 섬광 켜짐 / 교전 중 `AiDirector.Update` GC 0(AiPerformanceTests 방식). 세 테스트 모두 퍼치 위치 불변(0.05m).

**설계와 다른 점·정한 것**
- 훑기 폭 `scanArcDeg` = 전체 폭으로 해석(좌우 30°). 주기는 새 필드 없이 `searchLookSec` 재사용.
- Alert(반응 시간 중)에도 섬광을 켠다(“처음 볼 때” = Alert 진입). 사격만 Combat.
- 섬광 콜라이더는 끄기만 한다(런타임 파괴 없음).

**통합(7B-2c·리뷰)이 할 일**
- 2c 배치: 저격수 자식 "SniperGlint" = Quad(MeshCollider 제거) + `M_SniperGlint` + `SniperGlint` 컴포넌트, 로컬 위치 = 눈 높이 앞(예 (0, 1.6, 0.25)). 저격수 루트 yaw = 감시 방향(평시 훑기 기준이 된다).
- 실제 레이드에서 저격수 퍼치가 NavMesh 위가 아니면 EnemyAgent가 경고 1회만 하고 제자리(이동 의도가 없으니 동작은 같다).

## 7B-3 구현 메모 (gameplay-programmer, 2026-10-23)

원본에서 통합. 커밋하지 않음. 새 `.meta` 안 만듦(메뉴가 만든 에셋의 `.meta`는 유니티 생성).

**처음 함께 컴파일**: 7B-1·1b·2a·2b·2c 코드가 원본에서 충돌·컴파일 오류 없이 한 번에 컴파일됐다(`error CS` 0). 메뉴 실행 전 EditMode 1585 중 1572 통과, 실패 11 = 7B-2c 메모가 예고한 "메뉴 전 실패"(AudioConfig 세트·울음·LMG WAV, PaletteConsistency M_Mutant/M_Boss 없음, `M6DataAssetTests.AudioConfig_NumbersMatchDesign`, `AtmosphereAssetTests.CreateAssets_Rerun_KeepsGuidsAndBytes`) — 메뉴 뒤 전부 통과.

**고친 것**
- `EditorTools/M7SceneSetup.cs`: `CreateAssets`에 `M7AiDataSetup.CreateData();`(M6AudioSetup 뒤), `ApplyRaid`에 `if (!M7EnemySceneApply.ApplyRaid(scene)) return false;`, `RaidAssetsExist`에 AI-SNP·MUT·BOS·BGD 존재 조건(`using Ashfall.AI`).
- `AI/Runtime/EnemyAgent.cs` Start: "쓸 수 있는 무장이 없다" 경고를 `meleeDamage > 0` **이고 무장 목록이 비었을 때만** 생략(근접 유형인데 목록이 있는데 못 쓰면 여전히 경고 — 데이터 오류는 드러나게).
- 섬광 배치 조건(2a 메모) 확인 — `M7EnemySceneApply.EnsureGlint`에 빠진 것 없음: Quad 콜라이더 제거·M_SniperGlint·`SniperGlint` 컴포넌트·로컬 위치 = Eye 로컬 + 앞 0.12m(눈 높이 앞)·그림자/프로브 끔·렌더러 꺼 둠, 저격수 루트 yaw = `SquadPlan.YawDeg`(퍼치는 `YawTowards(StartsXZ[0], WatchXZ)` = 감시 방향). 고치지 않음.
- 적 총수 단언 20 → 34: `RaidSceneSmokeTests`·`RaidPerformanceTests`·`M6PerformanceTests`의 `ExpectedEnemies`(= `CrimsonBasinLayout.ExpectedEnemyCount` 20 + `M7EnemyLayout.Count` 14, PlayMode는 EditorTools를 안 보므로 상수 + 주석). 실패 메시지의 "20명" 문구만 `{ExpectedEnemies}`로. 다른 단언·예산·테스트 이름 무변경.

**메뉴 2회 (`-executeMethod Ashfall.EditorTools.M7SceneSetup.ApplyAll`)**
- 1회째: WAV 5(`GS_Lmg_{Near,Mid,Far}`, `Enemy/MUT_Alert`·`MUT_Attack`) + `Audio/Placeholder/Enemy.meta`, 머티리얼 3(`Palette/M_Mutant`·`Palette/M_Boss`·`M_SniperGlint`), `AudioConfig.asset`(MachineGun 세트·울음 배열·빈 필드 6), `Raid_CrimsonBasin.unity`(M7 적 14명 새로·순찰 경로 2). AI 데이터(AI-SNP/MUT/BOS/BGD·LT-*·ITM-REL-01)는 이미 있어 변경 없음.
- 2회째: "WAV 새로 씀 0·빈 필드 0", "새로 0, 다시 맞춤 0 — 변경 없음", 씬 저장 안 함 → `Assets`·`ProjectSettings` 1215 파일 md5 동일. EditMode·PlayMode 실행 뒤에도 동일.

**테스트 결과 (원본)**
- EditMode 1585/1585.
- PlayMode 158 중 156 통과(785초). `ExtractionFlowTests.Ext04_…` 통과(이번 실행에서 흔들림 없음), `M7EnemySceneSmokeTests`·`MutantFlowTests`·`SniperFlowTests`·`AudioFlowTests` 통과.
- **실패 2 = 성능 테스트 2개, 예산이 아니라 "전원 교전 진입" 단계에서**(예산 단언까지 가지 못함):
  - `M6PerformanceTests.RaidScene_StormPeak_…`: "90초 안에 34명 교전 진입 안 됨 (31)".
  - `RaidPerformanceTests.RaidScene_TwentyEnemies_…`: "90초 안에 34명 교전 진입 안 됨 (23)".
- 원인 진단(테스트를 임시로 고쳐 시간 초과 시 실패 대신 측정을 계속하게 한 진단 실행 2회 — 끝나고 원래대로 되돌림, md5 확인):
  - 한 번도 Combat에 안 든 적: M6 = `M7_SNP_PYL1`·`M7_BOS_VIL1`·`M7_BOS_VIL2`, Raid B = 위 + 변이체 일부·HWY 분대 등 11명. 공통으로 **움직이지 않는 저격수(Hold)와 거점 묶인 보스 무리(anchor 중심 = 첫 Tick 위치 = 마을, 표적 근처로 옮겨도 목적지가 마을 쪽 25m 안으로 잘린다)**가 남는다.
  - 상태 요약에서 대부분의 적이 표적 위치(±2m)에 몰려 Alert로 서 있다. 시야 레이(`PhysicsLineOfSight`)는 다른 적 캡슐도 가림으로 본다 — 변이체가 표적 둘레 ~1.35m에 붙고 다른 적이 LKP(= 표적)로 몰리면 서로 시야를 가려 늦게 오는 적·제자리 적이 교전에 못 든다(변이체를 끄고 돌리면 M6는 비변이체 26 중 23명 Combat).
  - 90초 대기 동안 적 탄이 떨어진 것으로 보이며(추정, 탄 수는 기록 안 함) 측정 구간에 `ShotFired` 0·투사체 0 → **아래 측정값은 최악 부하가 아니다.**
- 34명 측정값(진단 실행, 참고): M6(폭풍 Peak) AI 평균 0.137ms·99% 0.372ms, 프레임 중앙값 1.76ms(변이체 끔: 0.146/0.392ms, 교전 평균 23명) / Raid A 자연 교전 AI 0.115/0.318ms(변이체 끔 0.105/0.280ms), B 0.127/0.334ms(교전 평균 1~3명) / 엄폐 질의 32.8~37.6µs. 20명 기준 이전 값(B 0.12~0.20ms·99% 0.33~0.82ms, 교전 20명)과 비교하면 예산(1.0ms·3ms) 여유는 크지만 34명 전원 사격 상태는 아직 못 쟀다.
- **7B-4(또는 오케스트레이터)가 정할 것**: 성능 테스트의 전원 교전 조건이 M7 유형(Hold 저격수·거점 보스 무리·근접 변이체)과 맞지 않는다. 예산·단언은 바꾸지 않았다. 후보(오케스트레이터 결정 필요): (a) 옮길 때 거점·저격수 시선을 표적 기준으로 다시 잡는 테스트 도우미, (b) "교전 진입" 대상을 사격 유형으로 한정하고 변이체는 별도 계측, (c) 적끼리 시야를 가리지 않게 하는 AI 규칙(게임플레이 변경 — GDD 확인 필요, TODO(GDD-?)).

**사람 확인(에디터)**: 7B-2c 메모의 E1(섬광)·E3(변이체 울음)·E5(M_Mutant 구분)·W6(LMG 소리). 레이드에서 변이체 무리가 플레이어에게 붙었을 때 다른 적이 몸에 가려 쏘지 못하는 장면이 자연스러운지도 같이 볼 것.

## 7B-4 구현 메모 (test-engineer, 2026-10-23)

원본에서 검증. 제품 코드·데이터·씬 무변경(테스트 파일만). 커밋하지 않음. 새 `.meta`는 유니티가 만든 `M7AiRaidFlowTests.cs.meta` 하나.

**성능 테스트 전제 수정 (오케스트레이터 결정 (b)) — `RaidPerformanceTests`·`M6PerformanceTests`**
- 씬 총수 34 단언·예산(AI 평균 ≤ 1.0ms·99% ≤ 3ms, 투사체 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms, Audio+폭풍 ≤ 0.2ms, GC 0)·AI 규칙은 그대로.
- "90초 안에 전원 교전 진입"은 M4 고정 배치 20명(이름이 `Enemy_M7_`로 시작하지 않는 SCV·RDR, `ExpectedM4Enemies = 20` 단언)에만 건다. 고리로 옮기는 것도 M4 적만. M7 14명은 씬에 남아 AI 틱(34명 등록)에 포함된다. M7 유형 최악 부하는 7D `M7PerformanceTests`.
- 측정 직전 테스트 도우미 `RaidPerformanceTests.RefillAmmo`(장착·예비 탄창을 `Magazine.TryAddRound`로 가득)를 부른다.
- 표적 근처(< 400m) 변이체(고속도로 무리 4)는 시작 때 `ParkNearbyMutants`로 표적에서 700m 넘게 떨어진 NavMesh로 옮긴다(씬·틱에는 남음, 변이체는 7D에서 따로 잰다).
- `RaidPerformanceTests` Measure가 측정 구간의 적 `ShotFired`를 세고, B에 `Shots > 0` 단언을 더했다(M6은 기존 `ShotsSeen > 0` 그대로).

**진단 — 측정 구간 사격**
- M6 이후 7A 진행 중 로그(10-22, 적 20, AI 코드 = M6과 같음): M6 `ShotFired` 496~509(캡처 상한 512), Raid B 동시 탄 최대 12~19. 정확히 46857a7에서 다시 돌리지는 않았다. 그 커밋의 M6 테스트는 `ShotsSeen > 0`을 단언하고 통과했다.
- 7B-3 진단(34명 전원 이동): `ShotFired` 0, 동시 탄 0. 이번에 원인을 두 가지로 나눴다.
  1. **탄 소진**: 적 무기는 탄이 유한하다(예비 2~3). 90초 대기 동안 무적 표적에 쏘면 바닥난다. → 탄 보충.
  2. **변이체가 표적과 겹침**: 변이체가 표적 위치까지 들어가 수평 0.0m에 선다(`M7AiRaidFlowTests` 무리 테스트의 최근접 0.0m에서도 재현). 표적 눈이 변이체 캡슐 안에 들어가 모든 적의 시야 레이가 막힌다. 그래서 M4 적이 교전하지 못하고 LKP(= 표적 발)로 걸어와 Alert로 몰린다.
     - 변이체를 제자리에 두고 탄만 보충한 실행: Raid B는 M4 교전 0/20(90초). 고속도로 무리가 A의 총성(200m)을 듣고 와서 표적에 겹쳤다.
     - 34명 전원을 고리로 옮긴 M6 실행(통과): `ShotFired` 29, 동시 탄 최대 3, 교전 평균 14.7, AI 0.190/0.517ms.
     - 위 두 줄 때문에 변이체 옮기기를 넣었다.
- 수정 후(PlayMode 2회):
  - M6: M4 20명 교전 3.2~3.3초, 교전 평균 20, `ShotFired` 512(캡처 상한), 동시 탄 17~18, AI 0.149~0.152ms·99% 0.287~0.437ms, 프레임 중앙값 1.73~1.80ms, Audio+폭풍 0.0023ms, GC 0.
  - Raid A: AI 0.118~0.124ms·99% 0.217~0.334ms, `ShotFired` 97~103.
  - Raid B: M4 20명 14.6~15.7초, 교전 평균 20.7~21.0, `ShotFired` 493~517, 동시 탄 17~19, AI 0.148~0.155ms·99% 0.279~0.455ms, 투사체 0.030~0.035ms, 프레임 중앙값 1.50~1.54ms.
  - 엄폐 질의 30~33µs.

**새 PlayMode `M7AiRaidFlowTests` (4, 레이드 씬 `M4Raid.Load(SP-W)`)**
- `Boss_Killed_CorpseHasRelic_NoLmgNoBelt_RelicLooted`
  - 살아 있는 보스 = LMG(enemyOnly)·MAG-762-100.
  - 처치(`IDamageable.ApplyDamage`) 뒤 시체 = ITM-REL-01 1개 + MAG-762-5 ×3. LMG·탄띠·enemyOnly는 없다.
  - `LootContainer.Interact` → 검색 완료 → 유물 가져오기.
- `BossGroup_TargetLuredOutsideAnchor_StaysWithinAnchorRadius`
  - 거점 12m에서 총성 → 반응 → 표적을 거점 밖 55m로 옮기고 20초 동안 총성을 낸다.
  - 4명 모두 각자 거점(첫 Tick 위치) 25m + 1m 안에 있었다(최대 16.4~24.3m). 반응 프레임은 100%였고, 가장 멀리 간 대원 > 10m(유인이 실제로 걸렸다는 근거).
- `Sniper_Killed_DmrDurability50to90_LootedEquippedAndFires`
  - 살아 있는 저격수 DMR 내구도 = 100.
  - 시체 DMR 내구도는 2명 모두 50~90 안이다(관측 51.62·55.83).
  - 루트에서 Primary2에 장착 → Tab으로 인벤토리를 닫음 → 2 → LMB → `ShotFired(Player, DMR)`, 탄 −1, 마모 −0.1, 고장 없음.
  - 내구도 ≥ 50이면 고장 확률 0이라 첫 발만 단언한다.
- `MutantPack_ApproachesTarget_EnemyVocalizedAlertThenAttack`
  - MUT_REF 무리 20m 앞 무적 표적 + 총성 → `MutantAlert` 4개(0~0.1초, 무리 위치) → 접근(≤ 3m) → `MutantAttack`(4.6초, 쿨다운 4초 뒤) → 교전 → 표적에 근접 타격.

**결과**
- EditMode 1585/1585.
- PlayMode 162/162 ×2 연속(688초·686초).
- Mono `BuildWindows64` 성공.
- `Assets`·`ProjectSettings`(테스트 제외) md5와 git status는 테스트·빌드 전후 동일.

**발견 (제품 코드 — 고치지 않음, 7B-5 리뷰가 판단)**
- 변이체가 표적과 수평 0m까지 겹친다. 기대는 `MutantSurround` "사거리 × 0.75 안쪽이면 Hold"(≈ 1.35m)였다.
  - 추정 원인: 같은 무리에 시야가 막혀 안 보이는 Tick에는 `Engage`가 LKP(= 표적 발)로 질주한다. 또 Hold는 목적지만 지울 뿐 감속 중 미끄러짐이 남는다.
  - 결과: 실제 플레이어에게도 몸이 겹친다. 무리에 둘러싸인 표적은 다른 적이 볼 수 없다(사격 압박이 사라짐).
  - 7D `M7PerformanceTests`의 "변이체 근접 + 보스 LMG 연사"도 이 때문에 사격이 거의 없을 수 있다.
  - 안 보일 때 LKP 대신 둘레 자리로 가거나, LKP를 사거리 × 0.75 앞에서 끊는 방안이 있다. TODO(GDD-?) Q25.

## 7B 리뷰 수정 메모 (ai-programmer, 2026-10-23)

`docs/design/M7_review.md` "7B AI" 1~15 처리. 원본에서 실행(Unity 한 번에 하나). 새 asmdef·기능 간 참조 없음, 새 `.meta` 안 만듦(새 파일 없음). 커밋하지 않음.
결정 요약은 `docs/CHANGELOG_GDD.md` "M7 설계 결정 … — 7A 무기" 절 아래 "### 7B AI"(리뷰 14). §1.4 AI-MUT 피해 문장을 부위 힌트로 고쳤다.

**높음**
1. 변이체 타격이 머리로 감 — 수정(①안). Core `DamageInfo`에 `HasPartHint`·`PartHint`(+ 6인자 생성자, 기존 5인자 = 힌트 없음·동작 그대로). `PlayerHealth.ApplyDamage`는 힌트가 있으면 지점 판정 없이 그 부위에 `Body.ApplyHit`.
   AI: `EnemyMelee.PickStrikePart(random, AiConfigSO)`(가중치 `meleePartWeightThorax/Stomach/LeftArm/RightArm` = 1/1/0.5/0.5, 음수·NaN = 0, 합 0 이하·null = 흉부, 난수 1회).
   `EnemyAgent.Strike` = 지점 표적 중심 + 부위 힌트. `PickStrikeOffset`·분포 상수 7개·캡슐 높이/반지름 캐시·반지름 0.2배 추정 삭제(IDamageable 캐시만 유지).
   테스트: EditMode `EnemyMeleeTests` 분포(머리·다리 0, 1/3·1/3·1/6·1/6)·사용자 가중치·전부 0·시드/할당 4개(옛 오프셋 테스트 3개 대체), PlayMode `MutantFlowTests.RealPlayer_StandCrouchProne_…`
   (M3_AI 씬 실제 Player를 표적으로 다시 등록, 피해 5 = 굴림 최소 미만, 선·웅크림(C)·엎드림(Z) 각 3타 → 머리·다리 HP 매 프레임 불변, 흉부·복부·팔 HP 감소 = 5의 배수, 최근접 ≥ 1.0m). `Pack4`는 모든 타격에 힌트·허용 부위 단언 추가.

**중간**
2. 거점 반경 밖 엄폐 — 수정. `CoverQuery.HasAnchor·AnchorCenter·AnchorRadiusM`, `CoverSelector`는 숨는 자리·내다보는 자리 중 하나라도 반경 밖이면 후보 제외(`RaiderTactics.MakeQuery`가 채움). TacticsContext Q29 TODO 삭제.
   `ScavengerTactics.TickCombat`: 거점 있으면 선호 거리 밖이어도 Hold + 보이고 `engageMaxRangeM` 안이면 사격(엄폐 없는 BOS·BGD 대체 행동). 거점 없는 SCV·RDR 경로는 그대로.
   테스트: `CoverSelectorTests.Anchor_CandidatesOutsideRadius_Excluded`, `AnchorTests.Brain_AnchoredFallback_FarVisibleTarget_HoldsAndFires_UnanchoredStillApproaches`, `Brain_AnchoredRaider_CoverQueryCarriesAnchor`.
3. 하드코딩 — 수정. `MutantSurround` 0.5·0.75 → `AiConfigSO.mutantCloseRadiusRatio`·`mutantHoldInsideRangeRatio`(`ApproachPoint`에 인자 추가), 타격 분포·반지름은 1에서 없어짐. 테스트 `MutantTacticsTests.SurroundRatios_ReadFromConfig`.
   `M7AiDataSetup`이 AiConfig 새 필드 7개를 "하나라도 파일에 없을 때만" 기록(`AiConfigM7Fields`) — `AiConfig.asset`에 9필드가 직렬화됨(값 = 초기값). 테스트 `M7AiDataAssetTests.AiConfig_MeleeAndSurroundFields_Serialized`.
4. 7B-4에서 처리됨 — 확인만(이번 PlayMode 2회 모두 성능 테스트 통과, 아래).
15. 변이체 겹침 — 수정. `MutantTactics.Engage`가 안 보일 때도 LKP를 중심으로 같은 둘레 자리로 질주, LKP와 사거리 × 0.75 안이면 Hold(LKP 쪽 봄). `EnemyAgent.ApplyMovement`: 근접 유형은 Melee 의도 중 표적과
   수평 사거리 × 0.75 안(높이 차 상한 안)이면 매 프레임 멈추고, 멈추는 순간 `nav.velocity = 0`(근접 유형만 — SCV·RDR 이동 불변). 테스트: EditMode `Combat_LostSight_SprintsToSurroundPointAroundLastKnown`(기존 LKP 단언 대체)·
   `Combat_LostSight_NearLastKnown_HoldsInsideRatio_NeverDestinationAtLkp`, PlayMode `M7AiRaidFlowTests.MutantPack_…` 접근 뒤 5초 더 관찰 최근접 ≥ 1.0m(관측 1.20m), `MutantFlowTests.Pack4_…` 3초 관찰 ≥ 1.0m.

**낮음**
5. `AiConfigSO.meleeMaxHeightDiffM` 1.2(TODO) — `EnemyMelee.InReach(self, target, range, maxDy)`(≤ 0·NaN = 무제한). 테스트 `InReach_HorizontalRangeAndHeightLimit`.
6. 거점 중심 = 분대 리더 거점 공유(`EnemyBrain.TryResolveAnchorCenter`: 리더 없음·자기·리더가 거점 유형 아님 = 자기 첫 Tick 위치, 리더가 아직 없으면 다음 Tick). 테스트 `Brain_SquadFollower_SharesLeaderAnchor_WaitsForLeader`.
   `M7AiRaidFlowTests.BossGroup_…`의 "각자 거점 = 시작 위치" 단언 → "보스 거점 = 보스 시작 위치, 호위 거점 = 보스 거점"(관측 최대 20.8~24.7m ≤ 26m).
7. `TacticsContext.ClampToAnchor`: 잘린 점을 `TrySampleNavPoint`로 바닥에 붙이고, 샘플이 반경 밖이면 그 높이로 다시 자름. 같은 입력은 캐시(MoveTo·Reached 일관). `AnchorRules.Clamp`(순수, y 유지)는 그대로. 테스트 `Brain_ClampedDestination_SampledToNavMesh_StaysInRadius`.
8. `CorpseLootBuilder` 머리·굴림 정리 주석을 동작대로(루트 굴림에서 빼는 탄창 = 이 시체 무기가 적 전용일 때 그 탄창만).
9. `M7EnemySceneApply.ApplyRaid`: 모든 대원 시작점·웨이포인트 NavMesh 스냅을 머티리얼·씬 변경 전에 계산(`EnsureRoute`는 계산된 점을 받음).
10. 경계 울음 = 분대 단위(`Squad.TryClaimAlertVocal(now, vocalCooldownSec)` — 분대 2명 이상일 때 먼저 경계에 든 한 명만, 못 얻으면 자기 쿨다운 안 씀). 공격 울음은 대원마다.
    테스트 `SquadTests.AlertVocal_OnePerSquadPerCooldown`, `MutantFlowTests.Pack4`의 "MutantAlert ≥ 4" → "= 1, 위치 = 대원 눈"(규칙 변경에 따른 단언 변경).
11. 코드 변경 없음 — 테스트·빌드 전후 `Assets`·`Packages`·`ProjectSettings`의 .cs 제외 파일 md5·git status 확인(아래).
12. `RaidPerformanceTests.RaidScene_TwentyEnemies_…` → `RaidScene_34Enemies_NaturalAndM4FullCombat_AiProjectileFrameWithinBudget_CoverQueryCost`(다른 문서에서 옛 이름은 과거 메모에만 남음). M6 `…TwentyEnemiesCombat…`은 교전 20명이 맞아 그대로.
13. `AiDirector` 등록 목록 2개·`AiTickScheduler` 초기 용량 32 → `InitialAgentCapacity` 64.
14. CHANGELOG "### 7B AI" 소절.

**M3·M6 회귀**: SCV·RDR 경로 변경 없음(거점 0 → 새 분기 안 탐, `CoverQuery` 거점 기본값 false, 근접·울음 코드는 melee/Mutant만). 기존 테스트 단언 변경은 위 변이체·보스 3곳(규칙 변경)뿐.

**결과**
- `M7SceneSetup.ApplyAll` 2회: 1회째 `AiConfig.asset`만 바뀜(새 필드 직렬화, 씬 "새로 0, 다시 맞춤 0 — 변경 없음"), 2회째 .cs 제외 824파일 md5 동일 → 멱등.
- EditMode 1596/1596(새 11). PlayMode 163/163 × 2회 연속(700초·701초, 새 `RealPlayer_StandCrouchProne_…` 1).
  성능(1회째): M6 Peak AI 0.148ms·99% 0.272ms·ShotFired 512·GC 0 / Raid A 0.118·0.223ms / B 0.148·0.296ms, 교전 평균 21.0, ShotFired 518, 엄폐 질의 30.9~35.7µs.
- Mono `BuildWindows64` 성공(오류 0·경고 0, 110,170,118 bytes, 31초). 테스트·빌드 뒤 .cs 제외 파일 md5 = ApplyAll 2회째와 동일.
- 사람 확인: 변이체가 플레이어에게 붙을 때 1.35m 근처에서 멈추고 미끄러지지 않는지, 웅크림·엎드림에서 HUD 부위 상태가 흉부·복부·팔만 바뀌는지, 무리 경계 울음이 한 번만 들리는지(7.1 E3).

## 7C-1 구현 메모 (gameplay-programmer, 2026-10-23)

계약만(원본). 새 asmdef·기능 간 참조 없음. 새 `.meta`는 Unity가 만든 것만(손으로 안 만듦). 에셋·씬 변경 없음(배치모드 뒤 git status = .cs/.meta만).
EditMode 1610/1610(기존 1596 + 새 `RadiationZoneMathTests` 14), 기존 테스트 단언 변경 없음. PlayMode는 돌리지 않음. 커밋하지 않음.

**Core 공개 API**
- `Combat/BodyEnums.cs`: `enum SurvivalStat { Hydration, Energy, Radiation }`, `enum SurvivalTier { Normal, Low, Critical }`. `StatusEffect`는 그대로(주석만).
- `Services/IBodyStatusView.cs`: `SurvivalTier GetSurvivalTier(SurvivalStat stat)`(등급만 — RULE-05 테스트 2개가 그대로 통과).
- `Services/IRadiationField.cs`(새): `float ZoneRatePerSec(Vector3 position)` 하나. 폭풍 률은 포함하지 않는다(합은 소비자, 방독면은 폭풍 몫만).
- `Services/IHideoutService.cs`: `IReadOnlyList<CraftingRecipeView> Recipes { get; }`, `CraftResult TryCraft(string recipeId)`.
  `enum CraftResult { Ok, MissingMaterials, NoSpace, UnknownRecipe, Unavailable }`,
  `readonly struct CraftingRecipeView(string Id, string DisplayName, ItemDefinitionSO Output, int OutputCount, IReadOnlyList<CraftingInputView> Inputs)`,
  `readonly struct CraftingInputView(ItemDefinitionSO Item, int Required, int Owned)` + `bool IsSatisfied`(Owned ≥ Required).
- `Data/MedicalDefinitionSO.cs`: `float hydrationRestore`, `float energyRestore`(둘 다 `[Min(0)]`, 기본 0), `bool curesRadiation`(기본 false) — 기존 의약품 동작 불변.
- `Data/ArmorDefinitionSO.cs`: `bool blocksStormRadiation`(기본 false). `ServiceRegistry`·`IAshStormInfo` 주석 갱신.

**기능 쪽 껍데기**
- Player `PlayerConfigSO`(§3.6, 초기값 = 표 값): `hydrationMax 100`, `hydrationDecayPerSec 0.1`, `dehydratedHpPerSec 0.5`, `stomachDestroyedHydrationMultiplier 2`,
  `energyMax 100`, `energyDecayPerSec 0.07`, `starvingStaminaRegenMultiplier 0.5`, `radiationSicknessDose 100`, `radiationSicknessMaxHpMultiplier 0.8`, `radiationWarnDose 50`, `survivalLowRatio 0.25`.
  기존 `PlayerConfig.asset`에는 아직 직렬화되지 않았다(로드 시 초기값) — 7C-1b 데이터 메뉴가 명시 기록.
- Player `BodyHealthModel.GetSurvivalTier` → 항상 `Normal`(2a가 `AttachSurvival` 위임으로 바꾼다).
- Raid `Data/RadiationZoneConfig.cs`(새, `[Serializable] struct`): `id`, `Vector2 centerXZ`(y = 월드 Z), `radiusM`, `ratePerSec`, `edgeFalloffM`.
  `RaidConfigSO.radiationZones = new List<RadiationZoneConfig>()`(기본 빈 목록).
- Raid `Logic/RadiationZoneMath.cs`(새, **구현 포함**): `static float RateAt(IReadOnlyList<RadiationZoneConfig> zones, Vector3 position)`(null·빈 = 0, 합),
  `static float RateAt(in RadiationZoneConfig zone, Vector3 position)`. 수평 거리 d: d ≤ 반경 − 감쇠 = 최대, 반경까지 선형 0, d > 반경 = 0.
  반경·률 ≤ 0/NaN/무한 = 0, 감쇠 ≤ 0/NaN = 경계에서 끊김(d = 반경도 최대), 감쇠 ≥ 반경 = 중심부터 선형, NaN 위치 = 0. 할당 없음.
- Hideout `Data/CraftingConfigSO.cs`(새, 메뉴 `Ashfall/Crafting Config`): `CraftingRecipe[] recipes`; `class CraftingRecipe { id, displayName, CraftingIngredient[] inputs, ItemDefinitionSO output, int outputCount = 1 }`,
  `struct CraftingIngredient { ItemDefinitionSO item; int count; }`.
- Hideout `HideoutService.Recipes` → 빈 배열, `TryCraft` → `Unavailable`. 생성자·`HideoutController`는 안 바꿨다(CraftingConfigSO 주입은 2b).
- UI `HideoutTab.Crafting`(끝에 추가). 탭 버튼·뷰 없음 — `SelectTab(Crafting)`은 되지만 지금은 장비·상인 뷰가 모두 숨는다(2b가 채움).
- 테스트 가짜: `UiTestFakes.FakeBodyStatusView.SurvivalTiers`(사전, 없으면 Normal). `FakeHideoutService`는 `HideoutUiModelTests.cs` 안에 있다(UiTestFakes 아님) —
  `RecipeList`(List), `CraftResultToReturn`(기본 Ok), `TryCraft`가 `Calls`에 `"Craft:" + id` 기록.
- EditorTools `M7Names` 7C 구역: `WaterId/FoodId/AntiRadId`(Medical 폴더 경로), `GasMaskId`(Armor 폴더 `GEAR-GSM-01.asset`), `PipePath/EnginePartPath/WiresPath`(JNK-01~03),
  `CraftingConfigPath`(`Data/Hideout/CraftingConfig.asset`), 레시피 id 8개 `Recipe*Id`, `RefineryZoneId = "RAD-REF"`, `RaidConfigPath`,
  씬 `RadiationFieldObject = "RadiationField"`, `ToxicMarkersObject = "M7_ToxicMarkers"`, `ToxicMarkerPrefix = "M7_Toxic_"`, `ToxicMaterialPath`(`Art/Materials/Palette/M_Toxic.mat`).

**설계와 다른 점·구체화**
- 레시피 산출은 §4.7·`CraftingRecipeView`대로 **하나(output × outputCount)**다. 산출 여러 종류 배열은 만들지 않았다(§3.8 레시피가 모두 1종).
- `RadiationZoneConfig`는 struct(기존 `ExtractionPointConfig`와 같음), `RateAt` 단일 구역 오버로드 추가. 목록 인자는 `IReadOnlyList`라 List·배열 모두 받는다.
- `CraftingInputView.IsSatisfied` 편의 멤버 추가. `PaletteMaterialMap` Toxic 키는 2c 몫이라 안 넣었고 경로 이름만 M7Names에 있다.

**7C-1b·2a·2b·2c가 알아야 할 것**
- 1b: PlayerConfig 생존 필드·`RaidConfig.radiationZones`(RAD-REF (300, −60) 70m 0.5/s 15m)·`CraftingConfig.asset`을 M7Names 경로로. 방독면 = `ArmorDefinitionSO { kind = Helmet, armorClass 0, covers 빈, blocksStormRadiation }`.
  물·음식·해독제의 `cures`는 빈 배열, `healAmount 0`.
- 2a: `BodyHealthModel.GetSurvivalTier` 껍데기를 위임으로 바꾼다. 등급 경계는 `survivalLowRatio`·`radiationWarnDose`·`radiationSicknessDose`. 방독면 판정은 `GetEquipped(Helmet)` 정의가 `ArmorDefinitionSO`이고 `blocksStormRadiation`.
- 2b: `HideoutService`에 `CraftingConfigSO`를 넣고(생성자 인자 추가 시 HideoutController·테스트 호출 갱신) `Recipes`(Owned = 보관함 같은 정의 수)·`TryCraft`(성공 시 `publishChanged`, 떠나는 중 `Unavailable`, 모르는 id `UnknownRecipe`)를 채운다. `FakeHideoutService`는 이미 새 멤버가 있다.
- 2c: `RadiationField`는 `RadiationZoneMath.RateAt(config.radiationZones, position)`을 그대로 쓰고 `IRadiationField`로 OnEnable/OnDisable 등록. 가이거 입력 = 폭풍 률 + 청취자 위치 구역 률.

## 7C-1b 구현 메모 (data-designer, 2026-10-23)

메뉴 `Ashfall/Setup/Create M7 Survival Data`(배치 `Ashfall.EditorTools.M7SurvivalDataSetup.CreateData`, 7C 몫만은 `WriteM7SurvivalData()`). `CreateData`는 `M7AiDataSetup.CreateData()`(→ 7A → M6 → … M1)를 먼저 부른다.
원본에서 2회 실행 → 두 번째 실행 후 `Data/`·`Audio/`·`Scenes/`·`Settings/`·`Prefabs/`·`Art/`·`ProjectSettings/` 365개 파일 md5 동일(GUID·.meta 포함). `M7SceneSetup`의 7C 줄은 7C-3 몫이라 넣지 않았다.

**만든 에셋 (없을 때만)**: `Data/Items/Medical/ITM-WTR-01`(물병 1×2·0.6kg·수분 +50·3초·2회·150·Common), `ITM-FOD-01`(통조림 1×1·0.4kg·에너지 +60·4초·1회·200·Common),
`MED-ANT-01`(해독제 1×1·0.1kg·curesRadiation·3초·1회·600·Uncommon) — 셋 다 `cures` 빈 배열·`healAmount 0`. `Data/Items/Armor/GEAR-GSM-01`(방독면 Helmet·armorClass 0·covers 빈·blocksStormRadiation·maxDurability 0·2×2·0.8kg·1500·Uncommon),
`Data/Hideout/CraftingConfig.asset`(레시피 8개, 이미 있으면 빠진 id만 끝에 추가).
**기존 에셋 (빈 필드만)**: `PlayerConfig` §3.6 필드 11개(파일에 없는 필드만, 값 = 상수 `M7SurvivalDataSetup.HydrationMax` 등), `RaidConfig.radiationZones` RAD-REF(같은 id 없을 때만 끝에 추가, 중심 = `CrimsonBasinLayout.Refinery.CenterXZ` (300, −60)·70m·0.5/s·15m),
희귀도(현재 Scrap일 때만 — `ShouldWriteRarity`), 상인 끝에 물·음식·해독제·방독면, 루트 테이블 6개 끝에 추가(아래), ItemDatabase 재수집(30 → 34).
이번 실행에서 희귀도가 바뀐 기존 에셋: MAG-9MM-12·MAG-9MM-30·MED-BND-01·RIG-01 → Common, MAG-556-30·MED-TRQ-01·MED-SPL-01·BAG-01·ARM-HLM-01 → Uncommon, WPN-RIF-01·ARM-BDY-01 → Rare.
(SetDirty로 다시 쓰인 의약품·방어구에는 7C-1 새 필드 `hydrationRestore 0`·`blocksStormRadiation 0` 등이 함께 직렬화됐다 — 값은 기본값 그대로.)

**설계와 다른 점·정한 것 (CHANGELOG "7C 생존·제작 데이터")**
- 레시피 RCP-AMMO-12GA·RCP-MED-SPL 재료 JNK-01 × 1 → **× 2**(설계 가치 확인 "산출 매도 ≤ 재료 매수"를 × 1이 어김: 80 > 40, 48 > 40). 나머지 6개는 표 그대로. 산출은 1종(output × outputCount), 붕대만 × 2.
  레시피 이름(TODO(GDD-?) Q47): "권총 탄창 (12발)"·"5.56 탄창 (30발)"·"7.62 탄창 (5발)"·"12ga 탄 상자 (8발)"·"붕대 × 2"·"지혈대"·"부목"·"해독제".
- LT-CRATE 방독면 가중치 0.3 → **0.5**(M4 Q7-A "신호탄 0.5가 상자 최저 가중치" 테스트·규칙 유지).
- 희귀도: WPN-PST-01·WPN-SMG-01·ITM-FLR-01은 M3·M4가 Common으로 만들어 "Scrap = 미설정" 규칙상 **Common으로 남는다**(§3.11은 우수). 기존 M3·M4 테스트도 Common을 단언한다. §3.11대로 하려면 오케스트레이터 결정 필요.
- 루트 추가(표마다 끝, 이미 있으면 가중치 보존): LT-CRATE 물 2·음식 2·해독제 0.5·방독면 0.5·JNK-02 0.5 / LT-SCV 물 1·음식 1·BOX-12GA-8 0.5 / LT-RDR 물 1·음식 1 / LT-SNP 해독제 1·물 1 / LT-MUT 해독제 0.3 / LT-BOS 방독면 1·해독제 1(유물은 계속 guaranteedFirstRoll만).
- 상인: 12ga 탄 상자는 7A(Q20)가 이미 넣어 다시 넣지 않는다 — 7C 추가분 4개(물·음식·해독제·방독면)만 끝에.

**테스트**: 새 `EditMode/M7SurvivalDataAssetTests`(GDD 생존 값·파일 기록, 새 아이템 값, 방독면 Helmet·0등급·blocksStormRadiation(다른 방어구는 false), 희귀도 표 = 프로젝트 전 아이템, Scrap 판매 아이템 = JNK만, 희귀도 기록 규칙,
RAD-REF 값·중심/밖/가장자리 률·스폰 3곳 0, 레시피 표·재료 고철·산출 탄창/의약품·산출 매도 ≤ 재료 매수, 상인·루트 추가분, 재실행 바이트·GUID 동일(`WriteM7SurvivalData()`만, 머리 주석에 위험)).
기존 정확 목록 단언에 이어 붙임(값 단언 변경 없음): `M3DataAssetTests` LT-SCV·LT-RDR, `M4DataAssetTests.LootCrate_MatchesDesign`, `M5DataAssetTests` 상인 목록·가격 표 범위, `M7AiDataAssetTests` LT-SNP·MUT·BOS,
`M7WeaponDataAssetTests.Trader_EndsWithM7Additions_AllPriced`(7A 추가분 = 7C 추가분 바로 앞).

**7C-2가 알아야 할 것**
- 2a: 물·음식·해독제는 `cures` 빈 배열·`healAmount 0`이다 — 현재 `MedicalUseModel.FindTarget`은 이 경우 null(→ `NotNeeded`)이라 전신 효과 판정(Q42)을 넣어야 레이드에서 쓸 수 있다. 물병은 2회(회당 +50).
  방독면은 `ArmorDefinitionSO`이고 kind Helmet이라 헬멧 슬롯에만 들어가며 ARM-HLM-01과 동시 착용 불가. armorClass 0·covers 빈 — 방어 판정 코드가 생기면 0등급 = 방어 없음으로 볼 것.
- 2b: `CraftingConfig.asset` 경로 = `M7Names.CraftingConfigPath`. HideoutController 직렬화 참조 연결은 2b/7C-3 씬 메뉴 몫(이 메뉴는 씬을 건드리지 않는다). 산출 탄창은 가득 찬 상태로 만들 것(Q46), 탄 상자도 가득(8발).
- 2c: `RaidConfig.radiationZones`에 RAD-REF 하나. 스폰 3곳은 구역 밖(테스트). POI-REF 상자·RDR 분대가 구역 안에 있다(상자 (300, −40) 등 — 의도된 위험).
- 7C-3: `M7SceneSetup`에 `M7SurvivalDataSetup.WriteM7SurvivalData()`(또는 CreateData) 한 줄. 이 메뉴는 PlayerConfig·RaidConfig·상인·루트 6개·DB를 SetDirty할 수 있으니 통합 메뉴 2회 md5 확인에 포함할 것.

## 7C-2a 구현 메모 (gameplay-programmer, 2026-10-23)

생존(Player)만. 원본에는 `Scripts/Player/*`와 새 테스트 4개만 썼다(Core·M7Names·테스트 공용 도우미·데이터 메뉴·Inventory 무변경, 새 `.meta` 없음). 사본(`copy_7C2a`, 다른 단계 진행 파일 섞이지 않음 확인)에서
EditMode 1679/1679, PlayMode 필터(Survival·PlayerDamage·AshStormFlow·InventoryFlow·M6RaidFlow·PlayerSoundEvent) 28/28, PlayMode 전체 169/169. 기존 `RadiationDoseTests`·`AshStormFlowTests` 단언 무변경. 커밋하지 않음.

**Player 공개 API**
- `SurvivalModel`(새, 순수): `Hydration`·`Energy`·`HydrationMax`·`EnergyMax`(public 읽기 — 아래 "다른 점"), `IsDehydrated`·`IsStarving`, `Tick(dt, stomachDestroyed)`(0에서 멈춤, dt ≤ 0/NaN/무한 무시),
  `Restore(hydration, energy)`(최대에서 자름, 음수·NaN 무시), `GetTier(stat)`(수분·에너지, Radiation = Normal), `TierOf(value, max)`(0 = Critical, ≤ 최대 × `survivalLowRatio` = Low), `static RadiationTier(dose, config)`(≥ 100 Critical, ≥ 50 Low), `event TierChanged`.
- `RadiationDoseModel.IsSick(threshold)` 추가(기존 Tick·Reset 그대로).
- `BodyHealthModel`: `AttachSurvival(SurvivalModel, RadiationDoseModel)`, `Survival`·`Radiation` 읽기, `GetSurvivalTier`(위임, 붙이기 전 Normal), `ApplySystemicDamage(perPart)`,
  `MaxHpMultiplier { get; set; }`(0 이하·NaN·무한 무시, 낮추면 현재 HP를 새 최대로 자름, 올려도 HP 회복 없음), `GetMaxHp` = 설정 × 배율, `SyncRadiation()`(선량 → 배율 1 또는 0.8 + 방사능 등급 변화 알림).
  전신 등급(3종) 변화도 `StatusChanged`(→ `PlayerBodyStatusChanged`)에 합쳐 바뀔 때만 낸다.
- `MedicalUseModel`: `const SystemicTarget = Thorax`, `static NeedsSystemic(body, def)`. `FindTarget` = 기존 부위 판정 → 없으면 전신 필요 시 Thorax. 완료 = 기존 cures·heal + `Restore` + 해독제(`Reset` + `SyncRadiation`). 생성자 그대로(`body.Survival`을 쓴다).
- `StaminaModel.Tick(dt, sprinting, moving, regenMultiplier = 1f)`(재생에만, 0 미만·NaN = 0). `PlayerMotor.StaminaRegenMultiplier`(에너지 0 → `starvingStaminaRegenMultiplier`).
- `PlayerHealth`: `Survival`, `LastRadiationRatePerSec`·`GasMaskWorn`(테스트·디버그 읽기), `static RadiationRate(storm, gasMask, zone)`. Update 순서 = 출혈 → 의약품 → 생존(수분 0이면 `dehydratedHpPerSec × dt` 부위마다) → 방사능(률 합 → Tick → `SyncRadiation`).
  방독면 = `IInventoryService.GetEquipped(Helmet).Definition is ArmorDefinitionSO { blocksStormRadiation }`, 구역 률은 발 위치(`transform.position`). 사망·`RaidEnded` 뒤 생존·방사능 모두 멈춤. GC 0(PlayMode 확인).

**설계와 다른 점·정한 것**
- 수분 0 사망 시점: 부위마다 0.5/s(Q33)면 **머리 35가 70초에 먼저 0 → 사망(사인 Head)**. 설계 문구 "흉부 80 → 160초"와 다르다. 규칙(머리·흉부 0 = 사망)을 바꾸지 않고 TODO(GDD-?) Q33로 남김 — 테스트 `SystemicDamage_FullHealth_HeadReachesZeroFirst`가 이 동작을 고정한다. 160초가 의도면 머리 제외 등 기획 결정 필요.
- 수분·에너지 수치 읽기는 설계의 "internal"이 아니라 **public**(테스트 어셈블리가 읽어야 하고 InternalsVisibleTo를 늘리지 않음). UI는 Player를 참조할 수 없고 `IBodyStatusView`는 등급만이라 RULE-05 구조는 그대로.
- 설계 §6의 "`StaminaModelTests` 확장"은 그 파일이 없어(스태미나 테스트는 `PlayerMovementTests`) `SurvivalModelTests.cs` 안의 `StaminaRegenMultiplierTests` 클래스로 넣었다.
- 수분 0 피해는 복부 배율·상태이상 굴림·파괴 부위 분산이 없다(파괴 부위는 건너뜀). 해독제는 선량 > 0일 때만 시작, 부위 대상이 있는 아이템(예: 붕대 + 수분)은 부위가 우선이고 완료 시 전신 효과도 함께.
- 생존 감소는 레이드가 아닌 씬(M1~M3 테스트 씬)에서도 돈다(1000초에 0이라 기존 테스트 무관). 은신처에는 PlayerHealth가 없다.

**테스트**: EditMode `SurvivalModelTests`(14 케이스 + `StaminaRegenMultiplierTests` 5)·`BodyHealthSystemicTests`(16)·`MedicalSurvivalTests`(11), PlayMode `SurvivalFlowTests`(M2 씬 + 가짜 `IAshStormInfo`·`IRadiationField`, 실제 인벤토리 장착·사용 경로):
방독면 = 률 1.5 → 0.5(구역 그대로), 일반 헬멧은 안 막음, 선량 100 → 최대 HP 0.8·Critical·`PlayerBodyStatusChanged` → 해독제(인벤토리 TryUse) 선량 0·배율 1·HP 그대로, 수분 0 → 부위마다 ≈0.5/s → 물 → 멈춤, 에너지 0 → 재생 20 → 10/s, `PlayerHealth.Update` GC.Alloc 0.

**통합(7C-3·7C-4)이 알 것**
- 2c `RadiationField`가 등록되면 PlayerHealth가 자동으로 구역 률을 더한다(코드 연결 없음). M6 `AshStormFlowTests` Peak 선량 10 ± 0.5는 구역 밖·방독면 없음이면 그대로.
- 2b 전신 줄은 `IBodyStatusView.GetSurvivalTier`만 읽으면 된다 — 등급 변화마다 `PlayerBodyStatusChanged`가 온다.
- 물·음식·해독제는 `MedicalUseStarted/Completed`의 Target = Thorax(전신). HUD 의약품 진행 표시가 부위 이름을 쓰면 "흉부"로 보일 수 있다(UI 문구는 2b/7D 판단).
- 7C-4 "다음 레이드 초기화"는 플레이어가 씬과 함께 새로 생겨 자동(세이브 변경 없음, Q32).

## 7C-2c 구현 메모 (gameplay-programmer, 2026-10-23)

사본(`scratchpad/copy_7C2c`)에서만 메뉴 실행·테스트. 원본에는 아래 코드·테스트 파일만 썼고 머티리얼·씬은 만들지 않았다(7C-3 통합이 원본에서 메뉴 실행).
사본 격리: 복사 시점의 2a·2b 새 테스트(`BodyHealthSystemicTests`·`CraftingModelTests`·`MedicalSurvivalTests`·`SurvivalModelTests`·`SurvivalFlowTests`)는 사본에서 뺐다.
2a·2b 진행 중 코드(Player·Hideout·UI)는 7C-1 상태의 HEAD 판이 없어(7C-1·1b가 미커밋) 그대로 두었다 — 함께 컴파일·통과했다.

**새 자리표시 에셋 (사본에서 생성, 원본은 통합이 만든다 — 전부 TODO(GDD-?))**
- 머티리얼 1개: `Art/Materials/Palette/M_Toxic.mat`(URP Lit, `_BaseColor` = Toxic #7A8A2A, 매끄러움 0.3, 발광 ×0.25) — `PaletteMaterialMap.Key.Toxic`(경로 = `M7Names.ToxicMaterialPath`).
  Materials 표에 들어가 M6 아트 메뉴·`PaletteConsistencyTests`·`AtmosphereAssetTests`가 자동으로 본다(같은 생성 방식이라 M6 메뉴를 나중에 돌려도 바이트 동일).
- `Scenes/Raid_CrimsonBasin.unity`: 최상위 2개 추가 — `RadiationField`(RadiationField: config = RaidConfig, raid = 씬 RaidController, 위치 = 구역 중심 지면)·
  `M7_ToxicMarkers`(자식 54개 `M7_Toxic_*`: 둘레 `Pole_01~12`(Cylinder 2.5m)·`Sign_01~12`(Cube 판, 바깥을 봄)·`Drum_01~12`, 구역 안 `PileA~C_1~6`(서 있는 4 + 위 1 + 쓰러진 1)). 콜라이더 없음, 전부 M_Toxic.
- WAV·데이터 에셋 변경 없음.

**구현**
- Raid `Runtime/RadiationField.cs`(새): `IRadiationField` OnEnable 등록/OnDisable 해제(정적 인스턴스 없음), `ZoneRatePerSec` = `RadiationZoneMath.RateAt(config.radiationZones, p)`(할당 0). 직렬화 `config`(비면 `raid.Config`). 테스트용 `Config`·`Zones`·`IsRegistered`.
- Audio `AmbientSoundscape.Tick(dt, intensity, stormRad, zoneRad = 0)`: 가이거 입력 = `EnvironmentRadiationPerSec(storm, zone)`(둘 다 음수·NaN·무한 = 0, 합), `GeigerInputPerSec` 공개. `GeigerClickModel`은 그대로.
  `AudioDirector`: `IRadiationField`를 기존 서비스 재조회(`ResolveServices`·`DropDestroyedServices`)로 잡고 매 Update 청취자 위치(`ResolveListenerPosition` = IPlayerTarget 눈)로 구역 률 조회 → Ambient에 전달. 방독면 무관(Q41).
  테스트용 `StormRadiationPerSec`·`ZoneRadiationPerSec`·`HasRadiationField`.
- EditorTools `M7HazardLayout.cs`(새, 데이터·순수 검사): 위치는 RAD-REF 설정(중심·반경·감쇠)에서 계산 — 둘레 반경 = 반경 − 0.5m, 기둥 각도 19°+30k, 드럼통 각도 표(정유소 탱크 (245, −100)·(350, −95) 근처 207~227°·322~328° 회피),
  무더기 중심 오프셋 (−18, 20)·(22, 2)·(−10, −35). `Check`: 수 54·이름 중복·접두어, 둘레 = 감쇠 띠(55~70m) 안, 무더기 = 최대 률 영역(≤ 55m) 안, 맵 안, 스폰 패드 밖.
- EditorTools `M7HazardSceneApply.cs`(새): `ApplyRaid(Scene)`(true 또는 예외), 단독 메뉴 `Ashfall/Setup/M7/Apply Radiation Zone (Raid Scene)`(배치 `Ashfall.EditorTools.M7HazardSceneApply.ApplyRaidSceneMenu`), `EnsureToxicMaterial()`, `Validate(...)`.
  검사 먼저(씬 변경 전): RaidConfig에 RAD-REF(없으면 "Create M7 Survival Data 먼저" 예외)·씬 RaidController가 같은 설정·Terrain·Layout.Check·**씬 고체 콜라이더(트리거·지형·적·CharacterController 제외) AABB와 겹침 없음(틈 ≥ 0.1m, 바닥 0.15m 제외)** → 머티리얼 → RadiationField(씬에 하나만) → 표시물(이름으로 찾아 값이 다를 때만 다시 맞춤, 표에 없는 자식 지움, 메시가 바뀌었으면 다시 만듦) → Validate. 바뀐 것이 있을 때만 `MarkSceneDirty`.
  NavMesh: 레이드 NavMeshSurface는 `PhysicsColliders`·`Children` 수집이라 콜라이더 없는 최상위 표시물은 굽기에 들어가지 않는다(NavMeshModifier 불필요).
- `PaletteMaterialMap`: `Key.Toxic`(끝에 추가).

**테스트**
- PlayMode 새 `RadiationFieldFlowTests` 4(레이드 씬, 적 제거 후): 등록·최상위·RaidController와 같은 설정·RAD-REF 값, 중심 0.5·높이 무관·감쇠 시작 0.5·가운데 0.25·가장자리 3m 0.1·경계/밖 0·단조 감소·스폰 3곳 0, 조회 1000회 GC 0 /
  OnDisable 해제·재등록 / Toxic 표시 54(둘레 36·무더기 18)·최상위·콜라이더·NavMeshObstacle·Rigidbody 없음·M_Toxic = 팔레트 Toxic·둘레 = 감쇠 띠·무더기 = 0.5/s·무더기 자리 NavMesh 그대로 /
  가이거: 스폰 = 0, 정유소 중심(PlaceAt) → `ZoneRadiationPerSec` = 서비스 률(청취자 위치) = 0.5·입력 0.5·3초 클릭 > 0, 폭풍 Peak → 1 + 0.5, 구역 밖 → 1.
- PlayMode `AudioFlowTests` +3(기존 케이스 무변경): 가짜 구역 안 청취자 → 폭풍 없이 클릭·조회 위치 = 청취자 눈·밖 → 클릭 멈춤 / 폭풍 + 구역 합·NaN·음수 구역 = 0·서비스 해제 → 폭풍만·순수 합 규칙 / 구역 조회 + Ambient.Tick 300회 GC 0.
  각 케이스는 기존 IRadiationField 등록을 저장했다가 되돌린다(TearDown 무변경). M6 가이거·폭풍 단언(`Ambient_Wind_StormLoop_Geiger_FollowStorm`·`AshStormFlowTests` 등) 그대로 통과.

**사본 결과**
- 메뉴 `M7HazardSceneApply.ApplyRaidSceneMenu` 1회째: "Toxic 표시 54개 (새로 54)", M_Toxic 생성, 장애물 223개와 겹침 검사 — 가장 가까운 틈 3.37m(`Sign_11` ↔ Tank), 씬 저장.
  2회째: "새로 0, 다시 맞춤 0, 지움 0 — 변경 없음"(씬 저장 안 함) → `Assets`·`ProjectSettings`(Tests 제외) md5 동일.
- EditMode 1633/1633. PlayMode 170/170(857초, 새 7개 포함).

**7C-3 통합이 할 일 (원본)**
- `M7SceneSetup.ApplyRaid`의 주석 줄 `if (!M7HazardSceneApply.ApplyRaid(scene)) return false;` 살리기(실패는 예외). 머티리얼 단계는 따로 필요 없다(`ApplyRaid`가 `EnsureToxicMaterial`을 부른다).
  `RaidAssetsExist`에 7C 조건을 더한다면 RAD-REF가 있어야 하므로 `M7SurvivalDataSetup` 줄(CreateAssets)이 먼저 돌게 할 것 — 구역이 없으면 이 적용은 예외를 낸다.
- 원본에서 `Ashfall/Setup/Apply M7 Content` 2회 → 새 파일 `M_Toxic.mat`(+ .meta), `Raid_CrimsonBasin.unity` 변경, 2회째 바이트 동일 확인.
- 메뉴 실행 전 원본에서는 `RadiationFieldFlowTests`(RadiationField 없음)·`PaletteConsistencyTests.PaletteMaterials_*`(M_Toxic 없음)·`AtmosphereAssetTests`(M_Toxic 경로)가 실패하는 것이 정상이다.
- 2a 접점: Player 선량 = 폭풍(방독면이면 0) + 같은 `IRadiationField.ZoneRatePerSec`(조회 위치는 2a 구현). 2a `SurvivalFlowTests`의 가짜 IRadiationField는 레이드 씬의 실제 RadiationField와 같은 키로 등록된다 — 레이드 씬에서 쓸 때는 덮어쓰기 경고가 나므로 기존 등록을 저장·복원할 것.
- 사람 확인(에디터): §7.1 — 정유소 둘레 경고 기둥·판·드럼통이 보이고 Toxic 색이 다른 구조물과 구분되는지, 판이 바깥을 보는지, 무더기가 땅에 붙어 있는지(경사), 구역 안에서 가이거가 폭풍 없이 울리는지, 지도에 구역 표시가 없는지(Q40).

## 7C-2b 구현 메모 (ui-programmer, 2026-10-23)

사본에서만 배치모드(원본 Unity 실행 없음). 사본 EditMode 1677/1677, PlayMode 필터(은신처·HUD) 9/9, PlayMode 전체 164/164. 커밋하지 않음. 새 `.meta`는 원본에 만들지 않았다.
사본 Hideout 씬의 `HideoutController.crafting`은 사본 전용 임시 메뉴로만 연결했다(원본 씬 변경 없음 → 7C-3 몫).

**Hideout**
- `Logic/CraftingModel.cs`(새, 순수 정적): `IsValid(recipe)`(id·산출·산출 수 ≥ 1·재료 1줄 이상·각 줄 정의 + 개수 ≥ 1 — 재료 없는 레시피는 무효), `CountOwned(IGridView, def)`, `RequiredTotals`(같은 정의 줄 합산),
  `ToView(recipe, stash)`, `HasMaterials`, `TryCraft(ItemGrid stash, CraftingRecipe recipe)`: 무효·stash null → `UnknownRecipe`, 재료 부족 → `MissingMaterials`, 재료 제거(보관함 배치 순 = 위→아래·왼→오른쪽) → `ItemFactory.Create(output)` × outputCount(탄창·탄 상자 가득)
  → `StashRules.TryPlaceAll` → 실패면 보관함을 제작 전 배치(같은 인스턴스·칸·회전·**목록 순서**)로 통째로 되돌리고 `NoSpace`. 재료를 먼저 빼므로 재료 자리를 산출물이 쓸 수 있다.
- `HideoutService`: 생성자 끝에 선택 인자 `CraftingConfigSO crafting = null`(기존 호출 무변경). `Recipes` = 유효 레시피만·중복 id는 첫 번째만, Owned = 보관함 수. `TryCraft`: 떠나는 중·**읽기 전용**·프로필 없음 → `Unavailable`, 모르는 id → `UnknownRecipe`, 성공만 `HideoutChanged`. 메모리에서만 바뀌고 W2/W3/W4 저장.
- `HideoutController`: `[SerializeField] CraftingConfigSO crafting` + `Crafting` 읽기. 비어 있으면 경고 한 줄 후 은신처는 그대로 시작([제작] 탭 = "제작법이 없다"). 에디터 경로 찾기(M7Names) 없음.

**UI**
- `HideoutTab.Crafting` 탭 버튼 `hideout-tab-crafting`("제작"). `CraftingViewModel`(새, 순수: 줄 = 이름(없으면 산출 이름 ×n)·재료 칸(이름·"가진 수/필요 수")·`HasMaterials`(같은 정의 합산)·`CanCraft` = 재료 + 떠나는 중·읽기 전용 아님),
  `CraftingView`(새, `crafting-panel`·`crafting-title`·`crafting-hint`·`crafting-recipes`, 줄 클래스 `crafting-row`·`crafting-row-name`·`crafting-input`(+`--missing`)·`crafting-input-name`·`crafting-input-count`·`crafting-row-button`).
  `HideoutPresenter.Crafting`(상인처럼 지연 생성·Refresh가 버림), `Craft(recipeId)` → 결과 문구는 허브 메시지 줄: 성공 "만들었다: 레시피 이름", 재료 부족 "재료가 모자란다.", 공간 없음 "보관함에 자리가 없어 만들 수 없다.", 모르는 id "알 수 없는 제작법이다.", Unavailable "지금은 할 수 없다.".
- 부위 상태 화면(H): `BodyStatusPresenter.BuildSurvival(view)` → `SurvivalRow`("전신" + 칩: 수분 목마름/탈수, 에너지 배고픔/허기, 방사능 방사능 오염/방사선 피폭, 순서 수분 → 에너지 → 방사능, Normal은 칩 없음).
  `GameUI.uxml` 7부위 목록 아래 새 자리 `body-status-survival`(목록 `body-status-list`는 7줄 그대로). `BodyStatusScreenView.Render(rows, survival)` 추가(기존 `Render(rows)` 유지 = 전신 줄 비움). `GameUIRoot`가 둘 다 그린다.
- USS: 제작 탭 스타일(부족 재료 = Dried_Blood 배경·Rust_Red 테두리, 텍스트 Bone), 변수 `--crafting-name-width: 220px`(TODO(GDD-?) 그레이박스), 전신 칩 `bs-survival--low`(Dried_Blood)·`--critical`(Rust_Red 굵게)·`--radiation`(Toxic 테두리). 팔레트 토큰만.

**테스트**: 새 `EditMode/CraftingModelTests`(11), `HideoutServiceTests` +8(설정 없음·Recipes·장비 불산입·성공 → HideoutChanged·메모리만·W2 저장·다시 읽기 가득·NoSpace·모르는 id·떠나는 중·읽기 전용),
`HideoutUiModelTests` + `CraftingViewModelTests`(5)·`HideoutCraftingPresenterTests`(7)·`HideoutCraftingUxmlTests`(2), `UiModelTests` + `SurvivalRowTests`(11: 문구 6종·순서·RULE-05 정규식 `[0-9%０-９█▓▒░■□|/]` 27조합·GameUI.uxml 위치·렌더),
새 `PlayMode/HideoutCraftingFlowTests`(Boot → [은신처] → 보관함에 JNK-01×2·JNK-03 지급 → [제작] 탭 → RCP-MED-ANT 비활성·RCP-AMMO-556 [제작] 클릭 → 보관함 = 가득 찬 MAG-556-30·탄피 불변·파일 미변경 → [메인 메뉴] save.json → 새 ProfileService로 다시 읽기 → 재진입).

**설계와 다른 점·정한 것**
- 읽기 전용이면 제작 `Unavailable`·버튼 비활성(오케스트레이터 지시. 상인 거래는 M5대로 읽기 전용에서도 된다 — 불일치는 TODO 없이 의도).
- 무효 레시피(재료 없음 등)·중복 id는 목록에서 빼고 `UnknownRecipe`(TODO(GDD-?) Q47 데이터 오류 취급). 재료 고르기 = 보관함 배치 순(설계 "보관함 배치 순"을 상인 매도 목록과 같은 위→아래·왼→오른쪽으로).
- 결과 문구는 별도 칸이 아니라 허브 메시지 줄(상인·키트와 같음). 레시피 이름에 "(30발)"처럼 용량이 들어간 것은 7C-1b 데이터 이름 그대로(현재 탄 수가 아니라 RULE-04 대상 아님).
- `PlayMode/HideoutCraftingFlowTests`는 `HideoutController.Crafting`이 씬에 연결돼 있어야 통과한다(연결 전에는 "7C-3 씬 메뉴" 메시지로 실패) — 7C-3 이후 원본에서 통과.

**7C-3이 할 일**
- Hideout 씬 `HideoutController.crafting` ← `M7Names.CraftingConfigPath`(직렬화 이름 `crafting`). 주의: `EditorSceneManager.OpenScene(Single)` **뒤에** `AssetDatabase.LoadAssetAtPath`로 읽을 것 — 먼저 읽으면 씬 열기의 언로드로 참조가 끊겨 `{fileID: 0}`으로 저장된다(사본에서 실제로 겪음).
- 연결 후 원본 PlayMode에서 `HideoutCraftingFlowTests` 통과 확인. 에디터 확인: 은신처 [제작] 탭 줄 배치·스크롤, H 화면 전신 줄(물 0 근처·폭풍 피폭) 문구.

## 7C-3 구현 메모 (gameplay-programmer, 2026-10-23)

원본에서 7C-1·1b·2a·2b·2c를 처음 함께 컴파일 — **충돌·컴파일 오류 없음**(코드 수정 없이 통과). 통합 전 EditMode 1720/1723: 실패 3개는 2c 메모대로 M_Toxic 없음(`PaletteConsistencyTests.PaletteMaterials_*` 2·`AtmosphereAssetTests.CreateAssets_Rerun_KeepsGuidsAndBytes`) — 메뉴 실행 뒤 해소. 커밋하지 않음.

**코드**
- `EditorTools/M7SceneSetup.cs`: `CreateAssets`에 `M7SurvivalDataSetup.WriteM7SurvivalData()`(M7AiDataSetup 다음. `CreateData`는 M1~M7B 연쇄를 한 번 더 돌릴 뿐이라 7C 몫만 — 결과 같음),
  `ApplyRaid`에 `M7HazardSceneApply.ApplyRaid(scene)`(주석 줄 살림), 새 `ApplyHideout(scene)`(씬에서 HideoutController 정확히 1개 → `crafting` ← `M7Names.CraftingConfigPath`, 같으면 변경 없음·다르면 `MarkSceneDirty`. 에셋은 `ApplyToSceneFile`의 `OpenScene(Single)` 뒤 함수 안에서 읽는다 — 2b 메모의 참조 끊김 회피),
  `ApplyHideoutIfAssetsExist`·`HideoutAssetsExist`(CraftingConfig 있음), `ApplyAll`이 은신처 씬도 적용(바뀐 경우만 저장).
  `RaidAssetsExist`에 7C 조건: MED-ANT-01·GEAR-GSM-01·CraftingConfig 있음 + RaidConfig에 RAD-REF(`M7HazardSceneApply.TryFindZone`).
- `EditorTools/M5SceneSetup.cs`: 은신처 씬 저장 직전 `M7SceneSetup.ApplyHideoutIfAssetsExist(scene)` 한 줄(M6 호출 다음).
- 4번(물·음식·해독제 "흉부" 문구): 확인 결과 HUD·부위 화면·인벤토리 어디도 `MedicalUseStarted/Completed.Target`을 표시하지 않는다(UI는 이 이벤트를 구독하지 않음, 부위 이름은 부위 줄·사망 원인에만) — **UI 수정 불필요**. 진행 표시가 생기면(7D 등) `MedicalUseModel.NeedsSystemic`과 같은 판정(cures 빈·healAmount 0 + 수분/에너지/curesRadiation)으로 "전신"을 쓸 것.

**원본 메뉴 `M7SceneSetup.ApplyAll` 2회**
- 1회째: `M_Toxic.mat`(+ .meta) 생성, `Raid_CrimsonBasin.unity`(RadiationField + M7_ToxicMarkers 54개), `Hideout.unity`(`crafting` = CraftingConfig GUID) 저장. 데이터 에셋(PlayerConfig·RaidConfig·상인·루트 6개·DB·생존 아이템)은 7C-1b가 원본에서 이미 써 두어 이번에 바뀌지 않음.
- 2회째: 적 "변경 없음", 방사능 "새로 0, 다시 맞춤 0, 지움 0 — 변경 없음", 은신처 "연결 그대로", 두 씬 저장 안 함 → `Assets`·`ProjectSettings` 1271개 파일 md5 동일.

**결과**: EditMode 1723/1723, PlayMode 177/177(715초) — `AshStormFlowTests.PlayerDose_PeakTenSeconds_AndPauseFreezes`(Peak 선량 10 ± 0.5) 무변경 통과, `HideoutCraftingFlowTests`·`RadiationFieldFlowTests` 4·`SurvivalFlowTests` 6 통과. 테스트 전후 git status·md5 동일(테스트가 커밋 에셋을 바꾸지 않음).
새 `.meta`(Unity 생성): 7C-2 새 스크립트·테스트 11개 + M_Toxic.mat.meta.

## 7C-4 구현 메모 (test-engineer, 2026-10-23)

원본에서 검증만(제품 코드·데이터·씬·공용 테스트 도우미 무변경). 새 파일은 `PlayMode/M7SurvivalRaidFlowTests.cs` 하나(+ Unity가 만든 .meta). 커밋하지 않음.

**새 테스트 `M7SurvivalRaidFlowTests` (2, SaveSandbox 새 폴더, 적 제거, M5Hideout·M4Raid 도우미 그대로 사용)**
- `GasMaskPeak_RefineryZone_Antidote_Extract_NextRaidSurvivalReset`: MainMenu [은신처] → 키트 → [상인] UI로 방독면(GEAR-GSM-01)·해독제(MED-ANT-01) 구매 → 방독면 헬멧 슬롯·권총·해독제 반입 → [레이드 시작]
  → `GasMaskWorn`·선량 0 → 폭풍 `ForceStartNow` + `AdvanceTime` Peak, 2초 → 률 0·선량 0, 가이거 입력 = 1(방독면 무관, Q41)
  → RAD-REF 중심 `PlaceAt` 2초 → 률 = 구역 0.5(폭풍 0)·선량 ≈ 0.5 × 2 증가, `AudioDirector.ZoneRadiationPerSec` > 0, 가이거 입력 = 1.5
  → 스폰으로 돌아가 선량 고정 → 해독제(실제 `TryUse` 경로, Rejected 없음) → 선량 0·최대 HP 배율 1·해독제 소모, Peak에서도 0 유지
  → `Survival.Tick(300)`으로 수분·에너지 감소, 구역에 1초 다시 들어가 선량 > 0 → EXT-01 탈출 → 은신처 결과 [확인]: 방독면 반출(헬멧), save.json에 hydration/energy/radiation/dose 문자열 없음(Q32)
  → 다음 [레이드 시작]: 새 PlayerHealth, 수분·에너지 = 최대(±0.5), 선량 0, 생존 등급 3종 Normal, 폭풍 Pending, 반출 방독면 다시 착용.
- `CraftedMagazine_Loadout_ReloadAndFireInRaid`: 은신처 → 키트 → RCP-AMMO-9MM12 재료(레시피 뷰의 JNK 줄 그대로)를 보관함에 지급 → [제작] 탭·[제작] 버튼(UiPointer) → 재료 0·새 MAG-9MM-12 1개(가득, Q46)
  → 키트 탄창은 보관함에 두고 제작 인스턴스만 `TryQuickMove` → 반입 = 권총 + 그 인스턴스 → 레이드: 반입 탄창 12발 → R → `LoadedMagazine`가 반입 인스턴스와 같음·12발 → LMB 1발(ShotFired 권총·투사체 1) → 11발.

**결과**: 필터 실행 2/2(32초). EditMode 1723/1723(종료 0). PlayMode 전체 179/179 × 2회 연속(752초·753초, 종료 0) — 기존 177 + 새 2, `AshStormFlowTests`·`M6RaidFlowTests`·`SurvivalFlowTests`·`RadiationFieldFlowTests`·`HideoutCraftingFlowTests` 포함.
Mono `BuildWindows64` 종료 0: "Succeeded, 오류 0, 경고 0", 110,232,870 bytes. 테스트·빌드 전후 git status 같음(새 .meta만; 테스트·빌드가 재저장한 머티리얼·설정은 바이트 동일). 결함 없음.

**AC2 판정**
| 항목 | 자동 근거 | 판정 |
|---|---|---|
| 제작대 | EditMode `CraftingModelTests` 11·`HideoutServiceTests`(제작 +8)·`CraftingViewModelTests` 5·`HideoutCraftingPresenterTests` 7·`HideoutCraftingUxmlTests` 2·`M7SurvivalDataAssetTests`(레시피 표·산출 매도 ≤ 재료 매수), PlayMode `HideoutCraftingFlowTests`(제작 → 저장 → 다시 읽기)·`M7SurvivalRaidFlowTests` ②(제작 탄창으로 레이드 재장전·사격) | 자동 통과. 에디터 확인 C6(탭 배치·문구)·C7(경제 체감) 필요 |
| 방사능 | EditMode `RadiationZoneMathTests` 14·`RadiationDoseTests` 12·`BodyHealthSystemicTests` 16(최대 HP ×0.8)·`MedicalSurvivalTests` 11(해독제)·`SurvivalRowTests` 11, PlayMode `SurvivalFlowTests`(방독면·선량 100·해독제)·`RadiationFieldFlowTests` 4(구역 률·Toxic 표시·가이거)·`AshStormFlowTests`(Peak 10 ± 0.5 무변경)·`M7SurvivalRaidFlowTests` ① | 자동 통과. 에디터 확인 C2(방독면 체감)·C3(Toxic 표시·가이거 체감)·C4(등급 체감) 필요 |
| 탈수 | EditMode `SurvivalModelTests` 14(감소율·복부 ×2·등급)·`BodyHealthSystemicTests`(부위마다 0.5/s)·`MedicalSurvivalTests`(물), PlayMode `SurvivalFlowTests`(수분 0 → 부위 피해 → 물로 멈춤)·`M7SurvivalRaidFlowTests` ①(다음 레이드 초기화) | 자동 통과. 에디터 확인 C1(전신 줄 문구)·C5(16.7분 체감) 필요. 머리가 70초에 먼저 0 → 사망(7C-2a 메모, Q33 기획 확인 대기) |
| 허기 | EditMode `SurvivalModelTests`·`StaminaRegenMultiplierTests` 5·`MedicalSurvivalTests`(음식), PlayMode `SurvivalFlowTests`(에너지 0 → 재생 ×0.5)·`M7SurvivalRaidFlowTests` ① | 자동 통과. 에디터 확인 C1·C5(23.8분 체감) 필요 |
| RULE-05 | `SurvivalRowTests`(숫자·막대 정규식 27조합), 기존 `UiModelTests` | 자동 통과. C1 사람 확인 |

## 7C 리뷰 수정 메모 (gameplay-programmer, 2026-10-23)

원본에서 수정(M7_review.md "7C 생존·제작" 1~9, 오케스트레이터 결정대로). 새 파일·새 `.meta`·에셋·씬 변경 없음. CLAUDE.md는 고치지 않음(마일스톤 끝). 커밋하지 않음.

- **1 (중간) 탈수에서 머리 제외:** `BodyHealthModel.ApplySystemicDamage`가 머리를 건너뛴다 → 흉부 80이 160초에 0 → 사망(사인 흉부, Q33 A 문구). 주석 `TODO(GDD-?) Q33`: 전신 사인 문구("탈수")는 MVP 남은 목록.
  테스트: `SystemicDamage_FullHealth_HeadReachesZeroFirst` → `SystemicDamage_FullHealth_ThoraxReachesZeroFirst_At160Seconds`(159초 생존·머리 그대로·160초 흉부 사망, 이유 주석), `SystemicDamage_HeadNeverDamaged` 새로,
  `SystemicDamage_EveryIntactPart_PerPartRate`는 머리 = 그대로로 단언. PlayMode `SurvivalFlowTests.Dehydrated_DamagesEveryPart_TierEvent` → `Dehydrated_DamagesPartsExceptHead_TierEvent`(머리 손실 0, 나머지 0.4~0.65).
  주석·툴팁 갱신: `PlayerHealth`, `PlayerConfigSO.dehydratedHpPerSec`, `M7SurvivalDataSetup.DehydratedHpPerSec`(값 그대로 — 에셋 무변경).
- **2·3·9 CHANGELOG:** "M7 설계 결정" 7C 소절에 7C 구현 결정(머리 제외, public 읽기, 전신 = Thorax, 테스트 씬에서도 감소, 무효·중복 레시피, 재료 배치 순·합계 판정, 읽기 전용 제작만 Unavailable 사유, 가이거 = 폭풍 + 구역, 희귀도 3개 Common 유지),
  §13.3 경계, 자리표시 에셋(M_Toxic·Toxic 표시물 54개·은신처 연결), 7C 임시값 TODO 목록, 리뷰 수정 1~9 요약을 추가했다.
- **4 재료 칸 충족 = 정의 합계:** `CraftingViewModel.Build`가 정의별 필요 합계를 먼저 구하고 칸마다 `Owned ≥ 합계`로 표시(버튼 판정과 같음, 문구 "가진 수/줄 필요 수"는 그대로). Core `CraftingInputView.IsSatisfied`는 줄 단위라는 주석만 추가(API 불변).
  테스트: `SameDefinitionTwoLines_SummedForCanCraft`(두 칸 모두 부족 표시로 바뀜) 갱신, `SameDefinitionTwoLines_EnoughForTotal_AllSatisfied` 새로.
- **5 사망 시 break:** 같은 호출에서 흉부가 0이 되면 남은 부위(복부·팔·다리)를 깎지 않는다. 테스트 `SystemicDamage_DeathStopsRemainingParts`.
- **6 생존 필드 범위:** `PlayerConfigSO` 생존 11개에 `[Min(0)]`(최대·감소율·탈수 피해·복부 배율·선량 경계 8개)·`[Range(0,1)]`(굶주림 재생 배율·피폭 최대 HP 배율·Low 비율 3개). 인스펙터 가드만이라 `PlayerConfig.asset` 값(전부 범위 안) 영향 없음.
  테스트 `SurvivalModelTests.cs`에 `SurvivalConfigRangeTests`(11 케이스, 속성 반영 검사).
- **7 주석:** `GeigerClickModel` 머리 주석 = 입력은 환경 방사능률(폭풍 + 구역, `AmbientSoundscape.EnvironmentRadiationPerSec`, 방독면 무관).
- **8:** 코드 변경 없음. 배치모드 전후 git status·md5(`Assets`·`ProjectSettings`, Tests 제외) 비교 — 동일.

**결과**: EditMode 1737/1737(기존 1723 + 14). PlayMode 전체 179/179 × 2회 연속(750초·748초, 종료 0). Mono `BuildWindows64` 종료 0: "Succeeded, 오류 0, 경고 0", 110,233,382 bytes.
테스트·빌드 전후 git status·md5 동일(커밋 에셋 무변경). Unity 프로세스 남지 않음(Hub 보조 `unity.exe serve`만).

## 7D-1 구현 메모 (gameplay-programmer, 2026-10-23)

원본에서 작업(커밋하지 않음). 새 `.meta`는 만들지 않았다(유니티가 생성). UI·Art/UI는 건드리지 않음(7D-2 병렬).

- **Core `Diagnostics/`(새)**:
  - `FrameTimeStats` — 고정 링 버퍼(가득 차면 가장 오래된 것부터 덮어씀) + 같은 크기 작업 배열, 직접 힙 정렬(`Array.Sort` 비교자 할당 회피). 중앙값(짝수 = 가운데 둘 평균),
    99% = nearest-rank `ceil(0.99 n)`(정수 계산 — `0.99f × 100`이 100.0000x가 되어 한 칸 밀리던 것을 막음), 최대, 평균·fps 추정(1000/평균). 음수·NaN 무시. Add·Compute·Clear 할당 0.
  - `SmokeRunner` — 순수 실행기: `ParseArguments`(`-ashfallSmoke <폴더>`, 이름 대소문자 무시, 상대 경로 → 절대, 값 없음·`-`로 시작 = 요청됐지만 무효 → 종료 1),
    단계 표를 한 틱에 한 단계까지 진행, 전체 실시간 제한 60초(`DefaultTimeoutSec`, 기술 상수 `TODO(GDD-?) Q51`) — 초과·단계 실패·단계 예외·외부 `Fail` = 종료 코드 1, 전부 통과 = 0. 단계마다 `[Smoke] 단계 i/N 이름 통과 (t초) — 설명` 한 줄.
  - `SmokeScenario`(설계의 "순수 단계 표") — `Boot`(MainMenu 대기) → `EnterHideout`(`GameFlow.EnterHideout` → Hideout + `IHideoutService`) → `HideoutSaveLoad`(`ProfileService.Save` → 저장소 다시 읽기·해석, version = 2, raidInProgress false)
    → `StartRaid`(`IHideoutService.StartRaid`, 파일 raidInProgress true 확인 → Raid + `IRaidInfo`·`IAshStormInfo`) → `ForceStorm`(이벤트 → Pending 벗어남) → `RaidPlay`(레이드 경과 10초, `DefaultRaidDurationSec` `TODO(GDD-?) Q51`)
    → `Abandon`(`RaidAbandonRequested` → 은신처 복귀) → `HideoutResult`(결과 Abandoned, 흐름 표·raidInProgress 해제, W1 저장 성공, 파일 version 2·raidInProgress false). Core 서비스·이벤트·GameFlow만 쓴다(`SmokeEnvironment`).
  - `DevDiagnostics`(MonoBehaviour, 정적 인스턴스 없음) — `ShouldAttach(isDebugBuild, isEditor) = isDebugBuild && !isEditor`. 10초(`LogIntervalSec`)마다 `[DevDiagnostics] 프레임 n개 — 중앙값·99%·최대 ms, fps 약 N` 한 줄,
    링 버퍼 4096(`FrameSampleCapacity`, 둘 다 기술 상수 `TODO(GDD-?) Q51`). 스모크면 Awake(= GameManager Awake, 첫 씬 로드 전)에서 `GameManager.UseSaveStorage(new FileSaveStorage(폴더))` →
    실제 persistentDataPath를 읽거나 쓰지 않는다. `Application.logMessageReceived`로 예외 로그(버스가 삼킨 핸들러 예외 포함) = 실패, 오류 로그는 세어 마지막 줄에. 끝나면 `Application.Quit(종료 코드)`.
- **새 Core 계약(최소, 보고)**: `Events/GameEvents.cs` `AshStormStartRequested : IGameEvent`(스모크 "폭풍 강제 시작"용 — Core에 폭풍 명령 경로가 없어서). Raid `AshStormController`가 OnEnable 구독·OnDisable 해지,
  `Debug.isDebugBuild`(개발 빌드·에디터)일 때만 기존 `ForceStartNow()`를 부른다(릴리스 무시). 다른 기능 변경 없음.
- **`GameManager`**: Awake 끝 한 줄 `if (DevDiagnostics.ShouldAttach(Debug.isDebugBuild, Application.isEditor)) gameObject.AddComponent<DevDiagnostics>();`. 세이브 폴더 교체는 DevDiagnostics가 기존 `UseSaveStorage`로 한다.
- **`FileSaveStorage`**: 폴더 주입 생성자 `FileSaveStorage(string directory, string fileName = "save.json")`가 M5부터 있어 변경 없음.
- **`EditorTools/BuildTools.cs`**: `BuildWindows64`(릴리스, 출력 그대로) 유지 + `BuildWindows64Development`(`Builds/Windows64Dev/`) + `BuildWindows64Il2Cpp`(`Builds/Windows64Il2Cpp/`)·`BuildWindows64Il2CppDevelopment`(`Builds/Windows64Il2CppDev/`, 7D-IL2CPP 스모크용 — 같은 확인).
  IL2CPP: `EnsureIl2CppAvailable` = `BuildPipeline.IsBuildTargetSupported(Standalone, StandaloneWindows64)` + `GetPlaybackEngineDirectory/Variations`의 `win64_*_il2cpp` 폴더. 판정은 순수 `HasWin64Il2CppVariation`·`DescribeIl2CppProblem`.
  없으면 `InvalidOperationException("[BuildTools] IL2CPP 모듈 설치 필요: …")` → 배치모드 종료 1. 있으면 이 빌드 동안만 `PlayerSettings.SetScriptingBackend(Standalone, IL2CPP)`(개발이면 스택 추적 `MethodFileLineNumber` — Unity 6 이름) → finally에서 둘 다 되돌림.
  ProjectSettings 백엔드는 Mono 그대로(테스트로 고정). 메뉴 `Ashfall/Build/Windows64 (Development)`·`Windows64 IL2CPP`·`Windows64 IL2CPP (Development)` 추가.
- **`Assets/_Project/link.xml`(새)**: 런타임 Ashfall 어셈블리 9개 `preserve="all"`(EditorTools 제외). Managed Stripping Level 그대로.
- **테스트(EditMode, 새 3파일)**: `FrameTimeStatsTests` 11(빈 값·중앙값 홀짝·99%·최대·fps·링 덮어쓰기·정렬이 링 순서를 안 바꿈·무효 표본·Clear·할당 0),
  `SmokeRunnerTests` 14(인자 파싱 4, 진단 부착 조건, 단계 표 순서, 실행기 순서·종료 0·시간 초과(단계 이름·대기 설명)·전체 기준 제한·실패/예외·외부 실패,
  가짜 씬 로드로 단계 표 끝까지 — 실제 `GameFlow`·`ProfileService`·`HideoutService` + 메모리 세이브: 통과·폭풍 미시작 = 시간 초과·저장 실패 = HideoutSaveLoad 실패),
  `BuildToolsTests` 6(변형 폴더 판정, 문구, **이 PC 실제 확인**(모듈 없으면 예외 문구, 설치 뒤엔 통과로 바뀜), 백엔드 Mono 고정, 출력 경로, link.xml이 런타임 asmdef 전부 포함).

**실제 확인(이 PC)**
- `-executeMethod …BuildWindows64Il2Cpp` → 종료 1, `InvalidOperationException: [BuildTools] IL2CPP 모듈 설치 필요: win64 IL2CPP 플레이어 변형(win64_*_il2cpp)이 없다 (…\PlaybackEngines\WindowsStandaloneSupport\Variations). …`. 백엔드 변경 전 실패.
- `BuildWindows64Development` → 종료 0, Succeeded·오류 0·경고 0, 175,089,169 bytes(4분 42초).
- 스모크 `Builds/Windows64Dev/Ashfall.exe -batchmode -nographics -ashfallSmoke <scratchpad>\smoke_save -logFile smoke.log` → **종료 0**, 8단계 전부 통과(전체 14.7초, 오류 로그 0개), 프레임 시간 한 줄
  (nographics: 중앙값 1.07ms·99% 2.50ms), `smoke_save/save.json`(version 2, raidInProgress false)·`.bak` 생성. 실제 `LocalLow/…/Ashfall/save.json`은 시각 그대로(건드리지 않음).
- `BuildWindows64`(릴리스) → 종료 0, Succeeded·오류 0·경고 0, 110,251,190 bytes. 릴리스 exe에 `-ashfallSmoke`를 줘도 `[DevDiagnostics]`·`[Smoke]` 로그 0줄, 폴더 생성 없음(15초 뒤 종료시킴) = 진단 컴포넌트 없음.

## 7D-2 구현 메모 (ui-programmer, 2026-10-23)

§6 희귀도 색(Q49 A). UI 어셈블리·USS·테스트만 고쳤다(Core·EditorTools·데이터 무변경, RULE-04/05 영향 없음 — 문구·수치 표시 그대로).

- **새 `UI/RarityStyle.cs`(순수 static):** `ClassFor(Rarity|ItemDefinitionSO|ItemInstance)` → `rarity--scrap/common/uncommon/rare/relic`(null 정의 = null),
  `Apply(VisualElement, …)` = 희귀도 클래스 하나만 남기고 공통 `rarity` 클래스를 켠다(null이면 전부 지움).
- **칸 뷰:** 아이템 칸을 그리는 곳은 `InventoryScreenView.BuildItem` 하나(소지품 그리드·장비 슬롯·열린 루트 = 레이드 상자/시체·은신처 보관함 공통) + 퀵슬롯 칸 + 드래그 고스트.
  상인은 줄 목록이라 `TraderView.Row` 이름 칸(`trader-row-name`) 왼쪽 테두리(3px, 유물 4px)로 표시 — 매수(정의)·매도(보관함 인스턴스) 둘 다.
  **검색 전 "?" 아이템에는 붙이지 않는다**(§6 검색 연출 — 희귀도가 미리 드러나지 않게).
- **USS(`Ashfall.uss`):** `:root`에 `--rarity-rare-blue: #4E6C8A`(팔레트 밖 유일한 색, TODO(GDD-?) Q49) 추가. 규칙은 `.inv-item.rarity--*`·`.inv-quick.rarity--*`·`.trader-row-name.rarity--*`
  (두 클래스 선택자라 기본 `.inv-item` Rust_Red 테두리를 덮는다): 고철 `--ash-grey`, 일반 `--bone`, 우수 `--toxic`, 희귀 `--rarity-rare-blue`,
  유물 `--ember` 2px + 바탕 `--rust-red`. **설계와 다른 점:** UI Toolkit에 box-shadow가 없어 유물 "발광"은 두꺼운 Ember 테두리 + Rust_Red 바탕으로 근사(TODO(GDD-?) Q49).
  우수 Toxic 재사용으로 "Toxic = 유일한 비적색 강조"(§10.1)가 인벤토리에서 흔해진다 — 설계 Q49대로 두고 7D-4 CHANGELOG 대상.
- **테스트:** 새 `EditMode/RarityStyleTests`(9: 매핑·enum 순서·null, Apply 교체/해제, USS 색 5종 × 칸 3종, HideoutUI.uxml 위 뷰 — 그리드 고철·보관함 유물·"?" 클래스 없음·장비 슬롯 우수·퀵슬롯 일반/빈 칸 없음·상인 매수 희귀/일반·매도 유물).
  `PaletteConsistencyTests.Uss_ColorLiterals_ArePaletteTokens_ExceptRareBlue`(새 케이스: USS 색 변수 = 팔레트 RGB, 팔레트 밖은 `--rarity-rare-blue` 하나뿐, 색 속성은 정의된 색 변수만).
  기존 `HideoutUxmlTests.Uss_UsesOnlyPaletteColors`는 `--rarity-rare-blue:` 정의 줄 하나만 건너뛰게 고쳤다(그대로면 새 색으로 실패).
- **결과(사본, 다른 에이전트 진행 중 Core 파일은 사본에서만 HEAD로 격리):** EditMode 1747/1747(종료 0). PlayMode 필터(은신처·HUD·인벤토리·루트·UI 입력) 18/18, PlayMode 전체 179/179(819초, 종료 0).
  사본 첫 실행은 복사한 Library가 ScriptableObject를 못 읽어 데이터 테스트 105개가 실패 → 사본 Library 삭제 후 재임포트하니 해결(코드 무관).
- **에디터 확인(C8):** 은신처 [장비]·[상인]과 레이드 인벤토리에서 칸 테두리 색 5종, 유물(ITM-REL-01, 보스 루트) 테두리가 팔레트와 어울리는지, 희귀 청색이 튀지 않는지.

**결과**: EditMode 1778/1778(종료 0 — 이 수에는 동시에 원본에 반영된 7D-2 `RarityStyleTests` 등이 포함). PlayMode 전체 179/179(822초, 종료 0, 로그에 `DevDiagnostics` 0줄 = 에디터 Play에 진단 없음).
`ProjectSettings` 무변경(백엔드 Mono). Unity·빌드 프로세스 남지 않음(Hub 보조 `unity.exe serve`만).

## 7D-3 구현 메모 (test-engineer, 2026-10-23)

원본에서 검증. 제품 코드·데이터·씬·ProjectSettings 무변경(새 테스트 파일 1개). 커밋하지 않음. 새 `.meta`는 유니티가 만든 `M7PerformanceTests.cs.meta` 하나.

**새 PlayMode `M7PerformanceTests` (1, Q50 A — 예산 그대로)**
- 실제 레이드 씬(SP-W), 34명 무적(복제 아키타입), 측정 직전 `RaidPerformanceTests.RefillAmmo`, 무적 표적 `PerfTarget`(근접 = 부위 힌트 있는 피격, 총탄 따로 셈) = 청취자, 폭풍 Peak 강제(시야 30m).
- 배치: 표적 = 보스 거점 중심 5m(보스 눈 시선 뚫림, Peak 30m 안에서 보스가 거점 어디로 가도 보이게), 변이체 8명 2~6m, 저격수 2명 22m(시선 뚫림·표적 쪽), M4 20명 12~24m 고리, 보스·호위는 제자리.
  플레이어는 SP-W(약 330m)에서 펌프 샷건(펠릿 8)을 Primary2로 들고 하늘로 연사(LMB 토글, 튜브는 테스트가 채움).
- 측정 A 10초(시간·사격 단언), 측정 B ≥ 300프레임·최소 5초(GC: AiDirector·WeaponController는 끄고 직접 호출, 투사체·PlayerHealth·Audio·폭풍은 추가 호출, RadiationField 질의),
  측정 A의 ShotFired·FootstepTaken·EnemyVocalized 재발행(오디오 이벤트 경로). 투사체 상한 = Σ(rpm/60 × 비행 시간 + 2) × 펠릿.
- 테스트 쪽 조정 2가지(결함 아님 — 아래 발견 1·2 참고): ① 표적을 0.8m 띄움(`TargetRaiseM`, 상자 위에 선 것처럼, 근접 높이 차 1.2m 안). ② GC 구간 전에 씬 콜라이더 263개의 관리 래퍼를 미리 만든다(`WarmColliderWrappers`).

**최악 상황 측정값 (PlayMode 전체 2회 = 3회째·4회째 실행)**

| 항목 | 예산 | 3회째 | 4회째 |
|---|---|---|---|
| AI 틱 평균 / 99% / 최대 | ≤ 1.0 / ≤ 3 ms | 0.175 / 0.560 / 0.997 | 0.187 / 0.459 / 1.074 |
| 투사체 스텝 평균 / 최대 | ≤ 1.0 ms | 0.050 / 0.238 | 0.056 / 0.432 |
| 프레임 중앙값 / 99% (렌더 제외) | ≤ 16.6 ms | 1.90 / 3.25 | 2.07 / 3.35 |
| Audio + 폭풍 + RadiationField 평균(최대) | ≤ 0.3 ms | 0.0035+0.0006+0.0012 = 0.0053 (0.379) | 0.0033+0.0007+0.0012 = 0.0051 (0.272) |
| GC 할당 프레임 (AI·무기·투사체·생존·Audio+폭풍·방사능) | 0 | 0·0·0·0·0·0 / 2418프레임 | 0·0·0·0·0·0 / 2451프레임 |
| 오디오 이벤트 재발행 GC | 0 | 627개 0 | 632개 0 |
| 교전 평균 / 생존 최소 | — | 33.7 / 34 | 33.7 / 34 |
| 적 ShotFired (LMG · DMR) | > 0 | 524 (60 · 8) | 513 (60 · 7) |
| 플레이어 샷건 / 근접 타격 / MutantAttack | > 0 | 12 / 72 / 20 | 11 / 74 / 20 |
| 섬광 켜진 프레임 | > 0 | 전 프레임 | 전 프레임 |
| 동시 탄 최대 / 이론 상한 | ≤ 상한 | 30 / 624 | 29 / 624 |
| GC 구간 중 샷건 · 적 사격(LMG) · 근접 | > 0 | 6 · 285(34) · 38 | 6 · 265(33) · 37 |

- 변이체 최근접(표적 발 수평) 1.06m, 보스-표적 최대 4.0m, 오디오 재생 ~2700·voice 빼앗기 ~2200, 플레이어 선량 ~25(SP-W, Peak).
- 같은 실행의 `M6PerformanceTests`: AI 0.164/0.545·0.191/0.584ms, `RaidPerformanceTests` B: AI 0.163/0.474·0.160/0.447ms — 기존 예산 안.

**결과**
- EditMode 1778/1778(종료 0, 34초).
- PlayMode 180/180 × 2회 연속(805초·806초, 종료 0). 그 전 2회(799·807초)는 179/180 — `M7PerformanceTests` 투사체 경로 GC 1프레임(아래 발견 2) → 테스트 수정 뒤 필터 실행 4회 + 전체 2회 모두 0.
- Mono `BuildWindows64` 종료 0: Succeeded·오류 0·경고 0, 110,251,222 bytes(15.7초). `BuildWindows64Development` 종료 0: Succeeded·오류 0·경고 0, 175,089,201 bytes(49.1초).
- 개발 빌드 스모크 `Builds/Windows64Dev/Ashfall.exe -batchmode -nographics -ashfallSmoke <scratchpad>\smoke7d3 -logFile <scratchpad>\smoke7d3.log` → **종료 0**, 8/8 단계 통과(14.0초, 오류 로그 0),
  `[DevDiagnostics]` 한 줄(nographics 중앙값 0.71ms·99% 1.81ms), `smoke7d3/save.json` version 2·raidInProgress false + `.bak`. 실제 `LocalLow/…/save.json` 시각 그대로(10-22 15:12).
- `BuildWindows64Il2Cpp` → 종료 1, `InvalidOperationException: [BuildTools] IL2CPP 모듈 설치 필요: …win64_*_il2cpp…` = **사용자 설치 대기**. ProjectSettings 백엔드 변경 없음.
- 테스트·빌드 전후 `Assets`(Tests 제외)·`ProjectSettings`·`Packages` md5 946파일 동일, git status 차이 = 새 `M7PerformanceTests.cs(.meta)`뿐.

**발견 (제품 코드 — 고치지 않음, 7D-4 리뷰가 판단)**
1. **변이체 둘레 + 지면 표적 = 다른 적이 표적에 겹쳐 들어온다.** 1차 실행(표적 지면, 거점 12m): 변이체 8명이 1.35m 둘레를 감싸 모든 사수의 눈 → 표적 눈·중심 레이가 변이체 캡슐에 막혔다.
   SCV·RDR·호위는 표적을 못 본 채 총성 자극·LKP(= 표적 발)로 걸어 들어가 수평 0.0~2.1m에 겹쳤고(34명 중 28명 0~2m, Alert/Search), 90초 동안 보스는 교전에 들지 못했다(재현: 이 메모의 `TargetRaiseM`을 0으로).
   7B 리뷰 15는 변이체만 멈추게 했다 — SCV·RDR은 표적 근처에서 멈추는 규칙이 없고 NavMeshAgent는 플레이어 CharacterController와 부딪히지 않는다.
   실제 플레이: 변이체 무리에 둘러싸인 플레이어에게 다른 적이 몸을 겹쳐 오고, 겹친 적이 다시 시야를 막는다. 방안: SCV·RDR도 LKP를 일정 거리(예: 2m) 앞에서 끊기, 또는 수색 목적지를 LKP 둘레로. TODO(GDD-?).
2. **GC 판정: 투사체 경로 1블록(2400여 프레임 중 1프레임, 예열 전 6회 실행 중 3회, 그중 1회는 AI 경로도 1블록).** 탄·시야 레이가 처음 맞힌 콜라이더의 관리 래퍼(`hit.collider`·`transform.root`)를 엔진이 처음 만들 때의 1회 할당으로 본다
   (예열 뒤 6회 연속 0). 정상 상태 프레임 할당은 아니다. `GC.GetAllocatedBytesForCurrentThread`는 이 Mono에서 실제 할당에도 0을 돌려줘 바이트 판정은 쓸 수 없었다.
3. `EventCapture` 상한 512 때문에 재발행 총성은 512개에서 잘린다(측정 구간 실제 적 ShotFired 510~535). 재발행 GC 판정에는 영향 없음.

**M7 AC 체크표**

| AC | 항목 | 자동 근거 | 에디터·빌드 사람 확인 | 대기 |
|---|---|---|---|---|
| AC1 | 무기 5종(PST·SMG·RIF·SHT·DMR) | EditMode `M7WeaponDataAssetTests`·`JamRulesTests`·`WeaponModelWearTests`·`TubeReloadTests`·`LooseAmmoSourceTests`·`PlayerInventoryLooseAmmoTests`·`WeaponSwitchModelTests`·`JamClearModelTests`·`PelletSpreadTests`·`SaveSerializerV2Tests`, PlayMode `WeaponSwitchFlowTests`·`JamFlowTests`·`TubeReloadFlowTests`·`M7WeaponRaidFlowTests`·`M7WeaponSceneSmokeTests`, `M7PerformanceTests`(샷건 연사) — 통과 | §7.1 W1~W8 | — |
| AC1 | AI-SNP | `M7AiDataAssetTests`·`SniperTacticsTests`·`AnchorTests`·`EnemyTacticsFactoryTests`, PlayMode `SniperFlowTests`·`M7AiRaidFlowTests`(DMR 내구도·루팅)·`M7EnemySceneSmokeTests`, `M7PerformanceTests`(섬광·DMR 사격) — 통과 | E1·E2·E7 | — |
| AC1 | AI-MUT | `EnemyMeleeTests`·`MutantTacticsTests`, PlayMode `MutantFlowTests`·`M7AiRaidFlowTests`(울음·접근·겹침 ≥ 1.0m), `M7PerformanceTests`(근접 72~75) — 통과 | E3·E5·E7 (+ 발견 1) | — |
| AC1 | AI-BOS | `M7AiDataAssetTests`(LMG + 호위 3)·`CorpseLootBuilderTests`·`EnemyWeaponTests`, PlayMode `M7AiRaidFlowTests`(유물·거점 25m), `M7PerformanceTests`(LMG ShotFired 60) — 통과 | E4·E5·E6 | — |
| AC1 | 오디오(세트 6·클릭·울음) | `AudioConfigAssetTests`, PlayMode `AudioFlowTests` — 통과 | W6, M6 A1~A9 | — |
| AC2 | 제작대 | `CraftingModelTests`·`HideoutServiceTests`·`HideoutUiModelTests`, PlayMode `HideoutCraftingFlowTests` — 통과 | C6·C7 | — |
| AC2 | 방사능 | `RadiationZoneMathTests`·`BodyHealthSystemicTests`·`MedicalSurvivalTests`, PlayMode `RadiationFieldFlowTests`·`AshStormFlowTests`·`M7SurvivalRaidFlowTests` — 통과 | C2·C3·C4 | — |
| AC2 | 탈수·허기 | `SurvivalModelTests`(스태미나 포함)·`UiModelTests`(RULE-05)·`M7SurvivalDataAssetTests`, PlayMode `SurvivalFlowTests` — 통과 | C1·C5 | — |
| AC2 | 희귀도 색(§6) | `RarityStyleTests`·`PaletteConsistencyTests` — 통과 | C8 | — |
| AC3 | Windows 빌드(Mono) | `BuildToolsTests`·`BuildSettingsTests`, 릴리스·개발 빌드 성공(경고 0), 개발 빌드 스모크 종료 0·8단계·save v2 | P3(릴리스 실행·세이브 유지) | — |
| AC3 | Windows 빌드(IL2CPP) | `BuildToolsTests`(이 PC = 모듈 없음 예외), `BuildWindows64Il2Cpp` 종료 1 "IL2CPP 모듈 설치 필요" | P4 | **사용자 설치 대기**(Hub 모듈 + VS 2022 C++) → 7D-IL2CPP |
| AC3 | 성능(CPU 예산·GC) | `M7PerformanceTests`(최악)·`M6PerformanceTests`·`RaidPerformanceTests`·`AiPerformanceTests`·`FrameTimeStatsTests`·`SmokeRunnerTests` — 통과(위 표) | P2(GPU·Profiler) | — |
| AC3 | 1080p 60fps(GTX 1660) | 자동 판정 불가(배치모드 렌더 없음) | **사람 측정**: P1 — 개발 빌드 1080p 전체 화면, Peak 폭풍 + 교전 + 변이체 무리에서 `Player.log`의 `[DevDiagnostics]` 중앙값 ≤ 16.6ms, GPU 이름 기록 | — |

## 7D 리뷰 수정 메모 (gameplay-programmer, 2026-10-23)

원본에서 작업(커밋하지 않음). 새 `.meta`는 유니티가 만든 `ApproachRules.cs.meta`·`ApproachStopTests.cs.meta`뿐. CLAUDE.md는 고치지 않았다(오케스트레이터 몫).

**7D-3 발견**
- 1 (적 겹침): `AiConfigSO.approachStopDistanceM` 2.0(TODO(GDD-?)) + 순수 `AI/Brain/ApproachRules`(`StopShort`·`IsWithinStop`) + `TacticsContext.ApproachPoint`(당긴 점을 NavMesh 샘플, 같은 표적은 캐시 — 방향은 처음 잡은 자리 기준, 이미 정지 거리 안이면 자기 위치).
  `ScavengerTactics.ChaseLastKnown`(SCV Alert·Combat 안 보임, RDR·보스·호위의 엄폐 없음 대체)과 Combat "선호 거리 밖 → 달려서 좁힘"이 목적지·도착 판정에 같은 점을 쓴다. 거점 자르기는 그 뒤(정지 점도 반경 안). 수색·엄폐·변이체 둘레는 그대로.
  `M7AiDataSetup.AiConfigM7Fields`에 필드 추가(파일에 없을 때만 SetDirty) → `AiConfig.asset`에 `approachStopDistanceM: 2` 한 줄.
  **바뀐 기존 단언 3개(이유: 목적지가 LKP 자체 → LKP에서 2m 앞으로 바뀐 것이 이번 수정의 의도):** `EnemyBrainTests.Alert_Scavenger_RunsToLastKnown`, `AiReviewFixTests.R3_CoverNone_StillFallsBackToScavenger`, `AnchorTests.Brain_ZeroAnchor_DestinationUnlimited` — 기대값만 `StopShort(…)`/`50 − 2`로. 수색·M3 PlayMode 행동 테스트는 변경 없이 통과.
  새 EditMode `ApproachStopTests` 10개(케이스 12: 기본값 2, 순수 규칙·경계·0/음수/NaN, SCV Alert 정지 점·NavMesh 높이·캐시, 정지 점 도착 → 제자리·LKP 쪽 보기, 이미 안 → 다가가지 않음, 0 = M3 동작, RDR 대체, Combat 원거리 접근, 거점 자르기).
  `M7PerformanceTests`: 사수(SCV·RDR·호위·보스) 표적 최근접 기록 + `≥ 1.0m` 단언. **`TargetRaiseM` 0은 불안정 → 0.8 유지:** 0으로 필터 1회 + 전체 2회 중 전체 1회째가 "90초 안에 보스 교전" 전제에서 실패
  (사수 20여 명이 테스트의 300m 소음으로 LKP 둘레 2m 고리에 한꺼번에 모여 회피로 0.6~1.1m까지 밀리고, 변이체와 함께 보스(거점 2.7m) 시선을 막음 — 적끼리 시야 가림은 7B 규칙). 통과한 2회는 사수 최근접 2.00m. 근거를 테스트 주석·CHANGELOG·§10 9번에 남겼다.
- 2 (GC 예열): 유지. 테스트 머리 주석에 근거(예열 전 6회 중 3회 1블록 → 예열 뒤 0, 콜라이더당 게임 전체 1회 엔진 래퍼 생성 = 정상 상태 할당 아님, 바이트 API 사용 불가).
- 3 (기록 상한): 512 → 1024(주석). 이번 측정 재발행 총성 505·514개로 잘리지 않음.

**7D 리뷰 (M7_review.md "7D")**
1. `Ashfall.uss` 끝에 `.inv-item.inv-ghost.inv-ghost--outside { border-color: var(--ember); }`(클래스 3개, 희귀도 규칙 뒤). `RarityStyleTests.Uss_GhostOutsideEmber_WinsOverRarityBorder` — 희귀도 5종마다 고스트 클래스 집합에 맞는 border-color 규칙을 구체성·순서로 골라 Ember·outside 규칙인지 확인.
2. §6 7D-IL2CPP에 ProjectSettings 확인 절차(빌드 전후 `git status ProjectSettings`·md5, 임시 diff `git checkout`). 3. 프레임 로그 `프레임 n개(전체 TotalAdded개, 버퍼 4096)` + 상수 주석 — 이번 nographics 스모크가 실제로 `전체 12257개`(약 1200fps)를 찍었다.
4. `SmokeRunner.IsSameFolder`·`PrepareSaveFolder`(persistentDataPath와 같으면 −1 → 종료 1, 폴더 생성, `save.json`·`.bak`·`.tmp`·`save.corrupt.json`만 지움). `SmokeRunnerTests` +3.
5. §6 7D-IL2CPP에 바깥 제한 180초(`WaitForExit`/`timeout`). 6. `logMessageReceivedThreaded` + Interlocked 카운터·첫 예외 문구(CompareExchange) → 메인 스레드 Update가 `smoke.Fail`. 마지막 줄에 예외 로그 수도.
7. `AshStormFlowTests.StartRequestedEvent_LeavesPending_SecondRequestIgnored`. 8. `RarityStyle.ClassFor` 범위 밖 = null(+ 테스트). 9. CHANGELOG "### 7D 성능·빌드·폴리시". 10. `BuildToolsTests` — 설치 확인을 고정 경로 + `win64*il2cpp` 패턴 + 빌드 대상 지원으로 따로 계산(구현 경로와 위치 일치도 확인),
   link.xml 역방향(모든 항목이 현 런타임 asmdef)·중복·preserve 검사, 에디터 전용 = asmdef `includePlatforms`만, 이름 = asmdef `name`. 11. 유지(CHANGELOG에 기록).
- §10 목록을 7A~7D 결과로 확정(23항목).

**결과**
- EditMode 1796/1796(종료 0, 14초). 첫 실행은 `M7AiDataAssetTests` 2개 실패(새 필드가 에셋에 아직 없음 — 그 테스트의 재실행 검사가 메뉴를 불러 에셋에 기록) → 기록 뒤 전부 통과.
- `M7SceneSetup.ApplyAll` 2회(종료 0·0): Assets(Tests 제외)·ProjectSettings·Packages 948파일 md5가 1회 전·1회 뒤·2회 뒤 모두 같음(필드는 위 테스트가 이미 기록) = 멱등.
- PlayMode 전체 181/181 × 2회 연속(776초·767초, 종료 0, `[DevDiagnostics]` 0줄). (`TargetRaiseM` 0 시험 때 전체 2회 = 180/181(위 전제 실패)·181/181.)
- `M7PerformanceTests` 최악 상황(전체 2회): AI 평균 0.160·0.160ms / 99% 0.294·0.307ms / 최대 0.418·0.555ms, 투사체 평균 0.047·0.048ms(최대 0.118·0.115), 프레임 중앙값 1.86·1.84ms·99% 2.65·2.69ms(렌더 제외),
  Audio+폭풍+방사능 0.0043·0.0046ms, GC 할당 프레임 6경로 전부 0(2590·2573프레임), 재발행 GC 0, 교전 평균 33.8·33.9명, 생존 최소 34, 적 ShotFired 505·514(LMG 60·60, DMR 8·7), 샷건 11·11, 근접 74·74,
  동시 탄 최대 31 / 상한 624, 변이체 최근접 1.07·1.08m, 사수 최근접 4.03·4.03m. 필터 3회(0.8m): AI 평균 0.171~0.178ms, 사수 최근접 2.20~2.61m.
- Mono `BuildWindows64` 종료 0: Succeeded·오류 0·경고 0, 110,253,558 bytes. `BuildWindows64Development` 종료 0: Succeeded·오류 0·경고 0, 175,093,213 bytes.
- 개발 빌드 스모크(바깥 `timeout 180`): ① `-ashfallSmoke <scratchpad>\smoke7d4`(미리 쓰레기 `save.json`·`.bak`·`other.txt`) → **종료 0**, "남은 세이브 파일 2개 지움", 8/8 통과 14.0초, 오류·예외 로그 0, 새 `save.json` version 2·raidInProgress false, `other.txt` 그대로.
  ② 값 없는 `-ashfallSmoke`(마지막 인자) → **종료 1**("뒤에 세이브 폴더가 없다"). ③ `-ashfallSmoke <LocalLow\DefaultCompany\Ashfall>`(= persistentDataPath) → **종료 1**, 실제 `save.json`·`.bak` md5·시각(10-22 15:12) 그대로.
- 테스트·적용·빌드 전후 Assets(Tests 제외)·ProjectSettings·Packages md5 동일(AiConfig.asset 한 줄은 의도한 변경), git status 차이 = 이번 새 파일뿐. ProjectSettings 무변경(백엔드 Mono). Unity·빌드·게임 프로세스 남지 않음(Hub 보조 `unity.exe` 하나).
- IL2CPP: 그대로 사용자 설치 대기(이번에 다시 실행하지 않음).

## 7D-IL2CPP 구현 메모 (gameplay-programmer, 2026-10-23)

원본에서 작업(커밋하지 않음). 새 파일·`.meta` 없음. CLAUDE.md는 고치지 않았다(오케스트레이터 몫). 자세한 기록은 CHANGELOG "7D 성능·빌드·폴리시"의 "7D-IL2CPP 전환".

- **툴체인:** Hub IL2CPP 모듈(`win64_player_{development,nondevelopment}_il2cpp`) + **Visual Studio Community 2026**(18, MSVC 14.51, Windows SDK 10.0.26100). Unity 6000.3.26f1이 VS 2026을 그대로 쓴다(빌드 로그 `C_Win_x64_VS2026_VB_MT`). VS 2022·MSVC v143 따로 필요 없음.
- **임시 전환 빌드(전환 전, §6 절차):** 전 `git status --porcelain ProjectSettings` 깨끗·md5 23파일 기록. `BuildWindows64Il2CppDevelopment` 종료 0(21분 51초, 오류·경고 0) → ProjectSettings.asset에 `scriptingBackend: Standalone: 0`·`il2cppStacktraceInformation: Standalone: 0`이 남음(예상대로) → HEAD 내용으로 되돌려 md5 동일.
  스모크(`timeout 180`) 종료 0·8/8·13.8초·오류·예외 0, 세이브 version 2·raidInProgress false, 실제 `save.json`·`.bak` md5 그대로. 값 없는 `-ashfallSmoke` → 종료 1. `BuildWindows64Il2Cpp` 종료 0(17분 8초, 경고 0) → 같은 임시 키 → 되돌림.
- **영구 전환:** `BuildTools.SetStandaloneBackendIl2Cpp`(메뉴 `Ashfall/Build/Set Standalone Backend IL2CPP`) — 모듈 확인 → 백엔드 IL2CPP + 스택 추적 MethodOnly 명시 → `AssetDatabase.SaveAssets`. diff = 두 키뿐(`Standalone: 1`, `il2cppStacktraceInformation: Standalone: 0`).
  `BuildWindows64`·`Development` = IL2CPP(`BuildWithBackend`: 이미 같으면 백엔드를 건드리지 않음, IL2CPP 개발 빌드만 스택 추적 파일·줄을 임시로), `BuildWindows64Il2Cpp*` 유지(되돌림 무동작), Mono 대체 `BuildWindows64Mono(·Development)`.
- **발견·수정:** 영구 전환 뒤 첫 `BuildWindows64`가 1.6초 만에 실패 — `BuildFailedException: Build path contains a project previously built with the Mono2x scripting backend, the current setting is for IL2CPP`(이전 Mono 출력 `Builds/Windows64/`).
  → `Build`가 출력 폴더(Builds/ 아래)에 다른 백엔드의 이전 빌드(`MonoBleedingEdge` / `GameAssembly.dll`)가 있으면 먼저 지운다(순수 `IsOtherBackendOutput` + 테스트). 실패한 빌드가 남긴 Input System `preloadedAssets` 항목(InputSystem_Actions)과 Performance Testing `Assets/Resources/`(+ `.meta`)는 되돌리고 전환 메서드를 다시 실행.
- **결과(영구 전환 뒤):** `BuildWindows64` 종료 0(20초, Bee 캐시, 경고 0, 배포 약 122MB / `totalSize` 1,340,621,650 bytes는 BackUpThisFolder 포함) → 창 모드 15초 실행 Boot → MainMenu, 예외 없음.
  `BuildWindows64Development` 종료 0(17분 12초, 경고 0, 배포 약 251MB) → 스모크 종료 0·8/8·13.8초·예외 0, 스택 추적에 파일:줄. 값 없는 인자 → 종료 1. `BuildWindows64Mono` 종료 0(110,253,574 bytes), 백엔드 IL2CPP로 되돌림. 모든 빌드 뒤 ProjectSettings md5 = 전환 직후와 동일.
- **link.xml·제품 코드 변경 없음.** 바뀐 파일: `EditorTools/BuildTools.cs`, `Tests/EditMode/BuildToolsTests.cs`(`ProjectBackend_StaysMono_UntilIl2CppStep` → `ProjectBackend_IsIl2Cpp`, `OtherBackendOutput_Detection` 추가, 출력 경로 6개), `ProjectSettings/ProjectSettings.asset`(두 키).
- **테스트:** EditMode 1797/1797(종료 0). PlayMode 전체 181/181 × 2회 연속(811초·811초, 종료 0).
