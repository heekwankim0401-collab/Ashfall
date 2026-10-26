# Ashfall

1인칭 싱글플레이 하드코어 익스트랙션 슈터. Unity 6 (6000.3.26f1), URP(3D), 새 Input System, UI Toolkit.
**구현 기준은 [docs/GDD.md](docs/GDD.md)다.** 작업 전 §0 작업 규칙, §13 기술 스펙, 현재 마일스톤(§15)을 읽는다.

## 현재 진행
- **MVP(M0~M7 + 마무리 F1~F4) 구현 완료, 사람 플레이 확인 미실시**(2026-10-24 사용자 결정으로 건너뜀). 자동 테스트(EditMode·PlayMode)·IL2CPP 릴리스/개발 빌드·스모크 통과.
  최종 정리 문서 `docs/design/MVP_final.md`(성능 실측·임시값 확정·남은 일). GDD §15는 기능 AC 체크, 성능 AC 2개는 GTX 1660 실측 전이라 비워 둠.
  M7 = 7A 무기·7B AI·7C 생존·제작·7D 성능·빌드, MVP 마무리 = F1 전신 사인·F2 보관함 업그레이드·F3 내구도 대략 표시·수리·가격·F4 방어구 관통·둔탁(플레이어·적).
  IL2CPP: ProjectSettings Standalone 백엔드 = IL2CPP(`BuildToolsTests`가 고정), C++ 툴체인 = Visual Studio 2026(2022도 가능) "C++를 사용한 데스크톱 개발".
  데이터: `Ashfall/Setup/Apply MVP Finish Data`(배치 `MvpFinishDataSetup.ApplyAll`, 멱등, 파일에 없는 필드만, `Apply M7 Content` 끝에서도 호출).
- 성능: 이 PC(Intel HD 620) 1080p 전체 화면 폭풍 Peak 중앙값 약 32.6ms(31fps, 미충족)·720p 13.4ms — GPU 병목, 데이터는 안 바꿈. GTX 1660 판정은 그 GPU에서
  `Ashfall.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 1 -ashfallSmoke <임시 폴더> -ashfallSmokeRaidSec 90 -logFile perf.log`의 `[DevDiagnostics]` 줄(중앙값 ≤ 16.6ms)로.
- 남은 일(`docs/design/MVP_final.md` §4): 사람 플레이 확인(`M6_design.md`·`M7_design.md`·`MVP_finish_design.md` 각 §7.1), 실제 에셋 교체(`M6_design.md`·`M7_design.md` 각 §2.3),
  수치 조정(임시값 = "MVP 기본값(플레이 검증 전)", 목록 `M7_design.md` §10 + `MVP_finish_design.md` §11), 원격 백업(원격 없음).
- 마일스톤 설계·리뷰 노트와 진행 방식은 `docs/design/`(README 참고). 새 마일스톤도 같은 방식으로 설계 문서를 여기에 남긴다.
  한 번에 한 마일스톤만 작업하고 다음 마일스톤 기능을 미리 만들지 않는다.
- 확인 씬(빌드 설정에는 없음): `Assets/_Project/Scenes/Test/M1_Greybox.unity`(`Ashfall/Setup/Recreate M1 Greybox Scene`),
  `Assets/_Project/Scenes/Test/M2_HealthInventory.unity`(`Ashfall/Setup/Create M2 Data` → `Create M2 Test Scene`),
  `Assets/_Project/Scenes/Test/M3_AI.unity`·`M3_AIPerf.unity`(`Ashfall/Setup/Create M3 Test Scenes`, 데이터·NavMesh 포함).
- 게임 씬(빌드 순서): `Boot` → `MainMenu` → `Hideout` → `Raid_CrimsonBasin`. `Ashfall/Setup/Create M4 Raid Scenes`가 MainMenu·레이드 씬·Terrain·NavMesh를,
  `Ashfall/Setup/Create M5 Hideout Scene`이 M5 데이터(가격·상인·키트·ItemDatabase)·Hideout 씬을 만들고, 둘 다 빌드 설정을 맞춘다.
