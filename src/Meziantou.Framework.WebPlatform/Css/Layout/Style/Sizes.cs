using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A computed math function of lengths and percentages that is not a sum of a length and a percentage (e.g.
/// <c>min(10px, 50%)</c>, <c>clamp(1px, 10%, 5em)</c>): it is evaluated when its percentage basis is known
/// (https://drafts.csswg.org/css-values-4/#calc-computed-value).
/// </summary>
internal sealed class CalcLength
{
    /// <summary>The largest length (the infinite values of the math functions are clamped to it, https://drafts.csswg.org/css-values-4/#calc-ieee).</summary>
    public const double MaxLength = 1e15;

    private readonly double _zoom;
    private readonly bool _nonNegative;

    public CalcLength(CssFunction function, double zoom, bool nonNegative)
    {
        Function = function;
        _zoom = zoom;
        _nonNegative = nonNegative;
    }

    /// <summary>The computed math function (its lengths are not zoomed).</summary>
    public CssFunction Function { get; }

    /// <summary>The effective zoom the lengths of the function are multiplied by.</summary>
    public double Zoom => _zoom;

    /// <summary>The parsed <c>calc-size()</c> function (see <see cref="CalcSizeValue"/>), when <see cref="Function"/> is one.</summary>
    internal CalcSizeValue? CalcSize => _calcSize ??= CalcSizeValue.Parse(Function, _zoom);

    private CalcSizeValue? _calcSize;

    /// <summary>
    /// Evaluates the function with a percentage basis in px (zoomed). The lengths and the percentages scale linearly with the
    /// zoom (the math functions of lengths are homogeneous), so the function is evaluated with the unzoomed basis and the result
    /// is zoomed.
    /// </summary>
    public double Resolve(double percentageBasis)
    {
        var basis = _zoom == 1 ? percentageBasis : percentageBasis / _zoom;
        var result = CssCalc.Evaluate(Function, new CssCalcContext { PercentageBasis = (basis, "px") }) is { IsResolved: true, Unit: "px" } value ? value.Value : 0;
        result = LengthPercentage.SanitizeLength(result * _zoom);

        // https://drafts.csswg.org/css-values-4/#calc-range: the value is clamped to the range of the property
        return _nonNegative ? Math.Max(0, result) : result;
    }

    public override string ToString() => CssSerializer.SerializeValue([Function], property: null);
}

/// <summary>A used <c>&lt;length-percentage&gt; | auto</c> (margins, insets...).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct LengthPercentageAuto : IEquatable<LengthPercentageAuto>
{
    private readonly LengthPercentage _value;
    private readonly bool _isAuto;

    private LengthPercentageAuto(LengthPercentage value, bool isAuto)
    {
        _value = value;
        _isAuto = isAuto;
    }

    public static LengthPercentageAuto Auto => new(default, isAuto: true);

    public static LengthPercentageAuto Zero => default;

    public bool IsAuto => _isAuto;

    /// <summary>The value when it is not auto (0px for auto).</summary>
    public LengthPercentage Value => _value;

    public static implicit operator LengthPercentageAuto(LengthPercentage value) => new(value, isAuto: false);

    public static LengthPercentageAuto FromLengthPercentage(LengthPercentage value) => value;

    /// <summary>Resolves the value against its percentage basis; auto is <paramref name="autoValue"/>.</summary>
    public double Resolve(double percentageBasis, double autoValue = 0) => _isAuto ? autoValue : _value.Resolve(percentageBasis);

    /// <summary>
    /// Parses a computed <c>&lt;length-percentage&gt; | auto</c>. The values that are not supported (e.g. the anchor
    /// functions, before the anchor positioning layout) are auto.
    /// </summary>
    public static LengthPercentageAuto Parse(string value, double zoom) => value != "auto" && LengthPercentage.TryParse(value, zoom, nonNegative: false, out var result) ? result : Auto;

    public bool Equals(LengthPercentageAuto other) => _isAuto == other._isAuto && _value.Equals(other._value);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is LengthPercentageAuto other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_value, _isAuto);

    public static bool operator ==(LengthPercentageAuto left, LengthPercentageAuto right) => left.Equals(right);

    public static bool operator !=(LengthPercentageAuto left, LengthPercentageAuto right) => !left.Equals(right);

    public override string ToString() => _isAuto ? "auto" : _value.ToString();
}

/// <summary>The kind of a <see cref="Size"/> (https://drafts.csswg.org/css-sizing-4/#sizing-values).</summary>
internal enum SizeKind : byte
{
    /// <summary><c>auto</c> (width, height, min-width, min-height, flex-basis).</summary>
    Auto,

    /// <summary><c>none</c> (max-width, max-height).</summary>
    None,

    /// <summary>A &lt;length-percentage&gt;.</summary>
    LengthPercentage,

