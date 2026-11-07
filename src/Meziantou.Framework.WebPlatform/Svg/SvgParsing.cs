using System.Globalization;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>Parsing and serialization of the SVG attribute value types (https://svgwg.org/svg2-draft/types.html).</summary>
internal static class SvgParsing
{
    /// <summary>Parses a number (https://www.w3.org/TR/css-syntax-3/#consume-number) at the position, and advances it.</summary>
    public static bool TryParseNumber(ReadOnlySpan<char> value, ref int position, out double result)
    {
        result = 0;
        var start = position;
        var i = position;
        if (i < value.Length && value[i] is '+' or '-')
        {
            i++;
        }

        var digits = 0;
        while (i < value.Length && char.IsAsciiDigit(value[i]))
        {
            i++;
            digits++;
        }

        if (i < value.Length && value[i] == '.' && i + 1 < value.Length && char.IsAsciiDigit(value[i + 1]))
        {
            i++;
            while (i < value.Length && char.IsAsciiDigit(value[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
            return false;

        if (i < value.Length && value[i] is 'e' or 'E')
        {
            var j = i + 1;
            if (j < value.Length && value[j] is '+' or '-')
            {
                j++;
            }

            if (j < value.Length && char.IsAsciiDigit(value[j]))
            {
                while (j < value.Length && char.IsAsciiDigit(value[j]))
                {
                    j++;
                }

                i = j;
            }
        }

        if (!double.TryParse(value[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return false;

        position = i;
        return true;
    }

    public static void SkipWhitespace(ReadOnlySpan<char> value, ref int position)
    {
        while (position < value.Length && InfraAscii.IsAsciiWhitespace(value[position]))
        {
            position++;
        }
    }

    /// <summary>Skips a comma-wsp (https://svgwg.org/svg2-draft/types.html#CommaWsp); returns whether a comma was found.</summary>
    public static bool SkipCommaWhitespace(ReadOnlySpan<char> value, ref int position)
    {
        SkipWhitespace(value, ref position);
        if (position < value.Length && value[position] == ',')
        {
            position++;
            SkipWhitespace(value, ref position);
            return true;
        }

        return false;
    }

    /// <summary>Parses a number surrounded by optional whitespace.</summary>
    public static bool TryParseSingleNumber(string? value, out float result)
    {
        result = 0;
        if (value is null)
            return false;

        var position = 0;
        SkipWhitespace(value, ref position);
        if (!TryParseNumber(value, ref position, out var number))
            return false;

        SkipWhitespace(value, ref position);
        if (position != value.Length || !float.IsFinite((float)number))
            return false;

        result = (float)number;
        return true;
    }

    /// <summary>Parses a list of numbers separated by comma-wsp (e.g. "1 2" or "1,2").</summary>
    public static bool TryParseNumberList(string? value, out List<float> result)
    {
        result = [];
        if (value is null)
            return false;

        var position = 0;
        SkipWhitespace(value, ref position);
        while (position < value.Length)
        {
            if (!TryParseNumber(value, ref position, out var number) || !float.IsFinite((float)number))
                return false;

            result.Add((float)number);
            var comma = SkipCommaWhitespace(value, ref position);
            if (comma && position >= value.Length)
                return false;
        }

        return true;
    }

    /// <summary>Serializes a float value like the browsers (the shortest representation of the float, formatted as a JavaScript number).</summary>
    public static string FormatNumber(float value)
    {
        var shortest = double.Parse(value.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return Html.Microsyntaxes.ToBestFloatingPointRepresentation(shortest);
    }

    public static string FormatNumber(double value) => Html.Microsyntaxes.ToBestFloatingPointRepresentation(value);

    /// <summary>
    /// Splits a list on whitespace, or on commas: a set of comma-separated tokens (https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#set-of-comma-separated-tokens),
    /// whose tokens are stripped of their leading and trailing ASCII whitespace and can be empty (an empty string is one empty token).
    /// </summary>
    public static List<string> SplitList(string value, bool commaSeparated)
    {
        var result = new List<string>();
        foreach (var range in value.AsSpan().SplitAny(commaSeparated ? "," : "\t\n\f\r "))
        {
            var item = value[range].Trim(" \t\n\f\r".ToCharArray());
            if (item.Length > 0 || commaSeparated)
            {
                result.Add(item);
            }
        }

        return result;
    }
}
