# M6 "아트 & 오디오 패스" 설계 (unity-architect) — 준비안

기준: docs/GDD.md §0, §1(디자인 기둥 3 "소리가 곧 정보"), §4.1, §4.3(STAT-RAD), §7(감각), §8.1, §8.3, §10, §11, §12, §13, §14(PaletteSO), §15 M6·M7, §17, §18(`AshStorm`).
M1~M5 결정(CHANGELOG_GDD — 특히 M3 "상태 색 표시는 M6에서 끈다", M4 "§8.3 동적 환경은 M6"), `M4_design.md`·`M5_design.md`(형식·구현 메모), 현재 코드(cb95547)를 전제로 한다.
**상태: 준비안.** §5 질문은 오케스트레이터가 ★추천안으로 확정한다는 전제로 썼다. 답이 다르면 표시된 곳(⟨Qn⟩)만 바꾼다.
여기 없는 것은 만들지 않는다. 모호하면 `// TODO(GDD-?) Qn` + 가장 단순한 구현.

**외부 에셋 금지(사용자 지시).** 다운로드·Asset Store 에셋을 쓰지 않는다. 소리·텍스처·머티리얼·볼륨 프로필은 전부 에디터 스크립트가 만든 **자리표시**이고,
실제 에셋으로 바꿀 자리를 §2.3 표에 적는다. 씬·`.asset` YAML은 손으로 쓰지 않는다(메뉴 + `-executeMethod`).

현재 코드에서 M6이 기대는 사실(확인함):
- `Ashfall.Audio` asmdef는 있고(참조 Core) 스크립트가 없다("will not be compiled" 경고의 원인). M6이 처음 채운다.
- 발소리 소음은 `PlayerMotor`가 `FootstepNoiseModel`(0.5초 펄스, AI 청각용)로 `NoiseEmitted(Footstep)`를 낸다. 사격은 `WeaponController`가 `NoiseEmitted(Gunshot)`.
  **적의 사격은 아무 이벤트도 내지 않는다**(`EnemyWeapon.Tick` → `IProjectileService.Fire`만, M3 "적의 총성은 다른 적이 듣지 않는다"). 적 발소리도 없다.
- AI 시야 거리는 `SightCone.EffectiveRange(archetype.sightRange, posture, config)` 한 곳(`EnemyAgent.Sense`), 서비스 조회는 `AiDirector.ResolveServices`(재시도 주기).
- 레이드 시간은 `RaidController.ElapsedSec`/`Session`(scaled deltaTime, 일시정지 중 멈춤, `AdvanceTime` 디버그). `RaidController`에는 손대지 않아도 폭풍 시각을 정할 수 있다.
- 레이드 씬(`M4SceneSetup`)은 `NewScene(DefaultGameObjects)`의 Directional Light를 남기고(색 "M6"), 블록아웃은 기본 머티리얼의 프리미티브(구조물 이름 `{Shape}_{nn}`),
  Terrain은 TerrainLayer 없음, `RenderSettings`(안개·하늘·환경광)는 기본값이다. 플레이어 카메라는 `new GameObject(Camera, AudioListener)` — **URP 포스트 처리가 꺼져 있다**
  (`UniversalAdditionalCameraData.renderPostProcessing` 기본 false).
- URP: `PC_RPAsset`의 볼륨 프로필 = `Assets/Settings/SampleSceneProfile.asset`(Bloom·Vignette·Tonemapping), 전역 기본 = `DefaultVolumeProfile.asset`. 둘 다 M6에서 건드리지 않는다.
- 레이캐스트는 모두 `QueryTriggerInteraction.Ignore`(투사체·시야·상호작용) → 트리거 콜라이더는 전투·상호작용에 영향이 없다.
- `Art/UI/Ashfall.uss`는 이미 §10.1 토큰 7개를 CSS 변수로 쓴다(UI 색 변경 불필요, 일관성 테스트만).
- 매니페스트에 `com.unity.modules.audio`·`com.unity.modules.particlesystem`이 **없다** — lock에서 depth 1, 의존하는 패키지는 `com.unity.timeline` 하나뿐.
- 테스트 asmdef(EditMode·PlayMode)는 `Ashfall.EditorTools`와 URP 어셈블리를 참조하지 않는다.

---

## 0. 범위

**만든다 (M6 AC)**
1. §10 아트 — 팔레트·포스트프로세싱·하늘·재 파티클 (AC1)
   - `PaletteSO`(Core, §14) — §10.1 토큰 7개. 모든 머티리얼 색은 팔레트 토큰에서 온다(에디터 동기화 ⟨Q6⟩).
   - 레이드 씬 Global Volume: Color Adjustments(채도 -30, 붉은 틴트), Film Grain 0.3, Vignette 0.35, Bloom 낮음, Tonemapping Neutral.
   - "Height Fog(Dried_Blood)" → `RenderSettings` Exponential² 거리 안개(Dried_Blood) + 하늘 지평선 띠를 같은 색으로 ⟨Q3⟩.
   - 하늘: 생성한 파노라마 텍스처(천정 Ash_Black → 지평선 Rust_Red, 낮게 걸린 흐린 태양) + `Skybox/Panoramic` ⟨Q4⟩, 같은 방향의 Directional Light(태양), Trilight 환경광.
   - 재 파티클: 플레이어 주변 반경 30m 상시, **Particle System**(VFX Graph 대신 ⟨Q2⟩).
   - 블록아웃·지형·적·탈출 구역·스위치 머티리얼을 팔레트로 칠함(GUID 유지) ⟨Q7⟩. 저수지 바닥에 물웅덩이(물 발소리용).
   - 적 머리 위 상태 색 표시 끄기(`AiConfigSO.showStateIndicator = false`, M3 결정) ⟨Q8⟩.
2. §11 오디오 — 3D 공간음, 지면별 발소리, 총성 거리 레이어, 환경음 (AC2)
   - 발소리: 지면 재질 흙/금속/잔해/물(`SurfaceKind`) × 걸음 종류. 플레이어·적 모두 ⟨Q11~Q13⟩.
   - 총성: 거리 3단계(근/중/원거리 메아리) 레이어, 무기 분류별 세트. 플레이어·적 모두 ⟨Q14·Q15⟩.
   - 환경음: 바람(상시, 폭풍 때 커짐), 멀리서 무너지는 금속(무작위 간격), 가이거 계수기(재 폭풍 방사능률에 비례 ⟨Q22⟩).
   - 레이드·메인 메뉴 BGM 없음. 은신처만 자리표시 BGM ⟨Q18⟩.
   - AudioSource 풀(할당 없음), 일시정지 시 소리 정지 ⟨Q19⟩.
3. §8.3 재 폭풍 (AC3)
   - 레이드마다 1회, 무작위 시각 ⟨Q23⟩, 최대 강도 3분 + 전환 ⟨Q24⟩.
   - 시야 30m: 렌더 안개 + **AI 시야 상한** ⟨Q26⟩. 재 파티클 증가, 하늘·태양 어두워짐, 바람·폭풍 소리, 가이거.
   - 방사능 +1/s: 플레이어 누적량만 쌓는다(효과·UI 없음, 방독면·해독제·100 효과는 M7) ⟨Q27⟩.

**만들지 않는다**
- M7: 무기 5종·무기 교체, AI-SNP·MUT·BOS, 제작대, 방사능 효과 전체(STAT-RAD 100 → 최대 HP -20%, 해독제, 방독면, 방사능 구역·정유소 Toxic 표시), 탈수·허기, 성능 목표 최종 판정.
- VFX Graph 에셋, 커스텀 높이 안개 렌더러 기능, AudioMixer ⟨Q20⟩, 소리 전달 지연·차폐 ⟨Q16·Q17⟩, 장전·빈 탄창·피격·사망·착지·UI 효과음, 음량 설정 화면.
- 실제 모델·텍스처·폰트(§12 스텐실 폰트 포함) — 사용자 에셋 목록(§2.3)에만 적는다. 확인 씬(M1~M3) 아트 적용 ⟨Q9⟩.
- 은신처 3D 공간, 메인 메뉴 배경 연출(카메라 배경색만 ⟨Q29⟩).

---

## 1. 아키텍처

### 1.1 경계 요약

새 asmdef 없음. 기능끼리 참조 추가 없음. 싱글톤은 계속 `GameManager` 하나(Audio·폭풍 관리자는 씬 오브젝트, 정적 인스턴스 없음 — `AiDirector`·`RaidController`와 같은 방식).

| 필요한 것 / 주는 것 | 상대 | 방법 | 이유 |
|---|---|---|---|
| 팔레트 토큰 | EditorTools(머티리얼·볼륨·하늘), Raid(폭풍 안개 색), 테스트(USS) | ★Core `PaletteSO` + `PaletteToken` (§14) | 여러 기능이 같이 쓰는 데이터 → Core |
| 발소리 발생 | Player·AI → Audio | ★이벤트 `FootstepTaken(위치, 걸음, 출처)` | 알림(1:N, 응답 불필요). 지면 판정은 Audio가 한다(들리는 거리 밖이면 레이캐스트도 안 함) |
| 발소리 리듬(보폭) | Player·AI 공용 | ★Core 순수 `StrideCadence` ⟨Q12⟩ | 같은 규칙을 두 기능이 쓴다(M3 `WeaponModel` 이동과 같은 원칙) |
| 총성 발생 | Weapons(플레이어)·AI(적) → Audio | ★이벤트 `ShotFired(위치, 무기 정의, 출처)` ⟨Q14⟩ | AI 청각용 `NoiseEmitted`는 그대로(적 총성은 적이 못 듣는 M3 규칙 유지) |
| 지면 재질 | 레벨(EditorTools가 배치) → Audio | ★Audio `GroundSurface` 컴포넌트 + `SurfaceKind` ⟨Q11⟩ | 소비자가 Audio뿐 → Core에 두지 않는다. EditorTools는 Audio를 참조(1단계 asmdef) |
| 재 폭풍 상태(강도·시야·방사능률) | Raid → AI·Player·Audio | ★Core 서비스 `IAshStormInfo`(Raid 등록, 매 틱 동기 조회) | 감각 주기·프레임마다 현재 값이 필요. 늦게 시작한 구독자도 현재 상태를 읽어야 함 |
| 재 폭풍 단계 전환 | Raid → Audio(폭풍 루프 시작·정지) | ★이벤트 `AshStormChanged(단계)` | 전환 순간 알림. 강도는 서비스로 읽는다 |
| 일시정지 | Core(GameFlow) → Audio | `GameManager.Flow.IsWorldPaused` 읽기 → `AudioListener.pause` ⟨Q19⟩ | timeScale 주체는 계속 GameFlow 하나. Audio는 소리 상태만 바꾼다 |
| 청취자 위치 | Player → Audio | 기존 `IPlayerTarget.EyePosition` | 카메라의 AudioListener와 같은 위치 |
| 안개·하늘·태양·재 파티클 조절(폭풍) | Raid 내부 | `AshStormPresenter`(Raid/Runtime)가 `RenderSettings`·파티클을 바꾼다 | §13.2 "Raid: 재폭풍". URP 패키지 참조 불필요(볼륨은 폭풍에 안 씀) |

### 1.2 이벤트·서비스 목록 (M6 추가분, 전부 Core)

| 타입 | 종류 | 발행/등록 | 구독/조회 | 빈도 |
|---|---|---|---|---|
| `FootstepTaken { Vector3 Position; FootstepGait Gait; SoundSource Source; }` | 이벤트 | Player `PlayerMotor`(보폭마다, 접지 중), AI `EnemyAgent`(보폭마다, 살아 있고 NavMesh 이동 중) | Audio `AudioDirector` | 적 20명 ≈ 40/s 이하 |
| `ShotFired { Vector3 Position; WeaponDefinitionSO Weapon; SoundSource Source; }` | 이벤트 | Weapons `WeaponController`(발당 1회, 펠릿 수 무관), AI `EnemyAgent`(`Weapon.Tick`이 1을 돌려줄 때) | Audio | 발사 수 |
| `AshStormChanged { AshStormPhase Phase; }` | 이벤트 | Raid `AshStormController`(단계가 바뀔 때만) | Audio | 레이드당 4회 |
| `IAshStormInfo { Phase; Intensity; VisibilityM; RadiationPerSec; }` | 서비스 | Raid `AshStormController`(OnEnable 등록/OnDisable 해제) | AI `AiDirector`(→ `EnemyAgent.Sense`), Player `PlayerHealth`, Audio `AudioDirector` | 동기 조회 |
| enum `SoundSource { Player, Enemy }`, `FootstepGait { Walk, Sprint, Crouch, Prone }`, `AshStormPhase { Pending, Rising, Peak, Falling, Over }` | — | | | |

- 이벤트는 기존처럼 `readonly struct`, `GameManager.Events.Publish`(할당 없음). 레이드 밖(은신처·메인 메뉴)에는 `IAshStormInfo`가 없다 → 소비자는 "폭풍 없음"으로 본다.
- `ShotFired.Weapon`은 null 허용(무기 없이 탄약만 쏘는 그레이박스 터렛 등 — M6에서 `TestTurret`은 발행하지 않는다). Audio는 null·미등록 분류면 `AudioConfigSO.fallbackGunshotClass` 세트.

### 1.3 비주얼 파이프라인 (2a)

```
PaletteSO (Data/Art/Palette.asset, §10.1 HEX)
  ├─ PaletteMaterialMap (EditorTools 표) ─► Art/Materials/Palette/*.mat (_BaseColor = 토큰)  + 기존 M2~M4 머티리얼 재도색(GUID 유지)
  ├─ TerrainLayer (회색 노이즈 텍스처 × diffuseRemapMax = Dried_Blood)
  └─ AtmosphereConfigSO (Data/Raid/AtmosphereConfig.asset, 토큰 + 수치)
        ├─ VP_RaidAtmosphere.asset (VolumeProfile, 매 실행 다시 씀 = 파생 산출물 ⟨Q30⟩)
        ├─ Sky_CrimsonDusk.png (equirect 1024×512, SkyPanorama 순수 함수) → M6_Sky.mat (Skybox/Panoramic)
        ├─ RenderSettings: fog Exp² (Dried_Blood, 맑을 때 가시거리), Trilight 환경광, skybox, sun
        ├─ Directional Light "Sun" (방위·고도 = 하늘 태양 위치, 색·세기)
        └─ AshParticles (Player 자식, World 시뮬레이션, 반경 30m 박스, M6_AshParticle.mat = URP Particles/Unlit + AshDot.png)
```

- **수치의 원본은 SO**(§0 "수치는 SO"): 볼륨 프로필·하늘 텍스처·머티리얼 색은 SO에서 메뉴가 만들어 내는 파생물이다. 조정은 SO에서 하고 메뉴를 다시 돌린다 ⟨Q30⟩.
  (볼륨 프로필을 인스펙터에서 직접 고치면 다음 메뉴 실행에 덮인다 — 프로필 파일 머리 주석·메뉴 로그로 알린다.)
- 안개: URP는 `RenderSettings` 안개(Linear/Exp/Exp²)를 Lit·Unlit·Particles·Terrain 셰이더에서 지원한다. 맑을 때 Exp² 가시거리 `clearVisibilityM`(450m, TODO)에서
  밀도 = `FogMath.DensityForVisibility(visibility, threshold)` = √(−ln threshold) / visibility. **레이드 씬은 항상 안개를 켠다** — 빌드의 셰이더 변형 자동 제거(Fog modes Automatic)가 씬에 쓰인 모드만 남기므로, 폭풍만 런타임에 안개를 켜면 Exp² 변형이 빠질 수 있다(위험 R1).
- 하늘은 안개를 받지 않는다 → 파노라마의 지평선 아래·지평선 띠를 안개 색(Dried_Blood)으로 칠해 원경 지형과 이어 보이게 한다(높이 안개 "느낌", ⟨Q3⟩).
- 포스트: 레이드 씬에 Global Volume 1개(priority 10, 프로필 `VP_RaidAtmosphere`) + **플레이어 카메라 `renderPostProcessing = true`**. 파이프라인 기본 프로필(SampleSceneProfile)은 그대로 두되,
  레이드 프로필이 Bloom·Vignette·Tonemapping을 모두 명시해 기본값을 덮는다. 은신처·메인 메뉴는 UI뿐이라 볼륨 없음, 카메라 배경만 Ash_Black ⟨Q5·Q29⟩.
- 재 파티클: Particle System 하나, `Player` 루트의 자식 "AshParticles"(simulationSpace World → 이동해도 떨어진 재는 제자리, 새 재는 플레이어 주변에서 생김).
  Shape Box 60×20×60m(반경 30m), 그림자 없음, maxParticles 상한. 폭풍은 같은 시스템의 방출 배율·속도 배율을 올린다(새 시스템 없음).
- 블록아웃 머티리얼·지면 재질 매핑은 한 표(`PaletteMaterialMap`, 구조물 이름의 Shape 접두어 기준)에서 둘 다 정한다(§3.4). 물웅덩이는 저수지 바닥의 얇은 트리거 박스
  (`Ignore Raycast` 레이어 — 기본 레이캐스트 마스크에서도 빠짐) + 물 머티리얼(Ash_Black, 매끈함) + `GroundSurface(Water)`. NavMesh는 다시 굽지 않는다.

### 1.4 오디오 파이프라인 (2b)

```
FootstepTaken ─┐                      ┌─ SurfaceProbe(레이캐스트 1회, 콜라이더 → SurfaceKind 캐시) ─ ClipPicker ─┐
ShotFired ─────┼─► AudioDirector ─────┼─ GunshotMix(거리 → 레이어·게인) ───────────────────────────────────┼─► AudioVoicePool (3D AudioSource N개, VoiceAllocator)
AshStormChanged┘   (씬당 1, 레이드)    └─ AmbientSoundscape: 바람 루프 · 폭풍 루프 · 금속 붕괴(IntervalScheduler) · 가이거(GeigerClickModel)
IAshStormInfo ─────────────────────────────┘
HideoutMusic (은신처 씬, 2D 루프, ignoreListenerPause)
```

- **3D 공간음**: 모든 효과음 voice는 spatialBlend 1, Doppler 0, 롤오프 = 사용자 정의 평탄 곡선(감쇠 없음) — **음량은 순수 함수가 계산**하고 Unity는 방향(팬)만 준다.
  그래야 거리 → 게인이 EditMode로 검증된다.
- **발소리**: 이벤트 위치와 청취자 거리 d가 걸음별 들리는 거리(`gaitAudibleM`)보다 멀면 버림(레이캐스트 없음). 아니면 `SurfaceProbe`가 위치 +0.5m에서 아래로 `RaycastNonAlloc`
  (트리거 포함, 마스크 Default + Ignore Raycast)해 가장 가까운 `GroundSurface` → 없고 TerrainCollider면 `terrainSurface`(흙) → 그 밖은 `untaggedSurface`(잔해).
  게인 = 재질 음량 × 걸음 음량 × (1 − d/들리는 거리)² × (플레이어 자신이면 `playerSelfVolume`). 같은 클립 연속 금지(`ClipPicker`), 피치 ±`pitchJitter`.
- **총성 레이어**: 거리 d로 근(≤ 50m) / 중(≤ 250m) / 원(≤ 1200m, 메아리 꼬리) 중 **하나**를 고른다 ⟨Q15⟩. 게인은 0m 1.0 → 50m 0.7 → 250m 0.35 → 1200m 0.1의 구간 선형(경계에서 연속), 그 너머는 안 들림.
  세트는 `WeaponDefinitionSO.cls`(권총·SMG·돌격소총, 그 밖은 대체 세트). 소리 전달 지연·차폐 없음 ⟨Q16·Q17⟩.
- **voice 풀**: `AudioDirector.Awake`에서 AudioSource N개(기본 24)를 자식으로 만든다(씬 파일에 넣지 않음). `VoiceAllocator`가 빈 칸 → 없으면 **요청보다 우선순위가 낮거나 같은** 것 중 가장 오래된 것을 빼앗는다
  (총성 > 금속 붕괴 > 적 발소리 > 내 발소리). 재생은 `source.clip = c; source.Play()` — 런타임 할당 없음.
- **환경음**: 2D 루프 소스 3개(바람·폭풍·가이거 클릭용). 바람 음량 = lerp(`windVolume`, `windStormVolume`, 폭풍 강도). 폭풍 루프는 `AshStormChanged(Rising)`에 재생 시작, 음량 = 강도 × 최대, `Over`에 정지.
  금속 붕괴는 `IntervalScheduler`(40~120초 무작위)마다 청취자에서 150~400m 떨어진 무작위 방향 위치에서 풀 voice로. 가이거는 `GeigerClickModel`(초당 클릭 = 방사능률 × 6, 상한 20, 지수 간격)로 클릭 원샷.
- **일시정지**: `AudioDirector.Update`가 `Flow.IsWorldPaused`를 `AudioListener.pause`에 반영(종료 오버레이 3초 포함). `OnDestroy`에서 false로 되돌린다. 은신처 음악은 `ignoreListenerPause`.
- **BGM**: 레이드·메인 메뉴에는 음악 소스가 없다(테스트로 확인). 은신처 씬에 `HideoutMusic`(자리표시 드론 루프) ⟨Q18⟩.

### 1.5 재 폭풍 (2c, 2d)

```
AshStormConfigSO ─► AshStormSchedule.PickStartSec(seed) ─► AshStormModel.Evaluate(raid.ElapsedSec)  (순수, 상태 없는 함수 + 단계 변화 감지)
                                                              │ Phase · Intensity(0~1) · VisibilityM · RadiationPerSec
AshStormController (Raid/Runtime, 씬 최상위 "AshStorm") ─────┼─► IAshStormInfo 등록 / AshStormChanged 발행
                                                              ├─► AshStormPresenter: 안개 밀도·색, 하늘 틴트·노출(머티리얼 복제본), 태양 세기, 재 파티클 배율
                                                              ├─► AI: AiDirector가 VisibilityM 조회 → EnemyAgent.Sense → SightCone.EffectiveRange(..., cap) = min(기존, cap)
                                                              ├─► Player: PlayerHealth가 RadiationPerSec 조회 → RadiationDoseModel.Tick(dt)
                                                              └─► Audio: 바람·폭풍 루프·가이거
```

