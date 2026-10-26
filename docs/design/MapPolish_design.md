# 맵 개선 설계 — Crimson Basin 밝기·광원·랜드마크 (2026-10-26)

사용자 피드백(2026-10-26, v0.1.0을 보고): ① "맵이 너무 어둡다. 광원을 몇 개 추가" ② "맵이 허허벌판이다. 랜드마크를 더 추가".
③ 에셋스토어 모델 교체는 나중 — 이번에는 하지 않지만, 랜드마크를 **프리팹 단위**로 만들어 나중에 갈아끼우기 쉽게 한다.
마일스톤이 아니라 MVP 뒤 폴리시 작업 하나다. 결정 사유는 `docs/CHANGELOG_GDD.md` "맵 개선 설계 결정".

## 1. 범위
- 하는 것: 레이드 씬 전체 밝기(태양·환경광·노출·맑을 때 안개) 상향, POI 광원(불타는 드럼통·깨진 가로등) 실시간 점광원(그림자 없음),
  큰 랜드마크 8개(프리팹), POI 사이 중간 크기 구조물·잔해(블록아웃 도형 + 엄폐 지점), NavMesh 다시 굽기와 경로·배치 확인.
- 안 하는 것: 실내·건물 진입(§1·§17 — 전부 속을 채운 외곽 구조물), 외부 에셋 다운로드, 광원 깜박임 등 런타임 코드, 라이트맵 굽기,
  재 폭풍 규칙 변경(§8.3 Peak 시야 30m — `AshStormConfigSO.stormVisibilityM` 그대로, 폭풍 안개 밀도는 맑을 때 값과 무관), 낮/밤.

## 2. 밝기 (AtmosphereConfigSO, 원본 수치 = SO)
지금 어두운 원인: 태양색이 순수 `Ember`(초록·파랑 성분이 거의 0)에 고도 8°(평지 조도 sin 8° ≈ 0.14), 환경광이 `Rust_Red`·`Dried_Blood`·`Ash_Black` × 1.0.
→ 붉은 것 말고는 거의 검게 보인다.

새 필드(파일에 없을 때만 기록, MVP 마무리와 같은 "명시 기록" 방식):
| 필드 | 뜻 | 기본값(코드 초기값) |
|---|---|---|
| `postExposureEv` | Color Adjustments Post Exposure(EV) | 0 (이전과 같음) |
| `sunLightBoneMix` | 태양 라이트 색 = lerp(`sunLightToken`, `Bone`, 이 값) | 0 (이전과 같음) |
| `lightingRevision` | 밝기 개정 번호. 맵 개선 메뉴가 1 미만일 때만 아래 표 값을 쓰고 1로 올린다 | 0 |
| `fireLight`, `lampLight` (`MapLightStyle`) | 점광원 종류별 토큰·Bone 섞임·세기·범위 | 아래 §3 |
| `maxMapLights` | 레이드 씬 점광원 상한(확인용) | 16 |

밝기 개정 1(TODO(GDD-?) — §10에 수치 없음, 에디터 미리보기 렌더 3안 중 고른 최종값, §8):
| 필드 | 이전 | 개정 1 |
|---|---|---|
| `sunLightIntensity` | 0.9 | 2.4 |
| `sunLightBoneMix` | 0 | 0.4 |
| `sunElevationDeg` | 8 | 12 (여전히 "낮게 걸린" 태양) |
| `ambientIntensity` | 1.0 | 3.5 |
| `ambientGroundToken` | Ash_Black | Dried_Blood |
| `postExposureEv` | 0 | 0.8 |
| `clearVisibilityM` | 450 | 600 (멀리 랜드마크가 보이게) |

`lightingRevision`을 쓰는 이유: 기존 필드는 이미 파일에 있어 "없을 때만 기록"으로는 바꿀 수 없고, 매번 덮어쓰면 기획 조정값이 날아간다.
개정 번호가 1 이상이면 메뉴는 값을 건드리지 않는다(멱등, 조정 보존).
§10.2 고정값(채도 -30·Film Grain 0.3·Vignette 0.35·Bloom 낮음)·안개 색·하늘 그라데이션은 그대로 — "검붉은 황혼" 유지.
코드 변경: `M6ArtSetup.EnsureVolumeProfile`이 `postExposure`를, `M6VisualSceneApply.ApplySun`이 Bone 섞임을 반영(둘 다 기본값이면 이전과 같음).

