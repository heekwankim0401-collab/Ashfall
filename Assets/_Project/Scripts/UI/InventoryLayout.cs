using UnityEngine;

namespace Ashfall.UI
{
    // 인벤토리 그리드 픽셀 ↔ 칸 계산. 칸 크기(px)는 USS 변수 --ash-cell-size로 정해지고
    // 뷰가 그리드 요소의 실제 폭에서 다시 잰다(그래서 여기는 숫자를 갖지 않는다).
    public static class InventoryLayout
    {
        // 그리드 폭(px) / 칸 수. 측정 전(0·NaN)이면 0.
        public static float CellPx(float gridWidthPx, int gridCellsX)
        {
            if (gridCellsX <= 0 || float.IsNaN(gridWidthPx) || gridWidthPx <= 0f)
                return 0f;
            return gridWidthPx / gridCellsX;
        }

        // 포인터가 아이템 가운데에 오도록 놓았을 때의 왼쪽 위 칸.
        // localPx: 그리드 왼쪽 위 기준 포인터 위치(px, y 아래쪽).
        public static Vector2Int TopLeftCellForPointer(Vector2 localPx, float cellPx, Vector2Int itemSize)
        {
            if (cellPx <= 0f)
                return Vector2Int.zero;
            float x = localPx.x / cellPx - itemSize.x * 0.5f;
            float y = localPx.y / cellPx - itemSize.y * 0.5f;
            // 반올림 (Mathf.RoundToInt는 .5에서 짝수로 가므로 쓰지 않는다)
            return new Vector2Int(Mathf.FloorToInt(x + 0.5f), Mathf.FloorToInt(y + 0.5f));
        }

        // 누른 자리에서 이만큼 움직이면 드래그 시작
        public static bool ExceedsThreshold(Vector2 from, Vector2 to, float thresholdPx) =>
            (to - from).sqrMagnitude >= thresholdPx * thresholdPx;
    }
}
