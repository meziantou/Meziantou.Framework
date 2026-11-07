using Meziantou.Framework.WebPlatform.Css.Syntax;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The interpolation of aspect-ratio (https://drafts.csswg.org/css-values-4/#combine-ratio): the ratios are interpolated by
/// their logarithm; the auto keyword must be on both values. A degenerate ratio (e.g. 0 / 1) is interpolated discretely.
/// </summary>
internal static class CssAspectRatioInterpolation
{
    public static Func<double, string?>? Prepare(string from, string to)
    {
        if (Parse(from) is not { } a || Parse(to) is not { } b || a.Auto != b.Auto || a.Ratio is not { } ratioA || b.Ratio is not { } ratioB)
            return null;

        var logA = Math.Log(ratioA);
        var logB = Math.Log(ratioB);
        return progress => Serialize(a.Auto, Math.Exp((logA * (1 - progress)) + (logB * progress)));
    }

    private static (bool Auto, double? Ratio)? Parse(string value)
    {
        var auto = false;
        var numbers = new List<double>();
        var hasSlash = false;
        foreach (var component in CssGenericInterpolation.Parse(value))
        {
            switch (component)
            {
                case CssPreservedToken { Type: CssTokenType.Ident } ident when ident.Token.IsIdent("auto"):
                    auto = true;
                    break;
                case CssPreservedToken { Type: CssTokenType.Delim } delim when delim.Token.Value == "/":
                    hasSlash = true;
                    break;
                case var number when CssGenericInterpolation.TryGetNumber(number, out var n, out var unit) && unit.Length == 0:
                    numbers.Add(n);
                    break;
                default:
                    return null;
            }
        }

        if (numbers.Count == 0)
            return auto ? (true, null) : null;

        var ratio = numbers.Count == 2 && hasSlash ? numbers[0] / numbers[1] : numbers[0];
        return (auto, ratio > 0 && double.IsFinite(ratio) ? ratio : null);
    }

    private static string Serialize(bool auto, double ratio) => (auto ? "auto " : "") + CssGenericInterpolation.FormatNumber(ratio) + " / 1";
}
