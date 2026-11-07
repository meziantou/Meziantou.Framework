using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// Computes the relative colors (https://drafts.csswg.org/css-color-5/#relative-colors), e.g. rgb(from red r calc(g * 2) b):
/// the origin color is converted to the relative color processing space, and the channel keywords are its components.
/// </summary>
internal static class CssRelativeColorResolver
{
    /// <summary>
    /// Computes a relative color; <paramref name="resolveColor"/> resolves the origin color and <paramref name="evaluate"/> the math
    /// functions of the channels. Returns null when the color cannot be computed (e.g. the origin color is currentcolor).
    /// </summary>
    public static CssColor? Compute(CssFunction function, Func<CssComponentValue, CssColor?> resolveColor, Func<CssFunction, CssCalcValue?> evaluate)
    {
        var name = InfraAscii.ToAsciiLowercase(function.Name);
        var values = ValueMatcher.RemoveWhitespace(function.Value);
        if (values.Count < 2 || resolveColor(values[1]) is not { } originColor || CssColorConversion.FromColor(originColor) is not { } origin)
            return null;

        var index = 2;
        string space;
        switch (name)
        {
            case "rgb" or "rgba":
                space = "srgb";
                break;
            case "hsl" or "hsla":
                space = "hsl";
                break;
            case "color":
                if (index >= values.Count || values[index] is not CssPreservedToken { Type: CssTokenType.Ident } spaceToken)
                    return null;

                space = InfraAscii.ToAsciiLowercase(spaceToken.Token.Value);
                space = space == "xyz" ? "xyz-d65" : space;
                index++;
                break;

            // https://drafts.csswg.org/css-color-5/#relative-alpha: the color space of the origin color
            case "alpha":
                space = origin.Space;
                break;
            default:
                space = name;
                break;
        }

        if (!CssColorConversion.IsSupportedSpace(space))
            return null;

        var color = CssColorConversion.Convert(origin, space);
        var keywords = GetChannelKeywords(name, space, color);
        var channelCount = name == "alpha" ? 0 : 3;
        var channels = new double?[3];
        for (var i = 0; i < 3; i++)
        {
            channels[i] = color.Channels[i];
        }

        for (var i = 0; i < channelCount; i++)
        {
            if (index >= values.Count || !TryResolveChannel(values[index], GetChannelScale(name, space, i), keywords, evaluate, out var channel))
                return null;

            // The channels of rgb() are numbers between 0 and 255
            channels[i] = name is "rgb" or "rgba" ? channel / 255 : channel;
            index++;
        }

        // The alpha defaults to the alpha of the origin color
        var alpha = color.Alpha;
        if (index < values.Count)
        {
            if (index + 2 != values.Count || values[index] is not CssPreservedToken { Token: var slash } || !slash.IsDelim('/') || !TryResolveChannel(values[index + 1], 1.0 / 100, keywords, evaluate, out alpha))
                return null;

            if (alpha is { } alphaValue)
            {
                alpha = Math.Clamp(double.IsNaN(alphaValue) ? 0 : alphaValue, 0, 1);
            }
        }

        var hueIndex = CssColorConversion.GetHueIndex(space);
        if (hueIndex >= 0 && channels[hueIndex] is { } hue)
        {
            channels[hueIndex] = NormalizeHue(hue);
        }

        // https://drafts.csswg.org/css-color-4/#specifying-lab-lch: the lightness is clamped to [0, 100] (or [0, 1]) and a negative chroma to zero
        if (space is "lab" or "lch" or "oklab" or "oklch" && channels[0] is { } lightness)
        {
            channels[0] = Math.Clamp(lightness, 0, space.StartsWith("ok", StringComparison.Ordinal) ? 1 : 100);
        }

        if (space is "lch" or "oklch" or "jzczhz" && channels[1] < 0)
        {
            channels[1] = 0;
        }

        return CssColorConversion.ToColor(new CssColorComponents(space, channels, alpha));
    }

