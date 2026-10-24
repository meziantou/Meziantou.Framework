using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The interpolation and the addition of the values of the sizing properties with <c>calc-size()</c>
/// (https://drafts.csswg.org/css-values-5/#interpolate-calc-size) and <c>interpolate-size</c>
/// (https://drafts.csswg.org/css-values-5/#interpolate-size): a <c>calc-size()</c> combines with another one whose basis is
/// the same (after they are prepared for interpolation) or <c>any</c>, with a &lt;length-percentage&gt; (treated as
/// <c>calc-size(any, value)</c>), and with a sizing keyword (treated as <c>calc-size(keyword, size)</c>); a sizing keyword
/// combines with a &lt;length-percentage&gt; when <c>interpolate-size</c> is <c>allow-keywords</c>.
/// </summary>
/// <remarks>
/// The result is a <c>calc-size()</c> whose calculation is the combination of the two calculations, e.g. <c>calc-size(auto,
/// (size) * 0.5 + (0px) * 0.5)</c>: it is simplified when it is computed, and resolved by the layout (the <c>size</c> keyword
/// terms are not merged by the simplification of the calculations).
/// </remarks>
internal static class CssCalcSizeInterpolation
{
    /// <summary>
    /// The limit of the length of a prepared calculation: "If this substitution would produce a value larger than an UA-defined
    /// limit, return failure" (https://drafts.csswg.org/css-values-5/#substitute-into-a-calc-size-calculation).
    /// </summary>
    private const int MaxCalculationLength = 10_000;

    /// <summary>Returns whether a property is a sizing property whose values may be a <c>calc-size()</c>.</summary>
    public static bool IsSizingProperty(string property) => property is "width" or "height" or "min-width" or "min-height" or "max-width" or "max-height"
        or "inline-size" or "block-size" or "min-inline-size" or "min-block-size" or "max-inline-size" or "max-block-size" or "flex-basis";

    /// <summary>
    /// Prepares the interpolation of two values of a sizing property, or returns null when neither is a <c>calc-size()</c> (and
    /// no keyword is interpolated with a length): the generic interpolation applies. The function returns null when the values
    /// cannot be interpolated (the interpolation is discrete).
    /// </summary>
    public static Func<double, string?>? Prepare(CssInterpolationContext context, string from, string to, out bool handled)
    {
        handled = false;
        if (TryPrepare(context, from, to, out var a, out var b) is not { } prepared)
            return null;

        // A value that fails to be prepared (its expansion is too large) cannot be interpolated
        handled = true;
        if (!prepared || GetCombinedBasis(a, b) is not { } basis)
            return null;

        // "the result's calc-size calculation is the interpolation of the two input calc-size calculations" (also at the ends of
        // the interval: calc-size(auto, 90px) behaves as auto)
        return progress => "calc-size(" + basis + ", (" + a.Calculation + ") * " + CssGenericInterpolation.FormatNumber(1 - progress) + " + (" + b.Calculation + ") * " + CssGenericInterpolation.FormatNumber(progress) + ")";
    }

    /// <summary>
    /// Adds (or accumulates) two values of a sizing property (https://drafts.csswg.org/css-values-5/#interpolate-calc-size): the
    /// calculations are added. Returns null when the values are not handled here, or <paramref name="value"/> when they cannot
    /// be combined.
    /// </summary>
    public static string? Combine(CssInterpolationContext context, string underlying, string value)
    {
        if (TryPrepare(context, underlying, value, out var a, out var b) is not { } prepared)
            return null;

        if (!prepared || GetCombinedBasis(a, b) is not { } basis)
            return value;

        return "calc-size(" + basis + ", (" + a.Calculation + ") + (" + b.Calculation + "))";
    }

