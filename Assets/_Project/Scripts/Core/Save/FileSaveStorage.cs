using System;
using System.IO;
using System.Text;

namespace Ashfall.Core
{
    // 파일 세이브 저장소 (M5 설계 §2.5). 임시 파일에 쓰고 디스크까지 Flush → File.Replace로 교체(직전 세대 .bak).
    // 파일: <dir>/save.json, save.json.bak, save.json.tmp(일시), save.corrupt.json(손상 시).
    // 읽을 때 save.json이 있으면 남은 .tmp는 무시한다(다음 쓰기가 덮어씀). save.json이 없을 때만 .tmp를 읽는다(TryRead).
    // .bak 자동 복구는 하지 않는다(RULE-03 우회 위험, Q22).
    public sealed class FileSaveStorage : ISaveStorage
    {
        public const string DefaultFileName = "save.json";

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public FileSaveStorage(string directory, string fileName = DefaultFileName)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("세이브 폴더가 비어 있다.", nameof(directory));
            if (string.IsNullOrEmpty(fileName))
                fileName = DefaultFileName;
            DirectoryPath = directory;
            FilePath = Path.Combine(directory, fileName);
            TempPath = FilePath + ".tmp";
            BackupPath = FilePath + ".bak";
            CorruptPath = Path.Combine(directory,
                Path.GetFileNameWithoutExtension(fileName) + ".corrupt" + Path.GetExtension(fileName));
        }

        public string DirectoryPath { get; }  // <dir>
        public string FilePath { get; }       // <dir>/save.json
        public string TempPath { get; }       // <dir>/save.json.tmp
        public string BackupPath { get; }     // <dir>/save.json.bak
        public string CorruptPath { get; }    // <dir>/save.corrupt.json
        public string Location => FilePath;

        // 마지막 TryRead가 save.json 대신 save.json.tmp를 읽었나 (로그·테스트용)
        public bool LastReadFromTemp { get; private set; }

        // save.json이 없고 .tmp가 있으면 .tmp를 읽는다 (M5 리뷰 9): 대체 경로의 Delete ~ Move 사이,
        // 또는 첫 저장의 Flush ~ Move 사이에 종료된 경우 .tmp가 가장 최근에 쓰려던 프로필이다.
        // save.json이 있으면 .tmp는 무시한다(교체 전 종료 — save.json이 마지막으로 완료된 저장). .bak은 읽지 않는다.
        public SaveReadStatus TryRead(out string text, out string error)
        {
            text = null;
            error = null;
            LastReadFromTemp = false;
            try
            {
                if (!File.Exists(FilePath))
                {
                    if (!File.Exists(TempPath))
                        return SaveReadStatus.Missing;
                    text = File.ReadAllText(TempPath, Encoding.UTF8);
                    LastReadFromTemp = true;
                    UnityEngine.Debug.LogWarning($"[FileSaveStorage] {FilePath}이 없어 남은 임시 파일을 읽는다: {TempPath}");
                    return SaveReadStatus.Ok;
                }
                text = File.ReadAllText(FilePath, Encoding.UTF8);
                return SaveReadStatus.Ok;
            }
            catch (Exception e)
            {
                error = Describe(e);
                return SaveReadStatus.Error;
            }
        }

        public bool TryWrite(string text, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                using (var stream = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Utf8NoBom.GetBytes(text ?? string.Empty);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);   // 디스크까지
                }

                if (File.Exists(FilePath))
                {
                    try
                    {
                        // 교체와 직전 세대 백업을 한 번에
                        File.Replace(TempPath, FilePath, BackupPath, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // 대체 경로: 짧은 비원자 구간. 도중에 실패해도 tmp가 남아 수동 복구할 수 있다.
                        File.Copy(FilePath, BackupPath, true);
                        File.Delete(FilePath);
                        File.Move(TempPath, FilePath);
                    }
                }
                else
                {
                    File.Move(TempPath, FilePath);
                }
                return true;
            }
            catch (Exception e)
            {
                // IOException·UnauthorizedAccessException 등. 호출자가 LogError·알림.
                error = Describe(e);
                return false;
            }
        }

        public bool TryQuarantine(out string movedTo, out string error)
        {
            movedTo = null;
            error = null;
            try
            {
                // save.json이 없으면 TryRead가 읽은 .tmp를 옮긴다 (M5 리뷰 9)
                string source = File.Exists(FilePath) ? FilePath : File.Exists(TempPath) ? TempPath : null;
                if (source == null)
                {
                    error = "옮길 세이브 파일이 없다.";
                    return false;
                }
                if (File.Exists(CorruptPath))
                    File.Delete(CorruptPath);   // 덮어씀 (이전 손상본은 버린다)
                File.Move(source, CorruptPath);
                movedTo = CorruptPath;
                return true;
            }
            catch (Exception e)
            {
                error = Describe(e);
                return false;
            }
        }

        private static string Describe(Exception e) => $"{e.GetType().Name}: {e.Message}";
    }
}