- **시각 = 레이드 경과 시간**(`RaidController.ElapsedSec`): 일시정지·종료 정지 중 멈추고, 디버그 `AdvanceTime`을 따른다 ⟨Q28⟩. `RaidController`는 수정하지 않는다(직렬화 참조로 읽기만).
- **일정** ⟨Q23·Q24⟩: 시작 = 균등 무작위 [180, 1200]초(시드: `AshStormController.seed`, 0이면 매번 무작위). Rising 20초 → Peak 180초(§8.3 "3분") → Falling 20초 → Over. 레이드당 한 번, Over 뒤 다시 없음.
  레이드가 먼저 끝나면 그대로 끝(폭풍을 못 볼 수 있다).
- **강도·값**: Intensity = 전환 중 선형 0→1→0. VisibilityM = Intensity 0이면 +∞, 아니면 lerp(맑은 가시거리 450, 30, Intensity). RadiationPerSec = 1 × Intensity(방독면 무시 — M7).
  Peak의 가시거리는 정확히 30m. 렌더 안개 밀도 = `FogMath.DensityForVisibility(VisibilityM)` — 맑을 때 밀도에서 시작해 이어진다.
- **AI 시야** ⟨Q26⟩: `effective = min(sightRange × 자세 배율, VisibilityM)`. 청각·사거리·엄폐 규칙은 그대로. 폭풍 밖(또는 은신처)에서는 +∞ = 기존과 같다.
- **방사능** ⟨Q27⟩: Player 순수 `RadiationDoseModel`(누적만, 상한·효과 없음). 레이드마다 0에서 시작(체력 이월 없음, M5 Q18). 사망 후 멈춤. `PlayerHealth.RadiationDose`로 테스트에서 읽는다.
  이벤트·UI(부위 상태 화면 STAT-RAD 표시)·방독면·해독제·100 효과는 M7 `TODO(GDD-?)`.
- **예고 없음** ⟨Q25⟩: Rising 20초(안개가 짙어지고 바람이 거세짐)가 예고 역할.
- **표시 복원**: 프레젠터는 시작 때 씬의 안개 밀도·색, 하늘 머티리얼, 태양 세기, 파티클 방출을 기억하고 Intensity 0이면 그 값으로 돌린다. 하늘은 **머티리얼 복제본**을
  `RenderSettings.skybox`에 넣어 바꾼다(에디터 Play에서 공유 에셋이 바뀌지 않게), `OnDestroy`에서 원본을 돌려놓고 복제본을 파괴한다.
- 디버그: `AshStormController` `[ContextMenu("Debug/재 폭풍 지금 시작")]`(시작 시각 = 현재, 이미 시작했으면 무시), `ForceStartNow()` 공개(테스트용).

### 1.6 씬 적용 방식 ⟨Q1⟩

- 새 메뉴 **`Ashfall/Setup/Apply M6 Art & Audio`**(배치 `Ashfall.EditorTools.M6SceneSetup.ApplyAll`):
  1. 데이터·에셋: `M6DataSetup.CreateData()`(1b) → `M6ArtSetup.CreateAssets()`(2a) → `M6AudioSetup.CreateAssets()`(2b).
  2. `Raid_CrimsonBasin.unity`를 **열어**(`OpenScene`, 새로 만들지 않음) `M6VisualSceneApply.ApplyRaid` → `M6StormSceneApply.Apply` → `M6AudioSceneApply.ApplyRaid` → 같은 경로에 저장.
  3. `Hideout.unity`: `M6VisualSceneApply.ApplyMenuScene`(카메라 배경) → `M6AudioSceneApply.ApplyHideout` → 저장. `MainMenu.unity`: `ApplyMenuScene` → 저장.
- **멱등**: 이름으로 찾아 "있으면 다시 설정, 없으면 생성"(지우고 다시 만들지 않음 → 두 번째 실행에서 씬 파일 바이트가 같기를 기대, 최소한 GUID·오브젝트 수 동일).
  컴포넌트는 `GetComponent ?? AddComponent`. 씬·에셋 `.meta` GUID 유지(같은 경로 저장, 에셋은 있으면 덮어쓰기/수정).
- **M4·M5 생성 메뉴와의 관계**: `M4SceneSetup.CreateRaidScene`은 저장 직전 `M6SceneSetup.ApplyRaidIfAssetsExist(scene)`를, `M5SceneSetup`·`M4SceneSetup.CreateMainMenuScene`도
  같은 방식으로 부른다(3단계, 각 한 줄). M4 메뉴를 다시 돌려도(Terrain·NavMesh 재생성) M6이 빠지지 않는다. M6 에셋이 없으면 경고 없이 건너뜀(M4만 쓰는 상황 호환).
- 확인 씬(M1~M3)은 건드리지 않는다 ⟨Q9⟩. 빌드 설정·씬 목록 변경 없음.

### 1.7 성능·할당

- 이벤트 경로(`FootstepTaken`·`ShotFired` 처리) **GC 할당 0**: 버퍼 재사용 `RaycastNonAlloc`(4칸), 콜라이더 instanceID → `SurfaceKind` 사전(첫 만남에만 추가 — 시작 때 `GroundSurface` 전부 미리 등록), 클립 선택 `System.Random` 재사용.
- AudioSource: 효과음 24 + 환경 3(바람·폭풍·가이거) + 은신처 1. 런타임 `Instantiate`·`AddComponent` 없음(Awake 1회 생성 제외).
- 들리는 거리 밖 발소리는 레이캐스트 전에 버림. 적 20명 기준 레이캐스트 ≤ 40/s.
- 재 파티클 1개 시스템, 상한 `maxParticles`(평시 6000, 폭풍 배율로 방출만 늘고 상한 12000 — `AtmosphereConfigSO`), 그림자·충돌·조명 없음.
- 폭풍 프레젠터는 강도가 바뀌는 프레임(전환 40초)에만 `RenderSettings`·머티리얼을 쓴다.
- 4단계 자동 판정(§7): 기존 M3/M4 예산(AI 평균 ≤ 1.0ms·99% ≤ 3ms, 투사체 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms) 유지 + Audio·폭풍 갱신 평균 ≤ 0.2ms, GC 0. GPU(파티클·포스트)는 사람이 Profiler로.

---

## 2. 자리표시 에셋

### 2.1 만드는 방법 (전부 EditorTools, 결정적)
- **WAV**: 순수 `SfxSynth`(고정 시드 `System.Random` + 사인·노이즈·필터·엔벨로프)가 float 샘플을 만들고 `WavWriter`가 PCM16 모노 22050Hz RIFF 바이트로 써서 `File.WriteAllBytes` → `AssetDatabase.ImportAsset`.
  `AudioImporter`: 모노 강제, 1초 미만 = DecompressOnLoad, 루프·긴 소리 = CompressedInMemory(Vorbis 0.7), preload.
  **파일이 없을 때만 쓴다** ⟨Q21⟩ — 사용자가 같은 경로에 실제 WAV를 덮어쓰면 다음 메뉴 실행에도 보존된다. 다시 만들려면 파일을 지운다.
- **텍스처**: 순수 함수(`SkyPanorama.Sample`, 노이즈·원형 점)로 `Texture2D` → `EncodeToPNG` → `Art/Generated/*.png`(매번 다시 씀 — 파생물). 임포트 설정 코드로(하늘: Clamp/Repeat U, 밉맵 없음, sRGB).
- **머티리얼**: `Art/Materials/Palette/M_*.mat`(URP Lit/Unlit/Particles), 색 = 팔레트 토큰. 있으면 색·속성만 다시 기록(GUID 유지).
- **볼륨 프로필**: `Art/PostProcess/VP_RaidAtmosphere.asset`(`VolumeProfile`, 없으면 생성, 있으면 구성 요소 값 다시 기록 — GUID 유지).
- **SO 클립 필드는 비어 있을 때만 채운다** ⟨Q21⟩ — 사용자가 `AudioConfig`에 다른 클립을 넣었으면 덮지 않는다.

### 2.2 자리표시 에셋 목록 (모두 `TODO(GDD-?)` 자리표시)

| 경로 (`Assets/_Project/...`) | 내용 / 합성 | 쓰는 곳 |
|---|---|---|
| `Audio/Placeholder/Footsteps/FS_Dirt_01~03.wav` | 저역 통과 노이즈 짧은 "툭"(0.18s) | `AudioConfigSO.footsteps[Dirt].clips` |
| `Audio/Placeholder/Footsteps/FS_Metal_01~03.wav` | 노이즈 타격 + 감쇠 공진 사인 2~3개(0.3s) | `footsteps[Metal]` |
| `Audio/Placeholder/Footsteps/FS_Debris_01~03.wav` | 짧은 노이즈 알갱이 4~6개(자갈, 0.25s) | `footsteps[Debris]` |
| `Audio/Placeholder/Footsteps/FS_Water_01~03.wav` | 대역 통과 노이즈 + 하강 스윕(첨벙, 0.35s) | `footsteps[Water]` |
| `Audio/Placeholder/Gunshots/GS_{Pistol,Smg,Rifle}_{Near,Mid,Far}.wav` (9) | 근: 날카로운 노이즈 크랙 + 저음 펀치(0.5s) / 중: 저역 통과·둔한 펀치(0.9s) / 원: 둔한 쿵 + 지연 반복 메아리 꼬리(2.5s). 분류별 음높이·길이 차이 | `gunshots[].near/mid/far` |
| `Audio/Placeholder/Ambience/AMB_Wind_Loop.wav` | 저역 노이즈 + 느린 LFO, 10s 이음매 없는 루프(끝·처음 교차 페이드) | `windLoop` |
| `Audio/Placeholder/Ambience/AMB_AshStorm_Loop.wav` | 밝은 노이즈 쉿 소리 + 돌풍 LFO, 10s 루프 | `stormLoop` |
| `Audio/Placeholder/Ambience/AMB_MetalCollapse_01~02.wav` | 저음 우르릉 + 금속 공진 + 삐걱임(3.5s) | `collapseClips` |
| `Audio/Placeholder/Ambience/AMB_Geiger_Click.wav` | 4ms 클릭 | `geigerClick` |
| `Audio/Placeholder/Music/MUS_Hideout_Loop.wav` | 저음 사인 화음 드론 + 느린 트레몰로, 30s 루프 | `hideoutMusic` ⟨Q18⟩ |
| `Art/Generated/Sky_CrimsonDusk.png` | equirect 1024×512 그라데이션 + 흐린 태양 | `Art/Materials/M6_Sky.mat`(`Skybox/Panoramic`) |
| `Art/Generated/AshDot.png` | 64×64 부드러운 원(알파) | `Art/Materials/M6_AshParticle.mat` |
| `Art/Generated/GroundNoise.png` | 256×256 이음매 없는 회색 노이즈 | `Art/Terrain/TL_AshSoil.terrainlayer`(diffuseRemapMax = Dried_Blood) |
| `Art/Materials/Palette/M_Concrete·M_RustMetal·M_Debris·M_Puddle·M_Crate·M_Accent.mat` | 팔레트 색 단색(URP Lit) | 블록아웃(§3.4) |
| `Art/PostProcess/VP_RaidAtmosphere.asset` | §10.2 포스트 값 | 레이드 Global Volume |

합계 WAV 33개(약 4MB, 22kHz 모노). 크기를 줄이려면 음악 길이를 줄인다(TODO).

### 2.3 사용자에게 필요한 실제 에셋 (교체 위치)

| 에셋 | 권장 형식 | 교체 방법 (둘 중 하나) |
|---|---|---|
| 발소리 흙/금속/잔해/물 각 4~6개 | WAV/OGG 모노 44.1kHz, 0.2~0.5s, 앞 무음 없음 | ① `Audio/Placeholder/Footsteps/`의 같은 이름 WAV 덮어쓰기(GUID 유지, 참조 그대로) ② 새 파일을 `Data/Audio/AudioConfig.asset` → `footsteps[재질].clips` 배열에 넣기(개수 자유) |
| 총성 근/중/원 × 권총·SMG·소총 (M7: 샷건·저격 추가) | 모노, 원거리는 메아리 꼬리 포함 2~4s | ① 같은 이름 덮어쓰기 ② `AudioConfig.gunshots[분류].near/mid/far`. 새 분류는 배열에 원소 추가(`weaponClass`) |
| 바람 루프, 재 폭풍 루프 | 스테레오 가능(2D), 이음매 없는 루프 20~60s | `AudioConfig.windLoop`, `stormLoop` |
| 멀리서 무너지는 금속 3~5개 | 모노 3~6s | `AudioConfig.collapseClips` |
| 가이거 클릭 | 모노 단발 클릭(연속 녹음 아님 — 클릭 간격은 코드가 만든다) | `AudioConfig.geigerClick` |
| 은신처 BGM | 스테레오 루프 | `AudioConfig.hideoutMusic` |
| 하늘 파노라마 | equirect 2:1 PNG/EXR(HDR이면 Bloom 태양 가능), 태양 방위·고도를 `AtmosphereConfig.sunAzimuthDeg/sunElevationDeg`와 맞출 것 | `Art/Materials/M6_Sky.mat`의 `_MainTex`를 바꾸고 **`AtmosphereConfig.skyTextureOverride`에 넣는다**(메뉴가 생성 PNG로 되돌리지 않게) |
| 재 입자 텍스처(플립북 가능) | 알파 PNG | `M6_AshParticle.mat`의 `_BaseMap` + `AtmosphereConfig.ashTextureOverride` |
| 지형 텍스처(albedo·normal, 흙/재/아스팔트) | 타일링 PNG | `Art/Terrain/TL_AshSoil.terrainlayer` 수정 또는 레이어 추가(메뉴는 `TL_AshSoil`만 관리) |
| 구조물·차량·탱크·송전탑 모델 | FBX + 팔레트 톤 텍스처 | M6 범위 밖(블록아웃 유지). 교체 시 `GroundSurface`(재질)·`CoverPoint`를 같이 둘 것 |
| 스텐실·타자기 계열 폰트(§12) | TTF/OTF(라이선스 확인) | `Art/UI/AshfallTheme.tss`/`Ashfall.uss`의 `-unity-font-definition` (M6 미적용) |
| 아이템 아이콘 | PNG | `ItemDefinitionSO.icon` (M6 미적용) |

- 교체 후에는 `Ashfall/Setup/Apply M6 Art & Audio`를 다시 돌려도 사용자의 WAV·SO 클립 참조는 보존된다(§2.1). 하늘·재 텍스처는 `*Override` 필드가 있으면 생성물을 쓰지 않는다.

### 2.4 내장 모듈 매니페스트 ⟨Q10⟩
- `com.unity.modules.audio`(AudioSource·AudioClip·AudioImporter), `com.unity.modules.particlesystem`(재 파티클) — 지금은 `com.unity.timeline`의 의존성으로만 들어온다(M4 terrainphysics와 같은 종류의 위험).
  **추천: 둘 다 매니페스트에 명시**(동작 변화 없음, lock depth만 0). 회귀 테스트 `M6ModuleTests`(매니페스트 두 줄 + `AudioSource`·`ParticleSystem` 형식 로드).
- `imageconversion`(EncodeToPNG)은 에디터 메뉴에서만 쓰므로 건드리지 않는다(빌드 런타임 불필요). 그 밖 모듈도 그대로.

---

## 3. 수치 (SO 기본값 — GDD에 없는 것은 전부 `TODO(GDD-?)`)

### 3.1 `PaletteSO` (§10.1, GDD 값)
| 토큰 | 필드 | HEX |
|---|---|---|
| `Ash_Black` | `ashBlack` | `#0D0807` |
| `Dried_Blood` | `driedBlood` | `#3A0B0B` |
| `Rust_Red` | `rustRed` | `#6E1A14` |
| `Ember` | `ember` | `#B2321C` |
| `Ash_Grey` | `ashGrey` | `#5A524E` |
| `Bone` | `bone` | `#C9BBA8` |
| `Toxic` | `toxic` | `#7A8A2A` |
색은 sRGB(인스펙터·`ColorUtility.TryParseHtmlString`과 같은 공간)로 저장한다. Linear 프로젝트에서 머티리얼·안개 색으로 넣을 때 Unity가 변환한다.

### 3.2 `AtmosphereConfigSO` (Raid/Data)
| 묶음 | 필드 = 기본값 | 근거 |
|---|---|---|
| 팔레트 | `palette` = Palette.asset | |
| 안개 | `fogToken` = DriedBlood, `clearVisibilityM` = 450, `fogVisibilityThreshold` = 0.05 | §10.2 Dried_Blood / 450·0.05 TODO |
| 하늘 | `zenithToken` = AshBlack, `horizonToken` = RustRed, `belowHorizonToken` = DriedBlood, `horizonBandDeg` = 10, `skyTextureSize` = (1024, 512), `skyTextureOverride` = null | §10.2 Ash_Black → Rust_Red / 나머지 TODO |
| 태양 | `sunAzimuthDeg` = 245, `sunElevationDeg` = 8, `sunDiskDeg` = 2.5, `sunGlowDeg` = 18, `sunHaze` = 0.6(0 = 또렷, 1 = 안 보임), `sunCoreToken` = Bone, `sunGlowToken` = Ember, `sunLightToken` = Ember, `sunLightIntensity` = 0.9 | "낮게 걸린 흐린 태양" / 수치 TODO |
| 환경광 | Trilight `ambientSkyToken` = RustRed, `ambientEquatorToken` = DriedBlood, `ambientGroundToken` = AshBlack, `ambientIntensity` = 1.0 | TODO |
| 포스트 | `saturation` = −30, `tintToken` = Ember, `tintStrength` = 0.15(colorFilter = lerp(흰색, Ember, 0.15)), `contrast` = 10, `filmGrainIntensity` = 0.3, `filmGrainType` = Medium, `vignetteIntensity` = 0.35, `vignetteSmoothness` = 0.4, `vignetteToken` = AshBlack, `bloomIntensity` = 0.15, `bloomThreshold` = 1.1, `tonemapping` = Neutral | §10.2 채도·그레인·비네트 GDD 값 / 틴트·대비·Bloom 수치·Tonemapping TODO |
| 재 파티클 | `ashRadiusM` = 30, `ashHeightM` = 20, `ashRatePerSec` = 600, `ashLifetimeSec` = 10, `ashMaxParticles` = 6000, `ashMaxParticlesStorm` = 12000, `ashSizeM` = (0.03, 0.08), `ashFallSpeedMps` = (0.3, 0.8), `ashNoiseStrength` = 0.3, `ashToken` = AshGrey, `ashAlpha` = 0.7, `ashTextureOverride` = null | §10.2 "30m" / 나머지 TODO |

`TonemapChoice { None, Neutral, Aces }`는 Raid에 둔다(Raid가 URP 형식을 참조하지 않게, EditorTools가 URP 값으로 바꾼다).

### 3.3 `AshStormConfigSO` (Raid/Data)
| 필드 = 기본값 | 근거 |
|---|---|
| `startWindowSec` = (180, 1200) | "레이드 중 1회 랜덤" ⟨Q23⟩ TODO |
| `riseSec` = 20, `peakSec` = 180, `fallSec` = 20 | "3분" = Peak ⟨Q24⟩ / 전환 TODO |
| `stormVisibilityM` = 30 | §8.3 |
| `radiationPerSec` = 1 | §8.3 |
| `palette`, `stormFogToken` = AshGrey | TODO (재 색 안개) |
| `skyTintToken` = AshGrey, `skyExposureAtPeak` = 0.35, `sunIntensityFactorAtPeak` = 0.25 | TODO |
| `ashEmissionMultiplier` = 4, `ashSpeedMultiplier` = 3, `ashSizeMultiplier` = 1.5 | TODO |

### 3.4 블록아웃 머티리얼·지면 재질 표 (`PaletteMaterialMap`, 레벨 데이터 — TODO(GDD-?))
| 대상 (M4 이름 규칙) | 머티리얼 (토큰, 속성) | `SurfaceKind` |
|---|---|---|
| Terrain | `TL_AshSoil`(Dried_Blood × 노이즈) | Dirt(`AudioConfig.terrainSurface`) |
| `LowWall_*`, `HighWall_*`, `House_*`, `Deck_*`(Deck·Pillar), `Ramp_*`(Slab) | `M_Concrete` (Ash_Grey, smoothness 0.1) | Debris |
| `Rubble_*` | `M_Debris` (Ash_Grey, smoothness 0.05) | Debris |
| `Vehicle_*`, `VehiclePile_*`, `Tank_*`, `Post_*`, `Flat_*`(철로·착륙장 테두리) | `M_RustMetal` (Rust_Red, metallic 0.3, smoothness 0.25) | Metal |
| 루트 상자(`LootContainer`가 있는 큐브) | `M_Crate` (Rust_Red, smoothness 0.2) | Metal |
| `PowerSwitch_*` | `M_Accent` (Ember, 약한 발광) | Metal |
| 물웅덩이(새, 저수지 3곳) | `M_Puddle` (Ash_Black, smoothness 0.95) | Water |
| 기존 `M4_ExtractionZone.mat` | Bone α0.25 (URP Unlit 투명) | — |
| 기존 `M3_EnemySCV.mat` / `M3_EnemyRDR.mat` | Ash_Grey / Bone (지형 Dried_Blood와 대비) | — |
| 기존 `M2_TurretZone.mat` | Ember α0.3 | — |
| 그 밖 콜라이더(경계 벽·알 수 없는 것) | 바꾸지 않음 | `untaggedSurface` = Debris |
- 머티리얼 이름·토큰은 `PaletteMaterialMap`의 표 한 곳에서만 정한다. 테스트(§7 AC1)가 모든 `M_*`·기존 4개의 `_BaseColor`(RGB) = 토큰인지 확인한다.

