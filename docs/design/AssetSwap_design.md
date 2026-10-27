# 에셋 교체 설계 — 자리표시 블록아웃 → 에셋스토어 무료 모델 (2026-10-26)

사용자 요청 "에셋스토어 무료 3D 모델로 갈아끼우기". GDD §10(아트 디렉션·팔레트), M6·M7 설계 §2.3(실제 에셋 교체 위치).
**게임 로직·수치·콜라이더·엄폐·NavMesh는 바꾸지 않는다.** 보이는 메시만 바꾼다.

## 1. 결정 (사용자, 2026-10-26)
- 패키지(무료, 표준 Unity Asset Store EULA)는 사용자가 에디터 Package Manager로 가져왔다. 14개 폴더(§2)는 `.gitignore`로 저장소에서 뺀다
  (EULA: 원본 재배포 금지). **릴리스 빌드(zip)에는 포함**(EULA 허용 — 게임 안에 들어간 형태).
- 배정은 §3 표(제안안 그대로 승인). 로우폴리로 통일(이 PC GPU 병목, MVP_final §1).
- 가져올 때 딸려 들어온 `com.unity.postprocessing`(Built-in 데모용)과 `UNITY_POST_PROCESSING_STACK_V2` 정의는 되돌렸다(URP는 자체 Volume).

## 2. 가져온 패키지 (Assets 바로 아래, git 제외)
`Abandoned buildings`, `FREE Low Poly Shipping Container`, `Free - Low Poly Weapon Pack`, `H&L Assets`(방사능 드럼통),
`Low Poly AR Weapon Pack 1`, `Low Poly Pistol Weapon Pack 2`, `Low Poly SMG Weapon Pack 2`, `Low Poly ShotGun Weapon Pack 2`,
`Low Poly Weapon Pack 4_MW_1`, `LowPolyBarriersPackFree`, `Lowpoly_Crates`, `Shady_3d`, `Zombie_Mutant`, `ithappy`(Apocalypse_Free·Military_Free).

## 3. 구조 — "스킨" 덮개
블록아웃은 **그대로 남긴다**(렌더러·팔레트 머티리얼·콜라이더·GroundSurface·엄폐 지점 전부). 그 위에 자식 `Skin`(스킨 프리팹 인스턴스)을 얹는다.

- **스킨 프리팹**(`Assets/_Project/Skins/`, git 제외 — EULA 원본에서 만든 파생물): `Ashfall/Setup/Apply Asset Swap`이 패키지 모델로 만든다.
  원점 = 밑면 중심, 앞 = +z(배정표의 회전 보정), 콜라이더·스크립트 제거(렌더러·Animator만), 그림자 켬, 머티리얼 = §5 URP Lit 사본.
- **숨기기 = 런타임 컴포넌트 `BlockoutSkin`**(Core, `[ExecuteAlways]`): `Skin` 루트에 붙고, 직렬화된 블록아웃 렌더러 목록을
  `OnEnable`에서 `Renderer.forceRenderingOff = true`, `OnDisable`에서 false. forceRenderingOff는 직렬화되지 않으므로 씬·프리팹 파일의
  렌더러는 켜진 그대로다.
- **패키지가 없는 clone**(공개 저장소): 씬·프리팹이 가리키는 스킨 프리팹이 없으면 인스턴스가 "Missing Prefab" 자리(Transform만)가 된다.
  `BlockoutSkin`은 인스턴스에 **추가한** 컴포넌트라 이 자리에 남는다(10/27 clone 확인 — 처음 전제 "같이 사라진다"는 틀림, 블록아웃 286개가 숨겨졌다).
  그래서 `BlockoutSkin`은 자기 아래 렌더러(스킨 모델)가 하나도 없으면 숨기지 않는다 → 블록아웃이 그대로 보인다.
  다만 씬을 열 때 "Problem detected while opening the Scene file"(Missing Prefab) 오류 로그가 나서 레이드 씬을 여는 PlayMode 테스트가 실패한다(10/27 확인 14개).
  → **clone 절차: `Apply Asset Swap`을 한 번 실행**(원본이 없으면 깨진 `Skin` 725개를 지움 — 씬·VM 프리팹이 스킨 적용 전 커밋과 같아진다) → PlayMode 통과 확인(15 통과·3 건너뜀). README에 적었다.
- 스킨에는 콜라이더가 없다 → NavMesh(PhysicsColliders 굽기)·사격 판정·발소리(GroundSurface)는 블록아웃 그대로. NavMesh 다시 굽지 않는다.

