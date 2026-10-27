using System.Linq;
using Ashfall.Core;
using Ashfall.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 에셋 교체 (docs/design/AssetSwap_design.md §3~§5): 배정표 형식, 순수 맞춤 함수, BlockoutSkin 숨기기.
    // 패키지(git 제외)가 없어도 통과한다 — 원본 파일 존재는 보지 않는다(씬 결과는 PlayMode AssetSwapSceneSmokeTests).
    public class AssetSwapTests
    {
        [Test]
        public void Catalog_IdsUnique_AssignmentsResolve()
        {
            var ids = AssetSwapCatalog.Sources.Select(s => s.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
            foreach (AssetSwapCatalog.Source s in AssetSwapCatalog.Sources)
            {
                StringAssert.StartsWith("Assets/", s.Path, s.Id);
                StringAssert.DoesNotStartWith("Assets/_Project/", s.Path, $"{s.Id}: 원본은 git 제외 패키지 폴더");
            }
            foreach (string id in AssetSwapCatalog.WeaponSkins.Values
                         .Concat(AssetSwapCatalog.EnemySkins.Values.Select(v => v.Body))
                         .Concat(AssetSwapCatalog.EnemySkins.Values.Select(v => v.Gun).Where(g => g != null))
                         .Concat(AssetSwapCatalog.CarSkins).Concat(AssetSwapCatalog.HouseSkins).Concat(AssetSwapCatalog.CrateSkins)
                         .Concat(AssetSwapCatalog.DrumSkins)
                         .Concat(new[] { AssetSwapCatalog.ContainerSkin, AssetSwapCatalog.LowWallSkin, AssetSwapCatalog.HighWallSkin, AssetSwapCatalog.CrashedPlaneSkin }))
                Assert.IsNotNull(AssetSwapCatalog.Find(id), $"배정 {id}이 원본 표에 없다");
            Assert.AreEqual(5, AssetSwapCatalog.WeaponSkins.Count, "무기 5종");
            Assert.AreEqual(6, AssetSwapCatalog.EnemySkins.Count, "적 6종");
            Assert.IsNull(AssetSwapCatalog.EnemySkins["AI-MUT"].Gun, "변이체는 무장 없음");
            Assert.IsTrue(AssetSwapCatalog.Sources.Where(s => AssetSwapCatalog.WeaponSkins.Values.Contains(s.Id))
                .All(s => s.Anchor == AssetSwapCatalog.Anchor.FrontTip), "무기 앵커 = 총구");
            StringAssert.StartsWith(AssetSwapCatalog.SkinFolder + "/SK_", AssetSwapCatalog.SkinPrefabPath("Rifle"));
        }

        [Test]
        public void Pick_IsDeterministic_AndCoversOptions()
        {
            string[] options = { "A", "B", "C" };
            Assert.AreEqual(AssetSwapCatalog.Pick(options, "Vehicle_07"), AssetSwapCatalog.Pick(options, "Vehicle_07"));
            var picked = Enumerable.Range(1, 30).Select(i => AssetSwapCatalog.Pick(options, $"Crate_X_{i:00}")).Distinct().Count();
            Assert.AreEqual(3, picked, "번호가 바뀌면 고르는 것도 돈다");
        }

        [Test]
        public void AnchorPoint_BottomAndFrontTip()
        {
            var b = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f));
            Assert.AreEqual(new Vector3(1f, 0f, 3f), AssetSwapCatalog.AnchorPoint(b, AssetSwapCatalog.Anchor.Bottom));
            Assert.AreEqual(new Vector3(1f, 2f, 6f), AssetSwapCatalog.AnchorPoint(b, AssetSwapCatalog.Anchor.FrontTip));
        }

        [Test]
        public void FitInside_UsesTightestAxis_IgnoresZeroTargets()
        {
            Assert.AreEqual(0.5f, AssetSwapCatalog.FitInside(new Vector3(2f, 1f, 1f), new Vector3(1f, 1f, 1f)), 1e-5f);
            Assert.AreEqual(2f, AssetSwapCatalog.FitInside(new Vector3(2f, 9f, 1f), new Vector3(4f, 0f, 3f)), 1e-5f, "높이 0 = 제한 없음");
            Assert.AreEqual(1f, AssetSwapCatalog.FitInside(Vector3.one, Vector3.zero), "제한이 없으면 1");
        }

        [Test]
        public void AlignLongAxis_RotatesWhenAxesDiffer()
        {
            // 원본 긴 축 z(차), 블록 긴 축 x → 90° 돌리고 목표 x·z를 바꾼다
            (float yaw, Vector3 target) = AssetSwapCatalog.AlignLongAxis(new Vector3(2f, 1.5f, 5f), 4f, 1.5f, 2f);
            Assert.AreEqual(90f, yaw);
            Assert.AreEqual(new Vector3(2f, 1.5f, 4f), target);
            // 같은 축이면 그대로
            (yaw, target) = AssetSwapCatalog.AlignLongAxis(new Vector3(10f, 3f, 6f), 8f, 3.5f, 6f);
            Assert.AreEqual(0f, yaw);
            Assert.AreEqual(new Vector3(8f, 3.5f, 6f), target);
        }

        [Test]
        public void FillScale_PerAxis()
        {
            Assert.AreEqual(new Vector3(0.5f, 2f, 1f), AssetSwapCatalog.FillScale(new Vector3(2f, 1f, 3f), new Vector3(1f, 2f, 3f)));
            Assert.AreEqual(1f, AssetSwapCatalog.FillScale(new Vector3(0f, 1f, 1f), new Vector3(5f, 1f, 1f)).x, "크기 0 축은 1");
        }

        [Test]
        public void Tile_CountAndStretch_FillLength()
        {
            (int count, float stretch) = AssetSwapCatalog.Tile(20f, 2f);
            Assert.AreEqual(10, count);
            Assert.AreEqual(1f, stretch, 1e-5f);
            (count, stretch) = AssetSwapCatalog.Tile(3f, 2f);
            Assert.AreEqual(2, count, "1.5 → 반올림 2");
            Assert.AreEqual(0.75f, stretch, 1e-5f);
            (count, _) = AssetSwapCatalog.Tile(0.5f, 2f);
            Assert.AreEqual(1, count, "최소 1");
            Assert.AreEqual(-1.5f, AssetSwapCatalog.TileCenter(0, 2, 6f), 1e-5f);
            Assert.AreEqual(1.5f, AssetSwapCatalog.TileCenter(1, 2, 6f), 1e-5f);
        }

        [Test]
        public void Tinted_MultipliesTowardToken()
        {
            Color c = AssetSwapCatalog.Tinted(Color.white, Color.black, 0.35f);
            Assert.AreEqual(0.65f, c.r, 1e-5f);
            Assert.AreEqual(1f, c.a, "알파 유지");
            Assert.AreEqual(Color.white, AssetSwapCatalog.Tinted(Color.white, Color.red, 0f));
        }

        [Test]
        public void BlockoutSkin_HidesWhileEnabled_RestoresOnDisable()
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var skin = new GameObject("Skin");
            try
            {
                var renderer = block.GetComponent<Renderer>();
                var marker = skin.AddComponent<BlockoutSkin>();
                marker.SetHidden(new[] { renderer });
                Assert.IsFalse(renderer.forceRenderingOff, "스킨 모델이 없으면(Missing Prefab 자리) 숨기지 않는다");
                GameObject.CreatePrimitive(PrimitiveType.Sphere).transform.SetParent(skin.transform);
                marker.SetHidden(new[] { renderer });
                Assert.IsTrue(renderer.forceRenderingOff, "켜져 있으면 숨김");
                Assert.IsTrue(renderer.enabled, "렌더러 자체(직렬화 값)는 켜진 그대로");
                skin.SetActive(false);
                Assert.IsFalse(renderer.forceRenderingOff, "스킨이 꺼지면 블록아웃이 보인다");
                skin.SetActive(true);
                Assert.IsTrue(renderer.forceRenderingOff);
                marker.SetHidden(null);
                Assert.IsFalse(renderer.forceRenderingOff, "목록을 비우면 되돌린다");
            }
            finally
            {
                Object.DestroyImmediate(skin);
                Object.DestroyImmediate(block);
            }
        }
    }
}
