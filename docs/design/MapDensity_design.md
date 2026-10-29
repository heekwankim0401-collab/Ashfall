# 맵 밀도 설계 — Crimson Basin 건물·죽은 나무·풀 + 루트 지점 약 200곳 (2026-10-29)

사용자 피드백(2026-10-29): 맵이 아직 "허허벌판" — 건물·죽은 나무·풀을 **아주 많이** 늘리고, 루트 지점도 크게 늘린다.
마일스톤이 아니라 MVP 뒤 폴리시 작업 하나다(가장 가까운 선례 `MapPolish_design.md`, 스킨·성능은 `AssetSwap_design.md`).
결정 사유는 구현 때 `docs/CHANGELOG_GDD.md` "맵 밀도 설계 결정"에 기록한다(§12 초안).

**사용자 결정(그대로 반영)**
- 아이템은 **상자와 차량 트렁크에만** 있다(바닥 스폰 없음). 합계 **약 200곳**, 그중 일부만 **레이드마다 무작위로 활성**.
- 산포물: **죽은 나무와 풀**, 그리고 **건물을 많이**(속을 채운 외곽 구조물, 실내 없음 — GDD §1·§17).
- 성능 판정은 사용자가 다른 PC(GDD §13.1 GTX 1660 1080p 60fps)에서 한다. 이 PC(HD 620) 수치는 기록만.
  GPU 인스턴싱·LOD·컬링 거리·작은 산포물 그림자 끔 같은 **기본 최적화는 반드시** 넣는다.

## 1. 범위
- 하는 것: 건물(속 찬 폐가·창고·텐트 등) 대량 추가, 소형 구조물·차량 추가, 죽은 나무·덤불·쓰러진 통나무 산포, 지형 풀(Terrain Detail, GPU 인스턴싱),
  상자 추가와 **차량 트렁크(GDD §6, 지금까지 미구현)**, 레이드 시드에서 파생한 결정적 무작위 활성화, 새 루트 테이블 2개, 경제 시뮬레이션,
  NavMesh 다시 굽기와 MapPolish §6 확인 규칙 재사용, LOD·컬링·그림자 정책, 성능 측정.
- 안 하는 것: 실내·건물 진입(§1·§17), 바닥 아이템 스폰, 풀·덤불에 숨는 은폐 규칙(§7 감각 그대로 — §8.4), 바람에 흔들리는 풀(셰이더 작업),
  나무 재질 탄착음·발소리(§11 그대로), 낮/밤, 새 적·순찰 경로, 레이드 중 상자 상태 저장(RULE-03 그대로), 라이트맵·오클루전 컬링 굽기,
  CoverIndex 공간 분할(§7.3 상한을 넘을 때만 후속), GPU Resident Drawer 기본 적용(§8.2 A/B 실험만).

## 2. 현재 수치 (코드·씬에서 확인)
| 항목 | 값 | 출처 |
|---|---|---|
| 루트 상자 | **21** = 일반 19(POI마다 3~4, `LT-CRATE` 굴림 1~3회) + 신호탄 고정 2(정유소·저수지), 그리드 6×4, seed 0(열 때마다 무작위) | `CrimsonBasinLayout.LootCrates/FlareCrates`, `M4MapSetup.CreateLootCrates` |
| 시체 루트 | 적 34명(M4 20 + M7 14), `LT-SCV/RDR/SNP/MUT/BOS`, 그리드 6×5 — 이번에 안 바꿈 | `M7EnemyLayout`, `InventoryConfigSO.corpseGridSize` |
| 차량 트렁크 | **0** (GDD §6에 있으나 미구현) | — |
| M4 블록아웃 조각 | 181 = POI 94(고속도로 23·정유소 24·마을 20·송전탑 16·저수지 11) + 탈출 27 + 흩어진 잔해 60(seed 4041) | `M4MapSetup.*Pieces` |
| 맵 개선 | 랜드마크 8, 중간 구조물 29(군집 12), 광원 소품 13, 점광원 14/16 | `MapPolishLayout` |
| 씬 구조물 이름 수 | House 11, Vehicle 42(버스 2·보트 2 포함 → 승용차 약 38), VehiclePile 8, Container 7, LowWall 48, HighWall 44, Rubble 16, Tank 6 | `Raid_CrimsonBasin.unity` 이름 집계 |
| 엄폐 지점 | 494(맵 개선 굽기 확인 494/494), 엄폐 질의 평균 42~46µs(기준 200µs, 선형 탐색) | `MapPolish_design.md` §8, `AssetSwap_design.md` §9, `CoverSelector` |
| 지형 나무·디테일 | **0** — `drawTreesAndFoliage = false`, `treeDistance = 0`, `detailObjectDistance = 0`, TerrainData 554KB | `M4MapSetup.CreateTerrain` |
| NavMesh | 1.06MB, 굽기 약 17초 | `NavMesh-Environment.asset` |
| 렌더 설정 | PC 렌더러 Forward+, GPU Resident Drawer 꺼짐, LOD 크로스페이드 켬(디더), 그림자 거리 50m(URP)·캐스케이드 4, Standalone 정적 배칭 켬 | `PC_RPAsset`·`PC_Renderer`·ProjectSettings |
| 성능(HD 620 1080p, 폭풍 Peak 중앙값) | 스킨 있음 28.6~30.1ms(10/28 같은 날 쌍) | `AssetSwap_design.md` §9.1 |
| 상자 기대 가치 | `LT-CRATE` 굴림당 basePrice 약 224 → 상자당 약 448(판매 ×0.4 ≈ 179 탄피), 맵 일반 상자 합 약 8,500 | `LT-CRATE.asset`, `TraderConfig.sellPriceMultiplier` |

## 3. 목표 수치 (TODO(GDD-?) — 전부 임시값, 배치·시뮬레이션 뒤 조정)
구역 정의(순수 함수 `LootZoneRules.ZoneOf(xz)`, 루트와 산포가 같이 쓴다):
**POI**(5곳) = POI 발자국 + 40m(`ScatterMinDistanceFromPoiM`) 안, **외곽** = max(|x|,|z|) ≥ 380m(`EdgeRiseStartM`, 가장자리 경사), **길목** = 나머지.

### 3.1 구조물·산포
| 종류 | 지금 | 추가 | 합계 | 분배 |
|---|---|---|---|---|
| 건물(속 찬 폐가·창고·텐트·초소, House 계열 3~10m) | 11 | **+80** | 91 | 마을 +14, 고속도로 +8(모텔·정비소), 정유소 +6(사무동), 송전탑 +4(변전 건물), 저수지 +6(펌프장·창고), 길목 새 "작은 마을" 6곳 × 4~6 = 30, 외곽 외딴 농가 12 |
| 소형 구조물(벽·바리케이드·모래주머니·잔해·컨테이너·전봇대) | 약 120 | **+150** | 약 270 | POI 50, 길목 70(새 마을·군집 주변), 외곽 30 |
| 차량(승용차·트럭·버스·전차 잔해) | 50 | **+40** | 90 | 승용차 30(트렁크), 큰 차량 10(트렁크 없음) — 도로 띠 옆·마을·주차장 군집 |
| 죽은 나무(키 5~12m, 줄기 콜라이더) | 0 | **1,500** | 1,500 | 길목 900(죽은 숲 6~8곳 + 흩어짐), 외곽 500(경사면 실루엣), POI 100(마을·저수지 가장자리만) |
| 덤불(≤ 1.0m, 콜라이더 없음) | 0 | 400 | 400 | 나무 군락 가장자리·건물 주변 |
| 쓰러진 통나무·그루터기(콜라이더) | 0 | 120 | 120 | 죽은 숲 안 |
| 풀(Terrain Detail, 2층) | 0 | 걷는 지형의 약 40% | — | 최대 1.2포기/m², 보이는 거리 50m, 키 0.3~0.8m. 도로·고속도로·정유소 패드·건물 발자국+1m·상자 접근 1.5m 제외 |

