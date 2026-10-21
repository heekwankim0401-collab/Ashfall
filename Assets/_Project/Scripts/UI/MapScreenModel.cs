using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.UI
{
    public enum MapMarkerKind
    {
        PointOfInterest,
        Extraction,   // 활성 탈출구만 (§8.2 "지도 아이템에서만 확인 가능")
    }

    // 지도에 그릴 표식 하나. Position은 지도 영역 기준 0~1 (x: 서→동, y: 북→남 = 화면 위→아래).
    public readonly struct MapMarkerView
    {
        public readonly string Id;
        public readonly string Label;
        public readonly MapMarkerKind Kind;
        public readonly Vector2 Position;

        public MapMarkerView(string id, string label, MapMarkerKind kind, Vector2 position)
        {
            Id = id;
            Label = label;
            Kind = kind;
            Position = position;
        }
    }

    // RULE-06 종이 지도 (M). POI·활성 탈출구·남은 시간 대략치만. 내 위치는 그리지 않는다 —
    // IRaidInfo에 플레이어 위치가 없고, 이 모델도 IPlayerTarget 같은 위치 소스를 받지 않는다.
    // TODO(GDD-?) Q10: 남은 레이드 시간은 HUD가 아니라 여기서만, 분 단위 대략치("약 12분")로 보인다.
    // TODO(GDD-?) Q11: 정적 지도(그레이박스 표식). 지형 그림은 M6.
    public sealed class MapScreenModel
    {
        private readonly List<MapMarkerView> markers = new List<MapMarkerView>();
        private int timeKey = int.MinValue;

        public IReadOnlyList<MapMarkerView> Markers => markers;

        // "남은 시간: 약 12분". Build 전에는 null.
        public string RemainingText { get; private set; }

        // 화면을 열 때. 표식과 남은 시간을 다시 만든다.
        public void Build(IRaidInfo info)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));
            markers.Clear();
            Rect area = info.MapAreaXZ;
            Add(info.PointsOfInterest, MapMarkerKind.PointOfInterest, area);
            Add(info.ActiveExtractions, MapMarkerKind.Extraction, area);
            timeKey = int.MinValue;
            RefreshTime(info);
        }

        // 열려 있는 동안 매 프레임 불러도 된다. 표시 값(분)이 바뀔 때만 문자열을 만들고 true.
        public bool RefreshTime(IRaidInfo info)
        {
            if (info == null)
                return false;
            int key = TimeKey(info.RemainingSec);
            if (key == timeKey)
                return false;
            timeKey = key;
            RemainingText = RemainingTextFor(info.RemainingSec);
            return true;
        }

        public static string RemainingTextFor(float remainingSec)
        {
            int key = TimeKey(remainingSec);
            if (key < 0)
                return "남은 시간: 없음";
            if (key == 0)
                return "남은 시간: 1분 미만";
            return "남은 시간: 약 " + key + "분";
        }

        // 분 단위 반올림(.5는 올림). 1분 미만 = 0, 끝남 = -1.
        public static int TimeKey(float remainingSec)
        {
            if (!(remainingSec > 0f))
                return -1;
            if (remainingSec < 60f)
                return 0;
            return Mathf.FloorToInt(remainingSec / 60f + 0.5f);
        }

        // 월드 XZ → 지도 0~1 (북쪽 = 위). 영역 밖은 가장자리로 붙인다.
        public static Vector2 Normalize(Rect areaXZ, Vector2 positionXZ)
        {
            float u = areaXZ.width > 0f ? (positionXZ.x - areaXZ.xMin) / areaXZ.width : 0.5f;
            float v = areaXZ.height > 0f ? (positionXZ.y - areaXZ.yMin) / areaXZ.height : 0.5f;
            return new Vector2(Mathf.Clamp01(u), 1f - Mathf.Clamp01(v));
        }

        private void Add(IReadOnlyList<MapMarker> source, MapMarkerKind kind, Rect area)
        {
            if (source == null)
                return;
            for (int i = 0; i < source.Count; i++)
            {
                MapMarker m = source[i];
                string label = string.IsNullOrEmpty(m.DisplayName) ? m.Id : m.DisplayName;
                markers.Add(new MapMarkerView(m.Id, label, kind, Normalize(area, m.PositionXZ)));
            }
        }
    }
}
