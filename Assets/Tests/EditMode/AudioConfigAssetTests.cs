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
    // 아무 파일도 바뀌지 않음). 사용자 교체 보존 테스트는 임시 사본으로 한다.
    // 위험(M7 리뷰 7A 낮음 3): CreateAssets()의 레시피는 M6 + M7(AllRecipes)이라 M7 에셋이 없는 작업 트리에서 돌리면 테스트가 커밋 대상 WAV·
    // AudioConfig를 새로 만든다(7A-3에서 실제 발생). 그래서 두 재실행 테스트는 AssumeRerunWritesNothing()으로 "쓸 것이 없을 때만" 돌린다
    // (아니면 Inconclusive — 에셋 존재·연결은 다른 테스트가 실패로 알린다). 멱등이 깨지면 여전히 에셋을 고친 채 실패하니 테스트 뒤 git status를 본다.
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

        // 재실행 테스트 전 확인(M7 리뷰 7A 낮음 3): CreateAssets()가 새 WAV를 쓰거나 AudioConfig 빈 필드를 채울 상황이면 건너뛴다.
        // 메뉴의 레시피는 M6 + M7(AllRecipes)이라, M7 에셋이 아직 없는 작업 트리에서 M6 재실행 테스트가 M7 WAV·세트를 만들던 문제(7A-3에서 실제 발생)를 막는다.
        // AudioConfig 판정은 메모리 사본에 FillAudioConfig를 돌려 0인지 본다(커밋된 에셋은 건드리지 않는다).
        private static void AssumeRerunWritesNothing()
        {
            AudioConfigSO config = LoadConfig();
            string[] missing = M6AudioSetup.AllRecipes().Select(r => r.path).Where(p => !File.Exists(Path.GetFullPath(p))).ToArray();
            Assume.That(missing, Is.Empty, $"자리표시 WAV가 없어 재실행이 새 파일을 쓴다 — {MenuHint}");
            var copy = Object.Instantiate(config);
            try
            {
                Assume.That(M6AudioSetup.FillAudioConfig(copy), Is.EqualTo(0), $"AudioConfig에 빈 필드·세트가 있어 재실행이 에셋을 고친다 — {MenuHint}");
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

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
            AssumeRerunWritesNothing();
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

        // ---------- M7 7A-2c: 산탄·저격 총성, 무기 클릭 (M7 설계 §2.2·§4.9) ----------

        private static string[] M7WavPaths() => M6AudioSetup.M7Recipes().Select(r => r.path).ToArray();

        [Test]
        public void M7Recipes_8Files_UniquePaths_DistinctFromM6()
        {
            string[] m7 = M7WavPaths();
            Assert.AreEqual(3 * 3 + 2 + 2, m7.Length, "산탄·저격·경기관총(7B-2c) 근/중/원 + 헛격발·고장 해제 + 변이체 울음·공격(7B-2c)");
            Assert.AreEqual(m7.Length, m7.Distinct().Count());
            Assert.IsEmpty(m7.Intersect(AllWavPaths()), "M6 경로와 겹치지 않는다");
            Assert.IsTrue(m7.All(p => p.StartsWith(M6Names.PlaceholderAudioFolder + "/") && p.EndsWith(".wav")));
            CollectionAssert.Contains(m7, M6Names.GunshotAudioFolder + "/GS_Shotgun_Near.wav");
            CollectionAssert.Contains(m7, M6Names.GunshotAudioFolder + "/GS_Sniper_Far.wav");
            CollectionAssert.Contains(m7, M7Names.DryFireClipPath);
            CollectionAssert.Contains(m7, M7Names.JamClearClipPath);
            CollectionAssert.AreEqual(AllWavPaths().Concat(m7).ToArray(), M6AudioSetup.AllRecipes().Select(r => r.path).ToArray(),
                "메뉴 레시피 = M6 + M7");
            CollectionAssert.AreEqual(M6AudioSetup.GunshotClasses.Concat(M6AudioSetup.M7GunshotClasses).ToArray(),
                M6AudioSetup.AllGunshotClasses());
            // 7B 확장 이름 규칙(데이터만으로 MachineGun 세트 추가 — 파일 이름만 확인)
            Assert.AreEqual(M6Names.GunshotAudioFolder + "/GS_Lmg_Mid.wav", M6AudioSetup.GunshotPath(WeaponClass.MachineGun, GunshotLayer.Mid));
        }

        [Test]
        public void Config_FiveGunshotSets_M6OrderKept_NearMidFar()
        {
            AudioConfigSO c = LoadConfig();
            CollectionAssert.AreEqual(M6AudioSetup.GunshotClasses,
                c.gunshots.Take(M6AudioSetup.GunshotClasses.Length).Select(g => g.weaponClass).ToArray(), "M6 세트 순서 유지(앞부분)");
            foreach (WeaponClass cls in M6AudioSetup.AllGunshotClasses())
            {
                GunshotSet set = GunshotMix.FindSet(c, cls);
                Assert.IsNotNull(set, $"{cls}");
                Assert.AreEqual(1, c.gunshots.Count(g => g != null && g.weaponClass == cls), $"{cls} 세트 하나");
                Assert.IsNotNull(set.near, $"{cls} near");
                Assert.IsNotNull(set.mid, $"{cls} mid");
                Assert.IsNotNull(set.far, $"{cls} far");
                Assert.Less(set.near.length, set.far.length, $"{cls}: 원거리(메아리) 클립이 더 길다");
            }
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.GunshotPath(WeaponClass.Shotgun, GunshotLayer.Near)),
                GunshotMix.FindSet(c, WeaponClass.Shotgun).near);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.GunshotPath(WeaponClass.Sniper, GunshotLayer.Far)),
                GunshotMix.FindSet(c, WeaponClass.Sniper).far);
        }

        [Test]
        public void Config_WeaponClickClips_Assigned_Short()
        {
            AudioConfigSO c = LoadConfig();
            Assert.IsNotNull(c.dryFireClip, $"dryFireClip. {MenuHint}");
            Assert.IsNotNull(c.jamClearClip, $"jamClearClip. {MenuHint}");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M7Names.DryFireClipPath), c.dryFireClip);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M7Names.JamClearClipPath), c.jamClearClip);
            Assert.Less(c.dryFireClip.length, c.jamClearClip.length, "헛격발 클릭이 해제보다 짧다");
            Assert.Less(c.jamClearClip.length, M6AudioSetup.ShortClipSec);
            Assert.That(c.dryFireVolume, Is.InRange(0f, 1f));
            Assert.That(c.jamClearVolume, Is.InRange(0f, 1f));
        }

        [Test]
        public void M7Wavs_Exist_ImportedMono_LoadTypeBySize()
        {
            foreach (string path in M7WavPaths())
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.IsNotNull(clip, $"WAV 없음: {path}. {MenuHint}");
                Assert.AreEqual(1, clip.channels, $"{path} 모노");
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                Assert.IsTrue(importer.forceToMono, $"{path} forceToMono");
                AudioImporterSampleSettings s = importer.defaultSampleSettings;
                Assert.IsTrue(s.preloadAudioData, $"{path} preload");
                Assert.AreEqual(AudioCompressionFormat.Vorbis, s.compressionFormat, path);
                AudioClipLoadType expected = clip.length >= M6AudioSetup.ShortClipSec
                    ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                Assert.AreEqual(expected, s.loadType, $"{path} ({clip.length:0.00}s)");
            }
        }

        // 주의: Rerun_KeepsWavBytes_Guids_AndConfigBytes와 같이 실제 에셋에 CreateAssets()를 다시 돌린다(멱등이면 바뀌지 않음, 쓸 것이 있으면 건너뜀 — 머리 주석).
        [Test]
        public void Rerun_KeepsM7WavBytes_Guids_AndConfigBytes()
        {
            AssumeRerunWritesNothing();
            string[] files = M7WavPaths().Concat(new[] { M6Names.AudioConfigPath }).ToArray();
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

        // M6 시절 AudioConfig(세트 3개, 클릭 필드 없음)에 메뉴를 돌리면: 산탄·저격 세트를 끝에 추가(기존 세트·사용자 클립 보존), 빈 클릭만 채움.
        // 메모리 인스턴스로 확인한다(커밋된 AudioConfig.asset을 고치지 않는다).
        [Test]
        public void Fill_M6Config_AppendsM7Sets_AtEnd_PreservesUserClips()
        {
            var userClip = AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.FootstepPath(SurfaceKind.Metal, 0));
            Assert.IsNotNull(userClip, MenuHint);
            var c = ScriptableObject.CreateInstance<AudioConfigSO>();
            try
            {
                var rifle = new GunshotSet(WeaponClass.AssaultRifle, 0.5f) { near = userClip };
                c.gunshots = new[] { new GunshotSet(WeaponClass.Pistol, 1f), new GunshotSet(WeaponClass.Smg, 1f), rifle };
                c.dryFireClip = null;
                c.jamClearClip = userClip;   // 사용자가 넣은 해제 소리

                Assert.Greater(M6AudioSetup.FillAudioConfig(c), 0);

                CollectionAssert.AreEqual(
                    new[] { WeaponClass.Pistol, WeaponClass.Smg, WeaponClass.AssaultRifle, WeaponClass.Shotgun, WeaponClass.Sniper, WeaponClass.MachineGun },   // 7B-2c: MachineGun 이어 붙임
                    c.gunshots.Select(g => g.weaponClass).ToArray(), "M7 세트는 끝에 추가");
                Assert.AreSame(rifle, c.gunshots[2], "기존 세트 객체 유지");
                Assert.AreSame(userClip, rifle.near, "사용자 총성 클립 보존");
                Assert.AreEqual(0.5f, rifle.volume, 1e-6f);
                foreach (WeaponClass cls in M6AudioSetup.M7GunshotClasses)
                {
                    GunshotSet set = GunshotMix.FindSet(c, cls);
                    Assert.AreEqual(1f, set.volume, 1e-6f);
                    Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.GunshotPath(cls, GunshotLayer.Near)), set.near, $"{cls} near");
                    Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.GunshotPath(cls, GunshotLayer.Mid)), set.mid, $"{cls} mid");
                    Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.GunshotPath(cls, GunshotLayer.Far)), set.far, $"{cls} far");
                }
                Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M7Names.DryFireClipPath), c.dryFireClip, "빈 헛격발 채움");
                Assert.AreSame(userClip, c.jamClearClip, "사용자 해제 클립 보존");

                Assert.AreEqual(0, M6AudioSetup.FillAudioConfig(c), "두 번째는 채울 것 없음");
                Assert.AreEqual(6, c.gunshots.Length, "두 번째 실행은 세트를 더 붙이지 않는다");   // 7B-2c: 5 → 6 (MachineGun)
            }
            finally
            {
                Object.DestroyImmediate(c);
            }
        }

        // ---------- M7 7B-2c: 경기관총 총성 세트, 변이체 울음·공격 (M7 설계 §2.2·§4.9, Q26·Q27) ----------

        [Test]
        public void Config_MachineGunSet_UsesLmgPlaceholders()
        {
            AudioConfigSO c = LoadConfig();
            GunshotSet set = GunshotMix.FindSet(c, WeaponClass.MachineGun);
            Assert.IsNotNull(set, $"MachineGun 세트 없음. {MenuHint}");
            Assert.AreEqual(WeaponClass.MachineGun, c.gunshots.Last().weaponClass, "M7 세트 끝에 추가(산탄·저격 다음)");
            foreach (GunshotLayer layer in M6AudioSetup.GunshotLayers)
                StringAssert.Contains("/GS_Lmg_" + layer, M6AudioSetup.GunshotPath(WeaponClass.MachineGun, layer));
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.GunshotPath(WeaponClass.MachineGun, GunshotLayer.Near)), set.near);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.GunshotPath(WeaponClass.MachineGun, GunshotLayer.Mid)), set.mid);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.GunshotPath(WeaponClass.MachineGun, GunshotLayer.Far)), set.far);
            var rifle = GunshotMix.FindSet(c, WeaponClass.AssaultRifle);
            Assert.Less(set.near.length, rifle.near.length, "경기관총 근거리 = 소총보다 짧다(설계 §2.2)");
        }

        [Test]
        public void Config_MutantVocals_Assigned_AlertLongerThanAttack()
        {
            AudioConfigSO c = LoadConfig();
            Assert.IsNotNull(c.mutantAlertClips, MenuHint);
            Assert.IsNotNull(c.mutantAttackClips, MenuHint);
            Assert.GreaterOrEqual(c.mutantAlertClips.Length, 1, $"울음 클립 없음. {MenuHint}");
            Assert.GreaterOrEqual(c.mutantAttackClips.Length, 1, $"공격 클립 없음. {MenuHint}");
            Assert.IsTrue(c.mutantAlertClips.All(x => x != null), "빈 울음 클립");
            Assert.IsTrue(c.mutantAttackClips.All(x => x != null), "빈 공격 클립");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M7Names.MutantAlertClipPath), c.mutantAlertClips[0]);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M7Names.MutantAttackClipPath), c.mutantAttackClips[0]);
            Assert.Greater(c.mutantAlertClips[0].length, c.mutantAttackClips[0].length, "울음(1.2초) > 공격(0.5초)");
            Assert.Greater(c.vocalAudibleM, 0f);
            Assert.That(c.mutantAlertVolume, Is.InRange(0f, 1f));
            Assert.That(c.mutantAttackVolume, Is.InRange(0f, 1f));
        }

        [Test]
        public void M7bWavs_PathsUnderPlaceholderEnemy_InRecipes()
        {
            string[] m7 = M7WavPaths();
            CollectionAssert.Contains(m7, M7Names.MutantAlertClipPath);
            CollectionAssert.Contains(m7, M7Names.MutantAttackClipPath);
            CollectionAssert.Contains(m7, M6Names.GunshotAudioFolder + "/GS_Lmg_Near.wav");
            CollectionAssert.Contains(m7, M6Names.GunshotAudioFolder + "/GS_Lmg_Far.wav");
            StringAssert.StartsWith(M6Names.PlaceholderAudioFolder + "/", M7Names.MutantAlertClipPath);
            StringAssert.StartsWith(M6Names.PlaceholderAudioFolder + "/", M7Names.MutantAttackClipPath);
        }

        // 빈 울음 배열만 채운다: 사용자가 넣은 공격 클립(여러 개)은 그대로, 빈 칸만 있는 배열은 자리표시로. 두 번째는 0.
        [Test]
        public void Fill_MutantVocals_OnlyEmptyArrays_PreservesUserClips()
        {
            var userClip = AssetDatabase.LoadAssetAtPath<AudioClip>(M6AudioSetup.FootstepPath(SurfaceKind.Dirt, 0));
            Assert.IsNotNull(userClip, MenuHint);
            var c = ScriptableObject.CreateInstance<AudioConfigSO>();
            try
            {
                c.mutantAlertClips = new AudioClip[] { null, null };
                c.mutantAttackClips = new[] { userClip, userClip };
                M6AudioSetup.FillAudioConfig(c);
                Assert.AreEqual(1, c.mutantAlertClips.Length);
                Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(M7Names.MutantAlertClipPath), c.mutantAlertClips[0], "빈 칸만 있는 배열 → 자리표시");
                Assert.AreEqual(2, c.mutantAttackClips.Length, "사용자 배열 보존");
                Assert.IsTrue(c.mutantAttackClips.All(x => x == userClip));
                Assert.AreEqual(0, M6AudioSetup.FillAudioConfig(c), "두 번째는 채울 것 없음");
                Assert.AreEqual(1, c.gunshots.Count(g => g.weaponClass == WeaponClass.MachineGun), "MachineGun 세트 하나");
            }
            finally
            {
                Object.DestroyImmediate(c);
            }
        }
    }
}