## 3. POI 광원
- 실시간 Point Light, **그림자 없음**, 라이트맵 없음(Realtime). PC 렌더러가 Forward+라 개체당 4개 제한은 무관.
- 종류(TODO(GDD-?) 값): 불(`fireLight`: Ember, Bone 섞임 0.15, 세기 10, 범위 12m), 가로등(`lampLight`: Bone, 섞임 0, 세기 8, 범위 16m).
- 소품 프리팹(자리표시): `LP_FireBarrel`(드럼통 + 위쪽 Ember 발광 원판), `LP_StreetLamp`(기울어진 기둥 6m + 팔 + Bone 발광 등). 라이트는
  프리팹 밖(씬 래퍼의 자식 "Light")에 두고 매 실행 SO 값으로 다시 맞춘다 — SO가 원본, 프리팹을 모델로 바꿔도 라이트는 남는다.
- 발광 머티리얼 2개 추가(팔레트 폴더, `PaletteMaterialMap`): `M_EmberGlow`(Ember, 발광 1.6), `M_LampGlow`(Bone, 발광 1.5). Bloom(낮음)이 받는다.
- 개수: POI 13 + 랜드마크 1(추락기 잔불) = **14개** ≤ `maxMapLights` 16. 위치는 `MapPolishLayout.LightProps`(§5).
- 신호등(송전탑·라디오 마스트·플레어 스택 꼭대기)은 라이트 없이 발광 머티리얼만 — 멀리서 방향 표지, 비용 0.

## 4. 랜드마크·중간 구조물
### 4.1 랜드마크 8개 (프리팹 `Assets/_Project/Prefabs/Landmarks/LM_*.prefab`)
프리미티브 블록아웃, 팔레트 머티리얼, 콜라이더 + `GroundSurface`, 원점 = 지면 중심, 기초가 지면 아래 3m까지 내려가 경사 틈을 막는다.
프리팹은 **없을 때만** 만든다(같은 경로에 모델 프리팹을 덮어쓰면 유지). 씬에는 래퍼 `LM_{이름}_{nn}` 아래 프리팹 인스턴스 "Model" + 엄폐 지점.
**모델 교체 절차(리뷰 M4):** ① 같은 경로(`LM_*.prefab`)에 모델 프리팹을 덮어쓴다(원점 = 지면 중심, 콜라이더 + `GroundSurface`, 머티리얼은 팔레트 폴더 —
PlayMode `MapPolishSceneSmokeTests`·`RaidVisualSmokeTests`가 팔레트 머티리얼·GroundSurface를 요구하므로 다른 머티리얼을 쓰면 그 단언을 함께 고친다).
② 모양이 크게 바뀌면 `MapPolishPrefabs`의 발자국·엄폐 상자·광원 위치를 맞춘다. ③ `Ashfall/Setup/Map Polish/Rebuild Map Polish (Raid Scene)`로 그룹을 지우고
다시 배치·겹침 확인·NavMesh 굽기를 한다(그냥 Apply는 그룹이 있으면 배치·굽기를 건너뛴다).

| 프리팹 | 모양 | 높이 | 위치(XZ) | 역할 |
|---|---|---|---|---|
| `LM_WaterTower` | 다리 4 + 원통 물탱크 | 33m | (−385, 240) | 마을·저수지·EXT-04 사이 서쪽 표지 |
| `LM_BrokenPylon` | 기울어진 격자 송전탑 + 꺾인 꼭대기 | 약 30m | (60, 200) | 중앙 북쪽, 송전탑 언덕 방향 |
| `LM_CrashedPlane` | 동체 원통 + 부러진 날개 + 꼬리 날개 | 9m | (−60, 60) | 맵 중앙, 엄폐 + 잔불 광원 |
| `LM_Billboard` ×2 | 기둥 2 + 판 14×6m | 16m | (150, −225), (−285, −195) | 고속도로 양 끝 |
| `LM_BrokenOverpass` | 교각 3 + 상판 2 + 땅에 떨어진 상판 | 10m | (−160, −150) | 고속도로 북쪽 끊어진 고가 |
| `LM_FlareStack` | 굴뚝 원통 + Ember 발광 끝 | 45m | (395, 30) | 정유소 북동, 동쪽 표지 |
| `LM_RadioMast` | 가는 철탑 + Ember 발광 비컨 | 50m | (60, 380) | 북쪽(EXT-03 방향) 표지 |

### 4.2 중간 크기 구조물 (POI 사이 길목, Environment/`MapPolish_Structures`)
M4 블록아웃 생성기(`M4MapSetup` BlockoutBuilder)를 그대로 써서 지면에 맞추고 엄폐 지점을 만든다(이름 `{Shape}_{nn}` → M6 표로 칠함).
새 도형 `Container`(6×2.6×2.4m, 녹슨 금속, High 엄폐 앞뒤) 하나를 더한다(`PaletteMaterialMap` 규칙 추가). 군집 12곳 × 2~3개(약 30개):
무너진 담(HighWall/LowWall), 컨테이너, 버스 잔해(Vehicle 10×3×2.6), 속을 채운 창고(House 6×3×5), 잔해 더미(Rubble).
위치는 `MapPolishLayout.Structures`.

