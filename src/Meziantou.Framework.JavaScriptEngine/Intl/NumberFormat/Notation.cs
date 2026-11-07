using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>The exponent of the notations (ComputeExponent, ComputeExponentForMagnitude) and the compact patterns of CLDR.</summary>
internal static class Notation
{
    /// <summary>The largest magnitude to look for a compact pattern (CLDR has patterns up to 10^14, 10^19 for the East Asian languages).</summary>
    private const int MaxCompactMagnitude = 24;

    /// <summary>
    /// Gets the compact pattern of a magnitude (e.g. "0K" for 1000, "00 thousand" for 10000) and the magnitude of its
    /// type (the largest one of the data for a larger magnitude), for a plural category falling back to "other", and to
    /// the short style; null when the locale has no compact pattern for it. With <paramref name="exactCount"/>, the
    /// pattern of an explicit count ("1") of the magnitude, or null.
    /// </summary>
    public static (string? Pattern, int TypeMagnitude) GetCompactPattern(CldrLocale locale, string numberingSystem, string compactDisplay, bool currency, int magnitude, string count, bool exactCount = false)
    {
        if (magnitude < 3)
            return (null, magnitude);

        for (var m = Math.Min(magnitude, MaxCompactMagnitude); m >= 3; m--)
        {
            var type = "1" + new string('0', m);
            string? pattern;
            var other = exactCount ? count : "other";
            if (currency)
            {
                pattern = Get("currencyFormats", "short/standard/" + type + "-count-" + count) ?? Get("currencyFormats", "short/standard/" + type + "-count-" + other);
            }
            else
            {
                pattern = Get("decimalFormats", compactDisplay + "/decimalFormat/" + type + "-count-" + count)
                    ?? Get("decimalFormats", compactDisplay + "/decimalFormat/" + type + "-count-" + other)
                    ?? Get("decimalFormats", "short/decimalFormat/" + type + "-count-" + count)
                    ?? Get("decimalFormats", "short/decimalFormat/" + type + "-count-" + other);
            }

            // The patterns cover the magnitudes from 3 to the largest one of the data
            if (pattern is not null || exactCount)
                return (pattern, m);
        }

        return (null, magnitude);

        string? Get(string group, string path) => locale.GetNumberPattern(numberingSystem, group, path);
    }

    /// <summary>ComputeExponentForMagnitude(numberFormat, magnitude)</summary>
    public static int GetExponentForMagnitude(string notation, CldrLocale locale, string numberingSystem, string compactDisplay, bool currency, int magnitude)
    {
        switch (notation)
        {
            case "scientific":
                return magnitude;
            case "engineering":
                return (int)Math.Floor(magnitude / 3.0) * 3;
            case "compact":
                var (pattern, typeMagnitude) = GetCompactPattern(locale, numberingSystem, compactDisplay, currency, magnitude, "other");
                if (pattern is null or "0")
                    return 0;

                // The zeros of the positive subpattern ("0K;-0K")
                var zeros = 0;
                var separator = pattern.IndexOf(';', StringComparison.Ordinal);
                foreach (var c in separator < 0 ? pattern : pattern[..separator])
                {
                    if (c == '0')
                    {
                        zeros++;
                    }
                }

                if (zeros == 0)
                    return 0;

                return typeMagnitude - zeros + 1;
            default:
                return 0;
        }
    }

    /// <summary>ComputeExponent(numberFormat, x): x must be finite.</summary>
    public static (int Exponent, int Magnitude) ComputeExponent(DigitOptions digits, string notation, CldrLocale locale, string numberingSystem, string compactDisplay, bool currency, IntlDecimal x)
    {
        if (x.IsZero || notation is "standard")
            return (0, 0);

        var magnitude = x.Magnitude;
        var exponent = GetExponentForMagnitude(notation, locale, numberingSystem, compactDisplay, currency, magnitude);
        var formatted = digits.FormatNumericToString(x.ScaleByPowerOfTen(-exponent));
        if (formatted.RoundedNumber.IsZero)
            return (exponent, magnitude);

        var newMagnitude = formatted.RoundedNumber.Magnitude;
        if (newMagnitude == magnitude - exponent)
            return (exponent, magnitude);

        return (GetExponentForMagnitude(notation, locale, numberingSystem, compactDisplay, currency, magnitude + 1), magnitude + 1);
    }
}
