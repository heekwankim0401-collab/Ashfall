namespace Ashfall.Core
{
    public enum SaveReadStatus
    {
        Missing,   // 파일 없음 (첫 실행)
        Ok,
        Error,     // 읽기 예외 (권한·잠금 등)
    }

    // 세이브 텍스트 저장소 (M5 설계 §2.6). 런타임 기본 = FileSaveStorage(persistentDataPath).
    // 테스트는 OS 임시 폴더의 FileSaveStorage나 메모리 가짜를 주입한다(실제 save.json을 건드리지 않게).
    // 모든 메서드는 예외를 밖으로 던지지 않는다.
    public interface ISaveStorage
    {
        string Location { get; }                                     // 로그·알림용 (파일 경로)
        SaveReadStatus TryRead(out string text, out string error);
        bool TryWrite(string text, out string error);                // 원자적 (§2.5)
        bool TryQuarantine(out string movedTo, out string error);    // 손상 파일 → save.corrupt.json
    }
}