### 3.5 `AudioConfigSO` (Audio/Data)
| 묶음 | 필드 = 기본값 (TODO(GDD-?)) |
|---|---|
| 지면 판정 | `surfaceProbeLengthM` = 1.5, `surfaceMask` = Default + Ignore Raycast, `terrainSurface` = Dirt, `untaggedSurface` = Debris |
| 발소리 | `footsteps[]` = { surface, clips[], volume } × 4, `gaitVolume` = Walk 0.7 / Sprint 1.0 / Crouch 0.35 / Prone 0.2, `gaitAudibleM` = Walk 25 / Sprint 40 / Crouch 10 / Prone 5, `playerSelfVolume` = 0.6, `pitchJitter` = 0.06 |
| 총성 | `gunshots[]` = { weaponClass, near, mid, far, volume } × 3(Pistol·Smg·AssaultRifle), `fallbackGunshotClass` = AssaultRifle, `nearMaxM` = 50, `midMaxM` = 250, `farMaxM` = 1200, `gainAt0` = 1.0, `gainAtNear` = 0.7, `gainAtMid` = 0.35, `gainAtFar` = 0.1 |
| voice | `voiceCount` = 24, 우선순위 고정(총성 0 > 붕괴 1 > 적 발소리 2 > 내 발소리 3) |
| 환경 | `windLoop`, `windVolume` = 0.35, `windStormVolume` = 0.8, `stormLoop`, `stormLoopMaxVolume` = 0.7, `collapseClips[]`, `collapseIntervalSec` = (40, 120), `collapseDistanceM` = (150, 400), `collapseVolume` = 0.6, `geigerClick`, `geigerClicksPerRad` = 6, `geigerMaxClicksPerSec` = 20, `geigerVolume` = 0.5 |
| 은신처 | `hideoutMusic`, `hideoutMusicVolume` = 0.4 |

### 3.6 보폭 (발소리 리듬)
- `PlayerConfigSO`(Player): `strideWalkM` = 1.5, `strideSprintM` = 2.2, `strideCrouchM` = 0.8, `strideProneM` = 0.6 (걷기 3.0m/s → 2걸음/s 등).
- `AiConfigSO`(AI): `footstepStrideWalkM` = 1.5, `footstepStrideRunM` = 2.2, `footstepRunSpeedMps` = 3.5(이보다 빠르면 Sprint 걸음), `footstepMinSpeedMps` = 0.3(이보다 느리면 걸음 없음).
- `AiConfigSO.showStateIndicator` = **false**(M6 데이터 메뉴가 매번 기록) ⟨Q8⟩.

---

## 4. 파일·클래스·공개 API

### 4.1 Core (`namespace Ashfall.Core`) — 1단계
| 파일 | 내용 |
|---|---|
| `Core/Data/PaletteSO.cs` (새) | `enum PaletteToken { AshBlack, DriedBlood, RustRed, Ember, AshGrey, Bone, Toxic }`, `[CreateAssetMenu("Ashfall/Palette")] PaletteSO { Color ashBlack … toxic; Color Get(PaletteToken); static string GddName(PaletteToken) /* "Ash_Black" … */ }` |
| `Core/Events/GameEvents.cs` | `SoundSource`, `FootstepGait`, `FootstepTaken`, `ShotFired`, `AshStormPhase`, `AshStormChanged` (§1.2) |
| `Core/Services/IAshStormInfo.cs` (새) | 아래 |
| `Core/Movement/StrideCadence.cs` (새, 구현 포함) | 아래 |
| `Core/Services/ServiceRegistry.cs` | 주석에 `M6: IAshStormInfo(Raid)` |

```csharp
// 재 폭풍 현재 상태 (§8.3). Raid(AshStormController)가 등록. 레이드 밖에는 없다 = 폭풍 없음.
public interface IAshStormInfo
{
    AshStormPhase Phase { get; }
    float Intensity { get; }          // 0~1 (전환 중 선형)
    float VisibilityM { get; }        // 폭풍 아님(Intensity 0) = float.PositiveInfinity, Peak = 30
    float RadiationPerSec { get; }    // §8.3 +1/s × Intensity. 방독면은 M7(소비자가 처리)
}

// 보폭 거리 기반 걸음 리듬 (M6, AUD 발소리). Player·AI 공용, 순수.
public sealed class StrideCadence
{
    // 이번 프레임 수평 이동 거리와 보폭. 걸음이면 true. 한 틱에 최대 1걸음(밀린 걸음 몰아 내지 않음).
    // 멈춰 있다가(거리 ≤ 0) 움직이기 시작하면 누적을 보폭의 절반에서 시작한다(첫 걸음이 빨리 나게). 보폭 ≤ 0이면 항상 false.
    public bool Tick(float horizontalDistanceM, float strideLengthM);
    public void Reset();
}
```

### 4.2 Raid (`Ashfall.Raid`)
| 파일 | 단계 | 내용 |
|---|---|---|
| `Raid/Data/AtmosphereConfigSO.cs` (새) | 1 껍데기 | §3.2 필드·기본값·툴팁, `enum TonemapChoice` |
| `Raid/Data/AshStormConfigSO.cs` (새) | 1 껍데기 | §3.3 |
| `Raid/Logic/FogMath.cs` (새) | 2c | `static float DensityForVisibility(float visibilityM, float threshold)`(∞ → 0, ≤ 0 → 큰 값 클램프), `static float VisibilityForDensity(...)` |
| `Raid/Logic/AshStormSchedule.cs` (새) | 2c | `static float PickStartSec(Vector2 window, System.Random random)`(포함 범위, 창이 뒤집히면 정렬) |
| `Raid/Logic/AshStormModel.cs` (새) | 2c | 아래 |
| `Raid/Runtime/AshStormController.cs` (새) | 2c | MonoBehaviour, `IAshStormInfo` 구현. 필드: `RaidController raid`, `AshStormConfigSO config`, `AtmosphereConfigSO atmosphere`, `AshStormPresenter presenter`, `int seed`. OnEnable 등록 / OnDisable 해제. Update: `raid.Session`이 생기면 시작 시각 결정(1회) → `model.Evaluate(raid.ElapsedSec)` → 단계 바뀌면 `AshStormChanged` → `presenter.Apply(model)`. `ForceStartNow()`, ContextMenu |
| `Raid/Runtime/AshStormPresenter.cs` (새) | 2c | 필드: `ParticleSystem ashParticles`, `AtmosphereConfigSO atmosphere`, `AshStormConfigSO config`. Start에 원래 값 기억, `Apply(float intensity, float visibilityM)`, OnDestroy 복원(§1.5) |

```csharp
public sealed class AshStormModel
{
    public AshStormModel(AshStormConfigSO config, float clearVisibilityM, float startSec);
    public float StartSec { get; }
    public AshStormPhase Phase { get; private set; }
    public float Intensity { get; private set; }
    public float VisibilityM { get; private set; }
    public float RadiationPerSec { get; private set; }
    // 경과 시간으로 상태를 정한다(단조 증가 가정, 되감아도 결과는 그 시각의 상태). 단계가 바뀌었으면 true.
    public bool Evaluate(float elapsedSec);
    // 아직 Pending일 때만 시작 시각을 당긴다(디버그·테스트). 시작했으면 false.
    public bool Reschedule(float startSec);
}
```
- `RaidController`·`RaidSession`·`RaidConfigSO`는 **변경 없음**.

### 4.3 Audio (`Ashfall.Audio`, `Scripts/Audio/`) — 처음 생기는 스크립트
| 파일 | 단계 | 내용 |
|---|---|---|
| `Audio/Data/SurfaceKind.cs` (새) | 1 | `enum SurfaceKind { Dirt, Metal, Debris, Water }` |
| `Audio/Data/AudioConfigSO.cs` (새) | 1 껍데기 | §3.5 필드, `[Serializable] SurfaceFootsteps { surface; clips; volume }`, `[Serializable] GunshotSet { weaponClass; near; mid; far; volume }` |
| `Audio/Runtime/GroundSurface.cs` (새) | 1 구현 | `[SerializeField] SurfaceKind kind; public SurfaceKind Kind` (콜라이더와 같은 오브젝트, EditorTools가 붙임) |
| `Audio/Logic/GunshotMix.cs` (새) | 2b | `enum GunshotLayer { Near, Mid, Far }`, `static bool Evaluate(float distanceM, AudioConfigSO cfg, out GunshotLayer layer, out float gain)` |
| `Audio/Logic/FootstepMix.cs` (새) | 2b | `static bool Evaluate(float distanceM, FootstepGait gait, SoundSource source, AudioConfigSO cfg, out float gain)`, `static int Priority(SoundSource)` |
| `Audio/Logic/SurfaceResolver.cs` (새) | 2b | `struct SurfaceHit { float Distance; bool HasTag; SurfaceKind Tag; bool IsTerrain; }`, `static SurfaceKind Pick(SurfaceHit[] hits, int count, SurfaceKind terrain, SurfaceKind untagged)` — 재사용 배열 + 개수(할당 없음). 가장 가까운 태그 > 지형 > 기본(태그 없는 트리거는 건너뜀) |
| `Audio/Logic/ClipPicker.cs` (새) | 2b | `int Next(int clipCount)`(직전과 다른 인덱스, 1개면 0), `float Pitch(float jitter)` — `System.Random` 주입 |
| `Audio/Logic/VoiceAllocator.cs` (새) | 2b | `VoiceAllocator(int count)`, `int Acquire(int priority, float now, float durationSec)`(빈 칸 → 같거나 낮은 우선순위 중 가장 오래된 것 → 없으면 −1), `Release(int)`, `IsBusy(int, now)` |
| `Audio/Logic/GeigerClickModel.cs` (새) | 2b | `int Tick(float dt, float radiationPerSec)` — 지수 간격, 상한, 0이면 0 |
| `Audio/Logic/IntervalScheduler.cs` (새) | 2b | `IntervalScheduler(Vector2 rangeSec, System.Random)`, `bool Tick(float dt)` |
| `Audio/Runtime/SurfaceProbe.cs` (새) | 2b | 레이캐스트 버퍼·instanceID 캐시, `SurfaceKind Probe(Vector3 feet)`, `Prewarm()` |
| `Audio/Runtime/AudioVoicePool.cs` (새, 일반 클래스) | 2b | AudioSource N개 생성·설정, `bool Play(AudioClip, Vector3, float gain, float pitch, int priority)` |
| `Audio/Runtime/AmbientSoundscape.cs` (새, 일반 클래스) | 2b | 바람·폭풍·가이거 소스, `Tick(dt, listener, storm)`, `OnStormPhase(phase)` |
| `Audio/Runtime/AudioDirector.cs` (새) | 2b | 씬당 1(레이드), 정적 인스턴스 없음. 필드 `config`, `seed`. OnEnable 구독(`FootstepTaken`·`ShotFired`·`AshStormChanged`)/OnDisable 해지, `IPlayerTarget`·`IAshStormInfo` 지연 조회(재시도 0.5s), Update(일시정지 반영·환경음), OnDestroy(`AudioListener.pause=false`). ContextMenu `Debug/총성 근·중·원`, `Debug/금속 붕괴 지금`. 테스트용 읽기: `LastPlayed`(클립·위치·게인·레이어) |
| `Audio/Runtime/HideoutMusic.cs` (새) | 2b | 2D 루프, `ignoreListenerPause`, 클립 없으면 조용히 없음 |

### 4.4 Player (`Ashfall.Player`)
| 파일 | 단계 | 내용 |
|---|---|---|
| `Player/PlayerConfigSO.cs` | 1 | §3.6 보폭 필드 4개 |
| `Player/RadiationDoseModel.cs` (새) | 2c | `float Dose`, `Tick(float dt, float ratePerSec)`(음수·NaN 무시), `Reset()` — `TODO(GDD-?) Q27` M7 효과 |
| `Player/PlayerHealth.cs` | 2c | `RadiationDoseModel` 소유, `public float RadiationDose`, Update에서 `IAshStormInfo` 지연 조회 → 살아 있으면 `Tick(Time.deltaTime, info.RadiationPerSec)` |
| `Player/PlayerMotor.cs` | 2c | `StrideCadence` 추가: `controller.Move` 뒤 실제 수평 이동 거리(접지 중만)로 Tick → `FootstepTaken(transform.position, 현재 걸음, Player)`. 걸음 = 자세(엎드림 > 웅크림) > 질주 > 걷기. AI용 `FootstepNoiseModel`은 그대로 |

### 4.5 Weapons (`Ashfall.Weapons`)
| 파일 | 단계 | 내용 |
|---|---|---|
| `Weapons/WeaponController.cs` | 2c | 발사 직후(`WeaponNoise` 발행 옆) `ShotFired(origin, weapon, Player)` 1회 |

### 4.6 AI (`Ashfall.AI`)
| 파일 | 단계 | 내용 |
|---|---|---|
| `AI/Data/AiConfigSO.cs` | 1 | §3.6 보폭 필드 4개 |
| `AI/Sensing/SightCone.cs` | 2d | `EffectiveRange(sightRangeM, posture, config, float visibilityCapM)` 오버로드 = `Mathf.Min(기존, cap)`(cap ≤ 0이면 0, ∞면 기존). 기존 3인자 오버로드 유지 |
| `AI/Runtime/AiDirector.cs` | 2d | `ResolveServices`에서 `IAshStormInfo` 조회(없으면 null), 감각 호출 시 `cap = storm?.VisibilityM ?? ∞` 전달 |
| `AI/Runtime/EnemyAgent.cs` | 2d | `Sense(target, los, now, visibilityCapM)`; `FrameTick`에서 NavMeshAgent 속도로 `StrideCadence` → `FootstepTaken(위치, 걸음, Enemy)`(죽음·NavMesh 밖·속도 < 최소면 Reset); `ApplyWeapon`에서 `Weapon.Tick(...) > 0`이면 `ShotFired(muzzle, 무기 정의, Enemy)` |

### 4.7 EditorTools (`Ashfall.EditorTools`)
| 파일 | 단계 | 내용 |
|---|---|---|
| `EditorTools/M6Names.cs` (새) | 1 | 에셋 경로·씬 오브젝트 이름 상수(계약): 데이터 4개 경로, `Art/Generated`·`Art/Materials/Palette`·`Art/PostProcess`·`Art/Terrain`·`Audio/Placeholder/...` 폴더, 오브젝트 이름 `Sun`, `M6_GlobalVolume`, `AshParticles`, `M6_Puddles`, `AshStorm`, `AudioDirector`, `HideoutMusic` |
| `EditorTools/M6DataSetup.cs` (새) | 1b | 메뉴 `Ashfall/Setup/Create M6 Data`, 배치 `CreateData`. 먼저 `M5DataSetup.CreateData()`. ① `Data/Art/Palette.asset`(없을 때만, GDD HEX) ② `Data/Raid/AtmosphereConfig.asset`·`AshStormConfig.asset`(없을 때만, palette 연결) ③ 기존 `PlayerConfig`·`AiConfig`에 보폭 값 기록, `AiConfig.showStateIndicator=false` 기록(매번 — 멱등) |
| `EditorTools/PaletteMaterialMap.cs` (새) | 2a | §3.4 표(구조물 Shape 접두어 → 머티리얼 키·`SurfaceKind`, 기존 머티리얼 경로 → 토큰·알파) |
| `EditorTools/SkyPanorama.cs` (새, 순수) | 2a | `static Color Sample(Vector3 dir, AtmosphereConfigSO cfg)`, `static Vector3 SunDirection(cfg)`, `static Vector2 DirectionToUv(Vector3)` |
| `EditorTools/M6ArtSetup.cs` (새) | 2a | `CreateAssets()`: 텍스처 3, 팔레트 머티리얼 + 기존 4개 재도색, 하늘·재 머티리얼, TerrainLayer, `VP_RaidAtmosphere`(AtmosphereConfig → ColorAdjustments·FilmGrain·Vignette·Bloom·Tonemapping). URP Particles/Unlit 투명 설정(키워드·블렌드·큐) |
| `EditorTools/M6VisualSceneApply.cs` (새) | 2a | `ApplyRaid(Scene)`: Global Volume, 카메라 post on, RenderSettings(안개 Exp²·색·밀도, Trilight, skybox, sun), Directional Light → "Sun"(방향·색·세기), Terrain 레이어, 블록아웃 머티리얼 + `GroundSurface`, 물웅덩이 3, AshParticles(Player 자식). `ApplyMenuScene(Scene)`: 카메라 배경 Ash_Black. 메뉴 `Ashfall/Setup/M6/Apply Visual (Open Scene)` |
| `EditorTools/WavWriter.cs` (새, 순수) | 2b | `static byte[] EncodePcm16(float[] samples, int sampleRate, int channels = 1)` |
| `EditorTools/SfxSynth.cs` (새, 순수) | 2b | 레시피 `static float[] Footstep(SurfaceKind, int variant, int rate)`, `Gunshot(WeaponClass, GunshotLayer, int rate)`, `WindLoop`, `StormLoop`, `MetalCollapse(int variant)`, `GeigerClick`, `HideoutDrone`, 공용 `Normalize(peak 0.9)`, `CrossfadeLoop` |
| `EditorTools/M6AudioSetup.cs` (새) | 2b | `CreateAssets()`: WAV 33개(없을 때만) → 임포트 설정 → `Data/Audio/AudioConfig.asset`(없으면 생성, 빈 클립 필드만 채움) |
| `EditorTools/M6AudioSceneApply.cs` (새) | 2b | `ApplyRaid(Scene)`: "AudioDirector"(config). `ApplyHideout(Scene)`: "HideoutMusic"(config). 메뉴 `Ashfall/Setup/M6/Apply Audio (Open Scene)` |
| `EditorTools/M6StormSceneApply.cs` (새) | 2c | `Apply(Scene)`: "AshStorm"(AshStormController + AshStormPresenter, raid = 씬의 RaidController, configs, ashParticles = "AshParticles" 이름으로 찾음 — 없으면 경고·null). 메뉴 `Ashfall/Setup/M6/Apply Storm (Open Scene)` |
| `EditorTools/M6SceneSetup.cs` (새) | 3 | 메뉴 `Ashfall/Setup/Apply M6 Art & Audio`, `ApplyAll()`(§1.6), `ApplyRaidIfAssetsExist(Scene)`, `ApplyMenuIfAssetsExist(Scene)`, `ApplyHideoutIfAssetsExist(Scene)` |
| `EditorTools/M4SceneSetup.cs`, `M5SceneSetup.cs` | 3 | 저장 직전 위 함수 한 줄씩. 주석 "Directional Light 색은 M6" 갱신 |

### 4.8 asmdef·매니페스트 (1단계)
| 대상 | 변경 |
|---|---|
| `Ashfall.Core`, `Ashfall.Player`, `Ashfall.Weapons`, `Ashfall.AI`, `Ashfall.Raid`, `Ashfall.Audio`, `Ashfall.UI`, `Ashfall.Inventory`, `Ashfall.Hideout` | 없음 |
| `Ashfall.EditorTools` | `+ Ashfall.Audio`, `+ Unity.RenderPipelines.Core.Runtime`, `+ Unity.RenderPipelines.Universal.Runtime` |
| `Ashfall.Tests.EditMode` | `+ Ashfall.EditorTools`(합성기·하늘·매핑 순수 함수 테스트), `+ Unity.RenderPipelines.Core.Runtime`, `+ Unity.RenderPipelines.Universal.Runtime`(볼륨 프로필 값 확인) |
| `Ashfall.Tests.PlayMode` | `+ Unity.RenderPipelines.Core.Runtime`, `+ Unity.RenderPipelines.Universal.Runtime`(Volume·카메라 post 확인) |
| `Packages/manifest.json` | `+ com.unity.modules.audio`, `+ com.unity.modules.particlesystem` ⟨Q10⟩ |
새 asmdef 없음. 기능끼리 참조 추가 없음(Audio·Raid·AI·Player·Weapons → Core만).

### 4.9 그 밖
- 입력 변경 없음. 빌드 설정·씬 목록 변경 없음. `GameManager`·`GameFlow`·UI 코드 변경 없음(USS는 이미 팔레트 변수 — 일관성 테스트만).
- 문서 §13.2 폴더 구조 변경 없음(`Art/`·`Audio/` 하위 폴더만 생김).

---

## 5. GDD에 없거나 모호한 질문 (오케스트레이터가 ★추천안을 그대로 채택)

