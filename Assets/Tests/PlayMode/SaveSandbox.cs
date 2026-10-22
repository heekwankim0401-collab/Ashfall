#if UNITY_EDITOR
using System;
using System.IO;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.PlayMode
{
    // PlayMode 세이브 샌드박스 (M5 설계 §2.6, §13.3): 이 네임스페이스의 모든 PlayMode 테스트 전에 한 번,
    // GameManager의 세이브 저장소를 실제 Application.persistentDataPath 대신 OS 임시 폴더로 바꾼다.
    // - 실행 폴더: <OS temp>/AshfallPlayModeSaves/run_<guid>/. 끝나면 지운다.
    // - 은신처를 쓰는 테스트는 테스트마다 UseFreshStorage()로 새 하위 폴더를 쓴다(격리 + 메모리 프로필 버림 = "다음 실행" 흉내).
    // PlayMode 실행은 전체가 Play 모드 안이라 GameManager(BeforeSceneLoad 자동 생성)가 이미 있다.
    // GameManager.Awake는 파일을 읽지 않으므로(경로만) 첫 은신처 진입 전에 바꾸면 실제 경로는 한 번도 쓰이지 않는다.
    [SetUpFixture]
    public sealed class SaveSandbox
    {
        public const string RootFolderName = "AshfallPlayModeSaves";

        // 이번 실행의 샌드박스 폴더 (없으면 null — SetUpFixture 밖)
        public static string RunDirectory { get; private set; }
        // 지금 GameManager에 넣은 저장소
        public static FileSaveStorage Current { get; private set; }

        [OneTimeSetUp]
        public void Begin()
        {
            RunDirectory = Path.Combine(Path.GetTempPath(), RootFolderName, "run_" + Guid.NewGuid().ToString("N"));
            UseFreshStorage("default");
        }

        [OneTimeTearDown]
        public void End()
        {
            // Play 모드를 나갈 때 은신처가 열려 있으면 W3(OnApplicationQuit)가 이 폴더에 다시 쓸 수 있다 — 실제 경로가 아니라 무해.
            string dir = RunDirectory;
            RunDirectory = null;
            Current = null;
            if (string.IsNullOrEmpty(dir))
                return;
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSandbox] 임시 세이브 폴더를 지우지 못했다: {dir} — {e.Message}");
            }
        }

        // 새 하위 폴더 저장소로 바꾼다. GameManager의 메모리 프로필을 버린다(다음 EnsureLoaded가 새 위치에서 읽음).
        public static FileSaveStorage UseFreshStorage(string label = "test")
        {
            if (string.IsNullOrEmpty(RunDirectory))
                RunDirectory = Path.Combine(Path.GetTempPath(), RootFolderName, "run_" + Guid.NewGuid().ToString("N"));
            GameManager gm = GameManager.Instance;
            Assert.IsNotNull(gm, "[SaveSandbox] GameManager가 없다 — 세이브 저장소를 바꿀 수 없다");
            var storage = new FileSaveStorage(Path.Combine(RunDirectory, label + "_" + Guid.NewGuid().ToString("N")));
            gm.UseSaveStorage(storage);
            Current = storage;
            return storage;
        }

        // 경로가 실제 세이브 위치(persistentDataPath) 아래인가
        public static bool IsUnderPersistentDataPath(string path)
        {
            string real = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(path);
            return full.Equals(real, StringComparison.OrdinalIgnoreCase) ||
                   full.StartsWith(real + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   full.StartsWith(real + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
