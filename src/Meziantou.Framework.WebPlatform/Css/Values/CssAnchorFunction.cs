using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>The &lt;anchor-side&gt; of an <c>anchor()</c> function (https://drafts.csswg.org/css-anchor-position-1/#typedef-anchor-side).</summary>
internal enum AnchorSide : byte
{
    Inside,
    Outside,
    Top,
    Left,
    Right,
    Bottom,
    Start,
    End,
    SelfStart,
    SelfEnd,

    /// <summary>A &lt;percentage&gt; (<c>center</c> is 50%): <see cref="CssAnchorFunction.Percentage"/>.</summary>
    Percentage,
}

/// <summary>The &lt;anchor-size&gt; of an <c>anchor-size()</c> function (https://drafts.csswg.org/css-anchor-position-1/#typedef-anchor-size).</summary>
internal enum AnchorSizeKeyword : byte
{
    /// <summary>Omitted: the axis of the property the function is used in.</summary>
    None,
    Width,
    Height,
    Block,
    Inline,
    SelfBlock,
    SelfInline,
}

/// <summary>
/// A parsed <c>anchor()</c> (https://drafts.csswg.org/css-anchor-position-1/#anchor-pos) or <c>anchor-size()</c>
/// (https://drafts.csswg.org/css-anchor-position-1/#anchor-size-fn) function: the anchor name, the side or the size, and the
/// fallback value.
/// </summary>
internal sealed class CssAnchorFunction
{
    private CssAnchorFunction(bool isSize, string? name, AnchorSide side, double percentage, AnchorSizeKeyword size, CssComponentValue? fallback, CssComponentValue? sideValue)
    {
        SideValue = sideValue;
        IsSize = isSize;
        Name = name;
        Side = side;
        Percentage = percentage;
        Size = size;
        Fallback = fallback;
    }

    /// <summary>Whether the function is <c>anchor-size()</c> (else <c>anchor()</c>).</summary>
    public bool IsSize { get; }

    /// <summary>The &lt;anchor-name&gt; (a &lt;dashed-ident&gt;), or null for the default anchor element.</summary>
    public string? Name { get; }

    /// <summary>The &lt;anchor-side&gt; of <c>anchor()</c>.</summary>
    public AnchorSide Side { get; }

    /// <summary>The percentage of <see cref="AnchorSide.Percentage"/> (50 for 50% and <c>center</c>).</summary>
    public double Percentage { get; }

    /// <summary>The &lt;anchor-size&gt; of <c>anchor-size()</c>.</summary>
    public AnchorSizeKeyword Size { get; }

    /// <summary>The &lt;anchor-side&gt; or &lt;anchor-size&gt; as written, or null when it is omitted.</summary>
    public CssComponentValue? SideValue { get; }

    /// <summary>The fallback &lt;length-percentage&gt;, or null.</summary>
    public CssComponentValue? Fallback { get; }

    /// <summary>Returns whether a function is a valid anchor function allowed in the context (its fallback can contain the allowed anchor functions).</summary>
    public static bool IsValid(CssFunction function, CssAnchorFunctions allowed) => Parse(function, allowed) is not null;

    /// <summary>Parses an <c>anchor()</c> or <c>anchor-size()</c> function, or returns null when it is invalid or not allowed.</summary>
    public static CssAnchorFunction? Parse(CssFunction function, CssAnchorFunctions allowed)
    {
        if (CssCalc.GetAnchorFunction(function.Name) is not { } kind || (allowed & kind) == CssAnchorFunctions.None)
            return null;

        var isSize = kind == CssAnchorFunctions.AnchorSize;
        var arguments = ValueMatcher.SplitAtCommas(function.Value);
        if (arguments.Count > 2)
            return null;

        CssComponentValue? fallback = null;

        // anchor-size(<length-percentage>): the comma is omitted with the omitted optional arguments
        if (isSize && arguments.Count == 1 && ValueMatcher.RemoveWhitespace(arguments[0]) is [var single] && single is not CssPreservedToken { Type: CssTokenType.Ident })
        {
            return IsValidFallback(single, allowed) ? new CssAnchorFunction(isSize: true, name: null, AnchorSide.Inside, 0, AnchorSizeKeyword.None, single, sideValue: null) : null;
        }

        if (arguments.Count == 2)
        {
            // The comma follows the omitted optional arguments: anchor-size(, 10px) is invalid
            if (ValueMatcher.RemoveWhitespace(arguments[0]).Count == 0)
                return null;

            // <length-percentage>: the fallback is required after the comma
            if (ValueMatcher.RemoveWhitespace(arguments[1]) is not [var fallbackValue] || !IsValidFallback(fallbackValue, allowed))
                return null;

            fallback = fallbackValue;
        }

        string? name = null;
        var side = AnchorSide.Inside;
        var percentage = 0d;
        var size = AnchorSizeKeyword.None;
        var hasSide = false;
        CssComponentValue? sideValue = null;
        foreach (var value in ValueMatcher.RemoveWhitespace(arguments[0]))
        {
            if (value is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.Value.StartsWith("--", StringComparison.Ordinal))
            {
                if (name is not null)
                    return null;

                name = ident.Token.Value;
                continue;
            }

            if (hasSide)
                return null;

            hasSide = true;
            sideValue = value;
            if (isSize)
            {
                if (value is not CssPreservedToken { Type: CssTokenType.Ident } keyword)
                    return null;

                size = InfraAscii.ToAsciiLowercase(keyword.Token.Value) switch
                {
                    "width" => AnchorSizeKeyword.Width,
                    "height" => AnchorSizeKeyword.Height,
                    "block" => AnchorSizeKeyword.Block,
                    "inline" => AnchorSizeKeyword.Inline,
                    "self-block" => AnchorSizeKeyword.SelfBlock,
                    "self-inline" => AnchorSizeKeyword.SelfInline,
                    _ => AnchorSizeKeyword.None,
                };

                if (size == AnchorSizeKeyword.None)
                    return null;
            }
            else if (!TryParseSide(value, out side, out percentage))
            {
                return null;
            }
        }

        // anchor() requires an <anchor-side>; anchor-size() may omit both arguments
        if (!isSize && !hasSide)
            return null;

        return new CssAnchorFunction(isSize, name, side, percentage, size, fallback, sideValue);
    }

    private static bool TryParseSide(CssComponentValue value, out AnchorSide side, out double percentage)
    {
        percentage = 0;
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Ident } keyword:
                side = InfraAscii.ToAsciiLowercase(keyword.Token.Value) switch
                {
                    "inside" => AnchorSide.Inside,
                    "outside" => AnchorSide.Outside,
                    "top" => AnchorSide.Top,
                    "left" => AnchorSide.Left,
                    "right" => AnchorSide.Right,
                    "bottom" => AnchorSide.Bottom,
                    "start" => AnchorSide.Start,
                    "end" => AnchorSide.End,
                    "self-start" => AnchorSide.SelfStart,
                    "self-end" => AnchorSide.SelfEnd,
                    "center" => AnchorSide.Percentage,
                    _ => (AnchorSide)byte.MaxValue,
                };

                if (side == AnchorSide.Percentage)
                {
                    percentage = 50;
                }

                return side != (AnchorSide)byte.MaxValue;

            case CssPreservedToken { Type: CssTokenType.Percentage } token:
                side = AnchorSide.Percentage;
                percentage = token.Token.NumericValue;
                return true;

            // A math function of <percentage> (e.g. calc(50%))
            case CssFunction function when CssCalc.IsMathFunction(function.Name) && CssCalc.Evaluate(function, PercentageContext) is { TypeUnit: "%" } result:
                side = AnchorSide.Percentage;
                percentage = result.IsNumeric ? result.Value : 0;
                return true;

            default:
                side = default;
                return false;
        }
    }

    // The tree counting functions are resolved by the computed value (only the type matters for the specified value)
    private static readonly CssCalcContext PercentageContext = new() { ResolveTreeCountingFunction = static _ => 1 };

    /// <summary>Returns whether a component value is a &lt;length-percentage&gt; (a nested anchor function of an allowed kind included).</summary>
    private static bool IsValidFallback(CssComponentValue value, CssAnchorFunctions allowed)
    {
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Percentage }:
                return true;
            case CssPreservedToken { Type: CssTokenType.Number } number:
                return number.Token.NumericValue == 0;
            case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
                return CssCalc.TryConvertToCanonicalUnit(0, InfraAscii.ToAsciiLowercase(dimension.Token.Value), out _, out var unit) ? unit == "px" : CssCalc.IsRelativeUnit(InfraAscii.ToAsciiLowercase(dimension.Token.Value)) && ValueMatcher.IsKnownUnit(InfraAscii.ToAsciiLowercase(dimension.Token.Value));
            case CssFunction function when CssCalc.GetAnchorFunction(function.Name) is not null:
                return IsValid(function, allowed);
            case CssFunction function when CssCalc.IsMathFunction(function.Name):
                return CssCalc.Evaluate(function, GetTypeCheckingContext(allowed)) is { TypeUnit: "px" };
            default:
                return false;
        }
    }

    private static readonly CssCalcContext[] TypeCheckingContexts = [.. Enumerable.Range(0, 4).Select(flags => new CssCalcContext
    {
        ResolveDimension = static (value, unit) => CssCalc.IsRelativeUnit(unit) ? (value, "px") : null,
        ResolveTreeCountingFunction = static _ => 1,
        PercentageUnit = "px",
        AnchorFunctions = (CssAnchorFunctions)flags,
    })];

    /// <summary>The context that type-checks a math function of a &lt;length-percentage&gt; property that accepts the anchor functions.</summary>
    internal static CssCalcContext GetTypeCheckingContext(CssAnchorFunctions allowed) => TypeCheckingContexts[(int)allowed & 3];

    /// <summary>
    /// Serializes the function in its canonical form (the anchor name first, the fallback serialized as a specified value, e.g.
    /// <c>anchor(top --a, 0)</c> is <c>anchor(--a top, 0px)</c>); returns the function as written when it is not valid.
    /// </summary>
    public static string Serialize(CssFunction function) => Serialize(function, serializeFallback: null);

    /// <summary>
    /// Serializes the function in its canonical form, the fallback and a math function of the side serialized by
    /// <paramref name="serializeFallback"/> and <paramref name="serializeSide"/> (e.g. their computed values) when they are set.
    /// </summary>
    public static string Serialize(CssFunction function, Func<CssComponentValue, string>? serializeFallback, Func<CssFunction, string>? serializeSide = null)
    {
        if (Parse(function, CssAnchorFunctions.Anchor | CssAnchorFunctions.AnchorSize) is not { } parsed)
            return CssSerializer.SerializeValue([function], property: null);

        var builder = new StringBuilder();
        builder.Append(parsed.IsSize ? "anchor-size(" : "anchor(");
        var first = true;
        if (parsed.Name is { } name)
        {
            builder.Append(CssSerializer.SerializeIdentifier(name));
            first = false;
        }

        if (parsed.SideValue is { } value)
        {
            if (!first)
            {
                builder.Append(' ');
            }

            first = false;
            builder.Append(value is CssFunction side && CssCalc.IsMathFunction(side.Name)
                ? serializeSide?.Invoke(side) ?? CssCalc.SerializeSpecifiedValue(side) ?? CssSerializer.SerializeValue([side], property: null)
                : InfraAscii.ToAsciiLowercase(CssSerializer.SerializeValue([value], property: null)));
        }

        if (parsed.Fallback is { } fallback)
        {
            if (!first)
            {
                builder.Append(", ");
            }

            builder.Append(serializeFallback is null ? SerializeLengthPercentage(fallback) : serializeFallback(fallback));
        }

        builder.Append(')');
        return builder.ToString();
    }

    /// <summary>Serializes a specified &lt;length-percentage&gt; that may contain anchor functions (0 is 0px).</summary>
    internal static string SerializeLengthPercentage(CssComponentValue value) => value switch
    {
        CssPreservedToken { Type: CssTokenType.Number } => "0px",
        CssFunction function when CssCalc.GetAnchorFunction(function.Name) is not null => Serialize(function),
        CssFunction function when CssCalc.IsMathFunction(function.Name) => CssCalc.SerializeSpecifiedValue(function, percentagesAreLengths: true) ?? CssSerializer.SerializeValue([function], property: null),
        _ => CssSerializer.SerializeValue([value], property: null),
    };

    /// <summary>
    /// Replaces the anchor functions of a value (https://drafts.csswg.org/css-anchor-position-1/#anchor-resolution): each function is
    /// resolved to a length by <paramref name="resolve"/> (a resolvable anchor function), else replaced by its fallback value (whose
    /// anchor functions are resolved in turn). Returns null when a function is unresolvable and has no fallback: the declaration
    /// is then invalid at computed-value time. Returns the value itself when it contains no anchor function.
    /// </summary>
    /// <param name="values">The component values of the value.</param>
    /// <param name="resolve">Returns the length (in px) of a resolvable function, or null.</param>
    public static List<CssComponentValue>? Substitute(List<CssComponentValue> values, Func<CssAnchorFunction, double?> resolve)
    {
        if (!CssCalc.ContainsAnchorFunction(values))
            return values;

        var result = new List<CssComponentValue>(values.Count);
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function when CssCalc.GetAnchorFunction(function.Name) is not null:
                {
                    if (Parse(function, CssAnchorFunctions.Anchor | CssAnchorFunctions.AnchorSize) is not { } parsed)
                        return null;

                    if (resolve(parsed) is { } length)
                    {
                        result.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Dimension, NumericValue = LengthOrZero(length), Value = "px" }));
                        break;
                    }

                    if (parsed.Fallback is not { } fallback || Substitute([fallback], resolve) is not [var substituted])
                        return null;

                    result.Add(substituted);
                    break;
                }

                case CssFunction function when CssCalc.ContainsAnchorFunction(function.Value):
                {
                    if (Substitute(function.Value, resolve) is not { } arguments)
                        return null;

                    var copy = new CssFunction(function.Name);
                    copy.Value.AddRange(arguments);
                    result.Add(copy);
                    break;
                }

                case CssSimpleBlock block when CssCalc.ContainsAnchorFunction(block.Value):
                {
                    if (Substitute(block.Value, resolve) is not { } blockValues)
                        return null;

                    var copy = new CssSimpleBlock(block.AssociatedToken);
                    copy.Value.AddRange(blockValues);
                    result.Add(copy);
                    break;
                }

                default:
                    result.Add(value);
                    break;
            }
        }

        return result;

        static double LengthOrZero(double length) => double.IsFinite(length) ? length : 0;
    }
}
