using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Svg.Animations;

/// <summary>
/// The operations of the animations on the values of an attribute or a property
/// (https://svgwg.org/specs/animations/#AnimationAttributesAndProperties): parsing, interpolation, addition (additive
/// animations) and accumulation (cumulative animations). The values are opaque objects of the type.
/// </summary>
internal abstract class SmilValueType
{
    /// <summary>Whether the values can be interpolated and added; otherwise the animations are discrete, and the by animations have no effect.</summary>
    public virtual bool IsAdditive => true;

    /// <summary>The base value of the attribute (the underlying value of the animation of lowest priority), or null when it has none.</summary>
    public abstract object? GetBaseValue();

    /// <summary>Parses a value of the animation (<c>from</c>, <c>to</c>, <c>by</c>, <c>values</c>), or returns null for an invalid value.</summary>
    public abstract object? Parse(string value);

    /// <summary>Whether two values can be interpolated (e.g. lists of the same length); otherwise the animation is discrete.</summary>
    public virtual bool CanInterpolate(object from, object to) => true;

    public abstract object Interpolate(object from, object to, double progress);

    /// <summary>Adds two values of the animation (the to value of a from-by animation).</summary>
    public abstract object Add(object underlying, object value);

    /// <summary>Whether two values can be added (e.g. paths with the same commands); otherwise an additive animation has no effect.</summary>
    public virtual bool CanAdd(object underlying, object value) => true;

    /// <summary>Whether an empty attribute is a value of the type (an empty path), rather than a missing value.</summary>
    public virtual bool IsEmptyValue => false;

    /// <summary>Adds the value of an additive animation to its underlying value.</summary>
    public virtual object AddToUnderlying(object underlying, object value) => Add(underlying, value);

    /// <summary>Adds the value of the end of the simple duration <paramref name="repeat"/> times (cumulative animations).</summary>
    public abstract object Accumulate(object value, object end, long repeat);

    /// <summary>The zero of the type for a value (the from value of a by animation), or null when the type has none.</summary>
    public virtual object? GetZero(object value) => null;

    /// <summary>The distance between two values (paced animations), or null when it is not defined.</summary>
    public virtual double? GetDistance(object from, object to) => null;

    public abstract string Serialize(object value);

    /// <summary>Creates the operations of the animated value of an attribute of an element, or returns null when the attribute is not animatable.</summary>
    public static SmilValueType? Create(SvgElement target, string attribute, SvgAnimationElement animation)
    {
        if (animation is SvgAnimateTransformElement animateTransform)
            return new SmilTransformType(target, attribute, animateTransform.TransformType);

        if (IsPathDataAttribute(target, attribute))
            return new SmilPathType(target, attribute);

        if (target.GetAnimatedAttribute(attribute) is not var (type, key))
            return null;

        var isPair = attribute is "order" or "kernelUnitLength" or "baseFrequency" or "stdDeviation" or "radius";
        switch (type)
        {
            case "SVGAnimatedLength":
                return new SmilLengthListType(target, attribute, single: true, SvgProperties.GetLengthDefault(key));
            case "SVGAnimatedLengthList":
                return new SmilLengthListType(target, attribute, single: false, defaultValue: null);
            case "SVGAnimatedNumber":
                return new SmilNumberListType(target, attribute, isPair ? SmilNumberKind.NumberPair : SmilNumberKind.Number, SvgProperties.GetNumberDefault(key));
            case "SVGAnimatedInteger":
                return new SmilNumberListType(target, attribute, isPair ? SmilNumberKind.IntegerPair : SmilNumberKind.Integer, SvgProperties.GetNumberDefault(key));
            case "SVGAnimatedNumberList":
                return new SmilNumberListType(target, attribute, SmilNumberKind.NumberList, defaultValue: null);
            case "SVGPointList":
                return new SmilNumberListType(target, attribute, SmilNumberKind.PointList, defaultValue: null);
            case "SVGAnimatedRect":
                return new SmilNumberListType(target, attribute, SmilNumberKind.Rect, defaultValue: null);
            case "SVGAnimatedAngle":
            case "SVGAnimatedEnumeration" when attribute == "orient":
                return new SmilAngleType(target, attribute);
            case "SVGAnimatedEnumeration":
                return new SmilDiscreteType(target, attribute, SmilDiscreteKind.Enumeration, SvgProperties.GetEnumeration(key));
            case "SVGAnimatedBoolean":
                return new SmilDiscreteType(target, attribute, SmilDiscreteKind.Boolean, enumeration: null);
            case "SVGAnimatedPreserveAspectRatio":
                return new SmilDiscreteType(target, attribute, SmilDiscreteKind.PreserveAspectRatio, enumeration: null);
            case "SVGAnimatedString":
                return new SmilDiscreteType(target, attribute, SmilDiscreteKind.String, enumeration: null);
            default:
                return null;
        }
    }