- M6 아트·오디오: `Ashfall/Setup/Apply M6 Art & Audio`가 팔레트·대기·폭풍·오디오 데이터와 자리표시 에셋(하늘 PNG·팔레트 머티리얼·볼륨 프로필·합성 WAV `Assets/_Project/Audio/Placeholder/`)을
  만들고 Raid·Hideout·MainMenu 씬에 적용한다(재실행해도 같음, M4/M5 씬 메뉴도 저장 전에 부른다). WAV는 없을 때만 쓰고 SO 클립은 빈 필드만 채우므로 같은 경로에 실제 에셋을 덮어쓰면 유지된다.
- M7 콘텐츠: `Ashfall/Setup/Apply M7 Content`(배치 `M7SceneSetup.ApplyAll`, 멱등, M6이 없으면 먼저 적용)가 무기·AI·생존 데이터(`Create M7 Weapon/AI/Survival Data`),
  무기 뷰모델 프리팹(`Prefabs/Weapons/VM_{id}`), 자리표시 WAV, 레이드 씬 적 14명(`Enemy_M7_*`, 합계 34)·방사능 구역(`RadiationField`·`M7_ToxicMarkers`), 은신처 제작대 연결을 만든다.
  M4 레이드·M5 은신처 씬 메뉴도 저장 전에 M7 적용을 부른다.
- 맵 개선(2026-10-26, 사용자 피드백 "어둡다·허허벌판", `docs/design/MapPolish_design.md`): `Ashfall/Setup/Apply Map Polish`(배치 `MapPolishSetup.ApplyAll`, 멱등)가
  밝기 개정(`AtmosphereConfigSO.lightingRevision` 1 미만일 때만)·발광 머티리얼·랜드마크/광원 소품 프리팹(`Prefabs/Landmarks/LM_*`·`Prefabs/Props/LP_*`, 없을 때만)을 만들고,
  레이드 씬 Environment 아래 `MapPolish_Landmarks`·`MapPolish_Structures`·`MapPolish_Lights`를 없을 때만 배치 → NavMesh 다시 굽기 → 경로·엄폐·적·상자 확인(실패 = 예외).
  배치 데이터 `MapPolishLayout`, 점광원 14개(그림자 없음, 상한 `maxMapLights` 16). M4 레이드 씬 메뉴도 저장 전에 부른다. 프리팹을 모델로 바꾸면 `Ashfall/Setup/Map Polish/Rebuild Map Polish (Raid Scene)`.
  이 PC는 Windows 스마트 앱 컨트롤이 켜져 있다 — 2026-10-24에 만든 `Builds/Windows64Dev` exe는 실행이 막혔고 새로 만든 빌드는 실행됐다(막히면 다시 빌드). 보안 설정은 사용자 결정.
  맵 개선 성능(같은 빌드 쌍, 1080p Peak): 약 +1.4ms(+5%).
- 세이브: `Application.persistentDataPath/save.json`(version 3 — 보관함 단계 `stashLevel`·방어구 내구도, v2 = 무기 내구도·튜브, v1·v2는 읽을 때 자동 이전, 에디터 메뉴 `Ashfall/Save/Open Save Folder`·`Delete Save Files`). 테스트는 실제 경로를 쓰지 않는다
  (EditMode는 `MemorySaveStorage`, PlayMode는 `SaveSandbox` SetUpFixture — PlayMode 테스트는 `Ashfall.Tests.PlayMode` 네임스페이스에 둔다).
- 구현 중 정한 해석·임시값은 `docs/CHANGELOG_GDD.md`의 "M1 구현 중 결정", "M2/M3/M4/M5/M6/M7 설계 결정", "MVP 마무리 설계 결정"에 있다. GDD에 없는 수치는 `TODO(GDD-?)`.
  기획 확정이 필요한 MVP 남은 임시값 목록은 `docs/design/M7_design.md` §10(+ `MVP_finish_design.md` §11).
