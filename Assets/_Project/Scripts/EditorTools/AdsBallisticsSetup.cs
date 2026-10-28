using System;
using System.Collections.Generic;
using System.Text;
using Ashfall.Core;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // 정조준 가늠자 정렬 + 탄도(공기 저항·영점) 셋업 (docs/design/AdsBallistics_design.md, CHANGELOG_GDD 2026-10-28 AB-Q1~Q4).
    // 1) 데이터: WeaponDefinitionSO.adsEyeReliefM·zeroRangeM, AmmoDefinitionSO.dragPerM — **파일에 없는 필드만** 기록(MvpFinishDataSetup 방식).
    //    있으면 조정한 값으로 보고 보존한다. 새로 만드는 무기·탄약 에셋은 생성 람다가 FillNewWeapon/FillNewAmmo로 같은 값을 넣는다
    //    (새 에셋은 필드가 0으로 이미 기록되어 "없는 필드만"으로는 채워지지 않는다).
    // 2) 가늠자 기준점: VM 프리팹 5종에 자식 SightAnchor(WeaponSightLayout)를 맞춘다. 스킨이 있으면 스킨(원본 좌표 표 → VM 로컬), 없으면 블록아웃 값.
    //    바뀐 것이 있을 때만 저장(멱등). Apply Asset Swap·Remove Skins·Apply M7 Content 끝에서도 부른다(스킨을 다시 얹어도 앵커가 따라온다).
    // 배치모드: -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.AdsBallisticsSetup.ApplyAll -logFile setup.log
    //          (앵커만: ...AdsBallisticsSetup.FitSightAnchors)
    public static class AdsBallisticsSetup
    {
        private const string Tag = "[AdsBallisticsSetup]";
        private const float PositionEps = 1e-5f;
        private const float AngleEps = 0.01f;
        // 앵커 점이 스킨·블록아웃 렌더러 경계 밖으로 이보다 멀면 배치표가 틀린 것으로 본다 (m)
        private const float BoundsSlackM = 0.01f;

        // TODO(GDD-?) AB-Q1·Q2 임시값. eyeRelief = 눈 → 가늠자(SightAnchor) 거리 (m), zero = 영점 거리 (m).
        // 영점: 권총·산탄총 25m(근거리), SMG 50m, 소총 100m, 지정사수 200m. 눈 거리: 권총은 팔을 뻗은 자세, 구멍 가늠자는 눈 가까이.
        public static readonly (string Id, string Path, float EyeReliefM, float ZeroRangeM)[] Weapons =
        {
            ("WPN-PST-01", M3DataSetup.PistolPath, 0.30f, 25f),
            ("WPN-SMG-01", M3DataSetup.SmgPath, 0.13f, 50f),
            ("WPN-RIF-01", M3DataSetup.RiflePath, 0.12f, 100f),
            (M7Names.ShotgunId, M7Names.ShotgunPath, 0.15f, 25f),
            (M7Names.DmrId, M7Names.DmrPath, 0.16f, 200f),
        };

        // TODO(GDD-?) AB-Q3 임시값. 이차 저항 k (1/m), 200m 뒤 탄속 ≈ e^(-200k): 9mm 0.67(-33%), 5.56 0.80(-20%), 7.62 0.85(-15%),
        // 12ga 펠릿(00 벅샷 근사) 50m 0.82·200m 0.45. 현실 탄도 계수 범위의 대략값(설계 §4).
        public static readonly (string Id, string Path, float DragPerM)[] Ammo =
        {
            ("AMMO-9MM", M3DataSetup.Ammo9mmPath, 0.0020f),
            ("AMMO-556", M3DataSetup.Ammo556Path, 0.0011f),
            ("AMMO-762", M7Names.Ammo762Path, 0.0008f),
            ("AMMO-12GA", M7Names.Ammo12GaPath, 0.0040f),
        };

        [MenuItem("Ashfall/Setup/Weapons/Apply ADS and Ballistics")]
        public static void ApplyAll()
        {
            ApplyData();
            AssetDatabase.SaveAssets();   // 데이터는 앵커 맞춤 실패와 상관없이 저장 (리뷰 4a)
            if (!FitSightAnchors())
                throw new InvalidOperationException($"{Tag} 가늠자 기준점 맞춤 실패 — 위 오류 로그 확인");
            Debug.Log($"{Tag} 완료");
        }

        [MenuItem("Ashfall/Setup/Weapons/Fit Sight Anchors")]
        public static void FitSightAnchorsMenu()
        {
            if (!FitSightAnchors())
                throw new InvalidOperationException($"{Tag} 가늠자 기준점 맞춤 실패 — 위 오류 로그 확인");
        }

        // ---------- 데이터 ----------

        // 파일에 없는 필드만 기록. 에셋이 없으면 건너뛴다(그 에셋을 만드는 메뉴가 생성 람다로 넣는다).
        public static void ApplyData()
        {
            foreach ((string id, string path, float eyeRelief, float zero) in Weapons)
            {
                var w = AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>(path);
                if (w == null)
                    continue;
                bool changed = false;
                changed |= WriteIfMissing(path, nameof(WeaponDefinitionSO.adsEyeReliefM), () => w.adsEyeReliefM = eyeRelief);
                changed |= WriteIfMissing(path, nameof(WeaponDefinitionSO.zeroRangeM), () => w.zeroRangeM = zero);
                if (changed)
                {
                    EditorUtility.SetDirty(w);
                    Debug.Log($"{Tag} {id}: adsEyeReliefM {w.adsEyeReliefM}, zeroRangeM {w.zeroRangeM} 기록");
                }
            }
            foreach ((string id, string path, float drag) in Ammo)
            {
                var a = AssetDatabase.LoadAssetAtPath<AmmoDefinitionSO>(path);
                if (a == null)
                    continue;
                if (WriteIfMissing(path, nameof(AmmoDefinitionSO.dragPerM), () => a.dragPerM = drag))
                {
                    EditorUtility.SetDirty(a);
                    Debug.Log($"{Tag} {id}: dragPerM {a.dragPerM} 기록");
                }
            }
        }

        // 생성 람다용 (M1Setup·M3DataSetup·M7WeaponDataSetup): id가 표에 있으면 값을 넣는다
        public static void FillNewWeapon(WeaponDefinitionSO w)
        {
            foreach ((string id, string _, float eyeRelief, float zero) in Weapons)
                if (id == w.id)
                {
                    w.adsEyeReliefM = eyeRelief;
                    w.zeroRangeM = zero;
                }
        }

        public static void FillNewAmmo(AmmoDefinitionSO a)
        {
            foreach ((string id, string _, float drag) in Ammo)
                if (id == a.id)
                    a.dragPerM = drag;
        }

        private static bool WriteIfMissing(string assetPath, string fieldName, Action write)
        {
            if (SetupUtil.IsFieldSerialized(assetPath, fieldName))
                return false;
            write();
            return true;
        }

        // ---------- 가늠자 기준점 ----------

        // VM 프리팹마다 SightAnchor를 맞춘다. 프리팹이 없으면 건너뛴다(경고). 배치표가 모양과 맞지 않으면 그 프리팹은 저장하지 않고 false.
        public static bool FitSightAnchors()
        {
            bool ok = true;
            var report = new StringBuilder();
            foreach (WeaponSightLayout.Entry entry in WeaponSightLayout.Entries)
            {
                string path = M7Names.ViewModelPrefabPath(entry.WeaponId);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    Debug.LogWarning($"{Tag} {path} 없음 — 건너뜀 (Ashfall/Setup/Apply M7 Content가 만든다)");
                    continue;
                }
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform root = contents.transform;
                    bool skinned = WeaponSightProbe.TrySkinSourceToRoot(root, entry.WeaponId, out Matrix4x4 sourceToRoot);
                    Vector3 rear, front;
                    if (skinned)
                    {
                        rear = sourceToRoot.MultiplyPoint3x4(entry.SkinRear);
                        front = sourceToRoot.MultiplyPoint3x4(entry.SkinFront);
                    }
                    else
                    {
                        rear = entry.BlockoutRear;
                        front = entry.BlockoutFront;
                    }

                    if (!InsideVisibleBounds(root, skinned, rear, front, out string problem)
                        || skinned && !OnSightParts(root, entry, rear, front, out problem))
                    {
                        Debug.LogError($"{Tag} {path}: {problem} — 저장 안 함 (WeaponSightLayout 확인)");
                        ok = false;
                        continue;
                    }

                    Transform anchor = root.Find(WeaponSightLayout.AnchorChild);
                    bool changed = false;
                    if (anchor == null)
                    {
                        anchor = new GameObject(WeaponSightLayout.AnchorChild).transform;
                        anchor.SetParent(root, false);
                        changed = true;
                    }
                    Quaternion rotation = WeaponSightLayout.LineRotation(rear, front);
                    if ((anchor.localPosition - rear).sqrMagnitude > PositionEps * PositionEps)
                    {
                        anchor.localPosition = rear;
                        changed = true;
                    }
                    if (Quaternion.Angle(anchor.localRotation, rotation) > AngleEps)
                    {
                        anchor.localRotation = rotation;
                        changed = true;
                    }
                    if (anchor.localScale != Vector3.one)
                    {
                        anchor.localScale = Vector3.one;
                        changed = true;
                    }
                    report.AppendLine($"  {entry.WeaponId}: {(skinned ? "스킨" : "블록아웃")} 앵커 {F(rear)} → 가늠쇠 {F(front)}, " +
                                      $"시선 기울기 {Vector3.SignedAngle(Vector3.forward, front - rear, Vector3.right):0.00}°{(changed ? " (갱신)" : "")}");
                    if (changed)
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            Debug.Log($"{Tag} 가늠자 기준점:\n{report}");
            return ok;
        }


        // 스킨 앵커 검증 (리뷰 3): 가늠쇠 끝 = 앞 부품의 좁은 기둥(앞 점 x ±1.5mm, z ±1cm 안 정점) 최고 높이와 ±2mm,
        // 가늠자 = 뒤 부품의 같은 창 안 정점 중 가장 가까운 높이가 ±6mm(구멍 가늠자는 중심과 고리 안쪽 사이가 수 mm)이다. VM 로컬 m.
        // 원본 패키지가 없어 정점을 읽을 수 없으면(스킨만 남은 경우) 경고하고 통과(경계 검사는 이미 했다).
        private const float PostWindowX = 0.0015f, PostWindowZ = 0.01f, FrontTipTolerance = 0.002f, RearTolerance = 0.006f;

        private static bool OnSightParts(Transform root, WeaponSightLayout.Entry entry, Vector3 rear, Vector3 front, out string problem)
        {
            problem = null;
            List<WeaponSightProbe.PartVertices> parts = WeaponSightProbe.CollectSkinParts(root, entry.WeaponId);
            if (parts == null)
            {
                Debug.LogWarning($"{Tag} {entry.WeaponId}: 스킨 원본 패키지가 없어 가늠자 부품 정점 검증을 건너뜀");
                return true;
            }
            WeaponSightProbe.PartVertices frontPart = parts.Find(p => p.Name == entry.FrontPart);
            WeaponSightProbe.PartVertices rearPart = parts.Find(p => p.Name == entry.RearPart);
            if (frontPart == null || rearPart == null)
            {
                problem = $"부품 {entry.FrontPart}/{entry.RearPart} 없음";
                return false;
            }
            float tip = float.NegativeInfinity;
            foreach (Vector3 p in frontPart.Points)
                if (Mathf.Abs(p.x - front.x) <= PostWindowX && Mathf.Abs(p.z - front.z) <= PostWindowZ)
                    tip = Mathf.Max(tip, p.y);
            if (Mathf.Abs(tip - front.y) > FrontTipTolerance)
            {
                problem = $"가늠쇠 끝 높이 {front.y:0.0000} vs {entry.FrontPart} 기둥 {tip:0.0000}";
                return false;
            }
            float nearest = float.PositiveInfinity;
            foreach (Vector3 p in rearPart.Points)
                if (Mathf.Abs(p.x - rear.x) <= PostWindowX && Mathf.Abs(p.z - rear.z) <= PostWindowZ)
                    nearest = Mathf.Min(nearest, Mathf.Abs(p.y - rear.y));
            if (nearest > RearTolerance)
            {
                problem = $"가늠자 높이 {rear.y:0.0000}에서 {entry.RearPart} 정점까지 {nearest:0.0000}m";
                return false;
            }
            return true;
        }

        // 앵커 두 점이 보이는 모양(스킨이면 스킨 렌더러, 아니면 블록아웃 렌더러) 경계 근처에 있는지
        private static bool InsideVisibleBounds(Transform root, bool skinned, Vector3 rear, Vector3 front, out string problem)
        {
            problem = null;
            var renderers = new List<Renderer>();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (AssetSwapApply.UnderSkin(r.transform, root) == skinned)
                    renderers.Add(r);
            if (renderers.Count == 0)
            {
                problem = skinned ? "스킨 렌더러 없음" : "블록아웃 렌더러 없음";
                return false;
            }
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers)
                b.Encapsulate(r.bounds);
            b.Expand(BoundsSlackM * 2f);
            foreach (Vector3 local in new[] { rear, front })
            {
                Vector3 world = root.TransformPoint(local);
                if (!b.Contains(world))
                {
                    problem = $"점 {F(local)}이 모양 경계 밖";
                    return false;
                }
            }
            return true;
        }

        private static string F(Vector3 v) => $"({v.x:0.0000}, {v.y:0.0000}, {v.z:0.0000})";

        // 확인용: VM 프리팹 경로 → 현재 SightAnchor 로컬 위치 (없으면 null)
        public static Vector3? AnchorLocalPosition(string weaponId)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(M7Names.ViewModelPrefabPath(weaponId));
            Transform anchor = prefab != null ? prefab.transform.Find(WeaponSightLayout.AnchorChild) : null;
            return anchor != null ? anchor.localPosition : (Vector3?)null;
        }

    }
}