    /// <summary>The attributes whose value is path data, which are not reflected by an SVG DOM object: d of path, path of textPath.</summary>
    public static bool IsPathDataAttribute(SvgElement target, string attribute)
        => (attribute == "d" && target is SvgPathElement) || (attribute == "path" && target is SvgTextPathElement);

    private protected static string? GetBaseAttribute(SvgElement target, string attribute)
    {
        var value = target.GetAttributeValue(attribute);
        if (value is null && attribute == "href")
        {
            value = target.GetAttributeValue(Namespaces.XLink, "href");
        }

        return value;
    }

    private protected static double Lerp(double from, double to, double progress) => from + ((to - from) * progress);
}

internal enum SmilNumberKind
{
    Number,
    Integer,
    NumberPair,
    IntegerPair,
    NumberList,
    PointList,
    Rect,

    /// <summary>A pair of coordinates (the values of <c>animateMotion</c>).</summary>
    Point,
}

/// <summary>The numbers, the integers, the pairs of numbers, the lists of numbers, the lists of points and the rectangles: lists of numbers.</summary>
internal sealed class SmilNumberListType(SvgElement target, string attribute, SmilNumberKind kind, float? defaultValue) : SmilValueType
{
    private bool IsSingle => kind is SmilNumberKind.Number or SmilNumberKind.Integer;

    public override object? GetBaseValue()
    {
        if (kind != SmilNumberKind.Point && GetBaseAttribute(target, attribute) is { } value && Parse(value) is { } parsed)
            return parsed;

        return kind switch
        {
            SmilNumberKind.Number or SmilNumberKind.Integer => new double[] { defaultValue ?? 0 },
            SmilNumberKind.NumberPair or SmilNumberKind.IntegerPair => new double[] { defaultValue ?? 0, defaultValue ?? 0 },
            SmilNumberKind.Point => new double[2],
            SmilNumberKind.Rect => null,
            _ => Array.Empty<double>(),
        };
    }

    public override object? Parse(string value)
    {
        if (IsSingle)
        {
            if (!SvgParsing.TryParseSingleNumber(value, out var number) || (kind == SmilNumberKind.Integer && number != MathF.Truncate(number)))
                return null;

            return new double[] { number };
        }

        if (!SvgParsing.TryParseNumberList(value, out var numbers))
            return null;

        switch (kind)
        {
            case SmilNumberKind.NumberPair or SmilNumberKind.IntegerPair:
                if (numbers.Count is 0 or > 2 || (kind == SmilNumberKind.IntegerPair && numbers.Exists(n => n != MathF.Truncate(n))))
                    return null;

                return new double[] { numbers[0], numbers.Count == 2 ? numbers[1] : numbers[0] };

            case SmilNumberKind.Rect:
                // The values of a by animation can be negative
                if (numbers.Count != 4)
                    return null;

                break;

            case SmilNumberKind.PointList:
                if (numbers.Count % 2 != 0)
                    return null;

                break;

            case SmilNumberKind.Point:
                if (numbers.Count != 2)
                    return null;

                break;
        }

        return numbers.Select(n => (double)n).ToArray();
    }

    // An empty list is padded with zeros (the base value of a to animation, the zero of a by animation)
    public override bool CanInterpolate(object from, object to) => ((double[])from).Length == ((double[])to).Length || ((double[])from).Length == 0;

    public override object Interpolate(object from, object to, double progress)
    {
        var a = (double[])from;
        var b = (double[])to;
        var result = new double[b.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Lerp(i < a.Length ? a[i] : 0, b[i], progress);
        }

        return result;
    }

