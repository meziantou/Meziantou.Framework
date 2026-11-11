using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

internal static class EdgeMarkers
{
    /// <summary>Returns how far from the end of an edge its line stops, so it does not show through the marker.</summary>
    public static double GetSetback(EdgeMarker marker) => marker switch
    {
        EdgeMarker.Arrow => 8,
        EdgeMarker.OpenTriangle => 17,
        EdgeMarker.FilledDiamond or EdgeMarker.OpenDiamond => 17,
        EdgeMarker.Circle => 9,
        EdgeMarker.OpenCircle => 12,
        EdgeMarker.ContainsCircle => 14,
        _ => 0,
    };

    /// <summary>Draws a marker whose tip is on <paramref name="tip"/>, the edge arriving from <paramref name="from"/>.</summary>
    public static void Draw(SceneGroup group, EdgeMarker marker, LayoutPoint tip, LayoutPoint from, SceneColor color, SceneColor background, double strokeWidth)
    {
        if (marker is EdgeMarker.None)
            return;

        var length = Geometry.Distance(tip, from);
        if (length < 1e-9 || !Geometry.IsFinite(tip) || !Geometry.IsFinite(from))
            return;

        // The axes of the marker: u goes back along the edge, v across it
        var dx = (tip.X - from.X) / length;
        var dy = (tip.Y - from.Y) / length;
        (double X, double Y) P(double u, double v) => (tip.X - (dx * u) - (dy * v), tip.Y - (dy * u) + (dx * v));

        switch (marker)
        {
            case EdgeMarker.Arrow:
                Fill(Geometry.Polygon(P(0, 0), P(8, -4), P(8, 4)), color);
                break;
            case EdgeMarker.OpenArrow:
                Stroke(new PathBuilder().MoveTo(P(9, -5).X, P(9, -5).Y).LineTo(tip.X, tip.Y).LineTo(P(9, 5).X, P(9, 5).Y).Build());
                break;
            case EdgeMarker.OpenTriangle:
                FillAndStroke(Geometry.Polygon(P(0, 0), P(17, -6), P(17, 6)), background);
                break;
            case EdgeMarker.FilledDiamond:
                FillAndStroke(Geometry.Polygon(P(0, 0), P(8.5, -6), P(17, 0), P(8.5, 6)), color);
                break;
            case EdgeMarker.OpenDiamond:
                FillAndStroke(Geometry.Polygon(P(0, 0), P(8.5, -6), P(17, 0), P(8.5, 6)), background);
                break;
            case EdgeMarker.Circle:
                Fill(Circle(P(4.5, 0), 4.5), color);
                break;
            case EdgeMarker.OpenCircle:
                FillAndStroke(Circle(P(6, 0), 6), background);
                break;
            case EdgeMarker.ContainsCircle:
                FillAndStroke(Circle(P(7, 0), 7), background);
                Stroke(Line(P(1, 0), P(13, 0)));
                Stroke(Line(P(7, -6), P(7, 6)));
                break;
            case EdgeMarker.Cross:
                Stroke(Line(P(2, -4), P(10, 4)), 2);
                Stroke(Line(P(2, 4), P(10, -4)), 2);
                break;
            case EdgeMarker.ErOnlyOne:
                Stroke(Line(P(9, -9), P(9, 9)));
                Stroke(Line(P(15, -9), P(15, 9)));
                break;
            case EdgeMarker.ErZeroOrOne:
                Stroke(Line(P(9, -9), P(9, 9)));
                FillAndStroke(Circle(P(21, 0), 6), background);
                break;
            case EdgeMarker.ErOneOrMore:
                CrowFoot();
                Stroke(Line(P(18, -9), P(18, 9)));
                break;
            case EdgeMarker.ErZeroOrMore:
                CrowFoot();
                FillAndStroke(Circle(P(24, 0), 6), background);
                break;
        }

        void CrowFoot()
        {
            Stroke(Line(P(0, -9), P(15, 0)));
            Stroke(Line(P(0, 0), P(15, 0)));
            Stroke(Line(P(0, 9), P(15, 0)));
        }

        void Fill(PathData path, SceneColor fill) => group.Add(new ScenePath(path) { Fill = fill, CssClass = "marker" });

        void FillAndStroke(PathData path, SceneColor fill) => group.Add(new ScenePath(path) { Fill = fill, Stroke = color, StrokeWidth = strokeWidth, CssClass = "marker" });

        void Stroke(PathData path, double width = 0) => group.Add(new ScenePath(path) { Stroke = color, StrokeWidth = width > 0 ? width : strokeWidth, CssClass = "marker" });
    }

    private static PathData Line((double X, double Y) from, (double X, double Y) to) => new PathBuilder().MoveTo(from.X, from.Y).LineTo(to.X, to.Y).Build();

    private static PathData Circle((double X, double Y) center, double radius) => Geometry.Ellipse(center.X, center.Y, radius, radius);
}
