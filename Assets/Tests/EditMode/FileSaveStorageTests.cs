using System;
using System.IO;
using Ashfall.Core;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // §13.3 세이브 파일 (M5 설계 §2.5): 임시 파일 → 교체(원자적), 직전 세대 .bak, 손상 격리.
    // OS 임시 폴더의 테스트마다 새 폴더만 쓴다(실제 persistentDataPath 아님, §2.6).
    public class FileSaveStorageTests
    {
        private string directory;
        private FileSaveStorage storage;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "AshfallTests", Guid.NewGuid().ToString("N"));
            storage = new FileSaveStorage(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        [Test]
        public void Paths()
        {
            Assert.AreEqual(Path.Combine(directory, "save.json"), storage.FilePath);
            Assert.AreEqual(storage.FilePath, storage.Location);
            Assert.AreEqual(Path.Combine(directory, "save.json.tmp"), storage.TempPath);
            Assert.AreEqual(Path.Combine(directory, "save.json.bak"), storage.BackupPath);
            Assert.AreEqual(Path.Combine(directory, "save.corrupt.json"), storage.CorruptPath);
            StringAssert.StartsWith(Path.GetTempPath(), storage.FilePath, "테스트는 OS 임시 폴더만 쓴다");
        }

        [Test]
        public void Constructor_EmptyDirectory_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FileSaveStorage(""));
            Assert.Throws<ArgumentException>(() => new FileSaveStorage(null));
        }

        [Test]
        public void Read_Missing()
        {
            Assert.AreEqual(SaveReadStatus.Missing, storage.TryRead(out string text, out string error));
            Assert.IsNull(text);
            Assert.IsNull(error);
            Assert.IsFalse(Directory.Exists(directory), "읽기는 폴더를 만들지 않는다");
        }

        [Test]
        public void FirstWrite_CreatesDirectoryAndFile_NoTempNoBackup()
        {
            Assert.IsTrue(storage.TryWrite("{\"a\":1}", out string error), error);

            Assert.IsTrue(File.Exists(storage.FilePath));
            Assert.IsFalse(File.Exists(storage.TempPath));
            Assert.IsFalse(File.Exists(storage.BackupPath));
            Assert.AreEqual(SaveReadStatus.Ok, storage.TryRead(out string text, out _));
            Assert.AreEqual("{\"a\":1}", text);
        }

        [Test]
        public void SecondWrite_PreviousGenerationInBackup()
        {
            Assert.IsTrue(storage.TryWrite("first", out _));
            Assert.IsTrue(storage.TryWrite("second", out string error), error);

            Assert.AreEqual("second", File.ReadAllText(storage.FilePath));
            Assert.AreEqual("first", File.ReadAllText(storage.BackupPath));
            Assert.IsFalse(File.Exists(storage.TempPath));

            Assert.IsTrue(storage.TryWrite("third", out _));
            Assert.AreEqual("second", File.ReadAllText(storage.BackupPath), "백업은 직전 세대 하나");
        }

        [Test]
        public void Write_Utf8WithoutBom_KoreanRoundTrip()
        {
            Assert.IsTrue(storage.TryWrite("{\"name\":\"붕대\"}", out _));
            byte[] bytes = File.ReadAllBytes(storage.FilePath);
            Assert.AreEqual((byte)'{', bytes[0], "BOM 없음");
            storage.TryRead(out string text, out _);
            Assert.AreEqual("{\"name\":\"붕대\"}", text);
        }

        [Test]
        public void LeftoverTemp_IgnoredOnRead_OverwrittenOnWrite()
        {
            Assert.IsTrue(storage.TryWrite("good", out _));
            File.WriteAllText(storage.TempPath, "half-written garbage");

            Assert.AreEqual(SaveReadStatus.Ok, storage.TryRead(out string text, out _));
            Assert.AreEqual("good", text);

            Assert.IsTrue(storage.TryWrite("next", out string error), error);
            Assert.AreEqual("next", File.ReadAllText(storage.FilePath));
            Assert.IsFalse(File.Exists(storage.TempPath));
        }

        // M5 리뷰 9: 대체 경로 Delete ~ Move 사이 종료 → save.json 없고 .tmp만 → .tmp를 읽는다(.bak은 읽지 않음)
        [Test]
        public void MissingSave_LeftoverTemp_ReadsTemp_NotBackup()
        {
            Assert.IsTrue(storage.TryWrite("old", out _));
            Assert.IsTrue(storage.TryWrite("previous", out _));   // .bak = old
            File.WriteAllText(storage.TempPath, "latest");
            File.Delete(storage.FilePath);

            Assert.AreEqual(SaveReadStatus.Ok, storage.TryRead(out string text, out _));
            Assert.AreEqual("latest", text);
            Assert.IsTrue(storage.LastReadFromTemp);

            // 다음 쓰기가 정상 경로로 save.json을 만든다
            Assert.IsTrue(storage.TryWrite("written", out string error), error);
            Assert.AreEqual("written", File.ReadAllText(storage.FilePath));
            Assert.IsFalse(File.Exists(storage.TempPath));
            Assert.AreEqual(SaveReadStatus.Ok, storage.TryRead(out text, out _));
            Assert.IsFalse(storage.LastReadFromTemp);
        }

        [Test]
        public void MissingSave_OnlyBackup_StillMissing()
        {
            Assert.IsTrue(storage.TryWrite("old", out _));
            Assert.IsTrue(storage.TryWrite("previous", out _));
            File.Delete(storage.FilePath);
            Assert.AreEqual(SaveReadStatus.Missing, storage.TryRead(out _, out _), ".bak 자동 복구 없음 (Q22)");
        }

        [Test]
        public void Quarantine_TempOnly_MovesTemp()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(storage.TempPath, "half");
            Assert.IsTrue(storage.TryQuarantine(out string movedTo, out string error), error);
            Assert.AreEqual(storage.CorruptPath, movedTo);
            Assert.IsFalse(File.Exists(storage.TempPath));
            Assert.AreEqual("half", File.ReadAllText(storage.CorruptPath));
        }

        // M5 리뷰 1: 다른 프로세스가 잠근 파일 → 예외 없이 Error (Missing·Ok 아님)
        [Test]
        public void LockedFile_ReadError()
        {
            Assert.IsTrue(storage.TryWrite("good", out _));
            using (new FileStream(storage.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.AreEqual(SaveReadStatus.Error, storage.TryRead(out string text, out string error));
                Assert.IsNull(text);
                Assert.IsFalse(string.IsNullOrEmpty(error));
            }
            Assert.AreEqual(SaveReadStatus.Ok, storage.TryRead(out string after, out _));
            Assert.AreEqual("good", after);
        }

        // M5 리뷰 1 [추정] 경로: 진입 때 잠겨 있던 save.json — 격리·W1 덮어쓰기 없이 원본·.bak 그대로, 손상 파일 없음
        [Test]
        public void ProfileService_LockedSave_ReadOnly_FilesUntouched()
        {
            string json = "{\"version\": 1, \"casings\": 321}";
            Assert.IsTrue(storage.TryWrite("{\"version\": 1, \"casings\": 1}", out _));
            Assert.IsTrue(storage.TryWrite(json, out _));
            var profiles = new ProfileService(storage, () => new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc), _ => { });

            using (new FileStream(storage.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                    new System.Text.RegularExpressions.Regex("세이브를 읽지 못했다"));
                SaveLoadReport report = profiles.EnsureLoaded(FakeCatalog.Standard(), M5TestData.Defaults());
                Assert.AreEqual(SaveLoadStatus.ReadError, report.Status);
                Assert.IsFalse(profiles.Save());
            }

            Assert.IsFalse(profiles.Save(), "잠금이 풀려도 이 실행은 저장하지 않는다");
            Assert.AreEqual(json, File.ReadAllText(storage.FilePath));
            Assert.AreEqual("{\"version\": 1, \"casings\": 1}", File.ReadAllText(storage.BackupPath));
            Assert.IsFalse(File.Exists(storage.CorruptPath));
            Assert.IsFalse(File.Exists(storage.TempPath));
        }

        [Test]
        public void Quarantine_MovesToCorruptPath()
        {
            Assert.IsTrue(storage.TryWrite("garbage", out _));

            Assert.IsTrue(storage.TryQuarantine(out string movedTo, out string error), error);

            Assert.AreEqual(storage.CorruptPath, movedTo);
            Assert.IsFalse(File.Exists(storage.FilePath));
            Assert.AreEqual("garbage", File.ReadAllText(storage.CorruptPath));
            Assert.AreEqual(SaveReadStatus.Missing, storage.TryRead(out _, out _));
        }

        [Test]
        public void Quarantine_OverwritesPreviousCorrupt()
        {
            Assert.IsTrue(storage.TryWrite("old", out _));
            Assert.IsTrue(storage.TryQuarantine(out _, out _));
            Assert.IsTrue(storage.TryWrite("new", out _));

            Assert.IsTrue(storage.TryQuarantine(out _, out string error), error);
            Assert.AreEqual("new", File.ReadAllText(storage.CorruptPath));
        }

        [Test]
        public void Quarantine_NoFile_False()
        {
            Assert.IsFalse(storage.TryQuarantine(out string movedTo, out string error));
            Assert.IsNull(movedTo);
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        // 폴더 자리에 파일이 있으면 폴더를 만들 수 없다 → 예외 없이 false + 오류 문자열
        [Test]
        public void Write_Failure_ReturnsFalseWithoutException()
        {
            Directory.CreateDirectory(directory);
            string blocker = Path.Combine(directory, "blocker");
            File.WriteAllText(blocker, "not a directory");
            var blocked = new FileSaveStorage(Path.Combine(blocker, "sub"));

            bool ok = true;
            string error = null;
            Assert.DoesNotThrow(() => ok = blocked.TryWrite("x", out error));
            Assert.IsFalse(ok);
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        // 저장 대상이 폴더(쓸 수 없는 경로) → 예외 없이 읽기 Error 또는 Missing, 쓰기 false
        [Test]
        public void TargetIsDirectory_WriteFails()
        {
            Directory.CreateDirectory(Path.Combine(directory, "save.json"));

            Assert.IsFalse(storage.TryWrite("x", out string error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
            Assert.DoesNotThrow(() => storage.TryRead(out _, out _));
        }
    }
}