## 4. 배정 (스킨 적용 위치)
| 대상 | 스킨 원본 | 맞추는 법 |
|---|---|---|
| `VM_WPN-PST-01` | Pistol Pack 2 `Pistol_F` | 총구 끝 = `Muzzle`, 길이 = 자리표시 길이 |
| `VM_WPN-SMG-01` | SMG Pack 2 `SMG_F` | 〃 |
| `VM_WPN-RIF-01` | AR Pack 1 `AR_A_1` | 〃 |
| `VM_WPN-SHT-01` | ShotGun Pack 2 `ShotGun_F` | 〃 |
| `VM_WPN-DMR-01` | MW 1 `Recon_P` | 〃 |
| 적 SCV·RDR·호위(BGD) 몸 | ithappy `Adult_Survivor`(SCV)·`Adult_Woodcutter`(RDR·BGD) | `Body`(캡슐) 자식, 키 = 1.8m, 발 = 캡슐 바닥, Idle 반복 |
| 적 SNP·BOS 몸 | Shady_3d 방독면+후드(SNP)·헬멧(BOS) | 〃 |
| 적 MUT 몸 | `Zombie_Mutant` | 〃 |
| 적 총 `Muzzle/Gun` | SNP = `Recon_P`, 그 밖 = `AR_A_1` | 총구 끝 = `Muzzle` |
| `Vehicle_*`·`VehiclePile_*`의 `Car`·`Car_Upper` | ithappy `Apocalypse_Car_01` / Military `Hummer_003` 번갈아 | 블록 발자국 긴 변에 균등 비율 |
| `House_*` | `house_aband`(+ `house_roof`) / `house_ruined` 번갈아 | 블록 상자를 축마다 채움(콜라이더 = 보이는 모양), 밑면 = 지면 |
| `Container_*` | Shipping Container | 블록 상자를 축마다 채움 |
| `LowWall_*`(두께 ≤ 1m) | Barriers `ConcreteBarrier_08_v2` 반복 | 길이 방향 타일, 높이·두께 채움 |
| `HighWall_*`(두께 ≤ 1m) | Barriers `ConcreteBarrier_39_v2` 반복 | 〃 |
| 상자(`LootContainer`) | `Crate_01`·`02`·`05` 번갈아(열린 면 위로 세움) | 1m 정육면체 안에 균등 비율 |
| `M7_Toxic_` 드럼통(가장자리·더미, 누운 것 포함) | H&L `barrel_toxic_01`/`02` | 드럼통 축(원통 y) 방향 그대로, 지름·높이 맞춤 |
| `LM_CrashedPlane_*` | Military `Helicopter_001` | 발자국 균등 비율(실제 크기 1.2배까지), 6°·18° 기울이고 0.6m 박음 (잔불 발광은 남김) |

**유지(맞는 무료 모델 없음):** 잔해 더미, 정유소 원통 탱크, 고가 상판·기둥·경사판, 송전 기둥, 철로·띠, 두꺼운 벽(둑 4m 등), 랜드마크 나머지
(급수탑·송전탑·광고판·고가·플레어 스택·라디오 마스트), 광원 소품, 경고 기둥·판, 전원 스위치. Military `Tower_003`은 크기·용도가 맞는 대상이 없어 쓰지 않는다.

## 5. 머티리얼 (§10.1 팔레트 톤)
- 원본 머티리얼마다 사본 `Skins/Materials/{원본 이름}_{토큰}_{원본 GUID 앞 8자}{파일 안 id 4자리}.mat`(다른 패키지의 같은 이름 머티리얼이 서로 덮어쓰지 않게 — 리뷰 지적).
  - URP Lit·URP용 패키지 셰이더(`Universal Render Pipeline/*`, `Ithappy/*`)는 복사 + `_BaseColor` 틴트.
  - 그 밖(Built-in Standard, URP 대상이 없는 셰이더 그래프 — 분홍색)은 URP Lit으로 변환: `_BaseMap` = `_MainTex`/`_BaseMap` 또는 파일 이름에 basecolor·albedo 등이 든 텍스처,
    법선 맵 같은 방식, Built-in Fade/Transparent(renderQueue ≥ 3000 또는 `_Mode` ≥ 2)는 알파 블렌드 유지.
  - 틴트: 텍스처가 있으면 원본 색 × Lerp(흰색, 토큰, 0.35), 단색이면 Lerp(원본, 토큰, 0.7). Smoothness ≤ 0.2(없으면 0.1).
    토큰: 무기·구조물·차량 = Rust_Red, 사람 = Ash_Grey, 변이체·보스 = Dried_Blood, 방사능 = Toxic.
  - 이번 실행에 쓰지 않은 사본·빠진 원본의 스킨 프리팹은 지운다.
