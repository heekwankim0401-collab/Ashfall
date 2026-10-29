# 마일스톤 설계·리뷰 노트

각 마일스톤을 시작할 때 `unity-architect`가 쓴 설계 문서와, 커밋 전 `code-reviewer`의 지적 목록이다.
설계 문서 끝에는 단계별 구현 메모(1단계 계약, 2a~2d 병렬, 3단계 통합, 4단계 검증, 리뷰 수정)가 붙어 있어
설계와 실제 구현이 달라진 곳을 찾을 수 있다. 문서 안의 절대 경로(공유 노트 폴더, scratchpad)는 작성 당시 위치라 지금은 없을 수 있다.

| 파일 | 내용 |
|---|---|
| `M2_design.md` | 체력·상태이상, 그리드 인벤토리, 루트 검색, RULE-04/05. 이벤트 버스·서비스 레지스트리 도입 |
| `M3_design.md`, `M3_review.md` | 적 AI(SCV/RDR 상태 머신·감각·엄폐·분대), 무기 로직 Core 이동, 피해 필드 |
| `M4_design.md`, `M4_review.md` | 레이드 루프(Crimson Basin, 탈출 EXT-01~04, 타이머, 결과 화면, RULE-01). §5 질문 Q1~Q22와 결정 |
| `M5_design.md`, `M5_review.md` | 은신처 & 세이브(보관함·상인·부랑자 키트, save.json, RULE-03 강제 종료, 은신처 ↔ 레이드 루프). §5 질문 Q1~Q31 |
| `M6_design.md`, `M6_review.md` | 아트 & 오디오 패스(팔레트·포스트·하늘·재 파티클, 지면별 발소리·총성 거리 레이어·환경음, 재 폭풍). 자리표시 에셋 목록. §5 질문 Q1~Q30 |
| `M7_design.md`, `M7_review.md` | 콘텐츠 확장 & 폴리시(하위 단계 7A 무기 5종·교체·고장 / 7B AI-SNP·MUT·BOS / 7C 방사능·탈수·허기·제작대 / 7D 성능·빌드·진단·희귀도 색). §5 질문 Q1~Q53, §10 MVP 남은 임시값 |
| `MVP_finish_design.md`, `MVP_finish_review.md` | MVP 마무리(F1 전신 사인 / F2 보관함 업그레이드·세이브 v3 / F3 내구도 대략 표시·수리·가격 / F4 방어구 관통·둔탁, 플레이어·적). §5 질문 MF-Q1~Q24(+Q25 리뷰), §7.1 사람 확인 목록 |
| `MapPolish_design.md`, `MapPolish_review.md` | 맵 개선(2026-10-26, 사용자 피드백 "어둡다·허허벌판"): 밝기 개정·POI 점광원 14·랜드마크 프리팹 8·중간 구조물 29·NavMesh 다시 굽기와 확인, 성능 전후 |
| `AssetSwap_design.md` | 에셋 교체(2026-10-26): 에셋스토어 무료 패키지(git 제외)로 스킨 프리팹을 만들어 무기·적·구조물·상자·드럼통 블록아웃 위에 얹음(`BlockoutSkin`), 라이선스 처리·배정표·성능 |
| `MapDensity_design.md` | 맵 밀도(2026-10-29, 피드백 "허허벌판"): 건물 80·소형 구조물 150·차량 40, 죽은 나무 1,500·덤불·통나무·풀(Terrain Detail), LOD·컬·그림자, 루트 지점 약 200 계약(⓪) — ③ 상자·트렁크·루트는 다음 단계 |
| `AdsBallistics_design.md` | 정조준·탄도(2026-10-28): 무기별 가늠자 기준점 `SightAnchor`·정조준 자세, 공기 저항 `dragPerM`·영점 `zeroRangeM`, 테스트 실측 표·남은 위험 |
| `MVP_final.md` | MVP 최종 정리(2026-10-24): 이 PC 1080p 성능 실측(HD 620)·판단, 임시값 "MVP 기본값(플레이 검증 전)" 확정, 남은 일(플레이 확인·실제 에셋·수치 조정·GTX 1660 실측·원격 백업) 위치 |

진행 방식(M2~M4에서 쓴 것):
1. `unity-architect`가 설계 문서를 쓴다(작업 분할, 파일 소유, AC별 테스트 계획, CHANGELOG 초안 포함).
2. 1단계 "계약": Core 타입·인터페이스·SO 껍데기를 원본에서 만들고 EditMode 통과.
3. 2단계 병렬: 에이전트마다 프로젝트 사본(Assets·Packages·ProjectSettings·Library 복사)에서만 Unity를 돌리고, 원본에는 자기 파일만 쓴다.
4. 3단계 통합: 원본에서 처음 함께 컴파일하고 씬 생성 메뉴를 `-executeMethod`로 실행.
5. 4단계: `test-engineer`가 AC별 PlayMode 테스트·성능·Windows 빌드. 동시에 `code-reviewer`가 읽기 전용 리뷰.
6. 리뷰 수정 후 원본에서 EditMode·PlayMode(2회 연속)·빌드 재확인, `docs/CHANGELOG_GDD.md`에 결정 기록, 한국어 커밋.
