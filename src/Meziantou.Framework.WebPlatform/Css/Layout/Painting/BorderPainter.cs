using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// Paints the borders of a box, its outline and the column rules (https://drafts.csswg.org/css-backgrounds/#borders): the area
/// between the border edge and the padding edge, rounded by the border radii, with the style and the color of each side.
/// </summary>
/// <remarks>
/// A border whose four sides have the same style and color is one ring. Otherwise each side is painted in the trapezoid of its
/// side (https://drafts.csswg.org/css-backgrounds/#corner-transitions: the transition between two sides is on the line between
/// the outer corner and the inner corner): the transition between two colors is antialiased (when both are opaque, the side
/// painted first covers the whole corner, so the background does not show through the diagonal), and the one between two sides
/// of the same color is not (each pixel belongs to one side, without seam). <c>dotted</c> and <c>dashed</c> are strokes along the middle of each side (the
/// curves of the rounded corners are not dashed), <c>double</c> is two rings, <c>groove</c>, <c>ridge</c>, <c>inset</c> and
/// <c>outset</c> darken the colors of some sides. The dashes are adjusted to the length of the side as in the browsers
/// (<see cref="GetDashes"/>); the curves of the rounded corners are not dashed. The lines of a <c>double</c> side and the
/// halves of a <c>groove</c> or <c>ridge</c> side are whole numbers of device pixels, and a line paints the same pixels as
/// any side of a box and as a rule (see docs/rendering.md, Pixel snapping).
/// </remarks>
internal static class BorderPainter
{
    /// <summary>Paints a border.</summary>
    /// <param name="builder">The display list.</param>
    /// <param name="outer">The border edge, with the border radii.</param>
    /// <param name="widths">The widths of the sides.</param>
    /// <param name="styles">The styles of the sides.</param>
    /// <param name="colors">The colors of the sides.</param>
    /// <param name="source">The element of the border.</param>
    /// <param name="devicePixelRatio">The number of device pixels per CSS pixel, for the parts of the sides that are snapped to the device pixels.</param>
    public static void Paint(DisplayListBuilder builder, RoundedRect outer, Sides<double> widths, Sides<BorderStyle> styles, Sides<DisplayColor> colors, Node? source, double devicePixelRatio)
    {
        var top = IsPainted(widths.Top, styles.Top, colors.Top);
        var right = IsPainted(widths.Right, styles.Right, colors.Right);
        var bottom = IsPainted(widths.Bottom, styles.Bottom, colors.Bottom);
        var left = IsPainted(widths.Left, styles.Left, colors.Left);
        if (!top && !right && !bottom && !left)
            return;

        var inner = outer.Inset(widths);

        // A uniform border is one ring
        if (top && right && bottom && left && styles.Top == styles.Right && styles.Top == styles.Bottom && styles.Top == styles.Left
            && colors.Top == colors.Right && colors.Top == colors.Bottom && colors.Top == colors.Left && styles.Top is BorderStyle.Solid or BorderStyle.Double)
        {
            PaintRing(builder, outer, inner, widths, styles.Top, colors.Top, source, devicePixelRatio);
            return;
        }

        var painted = new Sides<bool>(top, right, bottom, left);
        if (outer.Contour is not null)
        {
            PaintShapedSides(builder, outer, inner, widths, styles, colors, painted, GetMiter, source, devicePixelRatio);
            return;
        }

        if (top)
        {
            PaintSide(builder, PhysicalSide.Top, outer, inner, widths, styles.Top, colors.Top, GetMiter(PhysicalSide.Top, PhysicalSide.Left), GetMiter(PhysicalSide.Top, PhysicalSide.Right), source, devicePixelRatio);
        }

        if (right)
        {
            PaintSide(builder, PhysicalSide.Right, outer, inner, widths, styles.Right, colors.Right, GetMiter(PhysicalSide.Right, PhysicalSide.Top), GetMiter(PhysicalSide.Right, PhysicalSide.Bottom), source, devicePixelRatio);
        }

        if (bottom)
        {
            PaintSide(builder, PhysicalSide.Bottom, outer, inner, widths, styles.Bottom, colors.Bottom, GetMiter(PhysicalSide.Bottom, PhysicalSide.Right), GetMiter(PhysicalSide.Bottom, PhysicalSide.Left), source, devicePixelRatio);
        }

        if (left)
        {
            PaintSide(builder, PhysicalSide.Left, outer, inner, widths, styles.Left, colors.Left, GetMiter(PhysicalSide.Left, PhysicalSide.Bottom), GetMiter(PhysicalSide.Left, PhysicalSide.Top), source, devicePixelRatio);
        }

        // The join of a side with an adjacent side, as in Blink (the miters of BoxBorderPainter): two antialiased diagonals would
        // let the background show through the seam
        Miter GetMiter(PhysicalSide side, PhysicalSide adjacent)
        {
            if (!Get(painted, adjacent))
                return Miter.Soft;

            var color = Get(colors, side);
            var adjacentColor = Get(colors, adjacent);
            var adjacentStyle = Get(styles, adjacent);
            if (color == adjacentColor && !HasUnmatchedColorsAtCorner(Get(styles, side), side, adjacent) && !HasUnmatchedColorsAtCorner(adjacentStyle, side, adjacent))
                return Miter.Hard;

            // The sides are painted in the order of PhysicalSide (top, right, bottom, left)
            if (side < adjacent && color.A >= 1 && adjacentColor.A >= 1 && adjacentStyle is BorderStyle.Solid or BorderStyle.Inset or BorderStyle.Outset or BorderStyle.Groove or BorderStyle.Ridge)
                return Miter.None;

            return Miter.Soft;
        }
    }

