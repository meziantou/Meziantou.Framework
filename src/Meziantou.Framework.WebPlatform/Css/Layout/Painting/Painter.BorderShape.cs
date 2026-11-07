using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The boxes shaped by border-shape (https://drafts.csswg.org/css-borders-4/#border-shape): their border, shadows and outline
// follow the paths of BorderShapeGeometry instead of the edges of the box.
internal sealed partial class Painter
{
    /// <summary>
    /// Paints the border of a box shaped by <c>border-shape</c> (https://drafts.csswg.org/css-borders-4/#border-shape-syntax):
    /// the stroke of the shape, of the width of the relevant side, or the area between the two shapes, in the border color of
    /// the relevant side. The border styles are not applied (the stroke is solid, as in Blink), and the area between two
    /// shapes is filled whatever the border widths.
    /// </summary>
    private void PaintShapedBorder(BoxFragment fragment, BorderShapeGeometry shape, PhysicalOffset origin)
    {
        var color = GetColor(fragment.Box.Style, shape.RelevantSide switch
        {
            PhysicalSide.Top => "border-top-color",
            PhysicalSide.Right => "border-right-color",
            PhysicalSide.Bottom => "border-bottom-color",
            _ => "border-left-color",
        });
        if (!color.IsTransparent)
        {
            FillBorderArea(shape, origin, color, fragment.Box.Element);
        }
    }

    /// <summary>Fills the area of a border shaped by <c>border-shape</c> with a color (the border, or the mask of <c>background-clip: border-area</c>).</summary>
    private void FillBorderArea(BorderShapeGeometry shape, PhysicalOffset origin, DisplayColor color, Dom.Node? source)
    {
        if (shape.InnerShape is not null)
        {
            FillRing(PathOperations.Translate(shape.Shape, origin), PathOperations.Translate(shape.InnerShape, origin), new DisplayPaint(color), source);
        }
        else if (shape.StrokeWidth > 0 && !shape.OuterPath.IsEmpty)
        {
            if (shape.OuterPath.RoundedRect is { } outer && HasCircularCorners(outer))
            {
                // The stroke of a rectangle with circular corners (or of a circle) is the ring of a border of the width of the stroke
                var color4 = new Sides<DisplayColor>(color, color, color, color);
                BorderPainter.Paint(_builder, outer.Translate(origin), Uniform(shape.StrokeWidth), new Sides<BorderStyle>(BorderStyle.Solid, BorderStyle.Solid, BorderStyle.Solid, BorderStyle.Solid), color4, source, _devicePixelRatio);
            }
            else
            {
                _builder.Add(new DrawPathItem(PathOperations.Translate(shape.Shape, origin), DisplayPaint.ForStroke(color, shape.Stroke)) { Source = source });
            }
        }

        static bool HasCircularCorners(RoundedRect rounded)
            => IsCircular(rounded.Radii.TopLeft) && IsCircular(rounded.Radii.TopRight) && IsCircular(rounded.Radii.BottomRight) && IsCircular(rounded.Radii.BottomLeft);

        static bool IsCircular((double X, double Y) radius) => radius.X <= 0 || radius.Y <= 0 || Math.Abs(radius.X - radius.Y) < 1e-6;
    }

    /// <summary>
    /// Returns the bounds of the box decorations of a fragment that paint over what is below it (in the coordinates of the
    /// fragment), or null when it has none: its border box when it has a background or a border, or the bounds of its border
    /// shaped by <c>border-shape</c>, and the bounds of its outer shadows (the blur extends them by three standard
    /// deviations). They are the area of an occluder of IntersectionObserver.
    /// </summary>
    internal static PhysicalRect? GetDecorationBounds(BoxFragment fragment)
    {
        var style = fragment.Box.Style;
        var shape = BorderShapeGeometry.Get(fragment);
        var edge = shape is null ? fragment.BorderBoxRect : shape.GetBounds();
        PhysicalRect? bounds = null;
        var hasBorder = shape is { InnerShape: not null } || fragment.Border != default;
        if ((hasBorder || HasBackground(style)) && (shape is null || !edge.IsEmpty))
        {
            bounds = edge;
        }

        if (GetShadows(style, "box-shadow") is { } shadows)
        {
            foreach (var shadow in shadows)
            {
                if (shadow.Inset || shadow.Color.IsTransparent)
                    continue;

                var extent = shadow.Spread + (shadow.Blur * 1.5);
                var rect = new PhysicalRect(edge.X + shadow.X - extent, edge.Y + shadow.Y - extent, edge.Width + (2 * extent), edge.Height + (2 * extent));
                if (rect.Width > 0 && rect.Height > 0)
                {
                    bounds = bounds is { } current ? UnionBounds(current, rect) : rect;
                }
            }
        }

        return bounds;

        static PhysicalRect UnionBounds(PhysicalRect a, PhysicalRect b)
        {
            var left = Math.Min(a.Left, b.Left);
            var top = Math.Min(a.Top, b.Top);
            return new PhysicalRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
        }
    }

    /// <summary>
    /// Paints the shadows of a box shaped by <c>border-shape</c> (https://drafts.csswg.org/css-borders-4/#border-shape-shadow-interaction):
    /// "An outer box-shadow is cast as if the shape defined by the outer path were opaque. An inner box-shadow is cast as if
    /// everything outside the shape defined by the inner path were opaque. [...] the shadow shape is expanded or contracted by
    /// the spread distance, blurred by the blur radius, and then clipped by the border-shape".
    /// </summary>
    private void PaintShapedBoxShadows(BoxFragment fragment, BorderShapeGeometry shape, List<Shadow> shadows, PhysicalOffset origin, bool inset)
    {
        var source = fragment.Box.Element;
        var edge = inset ? shape.InnerPath : shape.OuterPath;
        if (inset && edge.IsEmpty)
            return;

        var edgeBounds = edge.IsEmpty ? new PhysicalRect(origin, default) : edge.GetBounds().Translate(origin);

        // As for a box without shaped border (see PaintBoxShadows), when the background covers the outer path with an opaque color
        // (a shape without stroke inside of the border box: the background is clipped to the shape), the outer shadows are
        // clipped out one pixel inside of the path, where the background hides them: nothing shows through between the
        // antialiased edges of the shadow and of the background
        var knockout = edge;
        if (!inset && !edge.IsEmpty && shape.InnerShape is null && shape.StrokeWidth == 0 && HasOpaqueBorderBoxBackground(fragment)
            && Contains(new PhysicalRect(origin, fragment.Size), edgeBounds))
        {
            knockout = shape.GetOuterPath(-1);
        }

        for (var i = shadows.Count - 1; i >= 0; i--)
        {
            var shadow = shadows[i];
            if (shadow.Inset != inset || shadow.Color.IsTransparent)
                continue;

            var paint = new DisplayPaint(shadow.Color) { BlurSigma = shadow.Blur / 2 };
            var extent = (shadow.Blur * 1.5) + Math.Abs(shadow.Spread) + Math.Abs(shadow.X) + Math.Abs(shadow.Y) + 1;
            var offset = origin + new PhysicalOffset(shadow.X, shadow.Y);
            if (!inset)
            {
                var caster = PathOperations.Translate(shape.GetOuterPath(shadow.Spread), offset);
                if (caster.IsEmpty)
                    continue;

                // The shadow is clipped out of the outer path
                _builder.Save();
                if (!knockout.IsEmpty)
                {
                    _builder.Add(new ClipPathItem(CreateComplement(Inflate(edgeBounds.Union(caster.GetBounds()), extent), PathOperations.Translate(knockout, origin))));
                }

                FillPath(caster, paint, source);
                _builder.Restore();
            }
            else
            {
                // The area of the inner path outside of the hole: the inner path shrunk by the spread distance and offset
                _builder.Save();
                ClipToPath(PathOperations.Translate(edge, origin));
                var hole = PathOperations.Translate(shape.GetInnerPath(-shadow.Spread), offset);
                _builder.Add(new DrawPathItem(CreateComplement(Inflate(edgeBounds, extent), hole), paint) { Source = source });
                _builder.Restore();
            }
        }
    }

    /// <summary>
    /// Paints the outline of a box shaped by <c>border-shape</c>: it follows the outer edge of the border, at the distance of
    /// <c>outline-offset</c> (as in Blink; the specification does not define it). A double outline is two bands of a third of
    /// its width, a dotted or dashed one a stroke along the middle of the outline; the other styles are solid.
    /// </summary>
    private void PaintShapedOutline(BoxFragment fragment, BorderShapeGeometry shape, PhysicalOffset origin, double width, double outlineOffset, BorderStyle lineStyle, DisplayColor color)
    {
        if (color.IsTransparent || lineStyle is BorderStyle.None or BorderStyle.Hidden)
            return;

        var source = fragment.Box.Element;
        if (shape.GetOuterPath(outlineOffset + width).RoundedRect is { } rounded)
        {
            // The outline of a rounded rectangle is the one of a box with border radii, with all its styles
            BorderPainter.Paint(_builder, rounded.Translate(origin), Uniform(width), new Sides<BorderStyle>(lineStyle, lineStyle, lineStyle, lineStyle), new Sides<DisplayColor>(color, color, color, color), source, _devicePixelRatio);
            return;
        }

        var paint = new DisplayPaint(color);
        switch (lineStyle)
        {
            case BorderStyle.Double when Math.Round(width / 3, MidpointRounding.AwayFromZero) >= 1:
            {
                var band = Math.Round(width / 3, MidpointRounding.AwayFromZero);
                PaintBand(outlineOffset + width, outlineOffset + width - band);
                PaintBand(outlineOffset + band, outlineOffset);
                break;
            }

            case BorderStyle.Dotted or BorderStyle.Dashed:
            {
                var middle = PathOperations.Translate(shape.GetOuterPath(outlineOffset + (width / 2)), origin);
                if (!middle.IsEmpty)
                {
                    var stroke = lineStyle == BorderStyle.Dotted
                        ? new StrokeStyle(width, LineCap.Round, LineJoin.Round, Dashes: [0, 2 * width])
                        : new StrokeStyle(width, LineCap.Butt, LineJoin.Miter, shape.Stroke.MiterLimit, Dashes: [3 * width, 3 * width]);
                    _builder.Add(new DrawPathItem(middle, DisplayPaint.ForStroke(color, stroke)) { Source = source });
                }

                break;
            }

            default:
                PaintBand(outlineOffset + width, outlineOffset);
                break;
        }

        void PaintBand(double outer, double inner)
        {
            var outerPath = PathOperations.Translate(shape.GetOuterPath(outer), origin);
            if (!outerPath.IsEmpty)
            {
                FillRing(outerPath, PathOperations.Translate(shape.GetOuterPath(inner), origin), paint, source);
            }
        }
    }

    /// <summary>
    /// Fills the area inside of a path and outside of another one: the outside of the inner path, clipped to the outer one (the
    /// curves of the outer path, and the ones of an inner rounded rectangle, are not flattened).
    /// </summary>
    private void FillRing(DisplayPath outer, DisplayPath inner, DisplayPaint paint, Dom.Node? source)
    {
        if (outer.IsEmpty)
            return;

        if (inner.IsEmpty)
        {
            FillPath(outer, paint, source);
            return;
        }

        if (outer.RoundedRect is { Contour: null } outerRect && inner.RoundedRect is { Contour: null } innerRect && Contains(outer, innerRect))
        {
            // The ring between two rounded rectangles (e.g. two circles), as the border of a box with border radii
            _builder.Add(new DrawRingItem(outerRect, innerRect, paint) { Source = source });
            return;
        }

        _builder.Save();
        ClipToPath(outer);
        _builder.Add(new DrawPathItem(CreateComplement(Inflate(outer.GetBounds(), 1), inner), paint) { Source = source });
        _builder.Restore();

        // Whether a rounded rectangle is inside of a path: the ends and the middle of the curves of its corners are
        static bool Contains(DisplayPath path, RoundedRect rounded)
        {
            const double Diagonal = 0.2928932188134524;
            var rect = rounded.Rect;
            var radii = rounded.Radii;
            ReadOnlySpan<PhysicalOffset> points =
            [
                new(rect.Left + radii.TopLeft.X, rect.Top), new(rect.Left, rect.Top + radii.TopLeft.Y), new(rect.Left + (radii.TopLeft.X * Diagonal), rect.Top + (radii.TopLeft.Y * Diagonal)),
                new(rect.Right - radii.TopRight.X, rect.Top), new(rect.Right, rect.Top + radii.TopRight.Y), new(rect.Right - (radii.TopRight.X * Diagonal), rect.Top + (radii.TopRight.Y * Diagonal)),
                new(rect.Right - radii.BottomRight.X, rect.Bottom), new(rect.Right, rect.Bottom - radii.BottomRight.Y), new(rect.Right - (radii.BottomRight.X * Diagonal), rect.Bottom - (radii.BottomRight.Y * Diagonal)),
                new(rect.Left + radii.BottomLeft.X, rect.Bottom), new(rect.Left, rect.Bottom - radii.BottomLeft.Y), new(rect.Left + (radii.BottomLeft.X * Diagonal), rect.Bottom - (radii.BottomLeft.Y * Diagonal)),
            ];
            foreach (var point in points)
            {
                if (!PathOperations.Contains(path, point))
                    return false;
            }

            return true;
        }
    }

    /// <summary>Intersects the clip with the area of a path (as a rounded rectangle when it is one).</summary>
    private void ClipToPath(DisplayPath path)
    {
        if (path.RoundedRect is { Contour: null } rounded)
        {
            _builder.Add(rounded.IsRect ? new ClipRectItem(rounded.Rect, AntiAlias: true) : new ClipRoundedRectItem(rounded));
        }
        else
        {
            _builder.Add(new ClipPathItem(path));
        }
    }

    /// <summary>Fills a path (as a rounded rectangle when it is one: the backends draw and blur them exactly).</summary>
    private void FillPath(DisplayPath path, DisplayPaint paint, Dom.Node? source)
    {
        if (path.RoundedRect is { Contour: null } rounded)
        {
            FillRoundedRect(rounded, paint, source);
        }
        else if (!path.IsEmpty)
        {
            _builder.Add(new DrawPathItem(path, paint) { Source = source });
        }
    }

    /// <summary>Creates a path that fills the area of <paramref name="bounds"/> outside of a path.</summary>
    private static DisplayPath CreateComplement(PhysicalRect bounds, DisplayPath path)
    {
        if (path.RoundedRect is { } rounded)
            return DisplayPath.CreateComplement(bounds, rounded);

        var area = new DisplayPath();
        area.AddRect(bounds);
        return path.IsEmpty ? area : PathOperations.Combine(area, path, PathOperation.Difference);
    }
}
