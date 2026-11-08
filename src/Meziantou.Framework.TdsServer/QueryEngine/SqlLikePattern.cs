using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Evaluates the patterns of the SQL <c>LIKE</c> operator.</summary>
/// <remarks>
/// <c>%</c> matches any string, <c>_</c> any character, <c>[abc]</c> and <c>[a-c]</c> one character of the set, and
/// <c>[^abc]</c> one character outside of it. The escape character makes the next character a literal. Characters are
/// compared ordinally, as the <c>=</c> operator of the engine does.
/// </remarks>
internal static class SqlLikePattern
{
    // NonBacktracking matches in linear time, so a pattern sent by a client cannot make the match exponential
    private const RegexOptions PatternRegexOptions = RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.NonBacktracking;

    /// <summary>Returns whether <paramref name="value"/> matches <paramref name="pattern"/>, or <see langword="null"/> when either is NULL.</summary>
    public static bool? Like(string? value, string? pattern, string? escape)
    {
        if (value is null || pattern is null)
        {
            return null;
        }

        var escapeCharacter = GetEscapeCharacter(escape);
        if (TryGetSimplePattern(pattern, escapeCharacter, out var kind, out var text))
        {
            return kind switch
            {
                SimplePatternKind.Equals => string.Equals(value, text, StringComparison.Ordinal),
                SimplePatternKind.StartsWith => value.StartsWith(text, StringComparison.Ordinal),
                SimplePatternKind.EndsWith => value.EndsWith(text, StringComparison.Ordinal),
                _ => value.Contains(text, StringComparison.Ordinal),
            };
        }

        // Regex keeps a cache of the patterns it parsed, so a constant pattern is not parsed for every row
        return Regex.IsMatch(value, ToRegexPattern(pattern, escapeCharacter), PatternRegexOptions);
    }

    /// <summary>Gets the escape character of an <c>ESCAPE</c> clause.</summary>
    public static char? GetEscapeCharacter(string? escape)
    {
        return escape switch
        {
            null => null,
            { Length: 1 } => escape[0],
            _ => throw new TdsQueryEngineException("The ESCAPE clause of LIKE must be a single character."),
        };
    }

    /// <summary>
    /// Returns whether the pattern only contains literal text with an optional <c>%</c> at its start and its end,
    /// which a LINQ provider can run as <c>==</c>, <c>StartsWith</c>, <c>EndsWith</c> or <c>Contains</c>.
    /// </summary>
    public static bool TryGetSimplePattern(string pattern, char? escape, out SimplePatternKind kind, out string text)
    {
        kind = default;
        text = string.Empty;
        if (escape is '%' or '_' or '[')
        {
            return false;
        }

        var leadingWildcard = pattern.Length > 0 && pattern[0] == '%';
        var trailingWildcard = false;
        var builder = new StringBuilder(pattern.Length);
        for (var i = leadingWildcard ? 1 : 0; i < pattern.Length; i++)
        {
            var current = pattern[i];
            if (current == escape)
            {
                if (i + 1 >= pattern.Length)
                {
                    return false;
                }

                builder.Append(pattern[++i]);
                continue;
            }

            if (current == '%' && i == pattern.Length - 1)
            {
                trailingWildcard = true;
                break;
            }

            if (current is '%' or '_' or '[')
            {
                return false;
            }

            builder.Append(current);
        }

        text = builder.ToString();
        kind = (leadingWildcard, trailingWildcard) switch
        {
            (true, true) => SimplePatternKind.Contains,
            (true, false) => SimplePatternKind.EndsWith,
            (false, true) => SimplePatternKind.StartsWith,
            _ => SimplePatternKind.Equals,
        };
        return true;
    }

    /// <summary>Converts a <c>LIKE</c> pattern to an anchored regular expression.</summary>
    public static string ToRegexPattern(string pattern, char? escape)
    {
        var builder = new StringBuilder(pattern.Length + 8);
        builder.Append(@"\A");
        for (var i = 0; i < pattern.Length; i++)
        {
            var current = pattern[i];
            if (current == escape)
            {
                if (i + 1 >= pattern.Length)
                {
                    // SQL Server matches no value when the pattern ends with the escape character
                    return "[^\\s\\S]";
                }

                AppendLiteral(builder, pattern[++i]);
                continue;
            }

            switch (current)
            {
                case '%':
                    builder.Append(".*");
                    break;

                case '_':
                    builder.Append('.');
                    break;

                case '[' when TryAppendCharacterSet(builder, pattern, i, escape, out var endIndex):
                    i = endIndex;
                    break;

                default:
                    AppendLiteral(builder, current);
                    break;
            }
        }

        builder.Append(@"\z");
        return builder.ToString();
    }

    private static bool TryAppendCharacterSet(StringBuilder builder, string pattern, int startIndex, char? escape, out int endIndex)
    {
        var index = startIndex + 1;
        var negated = index < pattern.Length && pattern[index] == '^';
        if (negated)
        {
            index++;
        }

        var characters = new List<(char Start, char End)>();
        while (index < pattern.Length && pattern[index] != ']')
        {
            var start = pattern[index];
            if (start == escape && index + 1 < pattern.Length)
            {
                start = pattern[++index];
            }

            // "a-z" is a range, a '-' before the closing bracket is a literal
            if (index + 2 < pattern.Length && pattern[index + 1] == '-' && pattern[index + 2] != ']')
            {
                var end = pattern[index + 2];
                index += 2;
                if (end == escape && index + 1 < pattern.Length)
                {
                    end = pattern[++index];
                }

                characters.Add(start <= end ? (start, end) : (end, start));
            }
            else
            {
                characters.Add((start, start));
            }

            index++;
        }

        if (index >= pattern.Length)
        {
            // No closing bracket: the '[' is a literal
            endIndex = startIndex;
            return false;
        }

        endIndex = index;
        if (characters.Count == 0)
        {
            // "[]" matches no character, and "[^]" any character
            builder.Append(negated ? "[\\s\\S]" : "[^\\s\\S]");
            return true;
        }

        builder.Append(negated ? "[^" : "[");
        foreach (var (start, end) in characters)
        {
            AppendClassCharacter(builder, start);
            if (end != start)
            {
                builder.Append('-');
                AppendClassCharacter(builder, end);
            }
        }

        builder.Append(']');
        return true;
    }

    private static void AppendLiteral(StringBuilder builder, char value)
    {
        builder.Append(Regex.Escape(value.ToString()));
    }

    private static void AppendClassCharacter(StringBuilder builder, char value)
    {
        builder.Append(@"\u").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
    }

    internal enum SimplePatternKind
    {
        Equals,
        StartsWith,
        EndsWith,
        Contains,
    }
}
