# MVP 최종 정리 (2026-10-24)

사용자 결정(2026-10-24): "플레이 확인은 건너뛰고 마무리까지 다 해 달라." 사람의 에디터·플레이 확인 없이 MVP(M0~M7 + 마무리 F1~F4)를 닫는다.
이 문서는 마지막 자동 성능 실측, 임시값 처리, 남은 일의 위치를 모은다. 결정 사유는 `docs/CHANGELOG_GDD.md` "MVP 최종 정리" 절.

## 1. 상태
- 구현: M0~M7, MVP 마무리 F1~F4 전부. 자동 테스트(EditMode·PlayMode)·IL2CPP 릴리스/개발 빌드·스모크 통과.
- **사람 플레이 확인 미실시** — 아래 §4의 목록이 그대로 남아 있다.
- GDD §15 체크박스: 기능 AC는 체크, 성능 AC 2개(M3 "동시 적 20명 60fps", M7 "성능 목표 달성")는 GTX 1660 실측 전이라 비워 두고 주석을 달았다.

## 2. 자동 성능 실측 (이 PC)
- PC: **Intel(R) HD Graphics 620**(내장, 드라이버 31.0.101.2140, D3D11 level 11.1), Intel Core i7-7500U, RAM 16GB, 모니터 1920×1080 59Hz.
  GDD §13.1 목표 GPU(GTX 1660)보다 훨씬 낮은 노트북 내장 GPU다 — **GTX 1660 판정은 "다른 GPU 실측"으로 남는다.**
- 빌드: IL2CPP 개발 빌드 `Builds/Windows64Dev/`(이번 커밋 코드). 명령:
  `Ashfall.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 1 -ashfallSmoke <임시 폴더> -ashfallSmokeRaidSec 90 -logFile perf.log`
  스모크 흐름 그대로(Boot → MainMenu → Hideout → Raid, 폭풍 강제 시작 → 레이드 90초 → 포기 → Hideout), 종료 0·오류·예외 로그 0.
  레이드 체류 시간 인자 `-ashfallSmokeRaidSec`·로그의 95% 값은 이번에 더했다(§5).
- 구간: 폭풍 Rising 20초 → Peak(강도 1.00, 시야 30m) 70초, 적 34명 배치 그대로. **플레이어는 스폰에 가만히 있어 교전·변이체 무리는 포함되지 않았다**(입력 자동화 없음).

| 해상도(전체 화면) | 구간 | 프레임 중앙값 | 95% | 99% | fps(평균) |
|---|---|---|---|---|---|
| 1920×1080 | 레이드 초반(폭풍 Rising) | 27.2~27.6ms | 30.7~32.0ms | 31.3~33.5ms | 36~38 |
| 1920×1080 | **폭풍 Peak 정상 구간(10초 × 6)** | **32.4~32.9ms** | **34.3~34.5ms** | 34.6~37.9ms | **31** |
| 1280×720 | 폭풍 Peak 정상 구간(10초 × 5) | 13.3~13.4ms | 13.6~15.1ms | 14.3~15.2ms | 73~76 |

(첫 10초 창은 씬 로딩 정지(최대 2.7초)가 섞여 표에서 뺐다. 원본 로그는 남기지 않음.)

- **판정: 이 PC(HD 620)에서 1080p 60fps 미충족**(Peak 중앙값 32.6ms ≈ 31fps). 720p에서는 Peak 중앙값 13.4ms로 60fps 충족.
- **병목:** 픽셀 수 2.25배(720p → 1080p)에 프레임 시간 약 2.4배 → 해상도에 비례하는 GPU(채움률·포스트·파티클 오버드로) 병목. CPU 쪽은 배치모드 예산(AI 0.16~0.19ms, 렌더 제외 프레임 1.9ms)으로 여유가 크다.
- **조정 판단: 데이터 값은 바꾸지 않는다.** 1080p에서 2배 가까운 차이는 `AtmosphereConfigSO` 재 파티클 수·그림자 거리 같은 미세 조정으로 메울 수 있는 크기가 아니고, 원인이 목표보다 한참 낮은 GPU 등급이다.
  목표 GPU(GTX 1660, 외장 GPU로 HD 620보다 수 배 빠름)에서 실측한 뒤 판단한다. 그때도 부족하면 먼저 Peak 재 파티클 배율(`AshStormConfigSO.ashEmissionMultiplier`)·URP 그림자 거리·렌더 스케일 순으로 데이터만 조정한다.

## 3. 임시값
- `docs/design/M7_design.md` §10(23항목)과 `docs/design/MVP_finish_design.md` §2·§11의 `TODO(GDD-?)` 임시값을 **"MVP 기본값(플레이 검증 전)"으로 확정**한다 — 값은 바꾸지 않았다.
  코드의 `TODO(GDD-?)` 주석은 위치 표시로 그대로 둔다. 기획이 바꿀 때는 SO 에셋 값만 고치면 된다(§14).

## 4. 남은 일
1. **사람 플레이 확인**: `M6_design.md` §7.1(V1~V8·A1~A9·S1~S5·P1), `M7_design.md` §7.1(W1~W8·E1~E7·C1~C8·P1~P4), `MVP_finish_design.md` §7.1(1~6).
2. **GTX 1660급 실측**: 위 §2 명령으로 1080p 전체 화면. 가능하면 직접 플레이로 교전·변이체 무리까지(그 경우 `-ashfallSmoke` 없이 개발 빌드를 실행하면 10초마다 프레임 줄만 기록된다).
3. **실제 에셋 교체**: `M6_design.md` §2.3, `M7_design.md` §2.3(자리표시 WAV·프리팹·머티리얼·하늘/재/지형 텍스처, 같은 경로에 덮어쓰면 유지).
4. **수치 조정**: §3의 임시값 목록(M7 §10 + MVP 마무리 §11) — 플레이 확인 결과로.
5. **원격 백업**: 저장소에 원격이 없다. 사용자와 함께 다른 세션에서 설정한다.

## 5. 이번 정리에서 바꾼 코드 (진단만, 게임 동작 변화 없음)
- `SmokeRunner.RaidSecArgumentName`(`-ashfallSmokeRaidSec <초>`)·`TryParseRaidDuration`·`TimeoutForRaidDuration`: 스모크 레이드 체류 시간(기본 10초 그대로)과 그만큼 늘어나는 제한 시간. 잘못된 값 = 스모크 실패(종료 1).
- `FrameTimeStats`/`FrameTimeSummary.P95Ms`, `[DevDiagnostics]` 줄에 "95%" 추가(nearest-rank, 99%와 같은 방식).
- EditMode 테스트 3개 추가·단언 2개 보강(`SmokeRunnerTests`·`FrameTimeStatsTests`).
- 검증(원본): EditMode 1966/1966, PlayMode 188/188(종료 0). IL2CPP 개발 빌드 14분 34초·릴리스 빌드 16분 0초, 둘 다 Succeeded·오류 0·경고 0, 빌드 전후 ProjectSettings md5 동일.