### 3.2 루트 지점 (합 200, 레이드당 활성은 §5.3)
| 구역 | 상자(기존 포함) | 트렁크 | 합 | 상자 테이블 | 활성 비율(초안) |
|---|---|---|---|---|---|
| POI-HWY 고속도로 | 14 (기존 4) | 14 | 28 | `LT-CRATE` | 0.40 |
| POI-REF 정유소 | 16 (기존 4 + 신호탄 1) | 4 | 20 | `LT-CRATE` | 0.40 |
| POI-VIL 마을 | 16 (기존 4) | 6 | 22 | `LT-CRATE` | 0.40 |
| POI-PYL 송전탑 | 12 (기존 3) | 3 | 15 | `LT-CRATE` | 0.40 |
| POI-RES 저수지 | 13 (기존 4 + 신호탄 1) | 4 | 17 | `LT-CRATE` | 0.40 |
| 길목(ROUTE) | 36 | 22 | 58 | `LT-CRATE-LOW` | 0.30 |
| 외곽(OUTSKIRT) | 26 | 14 | 40 | `LT-CRATE-LOW` | 0.30 |
| **합** | **133** (기존 21 + 새 112) | **67** | **200** | 트렁크는 모두 `LT-TRUNK` | 약 35% → 레이드당 약 70곳 |

- 신호탄 상자 2개는 **항상 활성**(EXT-03 조건, M4 Q7) — 비율 계산에서 뺀다.
- 트렁크 규칙: **승용차(Vehicle, 길이 ≤ 5m)마다 트렁크 1개**(기존 약 38 + 새 30 ≈ 67). 버스·보트·차량 무더기·전차는 없음 — "승용차는 다 트렁크가 있다"로 일관.
- 활성 비율은 시뮬레이션(§9)으로 data-designer가 확정한다. 구역별 **정확히 round(n × 비율)개**(최소 1)를 뽑는다(베르누이가 아니라 표본 추출 — 레이드마다 총량 편차가 작다).

## 4. 에셋 조사 (Assets 아래 패키지, git 제외)
판단: ○ 쓴다 / △ 선택 / × 안 씀. 패키지 모델은 **AssetSwap 스킨 방식으로만** 쓴다(블록아웃 콜라이더 + 자식 `Skin`). 크기·방향은 구현 때 미리보기 렌더로 잰다(AssetSwap §3).
| 용도 | 후보(경로는 패키지 폴더 기준) | 판단 | 비고 |
|---|---|---|---|
| 죽은 나무 | **없음.** ithappy `Apocalypse_Free/Prefabs/Environment/Pine_01`(살아 있는 소나무, 머티리얼 1개 "Color") | △ | 잎을 뗄 수 없다. Ash_Black 틴트 "불탄 소나무"로 10~20% 섞는 선택안(§11 Q3). **기본안 = 자체 생성 메시**(§6.3) |
| 풀 | ithappy `Military_Free/Prefabs/Decorations/Grass_003`·`Grass_008`(아틀라스 `Military_base`, fbx 72·103KB) | × (풀) / △ (덤불 스킨) | TerrainData(git 포함)가 git 제외 프리팹을 참조하면 패키지 없는 clone에서 디테일이 깨진다 → 풀은 자체 메시. 정점 수도 디테일용으로 무겁다 |
| 덤불 | `Grass_008`, `Cactus_001/002`(테마 안 맞음) | △ / × | 기본안 = 자체 생성 마른 덤불 메시 |
| 쓰러진 통나무 | Barriers `WoodenBarriers/WoodenBarrierParts/SingleLog_09_v1`·`SingleSmallLog_12_v2` | ○(스킨) | 블록아웃 = 자체 통나무 메시 |
| 건물 | Abandoned `house_aband`(+`house_roof`)·`house_ruined`(기존 배정), `house_enter`·`house_stairs`(부품일 수 있음 — 미리보기 확인), Military `Buildings/Tent_002`·`Tent_010`·`Radiostation_001`·`Tower_003`(감시탑) | ○ | `HouseSkins`를 2 → 5~6종으로 늘림. 텐트도 콜라이더는 속 찬 상자 |
| 컨테이너 | `FREE Low Poly Shipping Container`(기존) | ○ | — |
| 승용차(트렁크) | ithappy `Props/Apocalypse_Car_01`, Military `Vehicles/Hummer_003`(기존 `CarSkins`) | ○ | 트렁크 콜라이더는 스킨과 별개(§5.4) |
| 큰 차량 | Military `Vehicles/Tank_006`(전차 잔해), `Generator_004` | ○ / △ | 트렁크 없음 |
| 소형 구조물 | Barriers `ConcreteBarrier_05/08/29/39`, `SandBagWall_04_v2`·`SandBagPallet_16_v3`·`SandBarrier_28_v1`, `MetalBarrier_06_v2`·`MetalFence_23_v1`, `BarbedWire*`, `WoodenBarrier_09_v1`·`WoodenPlankBarrier_14_v1`; ithappy `Concrete_Fence`·`Road_Barrier_01`; Military `Barrier_004~015`·`Hedgehog_001`·`Tires_001`·`Tire_001`·`Barrel_005`; Abandoned `pole_wood`(전봇대)·`junk_*`·`stove` | ○ | 새 도형 `Sandbag`·`Fence`·`Pole`·`Debris`(§6.2)에 배정 |
| 상자 | `Lowpoly_Crates/Meshes/Crate_01~15`(지금 01·02·05만), Military `Box_003/005/022` | ○ | 133개라 변형을 6종 이상으로 |
| 드럼통 | H&L `barrel_toxic_04`(지금 01·02) | △ | 방사능 구역 밖에는 쓰지 않음(Toxic 색은 방사능 전용 §10.1) |
**블록아웃 대안(패키지 없음):** 건물·차량·벽·상자는 지금처럼 팔레트 상자, 나무·덤불·통나무·풀은 **자체 생성 메시가 곧 최종 모습**(git 포함, 패키지 불필요).

## 5. 데이터
### 5.1 새 SO (원본 수치 = SO, §14 추가 — 문서 수정은 CHANGELOG 먼저)
**`LootSpawnConfigSO`** (Inventory, `Data/Inventory/LootSpawnConfig.asset`)
| 필드 | 뜻 | 기본값 |
|---|---|---|
| `zones[]` (`LootZoneConfig`) | `zoneId`(POI id / `ROUTE` / `OUTSKIRT`), `crateTargetCount`·`trunkTargetCount`(배치 생성 목표 — 바꾸면 Rebuild), `crateActiveRatio`·`trunkActiveRatio`, `crateTable`·`trunkTable` | §3.2 표 |
| `minActivePerZone` | 구역·종류별 최소 활성 수 | 1 |
| `inactiveMode` | `Empty`(빈 컨테이너) / `JunkOnly`(`inactiveTable` 굴림) | `Empty`(§11 Q1) |
| `inactiveTable` | `JunkOnly`일 때 | null |
| `crateGridSize`·`trunkGridSize` | 그리드 | 6×4(지금 M4 상수와 같음) · 5×3 |
| `crateDisplayName`·`trunkDisplayName` | 프롬프트 이름 | "보급 상자" · "차량 트렁크" |
→ M4 `CrateGridSize`·`CrateDisplayName` 상수는 이 SO 값을 읽게 바꾼다(값 같음 — 기존 21개 바이트 그대로).

**`MapDensityConfigSO`** (Raid, `Data/Raid/MapDensityConfig.asset` — `AtmosphereConfigSO`와 같은 어셈블리. 지금은 에디터 생성기만 읽고, 폭풍 컬링(§8.3)을 넣으면 런타임도 읽는다)
| 묶음 | 필드(기본값) |
|---|---|
| 나무 | `treeCount` 1500, `treeMinSpacingM` 5, `treeGroveNoiseScaleM` 90, `treeGroveThreshold` 0.45, `treeScaleRange` (0.8, 1.3), `treeMaxSlopeDeg` 30 |
| 덤불·통나무 | `bushCount` 400, `logCount` 120, `bushMaxHeightM` 1.0 |
| 풀 | `grassCoverage` 0.4, `grassMaxPerM2` 1.2, `grassDistanceM` 50, `grassHeightRange` (0.3, 0.8), `detailResolution` 1024, `detailResolutionPerPatch` 32 |
| 컬링(실제 거리, m) | 나무 LOD1 60·컬 260, 덤불 90, 통나무 120, 작은 구조물 180, 승용차 300, 건물 450, 상자 120 |
| 그림자 | `shadowMinHeightM` 1.5(이보다 낮은 새 산포물·구조물은 그림자 끔), `treeLod1Shadows` false |
| 상한(확인용) | `maxNewCoverPoints` 700, `maxTotalCoverPoints` 1200 |
LODGroup 비율은 생성 때 `거리 → 화면 비율`로 바꾼다(플레이어 카메라 FOV·현재 품질 단계 `lodBias` 반영 — SO 값 = 실제 거리).