    MinContent,
    MaxContent,

    /// <summary>The <c>fit-content</c> keyword.</summary>
    FitContent,

    /// <summary><c>fit-content(&lt;length-percentage&gt;)</c>: the argument is <see cref="Size.Value"/>.</summary>
    FitContentFunction,

    /// <summary><c>stretch</c> (https://drafts.csswg.org/css-sizing-4/#valdef-width-stretch).</summary>
    Stretch,

    /// <summary><c>contain</c> (https://drafts.csswg.org/css-sizing-4/#valdef-width-contain).</summary>
    Contain,

    /// <summary><c>content</c> (flex-basis, https://drafts.csswg.org/css-flexbox-1/#valdef-flex-basis-content).</summary>
    Content,

    /// <summary><c>calc-size()</c> (https://drafts.csswg.org/css-values-5/#calc-size): the function is <see cref="Size.CalcSizeFunction"/>.</summary>
    CalcSize,
}

/// <summary>
/// A used value of a sizing property (https://drafts.csswg.org/css-sizing-3/#sizing-properties): width, height, their
/// minimum and maximum, flex-basis, column-width...
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct Size : IEquatable<Size>
{
    private readonly LengthPercentage _value;

    private Size(SizeKind kind, LengthPercentage value)
    {
        Kind = kind;
        _value = value;
    }

    public static Size Auto => new(SizeKind.Auto, default);

    public static Size None => new(SizeKind.None, default);

    public SizeKind Kind { get; }

    /// <summary>The &lt;length-percentage&gt; of <see cref="SizeKind.LengthPercentage"/> and <see cref="SizeKind.FitContentFunction"/>.</summary>
    public LengthPercentage Value => _value;

    public bool IsAuto => Kind == SizeKind.Auto;

    public bool IsNone => Kind == SizeKind.None;

    /// <summary>Whether the size is a &lt;length-percentage&gt;.</summary>
    public bool IsLengthPercentage => Kind == SizeKind.LengthPercentage;

    /// <summary>Whether the size is an intrinsic size keyword (min-content, max-content, fit-content, fit-content()).</summary>
    public bool IsIntrinsic => Kind is SizeKind.MinContent or SizeKind.MaxContent or SizeKind.FitContent or SizeKind.FitContentFunction;

    /// <summary>The <c>calc-size()</c> function of <see cref="SizeKind.CalcSize"/>, resolved by the intrinsic sizing.</summary>
    public CssFunction? CalcSizeFunction => Kind == SizeKind.CalcSize ? _value.Calc?.Function : null;

    /// <summary>The parsed <c>calc-size()</c> of <see cref="SizeKind.CalcSize"/> (null when it cannot be parsed, it then behaves as <c>auto</c>).</summary>
    public CalcSizeValue? CalcSize => Kind == SizeKind.CalcSize ? _value.Calc?.CalcSize : null;

    /// <summary>
    /// The kind of value the size behaves as for all purposes other than resolving it: the keyword of the basis of a
    /// <c>calc-size()</c> (https://drafts.csswg.org/css-values-5/#calc-size-basis), <see cref="SizeKind.LengthPercentage"/> for a
    /// &lt;calc-sum&gt; or <c>any</c> basis, the kind of the size otherwise.
    /// </summary>
    public SizeKind BehavesAs => Kind != SizeKind.CalcSize ? Kind : CalcSize?.EffectiveBasis ?? SizeKind.Auto;

    /// <summary>Whether the size depends on the contents in the block axis (an intrinsic size keyword, or a <c>calc-size()</c> whose basis is one or <c>auto</c>), where it behaves as <c>auto</c> until the contents are laid out.</summary>
    public bool IsContentBasedInBlockAxis => BehavesAs is SizeKind.MinContent or SizeKind.MaxContent or SizeKind.FitContent or SizeKind.FitContentFunction || Kind == SizeKind.CalcSize && BehavesAs == SizeKind.Auto;

    public static Size FromLengthPercentage(LengthPercentage value) => new(SizeKind.LengthPercentage, value);

    public static Size FromKind(SizeKind kind) => new(kind, default);

    public static Size FitContentFunction(LengthPercentage value) => new(SizeKind.FitContentFunction, value);

    /// <summary>Resolves a &lt;length-percentage&gt; size, or returns null when it is not one or its percentage basis is unknown.</summary>
    public double? TryResolve(double? percentageBasis) => Kind == SizeKind.LengthPercentage ? _value.TryResolve(percentageBasis) : null;

    /// <summary>
    /// Resolves a &lt;length-percentage&gt; minimum size, or returns null when it is not one: the percentages of an unknown basis
    /// resolve against zero (https://drafts.csswg.org/css-sizing-3/#cyclic-percentage-contribution: "a cyclic percentage in a
    /// min size property [...] resolves against zero"; in the block axis too, as Blink: <c>calc(25px + 50%)</c> is 25px in a
    /// containing block whose block size depends on its contents, WPT css/css-values/calc-min-height-block-1.html).
    /// </summary>
    public double? TryResolveMinimum(double? percentageBasis) => Kind == SizeKind.LengthPercentage ? _value.TryResolve(percentageBasis) ?? _value.Resolve(0) : null;

    /// <summary>
    /// Parses a computed sizing value. <paramref name="noneOrAuto"/> is the keyword of the property (<see cref="SizeKind.None"/>
    /// for the maximum sizes, <see cref="SizeKind.Auto"/> otherwise), which is also the value of the values that are not
    /// supported (e.g. <c>anchor-size()</c> before the anchor positioning layout).
    /// </summary>
    public static Size Parse(string value, double zoom, SizeKind noneOrAuto = SizeKind.Auto)
    {
        switch (value)
        {
            case "auto":
                return Auto;
            case "none":
                return None;
            case "min-content" or "-webkit-min-content":
                return FromKind(SizeKind.MinContent);
            case "max-content" or "-webkit-max-content":
                return FromKind(SizeKind.MaxContent);
            case "fit-content" or "-webkit-fit-content" or "-moz-fit-content":
                return FromKind(SizeKind.FitContent);
            case "stretch" or "-webkit-fill-available" or "-moz-available":
                return FromKind(SizeKind.Stretch);
            case "contain":
                return FromKind(SizeKind.Contain);
            case "content":
                return FromKind(SizeKind.Content);
        }

        if (LengthPercentage.TryParse(value, zoom, nonNegative: true, out var length))
            return FromLengthPercentage(length);

        if (value.EndsWith(')', StringComparison.Ordinal) && ParseFunction(value) is { } function)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(function.Name, "fit-content") && GetSingleArgument(function) is { } argument
                && LengthPercentage.TryParse(CssSerializer.SerializeValue([argument], property: null), zoom, nonNegative: true, out var limit))
            {
                return FitContentFunction(limit);
            }

            if (InfraAscii.EqualsIgnoreAsciiCase(function.Name, "calc-size"))
                return new Size(SizeKind.CalcSize, LengthPercentage.FromCalc(new CalcLength(function, zoom, nonNegative: true)));
        }

        return FromKind(noneOrAuto);
    }

    private static CssFunction? ParseFunction(string value)
    {
        CssFunction? function = null;
        foreach (var component in CssParser.ParseListOfComponentValues(value))
        {
            switch (component)
            {
                case CssPreservedToken { Type: CssTokenType.Whitespace }:
                    break;
                case CssFunction f when function is null:
                    function = f;
                    break;
                default:
                    return null;
            }
        }

        return function;
    }

    private static CssComponentValue? GetSingleArgument(CssFunction function) => ValueMatcher.RemoveWhitespace(function.Value) is [var argument] ? argument : null;

    public bool Equals(Size other) => Kind == other.Kind && _value.Equals(other._value);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Size other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, _value);

    public static bool operator ==(Size left, Size right) => left.Equals(right);

    public static bool operator !=(Size left, Size right) => !left.Equals(right);

    public override string ToString() => Kind switch
    {
        SizeKind.LengthPercentage => _value.ToString(),
        SizeKind.FitContentFunction => "fit-content(" + _value.ToString() + ")",
        SizeKind.CalcSize => _value.ToString(),
        _ => Kind.ToString(),
    };
}

