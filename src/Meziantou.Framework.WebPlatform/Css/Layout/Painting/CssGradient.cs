using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A gradient of a computed value (https://drafts.csswg.org/css-images-4/#gradients): <c>linear-gradient()</c>,
/// <c>radial-gradient()</c>, <c>conic-gradient()</c>, their <c>repeating-</c> forms and the legacy <c>-webkit-</c> forms. It
/// becomes a shader for a concrete object size: the gradient line or ray, the color stops after the fixup
/// (https://drafts.csswg.org/css-images-4/#color-stop-fixup), and the colors interpolated in the interpolation color space
/// (https://drafts.csswg.org/css-images-4/#coloring-gradient-line) expanded into sRGB stops when it is not sRGB, as are the
/// transition hints.
/// </summary>
internal sealed class CssGradient
{
    /// <summary>
    /// The number of stops a segment of the gradient is sampled into when it is not interpolated in sRGB or has a transition
    /// hint. The colors are interpolated in sRGB between two stops: an interval whose middle color differs from the color
    /// interpolated in sRGB (the curve of the color space is steep, e.g. near black, or has an angle, e.g. at the hues of the
    /// primary colors in hsl) by more than <see cref="SampleTolerance"/> is split again, up to <see cref="MaxSampleDepth"/>
    /// times and down to intervals of <see cref="MinSampleDistance"/> px.
    /// </summary>
    private const int SegmentSamples = 24;

    private const int MaxSampleDepth = 6;

    private const double MinSampleDistance = 0.5;

    private const float SampleTolerance = 0.25f / 255;

    /// <summary>
    /// The largest length of the geometry of a gradient (the largest length of the layout of Blink): the infinite or huge
    /// positions and sizes are clamped to it.
    /// </summary>
    private const double MaxLength = 1 << 25;

    /// <summary>The coordinates over which the single-precision numbers of the shaders cannot place the stops of a gradient in its box.</summary>
    private const double MaxShaderCoordinate = 1e6;

    /// <summary>The maximum number of stops of an expanded repeating conic gradient.</summary>
    private const int MaxRepeatedStops = 8192;

    private enum GradientKind
    {
        Linear,
        Radial,
        Conic,
    }

    private enum RadialSize
    {
        FarthestCorner,
        FarthestSide,
        ClosestCorner,
        ClosestSide,
        Explicit,
    }

    /// <summary>A color stop or a transition hint: its color (null for a hint) and its position (a length-percentage along the gradient line, or a fraction of a turn for a conic gradient).</summary>
    private readonly record struct Stop(CssColorComponents? Color, LengthPercentage? Position, double? Turn);

    private GradientKind _kind;
    private bool _repeating;
    private double _angle = 180; // to bottom
    private (int X, int Y)? _corner;
    private bool _circle;
    private RadialSize _size = RadialSize.FarthestCorner;
    private LengthPercentage _radiusX;
    private LengthPercentage _radiusY;
    private LengthPercentage _positionX = LengthPercentage.FromPercentage(50);
    private LengthPercentage _positionY = LengthPercentage.FromPercentage(50);
    private double _fromAngle;
    private string _space = "oklab";
    private CssColorMix.HueMethod _hueMethod = CssColorMix.HueMethod.Shorter;
    private readonly List<Stop> _stops = [];

    private CssGradient()
    {
    }

    /// <summary>Parses a gradient function of a computed value, or returns null when it is not a valid gradient.</summary>
    public static CssGradient? Parse(CssFunction function, ComputedStyle style)
    {
        var name = InfraAscii.ToAsciiLowercase(function.Name);
        var legacy = name.StartsWith("-webkit-", StringComparison.Ordinal);
        if (legacy)
        {
            name = name["-webkit-".Length..];
        }

        var gradient = new CssGradient();
        if (name.StartsWith("repeating-", StringComparison.Ordinal))
        {
            gradient._repeating = true;
            name = name["repeating-".Length..];
        }

        gradient._kind = name switch
        {
            "linear-gradient" => GradientKind.Linear,
            "radial-gradient" => GradientKind.Radial,
            "conic-gradient" when !legacy => GradientKind.Conic,
            _ => (GradientKind)(-1),
        };

        if (!Enum.IsDefined(gradient._kind))
            return null;

        var segments = ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value));
        if (segments.Count == 0)
            return null;

        var zoom = style.Layout.EffectiveZoom;
        var first = 0;
        var explicitSpace = false;
        if (legacy && gradient._kind == GradientKind.Radial)
        {
            // -webkit-radial-gradient([<position>,] [<shape> <size>,] stops)
            while (first < segments.Count && segments[first].Count > 0 && !ValueMatcher.IsColor(segments[first][0]))
            {
                if (!gradient.ParseRadialPrelude(segments[first], zoom, legacy: true))
                    return null;

                first++;
            }
        }
        else if (segments[0].Count > 0 && !ValueMatcher.IsColor(segments[0][0]))
        {
            if (!gradient.ParsePrelude(segments[0], legacy, zoom, out explicitSpace))
                return null;

            first = 1;
        }

        var allLegacy = true;
        for (var i = first; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.Count == 0)
                return null;

            if (!ValueMatcher.IsColor(segment[0]))
            {
                // A transition hint: a single position between two color stops
                if (segment.Count != 1 || !gradient.TryParsePosition(segment[0], zoom, out var hint, out var hintTurn))
                    return null;

                gradient._stops.Add(new Stop(Color: null, hint, hintTurn));
                continue;
            }

            var color = ParseColor(style, segment[0], out var isLegacy);
            if (color is null)
                return null;

            allLegacy &= isLegacy;
            if (segment.Count == 1)
            {
                gradient._stops.Add(new Stop(color, Position: null, Turn: null));
                continue;
            }

            for (var j = 1; j < segment.Count && j <= 2; j++)
            {
                if (!gradient.TryParsePosition(segment[j], zoom, out var position, out var turn))
                    return null;

                gradient._stops.Add(new Stop(color, position, turn));
            }
        }

        // https://drafts.csswg.org/css-color-4/#interpolation-space: the legacy colors are interpolated in sRGB when no
        // interpolation method is specified, the other ones in Oklab
        if (!explicitSpace && allLegacy)
        {
            gradient._space = "srgb";
        }

        return gradient.CountColorStops() >= 1 ? gradient : null;
    }

    private int CountColorStops()
    {
        var count = 0;
        foreach (var stop in _stops)
        {
            if (stop.Color is not null)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Parses a color of a stop in the color space of its definition (currentcolor is the color of the style).</summary>
    private static CssColorComponents? ParseColor(ComputedStyle style, CssComponentValue value, out bool isLegacy)
    {
        isLegacy = true;
        CssColor? color;
        if (GetIdent(value) == "currentcolor")
        {
            color = CssValueComputer.ParseCssColorValue(style.GetUsedValue("color"));
        }
        else
        {
            // The colors that depend on currentcolor (color-mix(), the relative colors) are computed with the color of the style
            var serialized = CssSerializer.SerializeValue([value], property: null);
            if (serialized.Contains("currentcolor", StringComparison.Ordinal))
            {
                serialized = CssValueComputer.ResolveCurrentColor(style, "background-color", serialized, style.GetUsedValue("color"));
            }

            color = CssValueComputer.ParseCssColorValue(serialized);
        }

        if (color is null)
            return null;

        // The legacy colors (https://drafts.csswg.org/css-color-4/#legacy-color-syntax): an hsl() or hwb() color that has a
        // missing component is not converted to rgb(), and is still a legacy color
        isLegacy = color.LegacyRgb || color.Function is "hsl" or "hwb";
        return CssColorConversion.FromColor(color);
    }

    private bool ParsePrelude(List<CssComponentValue> prelude, bool legacy, double zoom, out bool explicitSpace)
    {
        explicitSpace = false;
        var values = prelude;

        // <color-interpolation-method>: in <space> [<hue-interpolation-method> hue], before or after the other parts
        var inIndex = prelude.FindIndex(value => GetIdent(value) == "in");
        if (inIndex >= 0)
        {
            if (inIndex + 1 >= prelude.Count || GetIdent(prelude[inIndex + 1]) is not { } space)
                return false;

            _space = space == "xyz" ? "xyz-d65" : space;
            if (!CssColorConversion.IsSupportedSpace(_space))
            {
                _space = "oklab";
            }

            explicitSpace = true;
            var length = 2;
            if (inIndex + 3 < prelude.Count && GetIdent(prelude[inIndex + 3]) == "hue")
            {
                _hueMethod = GetIdent(prelude[inIndex + 2]) switch
                {
                    "longer" => CssColorMix.HueMethod.Longer,
                    "increasing" => CssColorMix.HueMethod.Increasing,
                    "decreasing" => CssColorMix.HueMethod.Decreasing,
                    _ => CssColorMix.HueMethod.Shorter,
                };
                length = 4;
            }

            values = [.. prelude[..inIndex], .. prelude[(inIndex + length)..]];
        }

        switch (_kind)
        {
            case GradientKind.Linear:
                return ParseLinearPrelude(values, legacy);

            case GradientKind.Radial:
                return ParseRadialPrelude(values, zoom, legacy);

            default:
                return ParseConicPrelude(values, zoom);
        }
    }

    private bool ParseLinearPrelude(List<CssComponentValue> values, bool legacy)
    {
        if (values.Count == 0)
            return true;

        if (values.Count == 1 && TryParseAngle(values[0], out var angle))
        {
            // The legacy angles are counterclockwise from the east (https://compat.spec.whatwg.org/#css-gradient-prefixed)
            _angle = legacy ? 90 - angle : angle;
            return true;
        }

        var sides = values;
        var reverse = legacy;
        if (GetIdent(values[0]) == "to")
        {
            sides = values[1..];
            reverse = false;
        }

        int x = 0, y = 0;
        foreach (var side in sides)
        {
            switch (GetIdent(side))
            {
                case "left":
                    x = -1;
                    break;
                case "right":
                    x = 1;
                    break;
                case "top":
                    y = -1;
                    break;
                case "bottom":
                    y = 1;
                    break;
                case "center":
                    break;
                default:
                    return false;
            }
        }

        // A legacy gradient names its starting side
        if (reverse)
        {
            x = -x;
            y = -y;
        }

        if (x != 0 && y != 0)
        {
            _corner = (x, y);
        }
        else if (x != 0)
        {
            _angle = x > 0 ? 90 : 270;
        }
        else if (y != 0)
        {
            _angle = y > 0 ? 180 : 0;
        }

        return true;
    }

    private bool ParseRadialPrelude(List<CssComponentValue> values, double zoom, bool legacy)
    {
        var lengths = new List<LengthPercentage>(2);
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            switch (GetIdent(value))
            {
                case "circle":
                    _circle = true;
                    break;
                case "ellipse":
                    break;
                case "closest-side" or "contain":
                    _size = RadialSize.ClosestSide;
                    break;
                case "closest-corner":
                    _size = RadialSize.ClosestCorner;
                    break;
                case "farthest-side":
                    _size = RadialSize.FarthestSide;
                    break;
                case "farthest-corner" or "cover":
                    _size = RadialSize.FarthestCorner;
                    break;
                case "at":
                    // The position ends the prelude: the size that precedes it is set below
                    if (!ParsePosition(values[(i + 1)..], zoom))
                        return false;

                    i = values.Count;
                    break;
                default:
                    if (LengthPercentage.TryParse(value, zoom, nonNegative: true, out var length))
                    {
                        lengths.Add(length);
                        break;
                    }

                    // The legacy syntax starts with the position
                    if (legacy)
                        return ParsePosition(values[i..], zoom);

                    return false;
            }
        }

        if (lengths.Count == 1)
        {
            _circle = true;
            _size = RadialSize.Explicit;
            _radiusX = _radiusY = lengths[0];
        }
        else if (lengths.Count == 2)
        {
            _size = RadialSize.Explicit;
            _radiusX = lengths[0];
            _radiusY = lengths[1];
        }

        return true;
    }

    private bool ParseConicPrelude(List<CssComponentValue> values, double zoom)
    {
        for (var i = 0; i < values.Count; i++)
        {
            switch (GetIdent(values[i]))
            {
                case "from" when i + 1 < values.Count && TryParseAngle(values[i + 1], out var from):
                    _fromAngle = from;
                    i++;
                    break;
                case "at":
                {
                    var end = values.FindIndex(i + 1, value => GetIdent(value) == "from");
                    if (!ParsePosition(values[(i + 1)..(end < 0 ? values.Count : end)], zoom))
                        return false;

                    i = end < 0 ? values.Count : end - 1;
                    break;
                }

                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>Parses a &lt;position&gt; (a pair of offsets in the computed values, or keywords).</summary>
    private bool ParsePosition(List<CssComponentValue> values, double zoom)
    {
        var x = LengthPercentage.FromPercentage(50);
        var y = LengthPercentage.FromPercentage(50);
        var axis = 0;
        foreach (var value in values)
        {
            switch (GetIdent(value))
            {
                case "left":
                    x = LengthPercentage.FromPercentage(0);
                    axis = 1;
                    break;
                case "right":
                    x = LengthPercentage.FromPercentage(100);
                    axis = 1;
                    break;
                case "top":
                    y = LengthPercentage.FromPercentage(0);
                    axis = 2;
                    break;
                case "bottom":
                    y = LengthPercentage.FromPercentage(100);
                    axis = 2;
                    break;
                case "center":
                    axis++;
                    break;
                default:
                    if (!LengthPercentage.TryParse(value, zoom, nonNegative: false, out var length))
                        return false;

                    if (axis == 0)
                    {
                        x = length;
                    }
                    else
                    {
                        y = length;
                    }

                    axis++;
                    break;
            }
        }

        _positionX = x;
        _positionY = y;
        return true;
    }

    private bool TryParsePosition(CssComponentValue value, double zoom, out LengthPercentage? position, out double? turn)
    {
        position = null;
        turn = null;
        if (_kind == GradientKind.Conic)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Percentage } percentage)
            {
                turn = percentage.Token.NumericValue / 100;
                return true;
            }

            if (TryParseAngle(value, out var angle))
            {
                turn = angle / 360;
                return true;
            }

            // A calculation of angles and percentages (https://drafts.csswg.org/css-images-4/#typedef-angular-color-stop): the
            // percentages refer to a full turn
            if (value is CssFunction function && CssCalc.IsMathFunction(function.Name)
                && CssCalc.Evaluate(function, new CssCalcContext { PercentageBasis = (360, "deg"), PercentageUnit = "deg" }) is { IsNumeric: true, Unit: "deg" } result && !double.IsNaN(result.Value))
            {
                turn = result.Value / 360;
                return true;
            }

            return false;
        }

        if (!LengthPercentage.TryParse(value, zoom, nonNegative: false, out var length))
            return false;

        position = length;
        return true;
    }

    private static bool TryParseAngle(CssComponentValue value, out double degrees)
    {
        degrees = 0;
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Number, Token.NumericValue: 0 }:
                return true;

            case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
                var number = dimension.Token.NumericValue;
                degrees = InfraAscii.ToAsciiLowercase(dimension.Token.Value) switch
                {
                    "deg" => number,
                    "rad" => number * 180 / Math.PI,
                    "grad" => number * 0.9,
                    "turn" => number * 360,
                    _ => double.NaN,
                };
                return !double.IsNaN(degrees);

            default:
                return false;
        }
    }

    private static string? GetIdent(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Ident } ident ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : null;

    /// <summary>Returns the shader of the gradient drawn in a rectangle from (0, 0) to <paramref name="size"/>, or null when it paints nothing.</summary>
    public DisplayShader? CreateShader(PhysicalSize size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            return null;

        return _kind switch
        {
            GradientKind.Linear => CreateLinear(size),
            GradientKind.Radial => CreateRadial(size),
            _ => CreateConic(size),
        };
    }

    private LinearGradientShader? CreateLinear(PhysicalSize size)
    {
        // https://drafts.csswg.org/css-images-3/#linear-gradient-syntax: the direction of the gradient line; a corner gives the
        // angle that makes the 50% line go through the two other corners
        double dx, dy;
        if (_corner is { } corner)
        {
            dx = corner.X * size.Height;
            dy = corner.Y * size.Width;
            var norm = Math.Sqrt((dx * dx) + (dy * dy));
            dx /= norm;
            dy /= norm;
        }
        else
        {
            var radians = _angle * Math.PI / 180;
            dx = Math.Sin(radians);
            dy = -Math.Cos(radians);
        }

        var length = Math.Abs(size.Width * dx) + Math.Abs(size.Height * dy);
        var center = new PhysicalOffset(size.Width / 2, size.Height / 2);
        var start = new PhysicalOffset(center.X - (dx * length / 2), center.Y - (dy * length / 2));
        if (ResolveStops(length, length) is not { } stops)
            return null;

        var (offsets, colors) = stops;
        var lo = offsets[0];
        var hi = offsets[^1];
        if (_repeating)
        {
            if (hi - lo <= 1e-9)
                return Solid(Average(offsets, colors));
        }
        else
        {
            // The stops outside of the gradient line extend it
            lo = Math.Min(lo, 0);
            hi = Math.Max(hi, 1);
        }

        var result = Normalize(offsets, colors, lo, hi);
        if (length <= 0)
            return Solid(colors[^1]);

        var from = new PhysicalOffset(start.X + (dx * length * lo), start.Y + (dy * length * lo));
        var to = new PhysicalOffset(start.X + (dx * length * hi), start.Y + (dy * length * hi));
        return new LinearGradientShader(from, to, result, _repeating ? SpreadMode.Repeat : SpreadMode.Pad);
    }

    private DisplayShader? CreateRadial(PhysicalSize size)
    {
        var cx = ClampLength(_positionX.Resolve(size.Width));
        var cy = ClampLength(_positionY.Resolve(size.Height));

        // https://drafts.csswg.org/css-images-3/#radial-gradient-syntax: the size of the ending shape
        var left = Math.Abs(cx);
        var right = Math.Abs(size.Width - cx);
        var top = Math.Abs(cy);
        var bottom = Math.Abs(size.Height - cy);
        double rx, ry;
        switch (_size)
        {
            case RadialSize.Explicit:
                rx = Math.Max(0, ClampLength(_radiusX.Resolve(size.Width)));
                ry = _circle ? rx : Math.Max(0, ClampLength(_radiusY.Resolve(size.Height)));
                break;

            case RadialSize.ClosestSide or RadialSize.FarthestSide:
            {
                var closest = _size == RadialSize.ClosestSide;
                var horizontal = closest ? Math.Min(left, right) : Math.Max(left, right);
                var vertical = closest ? Math.Min(top, bottom) : Math.Max(top, bottom);
                if (_circle)
                {
                    rx = ry = closest ? Math.Min(horizontal, vertical) : Math.Max(horizontal, vertical);
                }
                else
                {
                    (rx, ry) = (horizontal, vertical);
                }

                break;
            }

            default:
            {
                // The corner closest to (farthest from) the center; an ellipse keeps the aspect ratio of the closest (farthest) side
                var closest = _size == RadialSize.ClosestCorner;
                var horizontal = closest ? Math.Min(left, right) : Math.Max(left, right);
                var vertical = closest ? Math.Min(top, bottom) : Math.Max(top, bottom);
                if (_circle)
                {
                    rx = ry = Math.Sqrt((horizontal * horizontal) + (vertical * vertical));
                }
                else
                {
                    rx = horizontal * Math.Sqrt(2);
                    ry = vertical * Math.Sqrt(2);
                }

                break;
            }
        }

        // https://drafts.csswg.org/css-images-3/#degenerate-radials: an ending shape with a zero size is a very thin ellipse
        const double Epsilon = 1e-4;
        if (rx <= 0 && ry <= 0)
        {
            rx = ry = Epsilon;
        }
        else if (rx <= 0)
        {
            rx = Epsilon;
            ry = 1e6;
        }
        else if (ry <= 0)
        {
            ry = Epsilon;
            rx = 1e6;
        }

        if (ResolveStops(rx, Math.Min(Math.Max(rx, ry), Math.Max(size.Width, size.Height) * 2)) is not { } stops)
            return null;

        var (offsets, colors) = stops;
        var lo = offsets[0];
        var hi = offsets[^1];
        if (_repeating)
        {
            var period = hi - lo;
            if (period <= 1e-9)
                return Solid(Average(offsets, colors));

            // The radius of the start circle cannot be negative: shift the pattern by whole periods
            if (lo < 0)
            {
                var shift = Math.Ceiling(-lo / period) * period;
                for (var i = 0; i < offsets.Count; i++)
                {
                    offsets[i] += shift;
                }

                lo += shift;
                hi += shift;
            }
        }
        else
        {
            // The colors at negative distances are not visible: the gradient starts with the color at the center
            if (lo < 0)
            {
                CutBefore(offsets, colors, 0);
            }

            lo = 0;
            hi = Math.Max(hi, 1);
        }

        // A gradient whose center or whose stops are very far from its box (e.g. an infinite position) cannot be computed
        // by the shader: when its box has a single color, the gradient is painted as this color
        if ((Math.Abs(cx) > MaxShaderCoordinate || Math.Abs(cy) > MaxShaderCoordinate || rx * hi > MaxShaderCoordinate || ry * hi > MaxShaderCoordinate)
            && GetUniformColor(offsets, colors, size, (x, y) => Math.Sqrt((((x - cx) / rx) * ((x - cx) / rx)) + (((y - cy) / ry) * ((y - cy) / ry)))) is { } uniform)
        {
            return Solid(uniform);
        }

        var result = Normalize(offsets, colors, lo, hi);
        var center = new PhysicalOffset(cx, cy);
        var shader = new RadialGradientShader(center, rx * lo, center, rx * hi, result, _repeating ? SpreadMode.Repeat : SpreadMode.Pad);
        if (Math.Abs(rx - ry) > 1e-9)
        {
            // The ellipse is a circle scaled vertically around the center
            return shader with { LocalTransform = DisplayMatrix.Translation(cx, cy).PreConcat(DisplayMatrix.Scale(1, ry / rx)).PreConcat(DisplayMatrix.Translation(-cx, -cy)) };
        }

        return shader;
    }

    private DisplayShader? CreateConic(PhysicalSize size)
    {
        var center = new PhysicalOffset(_positionX.Resolve(size.Width), _positionY.Resolve(size.Height));
        if (ResolveStops(1, Math.PI * Math.Max(size.Width, size.Height)) is not { } stops)
            return null;

        var (offsets, colors) = stops;
        if (_repeating)
        {
            var lo = offsets[0];
            var period = offsets[^1] - lo;
            if (period <= 1e-9 || offsets.Count / period > MaxRepeatedStops)
                return Solid(Average(offsets, colors));

            // The pattern is repeated over the whole turn
            var repeatedOffsets = new List<double>();
            var repeatedColors = new List<DisplayColor>();
            var startCopy = Math.Floor(-lo / period);
            for (var copy = startCopy; lo + (copy * period) < 1; copy++)
            {
                for (var i = 0; i < offsets.Count; i++)
                {
                    repeatedOffsets.Add(offsets[i] + (copy * period));
                    repeatedColors.Add(colors[i]);
                }
            }

            offsets = repeatedOffsets;
            colors = repeatedColors;
        }

        // The angles outside of the turn are not visible
        if (offsets[0] < 0)
        {
            CutBefore(offsets, colors, 0);
        }

        if (offsets[^1] > 1)
        {
            CutAfter(offsets, colors, 1);
        }

        var result = Normalize(offsets, colors, 0, 1);
        return new ConicGradientShader(center, (_fromAngle - 90) * Math.PI / 180, result);
    }

    private static LinearGradientShader Solid(DisplayColor color) => new LinearGradientShader(PhysicalOffset.Zero, new PhysicalOffset(1, 0), [new GradientStop(0, color), new GradientStop(1, color)]);

    /// <summary>The average color of a repeating gradient whose period is 0 (https://drafts.csswg.org/css-images-3/#repeating-gradients).</summary>
    private static DisplayColor Average(List<double> offsets, List<DisplayColor> colors)
    {
        var last = colors[^1];
        if (offsets[^1] - offsets[0] <= 1e-9)
            return last;

        double r = 0, g = 0, b = 0, a = 0;
        for (var i = 1; i < offsets.Count; i++)
        {
            var weight = offsets[i] - offsets[i - 1];
            var (c1, c2) = (colors[i - 1], colors[i]);
            r += weight * ((c1.R * c1.A) + (c2.R * c2.A)) / 2;
            g += weight * ((c1.G * c1.A) + (c2.G * c2.A)) / 2;
            b += weight * ((c1.B * c1.A) + (c2.B * c2.A)) / 2;
            a += weight * (c1.A + c2.A) / 2;
        }

        var total = offsets[^1] - offsets[0];
        a /= total;
        return a <= 0 ? DisplayColor.Transparent : new DisplayColor((float)(r / total / a), (float)(g / total / a), (float)(b / total / a), (float)a);
    }

    /// <summary>Maps the offsets from [lo, hi] to [0, 1].</summary>
    private static GradientStop[] Normalize(List<double> offsets, List<DisplayColor> colors, double lo, double hi)
    {
        var range = hi - lo;
        var result = new GradientStop[offsets.Count];
        for (var i = 0; i < offsets.Count; i++)
        {
            result[i] = new GradientStop(range > 0 ? Math.Clamp((offsets[i] - lo) / range, 0, 1) : 0, colors[i]);
        }

        return result;
    }

    /// <summary>Removes the stops before an offset, with a stop at the offset of the color interpolated there.</summary>
    private static void CutBefore(List<double> offsets, List<DisplayColor> colors, double offset)
    {
        var index = offsets.FindIndex(value => value >= offset);
        if (index < 0)
        {
            var last = colors[^1];
            offsets.Clear();
            colors.Clear();
            offsets.Add(offset);
            colors.Add(last);
            return;
        }

        if (index == 0)
            return;

        var color = InterpolateDisplay(colors[index - 1], colors[index], offsets[index] - offsets[index - 1] <= 0 ? 1 : (offset - offsets[index - 1]) / (offsets[index] - offsets[index - 1]));
        offsets.RemoveRange(0, index);
        colors.RemoveRange(0, index);
        offsets.Insert(0, offset);
        colors.Insert(0, color);
    }

    /// <summary>Removes the stops after an offset, with a stop at the offset of the color interpolated there.</summary>
    private static void CutAfter(List<double> offsets, List<DisplayColor> colors, double offset)
    {
        var index = offsets.FindLastIndex(value => value <= offset);
        if (index < 0)
        {
            var first = colors[0];
            offsets.Clear();
            colors.Clear();
            offsets.Add(offset);
            colors.Add(first);
            return;
        }

        if (index == offsets.Count - 1)
            return;

        var color = InterpolateDisplay(colors[index], colors[index + 1], offsets[index + 1] - offsets[index] <= 0 ? 0 : (offset - offsets[index]) / (offsets[index + 1] - offsets[index]));
        offsets.RemoveRange(index + 1, offsets.Count - index - 1);
        colors.RemoveRange(index + 1, colors.Count - index - 1);
        offsets.Add(offset);
        colors.Add(color);
    }

    /// <summary>Interpolates two sRGB colors premultiplied.</summary>
    private static double ClampLength(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, -MaxLength, MaxLength);

    /// <summary>
    /// Returns the color of a gradient in its box when it is the same at the centers of the pixels of its corners and at its
    /// center (computed with double-precision numbers), or null. <paramref name="getOffset"/> returns the offset on the
    /// gradient ray of a point of the box.
    /// </summary>
    private DisplayColor? GetUniformColor(List<double> offsets, List<DisplayColor> colors, PhysicalSize size, Func<double, double, double> getOffset)
    {
        var right = Math.Max(size.Width - 0.5, 0.5);
        var bottom = Math.Max(size.Height - 0.5, 0.5);
        DisplayColor? result = null;
        foreach (var (x, y) in (ReadOnlySpan<(double, double)>)[(0.5, 0.5), (right, 0.5), (0.5, bottom), (right, bottom), (size.Width / 2, size.Height / 2)])
        {
            var color = GetColorAt(getOffset(x, y));
            if (result is { } previous && (Math.Abs(previous.R - color.R) > SampleTolerance || Math.Abs(previous.G - color.G) > SampleTolerance || Math.Abs(previous.B - color.B) > SampleTolerance || Math.Abs(previous.A - color.A) > SampleTolerance))
                return null;

            result ??= color;
        }

        return result;

        DisplayColor GetColorAt(double offset)
        {
            var first = offsets[0];
            var last = offsets[^1];
            if (_repeating && last > first)
            {
                offset = first + ((((offset - first) % (last - first)) + (last - first)) % (last - first));
            }

            if (double.IsNaN(offset) || offset <= first)
                return colors[0];

            for (var i = 1; i < offsets.Count; i++)
            {
                if (offset < offsets[i])
                    return offsets[i] > offsets[i - 1] ? InterpolateDisplay(colors[i - 1], colors[i], (offset - offsets[i - 1]) / (offsets[i] - offsets[i - 1])) : colors[i];
            }

            return colors[^1];
        }
    }

    private static DisplayColor InterpolateDisplay(DisplayColor a, DisplayColor b, double t)
    {
        var alpha = a.A + ((b.A - a.A) * t);
        if (alpha <= 0)
            return DisplayColor.Transparent;

        return new DisplayColor(Component(a.R, b.R), Component(a.G, b.G), Component(a.B, b.B), (float)alpha);

        float Component(float x, float y) => (float)(((x * a.A) + (((y * b.A) - (x * a.A)) * t)) / alpha);
    }

    /// <summary>
    /// Resolves the color stops for a gradient line (or ray) length (https://drafts.csswg.org/css-images-4/#color-stop-fixup):
    /// the positions become fractions of the line, the missing ones are spread evenly, a position before a previous one is
    /// clamped, then the colors of the segments that are not interpolated in sRGB or that have a transition hint are sampled into
    /// more stops. Returns the offsets (not normalized: before 0 or after 1 when the stops are outside of the line) and the
    /// sRGB colors, or null when there is no color stop. <paramref name="extent"/> is the length in px the gradient line is
    /// painted over (the circumference of a conic gradient), which gives the number of samples of a segment.
    /// </summary>
    private (List<double> Offsets, List<DisplayColor> Colors)? ResolveStops(double length, double extent)
    {
        var count = _stops.Count;
        var positions = new double?[count];
        for (var i = 0; i < count; i++)
        {
            var stop = _stops[i];
            if (stop.Turn is { } turn)
            {
                positions[i] = turn;
            }
            else if (stop.Position is { } position)
            {
                positions[i] = length > 0 ? ClampLength(position.Resolve(length)) / length : 0;
            }
        }

        // The first and the last color stops default to 0% and 100%
        var firstColor = _stops.FindIndex(stop => stop.Color is not null);
        var lastColor = _stops.FindLastIndex(stop => stop.Color is not null);
        if (firstColor < 0)
            return null;

        positions[firstColor] ??= 0;
        positions[lastColor] ??= 1;

        // A position smaller than a previous one is clamped to it
        var max = double.NegativeInfinity;
        for (var i = 0; i < count; i++)
        {
            if (positions[i] is { } position)
            {
                max = Math.Max(max, position);
                positions[i] = max;
            }
        }

        // The color stops without position are spread evenly between the positioned ones around them
        for (var i = 0; i < count;)
        {
            if (positions[i] is not null || _stops[i].Color is null)
            {
                i++;
                continue;
            }

            var runEnd = i;
            while (runEnd < count && positions[runEnd] is null)
            {
                runEnd++;
            }

            var before = positions[i - 1] ?? 0;
            var after = runEnd < count ? positions[runEnd]!.Value : before;
            var stopsInRun = 0;
            for (var j = i; j < runEnd; j++)
            {
                if (_stops[j].Color is not null)
                {
                    stopsInRun++;
                }
            }

            var index = 0;
            for (var j = i; j < runEnd; j++)
            {
                if (_stops[j].Color is not null)
                {
                    index++;
                    positions[j] = before + ((after - before) * index / (stopsInRun + 1));
                }
            }

            i = runEnd;
        }

        // The colors in the interpolation space, and the sampling of the segments
        var offsets = new List<double>(count * 2);
        var colors = new List<DisplayColor>(count * 2);
        var space = _space;
        var sample = space != "srgb";
        CssColorComponents? previous = null;
        double previousPosition = 0;
        double? hint = null;
        for (var i = 0; i < count; i++)
        {
            var stop = _stops[i];
            var position = positions[i] ?? previousPosition;
            if (stop.Color is not { } color)
            {
                // A hint without color stop on both sides is ignored
                if (previous is not null)
                {
                    hint = position;
                }

                continue;
            }

            var converted = CssColorConversion.Convert(color, space);

            // https://drafts.csswg.org/css-color-4/#interpolation-missing: a missing component of a color stop takes the value
            // of the component of the other stop of the segment, so the stop has a color on each of its sides
            var stopColor = ToDisplayColor(converted);
            var segmentStartColor = previous is { } start ? ToDisplayColor(start) : stopColor;
            if (previous is { } before && (HasMissingComponent(before) || HasMissingComponent(converted)))
            {
                segmentStartColor = ToDisplayColor(CssColorMix.Interpolate(before, converted, 0, _hueMethod));
                stopColor = ToDisplayColor(CssColorMix.Interpolate(before, converted, 1, _hueMethod));
                if (colors.Count == 1)
                {
                    // The first stop: the color before the gradient line
                    colors[0] = segmentStartColor;
                }
                else if (colors[^1] != segmentStartColor)
                {
                    offsets.Add(previousPosition);
                    colors.Add(segmentStartColor);
                }
            }

            if (previous is { } from && (sample || hint is not null) && position > previousPosition)
            {
                // https://drafts.csswg.org/css-images-4/#coloring-gradient-line: the transition hint sets the position of the
                // half-way color
                var exponent = 1d;
                var hintFraction = 0.5;
                if (hint is { } h)
                {
                    hintFraction = (h - previousPosition) / (position - previousPosition);
                }

                if (hintFraction > 0 && hintFraction < 1)
                {
                    exponent = Math.Log(0.5) / Math.Log(hintFraction);
                }

                var segmentExtent = (position - previousPosition) * extent;
                var segmentStart = previousPosition;
                var segmentLength = position - previousPosition;
                var hueMethod = _hueMethod;
                var startColor = segmentStartColor;
                for (var k = 1; k <= SegmentSamples; k++)
                {
                    var t = (double)k / SegmentSamples;
                    var color1 = k == SegmentSamples ? stopColor : Sample(t);
                    Refine((double)(k - 1) / SegmentSamples, startColor, t, color1, MaxSampleDepth);
                    if (k < SegmentSamples)
                    {
                        offsets.Add(segmentStart + (segmentLength * t));
                        colors.Add(color1);
                    }

                    startColor = color1;
                }

                DisplayColor Sample(double t)
                {
                    var weight = hintFraction <= 0 ? 1 : hintFraction >= 1 ? 0 : Math.Pow(t, exponent);
                    return ToDisplayColor(CssColorMix.Interpolate(from, converted, weight, hueMethod));
                }

                // Adds the stops between two samples where the interpolation in sRGB is not close enough to the gradient
                void Refine(double t0, DisplayColor color0, double t1, DisplayColor color1, int depth)
                {
                    if (depth == 0 || !((t1 - t0) * segmentExtent > MinSampleDistance))
                        return;

                    var middle = (t0 + t1) / 2;
                    var middleColor = Sample(middle);
                    if (Math.Abs(middleColor.R - ((color0.R + color1.R) / 2)) <= SampleTolerance && Math.Abs(middleColor.G - ((color0.G + color1.G) / 2)) <= SampleTolerance
                        && Math.Abs(middleColor.B - ((color0.B + color1.B) / 2)) <= SampleTolerance && Math.Abs(middleColor.A - ((color0.A + color1.A) / 2)) <= SampleTolerance)
                    {
                        return;
                    }

                    Refine(t0, color0, middle, middleColor, depth - 1);
                    offsets.Add(segmentStart + (segmentLength * middle));
                    colors.Add(middleColor);
                    Refine(middle, middleColor, t1, color1, depth - 1);
                }
            }

            offsets.Add(position);
            colors.Add(stopColor);
            previous = converted;
            previousPosition = position;
            hint = null;
        }

        return (offsets, colors);
    }

    private static bool HasMissingComponent(CssColorComponents color) => color.Alpha is null || Array.IndexOf(color.Channels, null) >= 0;

    // The interpolated colors are CSS gamut mapped (https://drafts.csswg.org/css-color-4/#css-gamut-mapping)
    private static DisplayColor ToDisplayColor(CssColorComponents color) => DisplayColor.FromCss(color, RenderingColorSpace.Srgb, defaultAlpha: 1);
}
