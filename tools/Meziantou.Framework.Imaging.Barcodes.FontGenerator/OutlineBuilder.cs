using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Barcodes.FontGenerator;

/// <summary>
/// Turns the design of a glyph into TrueType contours: each centerline is stroked, the strokes and the fills are merged
/// into contours that do not overlap, and the points are rounded to font units.
/// </summary>
internal static class OutlineBuilder
{
    // The merge approximates curves with chords within 1/16 of a unit of the size it is given: the glyph is merged at
    // 1/8 of its size, so that the chords stay within half a font unit of the curves (finer would only add points)
    private const double Scale = 1.0 / 8;

    private static readonly Brush Ink = new SolidBrush(Colors.Black);

    /// <summary>Builds the contours of a glyph, clockwise for the outer ones (TrueType, y up).</summary>
    public static List<List<(int X, int Y)>> Build(GlyphDesign design)
    {
        // Each piece is filled on its own (the contours of a stroke can turn either way), then the pieces are united
        var scale = AffineTransform.CreateTranslation(design.OffsetX, 0).Then(AffineTransform.CreateScale(Scale, Scale));
        var pieces = new List<VectorPath>();
        foreach (var stroke in design.Strokes)
        {
            var pen = new Pen(Ink, stroke.Width * Scale) { LineJoin = stroke.Join, LineCap = stroke.Cap, MiterLimit = 10 };
            pieces.Add(stroke.Centerline.Transform(scale).Widen(pen).Simplify(FillRule.NonZero));
        }

        foreach (var fill in design.Fills)
        {
            pieces.Add(fill.Transform(scale).Simplify(FillRule.NonZero));
        }

        var merged = pieces.Count == 0 ? new VectorPathBuilder().Build() : pieces[0];
        foreach (var piece in pieces.Skip(1))
        {
            merged = merged.Combine(piece, PathCombineMode.Union);
        }
        var contours = new List<List<(int X, int Y)>>();
        List<(int X, int Y)>? current = null;
        foreach (var segment in merged.EnumerateSegments())
        {
            switch (segment.Kind)
            {
                case PathSegmentKind.MoveTo:
                    current = [];
                    contours.Add(current);
                    current.Add(ToFontUnits(segment.Point));
                    break;
                case PathSegmentKind.LineTo:
                    current!.Add(ToFontUnits(segment.Point));
                    break;
                case PathSegmentKind.Close:
                    break;
                default:
                    throw new InvalidOperationException($"The merged outline has a {segment.Kind} segment: only straight segments are expected.");
            }
        }

        var result = new List<List<(int X, int Y)>>();
        foreach (var contour in contours)
        {
            var cleaned = Clean(contour);
            if (cleaned.Count < 3)
                continue;

            // The merge turns the outer contours clockwise with the y axis down, which is counterclockwise with the y
            // axis up; TrueType wants them clockwise with the y axis up
            cleaned.Reverse();
            result.Add(cleaned);
        }

        return result;
    }

    private static (int X, int Y) ToFontUnits(PointD point) => ((int)Math.Round(point.X / Scale, MidpointRounding.AwayFromZero), (int)Math.Round(point.Y / Scale, MidpointRounding.AwayFromZero));

    // Removes the repeated points and the points on a straight line between their neighbors
    private static List<(int X, int Y)> Clean(List<(int X, int Y)> contour)
    {
        var points = new List<(int X, int Y)>(contour.Count);
        foreach (var point in contour)
        {
            if (points.Count == 0 || points[^1] != point)
            {
                points.Add(point);
            }
        }

        while (points.Count > 1 && points[0] == points[^1])
        {
            points.RemoveAt(points.Count - 1);
        }

        var changed = true;
        while (changed && points.Count >= 3)
        {
            changed = false;
            for (var i = 0; i < points.Count && points.Count >= 3; i++)
            {
                var previous = points[(i + points.Count - 1) % points.Count];
                var point = points[i];
                var next = points[(i + 1) % points.Count];
                var cross = ((long)(point.X - previous.X) * (next.Y - point.Y)) - ((long)(point.Y - previous.Y) * (next.X - point.X));
                if (cross == 0)
                {
                    points.RemoveAt(i);
                    changed = true;
                    i--;
                }
            }
        }

        return points;
    }
}