| # | 질문 | 선택지 | ★추천 |
|---|---|---|---|
| Q1 | 씬에 M6을 넣는 방식 | A 새 메뉴 `Apply M6`가 기존 씬을 열어 멱등 수정·제자리 저장 + M4/M5 생성 메뉴가 저장 전에 같은 적용 함수 호출 / B M4·M5 생성 메뉴만 확장(레이드 씬 재생성·NavMesh 재굽기) / C Apply만(M4 재실행 시 M6 사라짐) | **A** — 재굽기 없이 빠르고, 어느 메뉴를 돌려도 결과가 같다 |
| Q2 | §10.2 "VFX Graph" 재 파티클 | A Particle System(Player 자식, World 시뮬레이션) / B VFX Graph(`.vfx`는 코드 생성·손작성 어려움) | **A** — CHANGELOG에 차이 기록, GPU 파티클은 실제 에셋 단계 |
| Q3 | "볼류메트릭 느낌의 Height Fog" | A `RenderSettings` Exp² 거리 안개(Dried_Blood) + 하늘 지평선 띠를 안개색으로 / B 커스텀 Full Screen Pass 높이 안개 셰이더(HLSL 손작성) | **A** — URP 기본 기능, 폭풍 시야 30m도 같은 안개로 |
| Q4 | 그라데이션 하늘 | A 생성 PNG + 내장 `Skybox/Panoramic` / B 손으로 쓴 `.shader` / C `Skybox/Procedural` 색 조절 | **A** — 셰이더 코드 없음, 실제 하늘 그림으로 교체 쉬움 |
| Q5 | 포스트 볼륨 범위 | A 레이드 씬 Global Volume(프로필은 M6 에셋), 파이프라인 기본 프로필 불변 / B `PC_RPAsset` 기본 프로필 교체(모든 씬) | **A** |
| Q6 | "머티리얼은 PaletteSO 토큰 참조" 방식 | A 에디터 동기화(메뉴가 팔레트 → 머티리얼 색 기록, 테스트로 일치 확인) / B 런타임 바인더 컴포넌트(MaterialPropertyBlock) | **A** — 런타임 비용·코드 없음 |
| Q7 | 기존 머티리얼(M2~M4: 적·탈출 구역·터렛) | A 팔레트 색으로 재도색(GUID 유지) / B 그대로 | **A** — "모든 머티리얼" |
| Q8 | 적 상태 색 표시(M3 "M6에서 끈다") | A `AiConfig.showStateIndicator=false` 전역(확인 씬 포함, 인스펙터로 다시 켬) / B 레이드 전용 AiConfig | **A** |
| Q9 | 확인 씬(M1~M3 Test) 아트·오디오 | A 적용 안 함 / B 적용 | **A** — 그레이박스 확인용 |
| Q10 | 내장 모듈 매니페스트 | A audio·particlesystem 명시 + 회귀 테스트 / B 그대로(timeline 전이 의존) | **A** (M5 Q24 전례) |
| Q11 | 지면 재질 판정 | A 콜라이더의 `GroundSurface` 컴포넌트 + Terrain 기본 흙 + 미태그 기본 잔해, 물 = 저수지 웅덩이 트리거(Ignore Raycast) / B Terrain 알파맵 샘플 / C PhysicMaterial 이름 | **A** — 가장 단순, 실제 에셋 교체 때도 컴포넌트만 |
| Q12 | 발소리 리듬 | A 보폭 거리 기반 `StrideCadence`(자세·질주별 보폭 SO) / B AI 소음 펄스 0.5초 재사용 | **A** — 질주·웅크림에서 자연스러움, AI 청각은 그대로 |
| Q13 | 적 발소리 | A 적도 낸다(들리는 거리 밖은 버림) / B 플레이어만 | **A** — 디자인 기둥 3 "발소리로 위치 판단" |
| Q14 | 총성 이벤트 | A 새 `ShotFired`(플레이어·적), `NoiseEmitted`는 AI 청각 전용으로 유지 / B `NoiseEmitted`를 적도 발행(적이 적 총성을 듣게 됨 = M3 규칙 변경) | **A** |
| Q15 | 거리 레이어 선택 | A 구간별 1개 레이어 + 구간 선형 게인(경계 연속), 무기 분류별 세트 / B 경계에서 두 레이어 크로스페이드 / C 무기마다 세트 | **A** |
| Q16 | 소리 전달 지연(343m/s) | A 없음 / B 거리만큼 지연 | **A** — TODO, M7 폴리시 후보 |
| Q17 | 벽 뒤 차폐·저역 통과 | A 없음 / B 레이캐스트 차폐 | **A** |
| Q18 | 은신처 BGM ("BGM 없음(은신처 제외)") | A 은신처에만 자리표시 드론 루프 + `AudioConfig.hideoutMusic` 슬롯 / B 슬롯만(무음) / C 메인 메뉴도 | **A** — 문구가 은신처 BGM을 전제, 실제 곡으로 교체만 하면 됨 |
| Q19 | 일시정지·종료 오버레이 중 소리 | A `AudioListener.pause = Flow.IsWorldPaused`(은신처 음악 예외) / B 계속 재생 | **A** |
| Q20 | AudioMixer | A 없음(SO 음량) / B 믹서 에셋(코드 생성 API가 내부용) | **A** — 음량 설정 화면이 없다 |
| Q21 | 자리표시 재생성 정책 | A WAV는 파일 없을 때만, SO 클립 필드는 비었을 때만 / B 매번 덮어씀 | **A** — 사용자 교체 보호 |
| Q22 | 가이거 계수기 조건(§11 "방사능 구역") | A M6은 재 폭풍 방사능률에만 반응 / B 정유소 구역 볼륨도(M7 방사능 구역 앞당김) | **A** |
| Q23 | 재 폭풍 발생 | A 레이드마다 정확히 1회, 시작 균등 무작위 [180, 1200]초 / B 확률 발생 | **A** — "1회 랜덤" = 시각이 무작위 |
| Q24 | "3분"의 범위·전환 | A Peak 180초 + 앞뒤 전환 20초(총 220초) / B 전환 포함 180초 | **A** — 시야 30m가 3분 유지 |
| Q25 | 예고 | A 없음(Rising 20초가 예고) / B 30초 전 예고음·문구 | **A** — HUD 최소(§12) |
| Q26 | 시야 30m 적용 대상 | A 렌더 안개 + AI 시야 상한 min(기존, 30) / B 렌더만 | **A** — 플레이어만 눈이 머는 불공정 방지 |
| Q27 | 방사능 +1/s (STAT-RAD는 M7) | A 플레이어 누적량만(`RadiationDoseModel`), 효과·UI·이벤트 없음, 방독면·해독제·100 효과 M7 / B A + `RadiationDoseChanged` 이벤트 / C M6에서 안 함 | **A** — 쓰는 곳 없는 이벤트를 만들지 않음, §8.3 값은 반영 |
| Q28 | 폭풍 시간 기준 | A 레이드 경과 시간(일시정지 중 멈춤, AdvanceTime 따름) / B 실제 시간 | **A** |
| Q29 | 은신처·메인 메뉴 비주얼 | A 카메라 배경 Ash_Black만 / B 배경 그림·볼륨 | **A** — 메뉴형 UI |
| Q30 | 포스트·하늘 수치의 원본 | A `AtmosphereConfigSO`가 원본, 볼륨 프로필·하늘 PNG는 메뉴가 매번 다시 쓰는 파생물 / B 볼륨 프로필을 직접 편집 | **A** — §0 "수치는 SO", 팔레트 동기화와 같은 방식 |

---

## 6. 작업 단계

규칙(M3~M5와 같음): **원본 저장소에서 배치모드 Unity는 한 번에 하나**(1단계, 3단계, 4단계, 리뷰 수정 — 순차). 병렬 단계는 1단계 완료 상태를 scratchpad로 복사(`Assets`·`Packages`·`ProjectSettings`·`Library`)한
**프로젝트 사본에서만** 배치모드를 돌리고, 원본에는 **자기 소유 파일만** 쓴다(에셋·씬은 원본에 만들지 않는다 — 사본에서 메뉴 실행). 각 단계는 끝에 이 문서 맨 아래 "N단계 구현 메모"를 덧붙인다.
새 `.meta`는 만들지 않는다. 테스트는 실제 persistentDataPath를 쓰지 않는다(M5 `SaveSandbox` 유지).

### 1단계 — 계약 (원본)
**1a (gameplay-programmer) — 40~50분**
- Core 전부(§4.1): `PaletteSO`, 이벤트·enum, `IAshStormInfo`, `StrideCadence`(구현), ServiceRegistry 주석.
- 껍데기(필드·기본값·툴팁만, 동작 없음): `Raid/Data/AtmosphereConfigSO.cs`, `Raid/Data/AshStormConfigSO.cs`, `Audio/Data/AudioConfigSO.cs`, `Audio/Data/SurfaceKind.cs`, `Audio/Runtime/GroundSurface.cs`(구현),
  `PlayerConfigSO`·`AiConfigSO` 필드 추가, `EditorTools/M6Names.cs`.
- asmdef·매니페스트(§4.8).
- 테스트(새): `EditMode/StrideCadenceTests.cs`(첫 걸음 = 보폭 절반, 일정 속도에서 걸음 간격, 긴 프레임 1걸음, 멈춤 → Reset, 보폭 0), `EditMode/PaletteTests.cs`(Get·GddName 7개), `EditMode/M6ModuleTests.cs`(매니페스트·형식 로드), `EditMode/M6EventTests.cs`(새 이벤트 Publish 할당 0 — M3 `EventBus` 할당 테스트 방식).
**1b (data-designer, 1a 직후) — 25~30분**
- `EditorTools/M6DataSetup.cs`(§4.7) + 원본에서 `-executeMethod Ashfall.EditorTools.M6DataSetup.CreateData` 실행(데이터 에셋만 원본에 생김 — 2a·2b·2c가 같은 사본에서 쓰므로).
- 테스트: 새 `EditMode/M6DataAssetTests.cs`(팔레트 HEX = §10.1, Atmosphere §3.2·Storm §3.3 값, 보폭 값, `showStateIndicator=false`, 재실행 바이트 동일), `EditMode/M3DataAssetTests.cs` 한 줄(`showStateIndicator` → false).
- 마감: 원본 EditMode 전체 통과, PlayMode 전체 통과(기존 동작 변화 없음).

### 2단계 — 병렬 (각자 프로젝트 사본)
| 단계 | 담당 | 소유 파일(원본에 쓰는 것) | 마감 | 예상 |
|---|---|---|---|---|
| **2a** 비주얼 | gameplay-programmer | `EditorTools/PaletteMaterialMap.cs`, `SkyPanorama.cs`, `M6ArtSetup.cs`, `M6VisualSceneApply.cs` / 테스트 새 `EditMode/SkyPanoramaTests.cs`(천정 = Ash_Black, 지평선 = Rust_Red, 지평선 아래 = Dried_Blood, 태양 방향 픽셀이 가장 밝음·흐림 반영, UV 왕복), `EditMode/PaletteConsistencyTests.cs`(USS 변수 7개 = 팔레트 HEX, 매핑 표의 모든 머티리얼 `_BaseColor` = 토큰 — 에셋 있을 때), `EditMode/AtmosphereAssetTests.cs`(VP_RaidAtmosphere: 채도 −30·그레인 0.3·비네트 0.35·Bloom ≤ 0.3·틴트 적색 우세·Tonemapping Neutral, 하늘 머티리얼 셰이더 Panoramic, 재 머티리얼 투명, 메뉴 재실행 시 GUID 불변), 새 `PlayMode/RaidVisualSmokeTests.cs`(레이드 씬 로드 → 카메라 post on, Global Volume·프로필, 안개 Exp²·Dried_Blood·밀도 = 450m, skybox = M6_Sky, sun = "Sun" 고도 8°±0.5, AshParticles가 Player 자식·World·재생 중·상한, 블록아웃 렌더러에 기본 머티리얼 없음, 매핑 대상에 `GroundSurface`, 웅덩이 3개 = 트리거·Ignore Raycast·Water, 콘솔 오류 없음) | 사본에서 2a 메뉴 실행(Raid 씬 열고 적용) + 테스트 통과 | 60~75분 |
| **2b** 오디오 | gameplay-programmer(두 번째) | `Audio/Logic/*`, `Audio/Runtime/*`(GroundSurface 제외), `EditorTools/WavWriter.cs`, `SfxSynth.cs`, `M6AudioSetup.cs`, `M6AudioSceneApply.cs` / 테스트 새 `EditMode/WavWriterTests.cs`(RIFF·fmt·data 크기, PCM16 클램프), `EditMode/SfxSynthTests.cs`(레시피별 길이·피크 ≤ 0.9·결정적·루프 이음매 차 작음·무음 아님), `EditMode/GunshotMixTests.cs`(경계 50/250/1200, 게인 연속·단조 감소, 너머 = 안 들림, 0m = 1), `EditMode/FootstepMixTests.cs`, `EditMode/SurfaceResolverTests.cs`, `EditMode/VoiceAllocatorTests.cs`(빈 칸, 빼앗기 우선순위·나이, 전부 높은 우선순위면 −1), `EditMode/GeigerClickModelTests.cs`(0 → 0, 평균 클릭 수 ≈ 6×rate(시드 고정), 상한), `EditMode/IntervalSchedulerTests.cs`, `EditMode/AudioConfigAssetTests.cs`(재질 4개 모두 클립 ≥ 1, 분류 3세트 근/중/원, 루프 클립 loop 설정, 임포트 모노, 메뉴 재실행 시 WAV 바이트·GUID 불변, 사용자 클립 보존), 새 `PlayMode/AudioFlowTests.cs`(코드로 만든 씬: AudioDirector + 청취자 + `GroundSurface(Metal)` 바닥 → `FootstepTaken` → `LastPlayed` 클립이 금속 세트·위치·게인 / 지형 = 흙 / 웅덩이 트리거 = 물 / 들리는 거리 밖 = 재생 없음, `ShotFired` 30·100·400·1500m → 근·중·원·없음, 25발 연속 → 풀 빼앗기·예외 없음, `Flow.SetWorldPaused(true)` → `AudioListener.pause`, 300 이벤트 동안 GC 0, 레이드·메인 메뉴에 음악 소스 없음) | 사본에서 2b 메뉴 실행 + 테스트 통과 | 75~90분 |
| **2c** 재 폭풍 + 플레이어·무기 발행 | gameplay-programmer(세 번째) | `Raid/Logic/FogMath.cs`, `AshStormSchedule.cs`, `AshStormModel.cs`, `Raid/Runtime/AshStormController.cs`, `AshStormPresenter.cs`, `Player/RadiationDoseModel.cs`, `Player/PlayerHealth.cs`, `Player/PlayerMotor.cs`, `Weapons/WeaponController.cs`, `EditorTools/M6StormSceneApply.cs` / 테스트 새 `EditMode/AshStormTests.cs`(시작 창 안·시드 결정적, 단계 순서 Pending → Rising → Peak → Falling → Over 각 1회, Peak 가시거리 30·방사능 1, 전환 선형, Over 뒤 다시 없음, Reschedule은 Pending에서만, 큰 시간 점프(AdvanceTime)도 올바른 단계), `EditMode/FogMathTests.cs`(왕복, threshold 0.05에서 d = 가시거리 → 투과 0.05, ∞ → 0), `EditMode/RadiationDoseTests.cs`, `EditMode/PlayerSoundEventTests.cs`(가능하면 순수 걸음 선택 함수: 자세·질주 → 걸음) / 새 `PlayMode/AshStormFlowTests.cs`(레이드 씬 + 시드: `ForceStartNow` → `AshStormChanged` 순서, `IAshStormInfo` 값, 안개 밀도 증가·Peak 밀도 = 30m, 파티클 방출 배율, 플레이어 Peak 10초 ≈ 10 선량(±0.5), 일시정지 중 단계·선량 정지, 끝나면 안개·하늘·파티클 복원, 씬 언로드 후 skybox 원본), 새 `PlayMode/PlayerSoundEventTests.cs`(M2 확인 씬 방식: 걷기 → `FootstepTaken` 간격 ≈ 보폭/속도, 질주 걸음 Sprint, 정지 → 없음, 사격 1발 → `ShotFired(Player)` 1회) | 사본 EditMode·PlayMode 통과 | 60~75분 |
| **2d** AI | ai-programmer | `AI/Sensing/SightCone.cs`, `AI/Runtime/AiDirector.cs`, `AI/Runtime/EnemyAgent.cs` / 테스트 `EditMode/AiSightTests.cs`에 cap 케이스 추가(∞ = 기존, 30 < 60 → 30, 웅크림 36 vs 30 → 30, 엎드림 18 vs 30 → 18), 새 `PlayMode/AiStormSightTests.cs`(M3 확인 씬 + 가짜 `IAshStormInfo` 등록: 45m 서 있는 플레이어 — 폭풍 없음 보임 / VisibilityM 30 안 보임 / 20m 보임, 청각은 그대로), 새 `PlayMode/EnemySoundEventTests.cs`(순찰하는 적 → `FootstepTaken(Enemy)` 주기, 사망 후 없음, 교전 중 발사 수 = `ShotFired(Enemy)` 수, 적 총성으로 다른 적이 Alert 되지 않음(M3 규칙 회귀)), `EditMode/AiAllocationTests.cs`(새 오버로드 할당 0) | 사본 EditMode·PlayMode(AI 관련) 통과, 기존 `AiPerformanceTests` 예산 유지 | 35~45분 |

- 접점: 모두 1단계 Core API·SO 필드·`M6Names` 이름만 서로 기대한다. 2c `M6StormSceneApply`는 "AshParticles" 이름으로 파티클을 찾는다(2a가 만드는 이름 — 사본에서 2a 파일이 없으면 null + 경고, 2c 테스트는 코드로 만든 파티클로).
- 2a·2c는 같은 레이드 씬을 각자 사본에서 수정한다 — 원본 씬은 3단계가 한 번에 만든다.
- `TestData.cs`·`M3TestData.cs`·`M4TestData.cs`·`M5TestData.cs`·`UiTestFakes.cs`·Core 파일·`M6Names.cs`·`M6DataSetup.cs`는 2단계에서 아무도 고치지 않는다. 계약 변경은 오케스트레이터 경유.
- 가짜 `IAshStormInfo`가 여러 테스트에 필요하면 각 테스트 파일 안에 둔다(공용 도우미 수정 금지).

### 3단계 — 통합 (gameplay-programmer, 원본) — 40~50분
- 새 `EditorTools/M6SceneSetup.cs`(§1.6), `M4SceneSetup.cs`·`M5SceneSetup.cs` 한 줄씩.
- 원본에서 `-executeMethod Ashfall.EditorTools.M6SceneSetup.ApplyAll` **2회**: 두 번째 실행에서 `.meta` GUID 불변, 데이터·WAV·머티리얼·프로필 바이트 동일, 씬의 루트 오브젝트 수·이름 동일(씬 YAML의 로컬 fileID 차이는 M4처럼 허용하되 "있으면 재설정" 덕분에 같기를 기대).
- `M4SceneSetup.CreateAll`을 한 번 돌려 M6이 유지되는지 확인(선택, 시간이 되면 — NavMesh 굽기 포함 수 분).
- 테스트: 새 `PlayMode/M6RaidSceneSmokeTests.cs`(레이드 씬에 AudioDirector·AshStorm·Global Volume·AshParticles·Sun이 하나씩, 참조 연결, 서비스 `IAshStormInfo` 등록, 콘솔 오류 없음), `PlayMode/HideoutSceneSmokeTests.cs`에 음악 소스 1개·배경색 추가, `PlayMode/RaidSceneSmokeTests.cs`에 MainMenu 음악 없음.
- 마감: 원본 EditMode 전체, PlayMode 전체 통과.

### 4단계 — 검증 (test-engineer, 원본) — 60~75분
- 새 `PlayMode/M6PerformanceTests.cs`: 레이드 씬, 적 20명 교전(기존 `RaidPerformanceTests` 방식) **+ 재 폭풍 Peak 강제** + 오디오 켬: AI·투사체·프레임 예산 유지, `AudioDirector`+`AshStormController` 갱신 평균 ≤ 0.2ms, 측정 구간 GC 0(오디오·폭풍 경로), 파티클 수 ≤ `ashMaxParticlesStorm`.
- 새 `PlayMode/M6RaidFlowTests.cs`: 흐름 레이드(은신처 → 레이드) 중 폭풍을 `AdvanceTime`으로 불러 Peak에서 탈출 → 은신처 진입 시 `AudioListener.pause=false`·안개 원래 씬 값·skybox 원본 에셋(공유 머티리얼 변조 없음), 선량은 다음 레이드에서 0.
- Windows 빌드(씬 4개), 빌드 로그에서 셰이더 변형 경고(안개·Panoramic·Particles Unlit) 확인, AC 체크, PlayMode 2회 연속.

### 5단계 — 리뷰 (code-reviewer, 읽기 전용) — 10분
- 의존 방향(Audio·Raid·AI·Player·Weapons → Core만), 싱글톤 1개(AudioDirector·AshStormController 정적 인스턴스 없음), timeScale 쓰기 없음(Audio는 AudioListener만), 이벤트·서비스 해지 짝,
  런타임 할당(이벤트 경로), 공유 에셋 런타임 변조(skybox·머티리얼), 수치 하드코딩(SO 밖 숫자 — 레벨 배치 상수 제외), 자리표시 TODO 표시, M7 기능 선행 구현 여부.

### 리뷰 수정 — gameplay/ai — 30~40분
- 지적 수정 + 원본 EditMode·PlayMode(2회)·빌드 재확인, `docs/CHANGELOG_GDD.md` "M6 설계 결정" 기록, 한국어 커밋(`M6: 아트 & 오디오 패스 — … (ART, AUD, SYS-MAP 재 폭풍)`).

**예상 총 소요: 약 5~6시간**(1a 45 + 1b 30 + 병렬 최장 2b 80~90 + 3단계 45 + 4단계 70 + 리뷰 45).

---

## 7. AC별 테스트 계획

### AC1 — §10 팔레트·포스트프로세싱·하늘·재 파티클
| 종류 | 테스트 |
|---|---|
| EditMode `PaletteTests`(1a), `M6DataAssetTests`(1b) | 토큰 7개, Palette.asset HEX = §10.1(대소문자 무관, `ColorUtility.ToHtmlStringRGB`), GddName |
| EditMode `PaletteConsistencyTests`(2a) | `Ashfall.uss` 변수 7개 = 팔레트, 매핑 머티리얼·기존 4개 `_BaseColor` RGB = 토큰, 블록아웃 매핑 표에 M4 Shape 전부 존재 |
| EditMode `SkyPanoramaTests`(2a) | 그라데이션 끝값, 태양 위치·흐림, 지평선 아래 = 안개색 |
| EditMode `AtmosphereAssetTests`(2a) | 볼륨 값(§3.2), 머티리얼 셰이더·투명 설정, 재실행 GUID·바이트 |
| EditMode `M6DataAssetTests`(1b) | `showStateIndicator=false` |
| PlayMode `RaidVisualSmokeTests`(2a), `M6RaidSceneSmokeTests`(3) | 씬 구성(§6 2a) |
| 빌드(4) | 셰이더 포함·오류 없음 |
| 에디터 확인 | §7.1 V1~V8 |