    /// <summary>
    /// Prepares two values for interpolation: null when they are not handled here (the generic interpolation applies), false
    /// when one of them fails to be prepared (https://drafts.csswg.org/css-values-5/#calc-size-prepare-for-interpolation: they
    /// cannot be interpolated), true otherwise.
    /// </summary>
    private static bool? TryPrepare(CssInterpolationContext context, string from, string to, out PreparedValue a, out PreparedValue b)
    {
        a = default;
        b = default;
        var fromKind = Classify(from);
        var toKind = Classify(to);
        if (fromKind == ValueKind.Other || toKind == ValueKind.Other)
            return null;

        if (fromKind != ValueKind.CalcSize && toKind != ValueKind.CalcSize)
        {
            // https://drafts.csswg.org/css-values-5/#valdef-interpolate-size-allow-keywords: "Sizing values can also be combined if
            // one of them is an <size-keyword> and the other is a <length-percentage>"
            if (fromKind == toKind || !AllowsKeywords(context))
                return null;
        }

        if (Prepare(from, fromKind) is not { } preparedFrom || Prepare(to, toKind) is not { } preparedTo)
            return false;

        a = preparedFrom;
        b = preparedTo;
        return true;
    }

    private static bool AllowsKeywords(CssInterpolationContext context) => context.Style?.GetComputedValue("interpolate-size") == "allow-keywords";

    /// <summary>
    /// "If they are identical: the result's calc-size basis is that basis value. If either is any: the result's calc-size basis
    /// is the non-any basis. Otherwise: the values cannot be interpolated."
    /// </summary>
    private static string? GetCombinedBasis(PreparedValue a, PreparedValue b)
    {
        if (string.Equals(a.Basis, b.Basis, StringComparison.Ordinal))
            return a.Basis;

        if (a.Basis == "any")
            return b.Basis;

        return b.Basis == "any" ? a.Basis : null;
    }

    private enum ValueKind
    {
        Other,
        Keyword,
        LengthPercentage,
        CalcSize,
    }