    /// <summary>
    /// Paints the sides of a border whose corners are shaped by <c>corner-shape</c>
    /// (https://drafts.csswg.org/css-borders-4/#corner-shape-rendering: "An element's border is rendered in the area between
    /// its outer contour and its inner contour"). The area of a side is not its trapezoid: the inner contour of a concave
    /// corner extends in the padding box. The painted sides of one solid color are one ring; otherwise each side is painted
    /// between the lines of the diagonals of its two corners (from the corner of the border edge through the corner of the
    /// padding edge).
    /// </summary>
    private static void PaintShapedSides(DisplayListBuilder builder, RoundedRect outer, RoundedRect inner, Sides<double> widths, Sides<BorderStyle> styles, Sides<DisplayColor> colors, Sides<bool> painted, Func<PhysicalSide, PhysicalSide, Miter> getMiter, Node? source, double devicePixelRatio)
    {
        ReadOnlySpan<PhysicalSide> sides = [PhysicalSide.Top, PhysicalSide.Right, PhysicalSide.Bottom, PhysicalSide.Left];
        PhysicalSide? first = null;
        var isRing = true;
        foreach (var side in sides)
        {
            if (!Get(painted, side))
            {
                // A side that is not painted but has a width (a transparent side) is not a part of the ring
                isRing &= Get(widths, side) <= 0;
                continue;
            }

            first ??= side;
            isRing &= Get(styles, side) is BorderStyle.Solid or BorderStyle.Double && Get(styles, side) == Get(styles, first.Value) && Get(colors, side) == Get(colors, first.Value);
        }

        if (isRing && first is { } ringSide)
        {
            PaintRing(builder, outer, inner, widths, Get(styles, ringSide), Get(colors, ringSide), source, devicePixelRatio);
            return;
        }

        var o = outer.Rect;
        var i = inner.Rect;
        var extent = 4 * (o.Width + o.Height + 1);
        foreach (var side in sides)
        {
            if (!Get(painted, side))
                continue;

            // Clockwise: the corners of the border edge and of the padding edge at the start and at the end of the side
            var (outerStart, outerEnd, innerStart, innerEnd, previous, next) = side switch
            {
                PhysicalSide.Top => (new PhysicalOffset(o.Left, o.Top), new PhysicalOffset(o.Right, o.Top), new PhysicalOffset(i.Left, i.Top), new PhysicalOffset(i.Right, i.Top), PhysicalSide.Left, PhysicalSide.Right),
                PhysicalSide.Right => (new PhysicalOffset(o.Right, o.Top), new PhysicalOffset(o.Right, o.Bottom), new PhysicalOffset(i.Right, i.Top), new PhysicalOffset(i.Right, i.Bottom), PhysicalSide.Top, PhysicalSide.Bottom),
                PhysicalSide.Bottom => (new PhysicalOffset(o.Right, o.Bottom), new PhysicalOffset(o.Left, o.Bottom), new PhysicalOffset(i.Right, i.Bottom), new PhysicalOffset(i.Left, i.Bottom), PhysicalSide.Right, PhysicalSide.Left),
                _ => (new PhysicalOffset(o.Left, o.Bottom), new PhysicalOffset(o.Left, o.Top), new PhysicalOffset(i.Left, i.Bottom), new PhysicalOffset(i.Left, i.Top), PhysicalSide.Bottom, PhysicalSide.Top),
            };

            builder.Save();
            AddDiagonalClip(outerStart, innerStart, outerEnd, getMiter(side, previous));
            AddDiagonalClip(outerEnd, innerEnd, outerStart, getMiter(side, next));

            // The side ends where the diagonals of the two corners of the opposite side cross the ones of its corners: at the
            // distance from the side that is proportional to its width (the middle of the box for two sides of one width)
            var width = Get(widths, side);
            var opposite = side switch
            {
                PhysicalSide.Top => PhysicalSide.Bottom,
                PhysicalSide.Right => PhysicalSide.Left,
                PhysicalSide.Bottom => PhysicalSide.Top,
                _ => PhysicalSide.Right,
            };

            if (Get(painted, opposite) && width + Get(widths, opposite) > 0)
            {
                var ratio = width / (width + Get(widths, opposite));
                var half = side switch
                {
                    PhysicalSide.Top => new PhysicalRect(o.Left - extent, o.Top - extent, o.Width + (2 * extent), extent + (o.Height * ratio)),
                    PhysicalSide.Right => new PhysicalRect(o.Right - (o.Width * ratio), o.Top - extent, extent + (o.Width * ratio), o.Height + (2 * extent)),
                    PhysicalSide.Bottom => new PhysicalRect(o.Left - extent, o.Bottom - (o.Height * ratio), o.Width + (2 * extent), extent + (o.Height * ratio)),
                    _ => new PhysicalRect(o.Left - extent, o.Top - extent, extent + (o.Width * ratio), o.Height + (2 * extent)),
                };

                builder.Add(new ClipRectItem(half, AntiAlias: Get(colors, side) != Get(colors, opposite)) { Source = source });
            }
            var style = Get(styles, side);
            if (style is BorderStyle.Dotted or BorderStyle.Dashed)
            {
                // The dashes are straight lines along the side: only their part in the border area is painted
                builder.Add(new ClipRoundedRectItem(outer) { Source = source });
                builder.Add(new ClipPathItem(DisplayPath.CreateComplement(new PhysicalRect(o.X - 1, o.Y - 1, o.Width + 2, o.Height + 2), inner)) { Source = source });
            }

            PaintSideStyle(builder, side, outer, inner, widths, style, Get(colors, side), source, devicePixelRatio);
            builder.Restore();
        }

        // Clips to the half-plane of the diagonal of a corner that contains the other corner of the side. A side that covers the
        // whole corner (the adjacent side is opaque and painted after it) is not clipped
        void AddDiagonalClip(PhysicalOffset corner, PhysicalOffset innerCorner, PhysicalOffset otherCorner, Miter miter)
        {
            if (miter == Miter.None)
                return;

            var directionX = innerCorner.X - corner.X;
            var directionY = innerCorner.Y - corner.Y;
            var length = Math.Sqrt((directionX * directionX) + (directionY * directionY));
            if (length <= 0)
                return;

            directionX *= extent / length;
            directionY *= extent / length;
            var normalX = -directionY;
            var normalY = directionX;
            var side = (normalX * (otherCorner.X - corner.X)) + (normalY * (otherCorner.Y - corner.Y));
            if (side == 0)
                return;

            if (side < 0)
            {
                normalX = -normalX;
                normalY = -normalY;
            }

            var clip = new DisplayPath();
            clip.AddQuad(
                new PhysicalOffset(corner.X - directionX, corner.Y - directionY),
                new PhysicalOffset(corner.X + directionX, corner.Y + directionY),
                new PhysicalOffset(corner.X + directionX + normalX, corner.Y + directionY + normalY),
                new PhysicalOffset(corner.X - directionX + normalX, corner.Y - directionY + normalY));
            builder.Add(new ClipPathItem(clip, AntiAlias: miter != Miter.Hard) { Source = source });
        }
    }