### AC2 — §11 지면별 발소리, 총성 거리 레이어(+ 3D 공간음·환경음·BGM 규칙)
| 종류 | 테스트 |
|---|---|
| EditMode `StrideCadenceTests`(1a) | 걸음 리듬 |
| EditMode `GunshotMixTests`·`FootstepMixTests`·`SurfaceResolverTests`·`VoiceAllocatorTests`·`GeigerClickModelTests`·`IntervalSchedulerTests`(2b) | 순수 규칙 |
| EditMode `WavWriterTests`·`SfxSynthTests`·`AudioConfigAssetTests`(2b) | 자리표시 생성·임포트·보존 |
| EditMode `M6EventTests`(1a), `AiAllocationTests`(2d) | 할당 0 |
| PlayMode `AudioFlowTests`(2b) | 재질 → 세트, 거리 → 레이어, 풀, 일시정지, 3D(spatialBlend 1), 음악 없음 |
| PlayMode `PlayerSoundEventTests`(2c), `EnemySoundEventTests`(2d) | 플레이어·적 발행, 적 총성을 적이 못 들음(M3 회귀) |
| PlayMode `M6PerformanceTests`(4) | 예산·GC |
| 에디터 확인 | §7.1 A1~A9 |
- 배치모드에서 오디오 장치가 없을 수 있으므로 PlayMode는 `AudioSource.isPlaying` 대신 `AudioDirector.LastPlayed`·풀 상태(요청 기록)를 확인한다(위험 R3).

### AC3 — 재 폭풍 이벤트
| 종류 | 테스트 |
|---|---|
| EditMode `AshStormTests`·`FogMathTests`·`RadiationDoseTests`(2c), `AiSightTests`(2d) | 일정·단계·값·시야 상한·선량 |
| EditMode `M6DataAssetTests`(1b) | 180초·30m·1/s·창 |
| PlayMode `AshStormFlowTests`(2c) | 안개·파티클·하늘·선량·일시정지·복원 |
| PlayMode `AiStormSightTests`(2d) | AI 시야 30m |
| PlayMode `M6RaidFlowTests`(4) | 흐름 레이드 중 폭풍 → 탈출 → 은신처 원상 |
| 에디터 확인 | §7.1 S1~S5 |

### 회귀
- 기존 EditMode 995 · PlayMode 77 통과(바뀌는 단언: `M3DataAssetTests.showStateIndicator` 하나). `SightCone` 3인자 오버로드·`NoiseEmitted` 동작 불변, AI 성능 예산 유지.
- 확인 씬(M1~M3)은 변경 없음 — `M2SceneSmokeTests`·`M3SceneSmokeTests` 무변경 통과(AudioDirector가 없어도 이벤트 발행은 무해).

### 7.1 에디터 확인 항목 (자동 테스트로 못 보는 것 — 사람이 에디터·빌드에서)
비주얼 (Raid_CrimsonBasin을 Boot부터 또는 직접 Play)
- V1 첫 화면 인상: 검붉은 황혼, 채도 낮음, 붉은 틴트, 필름 그레인 보임, 화면 가장자리 어두움(비네트), Bloom 과하지 않음.
- V2 하늘: 천정 Ash_Black → 지평선 Rust_Red, 태양이 낮게(서남서) 흐리게, **Sun 라이트 그림자 방향이 하늘의 태양 위치와 맞음**.
- V3 원경: 지형이 Dried_Blood 안개로 사라지며 하늘 지평선과 이음매가 두드러지지 않음.
- V4 재 파티클: 주변에 상시 떨어짐, 질주해도 앞에 비는 곳이 없음, 너무 빽빽하지 않음, 뷰모델·카메라 바로 앞 깜빡임 없음.
- V5 머티리얼: 콘크리트·녹슨 금속·잔해 구분, 적이 지형과 구분됨, 탈출 구역 표시 가독성, 웅덩이가 젖어 보임.
- V6 지형 텍스처 타일링 반복이 거슬리는 정도.
- V7 은신처·메인 메뉴: 배경 Ash_Black, UI 색 그대로.
- V8 Windows 빌드에서 V1~V4가 에디터와 같음(셰이더 변형 누락 없음 — 안개·하늘·재가 사라지지 않음).
오디오 (헤드폰 권장)
- A1 걷기 흙(지형)·금속(차량 지붕/철로)·잔해(콘크리트 벽 위·잔해 더미)·물(저수지 웅덩이) 소리 구분, 질주·웅크림·엎드림 음량·리듬 차이.
- A2 적 발소리가 방향(좌우)으로 들림, 25~40m 밖에서는 안 들림.
- A3 총성: 내 사격(근), 적 교전을 100m·400m 거리에서(중·원 메아리) — `AudioDirector` ContextMenu `Debug/총성 근·중·원`으로도 확인.
- A4 바람 상시, 금속 붕괴가 가끔 멀리서(ContextMenu `Debug/금속 붕괴 지금`).
- A5 일시정지(Esc)·종료 오버레이 중 소리 멈춤, 해제 후 이어짐.
- A6 레이드·메인 메뉴에 음악 없음, 은신처에서만 드론 BGM, 은신처 일시정지 개념 없음(계속 재생).
- A7 소리 끊김·클릭 노이즈(루프 이음매), 많은 총성에서 소리가 사라지는 정도(풀 24).
- A8 자리표시 품질이 "거슬리지 않을 정도"인지(실제 에셋 우선순위 판단용).
- A9 실제 WAV 하나를 같은 경로에 덮어쓴 뒤 `Apply M6` 재실행 → 교체가 유지되는지.
재 폭풍
- S1 `AshStorm` 오브젝트 ContextMenu `Debug/재 폭풍 지금 시작` → 20초 동안 안개·재·바람이 짙어짐, Peak에서 30m 너머가 거의 안 보임.
- S2 Peak 중 적에게 35m에서 서 있어도 발견되지 않고 20m에서는 발견됨.
- S3 가이거 클릭이 폭풍 강도에 따라 빨라짐, 끝나면 멈춤.
- S4 폭풍이 끝나면 하늘·태양·안개·재가 원래대로.
- S5 폭풍 중 일시정지 → 진행 멈춤. Play 중지 후 `M6_Sky.mat` 에셋이 바뀌지 않았는지(git diff 없음).
성능
- P1 개발 빌드 + Profiler, 1080p, 폭풍 Peak + 교전: GPU(파티클·포스트) 시간, GTX 1660급 60fps 가능성(최종 판정은 M7).

### 7.2 위험
- R1 셰이더 변형 자동 제거로 빌드에서 안개가 빠질 수 있음 → 레이드 씬은 항상 Exp² 안개 켬(§1.3), 4단계 빌드 확인. 그래도 빠지면 Graphics 설정 Fog Modes를 Custom(Exp²)으로(ProjectSettings 수정은 오케스트레이터 승인).
- R2 URP Particles/Unlit 투명 설정을 코드로 맞추는 키워드·블렌드 조합이 버전에 민감 → 2a가 사본에서 렌더 큐·키워드를 테스트로 고정, 사람 확인 V4.
- R3 배치모드 오디오 장치 부재 → 요청 기록으로 검증(§7 AC2).
- R4 `Skybox/Panoramic`·`Universal Render Pipeline/Particles/Unlit`이 빌드에 포함되는지(머티리얼 참조로 포함되어야 함) → 4단계 빌드.
- R5 WAV 합성 결정성(같은 기계에서 바이트 동일) — 다른 기계에서 `Math.Sin` 끝자리 차이가 나도 "없을 때만 생성" 정책이라 커밋된 파일은 바뀌지 않는다.

---

## 8. CHANGELOG_GDD.md "M6 설계 결정" 초안

```markdown
## 2026-10-?? — M6 설계 결정 (§8.3, §10, §11, §13.1, §13.2, §13.3, §14, §15 M6, §4.3 STAT-RAD, §7)
문서 본문은 바꾸지 않았다. 아래는 M6 구현을 위한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
사용자 지시로 GDD에 없거나 모호한 30개 항목(설계 문서 `docs/design/M6_design.md` §5의 Q1~Q30)은 가장 단순한 추천안으로 정했다.
코드의 결정 지점에는 `TODO(GDD-?) Qn` 주석이 있다.
- **§10.2 에셋 (사용자 지시):** 외부 다운로드·Asset Store 에셋을 쓰지 않는다. 소리(WAV 33개)·하늘·재·지형 텍스처·머티리얼·볼륨 프로필은
  에디터 메뉴 `Ashfall/Setup/Apply M6 Art & Audio`가 코드로 만든 자리표시다(목록·교체 위치는 설계 §2.2·§2.3). WAV는 파일이 없을 때만,
  SO 클립 필드는 비었을 때만 채워 사용자 교체를 보존한다(Q21).
- **§10.2 VFX Graph → Particle System (Q2):** 재 파티클은 Player 자식의 Particle System(World 시뮬레이션, 반경 30m 박스)이다. `.vfx`는 코드로 만들기 어렵다.
  VFX Graph 패키지(§13.1)는 설치된 채 쓰지 않는다.
- **§10.2 Height Fog (Q3):** URP 기본 거리 안개(Exponential², Dried_Blood) + 하늘 지평선 띠를 안개색으로 칠해 높이 안개 "느낌"을 낸다. 볼류메트릭·높이 안개 셰이더는 만들지 않았다.
  레이드 씬은 빌드 셰이더 변형 유지를 위해 항상 안개를 켠다.
- **§10.2 하늘 (Q4):** 생성 파노라마 PNG(천정 Ash_Black → 지평선 Rust_Red, 고도 8° 흐린 태양) + 내장 `Skybox/Panoramic`, 같은 방향 Directional Light.
- **§10.2 포스트 (Q5, Q30):** 레이드 씬 Global Volume. 채도 −30, Film Grain 0.3, Vignette 0.35는 GDD 값, 붉은 틴트(Ember 15%)·대비 10·Bloom 0.15/임계 1.1·Tonemapping Neutral은 임시값.
  수치 원본은 `AtmosphereConfigSO`이고 볼륨 프로필·하늘 텍스처는 메뉴가 다시 쓰는 파생물이다. 은신처·메인 메뉴는 카메라 배경 Ash_Black만(Q29).
- **§10.2·§14 PaletteSO (Q6, Q7):** `PaletteSO`(Core, 토큰 7개). 머티리얼은 에디터 메뉴가 팔레트 색을 기록해 "토큰 참조"를 보장하고 테스트로 확인한다(런타임 바인더 없음).
  기존 M2~M4 머티리얼(적·탈출 구역·터렛)도 팔레트로 다시 칠했다(GUID 유지).
- **M3 결정 이행 (Q8):** 적 상태 색 표시를 껐다(`AiConfig.showStateIndicator=false`, 확인 씬 포함).
- **§11 발소리 (Q11~Q13):** 지면 재질 흙/금속/잔해/물은 콜라이더의 `GroundSurface` 컴포넌트(Audio)로 정한다. 지형 = 흙, 표시 없는 콜라이더 = 잔해, 물 = 저수지 웅덩이 트리거.
  걸음은 보폭 거리 기반(`StrideCadence`, Core)으로 플레이어·적 모두 `FootstepTaken` 이벤트를 낸다. AI 청각용 발소리 소음(0.5초)은 그대로.
- **§11 총성 (Q14~Q17):** 새 이벤트 `ShotFired`(플레이어·적). 거리 근 ≤ 50m / 중 ≤ 250m / 원 ≤ 1200m 중 한 레이어, 게인 구간 선형(임시값), 무기 분류별 세트.
  적 총성은 계속 적 AI가 듣지 않는다(`NoiseEmitted`는 AI 전용, M3 규칙 유지). 소리 지연·차폐 없음.
- **§11 환경음·BGM (Q18~Q20, Q22):** 바람(상시, 폭풍 때 커짐), 멀리서 무너지는 금속(40~120초 무작위), 가이거 계수기는 M6에 방사능 구역이 없어 재 폭풍 방사능률에만 반응한다.
  BGM은 은신처에만(자리표시 드론, `AudioConfig.hideoutMusic`). 일시정지·종료 오버레이 중 `AudioListener.pause`. AudioMixer 없음. 3D 공간음은 Unity 팬 + 순수 함수 게인.
- **§8.3 재 폭풍 (Q23~Q28):** 레이드마다 1회, 경과 180~1200초 중 무작위 시작, 전환 20초 + 최대 강도 180초 + 전환 20초. 시간은 레이드 경과 시간(일시정지 중 멈춤).
  Peak 시야 30m = 렌더 안개 + AI 시야 상한 min(기존, 30). 예고 없음. Raid가 `IAshStormInfo`(Core 서비스)와 `AshStormChanged` 이벤트를 제공한다.
- **§4.3 STAT-RAD (Q27):** 재 폭풍 중 플레이어 방사능 누적량만 쌓는다(+1/s × 강도). 100 도달 효과·해독제·방독면·화면 표시는 M7.
- **§13.2·§13.3:** `Ashfall.Audio`에 첫 코드(AudioDirector — 씬 오브젝트, 정적 인스턴스 없음). EditorTools가 Audio·URP 런타임을, 테스트 asmdef가 EditorTools·URP 런타임을 참조한다.
  내장 모듈 audio·particlesystem을 매니페스트에 명시했다(Q10).
- **§14 스키마에 없는 추가:** `AtmosphereConfigSO`·`AshStormConfigSO`(Raid), `AudioConfigSO`(Audio), `PlayerConfigSO` 보폭 4개, `AiConfigSO` 보폭·속도 4개.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 맑을 때 가시거리 450m·안개 임계 0.05, 태양 방위 245°·고도 8°·흐림, 환경광, 틴트·대비·Bloom,
  재 파티클 수·크기·속도, 폭풍 시작 창·전환 시간·폭풍 색·배율, 블록아웃 머티리얼·재질 매핑, 걸음 음량·들리는 거리, 총성 거리·게인, voice 24, 환경음 간격·거리·음량,
  가이거 클릭률, 보폭, 모든 자리표시 소리·텍스처.
```

---

## 9. 1단계 계약 파일 목록 (요약)

원본에 1단계가 만들거나 고치는 파일(이후 계약 변경은 오케스트레이터 경유):
- Core 새: `Core/Data/PaletteSO.cs`, `Core/Services/IAshStormInfo.cs`, `Core/Movement/StrideCadence.cs`
- Core 수정: `Core/Events/GameEvents.cs`, `Core/Services/ServiceRegistry.cs`(주석)
- 껍데기: `Raid/Data/AtmosphereConfigSO.cs`, `Raid/Data/AshStormConfigSO.cs`, `Audio/Data/AudioConfigSO.cs`, `Audio/Data/SurfaceKind.cs`, `Audio/Runtime/GroundSurface.cs`(구현)
- SO 필드: `Player/PlayerConfigSO.cs`(보폭 4), `AI/Data/AiConfigSO.cs`(보폭·속도 4)
- EditorTools: `EditorTools/M6Names.cs`, (1b) `EditorTools/M6DataSetup.cs`
- 설정: `Scripts/EditorTools/Ashfall.EditorTools.asmdef`, `Tests/EditMode/Ashfall.Tests.EditMode.asmdef`, `Tests/PlayMode/Ashfall.Tests.PlayMode.asmdef`, `Packages/manifest.json`
- 테스트: 새 `Tests/EditMode/StrideCadenceTests.cs`, `PaletteTests.cs`, `M6ModuleTests.cs`, `M6EventTests.cs`, (1b) `M6DataAssetTests.cs` / 수정 (1b) `M3DataAssetTests.cs`
- 데이터 에셋(1b가 원본에서 메뉴로 생성): `Data/Art/Palette.asset`, `Data/Raid/AtmosphereConfig.asset`, `Data/Raid/AshStormConfig.asset`, 기존 `PlayerConfig.asset`·`AiConfig.asset` 값 기록

---

## 질문 결정 (오케스트레이터, 2026-10-22)
- Q1~Q30 모두 ★추천안(A)을 그대로 채택한다. 사용자 확인 없이 진행하는 해석이므로 전부 CHANGELOG "M6 설계 결정"에 기록한다.
- CLAUDE.md의 "중복 줄" 정리 요청은 확인 결과 이미 한 줄뿐이라 변경 없음(M6 커밋에서 '현재 진행'만 갱신).

---

## 1a 구현 메모 (gameplay-programmer, 2026-10-22)
결과: 원본 EditMode 1054/1054 통과(기존 1021 + 새 33: StrideCadenceTests 12, PaletteTests 10, M6ModuleTests 6, M6EventTests 5). PlayMode는 1b 뒤에 돌린다(생략). 커밋 안 함. CHANGELOG_GDD는 건드리지 않음.
Unity가 `Packages/packages-lock.json`의 audio·particlesystem depth를 1 → 0으로 갱신했다(같이 커밋). `Ashfall.Audio`에 스크립트가 생겨 "will not be compiled" 경고가 사라졌다.

바뀐/새 파일
- Core 새: `Core/Data/PaletteSO.cs`(PaletteToken 포함), `Core/Services/IAshStormInfo.cs`, `Core/Movement/StrideCadence.cs`.
- Core 수정: `Core/Events/GameEvents.cs`(SoundSource·FootstepGait·FootstepTaken·ShotFired·AshStormPhase·AshStormChanged), `Core/Services/ServiceRegistry.cs`(주석).
- 껍데기: `Raid/Data/AtmosphereConfigSO.cs`(TonemapChoice·FilmGrainChoice 포함), `Raid/Data/AshStormConfigSO.cs`, `Audio/Data/AudioConfigSO.cs`(SurfaceFootsteps·GunshotSet·GaitValues 포함), `Audio/Data/SurfaceKind.cs`, `Audio/Runtime/GroundSurface.cs`.
- SO 필드: `Player/PlayerConfigSO.cs`(strideWalkM 1.5·strideSprintM 2.2·strideCrouchM 0.8·strideProneM 0.6), `AI/Data/AiConfigSO.cs`(footstepStrideWalkM 1.5·footstepStrideRunM 2.2·footstepRunSpeedMps 3.5·footstepMinSpeedMps 0.3). `showStateIndicator` 기본값은 true 그대로(1b 데이터 메뉴가 false 기록).
- EditorTools: `EditorTools/M6Names.cs`.
- 설정: EditorTools asmdef +Ashfall.Audio·URP Core/Universal Runtime, EditMode 테스트 asmdef +Ashfall.EditorTools·URP 2개, PlayMode 테스트 asmdef +URP 2개, manifest +com.unity.modules.audio·particlesystem.
- 테스트 새: `EditMode/StrideCadenceTests.cs`, `PaletteTests.cs`, `M6ModuleTests.cs`, `M6EventTests.cs`.

공개 API 최종 이름 (설계 §4.1 그대로 + 추가만)
- `PaletteSO`: 필드 `ashBlack, driedBlood, rustRed, ember, ashGrey, bone, toxic`(초기값 = §10.1 HEX, sRGB), `Color Get(PaletteToken)`(모르는 값 = 마젠타), `static string GddName(PaletteToken)`, **추가** `static string GddHex(PaletteToken)`("#0D0807" 대문자 — 1b 데이터 메뉴·테스트의 기준값).
- 이벤트 생성자: `FootstepTaken(Vector3 position, FootstepGait gait, SoundSource source)`, `ShotFired(Vector3 position, WeaponDefinitionSO weapon, SoundSource source)`, `AshStormChanged(AshStormPhase phase)`.
- `StrideCadence`: `bool Tick(float horizontalDistanceM, float strideLengthM)`, `void Reset()`, **추가** 읽기 `bool IsMoving`, `float AccumulatedM`.
  해석: 거리 ≤ 0·NaN 또는 보폭 ≤ 0·NaN이면 Reset(멈춤)하고 false. 이동 시작 틱은 누적 = 보폭/2 + 이번 거리. 걸음 뒤 남은 거리가 여전히 보폭 이상이면(긴 프레임) 0으로 버린다. 보폭이 틱마다 바뀌어도 그 틱의 보폭으로 비교(누적은 유지).
- `IAshStormInfo`: 설계 그대로.
- `AtmosphereConfigSO`: §3.2 필드 이름 그대로. `filmGrainType`은 **`FilmGrainChoice`**(Raid enum, URP `FilmGrainLookup`과 같은 이름·순서 `Thin1, Thin2, Medium1~6, Large01, Large02`, Custom 제외) — 설계의 "Medium"은 `Medium1`로 정함. 2a는 이름(또는 같은 정수값)으로 URP 값에 대응시킨다.
  `sunAzimuthDeg`의 기준은 툴팁에 "+Z = 0, 시계 방향"(245° = 서남서)으로 적었다 — 2a `SkyPanorama.SunDirection`이 이 기준을 따를 것. `skyTextureSize`는 `Vector2Int`, `ashSizeM`·`ashFallSpeedMps`는 `Vector2`(최소, 최대), override 두 개는 `Texture2D`.
- `AshStormConfigSO`: §3.3 필드 이름 그대로(`startWindowSec`는 `Vector2`).
- `AudioConfigSO`: §3.5 필드 이름 그대로. 형식 결정:
  - `gaitVolume`·`gaitAudibleM`는 **`GaitValues` 구조체**(`walk, sprint, crouch, prone` + `float Get(FootstepGait)`) — 배열 인덱스 대신.
  - `footsteps`는 `SurfaceFootsteps[]`(class: `surface, clips(AudioClip[]), volume`), 기본 4원소 Dirt·Metal·Debris·Water(볼륨 1, 클립 빈 배열).
  - `gunshots`는 `GunshotSet[]`(class: `weaponClass, near, mid, far, volume`), 기본 3원소 Pistol·Smg·AssaultRifle(볼륨 1).
  - `surfaceMask`는 `LayerMask` 기본 `(1<<0)|(1<<2)`(Default + Ignore Raycast). `collapseIntervalSec`·`collapseDistanceM`은 `Vector2`. `collapseClips`는 빈 배열.
  - voice 우선순위 상수(총성 0 > 붕괴 1 > 적 발소리 2 > 내 발소리 3)는 SO에 넣지 않았다 — 2b `FootstepMix.Priority` 등 Audio/Logic에서 정한다. 분류·재질로 세트를 찾는 조회 함수도 없음(2b가 Logic에 둔다).
