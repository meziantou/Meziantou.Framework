using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The geometry of the basic shapes (https://drafts.csswg.org/css-shapes/#basic-shape-functions) of <c>clip-path</c>
/// (https://drafts.fxtf.org/css-masking/#the-clip-path): <c>inset()</c> (and the computed <c>rect()</c> and <c>xywh()</c>),
/// <c>circle()</c>, <c>ellipse()</c>, <c>polygon()</c>, <c>path()</c> and <c>shape()</c>, as paths in the coordinates of a
/// reference box, and the reference boxes (&lt;geometry-box&gt;) of the boxes.
/// </summary>
internal static class BasicShapePaths
{
    /// <summary>The parts of a computed value of <c>clip-path</c>.</summary>
    /// <param name="Shape">The basic shape, or null.</param>
    /// <param name="Url">The url() of a clipPath element, or null.</param>
    /// <param name="ReferenceBox">The &lt;geometry-box&gt; keyword, or null (border-box).</param>
    public readonly record struct ClipPathValue(CssFunction? Shape, CssComponentValue? Url, string? ReferenceBox);

    /// <summary>Parses a computed value of <c>clip-path</c>; null for <c>none</c> or an invalid value.</summary>
    public static ClipPathValue? ParseClipPath(string value)
    {
        if (value is "none" or "")
            return null;

        CssFunction? shape = null;
        CssComponentValue? url = null;
        string? box = null;
        foreach (var component in ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)))
        {
            switch (component)
            {
                case CssPreservedToken { Type: CssTokenType.Url }:
                    url = component;
                    break;

                case CssFunction function when InfraAscii.ToAsciiLowercase(function.Name) is "url" or "src":
                    url = component;
                    break;

                case CssFunction function when shape is null:
                    shape = function;
                    break;

                case CssPreservedToken { Type: CssTokenType.Ident } ident when box is null:
                    box = InfraAscii.ToAsciiLowercase(ident.Token.Value);
                    break;

                default:
                    return null;
            }
        }

        return new ClipPathValue(shape, url, box);
    }

    /// <summary>
    /// Returns a reference box of a box fragment (https://drafts.fxtf.org/css-masking/#typedef-geometry-box) in the coordinates of
    /// its border box: "for elements with associated CSS layout box, the used value for fill-box is content-box and for stroke-box
    /// and view-box is border-box".
    /// </summary>
    /// <remarks>
    /// The specifications do not define the reference box of an inline box split over several lines: as in the browsers, it is
    /// the box of its first fragment, for all its fragments (in the coordinates of the fragment; the fragments on the other
    /// lines are clipped by the shape in the box of the first one).
    /// </remarks>
    public static PhysicalRect GetReferenceBox(BoxFragment fragment, string? box)
    {
        if (fragment.Box.IsInlineBox && fragment.Box.FirstFragment is BoxFragment first && !ReferenceEquals(first, fragment))
            return GetOwnReferenceBox(first, box).Translate(first.GetOffsetInRoot() - fragment.GetOffsetInRoot());

        return GetOwnReferenceBox(fragment, box);
    }

    private static PhysicalRect GetOwnReferenceBox(BoxFragment fragment, string? box)
    {
        var border = new PhysicalRect(PhysicalOffset.Zero, fragment.Size);

        // https://drafts.csswg.org/css-box-4/#valdef-box-view-box: "Uses the nearest SVG viewport as reference box": as in the
        // browsers, also for a foreignObject element, although it has a CSS box
        if (box == "view-box" && SvgForeignObjectLayout.GetViewportRect(fragment) is { } viewport)
            return viewport;

        return box switch
        {
            "margin-box" => border.Expand(GetMargins(fragment)),
            "padding-box" => border.Contract(fragment.Border),
            "content-box" or "fill-box" => border.Contract(fragment.Border.Add(fragment.Padding)),

            // https://drafts.csswg.org/css-borders-4/#border-shape-geometry-box: the box in the middle of the borders
            "half-border-box" => border.Contract(new Sides<double>(fragment.Border.Top / 2, fragment.Border.Right / 2, fragment.Border.Bottom / 2, fragment.Border.Left / 2)),
            _ => border,
        };
    }

    /// <summary>
    /// Returns the margins of the margin box of a fragment: the computed margins (as the browsers: not the margin of an
    /// over-constrained block that fills its containing block), the used ones for auto; the margins of an inline box in the block
    /// axis do not affect the layout (they are not in the fragment) but are part of its margin box.
    /// </summary>
    internal static Sides<double> GetMargins(BoxFragment fragment)
    {
        var used = fragment.Margin;
        if (fragment.Box.IsAnonymous)
            return used;

        var margins = fragment.Box.Style.Layout.Box.Margin;
        var basis = fragment.Parent is BoxFragment parent ? parent.ContentBoxRect.Width : 0;
        return new Sides<double>(
            margins.Top.IsAuto ? used.Top : margins.Top.Resolve(basis),
            margins.Right.IsAuto ? used.Right : margins.Right.Resolve(basis),
            margins.Bottom.IsAuto ? used.Bottom : margins.Bottom.Resolve(basis),
            margins.Left.IsAuto ? used.Left : margins.Left.Resolve(basis));
    }

    /// <summary>
    /// The geometry of a basic shape in its reference box: a rounded rectangle (<c>inset()</c>, <c>rect()</c>, <c>xywh()</c>,
    /// <c>circle()</c> and <c>ellipse()</c>, whose radii are the half axes), the vertices of a polygon, or a path
    /// (<c>path()</c> and <c>shape()</c>).
    /// </summary>
    public readonly record struct BasicShapeGeometry(RoundedRect? Rectangle, PhysicalOffset[]? Polygon, FillRule FillRule, DisplayPath? Path)
    {
        /// <summary>Returns the path of the shape.</summary>
        public DisplayPath ToPath()
        {
            if (Rectangle is { } rectangle)
                return DisplayPath.FromRoundedRect(rectangle);

            if (Polygon is { } vertices)
            {
                var path = new DisplayPath { FillRule = FillRule };
                path.MoveTo(vertices[0].X, vertices[0].Y);
                for (var i = 1; i < vertices.Length; i++)
                {
                    path.LineTo(vertices[i].X, vertices[i].Y);
                }

                path.Close();
                return path;
            }

            return Path!;
        }
    }

    /// <summary>Returns the path of a basic shape in a reference box (the lengths of the computed value are multiplied by <paramref name="zoom"/>), or null when the shape is not valid.</summary>
    public static DisplayPath? CreatePath(CssFunction shape, PhysicalRect box, double zoom) => Resolve(shape, box, zoom)?.ToPath();

    /// <summary>
    /// Returns the geometry of a basic shape in a reference box (the lengths of the computed value are multiplied by
    /// <paramref name="zoom"/>), or null when the shape is not valid. Used by <c>clip-path</c>, by <c>shape-outside</c> (see
    /// <see cref="FloatShapes"/>) and by <c>offset-path</c> (see <see cref="MotionPath"/>), whose circles and ellipses without
    /// position are centered at <paramref name="defaultCenter"/> instead of the center of the reference box.
    /// </summary>
    public static BasicShapeGeometry? Resolve(CssFunction shape, PhysicalRect box, double zoom, PhysicalOffset? defaultCenter = null)
    {
        var arguments = ValueMatcher.RemoveWhitespace(shape.Value);
        return InfraAscii.ToAsciiLowercase(shape.Name) switch
        {
            "inset" => FromRectangle(CreateInset(arguments, box, zoom)),
            "rect" => FromRectangle(CreateRect(arguments, box, zoom)),
            "xywh" => FromRectangle(CreateXywh(arguments, box, zoom)),
            "circle" => FromRectangle(CreateEllipse(isCircle: true, arguments, box, zoom, defaultCenter)),
            "ellipse" => FromRectangle(CreateEllipse(isCircle: false, arguments, box, zoom, defaultCenter)),
            "polygon" => CreatePolygon(shape.Value, box, zoom),
            "path" => FromPath(CreateSvgPath(shape, box, zoom)),
            "shape" => FromPath(CreateShape(shape.Value, box, zoom)),
            _ => null,
        };

        static BasicShapeGeometry? FromRectangle(RoundedRect? rectangle) => rectangle is { } value ? new BasicShapeGeometry(value, Polygon: null, FillRule.NonZero, Path: null) : null;

        static BasicShapeGeometry? FromPath(DisplayPath? path) => path is not null ? new BasicShapeGeometry(Rectangle: null, Polygon: null, path.FillRule, path) : null;
    }

    private static bool IsIdent(CssComponentValue value, string name) => value is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, name);

    /// <summary>Splits the arguments at the <c>round</c> keyword.</summary>
    private static (List<CssComponentValue> Before, List<CssComponentValue>? Radii) SplitRound(List<CssComponentValue> arguments)
    {
        var index = arguments.FindIndex(argument => IsIdent(argument, "round"));
        return index < 0 ? (arguments, null) : (arguments.GetRange(0, index), arguments.GetRange(index + 1, arguments.Count - index - 1));
    }

    /// <summary>https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-inset</summary>
    private static RoundedRect? CreateInset(List<CssComponentValue> arguments, PhysicalRect box, double zoom)
    {
        var (values, radii) = SplitRound(arguments);
        if (values.Count is 0 or > 4)
            return null;

        var insets = new LengthPercentage[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            if (!LengthPercentage.TryParse(values[i], zoom, nonNegative: false, out insets[i]))
                return null;
        }

        // The sides are expanded as the margins, the vertical ones refer to the height of the box, the horizontal ones to its width
        var top = insets[0].Resolve(box.Height);
        var right = (insets.Length > 1 ? insets[1] : insets[0]).Resolve(box.Width);
        var bottom = (insets.Length > 2 ? insets[2] : insets[0]).Resolve(box.Height);
        var left = (insets.Length > 3 ? insets[3] : insets.Length > 1 ? insets[1] : insets[0]).Resolve(box.Width);
        return CreateRoundedRect(FromEdges(box.Left + left, box.Top + top, box.Right - right, box.Bottom - bottom, normalize: true), radii, box, zoom);
    }

    /// <summary>https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-rect</summary>
    private static RoundedRect? CreateRect(List<CssComponentValue> arguments, PhysicalRect box, double zoom)
    {
        var (values, radii) = SplitRound(arguments);
        if (values.Count != 4)
            return null;

        var edges = new double[4];
        for (var i = 0; i < 4; i++)
        {
            var basis = i % 2 == 0 ? box.Height : box.Width;
            if (IsIdent(values[i], "auto"))
            {
                edges[i] = i is 0 or 3 ? 0 : basis;
            }
            else if (LengthPercentage.TryParse(values[i], zoom, nonNegative: false, out var length))
            {
                edges[i] = length.Resolve(basis);
            }
            else
            {
                return null;
            }
        }

        return CreateRoundedRect(FromEdges(box.Left + edges[3], box.Top + edges[0], box.Left + edges[1], box.Top + edges[2], normalize: true), radii, box, zoom);
    }

    /// <summary>https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-xywh</summary>
    private static RoundedRect? CreateXywh(List<CssComponentValue> arguments, PhysicalRect box, double zoom)
    {
        var (values, radii) = SplitRound(arguments);
        if (values.Count != 4)
            return null;

        var resolved = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!LengthPercentage.TryParse(values[i], zoom, nonNegative: i >= 2, out var length))
                return null;

            resolved[i] = length.Resolve(i % 2 == 0 ? box.Width : box.Height);
        }

        return CreateRoundedRect(new PhysicalRect(box.Left + resolved[0], box.Top + resolved[1], Math.Max(0, resolved[2]), Math.Max(0, resolved[3])), radii, box, zoom);
    }

    /// <summary>
    /// Returns a rectangle with the rounded corners of the <c>round</c> argument (the syntax of <c>border-radius</c>,
    /// the percentages refer to the reference box, the radii are reduced as the border radii when they overlap).
    /// </summary>
    private static RoundedRect? CreateRoundedRect(PhysicalRect rect, List<CssComponentValue>? radii, PhysicalRect box, double zoom)
    {
        if (radii is null || radii.Count == 0)
            return new RoundedRect(rect, default);

        var slash = radii.FindIndex(value => value is CssPreservedToken { Type: CssTokenType.Delim } delim && delim.Token.Value == "/");
        var horizontal = slash < 0 ? radii : radii.GetRange(0, slash);
        var vertical = slash < 0 ? radii : radii.GetRange(slash + 1, radii.Count - slash - 1);
        if (Expand(horizontal, box.Width, zoom) is not { } x || Expand(vertical, box.Height, zoom) is not { } y)
            return null;

        // https://drafts.csswg.org/css-backgrounds/#corner-overlap
        var factor = 1d;
        factor = Reduce(factor, rect.Width, x[0] + x[1]);
        factor = Reduce(factor, rect.Width, x[3] + x[2]);
        factor = Reduce(factor, rect.Height, y[0] + y[3]);
        factor = Reduce(factor, rect.Height, y[1] + y[2]);
        return new RoundedRect(rect, new BorderRadii(
            (x[0] * factor, y[0] * factor),
            (x[1] * factor, y[1] * factor),
            (x[2] * factor, y[2] * factor),
            (x[3] * factor, y[3] * factor)));

        static double Reduce(double factor, double length, double sum) => sum > 0 ? Math.Min(factor, length / sum) : factor;

        static double[]? Expand(List<CssComponentValue> values, double basis, double zoom)
        {
            if (values.Count is 0 or > 4)
                return null;

            var resolved = new double[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                if (!LengthPercentage.TryParse(values[i], zoom, nonNegative: true, out var length))
                    return null;

                resolved[i] = Math.Max(0, length.Resolve(basis));
            }

            // top-left, top-right, bottom-right, bottom-left
            return resolved.Length switch
            {
                1 => [resolved[0], resolved[0], resolved[0], resolved[0]],
                2 => [resolved[0], resolved[1], resolved[0], resolved[1]],
                3 => [resolved[0], resolved[1], resolved[2], resolved[1]],
                _ => resolved,
            };
        }
    }

    /// <summary>https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-circle and https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-ellipse</summary>
    private static RoundedRect? CreateEllipse(bool isCircle, List<CssComponentValue> arguments, PhysicalRect box, double zoom, PhysicalOffset? defaultCenter = null)
    {
        var at = arguments.FindIndex(argument => IsIdent(argument, "at"));
        var radii = at < 0 ? arguments : arguments.GetRange(0, at);
        var centerX = defaultCenter?.X ?? (box.Left + (box.Width / 2));
        var centerY = defaultCenter?.Y ?? (box.Top + (box.Height / 2));
        if (at >= 0)
        {
            if (ResolvePosition(arguments.GetRange(at + 1, arguments.Count - at - 1), box, zoom) is not { } center)
                return null;

            (centerX, centerY) = (center.X, center.Y);
        }

        var left = Math.Abs(centerX - box.Left);
        var right = Math.Abs(box.Right - centerX);
        var top = Math.Abs(centerY - box.Top);
        var bottom = Math.Abs(box.Bottom - centerY);
        var closestCorner = Math.Sqrt(Math.Pow(Math.Min(left, right), 2) + Math.Pow(Math.Min(top, bottom), 2));
        var farthestCorner = Math.Sqrt(Math.Pow(Math.Max(left, right), 2) + Math.Pow(Math.Max(top, bottom), 2));
        double radiusX;
        double radiusY;
        if (isCircle)
        {
            // A percentage refers to sqrt(width² + height²) / sqrt(2)
            var reference = Math.Sqrt((box.Width * box.Width) + (box.Height * box.Height)) / Math.Sqrt(2);
            if (radii.Count > 1)
                return null;

            if (ResolveRadius(radii.Count == 0 ? null : radii[0], reference, Math.Min(Math.Min(left, right), Math.Min(top, bottom)), Math.Max(Math.Max(left, right), Math.Max(top, bottom)), closestCorner, farthestCorner, zoom) is not { } radius)
                return null;

            radiusX = radius;
            radiusY = radius;
        }
        else
        {
            if (radii.Count is not (0 or 2))
                return null;

            // The specifications do not define closest-corner and farthest-corner for one radius of an ellipse
            // (https://github.com/w3c/csswg-drafts/issues/14010): as in Blink (and the tests), the radius is the distance to the
            // corner
            var closestSideX = Math.Min(left, right);
            var farthestSideX = Math.Max(left, right);
            var closestSideY = Math.Min(top, bottom);
            var farthestSideY = Math.Max(top, bottom);
            if (ResolveRadius(radii.Count == 0 ? null : radii[0], box.Width, closestSideX, farthestSideX, closestCorner, farthestCorner, zoom) is not { } x
                || ResolveRadius(radii.Count == 0 ? null : radii[1], box.Height, closestSideY, farthestSideY, closestCorner, farthestCorner, zoom) is not { } y)
            {
                return null;
            }

            radiusX = x;
            radiusY = y;
        }

        var ellipse = new PhysicalRect(centerX - radiusX, centerY - radiusY, 2 * radiusX, 2 * radiusY);
        var corner = (ellipse.Width / 2, ellipse.Height / 2);
        return new RoundedRect(ellipse, new BorderRadii(corner, corner, corner, corner));

        static double? ResolveRadius(CssComponentValue? value, double reference, double closest, double farthest, double closestCorner, double farthestCorner, double zoom)
        {
            if (value is null)
                return closest;

            if (value is CssPreservedToken { Type: CssTokenType.Ident } ident)
            {
                return InfraAscii.ToAsciiLowercase(ident.Token.Value) switch
                {
                    "closest-side" => closest,
                    "farthest-side" => farthest,
                    "closest-corner" => closestCorner,
                    "farthest-corner" => farthestCorner,
                    _ => null,
                };
            }

            return LengthPercentage.TryParse(value, zoom, nonNegative: true, out var length) ? Math.Max(0, length.Resolve(reference)) : null;
        }
    }

    /// <summary>Resolves a computed &lt;position&gt; (two offsets, or keywords) in a box.</summary>
    internal static PhysicalOffset? ResolvePosition(List<CssComponentValue> values, PhysicalRect box, double zoom)
    {
        if (values.Count == 0 || CssBackgrounds.ParsePosition(values) is not { } parsed)
            return null;

        if (ResolveOffset(parsed.X, box.Width, zoom, "left", "right") is not { } x || ResolveOffset(parsed.Y, box.Height, zoom, "top", "bottom") is not { } y)
            return null;

        return new PhysicalOffset(box.Left + x, box.Top + y);

        static double? ResolveOffset(string value, double size, double zoom, string start, string end)
        {
            if (LengthPercentage.TryParse(value, zoom, nonNegative: false, out var offset))
                return offset.Resolve(size);

            var parts = CssValueComputer.SplitComponents(value);
            if (parts.Length == 0)
                return null;

            var keyword = parts[0];
            var length = parts.Length > 1 && LengthPercentage.TryParse(parts[1], zoom, nonNegative: false, out var parsed) ? parsed.Resolve(size) : 0;
            if (keyword == "center")
                return size / 2;

            if (keyword == end || keyword is "x-end" or "y-end")
                return size - length;

            return keyword == start || keyword is "x-start" or "y-start" ? length : null;
        }
    }

    /// <summary>https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-polygon</summary>
    private static BasicShapeGeometry? CreatePolygon(List<CssComponentValue> values, PhysicalRect box, double zoom)
    {
        var fillRule = FillRule.NonZero;
        var vertices = new List<PhysicalOffset>();
        foreach (var arguments in CssValueComputer.SplitArguments(values))
        {
            var group = ValueMatcher.RemoveWhitespace(arguments);
            if (vertices.Count == 0 && group is [CssPreservedToken { Type: CssTokenType.Ident } rule])
            {
                fillRule = InfraAscii.EqualsIgnoreAsciiCase(rule.Token.Value, "evenodd") ? FillRule.EvenOdd : FillRule.NonZero;
                continue;
            }

            if (group.Count != 2 || !LengthPercentage.TryParse(group[0], zoom, nonNegative: false, out var xValue) || !LengthPercentage.TryParse(group[1], zoom, nonNegative: false, out var yValue))
                return null;

            vertices.Add(new PhysicalOffset(box.Left + xValue.Resolve(box.Width), box.Top + yValue.Resolve(box.Height)));
        }

        return vertices.Count == 0 ? null : new BasicShapeGeometry(Rectangle: null, [.. vertices], fillRule, Path: null);
    }

    /// <summary>https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-path: SVG path data in px from the origin of the reference box.</summary>
    private static DisplayPath? CreateSvgPath(CssFunction function, PhysicalRect box, double zoom)
    {
        if (!CssPath.TryParse(function, out var fillRule, out var commands))
            return null;

        var path = DisplayPath.FromSvg(commands, fillRule == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero);
        return Transform(path, new SvgAffine(zoom, 0, 0, zoom, box.X, box.Y));
    }

    /// <summary>Whether a point is in the fill area of a path (with its fill rule; the curves are flattened).</summary>
    public static bool Contains(DisplayPath path, PhysicalOffset point)
    {
        if (path.RoundedRect is { } rounded && rounded.IsRect)
            return rounded.Rect.Contains(point.X, point.Y);

        var winding = 0;
        foreach (var contour in Flatten(path))
        {
            for (var i = 0; i < contour.Length; i++)
            {
                AddLine(contour[i], contour[(i + 1) % contour.Length]);
            }
        }

        return path.FillRule == FillRule.EvenOdd ? (winding & 1) != 0 : winding != 0;

        // The winding number of the edges crossed by a ray from the point to the right (the even-odd rule counts the parity)
        void AddLine(PhysicalOffset a, PhysicalOffset b)
        {
            if (a.Y <= point.Y)
            {
                if (b.Y > point.Y && Cross(a, b) > 0)
                {
                    winding++;
                }
            }
            else if (b.Y <= point.Y && Cross(a, b) < 0)
            {
                winding--;
            }
        }

        double Cross(PhysicalOffset a, PhysicalOffset b) => ((b.X - a.X) * (point.Y - a.Y)) - ((point.X - a.X) * (b.Y - a.Y));
    }

    /// <summary>
    /// Returns the subpaths of a path as closed polygons (the last vertex is connected to the first one): the curves are
    /// flattened into 16 segments each.
    /// </summary>
    public static List<PhysicalOffset[]> Flatten(DisplayPath path)
    {
        var contours = new List<PhysicalOffset[]>();
        var contour = new List<PhysicalOffset>();
        var points = path.Points;
        var index = 0;
        var start = default(PhysicalOffset);
        var current = default(PhysicalOffset);
        foreach (var verb in path.Verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    EndContour();
                    start = current = points[index++];
                    contour.Add(current);
                    break;

                case PathVerb.LineTo:
                    current = points[index++];
                    Add(current);
                    break;

                case PathVerb.QuadTo:
                {
                    var c = points[index++];
                    var end = points[index++];
                    for (var i = 1; i <= 16; i++)
                    {
                        var t = i / 16d;
                        var u = 1 - t;
                        Add(new PhysicalOffset((u * u * current.X) + (2 * u * t * c.X) + (t * t * end.X), (u * u * current.Y) + (2 * u * t * c.Y) + (t * t * end.Y)));
                    }

                    current = end;
                    break;
                }

                case PathVerb.CubicTo:
                {
                    var c1 = points[index++];
                    var c2 = points[index++];
                    var end = points[index++];
                    for (var i = 1; i <= 16; i++)
                    {
                        var t = i / 16d;
                        var u = 1 - t;
                        Add(new PhysicalOffset(
                            (u * u * u * current.X) + (3 * u * u * t * c1.X) + (3 * u * t * t * c2.X) + (t * t * t * end.X),
                            (u * u * u * current.Y) + (3 * u * u * t * c1.Y) + (3 * u * t * t * c2.Y) + (t * t * t * end.Y)));
                    }

                    current = end;
                    break;
                }

                default:
                    // Close: the next segment starts at the start of the subpath
                    EndContour();
                    current = start;
                    break;
            }
        }

        EndContour();
        return contours;

        void Add(PhysicalOffset point)
        {
            // A segment drawn after a close starts a new subpath at the start of the closed one
            if (contour.Count == 0)
            {
                contour.Add(start);
            }

            contour.Add(point);
        }

        void EndContour()
        {
            if (contour.Count > 0)
            {
                contours.Add([.. contour]);
                contour.Clear();
            }
        }
    }

    /// <summary>Returns a path whose points are mapped by an affine transform.</summary>
    public static DisplayPath Transform(DisplayPath path, SvgAffine transform)
    {
        if (transform.IsIdentity)
            return path;

        var result = new DisplayPath { FillRule = path.FillRule };
        var points = path.Points;
        var index = 0;
        foreach (var verb in path.Verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                {
                    var p = Map(points[index++]);
                    result.MoveTo(p.X, p.Y);
                    break;
                }

                case PathVerb.LineTo:
                {
                    var p = Map(points[index++]);
                    result.LineTo(p.X, p.Y);
                    break;
                }

                case PathVerb.QuadTo:
                {
                    var c = Map(points[index++]);
                    var p = Map(points[index++]);
                    result.QuadTo(c.X, c.Y, p.X, p.Y);
                    break;
                }

                case PathVerb.CubicTo:
                {
                    var c1 = Map(points[index++]);
                    var c2 = Map(points[index++]);
                    var p = Map(points[index++]);
                    result.CubicTo(c1.X, c1.Y, c2.X, c2.Y, p.X, p.Y);
                    break;
                }

                default:
                    result.Close();
                    break;
            }
        }

        return result;

        SvgPathData.SvgPoint Map(PhysicalOffset point) => transform.Apply(point.X, point.Y);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-shapes-2/#shape-function: <c>shape([&lt;fill-rule&gt;]? from &lt;position&gt;, &lt;shape-command&gt;#)</c>,
    /// converted to SVG path commands in the reference box (the percentages of the x coordinates refer to its width, the ones of
    /// the y coordinates to its height).
    /// </summary>
    private static DisplayPath? CreateShape(List<CssComponentValue> values, PhysicalRect box, double zoom)
        => CreateShapeCommands(values, box, zoom, out var fillRule) is { } commands ? DisplayPath.FromSvg(commands, fillRule) : null;

    /// <summary>Returns the path data of a <c>shape()</c> function in a reference box, and its fill rule; null when the shape is not valid.</summary>
    internal static List<SvgPathCommand>? CreateShapeCommands(List<CssComponentValue> values, PhysicalRect box, double zoom, out FillRule fillRule)
    {
        var commands = new List<SvgPathCommand>();
        fillRule = FillRule.NonZero;
        var segmentStart = new PhysicalOffset(box.X, box.Y);
        var current = segmentStart;
        var first = true;
        foreach (var arguments in CssValueComputer.SplitArguments(values))
        {
            var group = ValueMatcher.RemoveWhitespace(arguments);
            if (group.Count == 0)
                return null;

            if (first)
            {
                // [<fill-rule>]? from <position>
                var index = 0;
                if (group[0] is CssPreservedToken { Type: CssTokenType.Ident } rule && !IsIdent(rule, "from"))
                {
                    fillRule = InfraAscii.EqualsIgnoreAsciiCase(rule.Token.Value, "evenodd") ? FillRule.EvenOdd : FillRule.NonZero;
                    index++;
                }

                if (index >= group.Count || !IsIdent(group[index], "from") || ResolvePosition(group.GetRange(index + 1, group.Count - index - 1), box, zoom) is not { } start)
                    return null;

                current = start;
                segmentStart = start;
                commands.Add(new SvgPathCommand('M', [(float)start.X, (float)start.Y]));
                first = false;
                continue;
            }

            var name = group[0] is CssPreservedToken { Type: CssTokenType.Ident } command ? InfraAscii.ToAsciiLowercase(command.Token.Value) : null;
            switch (name)
            {
                case "close":
                    commands.Add(new SvgPathCommand('Z', []));
                    current = segmentStart;
                    break;

                case "move" or "line":
                {
                    if (ResolveEndPoint(group, 1, current, box, zoom, out _) is not { } end)
                        return null;

                    commands.Add(new SvgPathCommand(name == "move" ? 'M' : 'L', [(float)end.X, (float)end.Y]));
                    if (name == "move")
                    {
                        segmentStart = end;
                    }

                    current = end;
                    break;
                }

                case "hline" or "vline":
                {
                    if (group.Count != 3)
                        return null;

                    var horizontal = name == "hline";
                    var by = IsIdent(group[1], "by");
                    if (!by && !IsIdent(group[1], "to"))
                        return null;

                    double? coordinate = null;
                    if (group[2] is CssPreservedToken { Type: CssTokenType.Ident } keyword)
                    {
                        var size = horizontal ? box.Width : box.Height;
                        coordinate = InfraAscii.ToAsciiLowercase(keyword.Token.Value) switch
                        {
                            "left" or "top" or "x-start" or "y-start" => 0,
                            "center" => size / 2,
                            "right" or "bottom" or "x-end" or "y-end" => size,
                            _ => null,
                        };
                    }
                    else if (LengthPercentage.TryParse(group[2], zoom, nonNegative: false, out var length))
                    {
                        coordinate = length.Resolve(horizontal ? box.Width : box.Height);
                    }

                    if (coordinate is not { } value)
                        return null;

                    var end = horizontal
                        ? new PhysicalOffset(by ? current.X + value : box.X + value, current.Y)
                        : new PhysicalOffset(current.X, by ? current.Y + value : box.Y + value);
                    commands.Add(new SvgPathCommand('L', [(float)end.X, (float)end.Y]));
                    current = end;
                    break;
                }

                case "curve" or "smooth":
                {
                    var with = group.FindIndex(value => IsIdent(value, "with"));
                    var endPart = with < 0 ? group : group.GetRange(0, with);
                    if (ResolveEndPoint(endPart, 1, current, box, zoom, out var relative) is not { } end)
                        return null;

                    var controls = new List<PhysicalOffset>();
                    if (with >= 0)
                    {
                        var rest = group.GetRange(with + 1, group.Count - with - 1);
                        var slash = rest.FindIndex(value => value is CssPreservedToken { Type: CssTokenType.Delim } delim && delim.Token.Value == "/");
                        foreach (var part in slash < 0 ? [rest] : new[] { rest.GetRange(0, slash), rest.GetRange(slash + 1, rest.Count - slash - 1) })
                        {
                            if (ResolveControlPoint(part, current, end, relative, box, zoom) is not { } control)
                                return null;

                            controls.Add(control);
                        }
                    }

                    if (name == "curve")
                    {
                        if (controls.Count == 1)
                        {
                            commands.Add(new SvgPathCommand('Q', [(float)controls[0].X, (float)controls[0].Y, (float)end.X, (float)end.Y]));
                        }
                        else if (controls.Count == 2)
                        {
                            commands.Add(new SvgPathCommand('C', [(float)controls[0].X, (float)controls[0].Y, (float)controls[1].X, (float)controls[1].Y, (float)end.X, (float)end.Y]));
                        }
                        else
                        {
                            return null;
                        }
                    }
                    else if (controls.Count == 0)
                    {
                        commands.Add(new SvgPathCommand('T', [(float)end.X, (float)end.Y]));
                    }
                    else
                    {
                        commands.Add(new SvgPathCommand('S', [(float)controls[0].X, (float)controls[0].Y, (float)end.X, (float)end.Y]));
                    }

                    current = end;
                    break;
                }

                case "arc":
                {
                    var of = group.FindIndex(value => IsIdent(value, "of"));
                    if (of < 0 || ResolveEndPoint(group.GetRange(0, of), 1, current, box, zoom, out _) is not { } end)
                        return null;

                    var radii = new List<LengthPercentage>(2);
                    var sweep = false;
                    var large = false;
                    var rotation = 0d;
                    for (var i = of + 1; i < group.Count; i++)
                    {
                        var value = group[i];
                        if (IsIdent(value, "cw"))
                        {
                            sweep = true;
                        }
                        else if (IsIdent(value, "ccw"))
                        {
                            sweep = false;
                        }
                        else if (IsIdent(value, "large"))
                        {
                            large = true;
                        }
                        else if (IsIdent(value, "small"))
                        {
                            large = false;
                        }
                        else if (IsIdent(value, "rotate") && i + 1 < group.Count && group[i + 1] is CssPreservedToken { Token: var angle })
                        {
                            rotation = angle.Type == CssTokenType.Dimension && CssCalc.TryConvertToCanonicalUnit(angle.NumericValue, angle.Value, out var degrees, out var unit) && unit == "deg" ? degrees : angle.NumericValue;
                            i++;
                        }
                        else if (radii.Count < 2 && LengthPercentage.TryParse(value, zoom, nonNegative: true, out var radius))
                        {
                            radii.Add(radius);
                        }
                        else
                        {
                            return null;
                        }
                    }

                    if (radii.Count == 0)
                        return null;

                    // https://drafts.csswg.org/css-shapes-2/#typedef-shape-arc-command: "If only one <length-percentage> is specified,
                    // it's used for both radii, with percentages resolving against the direction-agnostic size of the reference box
                    // (similar to the circle() function)"
                    var rx = radii.Count > 1 ? radii[0].Resolve(box.Width) : radii[0].Resolve(Math.Sqrt((box.Width * box.Width) + (box.Height * box.Height)) / Math.Sqrt(2));
                    var ry = radii.Count > 1 ? radii[1].Resolve(box.Height) : rx;
                    commands.Add(new SvgPathCommand('A', [(float)rx, (float)ry, (float)rotation, large ? 1 : 0, sweep ? 1 : 0, (float)end.X, (float)end.Y]));
                    current = end;
                    break;
                }

                default:
                    return null;
            }
        }

        return first ? null : commands;
    }

    /// <summary>Resolves a &lt;command-end-point&gt; (<c>to &lt;position&gt;</c> or <c>by &lt;coordinate-pair&gt;</c>) starting at <paramref name="index"/>.</summary>
    private static PhysicalOffset? ResolveEndPoint(List<CssComponentValue> group, int index, PhysicalOffset current, PhysicalRect box, double zoom, out bool relative)
    {
        relative = false;
        if (index >= group.Count)
            return null;

        var rest = group.GetRange(index + 1, group.Count - index - 1);
        if (IsIdent(group[index], "to"))
            return ResolvePosition(rest, box, zoom);

        if (!IsIdent(group[index], "by") || ResolveCoordinatePair(rest, box, zoom) is not { } delta)
            return null;

        relative = true;
        return new PhysicalOffset(current.X + delta.X, current.Y + delta.Y);
    }

    /// <summary>
    /// Resolves a &lt;control-point&gt;: a position (absolute commands), or a coordinate pair relative to the start of the segment
    /// (the default of the relative commands), its end, or the origin of the reference box (the default of the absolute commands).
    /// </summary>
    private static PhysicalOffset? ResolveControlPoint(List<CssComponentValue> values, PhysicalOffset start, PhysicalOffset end, bool relativeCommand, PhysicalRect box, double zoom)
    {
        var from = values.FindIndex(value => IsIdent(value, "from"));
        var coordinates = from < 0 ? values : values.GetRange(0, from);
        PhysicalOffset anchor;
        if (from >= 0 && from + 1 < values.Count)
        {
            anchor = IsIdent(values[from + 1], "start") ? start : IsIdent(values[from + 1], "end") ? end : new PhysicalOffset(box.X, box.Y);
        }
        else if (relativeCommand)
        {
            anchor = start;
        }
        else
        {
            return ResolvePosition(coordinates, box, zoom);
        }

        if (ResolveCoordinatePair(coordinates, box, zoom) is not { } delta)
            return null;

        return new PhysicalOffset(anchor.X + delta.X, anchor.Y + delta.Y);
    }

    private static PhysicalOffset? ResolveCoordinatePair(List<CssComponentValue> values, PhysicalRect box, double zoom)
    {
        if (values.Count != 2 || !LengthPercentage.TryParse(values[0], zoom, nonNegative: false, out var x) || !LengthPercentage.TryParse(values[1], zoom, nonNegative: false, out var y))
            return null;

        return new PhysicalOffset(x.Resolve(box.Width), y.Resolve(box.Height));
    }

    /// <summary>A rectangle from its edges; the edges that cross are moved to their middle when <paramref name="normalize"/>, else the rectangle is empty.</summary>
    private static PhysicalRect FromEdges(double left, double top, double right, double bottom, bool normalize)
    {
        if (normalize)
        {
            // "If the sum of a pair of insets is greater than the dimension in that axis, the used values are proportionally reduced": an empty box
            if (right < left)
            {
                left = right = (left + right) / 2;
            }

            if (bottom < top)
            {
                top = bottom = (top + bottom) / 2;
            }
        }

        return new PhysicalRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