    /// <summary>The join of a side of a border with an adjacent side.</summary>
    private enum Miter
    {
        /// <summary>The side ends at the diagonal of the corner, antialiased: the transition between two colors.</summary>
        Soft,

        /// <summary>
        /// The side ends at the diagonal of the corner, not antialiased: the two sides have the same color, and each pixel of the
        /// diagonal is painted by one of them.
        /// </summary>
        Hard,

        /// <summary>
        /// The side covers the whole corner: the adjacent side is opaque and painted after it over the corner, up to its
        /// antialiased diagonal (the pixels of the diagonal are a mix of the two colors, without the background).
        /// </summary>
        None,
    }

    /// <summary>Returns whether a style paints two sides of the same color with different shades (the 3D styles, at the top right and bottom left corners).</summary>
    private static bool HasUnmatchedColorsAtCorner(BorderStyle style, PhysicalSide side, PhysicalSide adjacent)
        => style is BorderStyle.Inset or BorderStyle.Outset or BorderStyle.Groove or BorderStyle.Ridge
            && ((side is PhysicalSide.Top or PhysicalSide.Left) != (adjacent is PhysicalSide.Top or PhysicalSide.Left));

    /// <summary>Paints a column rule (https://drafts.csswg.org/css-multicol-1/#crs): a line of a style filling a rectangle, vertical between two columns of a horizontal multi-column container.</summary>
    public static void PaintRule(DisplayListBuilder builder, PhysicalRect rect, bool vertical, BorderStyle style, DisplayColor color, Node? source, double devicePixelRatio)
    {
        if (rect.IsEmpty)
            return;

        if (style == BorderStyle.Solid)
        {
            builder.Add(new DrawRectItem(rect, new DisplayPaint(color)) { Source = source });
            return;
        }

        var width = vertical ? rect.Width : rect.Height;
        var side = vertical ? PhysicalSide.Left : PhysicalSide.Top;
        var widths = vertical ? new Sides<double>(0, 0, 0, width) : new Sides<double>(width, 0, 0, 0);
        var outer = new RoundedRect(rect, default);

        // The rule is painted as the only side of a border (see PaintSide): the same operations, in the same clip, so that it
        // paints the same pixels (a backend may rasterize a clipped line differently)
        builder.Save();
        builder.Add(new ClipRectItem(rect, AntiAlias: true));
        PaintSideStyle(builder, side, outer, outer.Inset(widths), widths, style, color, source, devicePixelRatio);
        builder.Restore();
    }

