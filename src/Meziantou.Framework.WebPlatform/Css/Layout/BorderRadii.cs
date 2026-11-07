using Meziantou.Framework.WebPlatform.Css.Cascade;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The used radii of the corners of a border box (https://drafts.csswg.org/css-backgrounds/#border-radius): the horizontal and
/// vertical radius of each corner, the percentages resolved against the border box, reduced when the curves of two corners
/// would overlap (https://drafts.csswg.org/css-backgrounds/#corner-overlap). Used by the hit testing, and by the painting (<see cref="Painter"/>).
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct BorderRadii((double X, double Y) TopLeft, (double X, double Y) TopRight, (double X, double Y) BottomRight, (double X, double Y) BottomLeft)
{
    /// <summary>Returns the used radii of the corners of a border box of the given size, or null when all of them are 0.</summary>
    public static BorderRadii? Get(ComputedStyle style, PhysicalSize size) => Get(style, size, squareSide: null, otherSquareSide: null);

    /// <summary>Returns the used radii of the corners of a border box whose corners of some sides are square (the sides where the box is broken): they do not reduce the other ones.</summary>
    private static BorderRadii? Get(ComputedStyle style, PhysicalSize size, PhysicalSide? squareSide, PhysicalSide? otherSquareSide)
    {
        var zoom = style.Layout.EffectiveZoom;
        var topLeft = IsSquare(PhysicalSide.Top, PhysicalSide.Left) ? default : GetRadius(style, "border-top-left-radius", size, zoom);
        var topRight = IsSquare(PhysicalSide.Top, PhysicalSide.Right) ? default : GetRadius(style, "border-top-right-radius", size, zoom);
        var bottomRight = IsSquare(PhysicalSide.Bottom, PhysicalSide.Right) ? default : GetRadius(style, "border-bottom-right-radius", size, zoom);
        var bottomLeft = IsSquare(PhysicalSide.Bottom, PhysicalSide.Left) ? default : GetRadius(style, "border-bottom-left-radius", size, zoom);
        if (IsZero(topLeft) && IsZero(topRight) && IsZero(bottomRight) && IsZero(bottomLeft))
            return null;

        // https://drafts.csswg.org/css-borders-4/#border-shape-radius-interaction: "When an element's computed value of
        // border-shape is not none, its border-radius is ignored, as if it was set to 0"
        if (BorderShapeGeometry.HasBorderShape(style))
            return null;

        // "Let f = min(Li/Si), where i ∈ {top, right, bottom, left}, Si is the sum of the two corresponding radii of the corners on
        // side i, and Ltop = Lbottom = the width of the box, and Lleft = Lright = the height of the box. If f < 1, then all corner
        // radii are reduced by multiplying them by f."
        var factor = 1d;
        factor = Reduce(factor, size.Width, topLeft.X + topRight.X);
        factor = Reduce(factor, size.Width, bottomLeft.X + bottomRight.X);
        factor = Reduce(factor, size.Height, topLeft.Y + bottomLeft.Y);
        factor = Reduce(factor, size.Height, topRight.Y + bottomRight.Y);

        // https://drafts.csswg.org/css-borders-4/#corner-shape-constrain-radii: "When concave corner-shape values are present,
        // diagonally opposite corners might overlap each other": "The used values of all border radii must be proportionally
        // reduced by multiplying them by the minimum of the opposite corner scale factor and the adjacent corner scale factor"
        var shapes = CornerShapes.Get(style);
        if (shapes.HasConcaveCorner)
        {
            factor = Math.Min(factor, GetOppositeCornerScaleFactor(size, topLeft, shapes.TopLeft, bottomRight, shapes.BottomRight, isTopLeft: true));
            factor = Math.Min(factor, GetOppositeCornerScaleFactor(size, topRight, shapes.TopRight, bottomLeft, shapes.BottomLeft, isTopLeft: false));
        }

        if (factor < 1)
        {
            topLeft = (topLeft.X * factor, topLeft.Y * factor);
            topRight = (topRight.X * factor, topRight.Y * factor);
            bottomRight = (bottomRight.X * factor, bottomRight.Y * factor);
            bottomLeft = (bottomLeft.X * factor, bottomLeft.Y * factor);
        }

        if (!shapes.IsRound)
        {
            // https://drafts.csswg.org/css-borders-4/#valdef-corner-shape-square: a square corner (superellipse(infinity)) is the
            // corner of the box at any distance from the border edge, as a corner without radius (it reduces the other radii)
            topLeft = double.IsPositiveInfinity(shapes.TopLeft) ? default : topLeft;
            topRight = double.IsPositiveInfinity(shapes.TopRight) ? default : topRight;
            bottomRight = double.IsPositiveInfinity(shapes.BottomRight) ? default : bottomRight;
            bottomLeft = double.IsPositiveInfinity(shapes.BottomLeft) ? default : bottomLeft;
            if (IsZero(topLeft) && IsZero(topRight) && IsZero(bottomRight) && IsZero(bottomLeft))
                return null;
        }

        return new BorderRadii(topLeft, topRight, bottomRight, bottomLeft);

        bool IsSquare(PhysicalSide side, PhysicalSide adjacent) => squareSide == side || squareSide == adjacent || otherSquareSide == side || otherSquareSide == adjacent;

        static bool IsZero((double X, double Y) radius) => radius.X <= 0 || radius.Y <= 0;

        static double Reduce(double factor, double length, double sum) => sum > 0 ? Math.Min(factor, length / sum) : factor;
    }

    /// <summary>
    /// Returns the outset-adjusted border radius of a corner (https://drafts.csswg.org/css-borders-4/#outset-adjusted-border-radius):
    /// the radius of the corner of an edge expanded by an outset in each axis (the margin edge, the spread of a shadow, the
    /// overflow clip margin). The radius grows by the outset, less for a radius smaller than the outset "so that a small rounded
    /// corner with a big outset does not appear to be disproportionally round"; the reduction is "more pronounced for rectangular
    /// shapes [...] and less pronounced for elliptical shapes" (the coverage of the edge by the corner). An inset (a negative
    /// outset) reduces the radius by its width.
    /// </summary>
    /// <param name="edge">The size of the edge that is expanded (the border box).</param>
    /// <param name="radius">The radii of the corner.</param>
    /// <param name="outsetX">The outset of the vertical side of the corner.</param>
    /// <param name="outsetY">The outset of the horizontal side of the corner.</param>
    public static (double X, double Y) GetOutsetAdjustedRadius(PhysicalSize edge, (double X, double Y) radius, double outsetX, double outsetY)
    {
        var coverage = 2 * Math.Min(edge.Width > 0 ? radius.X / edge.Width : 1, edge.Height > 0 ? radius.Y / edge.Height : 1);
        return (Adjust(radius.X, outsetX), Adjust(radius.Y, outsetY));

        double Adjust(double value, double outset)
        {
            if (value > outset || coverage > 1 || outset <= 0)
                return value + outset;

            var ratio = value / outset;
            return value + (outset * (1 - (Math.Pow(1 - ratio, 3) * (1 - Math.Pow(coverage, 3)))));
        }
    }

    /// <summary>
    /// Returns the opposite corner scale factor of two diagonally opposite corners
    /// (https://drafts.csswg.org/css-borders-4/#opposite-corner-scale-factor): the highest factor (1 at most) that, applied to
    /// the radii of the two corners, makes their hulls (the triangle of the corner area of a convex corner, a pentagon that
    /// contains the curve of a concave one) not intersect.
    /// </summary>
    private static double GetOppositeCornerScaleFactor(PhysicalSize size, (double X, double Y) first, double firstShape, (double X, double Y) second, double secondShape, bool isTopLeft)
    {
        if (IsEmpty(first) || IsEmpty(second) || (firstShape >= 0 && secondShape >= 0))
            return 1;

        // The corners of the box of the two hulls (the origins of their scaling), and the directions of their sides
        var (firstCorner, firstX, firstY, secondCorner, secondX, secondY) = isTopLeft
            ? (new PhysicalOffset(0, 0), 1, 1, new PhysicalOffset(size.Width, size.Height), -1, -1)
            : (new PhysicalOffset(size.Width, 0), -1, 1, new PhysicalOffset(0, size.Height), 1, -1);
        var firstHull = GetHull(firstShape);
        var secondHull = GetHull(secondShape);
        if (!Intersect(1))
            return 1;

        var low = 0d;
        var high = 1d;
        for (var i = 0; i < 40; i++)
        {
            var middle = (low + high) / 2;
            if (Intersect(middle))
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return low;

        static bool IsEmpty((double X, double Y) radius) => radius.X <= 0 || radius.Y <= 0;

        // The normalized inner corner hull: the points are the distances from the corner of the box, in radii
        static (double X, double Y)[] GetHull(double shape)
        {
            if (shape >= 0)
                return [(0, 0), (0, 1), (1, 0)];

            // The tangent of the curve at its middle, perpendicular to the diagonal of the corner area
            var tangent = 1 - (2 * CornerShapes.GetHalfCorner(shape));
            return [(0, 0), (0, 1), (tangent, 1), (1, tangent), (1, 0)];
        }

        bool Intersect(double scale)
        {
            var a = Map(firstHull, firstCorner, first, firstX, firstY, scale);
            var b = Map(secondHull, secondCorner, second, secondX, secondY, scale);
            return !HasSeparatingAxis(a, b) && !HasSeparatingAxis(b, a);
        }

        static PhysicalOffset[] Map((double X, double Y)[] hull, PhysicalOffset corner, (double X, double Y) radius, int directionX, int directionY, double scale)
        {
            var result = new PhysicalOffset[hull.Length];
            for (var i = 0; i < hull.Length; i++)
            {
                result[i] = new PhysicalOffset(corner.X + (directionX * hull[i].X * radius.X * scale), corner.Y + (directionY * hull[i].Y * radius.Y * scale));
            }

            return result;
        }

        // Whether an edge of a convex polygon separates it from another one (the polygons that only touch do not intersect)
        static bool HasSeparatingAxis(PhysicalOffset[] polygon, PhysicalOffset[] other)
        {
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Length];
                var normalX = b.Y - a.Y;
                var normalY = a.X - b.X;
                if (normalX == 0 && normalY == 0)
                    continue;

                double min = double.PositiveInfinity, max = double.NegativeInfinity, otherMin = double.PositiveInfinity, otherMax = double.NegativeInfinity;
                foreach (var point in polygon)
                {
                    var projection = (point.X * normalX) + (point.Y * normalY);
                    min = Math.Min(min, projection);
                    max = Math.Max(max, projection);
                }

                foreach (var point in other)
                {
                    var projection = (point.X * normalX) + (point.Y * normalY);
                    otherMin = Math.Min(otherMin, projection);
                    otherMax = Math.Max(otherMax, projection);
                }

                var tolerance = 1e-9 * Math.Max(1, Math.Max(Math.Abs(max), Math.Abs(otherMax)));
                if (max <= otherMin + tolerance || otherMax <= min + tolerance)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Returns the used radii of the corners of the border box of a fragment, or null when all of them are 0. A box split into
    /// several fragments (an inline box over several lines, a box broken by the fragmentation) has the corners of its edges at
    /// the breaks only with <c>box-decoration-break: clone</c> (https://drafts.csswg.org/css-break-3/#break-decoration): with
    /// <c>slice</c>, the corners of the sides it does not have are square.
    /// </summary>
    public static BorderRadii? Get(BoxFragment fragment)
    {
        var style = fragment.Box.Style;
        var writingDirection = fragment.WritingDirection;
        if (fragment.Box.IsInlineBox)
        {
            if ((fragment.IsFirstFragment && fragment.IsLastFragment) || style.Layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone)
                return Get(style, fragment.Size);

            // The inline boxes are split in their inline axis
            return Get(style, fragment.Size, fragment.IsFirstFragment ? null : writingDirection.InlineStart, fragment.IsLastFragment ? null : writingDirection.InlineEnd);
        }

        // The other boxes are split in their block axis: the fragment has the corners of the edges of the border box it contains.
        // The square corners do not reduce the other ones (https://drafts.csswg.org/css-backgrounds/#corner-overlap applies to
        // the corners of the fragment)
        if (fragment.StitchedBox is not { } stitched)
            return Get(style, fragment.Size);

        var blockSize = writingDirection.ToLogical(fragment.Size).Block;
        var isBrokenAtStart = fragment.StitchedOffset > 0.01;
        var isBrokenAtEnd = fragment.StitchedOffset + blockSize < writingDirection.ToLogical(stitched.Size).Block - 0.01;
        return Get(style, fragment.Size, isBrokenAtStart ? writingDirection.BlockStart : null, isBrokenAtEnd ? writingDirection.BlockEnd : null);
    }

    /// <summary>Returns the horizontal and vertical radius of a corner from its computed value (one or two &lt;length-percentage&gt;).</summary>
    private static (double X, double Y) GetRadius(ComputedStyle style, string property, PhysicalSize size, double zoom)
    {
        var value = style.GetComputedValue(property);
        if (value is "0px" or "0" or "")
            return default;

        // The two values are separated by a space outside of the math functions
        var separator = -1;
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '(':
                    depth++;
                    break;

                case ')':
                    depth--;
                    break;

                case ' ' when depth == 0:
                    separator = i;
                    break;
            }

            if (separator >= 0)
                break;
        }

        var horizontal = separator < 0 ? value : value[..separator];
        var vertical = separator < 0 ? value : value[(separator + 1)..];
        var x = LengthPercentage.TryParse(horizontal, zoom, nonNegative: true, out var h) ? Math.Max(0, h.Resolve(size.Width)) : 0;
        var y = LengthPercentage.TryParse(vertical, zoom, nonNegative: true, out var v) ? Math.Max(0, v.Resolve(size.Height)) : 0;
        return (x, y);
    }
}
