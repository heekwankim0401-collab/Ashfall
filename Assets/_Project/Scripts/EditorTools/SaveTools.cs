using System.Collections.Generic;
using System.IO;
using Ashfall.Core;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // 세이브 파일 도구 (M5 설계 §2.5·§2.6, §4.6 "SaveTools", Q20 — 메인 메뉴에 [새로 시작]이 없어 초기화는 이 메뉴로).
    // 게임과 같은 위치: Application.persistentDataPath/save.json (+ .bak, .tmp, save.corrupt.json).
    // Windows 빌드·에디터 모두 %USERPROFILE%\AppData\LocalLow\DefaultCompany\Ashfall\.
    // Play 중에는 비활성(실행 중인 ProfileService가 메모리 프로필을 다시 쓰므로 지워도 의미가 없다).
    public static class SaveTools
    {
        private const string Tag = "[SaveTools]";
        private const string OpenMenu = "Ashfall/Save/Open Save Folder";
        private const string DeleteMenu = "Ashfall/Save/Delete Save Files";

        // 게임 런타임 기본 저장소와 같은 경로 (GameManager.Awake)
        private static FileSaveStorage GameStorage() => new FileSaveStorage(Application.persistentDataPath);

        // 세이브 관련 파일 경로 전부 (지울 대상)
        public static IReadOnlyList<string> SaveFilePaths()
        {
            FileSaveStorage storage = GameStorage();
            return new[] { storage.FilePath, storage.BackupPath, storage.TempPath, storage.CorruptPath };
        }

        [MenuItem(OpenMenu)]
        public static void OpenSaveFolder()
        {
            FileSaveStorage storage = GameStorage();
            // 파일이 있으면 파일을 선택해 보여 주고, 없으면 폴더를 보여 준다(없으면 만든다 — 처음 실행 전에도 열리게)
            if (File.Exists(storage.FilePath))
            {
                EditorUtility.RevealInFinder(storage.FilePath);
                return;
            }
            Directory.CreateDirectory(storage.DirectoryPath);
            EditorUtility.RevealInFinder(storage.DirectoryPath);
        }

        [MenuItem(DeleteMenu)]
        public static void DeleteSaveFiles()
        {
            var existing = new List<string>();
            foreach (string path in SaveFilePaths())
                if (File.Exists(path))
                    existing.Add(path);
            if (existing.Count == 0)
            {
                EditorUtility.DisplayDialog("세이브 삭제", $"지울 세이브 파일이 없다.\n{GameStorage().DirectoryPath}", "확인");
                return;
            }
            if (!EditorUtility.DisplayDialog("세이브 삭제",
                    "다음 파일을 지운다. 되돌릴 수 없다.\n\n" + string.Join("\n", existing), "지우기", "취소"))
                return;

            foreach (string path in existing)
            {
                try
                {
                    File.Delete(path);
                    Debug.Log($"{Tag} 삭제: {path}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"{Tag} 삭제 실패: {path} — {e.Message}");
                }
            }
        }

        [MenuItem(OpenMenu, true)]
        [MenuItem(DeleteMenu, true)]
        private static bool ValidateNotPlaying() => !EditorApplication.isPlaying;
    }
}