    /// <summary>
    /// Returns the stroke of a dotted or dashed line of a width and a length, as in the browsers (Blink): a dash is 3 widths long
    /// (2 for the widths of 3px or more) with a gap of 2 widths (1), a dot is a circle of the width (a square below 3px) with a gap
    /// of one width, and the gaps are adjusted so the line starts and ends with a whole dash. Returns null when two dashes do not
    /// fit: the line is solid.
    /// </summary>
    internal static StrokeStyle? GetDashes(BorderStyle style, double width, double length)
    {
        var dash = width;
        var gap = width;
        if (style == BorderStyle.Dashed)
        {
            dash *= width >= 3 ? 2 : 3;
            gap *= width >= 3 ? 1 : 2;
        }

        if (length <= dash * 2)
            return null;

        // The gap that gives a whole number of dashes, the closest to the nominal gap
        var available = length + gap;
        var minDashes = Math.Floor(available / (dash + gap));
        var maxDashes = minDashes + 1;
        var minGap = (length - (minDashes * dash)) / Math.Max(1, minDashes - 1);
        var maxGap = (length - (maxDashes * dash)) / Math.Max(1, maxDashes - 1);
        var bestGap = maxGap <= 0 || Math.Abs(minGap - gap) < Math.Abs(maxGap - gap) ? minGap : maxGap;
        if (style == BorderStyle.Dotted && width >= 3)
        {
            // A dot is a round cap of a dash of length 0, centered on the position of the dash
            return new StrokeStyle(width, LineCap.Round, Dashes: [0, dash + bestGap], DashOffset: -dash / 2);
        }

        return new StrokeStyle(width, Dashes: [dash, bestGap]);
    }

    private static bool IsPainted(double width, BorderStyle style, DisplayColor color) => width > 0 && style is not (BorderStyle.None or BorderStyle.Hidden) && !color.IsTransparent;