/// <summary>
/// A <c>calc-size()</c> value (https://drafts.csswg.org/css-values-5/#calc-size): <c>calc-size(&lt;calc-size-basis&gt;, &lt;calc-sum&gt;)</c>.
/// It behaves as its basis for all purposes other than resolving the size, and resolves to its calculation, where the
/// <c>size</c> keyword is the used size of the basis (https://drafts.csswg.org/css-values-5/#resolving-calc-size).
/// </summary>
internal sealed class CalcSizeValue
{
    private readonly CssFunction _calculation;
    private readonly double _zoom;

    private CalcSizeValue(SizeKind basisKind, LengthPercentage basisLength, CalcSizeValue? nestedBasis, CssFunction calculation, double zoom)
    {
        BasisKind = basisKind;
        BasisLength = basisLength;
        NestedBasis = nestedBasis;
        _calculation = calculation;
        _zoom = zoom;
    }

    /// <summary>
    /// The basis: a sizing keyword (<see cref="SizeKind.Auto"/>, <see cref="SizeKind.MinContent"/>...), <see cref="SizeKind.None"/> for
    /// <c>any</c>, <see cref="SizeKind.LengthPercentage"/> for a &lt;calc-sum&gt; (<see cref="BasisLength"/>), or
    /// <see cref="SizeKind.CalcSize"/> for a nested <c>calc-size()</c> (<see cref="NestedBasis"/>).
    /// </summary>
    public SizeKind BasisKind { get; }

