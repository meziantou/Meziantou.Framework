namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>The unit of an entry of a list of dimensions (https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#rules-for-parsing-a-list-of-dimensions).</summary>
internal enum DimensionUnit
{
    Absolute,
    Percentage,
    Relative,
}

internal static partial class Microsyntaxes
{
    /// <summary>
    /// https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#rules-for-parsing-a-list-of-dimensions: the number/unit
    /// pairs of a comma-separated list (the <c>rows</c> and <c>cols</c> attributes of a <c>frameset</c>). The fraction step skips
    /// the U+002E (.) before collecting the digits (the specification collects them from the dot, which would never match).
    /// </summary>
    public static List<(double Value, DimensionUnit Unit)> ParseListOfDimensions(string rawInput)
    {
        var result = new List<(double Value, DimensionUnit Unit)>();
        var input = rawInput.AsSpan();
        if (input.Length > 0 && input[^1] == ',')
        {
            input = input[..^1];
        }

        if (input.IsEmpty)
            return result;

        // (split a string on commas: the tokens are stripped of their leading and trailing ASCII whitespace)
        foreach (var range in input.Split(','))
        {
            var token = Infra.InfraAscii.StripLeadingAndTrailingAsciiWhitespace(input[range]);
            var position = 0;
            var value = 0d;
            var unit = DimensionUnit.Absolute;
            if (token.IsEmpty)
            {
                result.Add((0, DimensionUnit.Relative));
                continue;
            }

            if (char.IsAsciiDigit(token[position]))
            {
                var start = position;
                while (position < token.Length && char.IsAsciiDigit(token[position]))
                {
                    position++;
                }

                // (a number too large for a double is the largest one)
                value += Math.Min(double.MaxValue, double.Parse(token[start..position], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture));
            }

            if (position < token.Length && token[position] == '.')
            {
                position++;
                var fraction = 0d;
                var scale = 1d;
                while (position < token.Length && (char.IsAsciiDigit(token[position]) || IsAsciiWhitespace(token[position])))
                {
                    if (char.IsAsciiDigit(token[position]))
                    {
                        scale /= 10;
                        fraction += (token[position] - '0') * scale;
                    }

                    position++;
                }

                value += fraction;
            }

            while (position < token.Length && IsAsciiWhitespace(token[position]))
            {
                position++;
            }

            if (position < token.Length)
            {
                if (token[position] == '%')
                {
                    unit = DimensionUnit.Percentage;
                }
                else if (token[position] == '*')
                {
                    unit = DimensionUnit.Relative;
                }
            }

            result.Add((value, unit));
        }

        return result;
    }
}