- `GroundSurface`: `[DisallowMultipleComponent]`, 직렬화 필드 `kind`(기본 Debris), **`Kind`는 get/set**(EditorTools·PlayMode 테스트가 코드로 설정하도록).
- `M6Names`: 폴더 `DataFolder, ArtDataFolder, RaidDataFolder, AudioDataFolder, ArtFolder, GeneratedArtFolder, MaterialFolder, PaletteMaterialFolder, PostProcessFolder, TerrainArtFolder, AudioFolder, PlaceholderAudioFolder, FootstepAudioFolder, GunshotAudioFolder, AmbienceAudioFolder, MusicAudioFolder`,
  데이터 `PalettePath`(Data/Art/Palette.asset), `AtmosphereConfigPath`, `AshStormConfigPath`(Data/Raid/), `AudioConfigPath`(Data/Audio/AudioConfig.asset),
  파생 에셋 `SkyTexturePath, AshDotTexturePath, GroundNoiseTexturePath, SkyMaterialPath, AshParticleMaterialPath, TerrainLayerPath, RaidVolumeProfilePath`(§2.2 경로),
  오브젝트 이름 `SunObject "Sun", GlobalVolumeObject "M6_GlobalVolume", AshParticlesObject "AshParticles", PuddlesObject "M6_Puddles", AshStormObject "AshStorm", AudioDirectorObject "AudioDirector", HideoutMusicObject "HideoutMusic"`.
  WAV 개별 파일 이름·팔레트 머티리얼 이름(`M_*`)은 넣지 않았다(2a `PaletteMaterialMap`·2b `M6AudioSetup` 소유). 기존 M2~M4 머티리얼 경로는 `M2SceneSetup.ZoneMaterialPath`·`M3SceneSetup.ScvMaterialPath/RdrMaterialPath`·`M4MapSetup.ZoneMaterialPath`를 그대로 쓸 것.

1b·2단계가 알 것
- 기존 `PlayerConfig.asset`·`AiConfig.asset`에는 새 필드가 아직 직렬화돼 있지 않다(로드하면 필드 초기값 = 설계값). 1b 메뉴가 값을 기록하면 파일에 생긴다.
- `PaletteSO` 필드 초기값이 이미 GDD HEX라 `CreateInstance`만으로 GDD 팔레트다. 1b는 `GddHex` + `ColorUtility.TryParseHtmlString`으로 기록·검증하면 된다.

---

## 1b 구현 메모 (data-designer, 2026-10-22)
결과: 원본 EditMode 1062/1062 통과(1a 1054 + 새 M6DataAssetTests 8), PlayMode 77/77 통과(기존 동작 변화 없음 — 상태 색 표시를 참조하는 PlayMode 테스트 없음, `EnemyDebugView`는 Renderer만 끈다). 커밋 안 함. CHANGELOG_GDD는 건드리지 않음(GDD 값과 다른 수치 없음).
메뉴 `Ashfall/Setup/Create M6 Data`를 원본에서 두 번 실행 — 두 번째 실행 뒤 `Assets`·`ProjectSettings`·`Packages` 전체 파일 해시가 같았다(바이트·GUID 동일). M5 이전 메뉴 연쇄도 기존 에셋을 바꾸지 않았다.

바뀐/새 파일
- 새 `EditorTools/M6DataSetup.cs`: 메뉴 `Ashfall/Setup/Create M6 Data`, 정적 `CreateData()`(먼저 `M5DataSetup.CreateData()`), M6 몫만 하는 공개 `WriteM6Data()`, 공개 `static Color HexColor(PaletteToken)`(GddHex → `ColorUtility.TryParseHtmlString`, sRGB). 경로 상수는 `M6Names`를 그대로 쓴다.
- 새 데이터 에셋: `Data/Art/Palette.asset`, `Data/Raid/AtmosphereConfig.asset`, `Data/Raid/AshStormConfig.asset`, `Data/Audio/AudioConfig.asset`(+ 폴더 `Data/Art`·`Data/Audio`, .meta는 Unity 생성).
- 기존 에셋: `Data/Player/PlayerConfig.asset`(strideWalkM 1.5·strideSprintM 2.2·strideCrouchM 0.8·strideProneM 0.6 기록), `Data/AI/AiConfig.asset`(footstep 4필드 기록, `showStateIndicator: 0`).
- 테스트: 새 `EditMode/M6DataAssetTests.cs`(경로 = M6Names, 팔레트 HEX = §10.1 직접 표기, Atmosphere §3.2·Storm §3.3·Audio §3.5 수치, 보폭 값 + 파일 기록 여부, `showStateIndicator=false`, 메뉴 재실행 시 바이트·GUID·SO 개수 동일), `EditMode/M3DataAssetTests.cs` 한 줄(`Assert.IsFalse(c.showStateIndicator)`).

설계와 다른 점·해석
- AudioConfig: §4.7 1b 목록에는 없고 2b `M6AudioSetup`이 "없으면 생성"하게 돼 있으나, 오케스트레이터 지시대로 1b가 §3.5 수치로 미리 만든다(클립 필드 전부 비움). 2b는 이미 있는 에셋에 빈 클립 필드만 채우면 된다(Q21과 같음).
- Palette·Atmosphere·AshStorm·Audio는 **없을 때만 생성**(§4.7 그대로 — 조정한 값 보존, Q30에서 Atmosphere가 사람이 고치는 원본). 이미 있는데 `palette`가 비었으면 연결만 한다.
- 보폭 8필드는 **파일에 없을 때만** 설계값을 기록(M3 `WriteIfMissing` 관례 — 조정 보존). `showStateIndicator=false`만 매번 기록(값이 true일 때만 SetDirty → 멱등).
- `filmGrainType` = `FilmGrainChoice.Medium1`(1a 메모의 해석).
- GDD 표와 다른 수치 없음. §10.1·§10.2·§8.3 밖의 값은 SO 툴팁·메뉴 주석에 TODO(GDD-?)로 남아 있다.

2단계가 알 것
- 원본에 데이터 에셋 4개가 있다 — 사본은 이 상태에서 복사할 것. 2a는 `AtmosphereConfig.asset`(palette 연결됨)을, 2c는 `AshStormConfig.asset`을 그대로 읽는다.
- 2b `M6AudioSetup`: `AudioConfig.asset`이 이미 있으므로 생성 분기는 타지 않는다. 클립 필드만 채우는 경로를 쓸 것.

---

## 2d 구현 메모 (ai-programmer, 2026-10-22)
결과(사본 `proj_2d`): EditMode 1089/1089 통과(1b 1062 + 2d 새 27: AiSightTests cap 12·EnemyFootstepGaitTests 13·AiAllocationTests 2), PlayMode **전체** 83/83 통과
(기존 77 + 새 AiStormSightTests 4·EnemySoundEventTests 2). `AiPerformanceTests` 예산 유지: AI 틱 평균 0.125ms·99% 0.581ms, 프레임 중앙값 1.09ms, AI Update GC 0
(`RaidPerformanceTests` B 전원 교전 AI 평균 0.217ms·99% 0.713ms). 사본에서 메뉴 실행 없음(씬·에셋 변경 없음). 커밋 안 함.

바뀐/새 파일 (원본)
- `AI/Sensing/SightCone.cs`: `EffectiveRange(sightRangeM, posture, config, float visibilityCapM)` 추가 = min(기존, cap). cap ≤ 0 → 0, +∞ → 기존, **NaN → 상한 없음**(설계에 없던 해석). 3인자 오버로드 그대로.
- `AI/Runtime/AiDirector.cs`: 공개 `IAshStormInfo Storm`(null = 폭풍 없음), `float VisibilityCapM`(Storm 없으면 +∞). `ResolveServices`가 `IAshStormInfo`도 조회(등록 해제되면 null로), `ValidateServices`가 파괴된 Unity 제공자를 놓는다.
  Update에서 프레임당 1번 cap을 읽어 `agent.Sense(..., visibilityCap)`. 조회 주기는 기존 `targetResolveRetrySec`(1초) — 폭풍 등록 뒤 최대 1초 늦게 반영(Rising 20초라 무해). 값 자체는 매 감각 갱신마다 서비스에서 바로 읽는다.
- `AI/Runtime/EnemyAgent.cs`:
  - `Sense(target, los, now, float visibilityCapM = +∞)`.
  - 발소리: `FrameTick`의 `ApplyMovement` 뒤 `TickFootsteps(dt)` — 이동 거리 = **NavMeshAgent 수평 속도 × dt**(transform 변위가 아니라서 Warp가 걸음이 되지 않음). NavMesh 밖·멈춤·사망 → `StrideCadence.Reset`.
    위치 = `transform.position`(적 루트 = 발밑). 공개 순수 `static bool TryPickFootstep(float horizontalSpeedMps, bool crouched, AiConfigSO, out FootstepGait, out float strideM)`:
    속도 < `footstepMinSpeedMps`(또는 ≤ 0·NaN) → 없음, **웅크린 채 이동 → Crouch(걷기 보폭)**(설계에 없던 해석, `TODO(GDD-?) Q13`), 속도 > `footstepRunSpeedMps` → Sprint(달리기 보폭), 그 밖 Walk.
    새 AI 파일을 만들지 않으려고(소유 범위) 순수 함수를 `EnemyAgent`의 정적 메서드로 뒀다. 순찰 1.5m/s → Walk 1초 간격, RDR 이동 3.2·SCV 3.0 → Walk, 질주 5.x → Sprint.
  - 총성: `ApplyWeapon`에서 `Weapon.Tick(...)`이 1 이상이면 `ShotFired(muzzle.position, Weapon.Item.Definition, Enemy)`. `NoiseEmitted`는 내지 않는다(M3 규칙 유지 — PlayMode로 회귀 확인).
- 테스트: `EditMode/AiSightTests.cs`(cap 케이스 + 같은 파일에 새 클래스 `EnemyFootstepGaitTests`), `EditMode/AiAllocationTests.cs`(cap 오버로드·걸음 선택·리듬 할당 0, `FootstepTaken`·`ShotFired` 발행 할당 0),
  새 `PlayMode/AiStormSightTests.cs`(가짜 `IAshStormInfo`는 파일 안 private 클래스: 폭풍 없음 45m 봄 / cap 30 45m 못 봄·게이지 0 → 20m 봄 / 값이 +∞로 바뀌면 같은 자리에서 다시 봄 / 폭풍 중 총성 청각 그대로),
  새 `PlayMode/EnemySoundEventTests.cs`(순찰 걸음 간격 중앙값 1.00초·Walk·위치, 사망 후 없음 / 교전 중 탄창 소모 10발 = `ShotFired(Enemy)` 10개·무기 정의·총구 위치, `NoiseEmitted(Gunshot)` 없음, 등 뒤 25m 다른 분대 RDR 평시 유지).

통합(3단계)·다른 단계가 알 것
- 2b `AudioDirector`: 적 발소리는 순찰 중에도 계속 나온다(적 20명 ≈ 최대 20~40/s). 들리는 거리 밖 버리기가 필수(설계 §1.4 그대로).
- 2c와 접점 없음(코드 공유 없음). 레이드 씬의 `AshStormController`가 `IAshStormInfo`를 OnEnable에 등록하면 AI가 1초 안에 잡는다. 은신처·확인 씬은 서비스가 없어 기존 그대로.
- 4단계 `M6PerformanceTests`: 폭풍 Peak에서는 시야 30m 밖 표적에 레이를 쏘지 않으므로 AI 비용은 같거나 줄어든다. 적 사격·발소리 이벤트는 구독자(Audio)가 있을 때 비용이 그쪽으로 간다.
- `.meta`: 새 테스트 파일 2개(`AiStormSightTests.cs`·`EnemySoundEventTests.cs`)의 `.meta`는 원본에 없다 — 통합 때 Unity가 만든다.

---

## 2c 구현 메모 (gameplay-programmer, 2026-10-22)
결과(사본 proj_2c): EditMode 1112/1112, PlayMode 87/87(전체) 통과. 커밋 안 함. CHANGELOG_GDD는 건드리지 않음(설계 §8 초안 그대로 쓰면 됨).
사본에서 실행한 메뉴: `-executeMethod Ashfall.EditorTools.M6StormSceneApply.ApplyRaidScene` 2회 — 두 번째 실행 뒤 `Raid_CrimsonBasin.unity` 바이트 동일(멱등).
사본에는 2a가 없어 "AshParticles" 없음 경고가 나오고 presenter.ashParticles = null, sun = 기존 "Directional Light"로 연결됐다.

원본에 쓴 파일
- 새: `Raid/Logic/FogMath.cs`, `Raid/Logic/AshStormSchedule.cs`, `Raid/Logic/AshStormModel.cs`, `Raid/Runtime/AshStormController.cs`, `Raid/Runtime/AshStormPresenter.cs`,
  `Player/RadiationDoseModel.cs`, `EditorTools/M6StormSceneApply.cs`.
- 수정: `Player/PlayerHealth.cs`(선량), `Player/PlayerMotor.cs`(FootstepTaken), `Weapons/WeaponController.cs`(ShotFired).
- 테스트 새: `EditMode/AshStormTests.cs`(17), `FogMathTests.cs`(12), `RadiationDoseTests.cs`(12), `PlayerSoundEventTests.cs`(9), `PlayMode/AshStormFlowTests.cs`(5), `PlayMode/PlayerSoundEventTests.cs`(5).
  EditMode·PlayMode에 같은 클래스 이름 `PlayerSoundEventTests`가 있다(네임스페이스·어셈블리 다름 — 설계 표 이름 그대로).

공개 API (설계 §4.2·§4.4 + 추가)
- `FogMath.DensityForVisibility(v, threshold)`(∞·NaN → 0, ≤ 0 → `MaxDensity` 10), `VisibilityForDensity(d, threshold)`(≤ 0 → ∞), **추가** `Transmittance(d, density)`.
- `AshStormSchedule.PickStartSec(Vector2 window, System.Random)`: 뒤집힌 창 정렬, 음수는 0으로.
- `AshStormModel`: 설계 시그니처 그대로 + **추가** `EndSec`. 생성 시 config 값을 복사한다. 맑을 때 가시거리가 ∞(Atmosphere 없음)·폭풍 시야 이하이면 시야 = stormVisibilityM / 강도로 잇는다.
- `AshStormController`: `IAshStormInfo`, `ForceStartNow()`(세션 전에 부르면 세션이 생길 때 즉시 시작), 읽기 `Model`·`Presenter`·`Config`·`Raid`·`Seed`·`StartSec`(미정 NaN). ContextMenu `Debug/재 폭풍 지금 시작`.
  **설계 보강**: 한 프레임에 단계를 건너뛰면(AdvanceTime) 사이 단계도 순서대로 `AshStormChanged`를 한 번씩 낸다 — 레이드당 항상 Rising·Peak·Falling·Over 4회(Audio가 Rising을 놓치지 않게).
- `AshStormPresenter`: 직렬화 `ashParticles`·**`sun`(추가, 비면 RenderSettings.sun)**·`atmosphere`·`config`. `Apply(intensity, visibilityM)`(값이 같으면 아무것도 안 씀), **추가** `SetAshParticles(ParticleSystem)`(테스트·늦게 생긴 파티클), 읽기 `AshParticles`·`Sun`·`SkyCopy`·`SkyOriginal`·`Original*`.
  - 안개: 폭풍 중 Exp²로 켜고 밀도 = max(씬 원래 Exp² 밀도(꺼져 있었으면 0), FogMath(VisibilityM)), 색 = lerp(원래, `stormFogToken`). Atmosphere 없으면 안개는 안 바꿈.
  - 하늘: 강도 > 0에서만 `new Material(원본)` 복제본을 RenderSettings.skybox에 넣고 `_Tint`(Panoramic) 또는 `_SkyTint`(Procedural)·`_Exposure`를 바꾼다. 강도 0이 되면 원본으로 돌리고 복제본 파괴. OnDestroy도 복제본 파괴(공유 에셋 불변).
    OnDestroy의 안개 복원은 이 씬이 활성일 때만(언로드 중 다음 씬 RenderSettings를 덮지 않게).
  - 재 파티클 "속도 배율"은 `main.simulationSpeed`로 적용(2a 모듈 구성에 기대지 않음 — 수명도 같이 빨라진다). 방출 `rateOverTimeMultiplier`, 크기 `startSizeMultiplier`, 상한 lerp(원래, `ashMaxParticlesStorm`).
- `RadiationDoseModel`: `Dose`, `Tick(dt, rate)`(0 이하·NaN·무한 무시), `Reset()`.
- `PlayerHealth`: **추가** `Radiation`(모델)·`RadiationDose`. `IAshStormInfo`는 매 프레임 `Services.TryGet`(사전 조회, 할당 없음 — 재시도 주기 상수 불필요). 사망·RaidEnded 뒤 멈춤.
- `PlayerMotor`: **추가** 정적 순수 `SelectGait(TargetPosture, bool sprinting)`, `StrideLength(PlayerConfigSO, FootstepGait)`, 읽기 `CurrentGait`. 새 파일을 소유하지 않아 순수 함수를 PlayerMotor의 정적 메서드로 뒀다.
  Move 전후 실제 수평 이동(접지 중, 이동 입력 있을 때)으로 `StrideCadence.Tick`. 공중에서는 세지 않고 누적 유지(isGrounded 깜빡임 대비), 입력 없음·막힘(거리 0) → 리셋. PlaceAt·OnDisable에서 리셋.
- `WeaponController`: `NoiseEmitted(Gunshot)` 바로 뒤 `ShotFired(origin, weapon, Player)`.
- `M6StormSceneApply`: `Apply(Scene)`(설계대로, sun = "Sun" → 없으면 첫 Directional Light), 메뉴 `Ashfall/Setup/M6/Apply Storm (Open Scene)`(활성 씬, 저장 안 함), **추가** 배치용 `ApplyRaidScene()`(레이드 씬 열기 → 적용 → 저장). seed는 덮지 않는다.

통합(3단계)·다른 단계가 알 것
- 순서: `M6VisualSceneApply.ApplyRaid`(AshParticles·Sun 생성) **다음에** `M6StormSceneApply.Apply` — 그래야 파티클·Sun 참조가 잡힌다(설계 §1.6 순서 그대로).
- 2b Audio: `AshStormChanged`는 건너뛴 단계도 순서대로 온다. 폭풍 시작 시각은 세션이 생긴 프레임에 정해지므로 Start 직후 `IAshStormInfo.Phase`는 Pending.
- `AshStormFlowTests`는 presenter에 파티클이 없으면 코드로 만든 파티클을 넣는다 — 통합 씬에서는 실제 AshParticles로 같은 단언(방출 ×4·상한 ≥ 원래)을 한다. 레이드 씬 안개가 원래 Exp² 450m여도 단언은 맞게 썼다.
- 4단계 `M6RaidFlowTests`: 안개·하늘 원상 확인은 "공유 에셋 값 불변 + 복제본 파괴 + 다음 씬 skybox가 복제본 아님"으로 보면 된다(테스트 `UnloadDuringPeak_...`와 같은 방법).
- `.meta`: 새 .cs 13개의 `.meta`는 원본에 없다 — 통합 때 Unity가 만든다.

---

## 2b 구현 메모 (gameplay-programmer, 2026-10-22)
결과(사본 proj_2b): EditMode 1163/1163 통과(1b 1062 + 새 101), PlayMode 91/91 통과(기존 77 + 새 14). 커밋 안 함. CHANGELOG_GDD는 건드리지 않음.
사본에서 `-executeMethod Ashfall.EditorTools.M6AudioSceneApply.ApplyAudioToScenes`(= `M6AudioSetup.CreateAssets` + 레이드·은신처 씬 열어 적용·저장)를 **2회** 실행 —
두 번째 실행 로그 "WAV 새로 씀 0, 임포트 설정 변경 0, 빈 필드 채움 0", 실행 전후 `Assets`·`ProjectSettings` 전체 md5 동일(WAV·.meta·AudioConfig·두 씬 바이트 같음).
EditMode 전체를 돈 뒤에도(사용자 교체 보존 테스트가 임시로 바꿨다 되돌림) 에셋 해시 동일.

원본에 쓴 파일(새, .meta 없음)
- `Audio/Logic/`: `GunshotMix.cs`(`enum GunshotLayer` 포함), `FootstepMix.cs`, `SurfaceResolver.cs`(`struct SurfaceHit`), `ClipPicker.cs`, `VoiceAllocator.cs`, `GeigerClickModel.cs`, `IntervalScheduler.cs`, **추가** `AmbientMix.cs`.
- `Audio/Runtime/`: `SurfaceProbe.cs`, `AudioVoicePool.cs`, `AmbientSoundscape.cs`, `AudioDirector.cs`(`PlayedSoundKind`·`PlayedSound` 포함), `HideoutMusic.cs`.
- `EditorTools/`: `WavWriter.cs`, `SfxSynth.cs`, `M6AudioSetup.cs`, `M6AudioSceneApply.cs`.
- 테스트 EditMode: `WavWriterTests`, `SfxSynthTests`, `GunshotMixTests`, `FootstepMixTests`, `SurfaceResolverTests`, `VoiceAllocatorTests`, `GeigerClickModelTests`, `IntervalSchedulerTests`(+ AmbientMix·ClipPicker 케이스), `AudioConfigAssetTests`. PlayMode: `AudioFlowTests.cs`(클래스 `AudioFlowTests` + `AudioSceneMusicTests`).

