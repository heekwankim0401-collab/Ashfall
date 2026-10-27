using UnityEngine;

namespace Ashfall.Core
{
    // 에셋 교체 스킨 (AssetSwap 설계 §3). 스킨 프리팹 인스턴스("Skin") 루트에 붙어, 켜져 있는 동안 블록아웃 렌더러를 숨긴다.
    // 숨기기 = Renderer.forceRenderingOff(직렬화되지 않음) — 씬·프리팹 파일의 블록아웃 렌더러는 켜진 그대로다.
    // 이 컴포넌트는 인스턴스에 추가된 컴포넌트라 스킨 프리팹이 없는 clone(패키지 미포함)에서도 Missing Prefab 자리에 남는다.
    // 그래서 스킨 쪽 렌더러가 하나도 없으면 숨기지 않는다 → clone에서는 블록아웃이 그대로 보인다.
    // 게임 상태·판정과 무관(콜라이더·GroundSurface·엄폐는 블록아웃 쪽). 에디터에서도 같은 모습이 보이게 [ExecuteAlways].
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BlockoutSkin : MonoBehaviour
    {
        [SerializeField] private Renderer[] hidden = System.Array.Empty<Renderer>();

        public Renderer[] Hidden => hidden;

        // 스킨 모델이 실제로 있는가(Missing Prefab 자리면 자식·렌더러가 없다)
        public bool HasSkinRenderer => GetComponentInChildren<Renderer>(true) != null;

        public void SetHidden(Renderer[] renderers)
        {
            if (isActiveAndEnabled)
                Apply(false);
            hidden = renderers ?? System.Array.Empty<Renderer>();
            if (isActiveAndEnabled)
                Apply(HasSkinRenderer);
        }

        private void OnEnable() => Apply(HasSkinRenderer);

        private void OnDisable() => Apply(false);

        private void Apply(bool off)
        {
            foreach (Renderer r in hidden)
                if (r != null)
                    r.forceRenderingOff = off;
        }
    }
}