    /// <summary>The &lt;calc-sum&gt; basis.</summary>
    public LengthPercentage BasisLength { get; }

    /// <summary>The nested <c>calc-size()</c> basis.</summary>
    public CalcSizeValue? NestedBasis { get; }

    /// <summary>Whether the basis is <c>any</c>: the value is a definite size that does not depend on a basis.</summary>
    public bool IsAny => BasisKind == SizeKind.None;

    /// <summary>The kind of value the <c>calc-size()</c> behaves as: the keyword of its (innermost) basis, <see cref="SizeKind.LengthPercentage"/> for a &lt;calc-sum&gt; or <c>any</c>.</summary>
    public SizeKind EffectiveBasis => BasisKind switch
    {
        SizeKind.CalcSize => NestedBasis!.EffectiveBasis,
        SizeKind.None => SizeKind.LengthPercentage,
        _ => BasisKind,
    };

    /// <summary>Parses a computed <c>calc-size()</c> function, or returns null when it is not valid.</summary>
    public static CalcSizeValue? Parse(CssFunction function, double zoom)
    {
        var arguments = ValueMatcher.SplitAtCommas(function.Value);
        if (arguments.Count != 2)
            return null;

        var calculation = new CssFunction("calc");
        calculation.Value.AddRange(arguments[1]);
        var basis = ValueMatcher.RemoveWhitespace(arguments[0]);
        switch (basis)
        {
            case [CssPreservedToken { Type: CssTokenType.Ident } keyword]:
                var kind = InfraAscii.ToAsciiLowercase(keyword.Token.Value) switch
                {
                    "any" => SizeKind.None,
                    _ => Size.Parse(keyword.Token.Value, zoom) is { Kind: not SizeKind.LengthPercentage and not SizeKind.None } parsed ? parsed.Kind : (SizeKind?)null,
                };

                return kind is { } basisKind ? new CalcSizeValue(basisKind, default, nestedBasis: null, calculation, zoom) : null;

            case [CssFunction nested] when InfraAscii.EqualsIgnoreAsciiCase(nested.Name, "calc-size"):
                return Parse(nested, zoom) is { } nestedValue ? new CalcSizeValue(SizeKind.CalcSize, default, nestedValue, calculation, zoom) : null;

            default:
                var sum = new CssFunction("calc");
                sum.Value.AddRange(arguments[0]);
                return LengthPercentage.TryParse(sum, zoom, nonNegative: true, out var length) ? new CalcSizeValue(SizeKind.LengthPercentage, length, nestedBasis: null, calculation, zoom) : null;
        }
    }

    /// <summary>
    /// Evaluates the calculation (zoomed px) with the used size of the basis as <c>size</c>; the percentages resolve against
    /// <paramref name="percentageBasis"/>, or against 0 when it is indefinite (https://drafts.csswg.org/css-values-5/#resolving-calc-size).
    /// </summary>
    public double Evaluate(double size, double? percentageBasis)
    {
        var context = new CssCalcContext
        {
            PercentageBasis = ((percentageBasis ?? 0) / _zoom, "px"),
            SizeKeywordValue = size / _zoom,
        };

        var result = CssCalc.Evaluate(_calculation, context) is { IsResolved: true, Unit: "px" } value ? value.Value : 0;
        return Math.Max(0, LengthPercentage.SanitizeLength(result * _zoom));
    }
}

/// <summary>An aspect ratio (https://drafts.csswg.org/css-sizing-4/#aspect-ratio): <c>auto || &lt;ratio&gt;</c>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AspectRatio(bool IsAuto, double Width, double Height)
{
    public static AspectRatio Auto => new(IsAuto: true, 0, 0);

    /// <summary>Whether a ratio is given (with or without auto).</summary>
    public bool HasRatio => Width > 0 && Height > 0;

    /// <summary>The ratio width / height, or null when there is none (or it is degenerate).</summary>
    public double? Ratio => HasRatio ? Width / Height : null;

    /// <summary>Parses a computed aspect-ratio: <c>auto</c>, <c>16 / 9</c>, <c>auto 2 / 1</c>.</summary>
    public static AspectRatio Parse(string value)
    {
        if (value == "auto")
            return Auto;

        var isAuto = false;
        double? width = null;
        double? height = null;
        var afterSlash = false;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "auto")
            {
                isAuto = true;
            }
            else if (part == "/")
            {
                afterSlash = true;
            }
            else if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                if (afterSlash)
                {
                    height = number;
                }
                else
                {
                    width = number;
                }
            }
        }

        return width is { } w ? new AspectRatio(isAuto, w, height ?? 1) : Auto;
    }
}