공개 API (설계 §4.3 그대로 + 추가)
- `GunshotMix.Evaluate(d, cfg, out layer, out gain)`, **추가** `FindSet(cfg, WeaponDefinitionSO)`/`FindSet(cfg, WeaponClass)`(null·세트 없는 분류 → `fallbackGunshotClass`), `Clip(set, layer)`, `ClipOrFallback(cfg, set, layer)`(세트의 그 레이어가 비면 대체 세트).
- `FootstepMix.Evaluate(d, gait, source, cfg, out gain)`, `Priority(source)`, 상수 `PriorityGunshot 0 / PriorityCollapse 1 / PriorityEnemyFootstep 2 / PriorityPlayerFootstep 3`, **추가** `FindSurface(cfg, kind)`. 재질 음량은 지면 판정 뒤 곱한다.
- `SurfaceResolver.Pick(hits, count, terrain, untagged)` — `SurfaceHit`에 **`IsTrigger` 필드 추가**(설계의 "태그 없는 트리거 건너뜀"을 하려면 필요).
- `ClipPicker(Random)`: `Next(count)`(0 → −1), `Pitch(jitter)`(jitter ≤ 0.9로 자름). `VoiceAllocator(count)`: `Acquire/Release/IsBusy`, 추가 `ReleaseAll`, `BusyCount(now)`, `PriorityOf`, `LastAcquireStole`.
- `GeigerClickModel(clicksPerRad, maxClicksPerSec, Random)`: `Tick(dt, rad)`, `Rate(rad)`, `Reset()`. `IntervalScheduler(range, Random)`: `Tick(dt)`, `Restart()`, `Remaining`, `LastInterval`.
- `AmbientMix`(추가, 순수): `WindVolume(cfg, intensity)`, `StormVolume(cfg, intensity)`, `StormLoopWanted(phase)`, `RandomAround(center, range, Random)`.
- `AudioDirector`: 직렬화 `config`, `seed`(0 = 매번 무작위). 읽기 `LastPlayed`(`Kind, Clip, Position, Gain, Pitch, Priority, Voice, Source, Surface, Layer`), `PlayedCount`, `CulledCount`, `Voices`(`PlayCount/StealCount/DroppedCount/LastVoice/Source(i)/Allocator`), `Ambient`(`Wind/Storm/GeigerSource`, `WindRequested`, `StormLoopActive`, `GeigerClicks`), `Probe`(`LastHitCount`), `Config`, `IsInitialized`.
  `Configure(config, seed)`(비활성 상태에서 Awake 전에 — 테스트용), `ResolveServices()`(즉시 재조회), `DebugGunshot(layer)`, `DebugCollapseNow()`, ContextMenu `Debug/총성 근`·`중`·`원`, `Debug/금속 붕괴 지금`.
- `HideoutMusic`: 직렬화 `config`, `Source`, `PlayRequested`, `Configure(config)`.
- `M6AudioSetup`: `CreateAssets()`(메뉴 `Ashfall/Setup/M6/Create Audio Assets`), `WritePlaceholderWavs()`, `ApplyImportSettings()`, `FillAudioConfig()`, 경로 `FootstepPath(kind, v)`·`GunshotPath(cls, layer)`·`CollapsePath(v)`·`WindLoopPath`·`StormLoopPath`·`GeigerClickPath`·`HideoutMusicPath`, `Recipes()`, `LoopPaths`.
- `M6AudioSceneApply`: `ApplyRaid(Scene)`·`ApplyHideout(Scene)`(bool — 설정 없으면 false, 저장 안 함), 메뉴 `Ashfall/Setup/M6/Apply Audio (Open Scene)`(활성 씬 적용 + 저장),
  **추가** 메뉴 `Ashfall/Setup/M6/Apply Audio (Raid + Hideout)` = `ApplyAudioToScenes()`(에셋 생성 + 두 씬 열어 적용·저장 — 2b 단독 확인용).

설계와 달라진 점·해석
- WAV는 **27개**(발소리 12 + 총성 9 + 바람·폭풍 2 + 금속 붕괴 2 + 가이거 1 + 은신처 1). 설계 §2.2 표의 합계 "33"은 표 내용과 맞지 않아 표 내용을 따랐다. 크기 약 3.2MB.
- 임포트: 모노 강제, Vorbis 품질 0.7, preload, loadInBackground false. 1초 미만 원샷 = DecompressOnLoad, 루프 3개·1초 이상 = CompressedInMemory. **임포터에는 루프 설정이 없다**(Unity) — 루프는 재생 쪽(`AudioSource.loop`)이 켜고 PlayMode에서 확인한다.
- 지면 판정 우선순위는 설계 문구 그대로 "가장 가까운 태그 > 지형 > 기본"(가장 가까운 콜라이더 우선이 아님). 레이 시작 높이 0.5m는 SO 필드가 없어 `SurfaceProbe.LiftM` 상수(TODO(GDD-?) Q11). 서비스 재조회 0.5초도 `AudioDirector.ServiceRetrySec` 상수.
- 총성에는 피치 변주 없음(설계 §1.4는 발소리만 ±pitchJitter). 총성·붕괴 voice 우선순위·발소리 우선순위는 `FootstepMix` 상수.
- 금속 붕괴 위치 = 청취자에서 수평 150~400m, 같은 높이. 가이거는 2D 소스 `PlayOneShot`(클릭당). 늦게 생긴 디렉터가 `Rising`을 놓쳐도 강도 > 0이면 폭풍 루프를 튼다.
- 청취자: `IPlayerTarget.EyePosition`, 없으면 씬의 AudioListener, 그것도 없으면 디렉터 위치. 일시정지 반영은 `Update`, `OnDisable`·`OnDestroy`에서 `AudioListener.pause = false`.
- 3D voice 롤오프 = 평탄 사용자 곡선, maxDistance = `farMaxM`(곡선이 평탄해서 값은 영향 없음). 시간 기준은 `Time.time`(세계 정지 중 voice 시간도 멈춤 — `AudioListener.pause`와 일치).
- `AudioConfigSO`·`GroundSurface`·Core·`M6Names`·`M6DataSetup`는 수정하지 않았다. `AudioConfig.asset`이 없을 때만 `M6DataSetup.CreateData()`를 먼저 부른다.

통합(3단계) 때 할 일
- `M6SceneSetup.ApplyAll` 순서: `M6DataSetup.CreateData` → (2a) → `M6AudioSetup.CreateAssets()` → 레이드 씬 열고 … → `M6AudioSceneApply.ApplyRaid(scene)` → 저장, 은신처 … → `ApplyHideout(scene)` → 저장. 반환값 false(설정 없음)면 건너뛴다. 메인 메뉴에는 오디오를 두지 않는다.
- 원본에서 메뉴를 돌리면 `Assets/_Project/Audio/Placeholder/**`(WAV 27 + .meta + 폴더 .meta)와 `AudioConfig.asset` 클립 참조가 새로 생긴다 — 함께 커밋. 2b의 새 .cs 27개(코드 17 + 테스트 10)와 새 폴더 `Audio/Logic`의 .meta는 Unity가 만든다.
- 3단계 스모크: 레이드 씬 "AudioDirector" 1개·`Config` 연결, 은신처 "HideoutMusic" 1개(`PlayRequested`, `Source.ignoreListenerPause`). `AudioSceneMusicTests`는 메인 메뉴·레이드에 `HideoutMusic`이 없음을 이미 본다(레이드에 AudioDirector가 없어도 통과).
- `AudioConfigAssetTests`는 에셋이 없으면 실패한다(메뉴 실행 전 원본에서는 실패 — 3단계가 메뉴를 돌린 뒤 통과). 이 테스트는 `CreateAssets`를 다시 부르고 FS_Dirt_01·AudioConfig를 잠시 바꿨다 되돌린다.
- 사본의 Library 경로가 길어 VisualScripting YamlDotNet `DirectoryNotFoundException`이 로그에 한 번 찍혔다(경로 260자 초과, 원본에서는 없음 — 무해).

---

## 2a 구현 메모 (gameplay-programmer, 2026-10-22)
결과(사본 proj_2a): EditMode 1089/1089(1b 1062 + 새 27: SkyPanoramaTests 11, PaletteConsistencyTests 6, AtmosphereAssetTests 10), PlayMode 81/81(전체, 새 RaidVisualSmokeTests 4) 통과. 커밋 안 함. CHANGELOG_GDD는 건드리지 않음.
사본에서 실행한 메뉴: `-executeMethod Ashfall.EditorTools.M6VisualSceneApply.ApplyToGameScenes`(1b 데이터 → 2a 에셋 → Raid·Hideout·MainMenu 열어 적용·저장) 5회.
M6 아트 에셋을 전부 지운 상태에서 1회 → 2회째 뒤 `Assets`·`ProjectSettings` 전체 파일 해시 동일(씬 3개·머티리얼·PNG·프로필·TerrainData 바이트·GUID 불변). 2a 파일만 있는 사본이라 오디오·폭풍 오브젝트는 없다.

원본에 쓴 파일
- 새: `EditorTools/PaletteMaterialMap.cs`, `EditorTools/SkyPanorama.cs`, `EditorTools/M6ArtSetup.cs`, `EditorTools/M6VisualSceneApply.cs`.
- 테스트 새: `EditMode/SkyPanoramaTests.cs`, `EditMode/PaletteConsistencyTests.cs`, `EditMode/AtmosphereAssetTests.cs`, `PlayMode/RaidVisualSmokeTests.cs`(`#if UNITY_EDITOR`, M6Names 값은 문자열로 — PlayMode asmdef는 EditorTools 미참조).

공개 API
- `PaletteMaterialMap`: `enum Key { Concrete, RustMetal, Debris, Crate, Accent, Puddle }`, `Spec`(Name·Path·Token·Alpha·Transparent·Unlit·Metallic·Smoothness·Emission), `Rule`(Material·Surface),
  `Materials`(Key → Spec, `Art/Materials/Palette/M_*.mat`), `LegacyMaterials`(M4 탈출 구역·M3 SCV/RDR·M2 터렛 — 경로는 M2/M3/M4 상수), `ShapeNames`, `TryGetStructureRule(name, out Rule)`, `ShapeOf(name)`, `CrateRule`·`PowerSwitchRule`·`PuddleRule`, 셰이더 이름 상수.
- `SkyPanorama`: `Sample(dir, cfg)`, `SunDirection(cfg)`, `DirectionFromAngles(az, el)`, `DirectionToUv(dir)`, **추가** `UvToDirection(uv)`·`SamplePixel`·`Render(w, h, cfg)`. UV 규약 = 내장 Skybox/Panoramic(u = 0.5 − atan2(z, x)/2π, v = 1 − acos(y)/π, `_Rotation` 0).
- `M6ArtSetup`: `CreateAssets()`(bool — AtmosphereConfig 없으면 false), 메뉴 `Ashfall/Setup/M6/Create Art Assets`(배치 `CreateAssetsMenu`, 실패 시 예외), 순수 `RenderAshDot`·`RenderGroundNoise`, 변환 `ToUrp(FilmGrainChoice)`·`ToUrp(TonemapChoice)`, 셰이더 이름 `SkyShaderName`·`AshShaderName`.
- `M6VisualSceneApply`: `ApplyRaid(Scene)`·`ApplyMenuScene(Scene)`(bool, 저장 안 함), 메뉴 `Ashfall/Setup/M6/Apply Visual (Open Scene)`(활성 씬 이름으로 분기, 저장은 사람), **추가** 메뉴·배치 `Apply Visual (Game Scenes)` = `ApplyToGameScenes()`(3단계 `M6SceneSetup.ApplyAll` 전 확인용 — 통합 뒤 지우거나 남겨도 무해), `FogDensity(visibility, threshold)`.

설계와 다른 점·해석 (전부 TODO(GDD-?) 자리표시)
- 하늘 띠(Q3): 고도 0 = Rust_Red(§10.2 "→ Rust_Red"), 지평선 **아래** `horizonBandDeg`(10°) 동안 Rust_Red → Dried_Blood, 그 아래 Dried_Blood. 위쪽 곡선 지수 0.6·번짐 세기 0.8은 SkyPanorama 상수(TODO). 흐림 1이면 태양 원반·번짐 모두 0.
- 안개 밀도는 EditorTools 안에 같은 식(`FogDensity` = √(−ln t)/d)을 뒀다 — 사본에 2c `FogMath`가 없어서. 통합 때 `FogMath.DensityForVisibility`로 바꿔도 결과 같음(잘못된 입력만 다름: 2c는 ≤0 → 10, 2a는 0).
- Sun: 최상위 "Sun" → 없으면 최상위 첫 Directional Light를 "Sun"으로 이름 변경(새로 만들지 않음). forward = −SunDirection. 그림자가 None이면 Soft로.
- 환경광: Trilight 색 × `ambientIntensity`, `RenderSettings.ambientIntensity` = 1. 반사 프로브·라이트맵은 손대지 않음.
- 지형: `TL_AshSoil`이 레이어에 없을 때만 추가(사용자 레이어 보존). 처음(레이어 0개)에는 알파맵 해상도 16 + 100% — TerrainData 파일이 커지지 않게. `diffuseRemapMax` = Dried_Blood의 **선형** 값(URP TerrainLit이 선형 공간에서 곱함), 노이즈 PNG는 sRGB 끔.
- 블록아웃: `Environment/<그룹>/<구조물>`에서 `LootContainer`가 있으면 상자, 아니면 `{Shape}_{nn}` 표. 구조물 아래 모든 MeshRenderer에 머티리얼, 트리거 아닌 콜라이더 오브젝트마다 `GroundSurface`. 표에 없는 구조물은 경고만(현재 0개). 경계 벽(Bounds)·Terrain·적·탈출 표시는 대상 아님(적·탈출 표시는 기존 머티리얼 재도색으로 바뀜).
- 물웅덩이: `M6_Puddles`(최상위)/`Puddle_01~03`, 저수지 중심 기준 오프셋·크기 상수(9×6, 6×4.5, 5×3.5m). 큐브 1개 = 보이는 수면 + 트리거(위 = 발자국 범위 최고 지면 + 3cm, 아래 = 최저 지면 − 10cm), 레이어 2, 정적 플래그 없음.
- 재 파티클: 상자 중심 = 플레이어 + 높이 × 0.3(−4~+16m), prewarm, 속도 = velocityOverLifetime(World, y = −낙하 속도), Noise(주파수 0.4 상수), `maxParticleSize` 0.01(화면 비율 — 카메라 앞 재가 화면을 덮지 않게, V4), 색·알파는 머티리얼(`_BaseColor` = ashToken × ashAlpha, startColor 흰색).
- 머티리얼: URP `ShaderGUI`가 다음 저장 때 다시 쓰는 값(`_Color` = `_BaseColor`의 선형 왕복값, 파티클 `_CameraFadeParams`(0, ∞)·`_SoftParticleFadeParams` 0)을 처음부터 같게 기록 — 이게 없으면 새로 만든 머티리얼이 두 번째 실행에서 바뀐다(확인함).
  Transparent = BaseShaderGUI Alpha 블렌드 조합(SrcAlpha/OneMinusSrcAlpha, ZWrite 0, `_SURFACE_TYPE_TRANSPARENT`, RenderType Transparent, 큐 3000, DepthOnly·ShadowCaster 패스 끔). M2_TurretZone은 Lit 투명이 되며 `_BlendModePreserveSpecular` 0.
- 텍스처: 하늘 PNG는 무압축(그라데이션 밴딩 방지, 2MB — TODO 실제 에셋 때 재검토), Repeat U/Clamp V, 밉맵 없음. PNG는 바이트가 다를 때만 쓰고 임포트 설정도 다를 때만 다시 임포트.
- 볼륨 프로필: 구성 요소는 프로필의 하위 에셋(HideInInspector|HideInHierarchy), 있으면 값만 다시 씀. 파일 머리 주석은 못 넣어 메뉴 로그에 "파생물 — AtmosphereConfig에서 조정"을 남긴다.
- 은신처·메인 메뉴: 씬의 모든 카메라 SolidColor + Ash_Black. 볼륨 없음.
- `PaletteConsistencyTests`(머티리얼)·`AtmosphereAssetTests`는 M6 아트 에셋이 하나도 없으면 `Assert.Ignore`(통합 전 원본 실행 대비). 하나라도 있으면 전부 단언한다. `AtmosphereAssetTests`의 재실행 테스트는 `CreateAssets()`를 다시 부른다.

통합(3단계)이 알 것
- 순서: `M6DataSetup.CreateData` → `M6ArtSetup.CreateAssets` → (씬) `M6VisualSceneApply.ApplyRaid` → `M6StormSceneApply.Apply`(AshParticles·Sun을 이름으로 찾음) → 오디오. 은신처·메인 메뉴는 `ApplyMenuScene`.
- `ApplyRaid`는 활성 씬이 아니면 `SetActiveScene`한다(RenderSettings가 활성 씬에 기록). 저장은 호출자.
- `RaidVisualSmokeTests`는 하늘을 `shader == Skybox/Panoramic` + 이름 접두어 "M6_Sky"로 본다(2c 복제본도 통과). 재 파티클 상한은 평시 값(폭풍 강도 0에서 2c가 원래대로 둔다는 전제).
- M4 `CreateRaidScene`은 NewScene의 "Directional Light"를 남기므로 M4 재실행 뒤 Apply가 다시 "Sun"으로 바꾼다. TerrainData의 레이어·알파맵은 M4 `CreateTerrain`(SetHeights)에서 유지된다(TerrainData를 지우지 않는 한).
- 원본에서 처음 실행하면 기존 머티리얼 4개(M2/M3/M4)·`Terrain-CrimsonBasin.asset`·씬 3개가 바뀐다(의도). 새 .cs 8개의 `.meta`는 원본에 없다 — 통합 때 Unity가 만든다.
- 위험 R1·R4(빌드 셰이더): 레이드 씬은 안개 Exp²를 항상 켠다. Panoramic·Particles/Unlit은 머티리얼 참조로 빌드에 들어가야 한다 — 4단계 빌드에서 확인.
- 사람 확인(§7.1 V1~V8): 특히 V2 태양 방향과 그림자, V3 지평선 띠, V4 재 밀도·크기, V6 지형 타일(12m).

---

## 3단계 구현 메모 (gameplay-programmer, 2026-10-22)
결과(원본): 첫 통합 컴파일 오류 0. `M6SceneSetup.ApplyAll` 2회 — 두 번째 실행 뒤 `Assets`·`ProjectSettings`·`Packages` 전체 파일 md5 동일(새 테스트 파일 `.meta` 1개만 추가 —
실행 중 새 .cs가 생겨서). 두 번째 로그: "WAV 새로 씀 0, 임포트 설정 변경 0, AudioConfig 빈 필드 채움 0". 기존 `.meta` GUID 변경 없음(`git status`에 수정된 `.meta` 없음).
EditMode **1267/1267**, PlayMode **113/113**(약 9분) 통과. 두 테스트 실행 뒤에도 에셋 md5 동일. 커밋 안 함. CHANGELOG_GDD는 건드리지 않음.
선택 확인: `M4SceneSetup.CreateAll` 재실행 → 레이드 씬에 Sun·M6_GlobalVolume·AshParticles·AshStorm·AudioDirector·M6_Puddles 각 1개, 메인 메뉴 배경 Ash_Black 유지,
Terrain·NavMesh 바이트 동일(씬 2개만 로컬 fileID가 바뀜). 확인 뒤 테스트를 돌린 씬 파일로 되돌려 두었다(차이 최소화).

바뀐/새 파일
- 새 `EditorTools/M6SceneSetup.cs`: 메뉴 `Ashfall/Setup/Apply M6 Art & Audio` = `ApplyAll()`(데이터 → 아트 → 오디오 → 레이드·은신처·메인 메뉴 열어 적용·저장),
  씬별 `ApplyRaid(Scene)`(비주얼 → 폭풍 → 오디오)·`ApplyHideout(Scene)`(배경 → 음악)·`ApplyMainMenu(Scene)`(배경), M4·M5용 `ApplyRaidIfAssetsExist`·`ApplyHideoutIfAssetsExist`·`ApplyMainMenuIfAssetsExist`
  (팔레트·Atmosphere·AshStorm·Audio 설정과 파생 에셋(프로필·하늘·재 머티리얼·지형 레이어)이 있을 때만, 없으면 조용히 건너뜀), `RaidAssetsExist()`. 적용 실패는 예외(배치 종료 코드로 드러나게).
- `EditorTools/M4SceneSetup.cs`: `CreateRaidScene` 저장 직전 `M6SceneSetup.ApplyRaidIfAssetsExist(scene)`, `CreateMainMenuScene` 저장 직전 `ApplyMainMenuIfAssetsExist(scene)` (각 한 줄).
- `EditorTools/M5SceneSetup.cs`: `CreateHideoutScene` 저장 직전 `ApplyHideoutIfAssetsExist(scene)` (한 줄).
- `EditorTools/M6VisualSceneApply.cs`: 2a의 `FogDensity` 삭제 → `FogMath.DensityForVisibility` 사용(중복 제거, 유효 입력 결과 같음 — `RaidVisualSmokeTests` 통과).
- `Audio/Runtime/AudioDirector.cs`(통합 충돌 수정): 등록 해제 없이 파괴된 Unity 제공자(`IPlayerTarget`·`IAshStormInfo`)를 놓는 `DropDestroyedServices()`를 Update·`ResolveListenerPosition`·`DebugGunshot`에서 부른다.
  원인: 첫 PlayMode에서 `RaidPerformanceTests`가 TearDown에서 실패 — 테스트의 `TestTarget`이 파괴된 뒤 같은 프레임에 2d 적 발소리(`FootstepTaken`)가 오고, 2b `AudioDirector`가
  캐시한 청취자(`target.EyePosition`)에서 `MissingReferenceException`. 2b·2d 각자 사본에서는 서로가 없어 드러나지 않았다. `AiDirector.ValidateServices`와 같은 방식(할당 없음).
- 테스트: 새 `PlayMode/M6RaidSceneSmokeTests.cs`(AudioDirector·AshStormController·AshStormPresenter·Volume·AshParticles·Directional Light 각 1개, 최상위 이름 = M6Names,
  AshParticles는 Player 자식, 레이드 음악 없음, config·raid·presenter·ashParticles·sun·RenderSettings.sun·볼륨 프로필 연결, `IAshStormInfo` = 씬의 AshStorm·Pending·시작 시각 정해짐,
  AiDirector.Storm이 2초 안에 잡힘, 2초 실행 오류 없음).
  `PlayMode/HideoutSceneSmokeTests.cs`: 은신처 카메라 배경 Ash_Black(SolidColor), HideoutMusic 1개(최상위·config = AudioConfig·클립·루프·ignoreListenerPause·PlayRequested), AudioDirector 없음 단언 추가,
  새 테스트 `MainMenu_HasNoMusic_BackgroundAshBlack`(HideoutMusic·AudioDirector 0, 음악 클립 트는 소스 없음, 배경 Ash_Black).