    public override object Add(object underlying, object value)
    {
        var a = (double[])underlying;
        var b = (double[])value;
        var result = new double[Math.Max(a.Length, b.Length)];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
        }

        return result;
    }

    public override object Accumulate(object value, object end, long repeat)
    {
        var a = (double[])value;
        var b = (double[])end;
        if (a.Length != b.Length)
            return value;

        var result = new double[a.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = a[i] + (b[i] * repeat);
        }

        return result;
    }

    public override object? GetZero(object value) => new double[((double[])value).Length];

    public override double? GetDistance(object from, object to)
    {
        var a = (double[])from;
        var b = (double[])to;
        if (a.Length != b.Length)
            return null;

        if (a.Length == 1)
            return Math.Abs(b[0] - a[0]);

        // The pairs and the rectangles: the distance between the vectors; the distance between lists is not defined
        if (kind is SmilNumberKind.NumberList or SmilNumberKind.PointList)
            return null;

        var sum = 0d;
        for (var i = 0; i < a.Length; i++)
        {
            sum += (b[i] - a[i]) * (b[i] - a[i]);
        }

        return Math.Sqrt(sum);
    }

    public override string Serialize(object value)
    {
        var numbers = (double[])value;
        var builder = new StringBuilder();
        for (var i = 0; i < numbers.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(kind == SmilNumberKind.PointList && i % 2 == 1 ? ',' : ' ');
            }

            var number = numbers[i];
            if (kind is SmilNumberKind.Integer or SmilNumberKind.IntegerPair)
            {
                number = Math.Round(number, MidpointRounding.AwayFromZero);
            }

            builder.Append(SvgParsing.FormatNumber((float)number));
        }

        return builder.ToString();
    }
}

/// <summary>A length with its unit.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SmilLength(double Value, ushort UnitType, string UnknownUnit);

/// <summary>
/// The lengths and the lists of lengths: the values are combined in user units, and the result has the unit of the nearest
/// value of the interval (as in the browsers).
/// </summary>
internal sealed class SmilLengthListType(SvgElement target, string attribute, bool single, string? defaultValue) : SmilValueType
{
    private readonly Dictionary<(ushort UnitType, string UnknownUnit), double> _factors = [];

    public override object? GetBaseValue()
    {
        if (GetBaseAttribute(target, attribute) is { } value && Parse(value) is { } parsed)
            return parsed;

        if (single)
            return Parse(defaultValue ?? "0");

        return Array.Empty<SmilLength>();
    }

    public override object? Parse(string value)
    {
        if (single)
        {
            if (!SvgLength.TryParse(value, out var number, out var unitType, out var unknownUnit))
            {
                // A math function: its value in user units
                if (value.Contains('(', StringComparison.Ordinal) && SvgUserSpace.GetUnitSize(target, attribute, "%") is { } percent
                    && SvgUserSpace.ResolveLength(target, value.Trim(), percent * 100) is { } resolved && double.IsFinite(resolved))
                {
                    return new[] { new SmilLength(resolved, SvgLength.SvgLengthTypeNumber, "") };
                }

                return null;
            }

            return new[] { new SmilLength(number, unitType, unknownUnit) };
        }

        var result = new List<SmilLength>();
        var position = 0;
        SvgParsing.SkipWhitespace(value, ref position);
        while (position < value.Length)
        {
            var start = position;
            while (position < value.Length && value[position] != ',' && !Infra.InfraAscii.IsAsciiWhitespace(value[position]))
            {
                position++;
            }

            if (!SvgLength.TryParse(value[start..position], out var number, out var unitType, out var unknownUnit))
                return null;

            result.Add(new SmilLength(number, unitType, unknownUnit));
            if (SvgParsing.SkipCommaWhitespace(value, ref position) && position >= value.Length)
                return null;
        }

        return result.ToArray();
    }