### 5.2 새 루트 테이블 (data-designer 초안, 시뮬레이션으로 확정)
| 테이블 | 항목(가중치) | 굴림 | 굴림당 basePrice |
|---|---|---|---|
| `LT-CRATE-LOW` (길목·외곽 상자) | JNK-01 4, JNK-03 4, JNK-02 1, MED-BND 3, ITM-WTR 2, ITM-FOD 2, MAG-9MM-12 1, BOX-12GA-8 1, MED-SPL 1 | 1~2 | 약 110 (상자당 약 166) |
| `LT-TRUNK` (트렁크) | JNK-01 3, JNK-03 3, JNK-02 1, ITM-WTR 2, ITM-FOD 2, MED-BND 2, BOX-12GA-8 1, MAG-9MM-12 1, MED-TRQ 1 | 0~2 | 약 122 (트렁크당 약 122) |
| `LT-CRATE` (POI, 기존) | 그대로 | 1~3 | 약 224 (상자당 약 448) |
신호탄(ITM-FLR-01)은 `LT-CRATE`에만(M4 Q7-A "최저 가중치" 유지). 새 아이템 없음.
생성: `MapDensityDataSetup`(메뉴 §6.4)이 SO·테이블을 **파일에 없을 때만** 만든다(MVP 마무리 방식, 기획 조정 보존).

### 5.3 레이드 시드 파생 결정적 활성화
- **Core:** `RaidStarted`에 `LootSeed`(int) 필드 추가(생성자 오버로드 — 기존 호출 그대로 컴파일). `Core/Raid/SeedMath`(순수): `StableHash(string)`(FNV-1a, 런타임·플랫폼 무관), `Mix(int, int)`, 0이면 1로.
- **Raid:** `RaidController.Start`가 쓴 시드(`seed` 또는 뽑은 값)로 `LootSeed = SeedMath.Mix(used, StableHash("LOOT"))` — **기존 `System.Random` 소비 순서를 바꾸지 않는다**(스폰·탈출구 선택 회귀 없음).
- **Inventory:**
  - `LootSpawnSlot`(MonoBehaviour, 상자·트렁크 오브젝트에): `slotKey`(오브젝트 이름 — 생성 때 고정), `zoneId`, `kind`(`LootSlotKind.Crate/Trunk`), `alwaysActive`.
  - `LootActivationPlanner`(순수): (슬롯 목록, 구역 설정, lootSeed) → 활성 슬롯과 슬롯별 내용물 시드. 구역·종류마다 `slotKey` 서수 정렬(씬 순서 무관) →
    `System.Random(Mix(lootSeed, StableHash(zoneId + kind)))` Fisher–Yates → 앞 k개. 내용물 시드 = `Mix(lootSeed, StableHash(slotKey))`.
  - `LootSpawnDirector`(씬 최상위 1개, 정적 인스턴스 없음): `OnEnable`에서 `RaidStarted` 구독 → 계획 → 각 `LootContainer.ConfigureForRaid(table, seed)` 또는 `ConfigureEmpty()`.
    이미 연 컨테이너(Model 있음)는 건드리지 않고 경고. 개발 빌드는 활성 수를 한 줄 로그.
  - `LootContainer`: 위 두 메서드 추가(Model 생성 전만 유효). 지휘자가 없는 씬(M2 확인 씬 등)은 직렬화된 테이블 그대로 = 지금 동작.
- 기능 간 통신은 `RaidStarted` 이벤트 하나(Inventory → Raid 직접 참조 없음). 새 서비스·싱글톤 없음.
- **비활성 처리 제안: `Empty`(기본)** — 오브젝트·콜라이더·스킨은 그대로, 열면 빈 그리드(검색 즉시 끝). 오브젝트를 끄는 방식은 NavMesh·엄폐·사격 판정이 레이드마다 달라지고,
  상호작용 프롬프트를 없애면 "빈 곳"이 멀리서 드러나 §1 정보의 희소성에 어긋난다. `JunkOnly`(고철 0~1개)는 허탕 피로를 줄이는 대안(§11 Q1).
- **세이브 영향 없음:** 컨테이너 상태·활성 목록은 레이드 안에서만(RULE-03 — 레이드 중 저장 없음), `RaidStarted`는 저장되지 않는다. `save.json` version 3 그대로, `ProfileService` 변경 없음.
  가져온 아이템은 기존 탈출 경로(`CarriedOutLoadout`)로만 저장된다.

### 5.4 트렁크 오브젝트
승용차 구조물 아래 자식 `Trunk`: 렌더러 없음, BoxCollider(0.6 × 0.8 × 1.6m, 비트리거 — 상호작용 레이캐스트가 트리거를 무시) + `LootContainer` + `LootSpawnSlot(kind = Trunk)`.
차체 뒷면에서 5cm 밖으로 내밀어 뒤에서 보면 트렁크가 먼저 맞는다(`InteractionTargets.Find`는 부모로 올라가므로 차체 콜라이더에는 붙이지 않는다). 콜라이더가 차체 안이라 NavMesh 영향 없음.
AssetSwap: `AssetSwapApply.ApplyCrate`는 `kind = Trunk`를 건너뛴다(렌더러가 없어 지금도 스킨은 안 붙지만 명시).

## 6. 배치
### 6.1 생성 방식
| 대상 | 방식 | 이유 |
|---|---|---|
| POI 안 건물·구조물·차량 | **손 배치**(`MapDensityLayout` 좌표 표) | 교전 공간·적 경로·보스 거점과 맞물린다 |
| 길목 새 "작은 마을" 6곳 | 중심·방향은 손, 마을 안 건물 4~6채는 seed 고정 생성(격자 + 흔들림) | 위치 의도 + 반복 작업 줄이기 |
| 외곽 외딴 농가·차량, 길목 흩어진 구조물 | seed 고정 생성(포아송 디스크, 최소 간격 40m / 15m) | 수가 많고 위치 의도가 약하다 |
| 죽은 나무·덤불·통나무 | seed 고정 생성: 군락 마스크(Perlin, `treeGroveThreshold`) × 포아송 디스크(Bridson, `treeMinSpacingM`) | 1,500개 — 손 불가 |
| 풀 | TerrainData 디테일 밀도 맵(노이즈 마스크 × 제외 마스크) | 지형 기능 |
| 새 상자 112 | seed 고정 생성: 구조물 면 앞 0.9m 후보 → 구역 할당 → 구역 목표 수만큼 간격 8m 이상으로 고름 | 벽·건물에 붙어 자연스럽고 접근면이 열린다 |
| 트렁크 67 | 규칙(승용차마다 1개) | §3.2 |
| 기존 21 상자 | 그대로(슬롯만 덧붙임) | M4 레벨 데이터 보존 |
모든 생성은 `System.Random(고정 seed)`와 고정 순회 순서 — 같은 입력이면 같은 결과(EditMode로 두 번 생성 비교). seed: 구조물 5052, 나무 5051, 풀 5054, 상자 5053(`MapDensityLayout` 상수, 레벨 데이터).

### 6.2 파일·코드 (EditorTools, 네임스페이스 `Ashfall.EditorTools`)
- `MapDensityLayout.cs` — 손 배치 표, 구역 함수(`LootZoneRules`), 포아송 디스크·마스크 생성기(순수), 데이터 검사(MapPolishLayout.CheckItems 확장: **POI 안 허용 + 순찰 구간 선분 3m 통로**, 보스 거점 25m 안 건물·나무 금지).
- `MapDensityPrefabs.cs` — 자체 메시(죽은 나무 4종: 줄기 + 가지 3~6, LOD0 약 200·LOD1 약 60 삼각형 / 덤불 2종 / 통나무 1종 / 풀포기 2종 약 12 삼각형)를
  `Assets/_Project/Art/Meshes/Scatter/`에, 프리팹 `Assets/_Project/Prefabs/Scatter/SC_*.prefab`(LODGroup·CapsuleCollider·그림자 설정)을 **없을 때만** 만든다(같은 경로 덮어쓰기 = 모델 교체).
  새 블록아웃 도형 `Sandbag`·`Fence`·`Pole`·`Debris`는 `M4MapSetup.Shape`·`PaletteMaterialMap` 규칙에 추가(맵 개선 `Container`와 같은 방식).
  머티리얼 `M_DeadWood`(Ash_Black ↔ Ash_Grey), `M_DryGrass`·`M_DryGrassTall`(Rust_Red·Ash_Grey 섞음) — 팔레트 폴더, `enableInstancing = true`.
