# GDD 변경 기록

> GDD §0: 문서 수정이 필요하면 이 파일에 사유를 먼저 기록한 뒤 `docs/GDD.md`를 고친다.
> 형식: `## YYYY-MM-DD — 섹션 ID` 아래에 변경 내용과 사유.

## 2026-10-21 — 최초 반입
- `docs/GDD.md`를 원본 기획서 그대로 저장소에 추가. 내용 변경 없음.

## 2026-10-21 — §13.2 폴더 구조
- `Scripts/EditorTools/`(어셈블리 `Ashfall.EditorTools`, 에디터 전용) 추가.
- 사유: 씬·에셋은 YAML을 손으로 쓰지 않고 에디터 메뉴로 생성하는데(M0 Boot·MainMenu 씬), 에디터 전용 코드를 둘 폴더가 §13.2에 없었다.
  이름을 `Editor`로 하지 않은 이유: `Ashfall.Editor` 네임스페이스가 다른 Ashfall 코드에서 `UnityEditor.Editor`를 가린다.