    /// <summary>The size in user units of one unit.</summary>
    private double GetFactor(SmilLength length)
    {
        if (length.UnitType is SvgLength.SvgLengthTypeNumber or SvgLength.SvgLengthTypePx)
            return 1;

        if (_factors.TryGetValue((length.UnitType, length.UnknownUnit), out var factor))
            return factor;

        try
        {
            var unit = new SvgLength { Context = (target, attribute) };
            unit.SetRaw(1, length.UnitType, length.UnknownUnit);
            factor = unit.Value;
        }
        catch (DomException)
        {
            // The unit cannot be converted (e.g. an element that is not rendered)
            factor = 1;
        }

        _factors[(length.UnitType, length.UnknownUnit)] = factor;
        return factor;
    }

    private double ToUserUnits(SmilLength length) => length.Value * GetFactor(length);

    private SmilLength FromUserUnits(double value, SmilLength unit)
    {
        var factor = GetFactor(unit);
        return unit with { Value = factor == 0 ? 0 : value / factor };
    }

    public override bool CanInterpolate(object from, object to) => ((SmilLength[])from).Length == ((SmilLength[])to).Length || ((SmilLength[])from).Length == 0;

    public override object Interpolate(object from, object to, double progress)
    {
        var a = (SmilLength[])from;
        var b = (SmilLength[])to;
        var result = new SmilLength[b.Length];
        for (var i = 0; i < result.Length; i++)
        {
            if (i >= a.Length)
            {
                result[i] = b[i] with { Value = b[i].Value * progress };
                continue;
            }

            // As in the browsers, the unit changes after the middle of the interval
            var unit = progress <= 0.5 ? a[i] : b[i];
            if (a[i].UnitType == b[i].UnitType && a[i].UnknownUnit == b[i].UnknownUnit)
            {
                result[i] = unit with { Value = Lerp(a[i].Value, b[i].Value, progress) };
            }
            else
            {
                result[i] = FromUserUnits(Lerp(ToUserUnits(a[i]), ToUserUnits(b[i]), progress), unit);
            }
        }

        return result;
    }

    private SmilLength Sum(SmilLength a, SmilLength b, double factor)
    {
        if (a.UnitType == b.UnitType && a.UnknownUnit == b.UnknownUnit)
            return b with { Value = a.Value + (b.Value * factor) };

        return FromUserUnits(ToUserUnits(a) + (ToUserUnits(b) * factor), b);
    }

    public override object Add(object underlying, object value)
    {
        var a = (SmilLength[])underlying;
        var b = (SmilLength[])value;
        var result = new SmilLength[Math.Max(a.Length, b.Length)];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = i >= a.Length ? b[i] : i >= b.Length ? a[i] : Sum(a[i], b[i], 1);
        }

        return result;
    }

    public override object Accumulate(object value, object end, long repeat)
    {
        var a = (SmilLength[])value;
        var b = (SmilLength[])end;
        if (a.Length != b.Length)
            return value;

        var result = new SmilLength[a.Length];
        for (var i = 0; i < result.Length; i++)
        {
            // The result keeps the unit of the value
            result[i] = a[i].UnitType == b[i].UnitType && a[i].UnknownUnit == b[i].UnknownUnit
                ? a[i] with { Value = a[i].Value + (b[i].Value * repeat) }
                : FromUserUnits(ToUserUnits(a[i]) + (ToUserUnits(b[i]) * repeat), a[i]);
        }

        return result;
    }

    public override object? GetZero(object value) => ((SmilLength[])value).Select(length => length with { Value = 0 }).ToArray();

    public override double? GetDistance(object from, object to)
    {
        var a = (SmilLength[])from;
        var b = (SmilLength[])to;
        if (a.Length != 1 || b.Length != 1)
            return null;

        return Math.Abs(ToUserUnits(b[0]) - ToUserUnits(a[0]));
    }

    public override string Serialize(object value)
    {
        var builder = new StringBuilder();
        var serializer = new SvgLength();
        foreach (var length in (SmilLength[])value)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            serializer.SetRaw((float)length.Value, length.UnitType, length.UnknownUnit);
            builder.Append(serializer.Serialize());
        }

        return builder.ToString();
    }
}

/// <summary>An angle with its unit, or the <c>auto</c> and <c>auto-start-reverse</c> keywords of the orient attribute of the markers.</summary>
internal sealed record SmilAngle(string? Keyword, double Degrees, ushort UnitType, bool Turn);