- Windows 빌드(IL2CPP): `-batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.BuildTools.BuildWindows64` → `Builds/Windows64/`(릴리스).
  `BuildWindows64Development` → `Builds/Windows64Dev/`(IL2CPP 개발). 첫 IL2CPP 빌드는 약 20분, 이후 캐시. Mono 대체 경로 `BuildWindows64Mono`(·`Development`) → `Builds/Windows64Mono*/`(그 빌드 동안만 Mono).
  출력 폴더에 다른 백엔드의 이전 빌드가 있으면 빌드 전에 지운다. 백엔드 전환 메뉴 `Ashfall/Build/Set Standalone Backend IL2CPP`.
  스모크: `Builds/Windows64Dev/Ashfall.exe -batchmode -nographics -ashfallSmoke <임시 폴더> -logFile smoke.log` → 성공 0·실패 1(개발 빌드만, 실제 세이브 폴더 금지).

## 구조 (GDD §13.2)
- `Assets/_Project/Scripts/<시스템>/` — 폴더마다 asmdef `Ashfall.<폴더명>`, 네임스페이스도 같다.
  - `Core` — 공용(씬 이름, BootLoader, 팔레트 `PaletteSO`(§10.1)·보폭 리듬 `StrideCadence`(`Movement/`), 아이템·무기·탄약·탄창 SO와 enum(`Data/`), `IDamageable`·`DamageInfo`·`IRecoilReceiver`(`Combat/`),
    유일한 싱글톤 `GameManager`(자동 생성)가 가진 `EventBus`(`Events/`)·`ServiceRegistry`와 기능 간 인터페이스(`Services/`),
    씬 흐름·레이드 결과·세계 정지(timeScale의 유일한 주체) `GameFlow`(`Flow/`)와 `RaidResult`(`Raid/`),
    런타임 아이템 `ItemInstance`·`Magazine`·`WeaponItem`·`MedicalItem`·`LootRoller`·`InventorySnapshot`, 그리드 `ItemGrid`, 반입 `Loadout`(`Items/`),
    플레이어·적이 같이 쓰는 무기 로직 `WeaponModel`·`FireControl`·`LocalMagazineSource`·`DamageRules`, 방어구 판정 `ArmorRules`(§5.3, 난수 주입)·사인 종류 `DeathCauseKind`(`Combat/`),
    MVP 마무리: 방어구 런타임 `ArmorItem`·내구도 공통 `IDurableItem`·`DurabilityRules`(`Items/`), 보관함 단계 `PlayerProfile.StashLevel`·`ItemGrid.TryGrow`, `IHideoutService` 보관함 확장·수리,
    세이브 `ProfileService`(GameManager 소유, 보관함·은신처 장비·탄피·키트·raidInProgress)·`SaveSerializer`·`FileSaveStorage`(`Save/`), id → 정의 `ItemDatabaseSO`).
    M7: 고장·내구도 `JamRules`·`WeaponWear`(`Combat/`), 낱탄 `ILooseAmmoSource`·방사능 구역 `IRadiationField`·생존 등급 `IBodyStatusView.GetSurvivalTier`·제작 `IHideoutService.Recipes/TryCraft`(`Services/`),
    개발 빌드 진단 `DevDiagnostics`·`FrameTimeStats`·`SmokeRunner`·`SmokeScenario`(`Diagnostics/`, GameManager 오브젝트의 컴포넌트, 개발 빌드에만). 다른 Ashfall 어셈블리를 참조하지 않는다.
    여러 기능이 같이 쓰는 타입은 여기 둔다. 기능 간 조회·명령은 레지스트리의 Core 인터페이스로, 변경 알림은 이벤트로.
  - `Player` — `PlayerConfigSO`, 순수 로직 `LocomotionModel`·`StaminaModel`·`BodyHealthModel`(부위 체력·상태이상·전신 피해·최대 HP 배율)·`MedicalUseModel`·`SurvivalModel`(수분·에너지)·`RadiationDoseModel`, 얇은 `PlayerMotor`·`PlayerHealth`.
  - `Weapons` — 순수 로직 `Ballistics`(펠릿)·`MagazineCheckModel`(RULE-04)·`AimSway`·`WeaponSwitchModel`(1/2/3)·`JamClearModel`(T 길게), `ProjectileSystem`(`IProjectileService`), `WeaponController`, `WeaponHandlingConfigSO`(탄약 표), 그레이박스 `HitTarget`·`TestTurret`.
  - `AI` — 순수 로직 `EnemyBrain`(상태 머신·전술 팩토리·거점)·`Squad`·전술(`ScavengerTactics`/`RaiderTactics`/`SniperTactics`/`MutantTactics`)·`EnemyMelee`·감각·`CoverSelector`·`EnemyBodyModel`·적 방어구 `EnemyArmorModel`·`CorpseLootBuilder`,
    얇은 `EnemyAgent`(NavMeshAgent)·`AiDirector`(틱 분산, 정적 인스턴스 없음)·`SniperGlint`·`CoverPoint`·`PatrolRoute`. 적 루트는 씬 최상위에 둔다.
  - `Inventory` — 순수 로직 `PlayerInventory`(`IInventoryService`·`IMagazineSource` 구현)·`LootContainerModel`, 얇은 `PlayerInventoryBehaviour`·`LootContainer`·`WorldItem`,
    은신처용 `HideoutInventoryBehaviour`(보관함을 열린 루트 자리로 연다).
  - `Hideout` — 순수 로직 `HideoutService`(`IHideoutService`: 진입·저장·레이드 시작)·`TraderModel`·`PriceRules`·`VagrantKitModel`·`StashRules`,
    제작대 `CraftingModel`, 보관함 확장 `StashUpgradeModel`, 얇은 `HideoutController`, `HideoutConfigSO`·`TraderConfigSO`·`CraftingConfigSO`.
  - `Raid` — 순수 로직 `RaidSession`(타이머·종료 단일 처리)·`ExtractionModel`·`ExtractionRules`·`ActiveExtractionPicker`·`RaidResultBuilder`(RULE-01 유일 판정),
    얇은 `RaidController`(정적 인스턴스 없음)·`ExtractionZone`·`PowerSwitch`·`PlayerSpawnPoint`·`PointOfInterest`, `RaidConfigSO`.
    재 폭풍(§8.3): 순수 `AshStormModel`·`AshStormSchedule`·`FogMath`·`AshStormVisualMath`, 얇은 `AshStormController`(`IAshStormInfo`)·`AshStormPresenter`(안개·하늘 복제본·재 파티클), `AtmosphereConfigSO`·`AshStormConfigSO`.
    방사능 구역: 순수 `RadiationZoneMath`, 얇은 `RadiationField`(`IRadiationField`), `RaidConfigSO.radiationZones`.
  - `Audio` — 순수 로직 `GunshotMix`(거리 3단계)·`FootstepMix`·`SurfaceResolver`·`VoiceAllocator`·`GeigerClickModel` 등, 얇은 `AudioDirector`(레이드, `FootstepTaken`·`ShotFired` 재생, 정적 인스턴스 없음)·
    `AmbientSoundscape`·`SurfaceProbe`·`HideoutMusic`, `AudioConfigSO`, 지면 재질 `GroundSurface`(흙/금속/잔해/물).
  - `UI` — UI Toolkit(GameUI: HUD·인벤토리·부위 상태·지도·일시정지·종료 오버레이, MainMenu: [은신처]/[종료], HideoutUI: 알림·레이드 결과·허브([장비]/[상인]/[제작]·키트)). 순수 모델·프레젠터(`GameUIPresenter` 등)와 뷰, `GameUIRoot`, 희귀도 테두리 `RarityStyle`. UXML/USS는 `Assets/_Project/Art/UI/`
    (USS 색은 팔레트 토큰만, 예외는 `--rarity-rare-blue` 1개).
  - `Player`, `Weapons`, `Inventory`, `AI`, `Raid`, `Hideout`, `UI`, `Audio` — `Ashfall.Core`만 참조. 기능끼리 직접 참조 금지(이벤트 버스).
  - `EditorTools` — 에디터 전용(씬·에셋 생성 메뉴). `Ashfall/...` 메뉴.
