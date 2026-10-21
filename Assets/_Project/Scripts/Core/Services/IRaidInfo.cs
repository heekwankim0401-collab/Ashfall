using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Core
{
    // 지도 화면의 표식 하나 (RULE-06 지도, M4 설계 §4.1). 월드 XZ 좌표.
    public readonly struct MapMarker
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly Vector2 PositionXZ;

        public MapMarker(string id, string displayName, Vector2 positionXZ)
        {
            Id = id;
            DisplayName = displayName;
            PositionXZ = positionXZ;
        }
    }

    // 레이드 정보 (지도·일시정지 화면이 열 때 동기로 읽는다). Raid(RaidController)가 등록한다.
    // TODO(GDD-?) Q10·Q11: HUD에 남은 시간 없음. 지도 화면에서 분 단위 대략치로만 보여 준다.
    public interface IRaidInfo
    {
        float ElapsedSec { get; }
        float RemainingSec { get; }
        Rect MapAreaXZ { get; }                              // 지도에 그릴 월드 범위
        IReadOnlyList<MapMarker> PointsOfInterest { get; }
        IReadOnlyList<MapMarker> ActiveExtractions { get; }  // RULE-06: 지도에서만. 내 위치는 주지 않는다(멤버 추가 금지)
    }
}
