using System.Globalization;
using System.Numerics;
using System.Text;
using Meziantou.Framework.Mermaid.Internals.Charts;

namespace Meziantou.Framework.Mermaid.Internals.TreemapDiagrams;

/// <summary>
/// Formats the values of a treemap as Mermaid does: with the <c>format</c> function of d3-format 3 in the en-US locale (a
/// port of it, see THIRD-PARTY-NOTICES.md), and the special cases of Mermaid for the formats that start with <c>$</c>.
/// </summary>
internal sealed class TreemapValueFormatter
{
    private const string Minus = "\u2212";
    private static readonly string[] Prefixes = ["y", "z", "a", "f", "p", "n", "\u00B5", "m", "", "k", "M", "G", "T", "P", "E", "Z", "Y"];

    private readonly string _outerPrefix;
    private readonly char _fill;
    private readonly char _align;
    private readonly char _sign;
    private readonly bool _zero;
    private readonly int? _width;
    private readonly bool _comma;
    private readonly int _precision;
    private readonly bool _trim;
    private readonly char _type;
    private readonly string _prefix;
    private readonly string _suffix;
    private readonly bool _maybeSuffix;

    private TreemapValueFormatter(string outerPrefix, Specifier specifier)
    {
        _outerPrefix = outerPrefix;
        _fill = specifier.Fill;
        _align = specifier.Align;
        _sign = specifier.Sign;
        _zero = specifier.Zero;
        _width = specifier.Width;
        _comma = specifier.Comma;
        _trim = specifier.Trim;
        _type = specifier.Type;
        var precision = specifier.Precision;

        // The "n" type is an alias for ",g"; the "" type, and any invalid type, is an alias for ".12~g".
        if (_type == 'n')
        {
            _comma = true;
            _type = 'g';
        }
        else if (!IsKnownType(_type))
        {
            precision ??= 12;
            _trim = true;
            _type = 'g';
        }

        // If zero fill is specified, padding goes after sign and before digits.
        if (_zero || (_fill == '0' && _align == '='))
        {
            _zero = true;
            _fill = '0';
            _align = '=';
        }

        _prefix = specifier.Symbol == '$' ? "$" : specifier.Symbol == '#' && _type is 'b' or 'o' or 'x' or 'X' ? "0" + char.ToLowerInvariant(_type) : "";
        _suffix = specifier.Symbol == '$' ? "" : _type is '%' or 'p' ? "%" : "";
        _maybeSuffix = _type is 'd' or 'e' or 'f' or 'g' or 'p' or 'r' or 's' or '%';
        _precision = precision is null ? 6 : _type is 'g' or 'p' or 'r' or 's' ? Math.Clamp(precision.Value, 1, 21) : Math.Clamp(precision.Value, 0, 20);
    }

    /// <summary>Creates the formatter of a <c>valueFormat</c>, with the special cases of Mermaid; an invalid format gives the format <c>,</c>.</summary>
    public static TreemapValueFormatter Create(string format)
    {
        var text = format.Length is 0 ? "," : format;
        if (string.Equals(text, "$0,0", StringComparison.Ordinal))
            return Create("$", ",");

        if (text.StartsWith('$', StringComparison.Ordinal) && text.Contains(',', StringComparison.Ordinal))
        {
            // Mermaid keeps the precision only: "$,.2f" is "$" and ",.2"
            var precision = GetPrecision(text);
            return Create("$", "," + precision);
        }

        if (text.StartsWith('$', StringComparison.Ordinal))
            return Create("$", text[1..]);

        return Create(string.Empty, text);
    }

    public string Format(double value)
    {
        var valuePrefix = _prefix;
        var valueSuffix = _suffix;
        if (_type == 'c')
            return _outerPrefix + Align(valuePrefix, string.Empty, JavaScriptNumber.ToString(value) + valueSuffix);

        var negative = value < 0 || (value == 0 && double.IsNegative(value));
        int? prefixExponent = null;
        var number = double.IsNaN(value) ? "NaN" : FormatType(Math.Abs(value), out prefixExponent);
        if (_trim)
        {
            number = Trim(number);
        }

        // If a negative value rounds to zero after formatting, and no explicit positive sign is requested, hide the sign.
        if (negative && IsZero(number) && _sign != '+')
        {
            negative = false;
        }

        valuePrefix = (negative ? (_sign == '(' ? "(" : Minus) : _sign is '-' or '(' ? "" : _sign.ToString()) + valuePrefix;
        valueSuffix = (_type == 's' && !double.IsNaN(value) && prefixExponent is { } exponent ? Prefixes[8 + (exponent / 3)] : "") + valueSuffix + (negative && _sign == '(' ? ")" : "");

        // Break the formatted value into the integer part that can be grouped, and the fractional or exponential part that is not.
        if (_maybeSuffix)
        {
            for (var i = 0; i < number.Length; i++)
            {
                var c = number[i];
                if (c is < '0' or > '9')
                {
                    valueSuffix = (c == '.' ? "." + number[(i + 1)..] : number[i..]) + valueSuffix;
                    number = number[..i];
                    break;
                }
            }
        }

        // If the fill character is not "0", grouping is applied before padding.
        if (_comma && !_zero)
        {
            number = Group(number, int.MaxValue);
        }

        return _outerPrefix + Align(valuePrefix, number, valueSuffix);
    }

