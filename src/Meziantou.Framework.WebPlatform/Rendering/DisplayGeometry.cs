using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>
/// A transform of the display list: a 2D affine transform (<c>x' = A x + C y + E</c>, <c>y' = B x + D y + F</c>, the matrix of
/// <c>matrix(a, b, c, d, e, f)</c>), or a 3D transform (<see cref="Matrix3d"/>, a 4x4 matrix in column-major order, as
/// <c>matrix3d()</c>) whose result is projected on the plane z = 0.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DisplayMatrix(double A, double B, double C, double D, double E, double F)
{
    /// <summary>The identity transform.</summary>
    public static DisplayMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>The 4x4 matrix in column-major order of a 3D transform, or null for a 2D transform (A to F).</summary>
    public double[]? Matrix3d { get; init; }

    /// <summary>Whether the transform is a translation (or the identity).</summary>
    public bool IsTranslation => Matrix3d is null && A == 1 && B == 0 && C == 0 && D == 1;

    /// <summary>Returns a translation.</summary>
    public static DisplayMatrix Translation(double x, double y) => new(1, 0, 0, 1, x, y);

    /// <summary>Returns a scale.</summary>
    public static DisplayMatrix Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

    /// <summary>Returns a rotation clockwise (the y axis points down) by an angle in degrees.</summary>
    public static DisplayMatrix Rotation(double degrees)
    {
        var (sin, cos) = SvgAffine.SinCosDegrees(degrees);
        return new DisplayMatrix(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>Returns the transform of an SVG affine matrix.</summary>
    public static DisplayMatrix FromSvg(SvgAffine affine) => new(affine.A, affine.B, affine.C, affine.D, affine.E, affine.F);

    /// <summary>
    /// Returns the transform of a 4x4 matrix in column-major order (see <see cref="LayoutTransforms.GetMatrix"/>): a 2D transform
    /// when it is one, else a 3D transform.
    /// </summary>
    public static DisplayMatrix FromMatrix4x4(ReadOnlySpan<double> m)
    {
        var is2D = m[2] == 0 && m[3] == 0 && m[6] == 0 && m[7] == 0 && m[8] == 0 && m[9] == 0 && m[10] == 1 && m[11] == 0 && m[14] == 0 && m[15] == 1;
        var affine = new DisplayMatrix(m[0], m[1], m[4], m[5], m[12], m[13]);
        return is2D ? affine : affine with { Matrix3d = m.ToArray() };
    }

    /// <summary>
    /// Returns the transform that applies <paramref name="first"/> then this transform (the matrix product this × first), for
    /// 2D transforms; a 3D transform is composed in 4x4.
    /// </summary>
    public DisplayMatrix PreConcat(DisplayMatrix first)
    {
        if (Matrix3d is null && first.Matrix3d is null)
        {
            return new DisplayMatrix(
                (A * first.A) + (C * first.B),
                (B * first.A) + (D * first.B),
                (A * first.C) + (C * first.D),
                (B * first.C) + (D * first.D),
                (A * first.E) + (C * first.F) + E,
                (B * first.E) + (D * first.F) + F);
        }

        var left = ToMatrix4x4();
        var right = first.ToMatrix4x4();
        var result = new double[16];
        for (var column = 0; column < 4; column++)
        {
            for (var row = 0; row < 4; row++)
            {
                var sum = 0d;
                for (var k = 0; k < 4; k++)
                {
                    sum += left[(k * 4) + row] * right[(column * 4) + k];
                }

                result[(column * 4) + row] = sum;
            }
        }

        return FromMatrix4x4(result);
    }

    /// <summary>Returns the 4x4 matrix in column-major order of the transform.</summary>
    public double[] ToMatrix4x4() => Matrix3d is { } m ? (double[])m.Clone() : [A, B, 0, 0, C, D, 0, 0, 0, 0, 1, 0, E, F, 0, 1];

    /// <summary>Maps a point by the transform (a 3D transform is projected on the plane z = 0).</summary>
    public PhysicalOffset Apply(PhysicalOffset point)
    {
        if (Matrix3d is { } m)
            return LayoutTransforms.TryTransformPoint(m, point, out var result) ? result : point;

        return new PhysicalOffset((A * point.X) + (C * point.Y) + E, (B * point.X) + (D * point.Y) + F);
    }

    /// <summary>Returns the CSS serialization of the transform (<c>translate()</c>, <c>matrix()</c> or <c>matrix3d()</c>).</summary>
    public override string ToString()
    {
        if (Matrix3d is { } m)
            return "matrix3d(" + string.Join(", ", m.Select(DisplayList.FormatNumber)) + ")";

        if (IsTranslation)
            return "translate(" + DisplayList.FormatNumber(E) + ", " + DisplayList.FormatNumber(F) + ")";

        return string.Create(CultureInfo.InvariantCulture, $"matrix({DisplayList.FormatNumber(A)}, {DisplayList.FormatNumber(B)}, {DisplayList.FormatNumber(C)}, {DisplayList.FormatNumber(D)}, {DisplayList.FormatNumber(E)}, {DisplayList.FormatNumber(F)})");
    }
}

/// <summary>
/// A rectangle with rounded corners (https://drafts.csswg.org/css-backgrounds/#corner-shaping): each corner is a quarter of an
/// ellipse with the horizontal and vertical radii of <see cref="Radii"/> (the corners without radius are square). The corners
/// of a box shaped by <c>corner-shape</c> are the ones of its <see cref="Contour"/>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct RoundedRect(PhysicalRect Rect, BorderRadii Radii)
{
    /// <summary>
    /// The contour of the box when its corners are not round (https://drafts.csswg.org/css-borders-4/#corner-shape-rendering),
    /// or null: the shape is then the contour path of the box for the insets between its border edge and <see cref="Rect"/>
    /// (<see cref="CornerContour.GetShape"/>), and <see cref="Radii"/> are only the radii of the elliptical approximation.
    /// </summary>
    public CornerContour? Contour { get; init; }

    /// <summary>
    /// Whether the corners of the contour outside of the border edge are the ones of the outset-adjusted border radii (the edge
    /// was expanded by <see cref="Outset"/>: a shadow, the margin edge, the overflow clip margin), or at the distance of the
    /// outsets from the border edge (<see cref="Inset"/> with negative widths: the outline).
    /// </summary>
    public bool HasAdjustedOutsets { get; init; }

    /// <summary>Whether all the corners are square.</summary>
    public bool IsRect => Contour is null && IsSquare(Radii.TopLeft) && IsSquare(Radii.TopRight) && IsSquare(Radii.BottomRight) && IsSquare(Radii.BottomLeft);

    /// <summary>
    /// Returns the shape of the border edge of a fragment of a box: its border box (at <paramref name="borderBox"/>) with the
    /// radii of <c>border-radius</c> and the shapes of <c>corner-shape</c>.
    /// </summary>
    public static RoundedRect FromBorderBox(BoxFragment fragment, PhysicalRect borderBox)
    {
        if (BorderRadii.Get(fragment) is not { } radii)
            return new RoundedRect(borderBox, default);

        return new RoundedRect(borderBox, radii) { Contour = CornerContour.Create(fragment.Box.Style, borderBox, radii) };
    }

    /// <summary>
    /// Returns the rounded rectangle whose edges are moved inwards by the given widths (the padding edge of a border box,
    /// https://drafts.csswg.org/css-backgrounds/#corner-shaping: "the inner border radius is the outer border radius minus the
    /// corresponding border thickness"), or outwards when they are negative (the spread of a shadow, the outline).
    /// </summary>
    public RoundedRect Inset(Sides<double> widths)
    {
        var rect = Rect.Contract(widths);
        var radii = new BorderRadii(
            Shrink(Radii.TopLeft, widths.Left, widths.Top),
            Shrink(Radii.TopRight, widths.Right, widths.Top),
            Shrink(Radii.BottomRight, widths.Right, widths.Bottom),
            Shrink(Radii.BottomLeft, widths.Left, widths.Bottom));
        return new RoundedRect(rect, radii) { Contour = Contour, HasAdjustedOutsets = HasAdjustedOutsets };

        // An outset of a rounded corner grows its radii; an outset of a square corner keeps it square
        static (double X, double Y) Shrink((double X, double Y) radius, double x, double y)
            => IsSquare(radius) ? default : (Math.Max(0, radius.X - x), Math.Max(0, radius.Y - y));
    }

    /// <summary>
    /// Returns the rounded rectangle whose edges are moved outwards by the given widths, with the outset-adjusted border radii
    /// (https://drafts.csswg.org/css-borders-4/#outset-adjusted-border-radius): the margin edge, the spread of a shadow, the
    /// overflow clip margin. The radii smaller than the outset grow less than the outset, as a function of the part of
    /// <paramref name="edge"/> (the size of the border edge) that the corner covers. The negative widths are insets.
    /// </summary>
    public RoundedRect Outset(Sides<double> widths, PhysicalSize edge)
    {
        var rect = Rect.Expand(widths);
        var radii = new BorderRadii(
            Grow(Radii.TopLeft, widths.Left, widths.Top),
            Grow(Radii.TopRight, widths.Right, widths.Top),
            Grow(Radii.BottomRight, widths.Right, widths.Bottom),
            Grow(Radii.BottomLeft, widths.Left, widths.Bottom));
        return new RoundedRect(rect, radii) { Contour = Contour, HasAdjustedOutsets = Contour is not null };

        (double X, double Y) Grow((double X, double Y) radius, double x, double y)
        {
            if (IsSquare(radius))
                return default;

            var adjusted = BorderRadii.GetOutsetAdjustedRadius(edge, radius, x, y);
            return (Math.Max(0, adjusted.X), Math.Max(0, adjusted.Y));
        }
    }

    /// <summary>Returns the rounded rectangle moved by an offset.</summary>
    public RoundedRect Translate(PhysicalOffset offset) => this with { Rect = Rect.Translate(offset), Contour = Contour?.Translate(offset) };

    private static bool IsSquare((double X, double Y) radius) => radius.X <= 0 || radius.Y <= 0;
}

/// <summary>The commands of a <see cref="DisplayPath"/>.</summary>
internal enum PathVerb : byte
{
    /// <summary>Starts a subpath at a point.</summary>
    MoveTo,

    /// <summary>A straight line to a point.</summary>
    LineTo,

    /// <summary>A quadratic Bézier curve (a control point and the end point).</summary>
    QuadTo,

    /// <summary>A cubic Bézier curve (two control points and the end point).</summary>
    CubicTo,

    /// <summary>Closes the current subpath.</summary>
    Close,
}

/// <summary>
/// A path of the display list (the geometry of an SVG shape, of a clip path, of a canvas path): subpaths of straight lines and
/// Bézier curves, filled with <see cref="FillRule"/>. The arcs and the ellipses are converted to cubic Bézier curves. A path
/// is immutable once it is added to a display list.
/// </summary>
internal sealed class DisplayPath
{
    private readonly List<PathVerb> _verbs = [];
    private readonly List<PhysicalOffset> _points = [];

    /// <summary>The fill rule of the path.</summary>
    public FillRule FillRule { get; set; }

    /// <summary>The commands of the path.</summary>
    public IReadOnlyList<PathVerb> Verbs => _verbs;

    /// <summary>The points of the commands: one for <see cref="PathVerb.MoveTo"/> and <see cref="PathVerb.LineTo"/>, two for <see cref="PathVerb.QuadTo"/>, three for <see cref="PathVerb.CubicTo"/>, none for <see cref="PathVerb.Close"/>.</summary>
    public IReadOnlyList<PhysicalOffset> Points => _points;

    /// <summary>Whether the path has no command.</summary>
    public bool IsEmpty => _verbs.Count == 0;

    /// <summary>
    /// The rounded rectangle (or ellipse) that the path is exactly when it was created by <see cref="FromRoundedRect"/> and not
    /// changed since, or null: a backend can draw or clip it as a rounded rectangle (e.g. with the antialiasing of the borders).
    /// </summary>
    public RoundedRect? RoundedRect { get; private set; }

    /// <summary>Creates a path of a rounded rectangle (<see cref="RoundedRect"/> is set).</summary>
    public static DisplayPath FromRoundedRect(RoundedRect rounded)
    {
        var path = new DisplayPath();
        path.AddRoundedRect(rounded);
        path.RoundedRect = rounded;
        return path;
    }

    public void MoveTo(double x, double y)
    {
        RoundedRect = null;
        _verbs.Add(PathVerb.MoveTo);
        _points.Add(new PhysicalOffset(x, y));
    }

    public void LineTo(double x, double y)
    {
        RoundedRect = null;
        _verbs.Add(PathVerb.LineTo);
        _points.Add(new PhysicalOffset(x, y));
    }

    public void QuadTo(double x1, double y1, double x, double y)
    {
        RoundedRect = null;
        _verbs.Add(PathVerb.QuadTo);
        _points.Add(new PhysicalOffset(x1, y1));
        _points.Add(new PhysicalOffset(x, y));
    }

    public void CubicTo(double x1, double y1, double x2, double y2, double x, double y)
    {
        RoundedRect = null;
        _verbs.Add(PathVerb.CubicTo);
        _points.Add(new PhysicalOffset(x1, y1));
        _points.Add(new PhysicalOffset(x2, y2));
        _points.Add(new PhysicalOffset(x, y));
    }

    public void Close()
    {
        RoundedRect = null;
        _verbs.Add(PathVerb.Close);
    }

    /// <summary>Adds a closed subpath of the four corners of a rectangle, clockwise from its top-left corner.</summary>
    public void AddRect(PhysicalRect rect)
    {
        MoveTo(rect.Left, rect.Top);
        LineTo(rect.Right, rect.Top);
        LineTo(rect.Right, rect.Bottom);
        LineTo(rect.Left, rect.Bottom);
        Close();
    }

    /// <summary>Adds a closed subpath of a quadrilateral.</summary>
    public void AddQuad(PhysicalOffset p1, PhysicalOffset p2, PhysicalOffset p3, PhysicalOffset p4)
    {
        MoveTo(p1.X, p1.Y);
        LineTo(p2.X, p2.Y);
        LineTo(p3.X, p3.Y);
        LineTo(p4.X, p4.Y);
        Close();
    }

    /// <summary>Adds a closed subpath of a rounded rectangle, clockwise from the start of its top side.</summary>
    public void AddRoundedRect(RoundedRect rounded)
    {
        if (rounded.Contour is { } contour)
        {
            // The corners shaped by corner-shape: the contour path of the box (flattened)
            contour.AddTo(this, rounded.Rect, rounded.HasAdjustedOutsets);
            return;
        }

        if (rounded.IsRect)
        {
            AddRect(rounded.Rect);
            return;
        }

        // The control points of the cubic approximation of a quarter of an ellipse
        const double Kappa = 0.5522847498307936;
        var rect = rounded.Rect;
        var (tl, tr, br, bl) = (rounded.Radii.TopLeft, rounded.Radii.TopRight, rounded.Radii.BottomRight, rounded.Radii.BottomLeft);
        MoveTo(rect.Left + tl.X, rect.Top);
        LineTo(rect.Right - tr.X, rect.Top);
        CubicTo(rect.Right - (tr.X * (1 - Kappa)), rect.Top, rect.Right, rect.Top + (tr.Y * (1 - Kappa)), rect.Right, rect.Top + tr.Y);
        LineTo(rect.Right, rect.Bottom - br.Y);
        CubicTo(rect.Right, rect.Bottom - (br.Y * (1 - Kappa)), rect.Right - (br.X * (1 - Kappa)), rect.Bottom, rect.Right - br.X, rect.Bottom);
        LineTo(rect.Left + bl.X, rect.Bottom);
        CubicTo(rect.Left + (bl.X * (1 - Kappa)), rect.Bottom, rect.Left, rect.Bottom - (bl.Y * (1 - Kappa)), rect.Left, rect.Bottom - bl.Y);
        LineTo(rect.Left, rect.Top + tl.Y);
        CubicTo(rect.Left, rect.Top + (tl.Y * (1 - Kappa)), rect.Left + (tl.X * (1 - Kappa)), rect.Top, rect.Left + tl.X, rect.Top);
        Close();
    }

    /// <summary>
    /// Creates a path that fills the area of <paramref name="bounds"/> outside of a rounded rectangle (the area a shadow is
    /// clipped to, the area between the two edges of a border). The corner areas of a box shaped by <c>corner-shape</c> that
    /// overlap are added to the area (non-zero fill rule: their union is outside of the shape).
    /// </summary>
    public static DisplayPath CreateComplement(PhysicalRect bounds, RoundedRect shape)
    {
        if (shape.Contour is { } contour && contour.GetShape(shape.Rect, shape.HasAdjustedOutsets) is { Cutouts: { } cutouts })
        {
            // Clockwise subpaths add to the area, the counterclockwise rectangle of the shape is removed from it
            var path = new DisplayPath { FillRule = FillRule.NonZero };
            path.AddRect(bounds);
            var rect = shape.Rect;
            path.MoveTo(rect.Left, rect.Top);
            path.LineTo(rect.Left, rect.Bottom);
            path.LineTo(rect.Right, rect.Bottom);
            path.LineTo(rect.Right, rect.Top);
            path.Close();
            foreach (var cutout in cutouts)
            {
                CornerContour.AddPolygon(path, cutout, reverse: false);
            }

            return path;
        }

        var result = new DisplayPath { FillRule = FillRule.EvenOdd };
        result.AddRect(bounds);
        if (shape.Rect.Width > 0 && shape.Rect.Height > 0)
        {
            result.AddRoundedRect(shape);
        }

        return result;
    }

    /// <summary>Adds a closed subpath of the ellipse inscribed in a rectangle.</summary>
    public void AddEllipse(PhysicalRect rect) => AddRoundedRect(new RoundedRect(rect, new BorderRadii((rect.Width / 2, rect.Height / 2), (rect.Width / 2, rect.Height / 2), (rect.Width / 2, rect.Height / 2), (rect.Width / 2, rect.Height / 2))));

    /// <summary>
    /// Creates a path from SVG path commands (any command: they are normalized to absolute moveto, lineto, cubic Bézier and
    /// closepath commands, https://svgwg.org/svg2-draft/paths.html).
    /// </summary>
    public static DisplayPath FromSvg(List<SvgPathCommand> commands, FillRule fillRule)
    {
        var path = new DisplayPath { FillRule = fillRule };
        foreach (var command in SvgPathData.Normalize(commands))
        {
            var v = command.Values;
            switch (command.Type)
            {
                case 'M':
                    path.MoveTo(v[0], v[1]);
                    break;

                case 'L':
                    path.LineTo(v[0], v[1]);
                    break;

                case 'C':
                    path.CubicTo(v[0], v[1], v[2], v[3], v[4], v[5]);
                    break;

                default:
                    path.Close();
                    break;
            }
        }

        return path;
    }

    /// <summary>Returns the bounds of the points of the path (the control points included), or an empty rectangle.</summary>
    public PhysicalRect GetBounds()
    {
        if (_points.Count == 0)
            return PhysicalRect.Empty;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var point in _points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new PhysicalRect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Returns the path in the SVG path data syntax (e.g. <c>M0 0L10 0Z</c>), followed by the fill rule when it is evenodd.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        var index = 0;
        foreach (var verb in _verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    AppendPoints('M', 1);
                    break;

                case PathVerb.LineTo:
                    AppendPoints('L', 1);
                    break;

                case PathVerb.QuadTo:
                    AppendPoints('Q', 2);
                    break;

                case PathVerb.CubicTo:
                    AppendPoints('C', 3);
                    break;

                default:
                    builder.Append('Z');
                    break;
            }
        }

        if (FillRule == FillRule.EvenOdd)
        {
            builder.Append(" evenodd");
        }

        return builder.ToString();

        void AppendPoints(char command, int count)
        {
            builder.Append(command);
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                var point = _points[index++];
                builder.Append(DisplayList.FormatNumber(point.X)).Append(' ').Append(DisplayList.FormatNumber(point.Y));
            }
        }
    }
}
