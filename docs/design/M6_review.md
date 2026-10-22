# M6 코드 리뷰 지적 (code-reviewer, 읽기 전용) — 높음 없음, 중간 1, 낮음 10

범위: cb95547 대비 작업 트리. 설계 §4·1a~3단계 메모의 Core(PaletteSO·IAshStormInfo·StrideCadence·GameEvents·ServiceRegistry), Audio 전부, Raid(FogMath·AshStorm*·AtmosphereConfigSO·AshStormConfigSO),
Player(PlayerMotor·PlayerHealth·RadiationDoseModel·PlayerConfigSO), Weapons(WeaponController), AI(SightCone·AiDirector·EnemyAgent·AiConfigSO), EditorTools(M6*·PaletteMaterialMap·SkyPanorama·WavWriter·SfxSynth·M4/M5SceneSetup 변경), 새 테스트 일부.
[확인] = 코드로 확인, [추정] = 실행 없이 추론. 경로는 `Assets/_Project/Scripts/` 기준.

## 중간
1. 재 폭풍 Peak에서 30m 밖 윤곽이 하늘을 배경으로 보일 수 있다 → §8.3 "시야 30m"와 Q26(플레이어만 눈이 머는 불공정 방지) 약화 — Raid/Runtime/AshStormPresenter.cs:150-183, Raid/Data/AshStormConfigSO.cs:29-36, EditorTools/M6ArtSetup.cs:338-342.
   [확인] 안개(Exp²)는 지오메트리에만 걸린다. 폭풍 하늘은 원본 파노라마 × `_Tint` lerp(회색 0.5 → Ash_Grey) × `_Exposure` 0.35이고, 안개 색은 lerp(Dried_Blood → Ash_Grey) = Peak에서 Ash_Grey(#5A524E)다.
   [추정] Peak 지평선 하늘 ≈ Rust_Red × (Ash_Grey/0.5 ≈ 0.7) × 0.35 ≈ 거의 검은 적색. 30m 밖 지형·구조물·적은 균일한 Ash_Grey 덩어리라 능선·지붕 위 적의 윤곽이 하늘을 배경으로 또렷하다.
   AI는 30m 상한으로 못 보는데 플레이어는 실루엣으로 100m+ 표적을 찾을 수 있다(§7.1 S1·S2에서 확인 필요).
   고칠 방향: 강도에 따라 하늘을 안개 색으로 수렴시킨다. 예: 복제 하늘의 `_Tint`·`_Exposure`를 "지평선 픽셀 ≈ 폭풍 안개 색"이 되게 정하거나, 강도 1에서 하늘 대신 안개 색 단색(카메라 SolidColor 또는 단색 스카이박스)으로 lerp한다.
   또는 `skyTintToken`·`stormFogToken`·노출 기본값을 같은 결과가 나게 맞추고 S1에서 확인한다. 수치는 AshStormConfigSO에 둔다.

## 낮음
2. Raid/Runtime/AshStormPresenter.cs:191-195, EditorTools/M6VisualSceneApply.cs:488 — `simulationSpeed`×3이 방출(시뮬레이션 초당)도 ×3 해서 처리량은 ×12다. 그런데 평시가 이미 상한(600/s × 10s = 6000 = `ashMaxParticles`)이고 폭풍 상한이 12000이라 실제 밀도는 ×2에 그친다.
   Falling에서 상한이 줄 때마다 남는 입자가 정리된다[추정: 순간 사라짐]. "방출 ×4" 의도와 다르므로 배율·상한을 다시 맞추거나 속도 배율은 velocityOverLifetime으로 옮긴다(V4·S1에서 확인).
3. EditorTools/M6AudioSetup.cs:122-163 — `ApplyImportSettings`가 레시피 경로 전부(사용자가 같은 이름으로 덮어쓴 WAV 포함)를 매 실행 모노·Vorbis 0.7로 되돌린다.
   설계 §2.3은 바람·폭풍 루프·은신처 BGM을 "스테레오 가능"이라 하는데 A9 절차를 따르면 다음 메뉴 실행에 모노가 된다. 파일을 새로 쓴 경우에만 임포트 설정을 강제하거나, `LoopPaths`는 forceToMono를 건드리지 않는다.
4. EditorTools/M6VisualSceneApply.cs:46-48(`AshMaxScreenSize`·`AshNoiseFrequency`·`AshBoxCenterAboveM`), SkyPanorama.cs:19-21(`GradientExponent`·`GlowStrength`), M6ArtSetup.cs:32-36(`GroundTileM`·`GroundNoiseMin`) —
   보기 튜닝 수치가 SO 밖 상수다(TODO 표시는 있음). Q30 "수치의 원본은 AtmosphereConfigSO"·§0에 맞추려면 AtmosphereConfigSO로 옮긴다(재 파티클 수치는 이미 거기 있어 같은 묶음).
   Audio의 `ServiceRetrySec`·`SurfaceProbe.LiftM`도 TODO 표시만 있다(AudioConfigSO 필드 후보). FogMath.MaxDensity·GeigerClickModel.MaxClicksPerTick·IntervalScheduler.MinIntervalSec·ClipPicker.MaxJitter는 계산 보호값이라 그대로 둬도 된다.
5. Tests/EditMode/AudioConfigAssetTests.cs:103-188, AtmosphereAssetTests(재실행 테스트) — EditMode 테스트가 실제 커밋 에셋(`FS_Dirt_01.wav`·`AudioConfig.asset`·M6 아트 파생물)을 고쳤다 `finally`로 되돌린다.
   테스트 도중 에디터가 죽거나 도메인이 다시 로드되면 작업 트리에 변경이 남는다. 에셋이 없으면 `CreateAssets()`→`M6DataSetup.CreateData()`가 테스트 중 프로젝트에 에셋을 만든다. 임시 폴더 사본으로 검증하거나 최소한 이 위험을 테스트 머리 주석에 적는다.
6. EditorTools/M6AudioSceneApply.cs:87-92(적용 false·저장 실패를 로그만), M6StormSceneApply.cs:41-52·69-82(AshStormConfig·Atmosphere null이어도 컨트롤러를 돌려줌 → `M6SceneSetup.ApplyRaid`가 성공으로 봄), M6SceneSetup.cs:66-82(`Apply*IfAssetsExist`가 반환값 무시) —
   배치 실행에서 일부 적용 실패가 종료 코드 0으로 지나가고 M4·M5 메뉴는 반쯤 적용된 씬을 저장한다. 실패를 예외로 올리거나(M6SceneSetup.ApplyToSceneFile처럼) config null이면 null을 돌려준다.
   `M6AudioSceneApply.ApplyOpenScene`만 저장까지 하는 것도 다른 "Open Scene" 메뉴와 다르다.
7. Raid/Runtime/AshStormController.cs:113 — 시작 시각을 `Debug.Log`로 남겨 빌드 Player.log에서 폭풍 시각을 알 수 있다(사소). `Debug.isDebugBuild`일 때만 남기거나 에디터 전용으로.
8. Audio/Runtime/SurfaceProbe.cs:28·85-94 — 처음 만나는 콜라이더(적 캡슐·시체 등)가 사전에 영구히 쌓이고 파괴돼도 지우지 않는다. 레이드 길이에선 작지만 사전 재할당이 이벤트 경로에서 일어날 수 있다.
   AudioDirector.cs:233-234 — 표적·청취자가 없으면 0.5초마다 `FindAnyObjectByType<AudioListener>`. 실패 시 재탐색 간격을 늘리거나 사전 크기를 넉넉히(또는 씬 언로드·Prewarm에서 정리).
9. 정리:
   - 갱신 안 된 주석: M4SceneSetup.cs:42·76("색은 M6", "M6에서 교체"), M5SceneSetup.cs:35, M4MapSetup.cs:168. 설계 §4.7이 갱신하기로 한 항목이다.
   - `AiConfigSO.showStateIndicator` 필드 기본값이 아직 true(AiConfigSO.cs:134): 에셋은 false지만 새로 만든 AiConfig·테스트 인스턴스는 표시가 켜진다.
   - 쓰지 않는 `AudioVoicePool.StopAll`(:76)·`AmbientSoundscape.StopAll`(:78)·`SurfaceProbe.ClearCache`(:51).
   - 총성 `LastPlayed.Surface`에 의미 없는 `untaggedSurface`가 들어간다(AudioDirector.cs:299).
   - `GetOrAdd`·`FindRoot`가 EditorTools 4파일에 중복된다.
   - 2a·2b·2c 단독 확인용 메뉴(`Apply Visual (Game Scenes)`·`Apply Audio (Raid + Hideout)`·`ApplyRaidScene`)는 통합 메뉴와 겹치므로 정리하거나 "확인용" 표시를 남긴다.
10. 문서·테스트: `docs/CHANGELOG_GDD.md`에 "M6 설계 결정"이 아직 없다(§0, 설계 §8 초안 — VFX Graph → Particle System, Height Fog 해석 등). 4단계 계획의 `PlayMode/M6RaidFlowTests.cs`(폭풍 Peak 탈출 → 은신처 원상·선량 0)가 없다.
    리뷰 수정 단계에서 둘 다 처리한다(진행 중이면 무시).
11. Audio/Runtime/HideoutMusic.cs:22-24 — 씬에 AudioSource를 두지 않아 은신처에 들어올 때마다 Awake에서 `AddComponent<AudioSource>`(설계 §1.7 "Awake 1회 생성"의 범위라 무해). M6AudioSceneApply.ApplyHideout이 AudioSource를 같이 붙이면 런타임 AddComponent가 없어진다.

## 확인한 것 (문제 없음)
- 의존 방향: Audio·Raid asmdef 참조는 Ashfall.Core뿐이다. 기능 코드에 다른 기능 `using` 없음(EditorTools만 Audio·Raid·Player·AI·Inventory 참조). 새 asmdef 없음.
- 싱글톤: GameManager 하나. AudioDirector·AshStormController·HideoutMusic에 정적 인스턴스 없음. 기능 코드에 `Time.timeScale` 쓰기 없음(Audio는 `AudioListener.pause`만, OnDisable·OnDestroy에서 false). 옛 `Input.Get*` 없음.
- 구독·등록 짝: AudioDirector 구독은 OnEnable/Update에서 하고 OnDisable/OnDestroy에서 해지(같은 버스·같은 대리자). AshStormController는 OnEnable Register / OnDisable Unregister(같은 인스턴스만 해제).
  PlayerHealth·PlayerMotor는 새 구독 없음(서비스는 매 프레임 TryGet).
- 파괴된 객체: AudioDirector.DropDestroyedServices, AiDirector.ValidateServices(Storm 포함), EnemyAgent 표적 검사, AshStormPresenter.OnDestroy(파티클·태양 Unity null 검사).
- 공유 에셋: 하늘은 `new Material` 복제본만 바꾸고 강도 0·OnDestroy에서 원본 복귀 + 복제본 파괴. 안개는 이 씬이 활성일 때만 복원(씬별 RenderSettings). 머티리얼 `.material` 런타임 접근 없음.
- 할당: FootstepTaken·ShotFired 처리(거리 컷 → RaycastNonAlloc 4칸 → 캐시 → ClipPicker·VoiceAllocator)·AmbientSoundscape.Tick·GeigerClickModel·AshStormModel.Evaluate·presenter(값 같으면 무동작)·SightCone cap·StrideCadence에 할당 없음. 이벤트는 readonly struct.
- 수치: 런타임 수치는 AudioConfigSO·AshStormConfigSO·AtmosphereConfigSO·PlayerConfigSO·AiConfigSO에 있고 GDD 밖 값은 TODO(GDD-?) 표시(낮음 4 제외).
  §10.1 HEX 7개, §10.2 채도 −30·그레인 0.3·비네트 0.35, §8.3 30m·+1/s·Peak 180초 = GDD.
- M7 선행 없음: 방사능은 누적만(상한·효과·UI·이벤트·방독면·해독제 없음, TODO Q27), 가이거는 폭풍 방사능률에만(Q22), 무기 교체·새 적 없음. Toxic은 GDD 팔레트 토큰일 뿐.
- §8.3·§10·§11 동작: 레이드당 1회(Over 뒤 없음), 건너뛴 단계도 순서대로 발행, 시간 = 레이드 경과(일시정지 정지). AI 시야 = min(기존, VisibilityM)이고 청각은 그대로.
  적 총성은 `ShotFired`만(NoiseEmitted 없음 — M3 규칙 유지). 총성 근/중/원 구간 선형·경계 연속. 레이드·메인 메뉴 음악 없음, 은신처만 ignoreListenerPause. Skybox/Panoramic UV 규약·태양 방향(+Z 0°, 시계)·Sun forward = −태양 방향 일치.
- 정확성: StrideCadence(첫 걸음 절반·틱당 1걸음), EnemyWeapon.Tick은 0/1만 돌려줌(발당 ShotFired 1회 맞음), FogMath 왕복·경계, VoiceAllocator 빼앗기 규칙, Geiger 푸아송·상한, WavWriter RIFF 헤더.
- 에디터 메뉴 멱등: 이름으로 찾아 재설정·GetOrAdd, 값이 다를 때만 기록, WAV는 없을 때만·SO 클립은 빈 필드만, 볼륨 프로필·머티리얼·PNG는 같은 경로에 덮어쓰기(GUID 유지). 3단계 메모의 2회 실행 md5 동일과 맞다.
  확인 씬(M1~M3) 미적용, 빌드 설정 변경 없음.
