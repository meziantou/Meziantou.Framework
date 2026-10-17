using System.Runtime.CompilerServices;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>The conversions of the geometry of the display lists to the one of the drawing API of Meziantou.Framework.Imaging.</summary>
internal static class PathConversions
{
    /// <summary>The largest magnitude of a coordinate of the drawing API: the coordinates beyond it are brought to it.</summary>
    public const double MaxCoordinate = 16_777_216;

    private static readonly ConditionalWeakTable<DisplayPath, VectorPath> Paths = [];

    /// <summary>Returns a coordinate in the range of the drawing API (a NaN is 0).</summary>
    public static double Clamp(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, -MaxCoordinate, MaxCoordinate);

    /// <summary>Returns a point in the range of the drawing API.</summary>
    public static PointD ToPoint(PhysicalOffset point) => new(Clamp(point.X), Clamp(point.Y));

    /// <summary>Returns a rectangle in the range of the drawing API (its edges are brought into the range).</summary>
    public static RectangleD ToRectangle(PhysicalRect rect)
    {
        var left = Clamp(Math.Min(rect.Left, rect.Right));
        var top = Clamp(Math.Min(rect.Top, rect.Bottom));
        var right = Clamp(Math.Max(rect.Left, rect.Right));
        var bottom = Clamp(Math.Max(rect.Top, rect.Bottom));
        return RectangleD.FromLTRB(left, top, right, bottom);
    }

    /// <summary>Returns the path of the drawing API of a path of a display list (cached: the paths of the display lists are not modified).</summary>
    public static VectorPath ToVectorPath(DisplayPath path) => Paths.GetValue(path, static path => Convert(path));

    private static VectorPath Convert(DisplayPath path)
    {
        var builder = new VectorPathBuilder();
        var points = path.Points;
        var verbs = path.Verbs;
        var index = 0;
        var open = false;
        foreach (var verb in verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    builder.MoveTo(ToPoint(points[index++]));
                    open = true;
                    break;

                case PathVerb.LineTo:
                    EnsureContour(builder, ref open, ToPoint(points[index]));
                    builder.LineTo(ToPoint(points[index++]));
                    break;

                case PathVerb.QuadTo:
                    EnsureContour(builder, ref open, ToPoint(points[index]));
                    builder.QuadraticBezierTo(ToPoint(points[index]), ToPoint(points[index + 1]));
                    index += 2;
                    break;

                case PathVerb.CubicTo:
                    EnsureContour(builder, ref open, ToPoint(points[index]));
                    builder.CubicBezierTo(ToPoint(points[index]), ToPoint(points[index + 1]), ToPoint(points[index + 2]));
                    index += 3;
                    break;

                case PathVerb.Close:
                    if (open)
                    {
                        builder.Close();
                        open = false;
                    }

                    break;
            }
        }

        return builder.Build();

        // A segment without a move starts at its first point (the display lists always start their contours with a move)
        static void EnsureContour(VectorPathBuilder builder, ref bool open, PointD point)
        {
            if (!open)
            {
                builder.MoveTo(point);
                open = true;
            }
        }
    }

    /// <summary>
    /// Returns the path of a rounded rectangle. The radii that do not fit in the rectangle are reduced as the ones of
    /// <c>border-radius</c> (https://drafts.csswg.org/css-backgrounds/#corner-overlap: all by the same factor), and a corner
    /// whose radius is 0 in an axis is square.
    /// </summary>
    public static DisplayPath GetRoundedRectPath(RoundedRect rounded)
    {
        if (rounded.Contour is not null || rounded.IsRect)
            return DisplayPath.FromRoundedRect(rounded);

        var rect = rounded.Rect;
        var topLeft = Sanitize(rounded.Radii.TopLeft);
        var topRight = Sanitize(rounded.Radii.TopRight);
        var bottomRight = Sanitize(rounded.Radii.BottomRight);
        var bottomLeft = Sanitize(rounded.Radii.BottomLeft);
        var factor = 1d;
        Limit(ref factor, rect.Width, topLeft.X + topRight.X);
        Limit(ref factor, rect.Width, bottomLeft.X + bottomRight.X);
        Limit(ref factor, rect.Height, topLeft.Y + bottomLeft.Y);
        Limit(ref factor, rect.Height, topRight.Y + bottomRight.Y);
        if (factor < 1)
        {
            topLeft = (topLeft.X * factor, topLeft.Y * factor);
            topRight = (topRight.X * factor, topRight.Y * factor);
            bottomRight = (bottomRight.X * factor, bottomRight.Y * factor);
            bottomLeft = (bottomLeft.X * factor, bottomLeft.Y * factor);
        }

        return DisplayPath.FromRoundedRect(rounded with { Radii = new BorderRadii(topLeft, topRight, bottomRight, bottomLeft) });

        static (double X, double Y) Sanitize((double X, double Y) radius) => radius.X > 0 && radius.Y > 0 && double.IsFinite(radius.X) && double.IsFinite(radius.Y) ? radius : (0, 0);

        static void Limit(ref double factor, double side, double sum)
        {
            if (sum > side && sum > 0)
            {
                factor = Math.Min(factor, Math.Max(0, side) / sum);
            }
        }
    }

    /// <summary>Returns whether a path is a single closed rectangle whose sides are parallel to the axes.</summary>
    public static bool TryGetRect(DisplayPath path, out PhysicalRect rect)
    {
        rect = default;
        var verbs = path.Verbs;
        var points = path.Points;
        if (verbs.Count is not (5 or 6) || points.Count != verbs.Count - 1 || verbs[0] != PathVerb.MoveTo || verbs[^1] != PathVerb.Close)
            return false;

        for (var i = 1; i < verbs.Count - 1; i++)
        {
            if (verbs[i] != PathVerb.LineTo)
                return false;
        }

        // A fifth point closes the rectangle explicitly
        if (points.Count == 5 && points[4] != points[0])
            return false;

        var (p0, p1, p2, p3) = (points[0], points[1], points[2], points[3]);
        var horizontalFirst = p0.Y == p1.Y && p1.X == p2.X && p2.Y == p3.Y && p3.X == p0.X;
        var verticalFirst = p0.X == p1.X && p1.Y == p2.Y && p2.X == p3.X && p3.Y == p0.Y;
        if (!horizontalFirst && !verticalFirst)
            return false;

        var left = Math.Min(p0.X, p2.X);
        var top = Math.Min(p0.Y, p2.Y);
        rect = new PhysicalRect(left, top, Math.Max(p0.X, p2.X) - left, Math.Max(p0.Y, p2.Y) - top);
        return rect.Width > 0 && rect.Height > 0;
    }

    /// <summary>Returns the transform of the drawing API of a 2D transform of a display list.</summary>
    public static AffineTransform ToAffine(DisplayMatrix matrix) => new(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);

    /// <summary>Returns the 3D transform of the drawing API of a 4x4 matrix in column-major order (the column vectors of CSS are its row vectors).</summary>
    public static Transform3D ToTransform3D(double[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
}
