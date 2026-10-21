using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>The exponent of the notations (ComputeExponent, ComputeExponentForMagnitude) and the compact patterns of CLDR.</summary>
internal static class Notation
{
    /// <summary>The largest magnitude with a compact pattern in CLDR (10^14).</summary>
    private const int MaxCompactMagnitude = 14;

    /// <summary>
    /// Gets the compact pattern of a magnitude (e.g. "0K" for 1000, "00 thousand" for 10000), falling back to the "other"
    /// count, and to the short style; null when the locale has no compact pattern for it (the number is not scaled).
    /// </summary>
    public static string? GetCompactPattern(CldrLocale locale, string numberingSystem, string compactDisplay, bool currency, int magnitude, string count)
    {
        if (magnitude < 3)
            return null;

        magnitude = Math.Min(magnitude, MaxCompactMagnitude);
        var type = "1" + new string('0', magnitude);
        string? pattern;
        if (currency)
        {
            pattern = Get("currencyFormats", "short/standard/" + type + "-count-" + count) ?? Get("currencyFormats", "short/standard/" + type + "-count-other");
        }
        else
        {
            pattern = Get("decimalFormats", compactDisplay + "/decimalFormat/" + type + "-count-" + count)
                ?? Get("decimalFormats", compactDisplay + "/decimalFormat/" + type + "-count-other")
                ?? Get("decimalFormats", "short/decimalFormat/" + type + "-count-" + count)
                ?? Get("decimalFormats", "short/decimalFormat/" + type + "-count-other");
        }

        return pattern;

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
                var pattern = GetCompactPattern(locale, numberingSystem, compactDisplay, currency, magnitude, "other");
                if (pattern is null or "0")
                    return 0;

                var zeros = 0;
                foreach (var c in pattern)
                {
                    if (c == '0')
                    {
                        zeros++;
                    }
                }

                if (zeros == 0)
                    return 0;

                return Math.Min(magnitude, MaxCompactMagnitude) - zeros + 1;
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