    private static ValueKind Classify(string value)
    {
        switch (CssGenericInterpolation.Parse(value))
        {
            case [CssPreservedToken { Type: CssTokenType.Ident } ident]:
                return InfraAscii.ToAsciiLowercase(ident.Token.Value) is "auto" or "min-content" or "max-content" or "fit-content" or "stretch" or "contain"
                    or "-webkit-fill-available" or "-webkit-min-content" or "-webkit-max-content" or "-webkit-fit-content" or "-moz-available" or "-moz-fit-content"
                    ? ValueKind.Keyword
                    : ValueKind.Other;

            case [CssPreservedToken { Type: CssTokenType.Dimension or CssTokenType.Percentage }]:
                return ValueKind.LengthPercentage;

            case [CssPreservedToken { Type: CssTokenType.Number } number] when number.Token.NumericValue == 0:
                return ValueKind.LengthPercentage;

            case [CssFunction function] when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "calc-size"):
                return ValueKind.CalcSize;

            case [CssFunction function] when CssCalc.IsMathFunction(function.Name):
                return ValueKind.LengthPercentage;

            default:
                return ValueKind.Other;
        }
    }

    /// <summary>
    /// Prepares a value for interpolation (https://drafts.csswg.org/css-values-5/#calc-size-prepare-for-interpolation): a
    /// &lt;calc-sum&gt; is <c>calc-size(any, value)</c>, a keyword <c>calc-size(keyword, size)</c>; the basis of a <c>calc-size()</c>
    /// is simplified to a keyword, <c>any</c> or <c>100%</c>, and substituted into the calculation.
    /// </summary>
    private static PreparedValue? Prepare(string value, ValueKind kind)
    {
        var components = CssGenericInterpolation.Parse(value);
        return kind switch
        {
            ValueKind.Keyword => new PreparedValue(Normalize(((CssPreservedToken)components[0]).Token.Value), "size"),
            ValueKind.LengthPercentage => new PreparedValue("any", Serialize(components)),
            _ => PrepareCalcSize((CssFunction)components[0]),
        };

        static string Normalize(string keyword) => InfraAscii.ToAsciiLowercase(keyword) switch
        {
            "-webkit-fill-available" or "-moz-available" => "stretch",
            "-webkit-min-content" => "min-content",
            "-webkit-max-content" => "max-content",
            "-webkit-fit-content" or "-moz-fit-content" => "fit-content",
            var other => other,
        };
    }

    private static PreparedValue? PrepareCalcSize(CssFunction function)
    {
        var arguments = ValueMatcher.SplitAtCommas(function.Value);
        if (arguments.Count != 2)
            return null;

        var basis = ValueMatcher.RemoveWhitespace(arguments[0]);
        var calculation = arguments[1];
        PreparedValue? result;
        switch (basis)
        {
            case [CssPreservedToken { Type: CssTokenType.Ident } keyword]:
                result = new PreparedValue(InfraAscii.ToAsciiLowercase(keyword.Token.Value), Serialize(calculation));
                break;

            case [CssFunction nested] when InfraAscii.EqualsIgnoreAsciiCase(nested.Name, "calc-size"):
                // "the calc-size basis of the outer function is replaced with that of the inner function, and the inner function's
                // calc-size calculation is substituted into the outer function's calc-size calculation"
                if (PrepareCalcSize(nested) is not { } inner)
                    return null;

                result = new PreparedValue(inner.Basis, Substitute(calculation, inner.Calculation));
                break;

            default:
                // A <calc-sum> without percentage: "Replace the basis with any, and the original basis is substituted into the
                // calc-size calculation"; with a percentage: "Replace the basis with 100% and the original basis is
                // de-percentified, then substituted into the calc-size calculation"
                var sum = Serialize(basis);
                result = ContainsPercentage(basis)
                    ? new PreparedValue("100%", Substitute(calculation, Serialize(basis, depercentify: true)))
                    : new PreparedValue("any", Substitute(calculation, sum));
                break;
        }

        return result is { } prepared && prepared.Calculation.Length > MaxCalculationLength ? null : result;
    }

    /// <summary>Replaces the <c>size</c> keywords of a calculation by a value wrapped in parentheses (https://drafts.csswg.org/css-values-5/#substitute-into-a-calc-size-calculation).</summary>
    private static string Substitute(List<CssComponentValue> calculation, string insertion)
    {
        var builder = new StringBuilder();
        Append(builder, calculation, insertion, depercentify: false);
        return builder.ToString();
    }

    private static string Serialize(List<CssComponentValue> values, bool depercentify = false)
    {
        var builder = new StringBuilder();
        Append(builder, values, insertion: null, depercentify);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, List<CssComponentValue> values, string? insertion, bool depercentify)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Ident } ident when insertion is not null && ident.Token.IsIdent("size"):
                    builder.Append('(').Append(insertion).Append(')');
                    break;

                case CssPreservedToken { Type: CssTokenType.Percentage } percentage when depercentify:
                    // https://drafts.csswg.org/css-values-5/#de-percentify-a-calc-size-calculation: "Replace every instance of a
                    // <percentage-token> in calc with (size * N), where N is the percentage's value divided by 100"
                    builder.Append("(size * ").Append(CssGenericInterpolation.FormatNumber(percentage.Token.NumericValue / 100)).Append(')');
                    break;

                case CssFunction function:
                    builder.Append(function.Name).Append('(');
                    Append(builder, function.Value, insertion, depercentify);
                    builder.Append(')');
                    break;

                case CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block:
                    builder.Append('(');
                    Append(builder, block.Value, insertion, depercentify);
                    builder.Append(')');
                    break;

                case CssPreservedToken { Type: CssTokenType.Whitespace }:
                    builder.Append(' ');
                    break;

                default:
                    builder.Append(CssSerializer.SerializeValue([value], property: null));
                    break;
            }
        }
    }

    private static bool ContainsPercentage(List<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Percentage }:
                    return true;
                case CssFunction function when ContainsPercentage(function.Value):
                    return true;
                case CssSimpleBlock block when ContainsPercentage(block.Value):
                    return true;
            }
        }

        return false;
    }

    /// <summary>A value prepared for interpolation: its basis (a keyword, <c>any</c> or <c>100%</c>) and its calculation.</summary>
    private readonly record struct PreparedValue(string Basis, string Calculation);
}
