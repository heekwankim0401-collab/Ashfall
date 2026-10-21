using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // RULE-06 종이 지도 화면 (M). POI·활성 탈출구 표식과 남은 시간 대략치만 그린다. 내 위치 표식은 없다.
    // 그레이박스: 지도 영역은 USS의 정사각형 칸, 표식은 점 + 이름. 지형 그림은 M6 (TODO(GDD-?) Q11).
    public sealed class MapScreenView
    {
        public const string ScreenName = "map-screen";
        public const string AreaName = "map-area";
        public const string TimeName = "map-time";

        public const string MarkerClass = "map-marker";
        public const string PoiClass = "map-marker--poi";
        public const string ExtractionClass = "map-marker--extract";
        public const string DotClass = "map-marker-dot";
        public const string LabelClass = "map-marker-label";

        private readonly VisualElement screen;
        private readonly VisualElement area;
        private readonly Label time;

        public MapScreenView(VisualElement documentRoot)
        {
            screen = documentRoot.Q<VisualElement>(ScreenName) ?? throw HudView.Missing(ScreenName);
            area = documentRoot.Q<VisualElement>(AreaName) ?? throw HudView.Missing(AreaName);
            time = documentRoot.Q<Label>(TimeName) ?? throw HudView.Missing(TimeName);
            SetVisible(false);
        }

        public void SetVisible(bool visible) =>
            screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void Render(IReadOnlyList<MapMarkerView> markers, string remainingText)
        {
            area.Clear();
            foreach (MapMarkerView m in markers)
            {
                var marker = new VisualElement { pickingMode = PickingMode.Ignore };
                marker.AddToClassList(MarkerClass);
                marker.AddToClassList(m.Kind == MapMarkerKind.Extraction ? ExtractionClass : PoiClass);
                // 표식 왼쪽 위 = 점 중심. 점 크기 보정은 USS(margin)가 한다.
                marker.style.left = Length.Percent(m.Position.x * 100f);
                marker.style.top = Length.Percent(m.Position.y * 100f);

                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList(DotClass);
                marker.Add(dot);

                var label = new Label(m.Label) { pickingMode = PickingMode.Ignore };
                label.AddToClassList(LabelClass);
                marker.Add(label);

                area.Add(marker);
            }
            SetTime(remainingText);
        }

        public void SetTime(string remainingText) => time.text = remainingText ?? string.Empty;
    }
}
