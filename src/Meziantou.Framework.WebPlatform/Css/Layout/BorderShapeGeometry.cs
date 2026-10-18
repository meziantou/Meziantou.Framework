using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The geometry of the border of a box shaped by <c>border-shape</c> (https://drafts.csswg.org/css-borders-4/#border-shape),
/// in the coordinates of the fragment (the origin is the top left corner of its border box):
/// <list type="bullet">
/// <item>one shape: the border is the stroke of the shape, of the width of the relevant side of the box
/// (https://drafts.csswg.org/css-borders-4/#relevant-side-for-border-shape); the reference box of the shape is the
/// <c>half-border-box</c> by default, so the stroke covers the borders of the box;</item>
/// <item>two shapes: the border is the area between the first shape (in the border box by default) and the second one (in
/// the padding box by default).</item>
/// </list>
/// The outer path is the outer edge of the border (the shape and its stroke, or the first shape), the inner path its inner
/// edge (the shape without its stroke, or the second shape): the shadows and the outline follow the outer path, the inset
/// shadows and the overflow clip the inner path. The specification does not define the joins of the stroke: as in Blink
/// (and the tests), they are miter joins, without miter limit when the shape has only straight segments, and with the
/// default limit of the strokes (4) when it has curves. The geometry of a fragment is cached while its style does not change.
/// </summary>
internal sealed class BorderShapeGeometry
{
    private static readonly ConditionalWeakTable<BoxFragment, BorderShapeGeometry> Cache = [];

    // The miter limit of the shapes made of straight segments: their sharp corners are not cut into bevels
    private const double PolygonMiterLimit = 1e10;

    private readonly ComputedStyle _style;
    private readonly PhysicalSize _size;
    private DisplayPath? _outerPath;
    private DisplayPath? _innerPath;

    private BorderShapeGeometry(ComputedStyle style, PhysicalSize size, DisplayPath shape, DisplayPath? innerShape, PhysicalSide relevantSide, double strokeWidth)
    {
        _style = style;
        _size = size;
        Shape = shape;
        InnerShape = innerShape;
        RelevantSide = relevantSide;
        StrokeWidth = innerShape is null ? strokeWidth : 0;
        Stroke = new StrokeStyle(StrokeWidth, LineCap.Butt, LineJoin.Miter, PathOperations.HasOnlyLines(shape) ? PolygonMiterLimit : 4);
    }

    /// <summary>The first shape: the path that is stroked, or the outer edge of the border when there are two shapes.</summary>
    public DisplayPath Shape { get; }

    /// <summary>The second shape (the inner edge of the border), or null when the border is the stroke of <see cref="Shape"/>.</summary>
    public DisplayPath? InnerShape { get; }

    /// <summary>The side of the box whose border color (and width, for a stroke) is the one of the border.</summary>
    public PhysicalSide RelevantSide { get; }

    /// <summary>The width of the stroke of <see cref="Shape"/> (0 when there are two shapes, or when the relevant side has no border).</summary>
    public double StrokeWidth { get; }

    /// <summary>The properties of the stroke of <see cref="Shape"/> (its joins are also the ones of the outline and of the spread of the shadows).</summary>
    public StrokeStyle Stroke { get; }

    /// <summary>
    /// The path the backgrounds are clipped to: the second shape, or the stroked shape (the middle of the border). The
    /// specification does not define it; this is the behavior of Blink (and of the tests).
    /// </summary>
    public DisplayPath BackgroundPath => InnerShape ?? Shape;

    /// <summary>The outer edge of the border: the first shape, with its stroke when it is stroked.</summary>
    public DisplayPath OuterPath => _outerPath ??= InnerShape is null ? PathOperations.Offset(Shape, StrokeWidth / 2, Stroke) : Shape;

    /// <summary>The inner edge of the border: the second shape, or the first shape without its stroke.</summary>
    public DisplayPath InnerPath => _innerPath ??= InnerShape ?? PathOperations.Offset(Shape, -StrokeWidth / 2, Stroke);

    /// <summary>Returns the outer edge of the border moved outwards by a distance (the spread of a shadow, the outline), or inwards when it is negative.</summary>
    public DisplayPath GetOuterPath(double distance) => InnerShape is null
        ? PathOperations.Offset(Shape, distance + (StrokeWidth / 2), Stroke)
        : PathOperations.Offset(Shape, distance, Stroke);

    /// <summary>Returns the inner edge of the border moved outwards by a distance, or inwards when it is negative (the spread of an inset shadow).</summary>
    public DisplayPath GetInnerPath(double distance) => InnerShape is null
        ? PathOperations.Offset(Shape, distance - (StrokeWidth / 2), Stroke)
        : PathOperations.Offset(InnerShape, distance, Stroke);

    /// <summary>
    /// Returns the path the contents of the box are clipped to when it clips its overflow
    /// (https://drafts.csswg.org/css-borders-4/#border-shape-overflow-interaction: "The inner border-shape clips the overflow
    /// content of the element"): the inner edge of the border, or the edge of the box of <c>overflow-clip-margin</c> (the outer
    /// edge for the border box) moved outwards by its length.
    /// </summary>
    public DisplayPath GetOverflowClip(BoxFragment fragment)
    {
        if (fragment.IsScrollContainer)
            return InnerPath;

        var margins = fragment.Box.Style.Layout.Overflow.OverflowClipMargin;
        var length = Math.Min(Math.Min(margins.Top.Length, margins.Bottom.Length), Math.Min(margins.Left.Length, margins.Right.Length));
        return margins.Top.Box == VisualBox.BorderBox ? GetOuterPath(length) : GetInnerPath(length);
    }

    /// <summary>Returns the bounds of the area painted by the border.</summary>
    public PhysicalRect GetBounds() => OuterPath.IsEmpty ? PhysicalRect.Empty : OuterPath.GetBounds();

    /// <summary>Whether the computed value of <c>border-shape</c> of a style is not <c>none</c>.</summary>
    public static bool HasBorderShape(ComputedStyle style) => style.GetComputedValue("border-shape") is not ("none" or "");

    /// <summary>Returns the geometry of the shaped border of a fragment of a box, or null when its <c>border-shape</c> is <c>none</c> (or is not valid).</summary>
    public static BorderShapeGeometry? Get(BoxFragment fragment)
    {
        var style = fragment.Box.Style;
        var value = style.GetComputedValue("border-shape");
        if (value is "none" or "")
            return null;

        if (Cache.TryGetValue(fragment, out var cached) && ReferenceEquals(cached._style, style) && cached._size == fragment.Size)
            return cached;

        var geometry = Create(fragment, style, value);
        if (geometry is not null)
        {
            Cache.AddOrUpdate(fragment, geometry);
        }

        return geometry;
    }

    private static BorderShapeGeometry? Create(BoxFragment fragment, ComputedStyle style, string value)
    {
        var shapes = new List<(CssFunction Shape, string? Box)>(2);
        foreach (var component in ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)))
        {
            if (component is CssFunction function)
            {
                shapes.Add((function, null));
            }
            else if (component is CssPreservedToken { Type: CssTokenType.Ident } ident && shapes.Count > 0 && shapes[^1].Box is null)
            {
                shapes[^1] = (shapes[^1].Shape, InfraAscii.ToAsciiLowercase(ident.Token.Value));
            }
            else
            {
                return null;
            }
        }

        if (shapes.Count is not (1 or 2))
            return null;

        // https://drafts.csswg.org/css-borders-4/#border-shape-geometry-box
        var zoom = style.Layout.EffectiveZoom;
        var isStroked = shapes.Count == 1;
        if (CreatePath(shapes[0], isStroked ? "half-border-box" : "border-box") is not { } shape)
            return null;

        DisplayPath? innerShape = null;
        if (!isStroked)
        {
            innerShape = CreatePath(shapes[1], "padding-box");
            if (innerShape is null)
                return null;
        }

        var side = GetRelevantSide(fragment, style);
        return new BorderShapeGeometry(style, fragment.Size, shape, innerShape, side, fragment.Border.Get(side));

        DisplayPath? CreatePath((CssFunction Shape, string? Box) item, string defaultBox)
            => BasicShapePaths.CreatePath(item.Shape, BasicShapePaths.GetReferenceBox(fragment, item.Box ?? defaultBox), zoom);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-borders-4/#relevant-side-for-border-shape: "the first side (in the order block-start,
    /// inline-start, block-end, and inline-end) that has a non-none border style, or block-start if they're all none".
    /// </summary>
    private static PhysicalSide GetRelevantSide(BoxFragment fragment, ComputedStyle style)
    {
        var direction = fragment.WritingDirection;
        var styles = style.Layout.Box.BorderStyle;
        ReadOnlySpan<PhysicalSide> sides = [direction.BlockStart, direction.InlineStart, direction.BlockEnd, direction.InlineEnd];
        foreach (var side in sides)
        {
            if (styles.Get(side) != BorderStyle.None)
                return side;
        }

        return direction.BlockStart;
    }
}