- TODO(GDD-?): 틴트 세기 0.35·0.7, 광택, 토큰 배정은 "MVP 기본값(플레이 검증 전)".

## 6. 메뉴·파일
- `EditorTools/AssetSwapCatalog.cs` — §4 표(레벨 데이터) + 순수 맞춤 함수(EditMode 테스트).
- `EditorTools/AssetSwapSkins.cs` — 스킨 프리팹·머티리얼·Idle 컨트롤러 생성(`Skins/`, 매번 다시 씀 — 파생물, GUID 유지). 사람 Idle = ithappy `AC_Adult_Idle_Relaxed`(움직임 적은 숨쉬기 —
  사망해 누운 몸이 두리번거리지 않게). 휴머노이드 아바타가 없으면 경고.
- `EditorTools/AssetSwapApply.cs` — 레이드 씬·VM 프리팹 5개에 `Skin` 자식 + `BlockoutSkin`(있으면 위치만 다시 맞춤). 스킨 판별 = 이름 "Skin…" + (`BlockoutSkin` 또는 Skins 폴더 프리팹 인스턴스
  또는 Missing Prefab) — 이름만 보지 않는다. 여러 스킨이 한 블록아웃을 덮으면(벽 타일·차량 더미) 숨김 목록은 첫 `Skin`만 갖는다(하나만 꺼져도 겹쳐 보이지 않게).
  스킨 크기는 미리보기 씬에서 잰다(열린 씬을 더럽히지 않음).
- `EditorTools/AssetSwapSetup.cs` — `Ashfall/Setup/Apply Asset Swap`(배치 `AssetSwapSetup.ApplyAll`, 멱등): 저장 안 한 씬 확인 → 스킨 생성 → 레이드 씬 적용·검사(통과해야 저장)
  → VM 프리팹 적용(프리팹마다 검사 후 저장). `Ashfall/Setup/Asset Swap/Remove Skins`(모든 `Skin` 제거 = MVP 모습).
  패키지가 하나도 없으면 스킨 폴더를 만들지 않고, 깨진(Missing) 스킨만 지운다. 원본이 일부만 있으면 있는 것만 적용하고 빠진 것은 경고·그 스킨 제거.
- `Core/Visual/BlockoutSkin.cs` — §3 런타임 컴포넌트(유일한 런타임 추가, 게임 상태 없음).
- 기존 코드 수정(스킨 무시): `M7HazardSceneApply.IsIntact`(스킨 자식은 표시물 자식으로 안 셈)·`Validate`(스킨 하위 렌더러 건너뜀),
  `M6VisualSceneApply.PaintGroup`(스킨은 "프리팹 모델"로 안 침, 스킨 하위 렌더러는 칠하지 않음), 테스트 `RaidVisualSmokeTests`(팔레트 검사에서 스킨 하위 제외 +
  스킨이 숨기지 않은 블록아웃은 `forceRenderingOff`가 꺼져 있어야 함 — 패키지 없는 clone 검사).
- M4·M5·M7·맵 개선 메뉴는 그대로(스킨은 별도 메뉴, 씬을 다시 만들면 스킨도 다시 적용).
- 운영: 스킨이 얹힌 레이드 씬·VM 프리팹은 **패키지가 있는 PC에서만 커밋**한다(다른 PC에서 메뉴를 돌리면 스킨 프리팹 GUID가 달라져 씬이 바뀐다).

## 7. 확인
- EditMode: 맞춤 함수(길이·바닥·총구 정렬), 배정표 경로 형식, `BlockoutSkin`이 forceRenderingOff를 켜고 끈다.
- PlayMode(패키지가 있을 때만, 없으면 Ignore): 레이드 씬 스킨 수·콜라이더 0·`BlockoutSkin` 대상 렌더러 숨김·휴머노이드 아바타, 뷰모델 스킨·총구 정렬, 든 무기 스킨.
- 기존 EditMode·PlayMode 전부 그대로 통과, IL2CPP 릴리스 빌드에 스킨 포함.
- 성능: 에디터 1080p 렌더 비교(10시점) + IL2CPP 개발 빌드 A/B(스킨 있음 / `Remove Skins`).

### 7.1 사람 확인 (에디터·플레이)
- 무기 5종: 손 위치·크기(자리표시 길이에 맞춰 실제보다 약간 작다), 총구 섬광 위치.
- 적: 모습·Idle 자세(걷기·사격 동작 없음 — 미끄러지듯 이동). 웅크리면 모델이 세로로 찌그러진다(`Body` y 스케일을 그대로 따름).
  사망하면 몸 스킨은 눕지만 총 스킨은 서 있던 높이에 떠 있다(기존 큐브와 같은 동작).
