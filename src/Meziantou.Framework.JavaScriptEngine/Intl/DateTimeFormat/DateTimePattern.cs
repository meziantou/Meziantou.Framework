using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>A part of a date pattern of UTS 35: a field (a pattern letter repeated <see cref="Count"/> times), or a literal text.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DateTimePatternPart(char Symbol, int Count, string? Literal)
{
    public bool IsLiteral => Literal is not null;

    public static DateTimePatternPart CreateLiteral(string value) => new('\0', 0, value);

    public static DateTimePatternPart CreateField(char symbol, int count) => new(symbol, count, Literal: null);
}

/// <summary>The date patterns of UTS 35 (https://unicode.org/reports/tr35/tr35-dates.html#Date_Format_Patterns).</summary>
internal static class DateTimePattern
{
    /// <summary>Gets whether a character is a pattern letter (an ASCII letter outside the quotes).</summary>
    public static bool IsPatternLetter(char c) => char.IsAsciiLetter(c);

    /// <summary>Parses a pattern into its fields and its literals (the quotes are removed; "''" is a quote).</summary>
    public static DateTimePatternPart[] Parse(string pattern)
    {
        var result = new List<DateTimePatternPart>();
        var literal = new StringBuilder();
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c == '\'')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                {
                    literal.Append('\'');
                    i += 2;
                    continue;
                }

                // A quoted literal, where "''" is a quote
                i++;
                while (i < pattern.Length)
                {
                    if (pattern[i] == '\'')
                    {
                        if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                        {
                            literal.Append('\'');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    literal.Append(pattern[i]);
                    i++;
                }

                continue;
            }

            if (IsPatternLetter(c))
            {
                if (literal.Length > 0)
                {
                    result.Add(DateTimePatternPart.CreateLiteral(literal.ToString()));
                    literal.Clear();
                }

                var start = i;
                while (i < pattern.Length && pattern[i] == c)
                {
                    i++;
                }

                result.Add(DateTimePatternPart.CreateField(c, i - start));
                continue;
            }

            literal.Append(c);
            i++;
        }

        if (literal.Length > 0)
        {
            result.Add(DateTimePatternPart.CreateLiteral(literal.ToString()));
        }

        return [.. result];
    }

    /// <summary>Formats parts as a pattern (the inverse of <see cref="Parse"/>).</summary>
    public static string ToPattern(IEnumerable<DateTimePatternPart> parts)
    {
        var result = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.IsLiteral)
            {
                AppendLiteral(result, part.Literal!);
            }
            else
            {
                result.Append(part.Symbol, part.Count);
            }
        }

        return result.ToString();
    }

    /// <summary>Appends a literal text to a pattern, quoting the letters and the quotes.</summary>
    public static void AppendLiteral(StringBuilder result, string literal)
    {
        var quoted = false;
        foreach (var c in literal)
        {
            if (c == '\'')
            {
                if (quoted)
                {
                    result.Append('\'');
                    quoted = false;
                }

                result.Append("''");
            }
            else if (IsPatternLetter(c))
            {
                if (!quoted)
                {
                    result.Append('\'');
                    quoted = true;
                }

                result.Append(c);
            }
            else
            {
                if (quoted)
                {
                    result.Append('\'');
                    quoted = false;
                }

                result.Append(c);
            }
        }

        if (quoted)
        {
            result.Append('\'');
        }
    }

    /// <summary>
    /// Replaces the hour letters (h, H, k, K) of a pattern by the letter of an hour cycle (V8's ReplaceHourCycleInPattern),
    /// so that every pattern of a DateTimeFormat uses its [[HourCycle]].
    /// </summary>
    public static string ReplaceHourCycle(string pattern, string? hourCycle)
    {
        if (hourCycle is null)
            return pattern;

        var replacement = GetHourSymbol(hourCycle);
        var result = new StringBuilder(pattern.Length);
        var inQuote = false;
        foreach (var c in pattern)
        {
            if (c == '\'')
            {
                inQuote = !inQuote;
                result.Append(c);
            }
            else if (!inQuote && c is 'h' or 'H' or 'k' or 'K')
            {
                result.Append(replacement);
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>Gets the pattern letter of an hour cycle (h11 = K, h12 = h, h23 = H, h24 = k).</summary>
    public static char GetHourSymbol(string hourCycle) => hourCycle switch
    {
        "h11" => 'K',
        "h12" => 'h',
        "h24" => 'k',
        _ => 'H',
    };

    /// <summary>Gets the hour cycle of a pattern letter, or null when it is not an hour letter.</summary>
    public static string? GetHourCycle(char symbol) => symbol switch
    {
        'K' => "h11",
        'h' => "h12",
        'H' => "h23",
        'k' => "h24",
        _ => null,
    };

    /// <summary>Gets the hour cycle of the first hour field of a pattern, or null.</summary>
    public static string? GetHourCycleOfPattern(string pattern)
    {
        foreach (var part in Parse(pattern))
        {
            if (!part.IsLiteral && GetHourCycle(part.Symbol) is { } hourCycle)
                return hourCycle;
        }

        return null;
    }

    /// <summary>Gets the type of the part of formatToParts of a pattern field (an ECMA-402 part type), or null for an unknown field.</summary>
    public static string? GetPartType(char symbol) => symbol switch
    {
        'G' => "era",
        'y' or 'Y' or 'u' => "year",
        'r' => "relatedYear",
        'U' => "yearName",
        'M' or 'L' => "month",
        'd' => "day",
        'E' or 'c' or 'e' => "weekday",
        'a' or 'b' or 'B' => "dayPeriod",
        'h' or 'H' or 'k' or 'K' => "hour",
        'm' => "minute",
        's' => "second",
        'S' => "fractionalSecond",
        'z' or 'Z' or 'O' or 'v' or 'V' or 'X' or 'x' => "timeZoneName",
        _ => null,
    };
}
