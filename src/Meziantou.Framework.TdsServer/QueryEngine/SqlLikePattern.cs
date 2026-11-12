using System.Text;

namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Evaluates the patterns of the SQL <c>LIKE</c> operator.</summary>
/// <remarks>
/// <c>%</c> matches any string, <c>_</c> any character, <c>[abc]</c> and <c>[a-c]</c> one character of the set, and
/// <c>[^abc]</c> one character outside of it. The escape character makes the next character a literal. Characters are
/// compared ordinally, as the <c>=</c> operator of the engine does.
/// </remarks>
internal static class SqlLikePattern
{
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

        return IsMatch(value, pattern, escapeCharacter);
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

    /// <summary>Returns whether <paramref name="value"/> matches the whole <paramref name="pattern"/>.</summary>
    /// <remarks>
    /// Every element of a pattern but <c>%</c> matches exactly one character, so the matcher only has to remember the
    /// last <c>%</c>: when the rest of the pattern fails, that <c>%</c> takes one more character. The match takes at most
    /// O(value length × pattern length) steps, whatever the pattern, so a pattern sent by a client cannot make it explode.
    /// </remarks>
    public static bool IsMatch(string value, string pattern, char? escape)
    {
        if (!TryParse(pattern, escape, out var elements))
        {
            return false;
        }

        var elementIndex = 0;
        var valueIndex = 0;
        var wildcardIndex = -1;
        var wildcardValueIndex = 0;
        while (valueIndex < value.Length)
        {
            if (elementIndex < elements.Count && elements[elementIndex].Kind is LikeElementKind.AnyString)
            {
                // Try to match the rest of the pattern with an empty string first
                wildcardIndex = elementIndex;
                wildcardValueIndex = valueIndex;
                elementIndex++;
            }
            else if (elementIndex < elements.Count && elements[elementIndex].Matches(value[valueIndex]))
            {
                elementIndex++;
                valueIndex++;
            }
            else if (wildcardIndex >= 0)
            {
                elementIndex = wildcardIndex + 1;
                wildcardValueIndex++;
                valueIndex = wildcardValueIndex;
            }
            else
            {
                return false;
            }
        }

        while (elementIndex < elements.Count && elements[elementIndex].Kind is LikeElementKind.AnyString)
        {
            elementIndex++;
        }

        return elementIndex == elements.Count;
    }

    /// <summary>Splits a pattern into elements, or returns <see langword="false"/> when it matches no value.</summary>
    private static bool TryParse(string pattern, char? escape, out List<LikeElement> elements)
    {
        elements = new List<LikeElement>(pattern.Length);
        for (var i = 0; i < pattern.Length; i++)
        {
            var current = pattern[i];
            if (current == escape)
            {
                if (i + 1 >= pattern.Length)
                {
                    // SQL Server matches no value when the pattern ends with the escape character
                    return false;
                }

                elements.Add(LikeElement.Literal(pattern[++i]));
                continue;
            }

            switch (current)
            {
                case '%':
                    // Consecutive '%' are equivalent to a single one
                    if (elements is not [.., { Kind: LikeElementKind.AnyString }])
                    {
                        elements.Add(new LikeElement(LikeElementKind.AnyString, default, Ranges: null));
                    }

                    break;

                case '_':
                    elements.Add(new LikeElement(LikeElementKind.AnyCharacter, default, Ranges: null));
                    break;

                case '[' when TryParseCharacterSet(pattern, i, escape, out var set, out var endIndex):
                    elements.Add(set);
                    i = endIndex;
                    break;

                default:
                    elements.Add(LikeElement.Literal(current));
                    break;
            }
        }

        return true;
    }

    private static bool TryParseCharacterSet(string pattern, int startIndex, char? escape, out LikeElement element, out int endIndex)
    {
        var index = startIndex + 1;
        var negated = index < pattern.Length && pattern[index] == '^';
        if (negated)
        {
            index++;
        }

        var ranges = new List<(char Start, char End)>();
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

                ranges.Add(start <= end ? (start, end) : (end, start));
            }
            else
            {
                ranges.Add((start, start));
            }

            index++;
        }

        if (index >= pattern.Length)
        {
            // No closing bracket: the '[' is a literal
            element = default;
            endIndex = startIndex;
            return false;
        }

        // "[]" matches no character, and "[^]" any character
        element = new LikeElement(negated ? LikeElementKind.NegatedSet : LikeElementKind.Set, default, [.. ranges]);
        endIndex = index;
        return true;
    }

    private enum LikeElementKind
    {
        Literal,
        AnyCharacter,
        AnyString,
        Set,
        NegatedSet,
    }

    private readonly record struct LikeElement(LikeElementKind Kind, char Value, (char Start, char End)[]? Ranges)
    {
        public static LikeElement Literal(char value) => new(LikeElementKind.Literal, value, Ranges: null);

        public bool Matches(char c)
        {
            return Kind switch
            {
                LikeElementKind.Literal => c == Value,
                LikeElementKind.AnyCharacter => true,
                LikeElementKind.Set => IsInRanges(c),
                LikeElementKind.NegatedSet => !IsInRanges(c),
                _ => false,
            };
        }

        private bool IsInRanges(char c)
        {
            foreach (var (start, end) in Ranges!)
            {
                if (c >= start && c <= end)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal enum SimplePatternKind
    {
        Equals,
        StartsWith,
        EndsWith,
        Contains,
    }
}