- `MapDensitySceneApply.cs` — 레이드 씬 적용. Environment 아래 그룹 `MapDensity_Buildings`·`MapDensity_Structures`·`MapDensity_Vegetation`·`MapDensity_Loot`를
  **모두 없을 때만** 만든다(하나라도 빠지면 넷 다 지우고 다시). 구조물은 `M4MapSetup.BlockoutBuilder`(이름 `{Shape}_{nn}` → M6 칠하기·AssetSwap 스킨 자동), 나무는 프리팹 인스턴스.
  그룹 루트 NavMeshModifier(Not Walkable) + 속 찬 상자 내부 `NavBlock`(맵 개선 `AddNavBlocks`). 씬 최상위 `LootSpawnDirector`(설정 연결). 기존 21 상자·승용차에 슬롯·트렁크 덧붙임.
  풀·지형 설정(`drawTreesAndFoliage`·`detailObjectDistance`)과 LOD 거리·그림자는 **매 실행 SO에서 다시 맞춘다**(같으면 안 바꿈 — 맵 개선 광원과 같은 방식).
- `MapDensitySetup.cs` — 메뉴(§6.4). `MapDensityDataSetup.cs` — SO·테이블(§5.2). `LootEconomySimulator.cs` — §9(순수 + 메뉴).
- 공용화: `MapPolishSceneApply`의 `CheckPlacement`·`AddNavBlocks`·`NavState`·`RemoveCovers*`·롤백을 `MapPlacementChecks`(internal)로 옮긴다 — **동작 변경 없음**(맵 개선 재실행 바이트 동일로 확인).
- 런타임: Core `RaidStarted.LootSeed`·`SeedMath`, Raid `MapDensityConfigSO`·`RaidController` 한 줄, Inventory `LootSpawnConfigSO`·`LootSpawnSlot`·`LootSlotKind`·`LootActivationPlanner`·`LootSpawnDirector`·`LootContainer` 메서드 2개.
  새 asmdef 없음(link.xml 변경 없음).
- AssetSwap: `ApplyCrate` 트렁크 제외, 스킨이 부모 `LODGroup` 안이면 스킨 렌더러를 같은 LOD에 넣음(§8.1), `HouseSkins`·`CrateSkins` 변형 추가와 새 도형 배정(§4).

### 6.3 순서·멱등
- `M4SceneSetup.CreateRaidScene`: M7 → 맵 개선 → **`MapDensitySceneApply.ApplyRaidIfAssetsExist`** 순(씬을 다시 만들어도 빠지지 않게). 에셋 교체는 지금처럼 별도 메뉴(패키지 있는 PC에서 다시 실행).
- `ApplyAll`: 맵 개선 그룹이 없으면 `MapPolishSetup.ApplyAll` → 데이터(없을 때만) → `M6ArtSetup.CreateAssets`(새 팔레트 머티리얼) → 메시·프리팹(없을 때만) → 레이드 씬 → `ApplyRaid` → 바뀌었으면 저장.
- 두 번째 실행에서 바뀐 파일 0개(맵 개선 §8과 같은 바이트 비교). TerrainData 디테일은 계산한 밀도 맵이 파일과 다를 때만 쓴다.

### 6.4 메뉴
- `Ashfall/Setup/Apply Map Density` (배치 `-executeMethod Ashfall.EditorTools.MapDensitySetup.ApplyAll`)
- `Ashfall/Setup/Map Density/Rebuild Map Density (Raid Scene)` — 그룹 지우고 다시 배치·확인·굽기(프리팹·목표 수를 바꾼 뒤)
- `Ashfall/Setup/Map Density/Refresh Grass & Culling` — 풀·LOD 거리·그림자만(NavMesh 안 건드림, SO 조정용)
- `Ashfall/Setup/Create Map Density Data` — SO·루트 테이블(없을 때만)
- `Ashfall/Tools/Simulate Loot Economy` — §9 보고서를 로그로

## 7. 확인 (적용 중 자동, 실패 = 예외, MapPolish §6 재사용)
1. **배치 전(데이터·물리):** 새 콜라이더가 기존 콜라이더(지형 제외)와 안 겹침. 엄폐 지점·peek·상자·적 시작·순찰 지점에서 2.5m, **순찰 구간 선분에서 3m**,
   스폰·탈출 25m, 전원 스위치 10m, 도로 띠 2m, 맵 경계 10m. 저격 시선(퍼치 눈 1.6m → 감시 지점 1.0m)을 **3D 레이캐스트로** 새 콜라이더가 막지 않음(나무가 많아 2D 선분 거리만으로는 부족).
   보스 거점(M7 `BOS_VIL`, 반경 25m) 안 새 건물·나무 없음. 건물 발자국 지면 높이 차 ≤ 2.5m, 경사 ≤ 8°(건물)·25°(구조물)·30°(나무).
2. **굽기 뒤:** 스폰 3 → 활성 탈출구 경로 전부 Complete(`ValidateNavigation`), 굽기 전 NavMesh 위 엄폐 지점은 0.5m 안 유지, 적 34 시작점·순찰 지점 NavMesh 위,
   순찰 구간 28개 Complete + **경로 길이 증가 ≤ 20%**(적이 나무 사이로 이상하게 돌지 않게), 새 구조물 자리 NavMesh 구멍, 속 찬 상자 안 NavMesh 섬 없음(NavBlock).
   새 엄폐 지점은 기준 스폰(SP-W)에서 닿아야(아니면 뺀다), 드러난 높이로 High/Low/제외(맵 개선 규칙). 나무·덤불·통나무에는 엄폐 지점을 만들지 않는다.
3. **상자·트렁크 200곳 도달:** 접근점(상자 열린 면 / 트렁크 뒤 1.2m)이 NavMesh 1.0m 안 + SP-W에서 경로 Complete, 접근점 눈높이 1.6m에서 컨테이너 중심으로 쏜 레이가
   `InventoryConfigSO.interactRangeM`(2.0m) 안에서 **그 컨테이너 콜라이더를 처음 맞춘다**(트렁크가 차체에 가려지지 않음). 구역별 개수 = `LootSpawnConfigSO` 목표.
4. **상한:** 새 엄폐 지점 ≤ 700, 전체 ≤ 1200(`CoverSelector` 선형 탐색 — 넘으면 줄이거나 CoverIndex 공간 분할을 후속 과제로).
5. **롤백:** 실패하면 만든 그룹·덧붙인 슬롯·트렁크를 지우고 NavMesh와 **TerrainData**를 적용 전 바이트로 되돌린 뒤 던진다(씬 저장 안 함).
   그룹이 이미 있는데 NavMesh에 구멍이 없으면 예외 — Rebuild 메뉴 안내.

## 8. 렌더링·성능
### 8.1 기본 최적화 (반드시)
| 대상 | 그리기 | LOD·컬링 | 그림자 | 정적 배칭 |
|---|---|---|---|---|
| 풀 | **Terrain Detail, `DetailPrototype.useInstancing = true`(GPU 인스턴싱)**, VertexLit 메시, 패치 단위 절두체 컬링 | `detailObjectDistance` 50m | 드리우지 않음(받기만) — API 지원 여부 구현 때 확인 | 해당 없음 |
| 죽은 나무 | 프리팹 MeshRenderer, 머티리얼 인스턴싱 켬 | LODGroup 2단계 + 컬(LOD1 60m, 컬 260m), 크로스페이드 | LOD0만 | **안 함**(LOD·크로스페이드 우선) |
| 덤불·통나무 | 〃 | 컬 90m / 120m | 끔 | 안 함 |
| 새 건물·차량·구조물 | 블록아웃(정적 배칭, 스킨이 있으면 `BlockoutSkin`이 숨김) + 스킨(Mesh LOD, 배칭 안 함 — AssetSwap §5.1) | 래퍼에 컬 전용 LODGroup(건물 450 / 승용차 300 / 작은 구조물 180 / 상자 120m). 스킨을 얹으면 AssetSwap이 스킨 렌더러를 같은 LOD에 넣는다 | 높이 < 1.5m 끔, 그 밖 켬 | 블록아웃만(지금과 같음) |
- 정적 배칭과 LOD: LODGroup 컬링은 정적 배칭 렌더러에도 작동한다(보이는 렌더러 구간만 그림). 충돌하는 것은 Unity 6 **Mesh LOD**(배칭된 메시는 단계를 못 고름) — 그래서 스킨·산포물은 배칭하지 않는다(AssetSwap 선례).
- URP에서는 SRP Batcher가 GPU 인스턴싱보다 우선이라 MeshRenderer 나무의 "인스턴싱 켬"은 GRD를 켤 때만 효과가 있다. 실제 인스턴싱은 풀(Terrain Detail)에서 확실히 얻고,
  나무는 SRP Batcher + LOD·컬로 비용을 묶는다(보이는 나무 약 60~120그루 추정).
