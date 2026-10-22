#if UNITY_EDITOR
using System.IO;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.PlayMode
{
    // 회귀 방지 (M5 설계 §2.6, AC2): PlayMode 테스트가 실제 세이브(persistentDataPath/save.json)를 건드리지 않는다.
    public class SaveSandboxTests
    {
        [Test]
        public void GameManagerStorage_IsNotUnderPersistentDataPath()
        {
            GameManager gm = GameManager.Instance;
            Assert.IsNotNull(gm, "GameManager 없음");
            Assert.IsNotNull(SaveSandbox.RunDirectory, "SaveSandbox(SetUpFixture)가 실행되지 않았다");
            string location = gm.Profiles.StorageLocation;
            Assert.IsFalse(string.IsNullOrEmpty(location), "저장 위치 없음");
            Assert.IsFalse(SaveSandbox.IsUnderPersistentDataPath(location),
                $"PlayMode 저장 위치가 실제 세이브 경로 아래다: {location} (persistentDataPath {Application.persistentDataPath})");
            StringAssert.Contains(SaveSandbox.RootFolderName, location, "샌드박스 폴더가 아니다");
        }

        [Test]
        public void UseFreshStorage_ChangesLocation_AndDropsLoadedProfile()
        {
            GameManager gm = GameManager.Instance;
            string before = gm.Profiles.StorageLocation;
            FileSaveStorage storage = SaveSandbox.UseFreshStorage("sandbox-test");
            Assert.AreNotEqual(before, gm.Profiles.StorageLocation, "새 폴더로 바뀌어야 한다");
            Assert.AreEqual(storage.FilePath, gm.Profiles.StorageLocation);
            Assert.IsFalse(gm.Profiles.IsLoaded, "메모리 프로필을 버린다");
            Assert.IsTrue(Path.GetFullPath(storage.FilePath).StartsWith(Path.GetFullPath(SaveSandbox.RunDirectory)),
                "실행 폴더 아래");
            Assert.IsFalse(SaveSandbox.IsUnderPersistentDataPath(storage.FilePath));
        }

        [Test]
        public void IsUnderPersistentDataPath_DetectsRealSavePath()
        {
            Assert.IsTrue(SaveSandbox.IsUnderPersistentDataPath(new FileSaveStorage(Application.persistentDataPath).FilePath));
            Assert.IsFalse(SaveSandbox.IsUnderPersistentDataPath(Path.Combine(Path.GetTempPath(), "x", "save.json")));
        }
    }
}
#endif