## 5. 배치 데이터·메뉴
- `EditorTools/MapPolishLayout.cs`(레벨 데이터, 게임 수치 아님): 랜드마크·구조물·광원 소품 위치, 회피 반경, 순수 검사 함수(EditMode 테스트).
- `EditorTools/MapPolishPrefabs.cs`: 랜드마크·소품 프리팹 부품 표와 생성("없을 때만").
- `EditorTools/MapPolishSceneApply.cs`: 레이드 씬에 적용. 그룹(`MapPolish_Landmarks`·`MapPolish_Structures`·`MapPolish_Lights`, Environment 자식)이
  **없을 때만** 만들고 NavMesh를 다시 굽는다. 광원 값은 매 실행 SO에서 다시 맞춘다(같으면 바뀌지 않음). 확인 실패 = 예외(씬 저장 안 함).
- `EditorTools/MapPolishSetup.cs`: 메뉴 `Ashfall/Setup/Apply Map Polish`(배치 `MapPolishSetup.ApplyAll`).
  순서: M7 에셋이 없으면 `M7SceneSetup.ApplyAll` → 밝기 개정·새 필드 기록 → `M6ArtSetup.CreateAssets`(볼륨·하늘·발광 머티리얼 파생물) →
  프리팹 → 레이드 씬 열기 → `M6VisualSceneApply.ApplyRaid`(밝기 반영) → `MapPolishSceneApply.ApplyRaid` → 바뀌었으면 저장.
- `M4SceneSetup.CreateRaidScene`은 M7 적용 다음 `MapPolishSceneApply.ApplyRaidIfAssetsExist`를 불러, 씬을 다시 만들어도 빠지지 않게 한다.
- `M6VisualSceneApply.ApplyBlockout`: 프리팹 인스턴스 모델을 가진 구조물은 칠하지 않는다(프리팹이 자기 머티리얼을 가진다).

## 6. 확인 (적용 중 자동, 실패 = 예외)
1. 배치 전: 새 콜라이더가 기존 콜라이더(지형 제외)와 겹치지 않는다. 엄폐 지점·상자·적 시작·순찰 지점에서 2.5m, 스폰·탈출에서 25m, 전원 스위치에서 10m,
   도로 띠에서 2m 떨어진다. 저격수 퍼치 → 감시 지점 시선(M7 SNP 2)을 새 콜라이더가 막지 않는다.
2. 굽기 뒤: 스폰 3 → 활성 탈출구 경로 전부 Complete(`M4MapSetup.ValidateNavigation`), 굽기 전 NavMesh 위에 있던 엄폐 지점은 굽기 뒤에도 0.5m 안,
   새 엄폐 지점도 NavMesh 위, 적 34명 시작점·순찰 지점 NavMesh 위, 순찰 구간 경로 Complete, 상자 21개 옆 NavMesh(2m).
   (리뷰 반영) 기준 스폰(SP-W)에서 적 시작점·상자까지 경로 Complete 회귀 없음, 새 엄폐 지점은 기준 스폰에서 닿을 수 있어야(아니면 뺀다),
   구조물 자리가 NavMesh 구멍(중심 지면 0.3m 안 NavMesh 없음). 새 그룹 루트는 NavMeshModifier(Not Walkable) — 윗면 섬 없음.
   엄폐 지점은 드러난 높이(상자 윗면 − 그 지점 지면)로 High → Low(≤ 1.3m) 또는 뺀다(< 0.9m), peek 자리도 콜라이더 밖.
   실패하면 만든 그룹을 지우고 NavMesh 에셋을 굽기 전 바이트로 되돌린다. 그룹이 이미 있는데 구멍이 없으면(옛 NavMesh) 예외 — Rebuild 메뉴.
3. 점광원 수 ≤ `maxMapLights`, 전부 그림자 없음.
EditMode: 배치 데이터 검사(POI·스폰·탈출·도로·적 경로·저격 시선·광원 상한), 새 필드·머티리얼 표. PlayMode: 레이드 씬 랜드마크 8·광원 14·그림자 없음·프리팹 인스턴스·
엄폐 지점 NavMesh, 기존 시각 테스트의 태양색 기대값을 Bone 섞임 반영으로 수정.