- 구조물·상자·드럼통·추락 헬기: 어울림·색감(레이드 조명이 붉어 전체가 붉게 보인다), 콜라이더와 보이는 모양이 맞는지(차량은 균등 비율이라 조금 다를 수 있다).

## 8. 남은 일
- 적 걷기·웅크림·사망 애니메이션(ithappy 클립 + 상태 연결 — AI 이벤트에 붙여야 함, 다음 단계). 웅크림 찌그러짐·사망 시 총 스킨도 그때.
- 1인칭 팔·장전 애니메이션, 랜드마크 대부분, 잔해·탱크 모델(맞는 무료 에셋 없음).
- **성능 최적화(우선):** 빌드 1080p에서 스킨이 약 +11~16ms(§9). 후보 — 스킨 그림자 끄기(작은 소품·상자·드럼통), 적 SkinnedMesh 정점 수·LOD, 머티리얼 합치기(GPU Instancing·SRP Batcher 호환 확인), 프레임 디버거로 원인 측정부터.

## 9. 결과 (2026-10-27)
- 구현(§6 파일), `Apply Asset Swap` 적용·저장(스킨 23종 — 무기 5, 적 34(총 26), 구조물 158, 상자 21, 드럼통 30, 랜드마크 1), 연속 실행 멱등(파일 변화 없음).
  미리보기 렌더(무기 방향·총구 정렬, 변이체 셰이더 변환, 상자 세움, 폐가 지붕, 추락 헬기). code-reviewer 지적(높음 2·중간·낮음) 전부 반영.
- 패키지 없는 clone 확인(패키지 14개·`Skins`를 잠시 밖으로 옮겨서):
  - 처음 전제가 틀렸다 — 추가한 `BlockoutSkin`이 Missing Prefab 자리에 남아 블록아웃 286개를 숨겼다 → 스킨 렌더러가 없으면 숨기지 않게 고침(§3), 다시 확인 0개.
  - PlayMode가 첫 테스트 전에 멈췄다(2회, 30분+): `AssetSwapSceneSmokeTests`의 `[UnitySetUp]` 코루틴 안에서 `Assert.Ignore`를 던지면 배치모드 테스트 러너가 멈춘다
    (필터한 테스트 중 이름순 첫 번째) → `[OneTimeSetUp]`으로 옮겨 해결, 이후 멈춤 없음.
  - 레이드 씬 열기 Missing Prefab 오류 로그로 PlayMode 14개 실패 → clone 절차 = `Apply Asset Swap` 한 번(깨진 스킨 725개 지움, 씬·VM이 스킨 전 커밋과 같아짐) → 15 통과·3 건너뜀(README).
    clone 상태 EditMode 1984/1984. 패키지를 되돌린 뒤 씬·VM 6개 파일 md5 원복.
- 테스트(패키지 있음): EditMode 1984/1984, PlayMode 195/195 × 2회. `RaidPerformanceTests…CoverQueryCost` 엄폐 질의 평균 43~46µs(기준 200µs) — 10/26 1회 0.284ms는 일시적 튐으로 본다.
- IL2CPP 빌드: 개발(스킨) 1.79GB·첫 빌드 35분, 개발(`Remove Skins`) 1.63GB, 릴리스 1.51GB·20분 — 모두 성공·오류 0, 릴리스 로그에 `Assets/_Project/Skins/` 34줄.
  개발 빌드 스모크(헤드리스) 종료 코드 0, 오류·예외 로그 0. 빌드 뒤 씬·VM 파일 md5 원복.
- **성능(이 PC HD 620, 1080p 전체 화면, 스모크 90초, `[DevDiagnostics]` 10초 창 중앙값, 첫 창 제외):**
  스킨 있음 A1 약 41.9ms, A2 약 36.9ms / 스킨 없음 B1 약 26.2ms, B2 약 25.9ms → **약 +11~16ms(+40~60%)**. 에디터 렌더 추정(+4~6ms)보다 훨씬 크다.
  GPU 병목인 이 PC에서 정점·그림자·머티리얼 수가 늘어난 영향으로 보인다(원인 분석 전). GTX 1660 판정 전이지만 성능 AC(16.6ms)에 불리 — 최적화가 후속 과제(§8).
  - 측정 중 A2 첫 실행이 시작 30초 뒤 멈췄다: `runInBackground: 0`이라 전체 화면 창이 포커스를 잃으면 플레이어가 일시정지한다. 5분 제한을 걸고 다시 쟀다(위 A2).
- 에디터 1080p 렌더(정적 배칭 없음, 10시점 중앙값): 스킨 없음 36~39ms → 전부 41~45ms, 적만 / 구조물만 각각 +2~3ms.