/// <summary>
/// The angles (the orient attribute of the markers): two angles are interpolated in degrees, and the result has the unit of
/// the nearest value of the interval; the keywords are discrete.
/// </summary>
internal sealed class SmilAngleType(SvgElement target, string attribute) : SmilValueType
{
    public override object? GetBaseValue() => GetBaseAttribute(target, attribute) is { } value && Parse(value) is { } parsed ? parsed : new SmilAngle(null, 0, SvgAngle.SvgAngleTypeUnspecified, Turn: false);

    public override object? Parse(string value)
    {
        var text = value.Trim(' ', '\t', '\n', '\f', '\r');
        if (text is "auto" or "auto-start-reverse")
            return new SmilAngle(text, 0, SvgAngle.SvgAngleTypeUnspecified, Turn: false);

        if (!SvgAngle.TryParse(text, out var number, out var unitType, out var turn))
            return null;

        return new SmilAngle(null, number * GetDegreesPerUnit(unitType, turn), unitType, turn);
    }

    private static double GetDegreesPerUnit(ushort unitType, bool turn) => turn ? 360 : unitType switch
    {
        SvgAngle.SvgAngleTypeRad => 180 / Math.PI,
        SvgAngle.SvgAngleTypeGrad => 0.9,
        _ => 1,
    };

    public override bool CanInterpolate(object from, object to) => ((SmilAngle)from).Keyword is null && ((SmilAngle)to).Keyword is null;

    public override object Interpolate(object from, object to, double progress)
        => (progress <= 0.5 ? (SmilAngle)from : (SmilAngle)to) with { Degrees = Lerp(((SmilAngle)from).Degrees, ((SmilAngle)to).Degrees, progress) };

    public override object Add(object underlying, object value)
    {
        var a = (SmilAngle)underlying;
        var b = (SmilAngle)value;
        return a.Keyword is null && b.Keyword is null ? b with { Degrees = a.Degrees + b.Degrees } : value;
    }

    public override object Accumulate(object value, object end, long repeat)
    {
        var a = (SmilAngle)value;
        var b = (SmilAngle)end;
        return a.Keyword is null && b.Keyword is null ? a with { Degrees = a.Degrees + (b.Degrees * repeat) } : value;
    }

    public override object? GetZero(object value) => ((SmilAngle)value).Keyword is null ? (SmilAngle)value with { Degrees = 0 } : null;

    public override double? GetDistance(object from, object to) => CanInterpolate(from, to) ? Math.Abs(((SmilAngle)to).Degrees - ((SmilAngle)from).Degrees) : null;

    public override string Serialize(object value)
    {
        var angle = (SmilAngle)value;
        if (angle.Keyword is not null)
            return angle.Keyword;

        var unit = angle.Turn ? "turn" : angle.UnitType switch
        {
            SvgAngle.SvgAngleTypeDeg => "deg",
            SvgAngle.SvgAngleTypeRad => "rad",
            SvgAngle.SvgAngleTypeGrad => "grad",
            _ => "",
        };

        return SvgParsing.FormatNumber((float)(angle.Degrees / GetDegreesPerUnit(angle.UnitType, angle.Turn))) + unit;
    }
}

internal enum SmilDiscreteKind
{
    String,
    Enumeration,
    Boolean,
    PreserveAspectRatio,
}

/// <summary>The values that are not additive: strings, enumerations, booleans and preserveAspectRatio. Their animations are discrete.</summary>
internal sealed class SmilDiscreteType(SvgElement target, string attribute, SmilDiscreteKind kind, SvgEnumeration? enumeration) : SmilValueType
{
    public override bool IsAdditive => false;

    public override object? GetBaseValue() => GetBaseAttribute(target, attribute);

    public override object? Parse(string value)
    {
        switch (kind)
        {
            case SmilDiscreteKind.Enumeration:
                return enumeration is not null && enumeration.IsKeyword(value) ? value : null;
            case SmilDiscreteKind.Boolean:
                return value is "true" or "false" ? value : null;
            case SmilDiscreteKind.PreserveAspectRatio:
                return SvgPreserveAspectRatio.TryParse(value, out _, out _) ? value : null;
            default:
                return value;
        }
    }