    private string Align(string valuePrefix, string number, string valueSuffix)
    {
        var length = valuePrefix.Length + number.Length + valueSuffix.Length;
        var padding = _width is { } width && length < width ? new string(_fill, width - length) : "";

        // If the fill character is "0", grouping is applied after padding.
        if (_comma && _zero)
        {
            number = Group(padding + number, padding.Length > 0 ? _width!.Value - valueSuffix.Length : int.MaxValue);
            padding = "";
        }

        return _align switch
        {
            '<' => valuePrefix + number + valueSuffix + padding,
            '=' => valuePrefix + padding + number + valueSuffix,
            '^' => padding[..(padding.Length >> 1)] + valuePrefix + number + valueSuffix + padding[(padding.Length >> 1)..],
            _ => padding + valuePrefix + number + valueSuffix,
        };
    }

    private static TreemapValueFormatter Create(string outerPrefix, string specifier)
    {
        // Mermaid falls back to the format "," when d3-format rejects the specifier
        if (TryParseSpecifier(specifier, out var parsed))
            return new TreemapValueFormatter(outerPrefix, parsed);

        return new TreemapValueFormatter(string.Empty, new Specifier { Comma = true });
    }

    private static string GetPrecision(string text)
    {
        // /\.\d+/.exec(formatStr)
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] == '.' && char.IsAsciiDigit(text[i + 1]))
            {
                var end = i + 1;
                while (end < text.Length && char.IsAsciiDigit(text[end]))
                {
                    end++;
                }

                return text[i..end];
            }
        }

        return "";
    }

    private static bool IsKnownType(char type) => type is '%' or 'b' or 'c' or 'd' or 'e' or 'f' or 'g' or 'o' or 'p' or 'r' or 's' or 'X' or 'x';

    private static bool IsZero(string number) => double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed == 0;

    private string FormatType(double value, out int? prefixExponent)
    {
        prefixExponent = null;
        switch (_type)
        {
            case '%':
                return ToFixed(value * 100, _precision);
            case 'b':
                return ToRadix(value, 2);
            case 'c':
                return JavaScriptNumber.ToString(value);
            case 'd':
                return FormatDecimal(value);
            case 'e':
                return ToExponential(value, _precision);
            case 'f':
                return ToFixed(value, _precision);
            case 'g':
                return ToPrecision(value, _precision);
            case 'o':
                return ToRadix(value, 8);
            case 'p':
                return FormatRounded(value * 100, _precision);
            case 'r':
                return FormatRounded(value, _precision);
            case 's':
                return FormatPrefixAuto(value, _precision, out prefixExponent);
            case 'X':
                return ToRadix(value, 16).ToUpperInvariant();
            case 'x':
                return ToRadix(value, 16);
            default:
                return JavaScriptNumber.ToString(value);
        }
    }

    private static string FormatDecimal(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return Math.Abs(rounded) >= 1e21 ? ToFixed(rounded, 0, allowExponent: false) : JavaScriptNumber.ToString(rounded);
    }

    private static string FormatRounded(double value, int precision)
    {
        if (!TryGetDecimalParts(value, precision, out var coefficient, out var exponent))
            return JavaScriptNumber.ToString(value);

        if (exponent < 0)
            return "0." + new string('0', -exponent - 1) + coefficient;

        if (coefficient.Length > exponent + 1)
            return coefficient[..(exponent + 1)] + "." + coefficient[(exponent + 1)..];

        return coefficient + new string('0', exponent - coefficient.Length + 1);
    }

    private static string FormatPrefixAuto(double value, int precision, out int? prefixExponent)
    {
        prefixExponent = null;
        if (!TryGetDecimalParts(value, precision, out var coefficient, out var exponent))
            return JavaScriptNumber.ToString(value);

        var prefix = Math.Clamp((int)Math.Floor(exponent / 3.0), -8, 8);
        prefixExponent = prefix * 3;
        var i = exponent - (prefix * 3) + 1;
        var n = coefficient.Length;
        if (i == n)
            return coefficient;

        if (i > n)
            return coefficient + new string('0', i - n);

        if (i > 0)
            return coefficient[..i] + "." + coefficient[i..];

        TryGetDecimalParts(value, Math.Max(0, precision + i - 1), out var smaller, out _);
        return "0." + new string('0', -i) + smaller;
    }

    /// <summary>formatDecimalParts: the significant digits and the exponent of x.toExponential(p - 1), or of x.toExponential() when p is 0.</summary>
    private static bool TryGetDecimalParts(double value, int precision, out string coefficient, out int exponent)
    {
        coefficient = "";
        exponent = 0;
        if (!double.IsFinite(value))
            return false;

        var text = precision > 0 ? ToExponential(value, precision - 1) : JavaScriptNumber.ToExponential(value);
        var e = text.IndexOf('e', StringComparison.Ordinal);
        var mantissa = text[..e];
        coefficient = mantissa.Length > 1 ? mantissa[0] + mantissa[2..] : mantissa;
        exponent = int.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Trims insignificant zeros, e.g., replaces 1.2000k with 1.2k.</summary>
    private static string Trim(string s)
    {
        var i0 = -1;
        var i1 = 0;
        for (var i = 1; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '.':
                    i0 = i1 = i;
                    break;
                case '0':
                    if (i0 == 0)
                    {
                        i0 = i;
                    }

                    i1 = i;
                    break;
                default:
                    if (s[i] is < '1' or > '9')
                        goto End;

                    if (i0 > 0)
                    {
                        i0 = 0;
                    }

                    break;
            }
        }

    End:
        return i0 > 0 ? s[..i0] + s[(i1 + 1)..] : s;
    }

    /// <summary>Groups the digits by three with commas, up to a width.</summary>
    private static string Group(string value, int width)
    {
        var i = value.Length;
        var parts = new List<string>();
        var length = 0;
        const int Grouping = 3;
        while (i > 0)
        {
            var g = Grouping;
            if (length + g + 1 > width)
            {
                g = Math.Max(1, width - length);
            }

            var start = Math.Max(0, i - g);
            parts.Add(value[start..i]);
            i -= g;
            length += g + 1;
            if (length > width)
                break;
        }

        parts.Reverse();
        return string.Join(',', parts);
    }

    /// <summary>The <c>toFixed</c> of JavaScript: the exact value rounded to a number of decimals, a tie rounded up.</summary>
    private static string ToFixed(double value, int digits, bool allowExponent = true)
    {
        // JavaScript writes the numbers from 1e21 with toString
        if (allowExponent && Math.Abs(value) >= 1e21)
            return JavaScriptNumber.ToString(value);

        var (mantissa, exponent) = Decompose(Math.Abs(value));
        var scaled = mantissa * BigInteger.Pow(10, digits);
        BigInteger integer;
        if (exponent >= 0)
        {
            integer = scaled << exponent;
        }
        else
        {
            var denominator = BigInteger.One << -exponent;
            integer = BigInteger.DivRem(scaled, denominator, out var remainder);
            if (remainder * 2 >= denominator)
            {
                integer++;
            }
        }

        var text = integer.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
        var result = digits > 0 ? text[..^digits] + "." + text[^digits..] : text;
        return value < 0 ? "-" + result : result;
    }

    /// <summary>The <c>toExponential</c> of JavaScript: the exact value rounded to a number of digits after the point, a tie rounded up.</summary>
    private static string ToExponential(double value, int digits)
    {
        if (value == 0)
            return digits > 0 ? "0." + new string('0', digits) + "e+0" : "0e+0";

        // The exact decimal digits of the value, and the position of the point: the value is 0.digits × 10^point
        var (mantissa, exponent) = Decompose(Math.Abs(value));
        string all;
        int point;
        if (exponent >= 0)
        {
            all = (mantissa << exponent).ToString(CultureInfo.InvariantCulture);
            point = all.Length;
        }
        else
        {
            all = (mantissa * BigInteger.Pow(5, -exponent)).ToString(CultureInfo.InvariantCulture);
            point = all.Length + exponent;
        }

        var significant = digits + 1;
        string kept;
        if (all.Length <= significant)
        {
            kept = all.PadRight(significant, '0');
        }
        else
        {
            var rounded = BigInteger.Parse(all.AsSpan(0, significant), NumberStyles.None, CultureInfo.InvariantCulture);
            if (all[significant] >= '5')
            {
                rounded++;
            }

            kept = rounded.ToString(CultureInfo.InvariantCulture);
            if (kept.Length > significant)
            {
                kept = kept[..significant];
                point++;
            }
        }

        var e = point - 1;
        var builder = new StringBuilder();
        if (value < 0)
        {
            builder.Append('-');
        }

        builder.Append(kept[0]);
        if (digits > 0)
        {
            builder.Append('.').Append(kept, 1, digits);
        }

        return builder.Append('e').Append(e < 0 ? '-' : '+').Append(Math.Abs(e).ToString(CultureInfo.InvariantCulture)).ToString();
    }

    /// <summary>Returns m and e such that a positive finite value is m × 2^e.</summary>
    private static (BigInteger Mantissa, int Exponent) Decompose(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0)
            return (mantissa, -1074);

        return (mantissa | (1L << 52), exponent - 1075);
    }

    private static string ToPrecision(double value, int precision)
    {
        if (value == 0)
            return precision > 1 ? "0." + new string('0', precision - 1) : "0";

        var exponential = ToExponential(value, precision - 1);
        var e = exponential.IndexOf('e', StringComparison.Ordinal);
        var exponent = int.Parse(exponential.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (exponent < -6 || exponent >= precision)
            return exponential;

        var digits = exponential[..e].Replace(".", "", StringComparison.Ordinal);
        if (exponent >= 0)
        {
            return exponent + 1 >= digits.Length ? digits : digits[..(exponent + 1)] + "." + digits[(exponent + 1)..];
        }

        return "0." + new string('0', -exponent - 1) + digits;
    }

    private static string ToRadix(double value, int radix)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded >= 9007199254740992)
            return JavaScriptNumber.ToString(rounded);

        var integer = (long)rounded;
        if (integer == 0)
            return "0";

        var builder = new StringBuilder();
        while (integer > 0)
        {
            builder.Insert(0, "0123456789abcdef"[(int)(integer % radix)]);
            integer /= radix;
        }

        return builder.ToString();
    }

    private static bool TryParseSpecifier(string specifier, out Specifier result)
    {
        // /^(?:(.)?([<>=^]))?([+\-( ])?([$#])?(0)?(\d+)?(,)?(\.\d+)?(~)?([a-z%])?$/i
        result = new Specifier();
        var i = 0;
        var s = specifier;
        if (s.Length >= 2 && s[1] is '<' or '>' or '=' or '^')
        {
            result.Fill = s[0];
            result.Align = s[1];
            i = 2;
        }
        else if (s.Length >= 1 && s[0] is '<' or '>' or '=' or '^')
        {
            result.Align = s[0];
            i = 1;
        }

        if (i < s.Length && s[i] is '+' or '-' or '(' or ' ')
        {
            result.Sign = s[i++];
        }

        if (i < s.Length && s[i] is '$' or '#')
        {
            result.Symbol = s[i++];
        }

        if (i < s.Length && s[i] == '0')
        {
            result.Zero = true;
            i++;
        }

        var widthStart = i;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            i++;
        }

        if (i > widthStart)
        {
            result.Width = int.TryParse(s.AsSpan(widthStart, i - widthStart), NumberStyles.None, CultureInfo.InvariantCulture, out var width) ? Math.Min(width, 10_000) : 10_000;
        }

        if (i < s.Length && s[i] == ',')
        {
            result.Comma = true;
            i++;
        }

        if (i < s.Length && s[i] == '.')
        {
            var precisionStart = ++i;
            while (i < s.Length && char.IsAsciiDigit(s[i]))
            {
                i++;
            }

            if (i == precisionStart)
                return false;

            result.Precision = int.TryParse(s.AsSpan(precisionStart, i - precisionStart), NumberStyles.None, CultureInfo.InvariantCulture, out var precision) ? precision : int.MaxValue;
        }

        if (i < s.Length && s[i] == '~')
        {
            result.Trim = true;
            i++;
        }

        if (i < s.Length && (char.IsAsciiLetter(s[i]) || s[i] == '%'))
        {
            result.Type = s[i++];
        }

        return i == s.Length;
    }

    private struct Specifier()
    {
        public char Fill { get; set; } = ' ';

        public char Align { get; set; } = '>';

        public char Sign { get; set; } = '-';

        public char Symbol { get; set; }

        public bool Zero { get; set; }

        public int? Width { get; set; }

        public bool Comma { get; set; }

        public int? Precision { get; set; }

        public bool Trim { get; set; }

        public char Type { get; set; }
    }
}
