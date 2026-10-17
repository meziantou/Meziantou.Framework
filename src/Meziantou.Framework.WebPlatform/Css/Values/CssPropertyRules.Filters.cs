using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Filter effects (https://drafts.fxtf.org/filter-effects/#FilterProperty)
internal static partial class CssPropertyRules
{
    /// <summary>The filter functions whose amount is clamped to 1 (100%).</summary>
    private static bool IsClampedFilter(string name) => name is "grayscale" or "invert" or "opacity" or "sepia";

    /// <summary>
    /// The specified value of filter and backdrop-filter: the zero lengths and angles have a unit, the color of drop-shadow()
    /// comes first, and the amounts of grayscale(), invert(), opacity() and sepia() are clamped to 1 (100%)
    /// (https://drafts.fxtf.org/filter-effects/#supported-filter-functions).
    /// </summary>
    private static string? CanonicalizeFilter(List<CssComponentValue> significant)
    {
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident }])
            return null;

        var functions = new List<string>(significant.Count);
        foreach (var value in significant)
        {
            if (value is not CssFunction function || InfraAscii.ToAsciiLowercase(function.Name) is not { } name || name == "url")
            {
                functions.Add(CssSerializer.SerializeValue([value], property: null));
                continue;
            }

            var arguments = ValueMatcher.RemoveWhitespace(function.Value);
            var serialized = new List<string>(arguments.Count);
            CssComponentValue? color = null;
            foreach (var argument in arguments)
            {
                switch (argument)
                {
                    case CssPreservedToken { Type: CssTokenType.Number } zero when zero.Token.NumericValue == 0 && name is "blur" or "drop-shadow":
                        serialized.Add("0px");
                        break;
                    case CssPreservedToken { Type: CssTokenType.Number } zero when zero.Token.NumericValue == 0 && name == "hue-rotate":
                        serialized.Add("0deg");
                        break;
                    case CssPreservedToken { Type: CssTokenType.Number } number when IsClampedFilter(name) && number.Token.NumericValue > 1:
                        serialized.Add("1");
                        break;
                    case CssPreservedToken { Type: CssTokenType.Percentage } percentage when IsClampedFilter(name) && percentage.Token.NumericValue > 100:
                        serialized.Add("100%");
                        break;
                    case not CssPreservedToken { Type: CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension } when name == "drop-shadow" && ValueMatcher.IsColor(argument):
                        color = argument;
                        break;
                    default:
                        serialized.Add(CssSerializer.SerializeValue([argument], property: null));
                        break;
                }
            }

            if (color is not null)
            {
                serialized.Insert(0, CssSerializer.SerializeValue([color], property: null));
            }

            functions.Add(name + "(" + string.Join(' ', serialized) + ")");
        }

        return string.Join(' ', functions);
    }

    /// <summary>
    /// The computed value of filter and backdrop-filter (https://drafts.fxtf.org/filter-effects/#funcdef-filter-blur...):
    /// the omitted arguments get their default value, the percentages are numbers, the amounts are clamped to their range,
    /// and drop-shadow() has a color (currentcolor by default) and a blur radius. The value is the generic computed value
    /// (the math functions are resolved, the lengths are absolute).
    /// </summary>
    public static string ComputeFilter(string value)
    {
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        if (values is [CssPreservedToken { Type: CssTokenType.Ident }])
            return value;

        var functions = new List<string>(values.Count);
        foreach (var component in values)
        {
            if (component is not CssFunction function || InfraAscii.ToAsciiLowercase(function.Name) is not { } name || name == "url")
            {
                functions.Add(CssSerializer.SerializeValue([component], property: null));
                continue;
            }

            var arguments = ValueMatcher.RemoveWhitespace(function.Value);
            string argument;
            switch (name)
            {
                case "blur":
                    argument = arguments is [var radius] ? CssSerializer.SerializeValue([radius], property: null) : "0px";
                    break;
                case "hue-rotate":
                    argument = arguments is [CssPreservedToken { Type: CssTokenType.Dimension } angle] ? CssSerializer.SerializeValue([angle], property: null) : "0deg";
                    break;
                case "drop-shadow":
                {
                    string? color = null;
                    var lengths = new List<string>(3);
                    foreach (var part in arguments)
                    {
                        if (part is CssPreservedToken { Type: CssTokenType.Number or CssTokenType.Dimension })
                        {
                            lengths.Add(part is CssPreservedToken { Type: CssTokenType.Number } ? "0px" : CssSerializer.SerializeValue([part], property: null));
                        }
                        else
                        {
                            color = CssSerializer.SerializeValue([part], property: null);
                        }
                    }

                    if (lengths.Count == 2)
                    {
                        lengths.Add("0px");
                    }

                    argument = (color ?? "currentcolor") + " " + string.Join(' ', lengths);
                    break;
                }

                default:
                {
                    // brightness(), contrast(), grayscale(), invert(), opacity(), saturate(), sepia(): a non-negative amount
                    double amount = 1;
                    if (arguments is [CssPreservedToken { Type: CssTokenType.Number or CssTokenType.Percentage } token])
                    {
                        amount = token.Type == CssTokenType.Percentage ? token.Token.NumericValue / 100 : token.Token.NumericValue;
                    }

                    amount = Math.Max(amount, 0);
                    if (IsClampedFilter(name))
                    {
                        amount = Math.Min(amount, 1);
                    }

                    argument = CssSerializer.SerializeNumber(amount);
                    break;
                }
            }

            functions.Add(name + "(" + argument + ")");
        }

        return string.Join(' ', functions);
    }
}