## 7. 성능
이 PC(HD 620) 1080p 폭풍 Peak 중앙값이 이미 약 32.6ms(GPU 병목). 같은 방법(IL2CPP 개발 빌드, `-ashfallSmoke … -ashfallSmokeRaidSec 90`,
`[DevDiagnostics]` 줄)으로 적용 전(현재 커밋 빌드)·후를 재고 Rising(평시에 가까움)·Peak 구간 악화 폭을 보고한다.
→ 오래된 빌드 exe는 Windows 스마트 앱 컨트롤에 막혀, 사본에서 적용 전(4f114f1)·후 개발 빌드를 새로 만들어 번갈아 쟀다(§8). 에디터 1080p 렌더 비교도 함께.
광원은 그림자 없음·범위 14~16m·14개라 한 화면에 몇 개만 영향, 랜드마크는 정적 배칭. 크게 나빠지면 광원 범위·개수를 SO에서 줄인다.

## 7.1 사람 확인 (에디터·플레이)
1. 레이드 씬: 평시에 지형·적·엄폐물이 확실히 보이는지, 여전히 검붉은 황혼인지(너무 밝거나 회색이면 `postExposureEv`·`ambientIntensity` 낮추기).
2. 각 POI 광원이 테마에 맞는지(불 = Ember, 가로등 = Bone), 깜박임이 없어도 괜찮은지.
3. 스폰 3곳에서 둘러봐 랜드마크(급수탑·송전탑·플레어 스택·라디오 마스트)로 방향을 잡을 수 있는지.
4. 재 폭풍 Peak에서 30m 시야가 그대로인지.
5. 새 구조물 근처에서 적이 끼거나 엄폐를 이상하게 쓰지 않는지.

## 8. 구현 메모
- 사본 `Ashfall_mp`에서 반복 → 원본에서 `MapPolishSetup.ApplyAll` 실행(통합). 두 번째 실행에서 바뀐 파일 0개(53개 바이트 동일).
- 배치 수정: 고속도로 드럼통 (−125, −296) → (−125, −287)(도로 띠 위), 끊어진 고가 (−140, −150) → (−160, −150)(M4 흩어진 잔해 Vehicle_55 엄폐 지점과 1.3m).
- 적용 결과(리뷰 수정 뒤 Rebuild): 랜드마크 8, 구조물 29(엄폐 104), 광원 소품 13, 새 엄폐 지점 112(다른 콜라이더 안 3개·드러난 높이 부족 3개(플레어 스택 기초) 뺌),
  내부 막기 Not Walkable 볼륨 46개, 구조물 자리 NavMesh 구멍 29/29, NavMesh 굽기 약 17초. 적 34·상자 21 모두 기준 스폰(SP-W)에서 도달.
- 리뷰 중 발견: 사람 키보다 높은 속 찬 상자(컨테이너·창고·버스) 안쪽 지면에 NavMesh 섬이 생겼다(Recast는 닫힌 메시 속을 채우지 않는다) → 볼륨으로 막음.
  M4 기존 블록아웃(집 3.5m·탱크 등)도 같은 섬이 있을 수 있다 — 이번 범위 밖, 후속.
  굽기 전·뒤 모두 엄폐 494/494·적 34/34·순찰 구간 28/28·상자 21/21, 스폰 3 → 활성 탈출구 9경로 Complete(길이 533~1041m).
- 밝기: 에디터 배치모드에서 플레이어 카메라를 렌더한 미리보기(10시점)로 3안 비교 — A(태양 2.4·Bone 0.4·환경광 ×3.5·노출 +0.8) 채택.
  B(노출 +1.2)는 하늘이 들뜨고, C(환경광 ×4.5)는 대비가 사라졌다. 점광원은 처음 값(불 4·가로등 3)으로는 `Dried_Blood` 지면에서 거의 안 보여 10·8로 올렸고,
  발광 배율 3·2는 자기 광원을 받아 하얗게 날아가 1.6·1.5로 내렸다. 폭풍 Peak(30m 안개) 미리보기에서 시야 규칙 그대로.
- 성능(빌드, 최종 정리와 같은 방법 — 1080p 전체 화면, 스모크 90초, 전→후→전→후): Rising 창 27.4/27.4 → 29.5/29.6ms(+2.2ms),
  Peak 창(4~9) 중앙값 29.5·27.6(전) → 29.9·29.9ms(후), 평균 +1.4ms(약 +5%). 같은 날 측정이 최종 정리(32.6ms)보다 낮아 같은 빌드 쌍끼리만 비교한다.
  에디터 1080p 렌더(같은 10시점): 평시 42.7 → 44.3ms, Peak 안개 43.2 → 44.6ms. 점광원만 끈 측정과 켠 측정의 차이는 잡음 범위 — 증가분은 구조물·밝기 쪽.