    /// <summary>
    /// Paints a side of a border in its trapezoid, with the joins of its two corners (<paramref name="startMiter"/> for the
    /// corner before the side clockwise, <paramref name="endMiter"/> for the one after it).
    /// </summary>
    private static void PaintSide(DisplayListBuilder builder, PhysicalSide side, RoundedRect outer, RoundedRect inner, Sides<double> widths, BorderStyle style, DisplayColor color, Miter startMiter, Miter endMiter, Node? source, double devicePixelRatio)
    {
        var o = outer.Rect;
        var i = inner.Rect;

        // Clockwise: the outer corners of the side, its inner corners, and the projections of the outer corners on the inner
        // edge (the corners of the side without diagonal)
        var (outerStart, outerEnd, innerStart, innerEnd, squareStart, squareEnd) = side switch
        {
            PhysicalSide.Top => (new PhysicalOffset(o.Left, o.Top), new PhysicalOffset(o.Right, o.Top), new PhysicalOffset(i.Left, i.Top), new PhysicalOffset(i.Right, i.Top), new PhysicalOffset(o.Left, i.Top), new PhysicalOffset(o.Right, i.Top)),
            PhysicalSide.Right => (new PhysicalOffset(o.Right, o.Top), new PhysicalOffset(o.Right, o.Bottom), new PhysicalOffset(i.Right, i.Top), new PhysicalOffset(i.Right, i.Bottom), new PhysicalOffset(i.Right, o.Top), new PhysicalOffset(i.Right, o.Bottom)),
            PhysicalSide.Bottom => (new PhysicalOffset(o.Right, o.Bottom), new PhysicalOffset(o.Left, o.Bottom), new PhysicalOffset(i.Right, i.Bottom), new PhysicalOffset(i.Left, i.Bottom), new PhysicalOffset(o.Right, i.Bottom), new PhysicalOffset(o.Left, i.Bottom)),
            _ => (new PhysicalOffset(o.Left, o.Bottom), new PhysicalOffset(o.Left, o.Top), new PhysicalOffset(i.Left, i.Bottom), new PhysicalOffset(i.Left, i.Top), new PhysicalOffset(i.Left, o.Bottom), new PhysicalOffset(i.Left, o.Top)),
        };

        // The diagonal of a rounded corner extends to the chord of the curve of the padding edge (as in Blink), so the trapezoid
        // contains the part of the curve that belongs to the side
        var innerRadii = inner.Radii;
        var (startChord, endChord) = side switch
        {
            PhysicalSide.Top => (GetChord(i.Left, i.Top, innerRadii.TopLeft.X, innerRadii.TopLeft.Y), GetChord(i.Right, i.Top, -innerRadii.TopRight.X, innerRadii.TopRight.Y)),
            PhysicalSide.Right => (GetChord(i.Right, i.Top, -innerRadii.TopRight.X, innerRadii.TopRight.Y), GetChord(i.Right, i.Bottom, -innerRadii.BottomRight.X, -innerRadii.BottomRight.Y)),
            PhysicalSide.Bottom => (GetChord(i.Right, i.Bottom, -innerRadii.BottomRight.X, -innerRadii.BottomRight.Y), GetChord(i.Left, i.Bottom, innerRadii.BottomLeft.X, -innerRadii.BottomLeft.Y)),
            _ => (GetChord(i.Left, i.Bottom, innerRadii.BottomLeft.X, -innerRadii.BottomLeft.Y), GetChord(i.Left, i.Top, innerRadii.TopLeft.X, innerRadii.TopLeft.Y)),
        };

        innerStart = ExtendToChord(outerStart, innerStart, startChord);
        innerEnd = ExtendToChord(outerEnd, innerEnd, endChord);
        if (startChord is not null || endChord is not null)
        {
            // The corners of the side without diagonal are as deep as the diagonals
            (squareStart, squareEnd) = side switch
            {
                PhysicalSide.Top or PhysicalSide.Bottom => (new PhysicalOffset(outerStart.X, innerStart.Y), new PhysicalOffset(outerEnd.X, innerEnd.Y)),
                _ => (new PhysicalOffset(innerStart.X, outerStart.Y), new PhysicalOffset(innerEnd.X, outerEnd.Y)),
            };
        }

        var start = startMiter == Miter.None ? squareStart : innerStart;
        var end = endMiter == Miter.None ? squareEnd : innerEnd;

        // A corner without diagonal has the antialiasing of the other corner (its edges are on the edges of the box)
        var hardStart = startMiter == Miter.Hard || (startMiter == Miter.None && endMiter == Miter.Hard);
        var hardEnd = endMiter == Miter.Hard || (endMiter == Miter.None && startMiter == Miter.Hard);

        builder.Save();
        if (hardStart == hardEnd)
        {
            if ((outerStart.X == start.X && outerEnd.X == end.X && start.Y == end.Y) || (outerStart.Y == start.Y && outerEnd.Y == end.Y && start.X == end.X))
            {
                // A side without diagonal (its adjacent sides have no width): its trapezoid is a rectangle, clipped as a
                // rectangle (the clip of a path would antialias the pixels of its edges once more, even on the device pixels:
                // the side would not paint the same pixels as the same line painted as a rule)
                builder.Add(new ClipRectItem(new PhysicalRect(Math.Min(outerStart.X, end.X), Math.Min(outerStart.Y, end.Y), Math.Abs(end.X - outerStart.X), Math.Abs(end.Y - outerStart.Y)), AntiAlias: !hardStart));
            }
            else
            {
                var clip = new DisplayPath();
                clip.AddQuad(outerStart, outerEnd, end, start);
                builder.Add(new ClipPathItem(clip, AntiAlias: !hardStart));
            }
        }
        else
        {
            // One clip per diagonal: the trapezoid is their intersection (the clip of a diagonal does not limit the curve of the
            // other corner: it extends inside the padding edge)
            var depth = o.Width + o.Height;
            var inward = side switch
            {
                PhysicalSide.Top => new PhysicalOffset(0, depth),
                PhysicalSide.Right => new PhysicalOffset(-depth, 0),
                PhysicalSide.Bottom => new PhysicalOffset(0, -depth),
                _ => new PhysicalOffset(depth, 0),
            };

            var startClip = new DisplayPath();
            startClip.AddQuad(outerStart, outerEnd, endChord is null ? squareEnd : squareEnd + inward, start);
            builder.Add(new ClipPathItem(startClip, AntiAlias: !hardStart));
            var endClip = new DisplayPath();
            endClip.AddQuad(outerStart, outerEnd, end, startChord is null ? squareStart : squareStart + inward);
            builder.Add(new ClipPathItem(endClip, AntiAlias: !hardEnd));
        }

        PaintSideStyle(builder, side, outer, inner, widths, style, color, source, devicePixelRatio);
        builder.Restore();
    }

