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

    /// <summary>Returns the transform of the drawing API of a 2D transform of a display list.</summary>
    public static AffineTransform ToAffine(DisplayMatrix matrix) => new(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);

    /// <summary>Returns the 3D transform of the drawing API of a 4x4 matrix in column-major order (the column vectors of CSS are its row vectors).</summary>
    public static Transform3D ToTransform3D(double[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
}