    /// <summary>The values of the channel keywords: the components of the origin color in the relative color processing space (the hues in degrees, in [0, 360)).</summary>
    private static Dictionary<string, double?> GetChannelKeywords(string function, string space, CssColorComponents color)
    {
        var names = function switch
        {
            "rgb" or "rgba" => ["r", "g", "b"],
            "hsl" or "hsla" => ["h", "s", "l"],
            "hwb" => ["h", "w", "b"],
            "lab" or "oklab" => ["l", "a", "b"],
            "lch" or "oklch" => ["l", "c", "h"],
            "ictcp" => ["i", "ct", "cp"],
            "jzazbz" => ["j", "a", "b"],
            "jzczhz" => ["j", "c", "h"],
            "color" when space is "xyz-d65" or "xyz-d50" => ["x", "y", "z"],
            "color" => new[] { "r", "g", "b" },
            _ => [],
        };

        var hueIndex = CssColorConversion.GetHueIndex(space);
        var keywords = new Dictionary<string, double?>(StringComparer.Ordinal) { ["alpha"] = color.Alpha };
        for (var i = 0; i < names.Length; i++)
        {
            var value = color.Channels[i];
            if (value is { } v)
            {
                // The channels of rgb() are numbers between 0 and 255
                value = function is "rgb" or "rgba" ? v * 255 : i == hueIndex ? NormalizeHue(v) : v;
            }

            keywords[names[i]] = value;
        }

        return keywords;
    }

    /// <summary>The value of 1% of a channel (the percentage reference ranges of the color functions).</summary>
    private static double GetChannelScale(string function, string space, int channel) => space switch
    {
        "srgb" when function is "rgb" or "rgba" => 255.0 / 100,
        "hsl" or "hwb" => 1,
        "lab" => channel == 0 ? 1 : 125.0 / 100,
        "oklab" => channel == 0 ? 1.0 / 100 : 0.4 / 100,
        "lch" => channel == 0 ? 1 : 150.0 / 100,
        "oklch" => channel == 0 ? 1.0 / 100 : 0.4 / 100,
        "ictcp" or "jzazbz" or "jzczhz" => CssColor.HdrPercentageScale(space, channel),
        _ => 1.0 / 100,
    };

    /// <summary>Resolves a channel: a number, a percentage, an angle (in degrees), none, a channel keyword or a math function.</summary>
    private static bool TryResolveChannel(CssComponentValue value, double percentageScale, Dictionary<string, double?> keywords, Func<CssFunction, CssCalcValue?> evaluate, out double? result)
    {
        result = null;
        switch (value)
        {
            case CssPreservedToken { Token: var token }:
                switch (token.Type)
                {
                    case CssTokenType.Number:
                        result = token.NumericValue;
                        return true;

                    case CssTokenType.Percentage:
                        result = token.NumericValue * percentageScale;
                        return true;

                    case CssTokenType.Dimension:
                        result = ToDegrees(token.NumericValue, InfraAscii.ToAsciiLowercase(token.Value));
                        return result is not null;

                    case CssTokenType.Ident:
                    {
                        var ident = InfraAscii.ToAsciiLowercase(token.Value);
                        if (ident == "none")
                            return true;

                        // A keyword whose component is missing is none
                        return keywords.TryGetValue(ident, out result);
                    }

                    default:
                        return false;
                }

            case CssFunction math when CssCalc.IsMathFunction(math.Name):
            {
                // The channel keywords are numbers (a missing component is zero)
                if (evaluate((CssFunction)SubstituteKeywords(math, keywords)) is not { IsNumeric: true } calc)
                    return false;

                var number = double.IsNaN(calc.Value) ? 0 : calc.Value;
                result = calc.Unit switch
                {
                    "" => number,
                    "%" => number * percentageScale,
                    "deg" => number,
                    _ => null,
                };

                return result is not null;
            }

            default:
                return false;
        }
    }

    private static CssComponentValue SubstituteKeywords(CssComponentValue value, Dictionary<string, double?> keywords)
    {
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Ident } ident when keywords.TryGetValue(InfraAscii.ToAsciiLowercase(ident.Token.Value), out var keyword):
                return new CssPreservedToken(new CssToken { Type = CssTokenType.Number, NumericValue = keyword ?? 0 });

            case CssFunction function:
            {
                var copy = new CssFunction(function.Name);
                foreach (var child in function.Value)
                {
                    copy.Value.Add(SubstituteKeywords(child, keywords));
                }

                return copy;
            }

            case CssSimpleBlock block:
            {
                var copy = new CssSimpleBlock(block.AssociatedToken);
                foreach (var child in block.Value)
                {
                    copy.Value.Add(SubstituteKeywords(child, keywords));
                }

                return copy;
            }

            default:
                return value;
        }
    }

    private static double? ToDegrees(double value, string unit) => unit switch
    {
        "deg" => value,
        "grad" => value * 0.9,
        "rad" => value * 180 / Math.PI,
        "turn" => value * 360,
        _ => null,
    };

    private static double NormalizeHue(double hue)
    {
        if (!double.IsFinite(hue))
            return 0;

        var result = hue % 360;
        return result < 0 ? result + 360 : result;
    }
}