    /// <summary>Returns the chord of the curve of a corner of the padding edge (the two ends of the curve), or null for a square corner.</summary>
    private static (PhysicalOffset A, PhysicalOffset B)? GetChord(double x, double y, double radiusX, double radiusY)
        => radiusX == 0 || radiusY == 0 ? null : (new PhysicalOffset(x + radiusX, y), new PhysicalOffset(x, y + radiusY));

    /// <summary>Returns the intersection of the diagonal of a corner (from the corner of the border edge through the corner of the padding edge) with the chord of the curve of the padding edge.</summary>
    private static PhysicalOffset ExtendToChord(PhysicalOffset outerCorner, PhysicalOffset innerCorner, (PhysicalOffset A, PhysicalOffset B)? chord)
    {
        if (chord is not var (a, b))
            return innerCorner;

        var directionX = innerCorner.X - outerCorner.X;
        var directionY = innerCorner.Y - outerCorner.Y;
        var chordX = b.X - a.X;
        var chordY = b.Y - a.Y;
        var denominator = (directionX * chordY) - (directionY * chordX);
        if (Math.Abs(denominator) < 1e-9)
            return innerCorner;

        var t = (((a.X - outerCorner.X) * chordY) - ((a.Y - outerCorner.Y) * chordX)) / denominator;
        return t <= 1 ? innerCorner : new PhysicalOffset(outerCorner.X + (t * directionX), outerCorner.Y + (t * directionY));
    }

