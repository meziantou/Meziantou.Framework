using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The offset transform of a box (https://drafts.fxtf.org/motion-1/#calculating-the-computed-distance-along-a-path): the box is
/// moved so that its anchor point (<c>offset-anchor</c>, the transform origin by default) is at the distance
/// <c>offset-distance</c> along its offset path (<c>offset-path</c>), and rotated around the anchor point by
/// <c>offset-rotate</c> (the direction of the path by default). The offset transform is applied after the <c>translate</c>,
/// <c>rotate</c> and <c>scale</c> properties and before <c>transform</c> (https://drafts.csswg.org/css-transforms-2/#ctm).
/// </summary>
/// <remarks>
/// <para>
/// The reference box of the path is the box of the containing block of the element given by the <c>&lt;coord-box&gt;</c> of
/// <c>offset-path</c> (its border box by default): the basic shapes, the rays and the shapes of the SVG elements referenced by
/// <c>url()</c> are in its coordinates, and a <c>&lt;coord-box&gt;</c> alone is the edge of the box with its rounded corners.
/// As in the browsers, the coordinates of <c>path()</c> are relative to the position of the element itself.
/// </para>
/// <para>
/// The paths start as the browsers draw them: a circle or an ellipse at its rightmost point, a rectangle after its top left
/// corner, clockwise. The distance wraps around a closed path and is clamped to an open one; it is not clamped along a ray.
/// </para>
/// </remarks>
internal static class MotionPath
{
    /// <summary>Whether the style has an offset path (<c>offset-path</c> is not <c>none</c>): the box is transformed.</summary>
    public static bool HasOffsetPath(ComputedStyle style) => style.GetComputedValue("offset-path") is not ("none" or "");

    /// <summary>
    /// Returns the offset transform of a box fragment as a 4x4 matrix (column-major order) that is applied in the coordinates
    /// translated by the transform origin, or null when the box has no offset path or when its path is not valid. The
    /// coordinates are the ones of the element: the origin of the border box of the fragment, without the effective zoom.
    /// </summary>
    /// <param name="fragment">The fragment of the box.</param>
    /// <param name="origin">The transform origin.</param>
    /// <param name="transformBox">The reference box of the transforms (<c>transform-box</c>), which <c>offset-anchor</c> refers to.</param>
    public static double[]? GetMatrix(BoxFragment fragment, PhysicalOffset origin, PhysicalRect transformBox)
    {
        var style = fragment.Box.Style;
        var zoom = style.Layout.EffectiveZoom;
        var coordBox = GetCoordBox(style.GetComputedValue("offset-path"));
        var (referenceBox, position) = GetReferenceBox(fragment, coordBox, zoom);
        var boxSize = new PhysicalSize(fragment.Size.Width / zoom, fragment.Size.Height / zoom);
        return GetMatrix(style, fragment.Box.Element, referenceBox, position, boxSize, origin, transformBox);
    }

    /// <summary>
    /// Returns the offset transform of an SVG element without CSS box, in its user space: the reference box of its path is its
    /// SVG viewport, and the coordinates of the paths are the ones of the user space.
    /// </summary>
    /// <param name="style">The style of the element.</param>
    /// <param name="element">The element.</param>
    /// <param name="viewport">The size of the SVG viewport of the element.</param>
    /// <param name="origin">The transform origin, in user space.</param>
    /// <param name="transformBox">The reference box of the transforms, in user space.</param>
    public static double[]? GetMatrix(ComputedStyle style, Dom.Element element, PhysicalSize viewport, PhysicalOffset origin, PhysicalRect transformBox)
        => GetMatrix(style, element, new RoundedRect(new PhysicalRect(0, 0, viewport.Width, viewport.Height), default), default, transformBox.Size, origin, transformBox);

    private static string? GetCoordBox(string value)
    {
        foreach (var component in CssParser.ParseListOfComponentValues(value))
        {
            if (component is CssPreservedToken { Type: CssTokenType.Ident } ident)
                return InfraAscii.ToAsciiLowercase(ident.Token.Value);
        }

        return null;
    }

    private static double[]? GetMatrix(ComputedStyle style, Dom.Element? element, RoundedRect referenceBox, PhysicalOffset position, PhysicalSize boxSize, PhysicalOffset origin, PhysicalRect transformBox)
    {
        var value = style.GetComputedValue("offset-path");
        if (value is "none" or "")
            return null;

        CssFunction? shape = null;
        CssComponentValue? url = null;
        foreach (var component in ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)))
        {
            switch (component)
            {
                case CssPreservedToken { Type: CssTokenType.Url }:
                case CssFunction function when InfraAscii.ToAsciiLowercase(function.Name) is "url" or "src":
                    url = component;
                    break;

                case CssFunction function:
                    shape = function;
                    break;
            }
        }

        // The point of the path and its direction (a unit vector), in the coordinates of the element
        var size = referenceBox.Rect;
        PhysicalOffset point;
        (double X, double Y) direction;
        var shapeName = shape is null ? null : InfraAscii.ToAsciiLowercase(shape.Name);
        if (shapeName == "ray")
        {
            if (GetPointOnRay(style, shape!, size, position, boxSize) is not { } ray)
                return null;

            (point, var rayAngle) = ray;
            point = new PhysicalOffset(point.X - position.X, point.Y - position.Y);
            direction = GetCosSin(rayAngle);
        }
        else
        {
            List<SvgPathCommand>? commands;
            var relativeToReferenceBox = true;
            if (url is not null)
            {
                // The shape of an SVG element, in the coordinates of the reference box; as in the browsers, a reference to
                // another element is a path of one point at the position of the box
                commands = element is not null && SvgResources.GetReferencedElement(element, url) is Dom.SvgGeometryElement referenced ? SvgShapeGeometry.GetPath(referenced) : null;
                if (commands is null || commands.Count == 0)
                {
                    commands = [Command('M', 0, 0)];
                    relativeToReferenceBox = false;
                }
            }
            else if (shapeName == "path")
            {
                commands = CssPath.TryParse(shape!, out _, out var pathCommands) ? pathCommands : null;
                relativeToReferenceBox = false;
            }
            else if (shape is not null)
            {
                commands = GetShapeCommands(style, shape, shapeName!, size, position);
            }
            else
            {
                commands = GetRoundedRectCommands(referenceBox);
            }

            if (commands is null || commands.Count == 0)
                return null;

            var geometry = new SvgPathGeometry(commands);
            var length = geometry.TotalLength;
            var distance = GetDistance(style, length);
            if (commands[^1].Type is 'Z' or 'z' && length > 0)
            {
                distance %= length;
                if (distance < 0)
                {
                    distance += length;
                }
            }
            else
            {
                distance = Math.Clamp(distance, 0, length);
            }

            var (pathPoint, tangent) = geometry.GetPointAndTangentAtLength(distance);
            point = relativeToReferenceBox ? new PhysicalOffset(pathPoint.X - position.X, pathPoint.Y - position.Y) : new PhysicalOffset(pathPoint.X, pathPoint.Y);
            direction = (Snap(tangent.X), Snap(tangent.Y));
        }

        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            return null;

        // https://drafts.fxtf.org/motion-1/#offset-rotate-property: the direction of the path (auto), turned by the angle. The
        // rotation is computed from the direction vector: the rotations by a multiple of a quarter turn are exact.
        var (auto, rotation) = ParseRotate(style.GetComputedValue("offset-rotate"));
        var (rotationCos, rotationSin) = GetCosSin(rotation);
        var (cos, sin) = auto ? ((direction.X * rotationCos) - (direction.Y * rotationSin), (direction.Y * rotationCos) + (direction.X * rotationSin)) : (rotationCos, rotationSin);

        // The anchor point is moved to the point of the path: the transform origin, or the point of offset-anchor in the
        // reference box of the transforms
        var anchor = GetAnchor(style, transformBox) ?? origin;
        var anchorX = origin.X - anchor.X;
        var anchorY = origin.Y - anchor.Y;

        // translate(point - origin) rotate(angle) translate(origin - anchor)
        return
        [
            cos, sin, 0, 0,
            -sin, cos, 0, 0,
            0, 0, 1, 0,
            point.X - origin.X + (cos * anchorX) - (sin * anchorY), point.Y - origin.Y + (sin * anchorX) + (cos * anchorY), 0, 1,
        ];
    }

    /// <summary>Returns the cosine and the sine of an angle in degrees, exact for the multiples of a quarter turn.</summary>
    private static (double Cos, double Sin) GetCosSin(double degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        return normalized switch
        {
            0 => (1, 0),
            90 => (0, 1),
            180 => (-1, 0),
            270 => (0, -1),
            _ => (Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180)),
        };
    }

    /// <summary>Rounds a component of a unit vector that is within the rounding errors of 0, 1 or -1.</summary>
    private static double Snap(double value)
    {
        const double Epsilon = 1e-12;
        return Math.Abs(value) < Epsilon ? 0 : Math.Abs(value - 1) < Epsilon ? 1 : Math.Abs(value + 1) < Epsilon ? -1 : value;
    }

    /// <summary>
    /// Returns the reference box of the offset path of a fragment (at the origin, with the radii and the shapes of its corners),
    /// and the position of the border box of the fragment in it, in the coordinates of the element (divided by the effective
    /// zoom): the box of the containing block of the box.
    /// </summary>
    private static (RoundedRect Box, PhysicalOffset Position) GetReferenceBox(BoxFragment fragment, string? coordBox, double zoom)
    {
        var x = fragment.Offset.X;
        var y = fragment.Offset.Y;
        BoxFragment? container = null;
        for (var ancestor = fragment.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is BoxFragment { Box.IsInlineBox: false } box)
            {
                container = box;
                break;
            }

            x += ancestor.Offset.X;
            y += ancestor.Offset.Y;
        }

        if (container is null)
            return (new RoundedRect(new PhysicalRect(0, 0, fragment.Size.Width / zoom, fragment.Size.Height / zoom), default), default);

        // https://drafts.fxtf.org/css-masking/#typedef-geometry-box: "for elements with associated CSS layout box, the used
        // value for fill-box is content-box and for stroke-box and view-box is border-box"
        var border = RoundedRect.FromBorderBox(container, container.BorderBoxRect);
        var rounded = coordBox switch
        {
            "content-box" or "fill-box" => border.Inset(container.Border.Add(container.Scrollbars)).Inset(container.Padding),
            "padding-box" => border.Inset(container.Border.Add(container.Scrollbars)),
            _ => border,
        };

        var rect = rounded.Rect;
        var radii = rounded.Radii;
        var referenceBox = new RoundedRect(new PhysicalRect(0, 0, rect.Width / zoom, rect.Height / zoom), new BorderRadii(Scale(radii.TopLeft), Scale(radii.TopRight), Scale(radii.BottomRight), Scale(radii.BottomLeft)))
        {
            Contour = rounded.Contour?.Translate(new PhysicalOffset(-rect.X, -rect.Y)).Scale(1 / zoom),
        };

        return (referenceBox, new PhysicalOffset((x - rect.X) / zoom, (y - rect.Y) / zoom));

        (double X, double Y) Scale((double X, double Y) radius) => (radius.X / zoom, radius.Y / zoom);
    }

    /// <summary>Returns the distance of <c>offset-distance</c> along a path of a length (the percentages refer to the length).</summary>
    private static double GetDistance(ComputedStyle style, double length)
    {
        var distance = LengthPercentage.TryParse(style.GetComputedValue("offset-distance"), 1, nonNegative: false, out var parsed) ? parsed.Resolve(length) : 0;
        return double.IsFinite(distance) ? distance : 0;
    }

    /// <summary>
    /// Returns the offset starting position (https://drafts.fxtf.org/motion-1/#offset-position-property) in the reference box:
    /// the position of the box for <c>auto</c>, the center of the reference box for <c>normal</c>, else the position.
    /// </summary>
    private static PhysicalOffset GetStartingPosition(ComputedStyle style, PhysicalRect referenceBox, PhysicalOffset position)
    {
        var value = style.GetComputedValue("offset-position");
        if (value == "auto")
            return position;

        if (value != "normal" && BasicShapePaths.ResolvePosition(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)), referenceBox, 1) is { } resolved)
            return resolved;

        return new PhysicalOffset(referenceBox.Width / 2, referenceBox.Height / 2);
    }

    /// <summary>Returns the anchor point of <c>offset-anchor</c> in the coordinates of the element, or null for <c>auto</c> (the transform origin).</summary>
    private static PhysicalOffset? GetAnchor(ComputedStyle style, PhysicalRect transformBox)
    {
        var value = style.GetComputedValue("offset-anchor");
        if (value is "auto" or "")
            return null;

        return BasicShapePaths.ResolvePosition(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)), transformBox, 1);
    }

    /// <summary>Parses a computed value of <c>offset-rotate</c>: whether the box follows the direction of the path, and the angle added to it in degrees.</summary>
    private static (bool Auto, double Angle) ParseRotate(string value)
    {
        var auto = false;
        var angle = 0d;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part)
            {
                case "auto":
                    auto = true;
                    break;

                case "reverse":
                    auto = true;
                    angle += 180;
                    break;

                default:
                    angle += ParseAngle(part) ?? 0;
                    break;
            }
        }

        return (auto, angle);
    }

    private static double? ParseAngle(string value)
    {
        foreach (var (unit, factor) in (ReadOnlySpan<(string, double)>)[("deg", 1), ("grad", 0.9), ("rad", 180 / Math.PI), ("turn", 360)])
        {
            if (value.EndsWith(unit, StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - unit.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return number * factor;
        }

        return null;
    }

    /// <summary>
    /// Returns the point of a <c>ray()</c> in the reference box and the direction of the ray in degrees
    /// (https://drafts.fxtf.org/motion-1/#ray-function): the ray starts at its position (<c>at</c>, else the offset starting
    /// position) in the direction of its angle (0deg is up, clockwise); its length is the distance to the side or to the corner
    /// of its size (<c>sides</c> is the distance to the edge of the box along the ray), reduced with <c>contain</c> by the half
    /// of the largest side of the box. The distance is not clamped to the ray.
    /// </summary>
    private static (PhysicalOffset Point, double Angle)? GetPointOnRay(ComputedStyle style, CssFunction ray, PhysicalRect referenceBox, PhysicalOffset position, PhysicalSize boxSize)
    {
        var arguments = ValueMatcher.RemoveWhitespace(ray.Value);
        double? angle = null;
        var size = "closest-side";
        var contain = false;
        PhysicalOffset? start = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case CssPreservedToken { Type: CssTokenType.Ident } ident when InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "at"):
                    start = BasicShapePaths.ResolvePosition(arguments.GetRange(i + 1, arguments.Count - i - 1), referenceBox, 1);
                    if (start is null)
                        return null;

                    i = arguments.Count;
                    break;

                case CssPreservedToken { Type: CssTokenType.Ident } ident when InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "contain"):
                    contain = true;
                    break;

                case CssPreservedToken { Type: CssTokenType.Ident } ident:
                    size = InfraAscii.ToAsciiLowercase(ident.Token.Value);
                    break;

                case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
                    angle = ParseAngle(CssSerializer.SerializeNumber(dimension.Token.NumericValue) + InfraAscii.ToAsciiLowercase(dimension.Token.Value));
                    break;

                case CssPreservedToken { Type: CssTokenType.Number, Token.NumericValue: 0 }:
                    angle = 0;
                    break;
            }
        }

        if (angle is not { } degrees || !double.IsFinite(degrees))
            return null;

        var origin = start ?? GetStartingPosition(style, referenceBox, position);
        var radians = degrees * Math.PI / 180;
        var directionX = Math.Sin(radians);
        var directionY = -Math.Cos(radians);
        var left = Math.Abs(origin.X);
        var right = Math.Abs(referenceBox.Width - origin.X);
        var top = Math.Abs(origin.Y);
        var bottom = Math.Abs(referenceBox.Height - origin.Y);
        var length = size switch
        {
            "farthest-side" => Math.Max(Math.Max(left, right), Math.Max(top, bottom)),
            "closest-corner" => Math.Sqrt(Math.Pow(Math.Min(left, right), 2) + Math.Pow(Math.Min(top, bottom), 2)),
            "farthest-corner" => Math.Sqrt(Math.Pow(Math.Max(left, right), 2) + Math.Pow(Math.Max(top, bottom), 2)),
            "sides" => GetDistanceToSides(),
            _ => Math.Min(Math.Min(left, right), Math.Min(top, bottom)),
        };

        if (contain)
        {
            length = Math.Max(0, length - (Math.Max(boxSize.Width, boxSize.Height) / 2));
        }

        var distance = GetDistance(style, length);
        return (new PhysicalOffset(origin.X + (directionX * distance), origin.Y + (directionY * distance)), degrees - 90);

        // The distance from the starting position to the edge of the reference box along the ray; 0 when it is outside of the box
        double GetDistanceToSides()
        {
            if (origin.X < 0 || origin.X > referenceBox.Width || origin.Y < 0 || origin.Y > referenceBox.Height)
                return 0;

            const double Epsilon = 1e-9;
            var horizontal = directionX > Epsilon ? (referenceBox.Width - origin.X) / directionX : directionX < -Epsilon ? origin.X / -directionX : double.PositiveInfinity;
            var vertical = directionY > Epsilon ? (referenceBox.Height - origin.Y) / directionY : directionY < -Epsilon ? origin.Y / -directionY : double.PositiveInfinity;
            var result = Math.Min(horizontal, vertical);
            return double.IsFinite(result) ? result : 0;
        }
    }

    /// <summary>
    /// Returns the path of a basic shape in the reference box: a circle or an ellipse without position is centered at the
    /// offset starting position.
    /// </summary>
    private static List<SvgPathCommand>? GetShapeCommands(ComputedStyle style, CssFunction shape, string name, PhysicalRect referenceBox, PhysicalOffset position)
    {
        if (name == "shape")
            return BasicShapePaths.CreateShapeCommands(shape.Value, referenceBox, 1, out _);

        PhysicalOffset? center = name is "circle" or "ellipse" ? GetStartingPosition(style, referenceBox, position) : null;
        if (BasicShapePaths.Resolve(shape, referenceBox, 1, center) is not { } geometry)
            return null;

        if (geometry.Polygon is { Length: > 0 } vertices)
        {
            var commands = new List<SvgPathCommand>(vertices.Length + 1) { Command('M', vertices[0].X, vertices[0].Y) };
            for (var i = 1; i < vertices.Length; i++)
            {
                commands.Add(Command('L', vertices[i].X, vertices[i].Y));
            }

            commands.Add(Command('Z'));
            return commands;
        }

        if (geometry.Rectangle is not { } rounded)
            return null;

        if (name is not ("circle" or "ellipse"))
            return GetRoundedRectCommands(rounded);

        // The ellipse starts at its rightmost point, clockwise
        var rect = rounded.Rect;
        var radiusX = rect.Width / 2;
        var radiusY = rect.Height / 2;
        var centerX = rect.X + radiusX;
        var centerY = rect.Y + radiusY;
        return
        [
            Command('M', centerX + radiusX, centerY),
            Command('A', radiusX, radiusY, 0, 1, 1, centerX - radiusX, centerY),
            Command('A', radiusX, radiusY, 0, 1, 1, centerX + radiusX, centerY),
            Command('Z'),
        ];
    }

    /// <summary>
    /// Returns the path of a rounded rectangle: it starts after its top left corner, clockwise. The corners shaped by
    /// <c>corner-shape</c> are the ones of the contour of the box (flattened).
    /// </summary>
    private static List<SvgPathCommand> GetRoundedRectCommands(RoundedRect rounded)
    {
        if (rounded.Contour is { } contour && contour.GetShape(rounded.Rect, rounded.HasAdjustedOutsets).Polygon is { Count: > 0 } polygon)
        {
            var contourCommands = new List<SvgPathCommand>(polygon.Count + 1) { Command('M', polygon[0].X, polygon[0].Y) };
            for (var i = 1; i < polygon.Count; i++)
            {
                contourCommands.Add(Command('L', polygon[i].X, polygon[i].Y));
            }

            contourCommands.Add(Command('Z'));
            return contourCommands;
        }

        var rect = rounded.Rect;
        var radii = rounded.Radii;
        var commands = new List<SvgPathCommand>(10) { Command('M', rect.Left + radii.TopLeft.X, rect.Top) };
        commands.Add(Command('L', rect.Right - radii.TopRight.X, rect.Top));
        AddCorner(radii.TopRight, rect.Right, rect.Top + radii.TopRight.Y);
        commands.Add(Command('L', rect.Right, rect.Bottom - radii.BottomRight.Y));
        AddCorner(radii.BottomRight, rect.Right - radii.BottomRight.X, rect.Bottom);
        commands.Add(Command('L', rect.Left + radii.BottomLeft.X, rect.Bottom));
        AddCorner(radii.BottomLeft, rect.Left, rect.Bottom - radii.BottomLeft.Y);
        commands.Add(Command('L', rect.Left, rect.Top + radii.TopLeft.Y));
        AddCorner(radii.TopLeft, rect.Left + radii.TopLeft.X, rect.Top);
        commands.Add(Command('Z'));
        return commands;

        void AddCorner((double X, double Y) radius, double x, double y)
        {
            if (radius.X > 0 && radius.Y > 0)
            {
                commands.Add(Command('A', radius.X, radius.Y, 0, 0, 1, x, y));
            }
        }
    }

    private static SvgPathCommand Command(char type, params double[] values) => new(type, Array.ConvertAll(values, static value => (float)value));
}
