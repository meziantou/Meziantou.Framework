using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The extent of the <c>clip-path</c> of a box (https://drafts.fxtf.org/css-masking/#the-clip-path): the bounding box of its
/// basic shape (the path of <see cref="BasicShapePaths"/>) in its reference box, or the reference box alone. Used by the
/// intersection observers and the layout shifts, which clip the targets by the clip paths of their ancestors. The <c>url()</c>
/// references (SVG <c>clipPath</c> elements) are not supported (no clip).
/// </summary>
internal static class ClipPaths
{
    /// <summary>Returns the bounding box of the clip path of a box fragment, in the coordinates of its border box, or null when it has none (or it is not supported).</summary>
    public static PhysicalRect? GetBounds(BoxFragment fragment)
    {
        var style = fragment.Box.Style;
        if (BasicShapePaths.ParseClipPath(style.GetComputedValue("clip-path")) is not { Url: null } clipPath)
            return null;

        // https://drafts.fxtf.org/css-masking/#typedef-geometry-box: the reference box (border-box by default)
        var box = BasicShapePaths.GetReferenceBox(fragment, clipPath.ReferenceBox);
        if (clipPath.Shape is not { } shape)
            return box;

        var zoom = style.Layout.EffectiveZoom;
        if (InfraAscii.EqualsIgnoreAsciiCase(shape.Name, "inset"))
            return GetInsetBounds(shape, box, zoom);

        if (BasicShapePaths.CreatePath(shape, box, zoom) is not { } path)
            return null;

        // The bounding box of the points of the path: the control points of the Bézier curves of the rounded corners and of the
        // ellipses are on their bounding box
        var points = path.Points;
        if (points.Count == 0)
            return new PhysicalRect(box.Left, box.Top, 0, 0);

        var (left, top, right, bottom) = (points[0].X, points[0].Y, points[0].X, points[0].Y);
        for (var i = 1; i < points.Count; i++)
        {
            var point = points[i];
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        return new PhysicalRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-inset: the rectangle of the insets (the rounded corners do not
    /// change the bounding box). As in the browsers, the rectangle of insets whose sum exceeds the size of the reference box is
    /// empty at the top and left insets (the painting reduces them proportionally): e.g. <c>inset(1000px)</c> clips a target that
    /// touches the center of the box (WPT intersection-observer/clip-path.html).
    /// </summary>
    private static PhysicalRect? GetInsetBounds(CssFunction shape, PhysicalRect box, double zoom)
    {
        var arguments = ValueMatcher.RemoveWhitespace(shape.Value);
        var round = arguments.FindIndex(argument => argument is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "round"));
        var count = round < 0 ? arguments.Count : round;
        if (count is 0 or > 4)
            return null;

        var insets = new LengthPercentage[count];
        for (var i = 0; i < count; i++)
        {
            if (!LengthPercentage.TryParse(arguments[i], zoom, nonNegative: false, out insets[i]))
                return null;
        }

        // The sides are expanded as the margins, the vertical ones refer to the height of the box, the horizontal ones to its width
        var top = insets[0].Resolve(box.Height);
        var right = (count > 1 ? insets[1] : insets[0]).Resolve(box.Width);
        var bottom = (count > 2 ? insets[2] : insets[0]).Resolve(box.Height);
        var left = (count > 3 ? insets[3] : count > 1 ? insets[1] : insets[0]).Resolve(box.Width);
        return new PhysicalRect(box.Left + left, box.Top + top, Math.Max(0, box.Width - left - right), Math.Max(0, box.Height - top - bottom));
    }
}