    /// <summary>Paints the style of a side (in the clip of its trapezoid).</summary>
    private static void PaintSideStyle(DisplayListBuilder builder, PhysicalSide side, RoundedRect outer, RoundedRect inner, Sides<double> widths, BorderStyle style, DisplayColor color, Node? source, double devicePixelRatio)
    {
        switch (style)
        {
            case BorderStyle.Dotted or BorderStyle.Dashed:
            {
                var width = GetWidth(widths, side);
                var o = outer.Rect;
                var half = width / 2;

                // The line is always drawn from its left or top end: the dashes are symmetric (the line starts and ends with a
                // whole dash), but the round caps of the dots are not rasterized the same way in the two directions, and the
                // same line must paint the same pixels as the top, right, bottom or left side of a box and as a rule
                var (start, end) = side switch
                {
                    PhysicalSide.Top => (new PhysicalOffset(o.Left, o.Top + half), new PhysicalOffset(o.Right, o.Top + half)),
                    PhysicalSide.Right => (new PhysicalOffset(o.Right - half, o.Top), new PhysicalOffset(o.Right - half, o.Bottom)),
                    PhysicalSide.Bottom => (new PhysicalOffset(o.Left, o.Bottom - half), new PhysicalOffset(o.Right, o.Bottom - half)),
                    _ => (new PhysicalOffset(o.Left + half, o.Top), new PhysicalOffset(o.Left + half, o.Bottom)),
                };

                var length = Math.Sqrt(((end.X - start.X) * (end.X - start.X)) + ((end.Y - start.Y) * (end.Y - start.Y)));
                if (GetDashes(style, width, length) is not { } stroke)
                {
                    // Too short for two dashes: a solid side
                    builder.Add(new DrawRingItem(outer, inner, new DisplayPaint(color)) { Source = source });
                    break;
                }

                builder.Add(new DrawLineItem(start, end, DisplayPaint.ForStroke(color, stroke)) { Source = source });
                break;
            }

            case BorderStyle.Double:
                PaintRing(builder, outer, inner, widths, style, color, source, devicePixelRatio);
                break;

            case BorderStyle.Groove or BorderStyle.Ridge:
            {
                // The outer half and the inner half of the side have the two shades (groove: the top-left outer half is dark)
                // The two halves of a side are whole numbers of device pixels: the top or left one is the larger one
                var middle = outer.Inset(new Sides<double>(
                    GetHalfWidth(widths.Top, devicePixelRatio, larger: true),
                    GetHalfWidth(widths.Right, devicePixelRatio, larger: false),
                    GetHalfWidth(widths.Bottom, devicePixelRatio, larger: false),
                    GetHalfWidth(widths.Left, devicePixelRatio, larger: true)));
                var topLeft = side is PhysicalSide.Top or PhysicalSide.Left;
                var outerDark = (style == BorderStyle.Groove) == topLeft;
                builder.Add(new DrawRingItem(outer, middle, new DisplayPaint(Shade(color, darker: outerDark))) { Source = source });
                builder.Add(new DrawRingItem(middle, inner, new DisplayPaint(Shade(color, darker: !outerDark))) { Source = source });
                break;
            }

            case BorderStyle.Inset or BorderStyle.Outset:
            {
                var topLeft = side is PhysicalSide.Top or PhysicalSide.Left;
                var dark = (style == BorderStyle.Inset) == topLeft;
                builder.Add(new DrawRingItem(outer, inner, new DisplayPaint(Shade(color, darker: dark))) { Source = source });
                break;
            }

            default:
                builder.Add(new DrawRingItem(outer, inner, new DisplayPaint(color)) { Source = source });
                break;
        }
    }

    /// <summary>Paints a solid ring, or the two rings of a double border (the outer and the inner thirds of each side).</summary>
    private static void PaintRing(DisplayListBuilder builder, RoundedRect outer, RoundedRect inner, Sides<double> widths, BorderStyle style, DisplayColor color, Node? source, double devicePixelRatio)
    {
        var paint = new DisplayPaint(color);
        if (style == BorderStyle.Double && (widths.Top >= 3 || widths.Right >= 3 || widths.Bottom >= 3 || widths.Left >= 3))
        {
            // The two lines of each side have the same width, a whole number of device pixels
            var line = new Sides<double>(GetDoubleLineWidth(widths.Top, devicePixelRatio), GetDoubleLineWidth(widths.Right, devicePixelRatio), GetDoubleLineWidth(widths.Bottom, devicePixelRatio), GetDoubleLineWidth(widths.Left, devicePixelRatio));
            builder.Add(new DrawRingItem(outer, outer.Inset(line), paint) { Source = source });
            builder.Add(new DrawRingItem(outer.Inset(new Sides<double>(widths.Top - line.Top, widths.Right - line.Right, widths.Bottom - line.Bottom, widths.Left - line.Left)), inner, paint) { Source = source });
            return;
        }

        builder.Add(new DrawRingItem(outer, inner, paint) { Source = source });
    }

    private static double GetWidth(Sides<double> widths, PhysicalSide side) => Get(widths, side);