    public override bool CanInterpolate(object from, object to) => false;

    public override object Interpolate(object from, object to, double progress) => progress < 0.5 ? from : to;

    public override object Add(object underlying, object value) => value;

    public override object Accumulate(object value, object end, long repeat) => value;

    public override string Serialize(object value) => (string)value;
}

/// <summary>
/// The path data of the d attribute: two paths with the same commands are interpolated coordinate by coordinate, each
/// command keeping the type (absolute or relative) of the nearest path; otherwise the animation is discrete.
/// </summary>
internal sealed class SmilPathType(SvgElement target, string attribute) : SmilValueType
{
    public override object? GetBaseValue() => target.GetAttributeValue(attribute) is { } value ? SvgPathData.Parse(value) : new List<SvgPathCommand>();

    public override bool IsEmptyValue => true;

    public override object? Parse(string value)
    {
        // The value of the d property of the parent element
        if (attribute == "d" && value.Trim(' ', '\t', '\n', '\f', '\r') == "inherit")
        {
            if (target.ParentNode is Element parent && SvgUserSpace.GetStyle(parent)?.GetComputedValue("d") is { } inherited
                && Css.Syntax.CssParser.ParseListOfComponentValues(inherited) is [Css.Syntax.CssFunction function] && Css.Values.CssPath.TryParse(function, out _, out var inheritedCommands))
            {
                return inheritedCommands;
            }

            return new List<SvgPathCommand>();
        }

        var commands = SvgPathData.Parse(value, out var complete);
        return complete ? commands : null;
    }

    public override bool CanAdd(object underlying, object value) => CanInterpolate(underlying, value);

    public override bool CanInterpolate(object from, object to)
    {
        var a = (List<SvgPathCommand>)from;
        var b = (List<SvgPathCommand>)to;
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (char.ToUpperInvariant(a[i].Type) != char.ToUpperInvariant(b[i].Type))
                return false;
        }

        return true;
    }

    public override object Interpolate(object from, object to, double progress)
        => Combine((List<SvgPathCommand>)from, (List<SvgPathCommand>)to, progress < 0.5, (x, y) => Lerp(x, y, progress));

    public override object Add(object underlying, object value)
        => Combine((List<SvgPathCommand>)underlying, (List<SvgPathCommand>)value, useFirstTypes: false, static (x, y) => x + y);

    public override object Accumulate(object value, object end, long repeat)
    {
        if (!CanInterpolate(value, end))
            return value;

        return Combine((List<SvgPathCommand>)value, (List<SvgPathCommand>)end, useFirstTypes: true, (x, y) => x + (y * repeat));
    }

    public override object? GetZero(object value)
        => ((List<SvgPathCommand>)value).Select(command => new SvgPathCommand(command.Type, new float[command.Values.Length])).ToList();

    /// <summary>
    /// Combines the coordinates of two paths with the same commands: the coordinates are combined as absolute coordinates, and
    /// each command of the result is absolute or relative as the command of the first or of the second path. The flags of the
    /// arcs are the ones of that path.
    /// </summary>
    private static List<SvgPathCommand> Combine(List<SvgPathCommand> first, List<SvgPathCommand> second, bool useFirstTypes, Func<double, double, double> combine)
    {
        var a = SvgPathData.ToAbsolute(first);
        var b = SvgPathData.ToAbsolute(second);
        var result = new List<SvgPathCommand>(a.Count);
        double x = 0, y = 0, startX = 0, startY = 0;
        for (var i = 0; i < a.Count; i++)
        {
            var type = char.ToUpperInvariant(a[i].Type);
            var values = new float[a[i].Values.Length];
            for (var j = 0; j < values.Length; j++)
            {
                values[j] = type == 'A' && j is 3 or 4
                    ? (useFirstTypes ? a[i].Values[j] : b[i].Values[j])
                    : (float)combine(a[i].Values[j], b[i].Values[j]);
            }

            // The current point after the absolute command
            var (nextX, nextY) = (x, y);
            switch (type)
            {
                case 'Z':
                    (nextX, nextY) = (startX, startY);
                    break;
                case 'H':
                    nextX = values[0];
                    break;
                case 'V':
                    nextY = values[0];
                    break;
                default:
                    if (values.Length >= 2)
                    {
                        nextX = values[^2];
                        nextY = values[^1];
                    }

                    break;
            }

            var relative = char.IsLower(useFirstTypes ? first[i].Type : second[i].Type);
            if (relative && values.Length > 0)
            {
                switch (type)
                {
                    case 'H':
                        values[0] -= (float)x;
                        break;
                    case 'V':
                        values[0] -= (float)y;
                        break;
                    case 'A':
                        values[5] -= (float)x;
                        values[6] -= (float)y;
                        break;
                    default:
                        for (var j = 0; j + 1 < values.Length; j += 2)
                        {
                            values[j] -= (float)x;
                            values[j + 1] -= (float)y;
                        }

                        break;
                }
            }

            result.Add(new SvgPathCommand(relative ? char.ToLowerInvariant(type) : type, values));
            if (type == 'M')
            {
                (startX, startY) = (nextX, nextY);
            }

            (x, y) = (nextX, nextY);
        }

        return result;
    }

    public override string Serialize(object value) => SvgPathData.Serialize((List<SvgPathCommand>)value);
}