### 8.2 선택 실험: GPU Resident Drawer
URP 에셋 `GPU Resident Drawer = Instanced Drawing`(Forward+ 조건 충족) + Graphics 설정 "BatchRendererGroup Variants = Keep All"이 필요한 **프로젝트 설정 변경**이다.
같은 날 A/B(끔/켬)로 재고, 이득 ≥ 1ms이고 회귀(`BlockoutSkin`의 `forceRenderingOff`, LOD 크로스페이드, 정적 배칭 렌더러 처리 — 구현 때 Unity 6.3 문서로 재확인)가 없을 때만 사용자 확인 뒤 켠다.
### 8.3 재 폭풍 30m 시야
폭풍 규칙(`stormVisibilityM` 30, 맑을 때 600m)은 그대로. 안개는 그리기를 줄이지 않으므로 Peak가 최악 구간이다.
3단계 측정에서 Peak 증가가 2ms를 넘으면 Raid `AshStormPresenter`가 Peak 동안 `Terrain.detailObjectDistance`를 `stormVisibilityM + 15`로, LOD 거리 배율(`QualitySettings.lodBias`)을 낮춘다
(값은 `MapDensityConfigSO`, 폭풍이 끝나면 원래 값 — 순수 계산은 `AshStormVisualMath`에 함수 하나). 측정 전에는 넣지 않는다.
### 8.4 게임플레이 영향
풀·덤불은 콜라이더가 없어 적 시야 레이(`PhysicsLineOfSight`)를 막지 않는다 — 플레이어만 가려지는 불공정을 피하려고 풀 키 ≤ 0.8m·덤불 ≤ 1.0m, 엄폐 지점 3m 안 덤불 없음.
나무 줄기(콜라이더)는 양쪽 시야·탄을 똑같이 막는다. 나무 탄착·발소리는 기본 재질(범위 밖).
### 8.5 측정
- 같은 방법(IL2CPP 개발 빌드, 1080p 전체 화면, `-ashfallSmoke … -ashfallSmokeRaidSec 90`, `[DevDiagnostics]` 10초 창 중앙값 + `RenderCounters` 드로콜·삼각형), 적용 전·후 빌드를 번갈아 2회씩(같은 날 쌍끼리만 비교).
  스모크는 스폰에서 가만히 있으므로 에디터 1080p 렌더 비교(맵 개선 10시점 + **새 밀집 시점 3곳: 마을·작은 마을·죽은 숲 가장자리**)를 함께. 패키지 있는 PC에서 스킨 포함 측정.
- 기록: Rising·Peak 창, 드로콜·삼각형·SetPass, NavMesh 굽기 시간·크기, 씬·TerrainData 파일 크기, `RaidPerformanceTests` 엄폐 질의 시간.
- 예상 비용(추정, 실측으로 대체): 드로콜 +300~400(약 1,900 → 2,250), 삼각형 +10만~20만(약 50만 → 65만), 풀 인스턴스 화면 안 약 1,500(1.8만 삼각형).
  HD 620 1080p **+2~4ms**, GTX 1660은 +0.5~1ms 수준 예상. 이 PC 목표는 AssetSwap과 같은 "**+3ms 이내**"(기록용), 판정은 사용자 GTX 1660.
- 크기: 씬 +약 3MB(나무 인스턴스 1,500 × 약 1.5KB 등), TerrainData +약 2MB(디테일 2층), NavMesh 굽기 17s → 약 25~35s 예상.
- 넘치면 조정 순서: 풀 밀도·거리 → 나무 컬 거리 → 나무 수 → 폭풍 컬링(§8.3) → GRD(§8.2).

## 9. 경제 (data-designer)
- 영향: 루트 지점 21 → 200(약 9.5배). 맵 전체 기대 가치(basePrice)는 초안 비율에서 약 1.8만(지금 약 8,500의 약 2.1배)이지만, 한 레이드에 실제로 가져가는 양은
  시간(25분)·이동·검색(아이템당 0.5초)·**가방 칸수**가 묶는다. 판단 지표는 "레이드당 반출 판매가치(탄피 환산)". 신호탄 기대 개수(EXT-03 희소성)·해독제·방독면 빈도도 같이 본다.
- **시뮬레이션** `LootEconomySimulator`(EditorTools, 순수 — `LootRoller`·`LootActivationPlanner`·배치 데이터·`PlayerConfigSO` 이동 속도·`InventoryConfigSO` 검색 시간·`TraderConfigSO.sellPriceMultiplier` 사용):
  seed 1..N(N = 1000) 레이드마다 스폰 선택(`ActiveExtractionPicker`와 같은 규칙) → 활성화 계획 → 플레이어 모델 2종
  ① **POI 털이**(가장 가까운 POI의 컨테이너 전부 → 활성 탈출구), ② **탐욕 순회**(가장 가까운 미방문 컨테이너, 직선 × 1.3 경로 계수, 15분 예산) —
  각각 반입 장비 2종(부랑자 키트 = 주머니만 / 표준 = 조끼 + 배낭 칸수)으로 칸이 차면 종료. 출력: 평균·중앙값·P90 판매가치, 아이템별 개수, 신호탄/레이드.
  비교 기준 = **지금 맵(상자 21, 전부 활성)** 같은 모델.
- **목표(기본안, §11 Q2):** 레이드당 기대 판매가치 **지금의 1.3~1.5배**(①·② 모두). 은신처 경제 싱크(보관함 업그레이드 5000/12000, 상인 가격)는 그대로.
- 조정 손잡이 순서: 구역별 활성 비율 → `LT-CRATE-LOW`·`LT-TRUNK` 가중치(고철 비중)·굴림 수 → POI `LT-CRATE` 굴림 수(1~3 → 1~2) → `inactiveMode`.
  초안(§3.2·§5.2)으로 어림하면 POI 하나 털이가 약 1.6~1.8배 → POI 비율 0.35 안팎이 될 가능성이 높다.
- EditMode 회귀: 커밋된 SO·테이블로 시뮬레이션(N = 200, 고정 seed)이 목표 범위 안인지 확인하는 테스트(데이터를 바꾸면 이 테스트가 알려 준다).
- data-designer 할 일: §5.2 테이블·§3.2 비율 확정, 시뮬레이션 보고 표(전·후, 두 전략 × 두 장비), `M7_design.md` §10 임시값 목록에 새 값 추가, CHANGELOG 수치 기록.

## 10. 테스트 계획
**EditMode**
- `LootActivationPlanner`: 같은 seed = 같은 결과, 다른 seed = 다른 집합(1000 seed 중 동일 집합 비율 낮음), 구역별 정확히 round(n × 비율)·최소 1, `alwaysActive` 항상, 슬롯 입력 순서 무관, 비율 0·1 경계, 내용물 시드 0 아님.
- `SeedMath`: `StableHash` 고정 기대값(플랫폼 무관), `Mix` 0 회피. `RaidStarted` 기존 생성자 호환. `RaidController` 시드 고정 시 스폰·탈출구 선택이 이전과 같음(회귀).
- `LootContainer.ConfigureForRaid/ConfigureEmpty`: Model 전 적용, 이후 무시, 빈 컨테이너 검색 즉시 완료.
- `MapDensityLayout`: 생성 결정성(두 번 생성 동일), 개수 = SO 목표, 데이터 회피 검사(§7.1 데이터 부분, 항목 주입 — 맵 개선 리뷰 M3 방식), 구역 함수 경계, 포아송 최소 간격.
- `MapDensityPrefabs`: 메시 삼각형 수 상한, LOD 비율 변환(거리 ↔ 비율 왕복), 머티리얼 팔레트 표(`PaletteMaterialMap`).
- 데이터: `LootSpawnConfig` 구역 id = POI 5 + ROUTE + OUTSKIRT, 목표 합 200, 테이블 항목 null 없음, 신호탄은 `LT-CRATE`에만. 경제 시뮬레이션 범위(§9).
- 기존: `MapPolishLayoutTests`·`LootContainerRollTests` 등 그대로 통과.
**PlayMode** (레이드 씬)
- `MapDensitySceneSmokeTests`: 그룹 4개, 건물 ≥ 85, 나무 ≥ 1,400(LODGroup·콜라이더), 풀 디테일 층 2·`drawTreesAndFoliage`, 컨테이너 200(상자 133·트렁크 67, 구역별 수),
  `LootSpawnDirector` 1개, 고정 seed 레이드에서 활성 수 = 계획, 비활성은 빈 그리드, 신호탄 상자 활성, 새 엄폐 지점 NavMesh 위, 그림자 정책(높이 < 1.5m 끔).