    private static T Get<T>(Sides<T> sides, PhysicalSide side) => side switch
    {
        PhysicalSide.Top => sides.Top,
        PhysicalSide.Right => sides.Right,
        PhysicalSide.Bottom => sides.Bottom,
        _ => sides.Left,
    };

    /// <summary>
    /// The width of each of the two lines of a <c>double</c> side: a third of the width of the side, rounded to a whole number
    /// of device pixels when the width is one (as Blink: <c>(width + 1) / 3</c>; the space between the lines takes the
    /// rest), so the lines have sharp edges and a side paints the same pixels whatever the edge its thirds are measured from.
    /// </summary>
    private static double GetDoubleLineWidth(double width, double devicePixelRatio)
    {
        var deviceWidth = width * devicePixelRatio;
        var rounded = Math.Round(deviceWidth, MidpointRounding.AwayFromZero);
        return Math.Abs(deviceWidth - rounded) < 1e-6 ? Math.Floor((rounded + 1) / 3) / devicePixelRatio : width / 3;
    }

    /// <summary>
    /// The width of the outer half of a <c>groove</c> or <c>ridge</c> side: half of the width of the side, a whole number of
    /// device pixels when the width is one (as the lines of Blink, the half at the top or at the left of the side is the larger
    /// one: the outer half of the top and left sides, the inner half of the bottom and right sides).
    /// </summary>
    private static double GetHalfWidth(double width, double devicePixelRatio, bool larger)
    {
        var deviceWidth = width * devicePixelRatio;
        var rounded = Math.Round(deviceWidth, MidpointRounding.AwayFromZero);
        if (Math.Abs(deviceWidth - rounded) >= 1e-6)
            return width / 2;

        return (larger ? Math.Ceiling(rounded / 2) : Math.Floor(rounded / 2)) / devicePixelRatio;
    }


    /// <summary>
    /// Returns the shade of a side of a 3D border style (https://drafts.csswg.org/css-backgrounds-3/#border-style leaves them to
    /// the user agent), as in Chromium: the darker sides (the top and left sides of inset, the bottom and right sides of outset,
    /// and the halves of groove and ridge) take a darker color, whose largest component is 0.33 lower, unless the color is
    /// already as dark as #202020; the other sides take a lighter color, whose largest component is 0.33 higher, unless the
    /// color is already as light as #EBEBEB (black is lightened to #545454). The components keep their ratios, and are
    /// truncated to 8 bits.
    /// </summary>
    private static DisplayColor Shade(DisplayColor color, bool darker)
    {
        var r = Math.Clamp((double)color.R, 0, 1);
        var g = Math.Clamp((double)color.G, 0, 1);
        var b = Math.Clamp((double)color.B, 0, 1);
        if (darker)
        {
            // (the squared distances in 8-bit components: #202020 is at 3 x 32² from black)
            if (DistanceSquared(r, g, b, 0) <= 3 * 32 * 32)
                return color;

            var v = Math.Max(r, Math.Max(g, b));
            var multiplier = Math.Max(0, (v - 0.33) / v);
            return color with { R = Truncate(r * multiplier), G = Truncate(g * multiplier), B = Truncate(b * multiplier) };
        }
        else
        {
            // (#EBEBEB is at 3 x 20² from white)
            if (DistanceSquared(r, g, b, 1) <= 3 * 20 * 20)
                return color;

            var v = Math.Max(r, Math.Max(g, b));
            if (v <= 0)
                return color with { R = 0x54 / 255f, G = 0x54 / 255f, B = 0x54 / 255f };

            var multiplier = Math.Min(1, v + 0.33) / v;
            return color with { R = Truncate(r * multiplier), G = Truncate(g * multiplier), B = Truncate(b * multiplier) };
        }

        static int DistanceSquared(double r, double g, double b, double reference)
        {
            var dr = (int)Math.Round(r * 255, MidpointRounding.AwayFromZero) - (int)(reference * 255);
            var dg = (int)Math.Round(g * 255, MidpointRounding.AwayFromZero) - (int)(reference * 255);
            var db = (int)Math.Round(b * 255, MidpointRounding.AwayFromZero) - (int)(reference * 255);
            return (dr * dr) + (dg * dg) + (db * db);
        }

        // (the components are scaled by a number just below 256 and truncated to 8 bits)
        static float Truncate(double value) => (float)(Math.Min(255, Math.Floor(value * 255.99998)) / 255);
    }
}
