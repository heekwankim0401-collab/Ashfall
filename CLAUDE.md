# Ashfall

1인칭 싱글플레이 하드코어 익스트랙션 슈터. Unity 6 (6000.3.26f1), URP(3D), 새 Input System, UI Toolkit.
**구현 기준은 [docs/GDD.md](docs/GDD.md)다.** 작업 전 §0 작업 규칙, §13 기술 스펙, 현재 마일스톤(§15)을 읽는다.

## 현재 진행
- 마일스톤: M0~M5(이동·사격, 체력·인벤토리, 적 AI, 레이드 루프, 은신처 & 세이브) 완료(자동 테스트 + 에디터·빌드 확인). 다음은 M6.
- 마일스톤 설계·리뷰 노트와 진행 방식은 `docs/design/`(README 참고). 새 마일스톤도 같은 방식으로 설계 문서를 여기에 남긴다.
  한 번에 한 마일스톤만 작업하고 다음 마일스톤 기능을 미리 만들지 않는다.
- 확인 씬(빌드 설정에는 없음): `Assets/_Project/Scenes/Test/M1_Greybox.unity`(`Ashfall/Setup/Recreate M1 Greybox Scene`),
  `Assets/_Project/Scenes/Test/M2_HealthInventory.unity`(`Ashfall/Setup/Create M2 Data` → `Create M2 Test Scene`),
  `Assets/_Project/Scenes/Test/M3_AI.unity`·`M3_AIPerf.unity`(`Ashfall/Setup/Create M3 Test Scenes`, 데이터·NavMesh 포함).
- 게임 씬(빌드 순서): `Boot` → `MainMenu` → `Hideout` → `Raid_CrimsonBasin`. `Ashfall/Setup/Create M4 Raid Scenes`가 MainMenu·레이드 씬·Terrain·NavMesh를,
  `Ashfall/Setup/Create M5 Hideout Scene`이 M5 데이터(가격·상인·키트·ItemDatabase)·Hideout 씬을 만들고, 둘 다 빌드 설정을 맞춘다.
- 세이브: `Application.persistentDataPath/save.json`(에디터 메뉴 `Ashfall/Save/Open Save Folder`·`Delete Save Files`). 테스트는 실제 경로를 쓰지 않는다
  (EditMode는 `MemorySaveStorage`, PlayMode는 `SaveSandbox` SetUpFixture — PlayMode 테스트는 `Ashfall.Tests.PlayMode` 네임스페이스에 둔다).
- 구현 중 정한 해석·임시값은 `docs/CHANGELOG_GDD.md`의 "M1 구현 중 결정", "M2/M3/M4/M5 설계 결정"에 있다. GDD에 없는 수치는 `TODO(GDD-?)`.
- Windows 빌드: `-batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.BuildTools.BuildWindows64` → `Builds/Windows64/`.

## 구조 (GDD §13.2)
- `Assets/_Project/Scripts/<시스템>/` — 폴더마다 asmdef `Ashfall.<폴더명>`, 네임스페이스도 같다.
  - `Core` — 공용(씬 이름, BootLoader, 아이템·무기·탄약·탄창 SO와 enum(`Data/`), `IDamageable`·`DamageInfo`·`IRecoilReceiver`(`Combat/`),
    유일한 싱글톤 `GameManager`(자동 생성)가 가진 `EventBus`(`Events/`)·`ServiceRegistry`와 기능 간 인터페이스(`Services/`),
    씬 흐름·레이드 결과·세계 정지(timeScale의 유일한 주체) `GameFlow`(`Flow/`)와 `RaidResult`(`Raid/`),
    런타임 아이템 `ItemInstance`·`Magazine`·`WeaponItem`·`MedicalItem`·`LootRoller`·`InventorySnapshot`, 그리드 `ItemGrid`, 반입 `Loadout`(`Items/`),
    플레이어·적이 같이 쓰는 무기 로직 `WeaponModel`·`FireControl`·`LocalMagazineSource`·`DamageRules`(`Combat/`),
    세이브 `ProfileService`(GameManager 소유, 보관함·은신처 장비·탄피·키트·raidInProgress)·`SaveSerializer`·`FileSaveStorage`(`Save/`), id → 정의 `ItemDatabaseSO`). 다른 Ashfall 어셈블리를 참조하지 않는다.
    여러 기능이 같이 쓰는 타입은 여기 둔다. 기능 간 조회·명령은 레지스트리의 Core 인터페이스로, 변경 알림은 이벤트로.
  - `Player` — `PlayerConfigSO`, 순수 로직 `LocomotionModel`·`StaminaModel`·`BodyHealthModel`(부위 체력·상태이상)·`MedicalUseModel`, 얇은 `PlayerMotor`·`PlayerHealth`.
  - `Weapons` — 순수 로직 `Ballistics`·`MagazineCheckModel`(RULE-04)·`AimSway`, `ProjectileSystem`(`IProjectileService`), `WeaponController`, 그레이박스 `HitTarget`·`TestTurret`.
  - `AI` — 순수 로직 `EnemyBrain`(상태 머신)·`Squad`·전술(`ScavengerTactics`/`RaiderTactics`)·감각·`CoverSelector`·`EnemyBodyModel`,
    얇은 `EnemyAgent`(NavMeshAgent)·`AiDirector`(틱 분산, 정적 인스턴스 없음)·`CoverPoint`·`PatrolRoute`. 적 루트는 씬 최상위에 둔다.
  - `Inventory` — 순수 로직 `PlayerInventory`(`IInventoryService`·`IMagazineSource` 구현)·`LootContainerModel`, 얇은 `PlayerInventoryBehaviour`·`LootContainer`·`WorldItem`,
    은신처용 `HideoutInventoryBehaviour`(보관함을 열린 루트 자리로 연다).
  - `Hideout` — 순수 로직 `HideoutService`(`IHideoutService`: 진입·저장·레이드 시작)·`TraderModel`·`PriceRules`·`VagrantKitModel`·`StashRules`,
    얇은 `HideoutController`, `HideoutConfigSO`·`TraderConfigSO`.
  - `Raid` — 순수 로직 `RaidSession`(타이머·종료 단일 처리)·`ExtractionModel`·`ExtractionRules`·`ActiveExtractionPicker`·`RaidResultBuilder`(RULE-01 유일 판정),
    얇은 `RaidController`(정적 인스턴스 없음)·`ExtractionZone`·`PowerSwitch`·`PlayerSpawnPoint`·`PointOfInterest`, `RaidConfigSO`.
  - `UI` — UI Toolkit(GameUI: HUD·인벤토리·부위 상태·지도·일시정지·종료 오버레이, MainMenu: [은신처]/[종료], HideoutUI: 알림·레이드 결과·허브([장비]/[상인]·키트)). 순수 모델·프레젠터(`GameUIPresenter` 등)와 뷰, `GameUIRoot`. UXML/USS는 `Assets/_Project/Art/UI/`.
  - `Player`, `Weapons`, `Inventory`, `AI`, `Raid`, `Hideout`, `UI`, `Audio` — `Ashfall.Core`만 참조. 기능끼리 직접 참조 금지(이벤트 버스).
  - `EditorTools` — 에디터 전용(씬·에셋 생성 메뉴). `Ashfall/...` 메뉴.
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
- 알려진 무해한 로그: 스크립트 없는 asmdef의 "will not be compiled" 경고, Cinemachine HDRP 샘플 asmref 경고, Addressables의 `DocExampleCode.TestStub` 테스트 1개.
