using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// Computes color-mix() (https://drafts.csswg.org/css-color-5/#color-mix): the colors are converted to the interpolation
/// color space and interpolated (https://drafts.csswg.org/css-color-4/#interpolation) with the missing components carried
/// forward, premultiplied alpha and the hue interpolation methods.
/// </summary>
internal static class CssColorMix
{
    internal enum HueMethod
    {
        Shorter,
        Longer,
        Increasing,
        Decreasing,
    }

    /// <summary>
    /// Computes a color-mix() function; <paramref name="resolveColor"/> resolves the colors to mix and <paramref name="evaluatePercentage"/>
    /// the math functions of the percentages. Returns null when the function cannot be computed (e.g. a color is currentcolor).
    /// </summary>
    public static CssColor? Compute(CssFunction function, Func<CssComponentValue, CssColor?> resolveColor, Func<CssFunction, double?> evaluatePercentage)
    {
        var arguments = CssValueComputer.SplitArguments(function.Value);
        var space = "oklab";
        var hueMethod = HueMethod.Shorter;
        var index = 0;
        if (arguments.Count > 0 && arguments[0] is [CssPreservedToken { Type: CssTokenType.Ident } inToken, CssPreservedToken { Type: CssTokenType.Ident } spaceToken, ..] && inToken.Token.IsIdent("in"))
        {
            space = InfraAscii.ToAsciiLowercase(spaceToken.Token.Value);
            if (space == "xyz")
            {
                space = "xyz-d65";
            }

            if (arguments[0].Count == 4 && arguments[0][2] is CssPreservedToken { Type: CssTokenType.Ident } hue && arguments[0][3] is CssPreservedToken { Type: CssTokenType.Ident } hueKeyword && hueKeyword.Token.IsIdent("hue") && CssColorConversion.IsPolar(space))
            {
                HueMethod? method = InfraAscii.ToAsciiLowercase(hue.Token.Value) switch
                {
                    "shorter" => HueMethod.Shorter,
                    "longer" => HueMethod.Longer,
                    "increasing" => HueMethod.Increasing,
                    "decreasing" => HueMethod.Decreasing,
                    _ => null,
                };

                if (method is null)
                    return null;

                hueMethod = method.Value;
            }
            else if (arguments[0].Count != 2)
            {
                return null;
            }

            index = 1;
        }

        if (!CssColorConversion.IsSupportedSpace(space) || arguments.Count - index < 1)
            return null;

        var colors = new List<CssColorComponents>(arguments.Count - index);
        var percentages = new List<double?>(arguments.Count - index);
        for (var i = index; i < arguments.Count; i++)
        {
            CssComponentValue? colorValue = null;
            double? percentage = null;
            var hasPercentage = false;
            foreach (var value in arguments[i])
            {
                if (!hasPercentage && value is CssPreservedToken { Type: CssTokenType.Percentage } percentageToken)
                {
                    percentage = percentageToken.Token.NumericValue;
                    hasPercentage = true;
                }
                else if (!hasPercentage && value is CssFunction math && CssCalc.IsMathFunction(math.Name))
                {
                    percentage = evaluatePercentage(math);
                    if (percentage is null)
                        return null;

                    hasPercentage = true;
                }
                else if (colorValue is null)
                {
                    colorValue = value;
                }
                else
                {
                    return null;
                }
            }

            if (colorValue is null || resolveColor(colorValue) is not { } color || CssColorConversion.FromColor(color) is not { } components)
                return null;

            // https://drafts.csswg.org/css-color-4/#interpolation: the colors are converted to the interpolation color space
            colors.Add(CssColorConversion.Convert(components, space));
            percentages.Add(percentage is { } p ? Math.Clamp(p, 0, 100) : null);
        }

        var (weights, leftover) = NormalizePercentages(percentages);

        // https://drafts.csswg.org/css-color-5/#color-mix-result: the items are mixed in order, from the first one
        var result = colors[0];
        var weight = weights[0];
        for (var i = 1; i < colors.Count; i++)
        {
            var combined = weight + weights[i];
            var progress = combined > 0 ? weights[i] / combined : 0.5;
            result = Interpolate(result, colors[i], progress, hueMethod);
            weight = combined;
        }

        var alphaMultiplier = 1 - (leftover / 100);
        if (result.Alpha is { } alpha)
        {
            result = result with { Alpha = alpha * alphaMultiplier };
        }

        return CssColorConversion.ToColor(result);
    }