- `Assets/_Project/link.xml` — IL2CPP 스트리핑 대비 런타임 Ashfall 어셈블리 보존(런타임 asmdef를 추가하면 한 줄 추가, `BuildToolsTests`가 검사).
- `Assets/_Project/Data/` — ScriptableObject 에셋. `Scenes/` — `Boot`(빌드 첫 씬) → `MainMenu` → `Hideout` ↔ `Raid_CrimsonBasin`.
- `Assets/Tests/EditMode/`, `Assets/Tests/PlayMode/` — 테스트 asmdef는 모든 Ashfall 어셈블리를 참조한다.
- `Assets/Settings/` — URP 에셋(`PC_RPAsset`이 Windows 기본), 볼륨 프로필. `Assets/InputSystem_Actions.inputactions` — 프로젝트 입력 액션.
- `docs/` — `GDD.md`, `CHANGELOG_GDD.md`. `.claude/agents/` — GDD §16 에이전트 7종.

## 작업 규칙
- 에이전트는 유니티 에디터를 조작할 수 없다. C# 스크립트·asmdef·텍스트 파일을 고치고, 배치모드로 컴파일·테스트한다.
- 수치는 하드코딩하지 않고 ScriptableObject(§14)로 뺀다. 로직은 순수 C# 클래스로 분리해 EditMode 테스트를 붙인다.
- 입력은 새 Input System(`UnityEngine.InputSystem`)만. 옛 `Input.GetKey` 계열 금지.
- 씬·프리팹·`.asset` YAML을 손으로 쓰지 않는다. 필요한 오브젝트·에셋을 만드는 에디터 스크립트(`EditorTools`, 메뉴 항목)를 쓰고 배치모드 `-executeMethod`로 실행한다.
- 새 파일의 `.meta`는 만들지 않는다(유니티가 GUID를 생성). 기존 `.meta`의 GUID는 바꾸지 않는다. 유니티가 만든 `.meta`는 커밋한다.
- 패키지에 기능을 쓸 때 해당 asmdef `references`에 패키지 어셈블리(예: `Unity.InputSystem`, `Unity.Cinemachine`)를 그때 추가한다.
- 문서와 코드가 충돌하면 문서가 우선. 문서를 고칠 땐 `docs/CHANGELOG_GDD.md`에 사유를 먼저 기록한다.
- 코드 식별자는 영어, 주석·문서는 한국어. 커밋 메시지는 한국어로 짧게, GDD ID(예: `SYS-PLR-01`)를 참조한다.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Builds/`는 git에 없다. 건드리지 않는다.
- 작업이 끝나면 사람이 에디터에서 해야 할 일(메뉴 실행, 컴포넌트 연결, 확인 방법)을 짧게 정리해서 알려준다.

## 배치모드 실행
조건: **Unity Hub가 켜져 있어야** 라이선스가 잡힌다(꺼져 있으면 종료 코드 198). **에디터에 이 프로젝트가 열려 있으면 안 된다.**
```
"C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml -logFile test.log
```
- PlayMode는 `-testPlatform PlayMode`. 종료 코드 0 = 통과, 2 = 실패 테스트 있음, 그 밖 = 실행·컴파일 오류(`test.log`에서 `error CS` 확인).
- 에디터 메서드 실행: `-batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.SceneSetup.CreateM0Scenes -logFile setup.log`
- 알려진 무해한 로그: 스크립트 없는 asmdef의 "will not be compiled" 경고, Cinemachine HDRP 샘플 asmref 경고, Addressables의 `DocExampleCode.TestStub` 테스트 1개,
  IL2CPP 빌드의 Cinemachine 생성 코드 C++ 경고 C4717 1건, BuildReport `totalSize`에 `*_BackUpThisFolder_ButDontShipItWithYourGame`(약 1.2GB, 배포 제외)이 포함되는 것.
