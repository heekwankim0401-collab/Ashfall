using UnityEngine;

namespace Ashfall.Audio
{
    // 이 콜라이더의 지면 재질 (§11, M6 Q11). 콜라이더와 같은 오브젝트에 둔다(EditorTools가 블록아웃·웅덩이에 붙인다).
    // 없는 콜라이더는 Terrain = AudioConfigSO.terrainSurface, 그 밖 = untaggedSurface로 본다.
    [DisallowMultipleComponent]
    public sealed class GroundSurface : MonoBehaviour
    {
        [SerializeField] private SurfaceKind kind = SurfaceKind.Debris;

        // 설정은 에디터 메뉴·테스트가 한다(런타임에 바꾸면 SurfaceProbe 캐시와 어긋날 수 있다).
        public SurfaceKind Kind
        {
            get => kind;
            set => kind = value;
        }
    }
}