- 트렁크 흐름: 승용차 뒤에서 F 레이캐스트 → `차량 트렁크` 열림·검색(`LootSearchFlowTests` 방식).
- 고칠 기존 테스트: `AssetSwapSceneSmokeTests`(모든 `LootContainer`에 상자 스킨 요구 → 트렁크 제외), `RaidVisualSmokeTests`(모든 `LootContainer`에 `M_Crate` 렌더러 요구 → 트렁크 제외,
  팔레트 검사에 새 머티리얼 포함), `M5TestSupport.TakeCrateLoot`(빈 컨테이너 건너뛰기 — 이미 그렇게 동작, 확인만).
- 성능: `RaidPerformanceTests` 엄폐 질의 < 200µs, `M6/M7PerformanceTests` 그대로 통과.
- 전체 EditMode·PlayMode 2회 연속, IL2CPP 개발·릴리스 빌드, 개발 빌드 스모크 0, 패키지 없는 clone 절차(AssetSwap §3) 재확인.

## 11. 구현 단계·커밋·에이전트
사본(`Ashfall_md`)에서 Unity를 돌리고 원본에는 자기 파일만 쓴다(README 진행 방식). 커밋 메시지 예: "맵 밀도 ①: 건물·구조물 추가 (SYS-MAP)".
| 단계(커밋) | 내용 | 담당 |
|---|---|---|
| ⓪ 계약 | Core `RaidStarted.LootSeed`·`SeedMath`, Inventory 타입 껍데기(`LootSpawnSlot`·`LootSlotKind`·`LootActivationPlanner`·`LootSpawnDirector`·`LootSpawnConfigSO`), Raid `MapDensityConfigSO`, EditMode 계약 테스트 | gameplay-programmer |
| ① 구조물·건물 | `MapPlacementChecks` 공용화(맵 개선 바이트 동일 확인), 새 도형, `MapDensityLayout` 구조물·건물·차량, SceneApply 1부(그룹·NavBlock·굽기·확인·롤백), 메뉴 | gameplay-programmer |
| ② 산포 | 자체 메시·프리팹·머티리얼, 나무·덤불·통나무 생성, 풀 디테일, LOD·컬·그림자, Refresh 메뉴 | gameplay-programmer |
| ③ 상자·트렁크·루트 | 새 상자 112·트렁크 67·슬롯, 지휘자·활성화·`LootContainer` 메서드, 도달 확인, `RaidController` 한 줄 / 데이터(SO 값·`LT-CRATE-LOW`·`LT-TRUNK`)·시뮬레이터·보고 | gameplay-programmer / **data-designer** |
| ④ 스킨·측정 | AssetSwap 트렁크 제외·LODGroup 연동·변형 추가, 패키지 있는 PC에서 `Apply Asset Swap`, 성능 A/B·에디터 렌더, (조건부) 폭풍 컬링·GRD 실험 | gameplay-programmer, **test-engineer** |
| ⑤ 검증·리뷰 | PlayMode 새 테스트·기존 테스트 수정, 전체 2회·빌드·스모크 / §13.3 규칙·성능 읽기 전용 리뷰 → 수정 | **test-engineer** / **code-reviewer** |
| ⑥ 문서 | `CHANGELOG_GDD.md` "맵 밀도 설계 결정" → GDD §8.1(산포·밀도·`MapDensityLayout`)·§14(새 SO 2개) 수정, CLAUDE.md 현재 진행·`docs/design/README.md` 한 줄, 이 문서 끝에 구현 메모 | 메인 세션 |
②는 ①과 병렬 가능(그룹이 다르고 NavMesh 굽기만 마지막에 함께). ③의 데이터·시뮬레이터는 ⓪ 뒤 바로 병렬.

## 12. CHANGELOG 초안 (구현 때 기록, GDD와 충돌하는 내용 없음)
- §6: 차량 트렁크 구현(승용차마다 1개, 그리드 5×3), 루트 지점 약 200, 레이드 시드 파생 무작위 활성(구역별 비율, 비활성 = 빈 컨테이너), 새 테이블 `LT-CRATE-LOW`·`LT-TRUNK`.
- §8.1: 건물·소형 구조물·차량 추가, 죽은 나무·덤불·통나무·풀 산포(배치 `MapDensityLayout`, 임시). §14: `LootSpawnConfigSO`·`MapDensityConfigSO`.
- §10.2: 산포물 LOD·컬링·그림자 정책(작은 산포물 그림자 없음). 세이브 영향 없음(version 3).

## 13. 사용자에게 물어볼 결정 (기본안 제시)
- **Q1 비활성 컨테이너:** A 빈 컨테이너(열면 비어 있음, 겉모습 같음) — **기본안** / B 고철만 0~1개(허탕 피로 감소, 경제 영향 작음).
- **Q2 경제 목표:** A 레이드당 기대 판매가치 지금의 **1.3~1.5배**(활성 약 30~35%, 길목·외곽·트렁크는 고철 위주) — **기본안** / B 2~2.5배(활성 약 50%, 풍족한 루팅).
- **Q3 죽은 나무 모델:** A 자체 생성 메시(패키지 불필요·공개 저장소에서도 같은 모습) — **기본안** / B A + ithappy 소나무를 검게 칠한 "불탄 소나무" 10~20% 섞기 /
  C 사용자가 죽은 나무 무료 패키지를 더 가져오면 스킨으로 교체(가져온 뒤 같은 경로 프리팹 덮어쓰기).
- (측정 뒤) GPU Resident Drawer를 켤지는 §8.2 A/B 결과를 보고 다시 묻는다.

## 14. 구현 메모 — ⓪ 계약 · ① 구조물·건물 · ② 산포 (2026-10-29)
범위: ⓪①②만. ③ 상자·트렁크·루트(새 상자 112·트렁크 67·`LootSpawnConfig` 에셋·`LT-CRATE-LOW`·`LT-TRUNK`·지휘자 동작·`RaidController` 한 줄·시뮬레이터)와 ④ 스킨 변형 추가·⑤ 리뷰는 다음 단계.
원본 프로젝트(`Ashfall`)에서 바로 작업했다(사본 없음). 커밋은 하지 않았다.

### 14.1 파일
- 런타임(⓪): Core `Raid/SeedMath.cs`·`Events/GameEvents.cs`(`RaidStarted.LootSeed` + 생성자 오버로드, 기존 호출 그대로), Inventory `LootSlotKind`·`LootSpawnSlot`·`LootSpawnConfigSO`(+`LootZoneConfig`·`InactiveLootMode`)·
  `LootActivationPlanner`(순수, 구현 완료 — 계약 테스트용)·`LootSpawnDirector`(껍데기: 설정 참조만, 씬에 아직 없음), Raid `Data/MapDensityConfigSO.cs`. 새 asmdef 없음(link.xml 그대로).
- 에디터: `MapPlacementChecks`(맵 개선 확인 공용화 + 순찰 통로·보스 거점·경로 길이·`TryPath`), `MapDensityLayout`(구역 `LootZoneRules`·`Footprint`·공간 색인·구조물/산포 생성기·데이터 검사·포아송 디스크),
  `MapDensityPrefabs`(자체 메시·프리팹·LOD 비율 변환), `MapDensitySceneApply`(적용·롤백·Refresh·풀·③용 `PassengerCars`), `MapDensitySetup`(메뉴), `MapDensityPreview`(미리보기 렌더 도구).
