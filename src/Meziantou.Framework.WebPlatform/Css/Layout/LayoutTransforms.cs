using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The transforms of the boxes (https://drafts.csswg.org/css-transforms-2/#ctm): the current transformation matrix of a box
/// from its <c>translate</c>, <c>rotate</c>, <c>scale</c>, offset path (<see cref="MotionPath"/>) and <c>transform</c>
/// properties around its <c>transform-origin</c>, in the coordinates of its fragment. Used by the scrollable overflow (https://drafts.csswg.org/css-overflow-3/#scrollable);
/// the transforms do not affect the layout.
/// </summary>
internal static class LayoutTransforms
{
    /// <summary>
    /// Returns the transformation matrix of a box fragment (a 4x4 matrix in column-major order, as <see cref="CssTransforms"/>),
    /// which maps a point in the coordinates of the fragment (its border box origin) to the coordinates of the fragment after
    /// the transform; null when the box has no transform or is not transformable
    /// (https://drafts.csswg.org/css-transforms-1/#transformable-element: the non-replaced inline boxes are not).
    /// </summary>
    public static double[]? GetMatrix(BoxFragment fragment)
    {
        // The fragment of a foreignObject element is placed by the SVG layout (its transform maps it to the content box of its svg element)
        if (fragment.SvgTransform is { } svgTransform)
            return svgTransform;

        var box = fragment.Box;
        var layout = box.Style.Layout;
        // The transforms of a table apply to its table wrapper box (https://drafts.csswg.org/css-tables-3/#global-style-overrides)
        if (!layout.HasTransform || box.IsInlineBox || box.Kind is ContainerBoxKind.Viewport or ContainerBoxKind.TableGrid)
            return null;

        // The values are in the coordinates of the element (not zoomed): the matrix is applied to the unzoomed coordinates
        var style = box.Style;
        var zoom = layout.EffectiveZoom;
        var reference = GetReferenceBox(fragment);
        var referenceBox = (reference.Width / zoom, reference.Height / zoom);
        var (originX, originY, originZ) = GetOrigin(style, referenceBox);
        originX += reference.X / zoom;
        originY += reference.Y / zoom;

        // https://drafts.csswg.org/css-transforms-2/#ctm: the offset transform of the motion path is applied after translate,
        // rotate and scale, and before transform
        double[]? matrix;
        var transformBox = new PhysicalRect(reference.X / zoom, reference.Y / zoom, referenceBox.Item1, referenceBox.Item2);
        if (MotionPath.HasOffsetPath(style) && MotionPath.GetMatrix(fragment, new PhysicalOffset(originX, originY), transformBox) is { } offset)
        {
            if (ToMatrix(GetTransformFunctions(style, individual: true, transform: false)) is not { } before || ToMatrix(GetTransformFunctions(style, individual: false, transform: true)) is not { } after)
                return null;

            matrix = CssTransforms.Multiply(CssTransforms.Multiply(before, offset), after);
        }
        else
        {
            matrix = ToMatrix(GetTransformFunctions(style));
            if (matrix is null)
                return null;
        }

        double[]? ToMatrix(string functions)
            => functions == "none" ? Translation(0, 0, 0) : CssTransforms.ToMatrix(CssParser.ParseListOfComponentValues(functions), CssCalcContext.Default, referenceBox, out _);

        // https://drafts.csswg.org/css-transforms-1/#transform-rendering: "the transformation matrix is computed [...] by
        // translating by the computed X, Y and Z values of transform-origin, multiplying by the transform, then translating by
        // the negated values of transform-origin"
        var result = CssTransforms.Multiply(Translation(originX, originY, originZ), matrix);
        result = CssTransforms.Multiply(result, Translation(-originX, -originY, -originZ));
        if (zoom != 1)
        {
            result = CssTransforms.Multiply(CssTransforms.Multiply(Scale(zoom), result), Scale(1 / zoom));
        }

        return result;
    }

    /// <summary>
    /// Returns the transformation matrix of a box fragment with the perspective of its parent box
    /// (https://drafts.csswg.org/css-transforms-2/#perspective-matrix-computation: the perspective applies to the transforms of the
    /// children in the DOM, not to the other descendants), or null when it has no transform. The perspective of the parent is
    /// applied around its <c>perspective-origin</c> in the coordinates of the fragment. A box of a 3D rendering context that has
    /// no transform has the perspective of its parent too (https://drafts.csswg.org/css-transforms-2/#accumulated-3d-transformation-matrix-computation:
    /// "If parent element has a value for perspective which is not none, pre-multiply the parent element's perspective matrix",
    /// for every element of the context): its plane stays in place, its descendants in the context are seen in the perspective.
    /// </summary>
    public static double[]? GetMatrixWithPerspective(BoxFragment fragment)
    {
        if (GetMatrix(fragment) is not { } matrix)
        {
            // (the box of an element that establishes or participates in a 3D rendering context, not the anonymous boxes
            // between it and its parent element)
            return fragment.SvgTransform is null && fragment.Box is { IsAnonymous: false, IsInlineBox: false, Kind: not (ContainerBoxKind.Viewport or ContainerBoxKind.TableGrid) } box
                && (UsesPreserve3D(box) || ParticipatesIn3DRenderingContext(fragment))
                ? GetPerspectiveMatrix(fragment)
                : null;
        }

        return GetPerspectiveMatrix(fragment) is { } perspective ? CssTransforms.Multiply(perspective, matrix) : matrix;
    }

    /// <summary>
    /// Returns the perspective matrix of the parent box of a fragment in the coordinates of the fragment
    /// (https://drafts.csswg.org/css-transforms-2/#perspective-matrix-computation: a translation to <c>perspective-origin</c>, the
    /// perspective, and the inverse translation), or null when the parent box has no perspective.
    /// </summary>
    public static double[]? GetPerspectiveMatrix(BoxFragment fragment)
    {
        if (fragment.SvgTransform is not null || GetParentElementBox(fragment.Box) is not { } parent)
            return null;

        var style = parent.Style;
        var value = style.GetComputedValue("perspective");
        if (value is "none" or "")
            return null;

        var zoom = style.Layout.EffectiveZoom;
        var distance = (CssValueComputer.ParsePixels(value) ?? 0) * zoom;

        // "If the value is smaller than 1px, it must be clamped to 1px"
        distance = Math.Max(distance, zoom);
        if (FindFragment(fragment, parent) is not { } parentFragment)
            return null;

        // https://drafts.csswg.org/css-transforms-2/#perspective-origin-property: in the border box of the parent
        var size = parentFragment.Size;
        var parts = Cascade.CssValueComputer.SplitSpaceSeparated(style.GetComputedValue("perspective-origin"));
        var originX = parts.Length > 0 && LengthPercentage.TryParse(parts[0], zoom, nonNegative: false, out var x) ? x.Resolve(size.Width) : size.Width / 2;
        var originY = parts.Length > 1 && LengthPercentage.TryParse(parts[1], zoom, nonNegative: false, out var y) ? y.Resolve(size.Height) : size.Height / 2;
        var offset = fragment.GetOffsetInRoot() - parentFragment.GetOffsetInRoot();
        originX -= offset.X;
        originY -= offset.Y;

        double[] perspective = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -1 / distance, 0, 0, 0, 1];
        return CssTransforms.Multiply(CssTransforms.Multiply(Translation(originX, originY, 0), perspective), Translation(-originX, -originY, 0));
    }

    /// <summary>Returns the box of the parent element of a box (the anonymous boxes are skipped), or null.</summary>
    internal static ContainerBox? GetParentElementBox(ContainerBox box)
    {
        // A block split out of an inline box is a child of the inline box in the DOM
        if (box.SplitInlineParent is { } inline)
            return inline;

        for (var parent = box.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Kind == ContainerBoxKind.Viewport)
                return null;

            if (!parent.IsAnonymous && parent.Kind != ContainerBoxKind.TableGrid)
                return parent;
        }

        return null;
    }

    /// <summary>Returns the fragment of a box: an ancestor of a fragment (its containing block chain), or the first fragment of the box.</summary>
    private static BoxFragment? FindFragment(Fragment fragment, ContainerBox box)
    {
        for (var ancestor = fragment.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is BoxFragment boxFragment && ReferenceEquals(boxFragment.Box, box))
                return boxFragment;
        }

        return box.FirstFragment as BoxFragment;
    }

    /// <summary>
    /// Returns the used value of <c>transform-style</c> of a box (https://drafts.csswg.org/css-transforms-2/#transform-style-property):
    /// whether it is <c>preserve-3d</c> and the box has no grouping property (https://drafts.csswg.org/css-transforms-2/#grouping-property-values:
    /// an overflow other than visible and clip, <c>opacity</c> less than 1, a filter, a clip, a clip path, an isolation, a mask, a blend mode,
    /// a backdrop filter, a view transition name: https://drafts.csswg.org/css-view-transitions-1/#named-and-transitioning) and is transformable.
    /// </summary>
    public static bool UsesPreserve3D(ContainerBox box)
    {
        if (box.IsAnonymous || box.Kind is ContainerBoxKind.Viewport or ContainerBoxKind.TableGrid || box.IsInlineBox)
            return false;

        var style = box.Style;
        if (style.GetComputedValue("transform-style") != "preserve-3d")
            return false;

        var layout = style.Layout;
        if (layout.OverflowX is not (OverflowKind.Visible or OverflowKind.Clip) || layout.OverflowY is not (OverflowKind.Visible or OverflowKind.Clip))
            return false;

        return style.GetComputedValue("opacity") is "1" or "100%"
            && style.GetComputedValue("filter") is "none" or ""
            && style.GetComputedValue("backdrop-filter") is "none" or ""
            && style.GetComputedValue("clip-path") is "none" or ""
            && style.GetComputedValue("isolation") != "isolate"
            && style.GetComputedValue("mask-image") is "none" or ""
            && style.GetComputedValue("mask-border-source") is "none" or ""
            && style.GetComputedValue("mix-blend-mode") is "normal" or ""
            && style.GetComputedValue("view-transition-name") is "none" or ""
            && !(layout.IsAbsolutelyPositioned && style.GetComputedValue("clip") is not ("auto" or ""));
    }

    /// <summary>
    /// Whether a fragment participates in the 3D rendering context of its parent (https://drafts.csswg.org/css-transforms-2/#3d-rendering-context):
    /// the used value of <c>transform-style</c> of its parent element is <c>preserve-3d</c>.
    /// </summary>
    public static bool ParticipatesIn3DRenderingContext(BoxFragment fragment)
        => fragment.SvgTransform is null && GetParentElementBox(fragment.Box) is { } parent && UsesPreserve3D(parent);

    /// <summary>
    /// Whether the back face of a plane transformed by a matrix is visible (https://drafts.csswg.org/css-transforms-2/#backface-visibility-property):
    /// the z component of the normal of the plane transformed by the inverse of the matrix is negative (as Chromium, the sign of the
    /// cofactor of the z diagonal element times the determinant).
    /// </summary>
    public static bool IsBackFaceVisible(ReadOnlySpan<double> m)
    {
        // The matrix is column-major (m[column * 4 + row]): the minor of the rows and columns x, y and w
        var cofactor = Determinant3(m[0], m[4], m[12], m[1], m[5], m[13], m[3], m[7], m[15]);
        var determinant = Determinant4(m);
        return cofactor * determinant < -1e-12;
    }

    /// <summary>Whether a 4x4 matrix is invertible (https://drafts.csswg.org/css-transforms-2/#transform-rendering: an element with a non-invertible transform is not rendered).</summary>
    public static bool IsInvertible(ReadOnlySpan<double> m)
    {
        var determinant = Determinant4(m);
        return determinant != 0 && double.IsFinite(determinant);
    }

    private static double Determinant3(double a, double b, double c, double d, double e, double f, double g, double h, double i)
        => (a * ((e * i) - (f * h))) - (b * ((d * i) - (f * g))) + (c * ((d * h) - (e * g)));

    private static double Determinant4(ReadOnlySpan<double> m)
    {
        var result = 0d;
        Span<double> minor = stackalloc double[9];
        for (var column = 0; column < 4; column++)
        {
            var index = 0;
            for (var row = 1; row < 4; row++)
            {
                for (var c = 0; c < 4; c++)
                {
                    if (c != column)
                    {
                        minor[index++] = m[(c * 4) + row];
                    }
                }
            }

            var sign = column % 2 == 0 ? 1 : -1;
            result += sign * m[column * 4] * Determinant3(minor[0], minor[1], minor[2], minor[3], minor[4], minor[5], minor[6], minor[7], minor[8]);
        }

        return result;
    }

    /// <summary>Whether a 4x4 matrix maps the plane z = 0 out of that plane (its z row is not zero): a 3D transform that needs a flattening.</summary>
    public static bool HasZOutput(ReadOnlySpan<double> m) => m[2] != 0 || m[6] != 0 || m[14] != 0 || m[3] != 0 || m[7] != 0 || m[11] != 0;

    /// <summary>
    /// Returns the reference box of the transforms of a box (https://drafts.csswg.org/css-transforms-1/#transform-box), in the
    /// coordinates of its fragment: its content box for <c>content-box</c> and <c>fill-box</c>, else its border box ("For elements
    /// with associated CSS layout box, the used value for fill-box is content-box and for stroke-box and view-box is border-box").
    /// </summary>
    public static PhysicalRect GetReferenceBox(BoxFragment fragment)
        => fragment.Box.Style.GetComputedValue("transform-box") is "content-box" or "fill-box" ? fragment.ContentBoxRect : fragment.BorderBoxRect;

    /// <summary>Returns the bounding box of a rectangle transformed by a matrix (projected on the plane z = 0).</summary>
    public static PhysicalRect TransformRect(PhysicalRect rect, ReadOnlySpan<double> matrix)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        Add(rect.Left, rect.Top, matrix);
        Add(rect.Right, rect.Top, matrix);
        Add(rect.Left, rect.Bottom, matrix);
        Add(rect.Right, rect.Bottom, matrix);
        return double.IsFinite(minX) && double.IsFinite(minY) && double.IsFinite(maxX) && double.IsFinite(maxY) ? new PhysicalRect(minX, minY, maxX - minX, maxY - minY) : PhysicalRect.Empty;

        void Add(double x, double y, ReadOnlySpan<double> m)
        {
            var w = (m[3] * x) + (m[7] * y) + m[15];
            if (w <= 0)
                return;

            var tx = ((m[0] * x) + (m[4] * y) + m[12]) / w;
            var ty = ((m[1] * x) + (m[5] * y) + m[13]) / w;
            minX = Math.Min(minX, tx);
            minY = Math.Min(minY, ty);
            maxX = Math.Max(maxX, tx);
            maxY = Math.Max(maxY, ty);
        }
    }

    /// <summary>
    /// Maps a point by a matrix (projected on the plane z = 0). Returns false when the point is behind the viewer (w ≤ 0).
    /// </summary>
    public static bool TryTransformPoint(ReadOnlySpan<double> m, PhysicalOffset point, out PhysicalOffset result)
    {
        var w = (m[3] * point.X) + (m[7] * point.Y) + m[15];
        if (w <= 0)
        {
            result = default;
            return false;
        }

        result = new PhysicalOffset(((m[0] * point.X) + (m[4] * point.Y) + m[12]) / w, ((m[1] * point.X) + (m[5] * point.Y) + m[13]) / w);
        return true;
    }

    /// <summary>
    /// Whether a transformation matrix maps the plane z = 0 to an area (it is invertible on that plane): false, for example, for
    /// <c>scale(0)</c> or a rotation that makes the plane perpendicular to the screen (<c>rotateX(90deg)</c>), within the
    /// rounding errors of the trigonometric functions.
    /// </summary>
    public static bool IsInvertibleOnPlane(ReadOnlySpan<double> m)
    {
        var determinant = (m[0] * ((m[5] * m[15]) - (m[13] * m[7]))) - (m[4] * ((m[1] * m[15]) - (m[13] * m[3]))) + (m[12] * ((m[1] * m[7]) - (m[5] * m[3])));
        return double.IsFinite(determinant) && Math.Abs(determinant) > 1e-10;
    }

    /// <summary>
    /// Maps a point of the coordinates after a transform back to the plane z = 0 of the coordinates before the transform (the
    /// point of the transformed box that is displayed at the point, for the hit testing and <c>convertPointFromNode()</c>):
    /// the inverse of the homography of the plane z = 0. Returns false when the matrix is not invertible on that plane.
    /// </summary>
    public static bool TryInverseTransformPoint(ReadOnlySpan<double> m, PhysicalOffset point, out PhysicalOffset result)
    {
        // The plane z = 0 is mapped by the 3x3 matrix of the columns x, y and w (the rows x, y and w of the 4x4 matrix)
        var a = m[0];
        var b = m[4];
        var c = m[12];
        var d = m[1];
        var e = m[5];
        var f = m[13];
        var g = m[3];
        var h = m[7];
        var i = m[15];
        var determinant = (a * ((e * i) - (f * h))) - (b * ((d * i) - (f * g))) + (c * ((d * h) - (e * g)));
        if (determinant == 0 || !double.IsFinite(determinant))
        {
            result = default;
            return false;
        }

        // The adjugate (the inverse up to the factor 1 / determinant, which the projection cancels)
        var x = (((e * i) - (f * h)) * point.X) + (((c * h) - (b * i)) * point.Y) + ((b * f) - (c * e));
        var y = (((f * g) - (d * i)) * point.X) + (((a * i) - (c * g)) * point.Y) + ((c * d) - (a * f));
        var w = (((d * h) - (e * g)) * point.X) + (((b * g) - (a * h)) * point.Y) + ((a * e) - (b * d));
        if (determinant < 0)
        {
            x = -x;
            y = -y;
            w = -w;
        }

        if (w <= 0)
        {
            result = default;
            return false;
        }

        result = new PhysicalOffset(x / w, y / w);
        return true;
    }

    /// <summary>
    /// Returns the transform functions of a box (https://drafts.csswg.org/css-transforms-2/#ctm): the <c>translate</c>,
    /// <c>rotate</c> and <c>scale</c> properties as functions (<paramref name="individual"/>), followed by the functions of
    /// <c>transform</c> (<paramref name="transform"/>).
    /// </summary>
    internal static string GetTransformFunctions(ComputedStyle style, bool individual = true, bool transform = true)
    {
        var builder = new StringBuilder();
        var translate = individual ? style.GetComputedValue("translate") : "none";
        if (translate != "none")
        {
            var parts = translate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            builder.Append(parts.Length switch
            {
                1 => $"translate({parts[0]}) ",
                2 => $"translate({parts[0]}, {parts[1]}) ",
                _ => $"translate3d({parts[0]}, {parts[1]}, {parts[2]}) ",
            });
        }

        var rotate = individual ? style.GetComputedValue("rotate") : "none";
        if (rotate != "none")
        {
            var parts = rotate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            builder.Append(parts switch
            {
                ["x", var angle] => $"rotateX({angle}) ",
                ["y", var angle] => $"rotateY({angle}) ",
                ["z", var angle] => $"rotateZ({angle}) ",
                [var x, var y, var z, var angle] => $"rotate3d({x}, {y}, {z}, {angle}) ",
                _ => $"rotate({parts[^1]}) ",
            });
        }

        var scale = individual ? style.GetComputedValue("scale") : "none";
        if (scale != "none")
        {
            var parts = scale.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            builder.Append(parts.Length switch
            {
                1 => $"scale({parts[0]}) ",
                2 => $"scale({parts[0]}, {parts[1]}) ",
                _ => $"scale3d({parts[0]}, {parts[1]}, {parts[2]}) ",
            });
        }

        var functions = transform ? style.GetComputedValue("transform") : "none";
        if (functions != "none")
        {
            builder.Append(functions);
        }

        return builder.Length == 0 ? "none" : builder.ToString();
    }

    /// <summary>Returns the computed <c>transform-origin</c> (https://drafts.csswg.org/css-transforms-1/#transform-origin-property) in px, the percentages resolved against the reference box.</summary>
    internal static (double X, double Y, double Z) GetOrigin(ComputedStyle style, (double Width, double Height) referenceBox)
    {
        var parts = Cascade.CssValueComputer.SplitSpaceSeparated(style.GetComputedValue("transform-origin"));
        var x = parts.Length > 0 ? Resolve(parts[0], referenceBox.Width) : referenceBox.Width / 2;
        var y = parts.Length > 1 ? Resolve(parts[1], referenceBox.Height) : referenceBox.Height / 2;
        var z = parts.Length > 2 ? Resolve(parts[2], 0) : 0;
        return (x, y, z);

        static double Resolve(string value, double basis)
        {
            if (value.EndsWith('%', StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
                return percentage * basis / 100;

            return value switch
            {
                "left" or "top" => 0,
                "center" => basis / 2,
                "right" or "bottom" => basis,
                // A calculation mixing lengths and percentages (calc(50% + 10px))
                _ => CssValueComputer.ParsePixels(value) ?? (LengthPercentage.TryParse(value, zoom: 1, nonNegative: false, out var mixed) ? mixed.Resolve(basis) : basis / 2),
            };
        }
    }

    private static double[] Translation(double x, double y, double z) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];

    private static double[] Scale(double factor) => [factor, 0, 0, 0, 0, factor, 0, 0, 0, 0, factor, 0, 0, 0, 0, 1];
}