    /// <summary>https://drafts.csswg.org/css-values-5/#normalize-mix-percentages, with the force normalization flag set.</summary>
    private static (double[] Percentages, double Leftover) NormalizePercentages(List<double?> percentages)
    {
        var specifiedSum = Math.Min(100, percentages.Sum(p => p ?? 0));
        var omitted = percentages.Count(p => p is null);
        var result = new double[percentages.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = percentages[i] ?? ((100 - specifiedSum) / omitted);
        }

        var total = result.Sum();
        if (total > 0)
        {
            for (var i = 0; i < result.Length; i++)
            {
                result[i] *= 100 / total;
            }
        }

        return (result, total < 100 ? 100 - total : 0);
    }

    /// <summary>
    /// Interpolates two colors in the same color space (https://drafts.csswg.org/css-color-4/#interpolation): a missing component
    /// takes the value of the other color, and the components are premultiplied by the alpha (except the hue).
    /// </summary>
    internal static CssColorComponents Interpolate(CssColorComponents a, CssColorComponents b, double progress, HueMethod hueMethod)
    {
        var hueIndex = CssColorConversion.GetHueIndex(a.Space);
        var alpha1 = a.Alpha ?? b.Alpha;
        var alpha2 = b.Alpha ?? a.Alpha;
        double? alpha = alpha1 is { } x1 && alpha2 is { } x2 ? Interpolate(x1, x2, progress) : null;
        var channels = new double?[3];
        for (var i = 0; i < 3; i++)
        {
            var c1 = a.Channels[i] ?? b.Channels[i];
            var c2 = b.Channels[i] ?? a.Channels[i];
            if (c1 is not { } v1 || c2 is not { } v2)
                continue;

            if (i == hueIndex)
            {
                var (h1, h2) = FixupHues(NormalizeHue(v1), NormalizeHue(v2), hueMethod);
                channels[i] = NormalizeHue(Interpolate(h1, h2, progress));
                continue;
            }

            // https://drafts.csswg.org/css-color-4/#interpolation-alpha: premultiplied interpolation (a missing alpha is not premultiplied)
            if (alpha1 is { } p1 && alpha2 is { } p2)
            {
                var value = Interpolate(v1 * p1, v2 * p2, progress);
                channels[i] = alpha is 0 or null ? value : value / alpha.Value;
            }
            else
            {
                channels[i] = Interpolate(v1, v2, progress);
            }
        }

        return new CssColorComponents(a.Space, channels, alpha);
    }

    private static double Interpolate(double a, double b, double progress) => (a * (1 - progress)) + (b * progress);

    private static double NormalizeHue(double hue)
    {
        var result = hue % 360;
        return result < 0 ? result + 360 : result;
    }

    /// <summary>https://drafts.csswg.org/css-color-4/#hue-interpolation</summary>
    private static (double H1, double H2) FixupHues(double h1, double h2, HueMethod method)
    {
        var delta = h2 - h1;
        switch (method)
        {
            case HueMethod.Shorter:
                if (delta > 180)
                {
                    h1 += 360;
                }
                else if (delta < -180)
                {
                    h2 += 360;
                }

                break;

            case HueMethod.Longer:
                if (delta > 0 && delta < 180)
                {
                    h1 += 360;
                }
                else if (delta > -180 && delta <= 0)
                {
                    h2 += 360;
                }

                break;

            case HueMethod.Increasing:
                if (h2 < h1)
                {
                    h2 += 360;
                }

                break;

            case HueMethod.Decreasing:
                if (h1 < h2)
                {
                    h1 += 360;
                }

                break;
        }

        return (h1, h2);
    }
}