/// <summary>A transform function of an <c>animateTransform</c> element: its type and its numbers.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SmilTransform(ushort Type, double A, double B, double C)
{
    public SvgTransform ToSvgTransform()
    {
        var transform = new SvgTransform();
        switch (Type)
        {
            case SvgTransform.SvgTransformTranslate:
                transform.SetTranslateRaw((float)A, (float)B);
                break;
            case SvgTransform.SvgTransformScale:
                transform.SetScaleRaw((float)A, (float)B);
                break;
            case SvgTransform.SvgTransformRotate:
                transform.SetRotateRaw((float)A, (float)B, (float)C);
                break;
            default:
                transform.SetSkewRaw(Type, (float)A);
                break;
        }

        return transform;
    }
}

/// <summary>The value of a transform attribute: the transforms of the base value (or of the animations of lower priority), then the animated transforms.</summary>
internal sealed class SmilTransformList
{
    public List<SvgTransform> Base { get; init; } = [];

    public List<SmilTransform> Animated { get; init; } = [];
}

/// <summary>
/// The transform lists animated by <c>animateTransform</c> (https://svgwg.org/specs/animations/#AnimateTransformElement):
/// the values are a transform of the type of the element; an additive animation appends its transform to the underlying list.
/// </summary>
internal sealed class SmilTransformType(SvgElement target, string attribute, ushort transformType) : SmilValueType
{
    public override object? GetBaseValue()
    {
        _ = SvgTransform.TryParseList(target.GetAttributeValue(attribute), out var transforms);
        return new SmilTransformList { Base = transforms };
    }

    public override object? Parse(string value)
    {
        if (transformType == SvgTransform.SvgTransformUnknown || !SvgParsing.TryParseNumberList(value, out var numbers))
            return null;

        SmilTransform? transform = transformType switch
        {
            SvgTransform.SvgTransformTranslate when numbers.Count is 1 or 2 => new SmilTransform(transformType, numbers[0], numbers.Count == 2 ? numbers[1] : 0, 0),
            SvgTransform.SvgTransformScale when numbers.Count is 1 or 2 => new SmilTransform(transformType, numbers[0], numbers.Count == 2 ? numbers[1] : numbers[0], 0),
            SvgTransform.SvgTransformRotate when numbers.Count is 1 => new SmilTransform(transformType, numbers[0], 0, 0),
            SvgTransform.SvgTransformRotate when numbers.Count is 3 => new SmilTransform(transformType, numbers[0], numbers[1], numbers[2]),
            SvgTransform.SvgTransformSkewX or SvgTransform.SvgTransformSkewY when numbers.Count is 1 => new SmilTransform(transformType, numbers[0], 0, 0),
            _ => null,
        };

        return transform is { } result ? new SmilTransformList { Animated = [result] } : null;
    }

    private static SmilTransform Single(object value) => ((SmilTransformList)value).Animated is [var transform, ..] ? transform : default;

    // The underlying value of a to animation is a list of transforms: the animation starts from the zero of the type
    public override bool CanInterpolate(object from, object to) => true;