- 기존 수정: `M4MapSetup`(도형 `Sandbag`·`Fence`·`Pole`·`Debris`, `Shape`·`Piece` public, `FixedPieces`, `TryPath`·`ValidateNavigation`), `PaletteMaterialMap`(새 도형 규칙·머티리얼 4·`Spec` 인스턴싱/양면),
  `M6ArtSetup.ApplySpec`(인스턴싱·`_Cull` — 기존 머티리얼은 값이 같아 그대로), `M6VisualSceneApply.PaintGroup`(그룹 바로 아래 프리팹 인스턴스는 칠하지 않음), `MapPolishSceneApply`(공용 함수로 위임),
  `M4SceneSetup.CreateRaidScene`(맵 개선 다음 `MapDensitySceneApply.ApplyRaidIfAssetsExist`), `AssetSwapApply.ApplyStructure`(컬 LODGroup에 스킨 렌더러 포함).
- 데이터·에셋(메뉴가 생성): `Data/Raid/MapDensityConfig.asset`, `Art/Materials/Palette/M_DeadWood·M_CharredWood·M_DryGrass·M_DryGrassTall`, `Art/Meshes/Scatter/*.asset`(11), `Prefabs/Scatter/SC_*.prefab`(9).

### 14.2 설계와 달라진 점
- **POI 안 배치:** 손 좌표 대신 POI 구역(발자국 + 40m)마다 seed 고정 생성(5052). 마을 6곳은 중심·방향만 손(`MapDensityLayout.Towns`), 외곽 농가 12곳은 외곽 띠(380~402m)에서 생성.
  분배는 §3.1 그대로(건물 POI 38·마을 30·농가 12 / 소형 POI 50·길목 70·외곽 30 / 승용차 POI 14·마을 8·농가 4·도로 옆 5 → 실제 구역별은 §14.3).
- **생성 = 입력에서 회피:** 씬의 기존 콜라이더 발자국(회전 상자 투영)·지점(엄폐·상자·적·순찰 2.5m, 스폰·탈출 25m, 스위치 10m)·순찰 구간 3m·도로 띠 2m·경계 10m·저격 시선 2m·보스 거점 25m·경사·지면 높이 차를
  생성기가 먼저 지키고, 씬 확인(`CheckPlacement`, 3D 저격 시선 포함)이 다시 본다. 새 구조물끼리·모든 콜라이더와 2.5m(통로).
- **기존 경로 통로 보존(추가):** 굽기 전 NavMesh 경로(스폰 3 → 탈출 4, 기준 스폰 → 적 34·상자 21·스위치, 순찰 구간)의 꼭짓점 선분 626개에서 새 콜라이더를 2m 비운다 — 도달성·순찰 경로 길이를 생성 단계에서 보장.
- **`TryPath`(추가):** 나무 1,500의 줄기 구멍으로 NavMesh 삼각형이 7.7천 → 2.4만으로 늘자 긴 질의(스폰 → 반대편 탈출·송전탑 언덕)가 A* 노드 한도에서 `PathPartial`이 됐다(실제로 끊긴 것이 아님 —
  25m 격자 표본 1,365/1,369로 NavMesh는 그대로, 부분 경로 끝에서 다시 찾으면 완성). 확인 함수(`ValidateNavigation`·`NavState`·엄폐 정리)는 부분 경로 끝에서 이어 찾고(최대 12번), 목표에 가까워지지 않으면(진짜 섬) 실패.
  희소한 NavMesh(맵 개선 이전)에서는 한 번에 끝나 결과가 같다. 구현은 `Ashfall.AI.NavPathProbe.TryConnected`(테스트와 공용, `M4MapSetup.TryPath`는 위임).
  **(리뷰 M1) 이어 찾기는 연결성(플레이어가 걸어서 닿는가: 스폰 → 탈출·적·상자, 엄폐 지점 도달) 확인에만 쓴다.** 적이 실제로 받는 경로는 한 번의 질의로 완성돼야 한다
  (`NavPathProbe.SingleQueryComplete`): 순찰 구간 28개 전부 + 적 시작점 → 250m(`LocalAgentPathM`) 안 다른 적·순찰 지점·상자 665개 — `NavState`가 굽기 전·뒤를 비교해 회귀면 실패(롤백),
  PlayMode `RaidScene_AgentPaths_SingleQueryComplete`도 같은 것을 본다. 결과: 굽기 뒤 28/28·665/665(최장 272m) 한 번에 완성.
  런타임 에이전트도 같은 노드 한도를 받는다 — PlayMode에서 임시 `NavMeshAgent`로 SP-N → EXT-02(약 940m)를 요청하면 `PathPartial`. 맵 횡단 12경로 중 한 번의 질의로 부분이 되는 것은 2개
  (SP-E → EXT-01, SP-N → EXT-02, 이어 찾기 1번) — 적은 맵을 가로지르는 경로를 받지 않으므로(순찰·근거리 교전) 지금은 실제 해결(나무를 NavMesh 굽기에서 빼고 `NavMeshObstacle`로 피하기,
  타일·복셀 조정 등)을 하지 않았다(§14.6).
- **엄폐 지점 간격(추가):** 새 엄폐 지점끼리·기존 지점과 4m(`newCoverMinSpacingM`) 안이면 뺀다 — 처음 3m에서 전체 1,201 > 1,200으로 실패해 4m로 정했다.
- **풀 그림자:** Terrain Detail에는 그림자 API가 없고, 머티리얼 ShadowCaster 패스를 끄면 URP 머티리얼 검증이 장면 렌더 때 다시 켜 파일이 매번 바뀌었다 → 머티리얼은 손대지 않고 풀 프리팹 렌더러 그림자 끔만 둔다.
  실제로 그림자가 지는지는 에디터에서 사람이 확인(§14.6).
- **나무 LOD 크기:** LODGroup 화면 비율은 프리팹(배율 1) 크기로 계산 — 배율 0.8~1.3 나무는 전환 거리도 그만큼 비례한다(씬 인스턴스마다 덮어쓰지 않아 씬 파일이 작다).
  구조물 컬 LODGroup 크기는 만들 때 블록아웃 기준으로 고정(스킨을 얹어도 바뀌지 않음 — 메뉴 순서와 무관하게 멱등).
- **Loot 그룹:** `MapDensity_Loot`는 ③이 만든다. 이번 "모두 없을 때만" 묶음은 Buildings·Structures·Vegetation 셋.

### 14.3 적용 결과 (레이드 씬, 리뷰 반영 뒤 `Rebuild` + `Apply Asset Swap`)
- 건물 80/80, 소형 150/150, 승용차 30/30, 큰 차량 10/10. 구역별 — POI-HWY 건물 8·소형 12·승용차 7·큰 2 / REF 6·10·2·1 / VIL 14·12·3·1 / PYL 4·8·1·0 / RES 6·8·2·0 / ROUTE 30·70·8·6 / OUTSKIRT 12·30·7·0.
- 나무 1,500(길목 900·외곽 500·마을 43·저수지 57, 군락 1,125), 통나무 120, 덤불 400. 포아송 후보 23,138(5m).
- 풀: 디테일 1024²(패치 32) 2층, 덮은 셀 391,773 / 걷는 지형 979,431 = 40.0%, 포기 276,084, 보이는 거리 50m.
- 새 엄폐 지점 454(생성 668 − 간격 4m 214), 전체 1,060 / 1,200. 내부 막기 볼륨 151. 구조물 자리 NavMesh 구멍 80/80·190/190.
- 굽기 전·뒤 엄폐 606/606·적 34/34(스폰 도달 34)·순찰 구간 28/28(한 번의 질의)·적 근거리 경로 665/665(한 번의 질의)·상자 21/21(도달 21), 스폰 3 → 활성 탈출구 9경로 연결, 순찰 경로 최대 증가 0.0%.
- 컬 LODGroup 270(그림자 끔 68 — 높이 < 1.5m), LOD 기준 FOV 60°·lodBias 2(PC).
- `Rebuild`는 처음 적용과 배치가 조금 다르다: 생성기 입력인 "기존 경로 통로"를 그때의 NavMesh(이미 촘촘한 것)에서 뽑기 때문 — 같은 씬·NavMesh 입력이면 같은 결과(결정적).
- NavMesh 굽기 약 11~15초(변화 작음), 파일: NavMesh 1.06 → 2.74MB, TerrainData 0.55 → 2.52MB, 레이드 씬 5.7 → 15.3MB(스킨 포함, 산포 프리팹 인스턴스 2,020 × 약 70줄이 대부분 — 설계 추정 +3MB보다 크다).
- 에셋 교체: 구조물 스킨 158 → 317(새 건물·차량·컨테이너·벽), VM 프리팹 변화 없음.
- 멱등: `Apply Map Density` 두 번째 실행·`Apply Asset Swap` 재실행·`Apply Map Polish` 재실행·미리보기 렌더 뒤 모두 `Assets/_Project`·`ProjectSettings` 1,092개 파일 바이트 동일(스킨 폴더 제외).

