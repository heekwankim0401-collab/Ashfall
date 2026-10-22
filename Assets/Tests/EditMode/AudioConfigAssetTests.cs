using System.IO;
using System.Linq;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 2b: 자리표시 오디오 에셋 + AudioConfig 클립 연결 (설계 §2.1·§2.2, Q18·Q21).
    // 에셋은 Ashfall/Setup/M6/Create Audio Assets(M6AudioSetup.CreateAssets — 3단계 Apply M6 Art & Audio가 부른다)가 만든다. 없으면 실패한다.
    // 주의(M6 리뷰 낮음 5): Rerun_KeepsWavBytes_Guids_AndConfigBytes는 실제 자리표시 에셋에 CreateAssets()를 다시 돌린다(멱등이라 정상이면
    // 아무 파일도 바뀌지 않음 — AudioConfig가 없으면 M6 데이터 메뉴가 테스트 중 에셋을 만든다). 사용자 교체 보존 테스트는 임시 사본으로 한다.
    public class AudioConfigAssetTests
    {
        private const string MenuHint = "메뉴 Ashfall/Setup/M6/Create Audio Assets(또는 Apply M6 Art & Audio)를 실행할 것.";

        private static AudioConfigSO LoadConfig()
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioConfigSO>(M6Names.AudioConfigPath);
            Assert.IsNotNull(c, $"AudioConfig 없음: {M6Names.AudioConfigPath}. {MenuHint}");
            return c;
        }

        private static string[] AllWavPaths() => M6AudioSetup.Recipes().Select(r => r.path).ToArray();

        [Test]
        public void Recipes_27Files_UniquePaths_UnderPlaceholderFolder()
        {
            string[] paths = AllWavPaths();
            Assert.AreEqual(4 * 3 + 3 * 3 + 2 + 2 + 1 + 1, paths.Length);
            Assert.AreEqual(paths.Length, paths.Distinct().Count());
            Assert.IsTrue(paths.All(p => p.StartsWith(M6Names.PlaceholderAudioFolder + "/") && p.EndsWith(".wav")));
            Assert.AreEqual(M6Names.FootstepAudioFolder + "/FS_Water_02.wav", M6AudioSetup.FootstepPath(SurfaceKind.Water, 1));
            Assert.AreEqual(M6Names.GunshotAudioFolder + "/GS_Rifle_Far.wav", M6AudioSetup.GunshotPath(WeaponClass.AssaultRifle, GunshotLayer.Far));
        }

        [Test]
        public void Config_AllSurfaces_HaveClips()
        {
            AudioConfigSO c = LoadConfig();
            foreach (SurfaceKind k in M6AudioSetup.Surfaces)
            {
                SurfaceFootsteps set = FootstepMix.FindSurface(c, k);
                Assert.IsNotNull(set, $"{k} 세트");
                Assert.GreaterOrEqual(set.clips.Length, 1, $"{k} 클립");
                Assert.IsTrue(set.clips.All(x => x != null), $"{k} 빈 클립");
            }
        }

        [Test]
        public void Config_GunshotSets_NearMidFar()
        {
            AudioConfigSO c = LoadConfig();
            foreach (WeaponClass cls in M6AudioSetup.GunshotClasses)
            {
                GunshotSet set = GunshotMix.FindSet(c, cls);
                Assert.IsNotNull(set, $"{cls}");
                Assert.IsNotNull(set.near, $"{cls} near");
                Assert.IsNotNull(set.mid, $"{cls} mid");
                Assert.IsNotNull(set.far, $"{cls} far");
                Assert.Less(set.near.length, set.far.length, $"{cls}: 원거리(메아리) 클립이 더 길다");
            }
        }

        [Test]
        public void Config_Ambience_And_HideoutMusic()
        {
            AudioConfigSO c = LoadConfig();
            Assert.IsNotNull(c.windLoop);
            Assert.IsNotNull(c.stormLoop);
            Assert.IsNotNull(c.geigerClick);
            Assert.IsNotNull(c.hideoutMusic, "Q18 은신처 BGM 자리표시");
            Assert.GreaterOrEqual(c.collapseClips.Length, 1);
            Assert.IsTrue(c.collapseClips.All(x => x != null));
            Assert.GreaterOrEqual(c.windLoop.length, 9.9f);
            Assert.GreaterOrEqual(c.stormLoop.length, 9.9f);
            Assert.GreaterOrEqual(c.hideoutMusic.length, 29.9f);
            Assert.Less(c.geigerClick.length, 0.05f);
        }

        [Test]
        public void Wavs_Exist_ImportedMono_LoadTypeBySize()
        {
            foreach (string path in AllWavPaths())
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.IsNotNull(clip, $"WAV 없음: {path}. {MenuHint}");
                Assert.AreEqual(1, clip.channels, $"{path} 모노");
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                Assert.IsTrue(importer.forceToMono, $"{path} forceToMono");
                AudioImporterSampleSettings s = importer.defaultSampleSettings;
                Assert.IsTrue(s.preloadAudioData, $"{path} preload");
                Assert.AreEqual(AudioCompressionFormat.Vorbis, s.compressionFormat, path);
                bool loop = M6AudioSetup.LoopPaths.Contains(path);
                AudioClipLoadType expected = loop || clip.length >= M6AudioSetup.ShortClipSec
                    ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                Assert.AreEqual(expected, s.loadType, $"{path} ({clip.length:0.00}s)");
            }
        }

        [Test]
        public void Rerun_KeepsWavBytes_Guids_AndConfigBytes()
        {
            LoadConfig();
            string[] files = AllWavPaths().Concat(new[] { M6Names.AudioConfigPath }).ToArray();
            var bytes = files.ToDictionary(p => p, p => File.ReadAllBytes(Path.GetFullPath(p)));
            var metas = files.ToDictionary(p => p, p => File.ReadAllBytes(Path.GetFullPath(p + ".meta")));
            var guids = files.ToDictionary(p => p, AssetDatabase.AssetPathToGUID);

            M6AudioSetup.CreateAssets();

            foreach (string p in files)
            {
                CollectionAssert.AreEqual(bytes[p], File.ReadAllBytes(Path.GetFullPath(p)), $"재실행으로 바뀜: {p}");
                CollectionAssert.AreEqual(metas[p], File.ReadAllBytes(Path.GetFullPath(p + ".meta")), $".meta 바뀜: {p}");
                Assert.AreEqual(guids[p], AssetDatabase.AssetPathToGUID(p), $"GUID 바뀜: {p}");
            }
        }

        // 사용자가 넣은 클립은 덮지 않고, 빈 필드만 채운다(Q21). 메모리 인스턴스로 확인한다 — 커밋된 AudioConfig.asset을 고치지 않는다(리뷰 낮음 5).
        [Test]
        public void Fill_PreservesUserClipAssignments_FillsOnlyEmpty()
        {
            var userClip = AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.FootstepPath(SurfaceKind.Metal, 0));
            Assert.IsNotNull(userClip, MenuHint);
            var c = ScriptableObject.CreateInstance<AudioConfigSO>();
            try
            {
                SurfaceFootsteps dirt = FootstepMix.FindSurface(c, SurfaceKind.Dirt);
                GunshotSet pistol = GunshotMix.FindSet(c, WeaponClass.Pistol);
                // 사용자가 다른 클립을 넣었다고 친다
                dirt.clips = new[] { userClip };
                pistol.near = userClip;
                c.windLoop = userClip;

                int filled = M6AudioSetup.FillAudioConfig(c);

                Assert.Greater(filled, 0, "빈 필드는 채운다");
                Assert.AreEqual(1, dirt.clips.Length);
                Assert.AreSame(userClip, dirt.clips[0], "사용자 발소리 배열 보존");
                Assert.AreSame(userClip, pistol.near, "사용자 총성 클립 보존");
                Assert.AreSame(userClip, c.windLoop, "사용자 바람 클립 보존");
                Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.GunshotPath(WeaponClass.Pistol, GunshotLayer.Mid)), pistol.mid, "빈 중거리 채움");
                Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.StormLoopPath), c.stormLoop, "빈 폭풍 루프 채움");
                Assert.AreEqual(0, M6AudioSetup.FillAudioConfig(c), "두 번째는 채울 것 없음");

                // 비워 두면 다시 채운다
                c.windLoop = null;
                Assert.AreEqual(1, M6AudioSetup.FillAudioConfig(c));
                Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.WindLoopPath), c.windLoop);
            }
            finally
            {
                Object.DestroyImmediate(c);
            }
        }

        // 같은 경로에 덮어쓴 사용자 WAV와 그 임포트 설정(예: 스테레오 = forceToMono 끔)을 다음 메뉴 실행이 되돌리지 않는다(Q21, 리뷰 낮음 3).
        // 커밋된 자리표시 WAV 대신 임시 폴더의 레시피 사본으로 확인한다(리뷰 낮음 5). 테스트 도중 에디터가 죽으면 임시 폴더가
        // 남을 수 있다(추적되지 않는 새 폴더 — 지우면 된다). 커밋된 파일은 건드리지 않는다.
        [Test]
        public void Rerun_PreservesUserWavFile_AndImportSettings_TempCopy()
        {
            const string folder = "Assets/__M6AudioSetupTestTemp";
            string path = folder + "/T_Loop.wav";
            var recipes = new (string path, System.Func<float[]> synth)[]
            {
                (path, () => SfxSynth.Footstep(SurfaceKind.Dirt, 0, SfxSynth.DefaultRate)),
            };
            Assert.IsFalse(AssetDatabase.IsValidFolder(folder), $"이전 실행의 임시 폴더가 남아 있다: {folder} — 지우고 다시 실행");
            try
            {
                // 처음: 새로 쓰고 자리표시 임포트 설정(모노)
                var created = M6AudioSetup.WritePlaceholderWavs(recipes);
                CollectionAssert.AreEqual(new[] { path }, created);
                M6AudioSetup.ApplyImportSettings(created);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                Assert.IsTrue(importer.forceToMono, "새로 쓴 파일은 자리표시 설정");
                string guid = AssetDatabase.AssetPathToGUID(path);

                // 사용자가 다른 WAV로 덮어쓰고 모노 강제를 끈다(스테레오 루프 교체 흉내)
                string full = Path.GetFullPath(path);
                byte[] user = WavWriter.EncodePcm16(SfxSynth.Footstep(SurfaceKind.Metal, 2), SfxSynth.DefaultRate);
                Assert.AreNotEqual(File.ReadAllBytes(full).Length, user.Length);
                File.WriteAllBytes(full, user);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = (AudioImporter)AssetImporter.GetAtPath(path);
                importer.forceToMono = false;
                importer.SaveAndReimport();

                // 다시 실행(CreateAssets와 같은 순서): 새로 쓴 것 없음 → 임포트 설정도 건드리지 않음
                created = M6AudioSetup.WritePlaceholderWavs(recipes);
                Assert.AreEqual(0, created.Count, "있는 파일은 쓰지 않는다");
                Assert.AreEqual(0, M6AudioSetup.ApplyImportSettings(created));
                CollectionAssert.AreEqual(user, File.ReadAllBytes(full), "사용자 WAV를 덮어쓰지 않는다(Q21)");
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path), "GUID 유지");
                importer = (AudioImporter)AssetImporter.GetAtPath(path);
                Assert.IsFalse(importer.forceToMono, "사용자 임포트 설정 보존(리뷰 낮음 3)");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