설계와 다른 점
- 설계 §6 3단계의 "MainMenu 음악 없음은 `RaidSceneSmokeTests`에" → 오케스트레이터 지시대로 `HideoutSceneSmokeTests`에 새 테스트로 넣었다(배경색 단언과 같이). `RaidSceneSmokeTests`는 그대로.
- §1.6의 `ApplyRaidIfAssetsExist` 외에 은신처·메인 메뉴용 `Apply*IfAssetsExist` 두 개를 같은 방식으로 추가(이름만 추가, 동작은 설계 그대로).
- 2a 확인용 메뉴(`Apply Visual (Game Scenes)`)·2b(`Apply Audio (Raid + Hideout)`)·2c(`ApplyRaidScene`) 배치 진입점은 남겨 두었다(무해, 정리는 리뷰 판단).

새로 생긴 에셋(커밋 대상, 전부 Unity 생성 `.meta` 포함)
- 아트: `Art/Generated/{Sky_CrimsonDusk,AshDot,GroundNoise}.png`, `Art/Materials/Palette/M_{Concrete,RustMetal,Debris,Crate,Accent,Puddle}.mat`, `Art/Materials/M6_Sky.mat`·`M6_AshParticle.mat`,
  `Art/PostProcess/VP_RaidAtmosphere.asset`, `Art/Terrain/TL_AshSoil.terrainlayer` (+ 폴더 `Generated`·`Materials/Palette`·`PostProcess`·`Terrain` `.meta`).
- 오디오: `Audio/Placeholder/**` WAV 27개(Footsteps 12·Gunshots 9·Ambience 5·Music 1) + 폴더 `.meta`.
- 데이터(1b, 이번에 처음 커밋): `Data/Art/Palette.asset`, `Data/Raid/AtmosphereConfig.asset`·`AshStormConfig.asset`, `Data/Audio/AudioConfig.asset`(클립 18필드 채움).
- 바뀐 기존 에셋(의도): `Art/Materials/M2_TurretZone.mat`·`M3_EnemyRDR.mat`·`M3_EnemySCV.mat`·`M4_ExtractionZone.mat`, `Scenes/Raid_CrimsonBasin.unity`·`Raid_CrimsonBasin/Terrain-CrimsonBasin.asset`,
  `Scenes/Hideout.unity`(HideoutMusic + 배경), `Scenes/MainMenu.unity`(배경만).
- 새 .cs의 `.meta`(1a~2d·3단계 전부)도 이번 실행에서 생겼다 — 함께 커밋.

4단계가 알 것
- 테스트 결과 파일·로그는 scratchpad(`m6_3_*.log`·`m6_3_*.xml`). `RaidPerformanceTests` 수치(AudioDirector·AshStorm 포함 씬): A 자연 교전 AI 평균 0.087ms·99% 0.231ms, B 전원 교전 AI 평균 0.123ms·99% 0.362ms, 프레임 중앙값 1.45ms.
- 첫 PlayMode 실행(수정 전)은 112/113, 종료 코드 2(`RaidPerformanceTests` — 위 AudioDirector 수정으로 해결). 두 번째 실행 113/113, 종료 코드 0.

---

## 4단계 구현 메모 (test-engineer, 2026-10-22)
결과(원본): EditMode **1267/1267** × 2회(종료 코드 0, 약 45초·39초), PlayMode **115/115** × 2회 연속(기존 113 + 새 2, 종료 코드 0, 각 약 598초).
Windows 빌드 성공(종료 코드 0, 13분 14초, 오류 0·경고 0, 110MB, `Builds/Windows64/` — level0~3 = 씬 4개). 커밋 안 함. 소스·씬·에셋 수정 없음 —
실행 전후 `Assets`·`ProjectSettings`·`Packages`(테스트 폴더 제외) 전체 md5 동일, `git status` 차이는 새 테스트 2개의 Unity 생성 `.meta`뿐(+ 리뷰어의 `docs/design/M6_review.md`).
로그·결과: scratchpad `m6_4_edit{1,2}.*`, `m6_4_play{1,2}.*`, `m6_4_new.*`(새 테스트만), `m6_build.log`.

새 테스트 (PlayMode, `Ashfall.Tests.PlayMode`)
- `M6PerformanceTests`: 실제 레이드 씬(SP-W), 적 20 무적화(M4 방식), 무적 `TestTarget` = `IPlayerTarget` = 오디오 청취자, `ForceStartNow` → Rising 1/4에서 전환 쓰기 측정 → `AdvanceTime` → Peak.
  Peak 시야 30m라 적 고리를 35~55m → **12~24m**로 좁혔다(전원 교전 3초). 10초 측정:
  - AI 틱·투사체·프레임: 기존 예산 그대로(평균 ≤ 1.0ms·99% ≤ 3ms, 투사체 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms).
  - `AudioDirector.Update`+`AshStormController.Update`: 엔진 호출 외에 같은 private Update를 대리자로 한 번 더 불러 Stopwatch + `Recorder("GC.Alloc")`(현재 스레드)로 잰다
    (끄면 구독·서비스 등록이 풀려서 `AiPerformanceTests`처럼 끄지 못함 → 측정 중 환경음 틱이 프레임당 2회 = 보수적). 에디터 스크립트 Update 마커는 배치모드에서 찾지 못했다(보고만).
  - 폭풍 전환 쓰기: 프레젠터는 값이 같으면 안 쓰므로 Rising 중 `presenter.Apply(강도×0.999)`→`Apply(강도)`를 120프레임 직접 불러 시간·GC.
  - 오디오 이벤트 경로: 측정 중 실제 발행된 `ShotFired`·`FootstepTaken`(고정 배열에 기록)을 같은 버스로 재발행해 GC 0·이벤트당 시간.
  - 재 파티클 `particleCount` 최대 ≤ `ashMaxParticlesStorm`, Peak `main.maxParticles` = 12000.
- `M6RaidFlowTests`: MainMenu [은신처] → 새 프로필(SaveSandbox 새 폴더) → 키트 권총 장착 → [레이드 시작](흐름 레이드, 적 제거) → 폭풍 Peak(선량 > 0, 하늘 복제본, 폭풍 루프)
  → Peak 중 EXT-01 탈출 → 종료 오버레이 중 `AudioListener.pause` = true·세계 정지 → 은신처: `pause` false, 안개 4값·skybox = 첫 은신처 진입 때 값과 같음(복제본 아님),
  복제본 파괴, 공유 `M6_Sky` 색·값 불변, `IAshStormInfo`·AudioDirector 없음, HideoutMusic 1, 결과 패널 → [확인] → 레이드 2: 선량 0(1초 뒤에도 0), 새 AshStorm Pending·시작 창 안,
  안개 = 레이드 1 씬 원래 값(Exp²·맑을 때 밀도), skybox = 같은 공유 `M6_Sky`.

성능 수치 (배치모드, 렌더 없음, 640×480) — 3회(단독·PlayMode 1·2)
- 폭풍 Peak + 20명 전원 교전(교전 평균 20.0명): AI 평균 0.106~0.134ms·99% 0.21~0.30ms, 투사체 평균 0.032~0.039ms, 프레임 중앙값 1.67~1.76ms·99% 2.4~2.9ms.
- AudioDirector.Update 평균 0.0016~0.0021ms + AshStormController.Update 0.0006~0.0008ms = **0.0022~0.0029ms**(최대 0.107ms) ≤ 0.2ms. GC 할당 프레임 0(잡음 0).
- 전환 쓰기(Apply 1회) 평균 0.0047ms·최대 0.052ms, GC 0. 이벤트 재발행 약 560개(총성 ~500·발소리 ~60) 6.0~6.5µs/이벤트, GC 0(첫 시도).
- 재 파티클 최대 12000 = 상한 12000(배치모드에서도 시뮬레이션이 상한까지 찬다). 오디오: 10초에 재생 ~2400, voice 빼앗기 ~2000, 버림 ~200(24 voice가 총성으로 꽉 참 — 아래 참고).
- 기존 `RaidPerformanceTests`(폭풍 없음, AudioDirector 포함): A AI 평균 0.077ms, B 0.109ms, 프레임 중앙값 1.34~1.39ms.

빌드 (`m6_build.log`)
- 셰이더: `Skybox/Panoramic` 2+2 변형, `URP/Particles/Unlit` ForwardLit vp 4·fp 8, `URP/Lit` ForwardLit vp 8·fp 64, `URP/Terrain/Lit`·`URP/Unlit` 컴파일됨. 셰이더 경고·오류 없음.
  로그는 키워드 이름을 찍지 않아 **FOG_EXP2 변형이 남았는지는 로그로 증명할 수 없다**(GraphicsSettings 안개 스트리핑 = Automatic, 레이드 씬이 Exp² 안개를 켜 둠 → 남아야 정상). V8에서 사람 확인.
  URP Lit 투명(M2_TurretZone)은 확인 씬 전용이라 빌드에 없다. 탈출 구역(URP Unlit 투명)은 포함.
- 포함 확인(빌드 리포트 "Used Assets"): WAV 27개 전부, `M6_Sky.mat`·`Sky_CrimsonDusk.png`(1.5MB)·`M6_AshParticle.mat`·`AshDot.png`·`VP_RaidAtmosphere`·`TL_AshSoil`·팔레트 머티리얼 6개, Palette·Atmosphere·AshStorm·Audio 설정.
- 로그의 `[Licensing::Module] Error: Access token is unavailable`은 라이선스 갱신 메시지(빌드 결과와 무관).

AC 체크
- AC1 자동: 팔레트 HEX·USS·머티리얼 `_BaseColor`(PaletteTests·M6DataAssetTests·PaletteConsistencyTests), 볼륨 값·하늘/재 머티리얼 셰이더·투명(AtmosphereAssetTests), 하늘 그라데이션·태양(SkyPanoramaTests),
  씬 구성(RaidVisualSmokeTests·M6RaidSceneSmokeTests), 빌드 포함. 에디터·빌드 확인: §7.1 V1~V8(특히 V2 태양·그림자 방향, V3 지평선 이음매, V4 재 밀도, V8 빌드 안개).
- AC2 자동: 순수 규칙(Gunshot/Footstep/Surface/Voice/Geiger/Interval), 자리표시 생성·임포트, AudioFlowTests(재질 → 세트, 거리 → 레이어, 3D, 일시정지, 음악 없음), 발행(Player/EnemySoundEventTests),
  성능·GC(M6PerformanceTests), 종료 오버레이 중 정지·은신처 해제(M6RaidFlowTests). 에디터 확인: A1~A9(실제 소리·방향·루프 이음매·자리표시 품질).
- AC3 자동: 일정·단계·선량·시야(AshStormTests·FogMathTests·RadiationDoseTests·AiSightTests·AshStormFlowTests·AiStormSightTests), 흐름 레이드 복원·선량 이월 없음(M6RaidFlowTests),
  Peak 성능·파티클 상한(M6PerformanceTests). 에디터 확인: S1~S5(체감 30m, S5 Play 중지 후 `M6_Sky.mat` git diff 없음), P1(GPU).

버그·관찰 (소스 수정 없음)
- 버그로 판정한 것 없음.
- 관찰: 20명 근접 교전에서 24 voice가 총성으로 차서 10초에 약 200개 소리가 버려진다(우선순위상 발소리가 먼저 빠짐 — 설계 §1.4대로). 교전 중 근처 적 발소리가 안 들릴 수 있다 → A7에서 판단.

---

## 리뷰 수정 메모 (gameplay-programmer, 2026-10-22)
결과(원본): `M6SceneSetup.ApplyAll` 2회 — 1회째가 새 SO 필드를 기존 데이터 에셋 3개에 기록하고 은신처 씬에 AudioSource를 더했다. 2회째 실행 전후 `Assets`·`ProjectSettings`·`Packages` 전체 md5 동일
("WAV 새로 씀 0, 임포트 설정 변경 0, 빈 필드 채움 0"). EditMode **1280/1280**(기존 1267 + 새 `AshStormVisualMathTests` 13, 오디오 보존 테스트 2개는 임시 사본 방식으로 교체), PlayMode **115/115 × 2회 연속**(약 572초·569초),
Windows 빌드 성공(오류 0·경고 0, 110MB). 테스트·빌드 전후 에셋 md5 동일. 커밋 안 함. 로그·결과: scratchpad `m6_rf_*`.
Peak 하늘 확인(PlayMode 로그): 하늘(선형) RGBA(0.102, 0.084, 0.076) = 안개(선형) Ash_Grey.

지적별 처리
- **중간 1 (Peak 하늘 실루엣) — 수정.** 새 순수 `Raid/Logic/AshStormVisualMath`: 강도가 `AshStormConfigSO.skyFogSwapIntensity`(새 필드, 0.5, TODO(GDD-?))를 넘으면
  하늘 복제본의 `_MainTex`를 `Texture2D.whiteTexture`로 바꾸고 `_Tint`를 "지금 안개 색 × 밝기"(밝기 0 → Peak 1)가 그려지게 계산한다(Panoramic 식 `tex × _Tint × unity_ColorSpaceDouble × _Exposure`, Linear/Gamma 둘 다, `_Exposure` 1).
  교체 전에는 기존대로 틴트 → `skyTintToken`, 노출 → `skyExposureAtPeak`(이제 "교체 강도에서 도달")로 어두워져 교체 순간 둘 다 거의 검정 — 이음매 작음.
  Peak에서 지평선뿐 아니라 하늘 전체 = 안개 색. 공유 머티리얼은 그대로(복제본만, 언로드 테스트에 공유 `_MainTex` 불변 단언 추가). 1로 두면 교체 없음(이전 동작).
  테스트: EditMode `AshStormVisualMathTests`(단계 경계 연속, Panoramic 틴트 → 안개 색 왕복, NaN), PlayMode `AshStormFlowTests` Peak에서 복제본 흰 텍스처·하늘 ≈ 안개(±0.002)·안개 = Ash_Grey.
  사람 확인: S1에서 Rising 중간에 하늘이 한 번 검게 가라앉았다가 재 색으로 밝아지는 연출이 거슬리지 않는지.
- **낮음 2 (재 파티클 배율) — 수정(배율·상한 맞춤).** 정상 상태 입자 수 = 방출 × 수명이고 시뮬레이션 속도 배율은 방출·수명을 같이 바꿔 수에 영향이 없다 — 그래서 속도 배율은 `simulationSpeed`로 유지
  (velocityOverLifetime으로 옮기면 수명 10초 동안 낙하 거리가 3배라 재가 지면 아래로 사라진다). 방출 배율은 `AshEmissionMultiplier`로 폭풍 상한이 담는 만큼으로 자른다(기본 데이터: ×4 → 실제 ×2 = 12000).
  상한을 넘는 방출이 없어져 Falling에서 상한이 줄 때 정리되는 입자도 줄었다(남는 정리는 점진적 — TODO V4·S1). SO 값(×4·12000)은 그대로, 툴팁에 "잘린다" 명시.
- **낮음 3 (임포트 설정 되돌림) — 수정.** `M6AudioSetup.CreateAssets`는 이번에 새로 쓴 WAV에만 임포트 설정을 적용한다(`WritePlaceholderWavs(recipes)`가 새로 쓴 목록을 돌려줌, `ApplyImportSettings(paths)`).
  덮어쓴 사용자 WAV의 스테레오·압축 설정을 보존한다. 무인자 `ApplyImportSettings()`는 지웠다.
- **낮음 4 (SO 밖 보기 상수) — 수정.** `AtmosphereConfigSO`에 `skyGradientExponent` 0.6·`sunGlowStrength` 0.8·`ashNoiseFrequency` 0.4·`ashBoxCenterRatio` 0.3·`ashMaxScreenSize` 0.01·`groundNoiseMin` 0.7·`groundTileM` 12,
  `AudioConfigSO`에 `surfaceProbeLiftM` 0.5·`serviceRetrySec` 0.5(전부 이전 상수와 같은 값, TODO(GDD-?)). `M6DataSetup`이 파일에 없을 때만 기록 → AtmosphereConfig·AudioConfig 바이트는 바뀌고 GUID 유지,
  파생물(PNG·머티리얼·지형 레이어·씬)은 값이 같아 그대로. 계산 보호값(FogMath.MaxDensity 등)·텍스처 크기·시드는 상수로 남겼다.
- **낮음 5 (테스트가 실제 에셋을 고침) — 수정(오디오) + 주석(나머지).** `AudioConfigAssetTests`의 사용자 보존 테스트 두 개를 바꿨다: 클립 보존은 메모리 `AudioConfigSO` 인스턴스 + `FillAudioConfig(config)`,
  WAV·임포트 설정 보존은 임시 폴더 `Assets/__M6AudioSetupTestTemp`의 레시피 사본(끝나면 삭제, 남아 있으면 다음 실행이 알림). 커밋된 `FS_Dirt_01.wav`·`AudioConfig.asset`은 더 이상 건드리지 않는다.
  메뉴 재실행 멱등 테스트(Audio·Atmosphere·M6Data)는 실제 에셋에 메뉴를 다시 돌리는 위험을 테스트 머리 주석에 적었다(멱등이라 정상이면 바뀌지 않음).
- **낮음 6 (적용 실패가 종료 코드 0) — 수정.** `M6StormSceneApply.Apply`는 AshStormConfig·AtmosphereConfig가 없으면 아무것도 만들지 않고 null. `M6AudioSceneApply.ApplyRaid/ApplyHideout`은 config 필드가 없으면 false.
  `M6SceneSetup.Apply*IfAssetsExist`는 에셋이 있는데 적용이 실패하면 예외(M4·M5 메뉴가 반쯤 적용된 씬을 저장하지 않음). `Apply Audio (Open Scene)`은 다른 Open Scene 메뉴처럼 저장하지 않는다(MarkSceneDirty만).
- **낮음 7 (폭풍 시각 로그) — 수정.** `Debug.isDebugBuild`일 때만(에디터·개발 빌드).
- **낮음 8 (사전 증가·청취자 탐색) — 수정.** `SurfaceProbe`는 Prewarm한 GroundSurface 콜라이더만 캐시하고 그 밖(지형·적·시체·늦게 생긴 웅덩이)은 매번 판정(TryGetComponent, 할당 없음) — 사전이 레이드 중 자라지 않는다
  (`AudioFlowTests`에 캐시 수 불변 단언). Prewarm은 먼저 비운다. `AudioDirector`의 AudioListener 탐색은 실패 시 `AudioConfigSO.listenerSearchRetrySec`(새 필드 5초, TODO) 동안 다시 하지 않는다.
- **낮음 9 (정리) — 수정.** 주석 갱신(M4SceneSetup 2곳·M5SceneSetup·M4MapSetup), `AiConfigSO.showStateIndicator` 기본값 false, 안 쓰는 `AudioVoicePool.StopAll`·`AmbientSoundscape.StopAll`·`SurfaceProbe.ClearCache` 삭제,
  `PlayedSound.Surface`/`Layer`는 해당 종류만 의미(그 밖 default) + `HasSurface`·`HasLayer`. `FindRoot`·`FindOrCreateRoot`·`GetOrAdd`를 `SetupUtil`로 모으고 M6Visual/Storm/Audio 씬 적용이 쓴다(M6ArtSetup의 VolumeProfile용 GetOrAdd는 다른 함수라 유지).
  확인용 메뉴 `Apply Visual (Game Scenes)`·`Apply Audio (Raid + Hideout)`와 배치 진입점 `M6StormSceneApply.ApplyRaidScene`은 지웠다(통합 메뉴와 같은 일, 참조 없음). "(Open Scene)" 메뉴 3개는 남겼다.
- **낮음 10 (CHANGELOG·M6RaidFlowTests) — 수정/해결됨.** `docs/CHANGELOG_GDD.md`에 "2026-10-22 — M6 설계 결정" 기록(설계 §8 초안 + 구현 메모 + 리뷰 수정). M6RaidFlowTests는 4단계가 만들었다.
- **낮음 11 (HideoutMusic AddComponent) — 수정.** `M6AudioSceneApply.ApplyHideout`이 HideoutMusic 오브젝트에 AudioSource를 같이 둔다(playOnAwake 끔·루프·2D, 값이 다를 때만 기록). `HideoutMusic.Awake`는 기존대로 GetComponent 우선.

바뀐/새 파일
- 새: `Raid/Logic/AshStormVisualMath.cs`, `Tests/EditMode/AshStormVisualMathTests.cs`(+ Unity 생성 .meta).
- 코드: `Raid/Data/AshStormConfigSO.cs`·`AtmosphereConfigSO.cs`, `Raid/Runtime/AshStormPresenter.cs`·`AshStormController.cs`, `Audio/Data/AudioConfigSO.cs`, `Audio/Runtime/AudioDirector.cs`·`SurfaceProbe.cs`·`AudioVoicePool.cs`·`AmbientSoundscape.cs`,
  `AI/Data/AiConfigSO.cs`, `EditorTools/SetupUtil.cs`·`M6DataSetup.cs`·`M6ArtSetup.cs`·`SkyPanorama.cs`·`M6VisualSceneApply.cs`·`M6StormSceneApply.cs`·`M6AudioSceneApply.cs`·`M6AudioSetup.cs`·`M6SceneSetup.cs`·`M4SceneSetup.cs`·`M5SceneSetup.cs`·`M4MapSetup.cs`.
- 테스트: `EditMode/M6DataAssetTests.cs`·`AudioConfigAssetTests.cs`·`AtmosphereAssetTests.cs`, `PlayMode/AshStormFlowTests.cs`·`AudioFlowTests.cs`.
- 에셋(메뉴가 기록): `Data/Raid/AtmosphereConfig.asset`·`AshStormConfig.asset`, `Data/Audio/AudioConfig.asset`(새 필드), `Scenes/Hideout.unity`(HideoutMusic AudioSource).
- 문서: `docs/CHANGELOG_GDD.md`(M6 설계 결정), 이 메모.