### 14.4 테스트
- EditMode **2081/2081**(새 `MapDensityContractTests` 10·`MapDensityLayoutTests` 11 포함, 리뷰 수정 뒤 재실행 같음). PlayMode **213/213**(2회째 전체), 새 `MapDensitySceneSmokeTests` 4.
- 1회째 전체 PlayMode 211/213 → 고친 것:
  - `RaidMapTests`(스폰 → 활성 탈출구 Complete): 위 A* 노드 한도로 SP-N → EXT-02가 PathPartial → **완화 근거:** 이 테스트가 보는 것은 "플레이어가 걸어서 탈출구에 닿는가"(연결성)이고
    플레이어는 NavMesh를 쓰지 않는다 → 배치 확인과 같은 `NavPathProbe.TryConnected`로 바꿨다(복제 코드 없음). 진짜로 끊긴 섬은 여전히 실패 — PlayMode `NavPathProbe_DisconnectedIslands…`(작은 NavMesh 두 덩이)가 회귀를 막는다.
  - `ExtractionFlowTests.Ext03…`: 1회째 전체 실행·선택 실행에서 서로 다른 증상(씬이 Hideout, 상태 TooEarly 유지)으로 실패, 단독 실행 6/6·2회째 전체 통과 — 테스트 순서/타이밍 의존 흔들림으로 보고 코드는 고치지 않았다(남은 위험).
- `RaidPerformanceTests` 엄폐 질의: 맵 밀도 직후 187~194µs(기준 200µs, 이전 42~46µs). 원인 = 선택 후보마다 쏘는 긴 시야 레이가 숲을 지나 모든 충돌을 모으는 비용.
  `PhysicsLineOfSight.IsBlocked`가 가장 가까운 충돌을 먼저 보고(무시 대상이 아니면 바로 "막힘" — 결과 같음) 필요할 때만 전체 수집 → **68~96µs**. 기준값은 바꾸지 않았다.
  (리뷰 M2) `CanSee`도 같은 방식 — 예전에는 충돌이 버퍼 8개를 넘으면(숲) 가장 가까운 가림막을 놓쳐 나무 너머가 보일 수 있었다. 동등성 PlayMode `LineOfSightProbeTests`
  (가장 가까운 것이 무시 대상 + 뒤 벽 → 막힘, 무시 대상만 → 안 막힘, 트리거만 → 안 막힘, 충돌 11개에서 가장 가까운 나무 인식).
- `PaletteConsistencyTests.BlockoutMap_CoversEveryM4Shape`: `M4MapSetup.Shape`가 public이 되어 리플렉션 플래그에 Public 추가.

### 14.5 미리보기·성능 (이 PC Intel HD 620 — 기록만, 판정은 GTX 1660)
- 미리보기: `MapDensityPreview.RenderViews`(배치, 1920×1080, 눈높이 1.6m, 플레이어 카메라 설정·포스트) 8시점 전·후(스폰 3·마을·작은 마을 T1·중앙·죽은 숲 가장자리).
- IL2CPP 개발 빌드 1080p 전체 화면 스모크 90초(폭풍 Peak 포함), `[DevDiagnostics]` 10초 창 중앙값(첫 창 제외), 전(오늘 오전 빌드 사본)→후→전→후:

| 빌드 | 1회 | 2회 | 드로콜 / 삼각형 / SetPass |
|---|---|---|---|
| 적용 전 | 30.1ms | 31.0ms | 약 1,880 / 47만 / 80 |
| 적용 후(스킨 포함) | 34.6ms | 35.1ms | 약 2,330 / 70만 / 108 |

  → **약 +4.3ms(+14%)**, 설계 목표 "+3ms 이내"(기록용)를 넘는다. 드로콜 +450·삼각형 +22만 — 설계 추정(+300~400·+10~20만)보다 약간 크다.
  넘칠 때 조정 순서(§8.5): 풀 밀도·거리 → 나무 컬 거리(260m) → 나무 수 → 폭풍 컬링(§8.3) → GRD(§8.2). GTX 1660 실측 뒤 정한다. 빌드 1.78GB·13분, 스모크 4회 모두 8/8 통과.

### 14.6 남은 위험
- 런타임 `NavMeshAgent`의 맵 횡단 긴 경로는 노드 한도로 부분 경로로 시작할 수 있다(확인 함수만 이어 찾기). 적은 POI 안 짧은 경로가 대부분이라 영향이 작을 것으로 보지만 플레이 확인 필요.
- 풀 그림자: 프리팹 렌더러만 그림자 끔 — Terrain Detail 인스턴싱이 실제로 그림자를 드리우는지 에디터 확인 필요. 풀은 50m에서 끊겨 보이고 셀 단위 배치라 격자 느낌(값 조정 = `MapDensityConfigSO`, Refresh 메뉴).
- 성능 +4.3ms(이 PC). 씬 파일 15.3MB(산포 인스턴스). 나무 콜라이더가 많아 탄·시야 레이가 숲에서 자주 막힌다(의도) — 저격수 시선은 3D 확인으로 보장.
- 맵 횡단 긴 경로는 런타임 `NavMeshAgent`도 부분 경로(위 §14.2) — 적에게 그런 경로를 주는 기능(예: 맵 반대편 소리 추적)을 넣으면 실제 해결이 필요하다.
- `Ext03` PlayMode 흔들림(§14.4). ③에서 상자 112·트렁크 67을 더하면 그 주변 풀 제외를 위해 `Refresh Grass & Culling`(또는 ③ 적용 끝에서 Refresh)이 필요.
- ③ 인터페이스: `MapDensitySceneApply.PassengerCars(scene)` → `PassengerCar`(래퍼·Block·길이 축·길이/폭·윗면/지면 높이·구역·새 차량 여부·`RearGroundPoint`).
  데이터 쪽은 `MapDensityLayout.StructurePlan.IsPassengerCar`·`Zone`, 구역 함수 `LootZoneRules.ZoneOf`, 활성화 `LootActivationPlanner.Plan`, 시드 `RaidStarted.LootSeed`·`SeedMath`.

### 14.7 리뷰 반영 (code-reviewer, 2026-10-29)
- M1 적 경로 = 한 번의 질의(§14.2), M2 `CanSee`·`IsBlocked` 가장 가까운 충돌 먼저 + 동등성 테스트(§14.4).
- M3 TerrainData 롤백: 풀·LOD·그림자(`Refresh`)를 배치·굽기·확인이 모두 성공한 뒤로 옮겼다 — `Build`는 TerrainData를 건드리지 않아 TerrainData 바이트 백업·복원을 없앴다.
  `Refresh`가 던지면 씬은 저장되지 않고 메모리의 TerrainData는 저장 없이 배치 프로세스와 함께 버려진다. 산포 프리팹 LOD·그림자 갱신은 SO 파생값이라 되돌리지 않는다(다음 실행이 같은 값으로 맞춘다).
- L6 진단: 이어 찾은 경로는 `Complete(연결 — 한 번 질의는 PathPartial, 이어 찾기 n)`·이어 붙인 전체 코너 수로 적는다.
- L8 `LootActivationPlanner.GroupKey` = `"{구역 id 길이}:{구역 id}|{종류}"` — 구분자가 id 안에 있어도 키가 겹치지 않는다(③ 전이라 시드 변경 영향 없음).
- 5(설계 확인): **비율 0이어도 지점이 있으면 `minActivePerZone`(기본 1)이 적용된다. 구역을 끄려면 `LootSpawnConfigSO.zones`에서 그 구역을 뺀다**(구역 없음 = 활성 0). EditMode 테스트로 고정.
- `NavState`는 맵 개선과 공용이라 맵 개선 `Rebuild`도 같은 적 경로 규칙(한 번의 질의)을 받는다(이전보다 엄격).