    public override object Interpolate(object from, object to, double progress)
    {
        var b = Single(to);
        var fromList = (SmilTransformList)from;
        var a = fromList.Animated.Count == 1 && fromList.Base.Count == 0 ? fromList.Animated[0] : new SmilTransform(b.Type, 0, 0, 0);
        return new SmilTransformList { Animated = [new SmilTransform(b.Type, Lerp(a.A, b.A, progress), Lerp(a.B, b.B, progress), Lerp(a.C, b.C, progress))] };
    }

    // The sum of two values of the animation (a from-by animation)
    public override object Add(object underlying, object value)
    {
        var (x, y) = (Single(underlying), Single(value));
        return new SmilTransformList { Animated = [new SmilTransform(y.Type, x.A + y.A, x.B + y.B, x.C + y.C)] };
    }

    // An additive animation: the transform is appended to the underlying list
    public override object AddToUnderlying(object underlying, object value)
    {
        var a = (SmilTransformList)underlying;
        var b = (SmilTransformList)value;
        return new SmilTransformList { Base = a.Base, Animated = [.. a.Animated, .. b.Animated] };
    }

    public override object Accumulate(object value, object end, long repeat)
    {
        var a = Single(value);
        var b = Single(end);
        return new SmilTransformList { Animated = [new SmilTransform(a.Type, a.A + (b.A * repeat), a.B + (b.B * repeat), a.C + (b.C * repeat))] };
    }

    public override object? GetZero(object value) => new SmilTransformList { Animated = [new SmilTransform(Single(value).Type, 0, 0, 0)] };

    public override double? GetDistance(object from, object to)
    {
        var a = Single(from);
        var b = Single(to);
        return a.Type switch
        {
            SvgTransform.SvgTransformTranslate or SvgTransform.SvgTransformScale => Math.Sqrt(((b.A - a.A) * (b.A - a.A)) + ((b.B - a.B) * (b.B - a.B))),
            _ => Math.Abs(b.A - a.A),
        };
    }

    public override string Serialize(object value)
    {
        var list = (SmilTransformList)value;
        return SvgTransform.Serialize([.. list.Base, .. list.Animated.Select(transform => transform.ToSvgTransform())]);
    }
}

/// <summary>Parsing of the lists of the animation attributes.</summary>
internal static class SmilParsing
{
    /// <summary>
    /// Splits a semicolon-separated list (https://svgwg.org/specs/animations/#ValuesAttribute): the white space around the
    /// values is ignored, and a trailing semicolon is allowed (the other empty values are kept).
    /// </summary>
    public static List<string> SplitValues(string value)
    {
        var result = new List<string>();
        foreach (var item in value.Split(';'))
        {
            result.Add(item.Trim(' ', '\t', '\n', '\f', '\r'));
        }

        if (result.Count > 0 && result[^1].Length == 0)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    /// <summary>
    /// Parses the keyTimes and keyPoints attributes: a list of numbers between 0 and 1. The key times start at 0 and do not
    /// decrease. Returns an empty list for an invalid value.
    /// </summary>
    public static List<double> ParseKeyTimes(string? value, bool verifyOrder)
    {
        var result = new List<double>();
        if (value is null)
            return result;

        var items = SplitValues(value);
        for (var i = 0; i < items.Count; i++)
        {
            if (!double.TryParse(items[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var time) || !(time >= 0 && time <= 1))
                return [];

            if (verifyOrder && (i == 0 ? time != 0 : time < result[^1]))
                return [];

            result.Add(time);
        }

        return result;
    }

    /// <summary>Parses the keySplines attribute: the control points of cubic Bézier curves, between 0 and 1. Returns an empty list for an invalid value.</summary>
    public static List<(double X1, double Y1, double X2, double Y2)> ParseKeySplines(string? value)
    {
        var result = new List<(double, double, double, double)>();
        if (value is null)
            return result;

        foreach (var item in SplitValues(value))
        {
            if (!SvgParsing.TryParseNumberList(item, out var numbers) || numbers.Count != 4 || numbers.Exists(static n => !(n >= 0 && n <= 1)))
                return [];

            result.Add((numbers[0], numbers[1], numbers[2], numbers[3]));
        }

        return result;
    }
}
