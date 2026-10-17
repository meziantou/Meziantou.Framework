using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// High dynamic range (https://drafts.csswg.org/css-color-hdr-1/)
internal static partial class CssPropertyRules
{
    /// <summary>The keywords of dynamic-range-limit, in the order of the parameters of its computed dynamic-range-limit-mix().</summary>
    private static readonly string[] DynamicRangeLimitKeywords = ["standard", "constrained", "no-limit"];

    /// <summary>
    /// https://drafts.csswg.org/css-color-hdr-1/#computing-dynamic-range-limit: a dynamic-range-limit-mix() computes to the
    /// weighted sum of the percentages of standard, constrained and no-limit in its (computed) parameters, e.g.
    /// dynamic-range-limit-mix(no-limit 10%, standard 30%) is dynamic-range-limit-mix(standard 75%, no-limit 25%). The value
    /// is a keyword when one of the percentages is 100%. A mix whose percentages sum to zero is the initial value (no-limit):
    /// such a mix is valid since https://github.com/w3c/csswg-drafts/issues/11678. The math functions of the value are already
    /// computed.
    /// </summary>
    public static string ComputeDynamicRangeLimit(string value)
    {
        if (ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is not [var component])
            return value;

        if (EvaluateDynamicRangeLimit(component) is not { } weights)
            return "no-limit";

        var parameters = new List<string>(DynamicRangeLimitKeywords.Length);
        for (var i = 0; i < DynamicRangeLimitKeywords.Length; i++)
        {
            var percentage = Math.Round(weights[i] * 100, 6, MidpointRounding.AwayFromZero);
            if (percentage == 100)
                return DynamicRangeLimitKeywords[i];

            if (percentage != 0)
            {
                parameters.Add(DynamicRangeLimitKeywords[i] + " " + CssSerializer.SerializeNumber(percentage) + "%");
            }
        }

        // All the percentages are 0%: the computed value is the initial value
        return parameters.Count == 0 ? "no-limit" : "dynamic-range-limit-mix(" + string.Join(", ", parameters) + ")";
    }

    /// <summary>Returns the fractions of standard, constrained and no-limit of a dynamic-range-limit value, or null when the value is not valid.</summary>
    private static double[]? EvaluateDynamicRangeLimit(CssComponentValue value)
    {
        if (value is CssPreservedToken { Type: CssTokenType.Ident } ident)
        {
            var index = Array.FindIndex(DynamicRangeLimitKeywords, keyword => ident.Token.IsIdent(keyword));
            if (index < 0)
                return null;

            var weights = new double[DynamicRangeLimitKeywords.Length];
            weights[index] = 1;
            return weights;
        }

        if (value is not CssFunction function || !InfraAscii.EqualsIgnoreAsciiCase(function.Name, "dynamic-range-limit-mix"))
            return null;

        var result = new double[DynamicRangeLimitKeywords.Length];
        double sum = 0;
        foreach (var item in SplitDynamicRangeLimitMix(function))
        {
            // <'dynamic-range-limit'> && <percentage [0,100]>: the parameter and the percentage are in any order
            if (item is not [var first, var second])
                return null;

            var (limit, percentage) = first is CssPreservedToken { Type: CssTokenType.Percentage } ? (second, first) : (first, second);
            if (percentage is not CssPreservedToken { Type: CssTokenType.Percentage } percentageToken || EvaluateDynamicRangeLimit(limit) is not { } weights)
                return null;

            var amount = percentageToken.Token.NumericValue;
            sum += amount;
            for (var i = 0; i < result.Length; i++)
            {
                result[i] += weights[i] * amount;
            }
        }

        // The initial value, no-limit
        if (sum <= 0)
            return [0, 0, 1];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] /= sum;
        }

        return result;
    }

    /// <summary>Returns the comma-separated items of a dynamic-range-limit-mix(), without whitespace.</summary>
    private static List<List<CssComponentValue>> SplitDynamicRangeLimitMix(CssFunction function)
    {
        var items = new List<List<CssComponentValue>> { new() };
        foreach (var value in ValueMatcher.RemoveWhitespace(function.Value))
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                items.Add([]);
            }
            else
            {
                items[^1].Add(value);
            }
        }

        return items;
    }
}
