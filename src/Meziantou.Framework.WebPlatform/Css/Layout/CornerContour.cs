using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The shapes of the corners of a box (https://drafts.csswg.org/css-borders-4/#corner-shaping): the superellipse parameter of
/// each <c>corner-*-shape</c> (<c>round</c> is 1, <c>bevel</c> 0, <c>scoop</c> -1, <c>squircle</c> 2, <c>square</c> +∞ and
/// <c>notch</c> -∞).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CornerShapes(double TopLeft, double TopRight, double BottomRight, double BottomLeft)
{
    /// <summary>The initial value: the four corners are round.</summary>
    public static CornerShapes Round { get; } = new(1, 1, 1, 1);

    /// <summary>Whether the four corners are round (the quarters of ellipses of <c>border-radius</c>).</summary>
    public bool IsRound => TopLeft == 1 && TopRight == 1 && BottomRight == 1 && BottomLeft == 1;

    /// <summary>Whether a corner is concave (its superellipse parameter is negative).</summary>
    public bool HasConcaveCorner => TopLeft < 0 || TopRight < 0 || BottomRight < 0 || BottomLeft < 0;

    /// <summary>Returns the shapes of the corners of a style.</summary>
    public static CornerShapes Get(ComputedStyle style) => new(
        Parse(style.GetComputedValue("corner-top-left-shape")),
        Parse(style.GetComputedValue("corner-top-right-shape")),
        Parse(style.GetComputedValue("corner-bottom-right-shape")),
        Parse(style.GetComputedValue("corner-bottom-left-shape")));

    /// <summary>Returns the superellipse parameter of a computed <c>corner-*-shape</c> (<c>superellipse(K)</c>), or 1 when it is not one.</summary>
    public static double Parse(string value) => TryParse(value, out var parameter) ? parameter : 1;

    /// <summary>Parses a computed <c>corner-*-shape</c>: <c>superellipse(&lt;number&gt; | infinity | -infinity)</c>.</summary>
    public static bool TryParse(string value, out double parameter)
    {
        parameter = 1;
        if (value is "superellipse(1)")
            return true;

        const string Prefix = "superellipse(";
        if (!value.StartsWith(Prefix, StringComparison.Ordinal) || !value.EndsWith(')', StringComparison.Ordinal))
            return false;

        var argument = value.AsSpan(Prefix.Length, value.Length - Prefix.Length - 1).Trim();
        if (argument.Equals("infinity", StringComparison.OrdinalIgnoreCase))
        {
            parameter = double.PositiveInfinity;
            return true;
        }

        if (argument.Equals("-infinity", StringComparison.OrdinalIgnoreCase))
        {
            parameter = double.NegativeInfinity;
            return true;
        }

        return double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out parameter) && !double.IsNaN(parameter);
    }

    /// <summary>Returns the computed value of a superellipse parameter (<c>superellipse(K)</c>).</summary>
    public static string Format(double parameter)
    {
        if (double.IsPositiveInfinity(parameter))
            return "superellipse(infinity)";

        if (double.IsNegativeInfinity(parameter))
            return "superellipse(-infinity)";

        return "superellipse(" + ((float)parameter).ToString("R", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Returns the normalized superellipse half corner of a superellipse parameter
    /// (https://drafts.csswg.org/css-borders-4/#normalized-superellipse-half-corner): the coordinate of the middle of the curve
    /// of the corner in the unit square, from 0 (<c>notch</c>) to 1 (<c>square</c>). It is the interpolation value of the
    /// parameter.
    /// </summary>
    public static double GetHalfCorner(double parameter)
    {
        if (double.IsNegativeInfinity(parameter))
            return 0;

        if (double.IsPositiveInfinity(parameter))
            return 1;

        var n = Math.Pow(2, Math.Abs(parameter));
        var convexHalfCorner = Math.Pow(0.5, 1 / n);
        return parameter < 0 ? 1 - convexHalfCorner : convexHalfCorner;
    }

    /// <summary>Returns the superellipse parameter of an interpolation value (https://drafts.csswg.org/css-borders-4/#corner-shape-interpolation), the inverse of <see cref="GetHalfCorner"/>.</summary>
    public static double FromHalfCorner(double interpolationValue)
    {
        if (interpolationValue <= 0)
            return double.NegativeInfinity;

        if (interpolationValue >= 1)
            return double.PositiveInfinity;

        if (interpolationValue == 0.5)
            return 0;

        var convexHalfCorner = interpolationValue < 0.5 ? 1 - interpolationValue : interpolationValue;
        var n = Math.Log(0.5) / Math.Log(convexHalfCorner);
        var parameter = Math.Log2(n);
        return interpolationValue < 0.5 ? -parameter : parameter;
    }
}

/// <summary>
/// The contour of a box whose corners are shaped by <c>corner-shape</c> (https://drafts.csswg.org/css-borders-4/#corner-shape-rendering):
/// the border edge of the box, the radii and the shapes of its corners. The path of an edge of the box (the padding edge, the
/// edge of a shadow, of the outline, of the overflow clip...) is the "border contour path" of the specification for the insets
/// between the border edge and that edge: the rectangle of the edge, from which the curve of each corner, offset from the curve
/// of the border edge, is clipped out.
/// </summary>
/// <param name="BorderRect">The border edge.</param>
/// <param name="Radii">The used radii of the corners of the border edge.</param>
/// <param name="Shapes">The superellipse parameters of the corners.</param>
internal sealed record CornerContour(PhysicalRect BorderRect, BorderRadii Radii, CornerShapes Shapes)
{
    /// <summary>
    /// Returns the contour of a border box, or null when no corner is shaped: the corners that have a radius are round (the
    /// shape of a corner without radius has no effect; the <c>square</c> corners have no radius, see <see cref="BorderRadii"/>).
    /// </summary>
    public static CornerContour? Create(ComputedStyle style, PhysicalRect borderRect, BorderRadii radii)
    {
        var shapes = CornerShapes.Get(style);
        if (shapes.IsRound)
            return null;

        shapes = new CornerShapes(
            IsSquare(radii.TopLeft) ? 1 : shapes.TopLeft,
            IsSquare(radii.TopRight) ? 1 : shapes.TopRight,
            IsSquare(radii.BottomRight) ? 1 : shapes.BottomRight,
            IsSquare(radii.BottomLeft) ? 1 : shapes.BottomLeft);
        return shapes.IsRound ? null : new CornerContour(borderRect, radii, shapes);

        static bool IsSquare((double X, double Y) radius) => radius.X <= 0 || radius.Y <= 0;
    }

    /// <summary>Returns the contour moved by an offset.</summary>
    public CornerContour Translate(PhysicalOffset offset) => this with { BorderRect = BorderRect.Translate(offset) };

    /// <summary>Returns the contour scaled from the origin (the border edge and the radii).</summary>
    public CornerContour Scale(double factor)
    {
        return new CornerContour(
            new PhysicalRect(BorderRect.X * factor, BorderRect.Y * factor, BorderRect.Width * factor, BorderRect.Height * factor),
            new BorderRadii(ScaleRadius(Radii.TopLeft), ScaleRadius(Radii.TopRight), ScaleRadius(Radii.BottomRight), ScaleRadius(Radii.BottomLeft)),
            Shapes);

        (double X, double Y) ScaleRadius((double X, double Y) radius) => (radius.X * factor, radius.Y * factor);
    }

    /// <summary>
    /// Adds the closed subpath of the contour of an edge of the box (straight lines: the curves are flattened): the polygon of
    /// <see cref="GetShape"/>, which is not the exact shape when the corners overlap.
    /// </summary>
    public void AddTo(DisplayPath path, PhysicalRect target, bool adjustOutsets = true) => AddPolygon(path, GetShape(target, adjustOutsets).Polygon, reverse: false);

    /// <summary>Adds a closed subpath of straight lines.</summary>
    public static void AddPolygon(DisplayPath path, List<PhysicalOffset> polygon, bool reverse)
    {
        if (polygon.Count < 3)
            return;

        var first = reverse ? polygon[^1] : polygon[0];
        path.MoveTo(first.X, first.Y);
        for (var i = 1; i < polygon.Count; i++)
        {
            var point = reverse ? polygon[polygon.Count - 1 - i] : polygon[i];
            path.LineTo(point.X, point.Y);
        }

        path.Close();
    }

    /// <summary>Whether a point is inside the contour of an edge of the box: in its rectangle, and in no corner area that is clipped out.</summary>
    public bool Contains(PhysicalRect target, PhysicalOffset point)
    {
        if (point.X < target.Left || point.X >= target.Right || point.Y < target.Top || point.Y >= target.Bottom)
            return false;

        foreach (var cutout in GetShape(target, adjustOutsets: true, alwaysGetCutouts: true).Cutouts!)
        {
            if (IsInPolygon(cutout, point))
                return false;
        }

        return true;
    }

    /// <summary>Whether a point is inside a polygon (non-zero winding number).</summary>
    private static bool IsInPolygon(List<PhysicalOffset> polygon, PhysicalOffset point)
    {
        var winding = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var side = ((b.X - a.X) * (point.Y - a.Y)) - ((point.X - a.X) * (b.Y - a.Y));
            if (a.Y <= point.Y)
            {
                if (b.Y > point.Y && side > 0)
                {
                    winding++;
                }
            }
            else if (b.Y <= point.Y && side < 0)
            {
                winding--;
            }
        }

        return winding != 0;
    }

    /// <summary>
    /// The shape of the contour of an edge of a box: the rectangle of the edge without the corner areas that are clipped out.
    /// </summary>
    /// <param name="Polygon">
    /// The polygon of the contour, clockwise from the start of its top side: the curves of the corners joined by the sides,
    /// clipped to the rectangle of the edge. It is the exact shape when <paramref name="Cutouts"/> is null.
    /// </param>
    /// <param name="Cutouts">
    /// The corner areas that are clipped out of the rectangle of the edge when some of them overlap (the union of the areas is
    /// clipped out: <paramref name="Polygon"/>, which joins the curves, is not the shape), or null. Each one is a clockwise
    /// polygon that extends outside of the rectangle (so it shares no edge with it).
    /// </param>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct ContourShape(List<PhysicalOffset> Polygon, List<List<PhysicalOffset>>? Cutouts);

    /// <summary>
    /// Returns the shape of the contour of an edge of the box (https://drafts.csswg.org/css-borders-4/#border-contour-path):
    /// "each corner is clipped out of the path" of the rectangle of the edge (<see cref="AddCorner"/>).
    /// </summary>
    /// <param name="target">The rectangle of the edge.</param>
    /// <param name="adjustOutsets">
    /// Whether the corners of an edge outside of the border edge are the ones of the outset-adjusted border radii (the shadows,
    /// the margin edge, the overflow clip margin), or at the distance of the outsets from the border edge (the outline).
    /// </param>
    /// <param name="alwaysGetCutouts">Whether the corner areas are also returned when they do not overlap.</param>
    public ContourShape GetShape(PhysicalRect target, bool adjustOutsets = true, bool alwaysGetCutouts = false)
    {
        var points = new List<PhysicalOffset>();
        if (target.Width <= 0 || target.Height <= 0)
            return new ContourShape(points, alwaysGetCutouts ? [] : null);

        var top = target.Top - BorderRect.Top;
        var right = BorderRect.Right - target.Right;
        var bottom = BorderRect.Bottom - target.Bottom;
        var left = target.Left - BorderRect.Left;
        var size = BorderRect.Size;

        (double X, double Y) topRight = adjustOutsets ? AdjustInset(size, Radii.TopRight, right, top) : (right, top);
        (double X, double Y) bottomRight = adjustOutsets ? AdjustInset(size, Radii.BottomRight, right, bottom) : (right, bottom);
        (double X, double Y) bottomLeft = adjustOutsets ? AdjustInset(size, Radii.BottomLeft, left, bottom) : (left, bottom);
        (double X, double Y) topLeft = adjustOutsets ? AdjustInset(size, Radii.TopLeft, left, top) : (left, top);

        // The corner areas, extended outside of the rectangle of the edge (diagonally from its corner)
        var cutouts = new List<List<PhysicalOffset>>(4);
        var extent = target.Width + target.Height + 64;
        AddCutout(AddCorner(points, Radii.TopRight.Y, Radii.TopRight.X, topRight.Y, topRight.X, new PhysicalOffset(target.Right, target.Top), new PhysicalOffset(-1, 0), new PhysicalOffset(0, 1), Shapes.TopRight), new PhysicalOffset(target.Right + extent, target.Top - extent));
        AddCutout(AddCorner(points, Radii.BottomRight.X, Radii.BottomRight.Y, bottomRight.X, bottomRight.Y, new PhysicalOffset(target.Right, target.Bottom), new PhysicalOffset(0, -1), new PhysicalOffset(-1, 0), Shapes.BottomRight), new PhysicalOffset(target.Right + extent, target.Bottom + extent));
        AddCutout(AddCorner(points, Radii.BottomLeft.Y, Radii.BottomLeft.X, bottomLeft.Y, bottomLeft.X, new PhysicalOffset(target.Left, target.Bottom), new PhysicalOffset(1, 0), new PhysicalOffset(0, -1), Shapes.BottomLeft), new PhysicalOffset(target.Left - extent, target.Bottom + extent));
        AddCutout(AddCorner(points, Radii.TopLeft.X, Radii.TopLeft.Y, topLeft.X, topLeft.Y, new PhysicalOffset(target.Left, target.Top), new PhysicalOffset(0, 1), new PhysicalOffset(1, 0), Shapes.TopLeft), new PhysicalOffset(target.Left - extent, target.Top - extent));

        // The path starts at the start of the top side: the end of the top left corner
        points.Insert(0, points[^1]);
        points.RemoveAt(points.Count - 1);

        // The parts of the curves outside of the rectangle of the edge (a curve offset inwards starts before the edge, a border
        // wider than the radius of the corner) do not shape it
        const double Epsilon = 1e-9;
        var isInside = true;
        foreach (var point in points)
        {
            if (point.X < target.Left - Epsilon || point.X > target.Right + Epsilon || point.Y < target.Top - Epsilon || point.Y > target.Bottom + Epsilon)
            {
                isInside = false;
                break;
            }
        }

        if (!isInside)
        {
            points = Clip(points, target);
        }

        // Without the repeated points (a square corner next to the end of a side)
        var result = new List<PhysicalOffset>(points.Count);
        foreach (var point in points)
        {
            if (result.Count == 0 || !IsSame(result[^1], point))
            {
                result.Add(point);
            }
        }

        while (result.Count > 1 && IsSame(result[0], result[^1]))
        {
            result.RemoveAt(result.Count - 1);
        }

        if (!isInside && result.Count > 1)
        {
            // The clipped polygon starts at the start of its top side too (its leftmost point on the top of the rectangle)
            var first = 0;
            for (var i = 1; i < result.Count; i++)
            {
                if (result[i].Y < result[first].Y - Epsilon || (Math.Abs(result[i].Y - result[first].Y) <= Epsilon && result[i].X < result[first].X))
                {
                    first = i;
                }
            }

            if (first > 0)
            {
                var rotated = new List<PhysicalOffset>(result.Count);
                rotated.AddRange(result.GetRange(first, result.Count - first));
                rotated.AddRange(result.GetRange(0, first));
                result = rotated;
            }
        }

        return new ContourShape(result, alwaysGetCutouts || HasOverlap(cutouts, target) ? cutouts : null);

        static bool IsSame(PhysicalOffset a, PhysicalOffset b) => Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;

        // The points of the last corner (from index), closed by a point outside of the rectangle, clockwise
        void AddCutout(int index, PhysicalOffset outside)
        {
            if (points.Count - index < 2)
                return;

            var cutout = new List<PhysicalOffset>(points.Count - index + 1) { outside };
            for (var i = points.Count - 1; i >= index; i--)
            {
                cutout.Add(points[i]);
            }

            cutouts.Add(cutout);
        }
    }

    /// <summary>
    /// Whether two corner areas overlap in the rectangle of the edge: their bounds in the rectangle are compared (the areas
    /// whose bounds overlap are handled as overlapping areas).
    /// </summary>
    private static bool HasOverlap(List<List<PhysicalOffset>> cutouts, PhysicalRect target)
    {
        if (cutouts.Count < 2)
            return false;

        Span<PhysicalRect> bounds = stackalloc PhysicalRect[4];
        var count = 0;
        foreach (var cutout in cutouts)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var point in Clip(cutout, target))
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            const double Epsilon = 1e-6;
            if (maxX - minX <= Epsilon || maxY - minY <= Epsilon)
                continue;

            var rect = new PhysicalRect(minX, minY, maxX - minX, maxY - minY);
            for (var i = 0; i < count; i++)
            {
                if (rect.Left < bounds[i].Right - Epsilon && bounds[i].Left < rect.Right - Epsilon && rect.Top < bounds[i].Bottom - Epsilon && bounds[i].Top < rect.Bottom - Epsilon)
                    return true;
            }

            bounds[count++] = rect;
        }

        return false;
    }

    /// <summary>
    /// Returns the inset of a corner for the insets of its two sides: for an outset, the difference between the radius and the
    /// outset-adjusted border radius (https://drafts.csswg.org/css-borders-4/#outset-adjusted-border-radius), so "a small
    /// rounded corner with a big outset does not appear to be disproportionally round".
    /// </summary>
    private static (double X, double Y) AdjustInset(PhysicalSize size, (double X, double Y) radius, double insetX, double insetY)
    {
        var adjusted = BorderRadii.GetOutsetAdjustedRadius(size, radius, -insetX, -insetY);
        return (radius.X - adjusted.X, radius.Y - adjusted.Y);
    }

    /// <summary>
    /// Adds the points of a corner of the contour, clockwise: the "border-aligned corner clip-out path" of
    /// https://drafts.csswg.org/css-borders-4/#border-aligned-corner-clip-out-path without its closing point, or the corner of
    /// the rectangle when nothing is clipped out. The start and the end of the curve of the border edge are moved along the
    /// normals of the curve by the insets (so the distance between the two curves is nearly constant), and the curve is drawn
    /// between them: a superellipse (flattened), a quarter of an ellipse between the tangents for the parameters between -1
    /// and 0, a straight line for a bevel.
    /// </summary>
    /// <param name="points">The points of the contour.</param>
    /// <param name="startRadius">The radius of the corner along <paramref name="v2"/>.</param>
    /// <param name="endRadius">The radius of the corner along <paramref name="v3"/>.</param>
    /// <param name="startInset">The inset of the side the corner starts on.</param>
    /// <param name="endInset">The inset of the side the corner ends on.</param>
    /// <param name="targetOuter">The corner of the rectangle of the edge.</param>
    /// <param name="v3">The unit vector from the corner to the start of the curve (along the side the corner starts on).</param>
    /// <param name="v2">The unit vector from the corner to the end of the curve (along the side the corner ends on).</param>
    /// <param name="k">The superellipse parameter.</param>
    /// <returns>The index of the first point of the corner in <paramref name="points"/>.</returns>
    private static int AddCorner(List<PhysicalOffset> points, double startRadius, double endRadius, double startInset, double endInset, PhysicalOffset targetOuter, PhysicalOffset v3, PhysicalOffset v2, double k)
    {
        var index = points.Count;
        if (!(startRadius > 0) || !(endRadius > 0) || double.IsPositiveInfinity(k))
        {
            points.Add(targetOuter);
            return index;
        }

        var originalOuter = Extend(targetOuter, v3, -endInset, v2, -startInset);
        var originalStart = Extend(originalOuter, v3, endRadius);
        var originalEnd = Extend(originalOuter, v2, startRadius);

        var halfCornerX = CornerShapes.GetHalfCorner(k);
        var controlPointX = Math.Clamp((halfCornerX / (Math.Sqrt(2) - 1)) - (1 / Math.Sqrt(2)), 0, 1);
        var insetDiff = Math.Clamp(endInset - startInset, -startRadius, endRadius);
        if (k <= 0 && (insetDiff == -startRadius || insetDiff == endRadius))
        {
            points.Add(targetOuter);
            return index;
        }

        var startControlPointX = controlPointX;
        var endControlPointX = controlPointX;
        if (insetDiff != 0)
        {
            var bevelNormalDelta = Math.Sqrt(Math.Max(0, (startRadius * startRadius) + (endRadius * endRadius) - (insetDiff * insetDiff)));
            var bevelNormalX = (endRadius * insetDiff) + (startRadius * bevelNormalDelta);
            var bevelNormalY = (-startRadius * insetDiff) + (endRadius * bevelNormalDelta);
            var bevelControlPointX = startRadius * bevelNormalY / ((startRadius * bevelNormalY) + (endRadius * bevelNormalX));
            startControlPointX = k < 0 ? bevelControlPointX * (2 * controlPointX) : 1 - ((1 - bevelControlPointX) * (2 * (1 - controlPointX)));
            endControlPointX = (2 * controlPointX) - startControlPointX;
        }

        var unmappedStartNormal = Normalize(new PhysicalOffset((1 - startControlPointX) * startRadius, startControlPointX * endRadius));
        var unmappedEndNormal = Normalize(new PhysicalOffset(endControlPointX * startRadius, (1 - endControlPointX) * endRadius));
        var startNormal = new PhysicalOffset((v3.X * unmappedStartNormal.X) + (v2.X * unmappedStartNormal.Y), (v3.Y * unmappedStartNormal.X) + (v2.Y * unmappedStartNormal.Y));
        var endNormal = new PhysicalOffset((v3.X * unmappedEndNormal.X) + (v2.X * unmappedEndNormal.Y), (v3.Y * unmappedEndNormal.X) + (v2.Y * unmappedEndNormal.Y));

        var adjustedStart = Extend(originalStart, startNormal, startInset);
        var adjustedEnd = Extend(originalEnd, endNormal, endInset);

        // The perpendicular of (x, y) is (-y, x)
        var startTangent = new PhysicalOffset(startNormal.Y, -startNormal.X);
        var endTangent = new PhysicalOffset(-endNormal.Y, endNormal.X);

        var miterStart = adjustedStart;
        var miterEnd = adjustedEnd;
        if (startInset < 0)
        {
            miterStart = IntersectLines(adjustedStart, adjustedStart + startTangent, targetOuter + v3, targetOuter) ?? adjustedStart;
            if (k >= 0)
            {
                adjustedStart = miterStart;
            }
        }

        if (endInset < 0)
        {
            miterEnd = IntersectLines(adjustedEnd, adjustedEnd + endTangent, targetOuter + v2, targetOuter) ?? adjustedEnd;
            if (k >= 0)
            {
                adjustedEnd = miterEnd;
            }
        }

        var adjustedHeight = ((adjustedEnd.X - adjustedStart.X) * v2.X) + ((adjustedEnd.Y - adjustedStart.Y) * v2.Y);
        var adjustedOuter = Extend(adjustedEnd, v2, -adjustedHeight);
        var adjustedCenter = Extend(adjustedStart, v2, adjustedHeight);

        PhysicalOffset? selfIntersection = null;
        if (k < 0 && startInset < 0 && endInset < 0)
        {
            if (-endInset >= startRadius)
            {
                selfIntersection = IntersectSegmentWithLine(miterStart, adjustedStart, miterEnd, adjustedEnd);
            }

            if (-startInset >= endRadius && selfIntersection is null)
            {
                selfIntersection = IntersectSegmentWithLine(miterEnd, adjustedEnd, miterStart, adjustedStart);
            }
        }

        points.Add(miterStart);
        if (selfIntersection is { } intersection)
        {
            points.Add(intersection);
        }
        else if (double.IsNegativeInfinity(k))
        {
            // A notch: the two sides of the corner area
            points.Add(adjustedCenter);
        }
        else if (k > 0 || k <= -1)
        {
            // The superellipse x^n + y^n = 1 in the corner mapped from the unit square: the point of the angle θ is
            // (sin(θ)^(2/n), cos(θ)^(2/n)), so the points are dense where the curve turns
            var exponent = 2 / Math.Pow(2, Math.Abs(k));
            var center = k < 0 ? adjustedOuter : adjustedCenter;
            var count = GetSegmentCount(adjustedStart, adjustedEnd);
            for (var i = 0; i <= count; i++)
            {
                var (sin, cos) = Math.SinCos(Math.PI / 2 * i / count);
                var x = i == 0 ? 0 : i == count ? 1 : Math.Pow(sin, exponent);
                var y = i == 0 ? 1 : i == count ? 0 : Math.Pow(cos, exponent);
                points.Add(MapToCorner(x, y, adjustedStart, adjustedEnd, center));
            }
        }
        else if (k < 0)
        {
            // Between a bevel and a scoop: the quarter of the ellipse that is tangent to the two tangents of the curve
            var tangentIntersection = IntersectLines(adjustedStart, adjustedStart + startTangent, adjustedEnd, adjustedEnd + endTangent) ?? adjustedStart;
            var count = GetSegmentCount(adjustedStart, adjustedEnd);
            for (var i = 0; i <= count; i++)
            {
                var (sin, cos) = Math.SinCos(Math.PI / 2 * i / count);
                var x = i == 0 ? 0 : i == count ? 1 : 1 - cos;
                var y = i == 0 ? 1 : i == count ? 0 : 1 - sin;
                points.Add(MapToCorner(x, y, adjustedStart, adjustedEnd, tangentIntersection));
            }
        }
        else
        {
            // A bevel: a straight line
            points.Add(adjustedStart);
            points.Add(adjustedEnd);
        }

        points.Add(miterEnd);
        return index;
    }

    /// <summary>Returns the number of segments of the flattened curve of a corner: more for the larger corners.</summary>
    private static int GetSegmentCount(PhysicalOffset start, PhysicalOffset end)
    {
        var extent = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        return double.IsFinite(extent) ? Math.Clamp((int)Math.Ceiling(Math.Sqrt(extent) * 8), 16, 128) : 16;
    }

    private static PhysicalOffset MapToCorner(double x, double y, PhysicalOffset start, PhysicalOffset end, PhysicalOffset center)
        => new(center.X + ((end.X - center.X) * x) + ((start.X - center.X) * y), center.Y + ((end.Y - center.Y) * x) + ((start.Y - center.Y) * y));

    private static PhysicalOffset Extend(PhysicalOffset point, PhysicalOffset vector, double scale) => new(point.X + (vector.X * scale), point.Y + (vector.Y * scale));

    private static PhysicalOffset Extend(PhysicalOffset point, PhysicalOffset vector1, double scale1, PhysicalOffset vector2, double scale2)
        => new(point.X + (vector1.X * scale1) + (vector2.X * scale2), point.Y + (vector1.Y * scale1) + (vector2.Y * scale2));

    private static PhysicalOffset Normalize(PhysicalOffset vector)
    {
        var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
        return length > 0 ? new PhysicalOffset(vector.X / length, vector.Y / length) : vector;
    }

    private static double Cross(PhysicalOffset a, PhysicalOffset b) => (a.X * b.Y) - (a.Y * b.X);

    /// <summary>Returns the intersection of two lines (each through two points), or null when they are parallel.</summary>
    private static PhysicalOffset? IntersectLines(PhysicalOffset a0, PhysicalOffset a1, PhysicalOffset b0, PhysicalOffset b1)
    {
        var a = a1 - a0;
        var b = b1 - b0;
        var denominator = Cross(a, b);
        if (Math.Abs(denominator) < 1e-6)
            return null;

        return Extend(a0, a, Cross(b0 - a0, b) / denominator);
    }

    /// <summary>Returns the intersection of a segment with a line, or of the segment of the points of the line with the line of the segment, or null.</summary>
    private static PhysicalOffset? IntersectSegmentWithLine(PhysicalOffset a0, PhysicalOffset a1, PhysicalOffset b0, PhysicalOffset b1)
    {
        var a = a1 - a0;
        var b = b1 - b0;
        var denominator = Cross(a, b);
        if (Math.Abs(denominator) < 1e-6)
            return null;

        var offset = b0 - a0;
        var aScale = Cross(offset, b) / denominator;
        if (aScale is >= 0 and <= 1)
            return Extend(a0, a, aScale);

        var bScale = Cross(offset, a) / denominator;
        if (bScale is >= 0 and <= 1)
            return Extend(b0, b, bScale);

        return null;
    }

    /// <summary>Clips a polygon to a rectangle (Sutherland–Hodgman: the rectangle is convex).</summary>
    private static List<PhysicalOffset> Clip(List<PhysicalOffset> polygon, PhysicalRect rect)
    {
        polygon = ClipEdge(polygon, static (point, edge) => point.X >= edge, static (a, b, edge) => new PhysicalOffset(edge, a.Y + ((b.Y - a.Y) * (edge - a.X) / (b.X - a.X))), rect.Left);
        polygon = ClipEdge(polygon, static (point, edge) => point.Y >= edge, static (a, b, edge) => new PhysicalOffset(a.X + ((b.X - a.X) * (edge - a.Y) / (b.Y - a.Y)), edge), rect.Top);
        polygon = ClipEdge(polygon, static (point, edge) => point.X <= edge, static (a, b, edge) => new PhysicalOffset(edge, a.Y + ((b.Y - a.Y) * (edge - a.X) / (b.X - a.X))), rect.Right);
        polygon = ClipEdge(polygon, static (point, edge) => point.Y <= edge, static (a, b, edge) => new PhysicalOffset(a.X + ((b.X - a.X) * (edge - a.Y) / (b.Y - a.Y)), edge), rect.Bottom);
        return polygon;

        static List<PhysicalOffset> ClipEdge(List<PhysicalOffset> input, Func<PhysicalOffset, double, bool> isInside, Func<PhysicalOffset, PhysicalOffset, double, PhysicalOffset> intersect, double edge)
        {
            var output = new List<PhysicalOffset>(input.Count + 4);
            for (var i = 0; i < input.Count; i++)
            {
                var current = input[i];
                var previous = input[(i + input.Count - 1) % input.Count];
                var currentInside = isInside(current, edge);
                var previousInside = isInside(previous, edge);
                if (currentInside != previousInside)
                {
                    output.Add(intersect(previous, current, edge));
                }

                if (currentInside)
                {
                    output.Add(current);
                }
            }

            return output;
        }
    }
}
