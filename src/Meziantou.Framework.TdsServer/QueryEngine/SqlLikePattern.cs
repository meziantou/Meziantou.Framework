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
    private const int MaxPatternLength = 4000;

    [ThreadStatic]
    private static CompiledPattern? t_lastPattern;

    /// <summary>Returns whether <paramref name="value"/> matches <paramref name="pattern"/>, or <see langword="null"/> when either is NULL.</summary>
    public static bool? Like(string? value, string? pattern, string? escape)
    {
        if (value is null || pattern is null)
        {
            return null;
        }

        ValidatePattern(pattern);
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
    /// The pattern runs as a nondeterministic automaton whose states are bits of a vector, one per element of the
    /// pattern: each character of the value updates all the states at once, 64 per machine word. So the match takes
    /// O(value length × pattern length / 64) steps whatever the pattern, and never scans the value twice.
    /// </remarks>
    public static bool IsMatch(string value, string pattern, char? escape)
    {
        var compiled = t_lastPattern;
        if (compiled is null || compiled.Escape != escape || !string.Equals(compiled.Pattern, pattern, StringComparison.Ordinal))
        {
            // Like is called once per row with the same pattern, so keep the last one instead of compiling it again
            compiled = new CompiledPattern(pattern, escape);
            t_lastPattern = compiled;
        }

        return compiled.IsMatch(value);
    }

    /// <summary>Throws when the pattern is longer than SQL Server accepts: 8,000 bytes, so 4,000 UTF-16 characters.</summary>
    public static void ValidatePattern(string pattern)
    {
        if (pattern.Length > MaxPatternLength)
        {
            throw new TdsQueryEngineException("The pattern of LIKE can be a maximum of 8,000 bytes.");
        }
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

    private sealed class CompiledPattern
    {
        // Bit j of a state vector is set when the value read so far matches the first j elements of the pattern
        private readonly int _finalState;
        private readonly int _wordCount;
        private readonly List<LikeElement>? _elements;
        private readonly ulong[] _loopMask = [];
        private readonly ulong[] _beforeAnyStringMask = [];
        private readonly char[] _intervalStarts = [];
        private readonly ulong[]?[] _intervalMasks = [];

        public CompiledPattern(string pattern, char? escape)
        {
            Pattern = pattern;
            Escape = escape;
            if (!TryParse(pattern, escape, out var elements))
            {
                return;
            }

            _elements = elements;
            _finalState = elements.Count;
            _wordCount = (elements.Count / 64) + 1;
            _loopMask = new ulong[_wordCount];
            _beforeAnyStringMask = new ulong[_wordCount];

            // The characters between two consecutive bounds of the ranges of the pattern match the same elements, so
            // a mask per interval gives the elements that a character matches
            var bounds = new SortedSet<char> { char.MinValue };
            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                if (element.Kind is LikeElementKind.AnyString)
                {
                    SetBit(_loopMask, i + 1);
                    SetBit(_beforeAnyStringMask, i);
                }

                foreach (var (start, end) in element.GetRanges())
                {
                    bounds.Add(start);
                    if (end < char.MaxValue)
                    {
                        bounds.Add((char)(end + 1));
                    }
                }
            }

            _intervalStarts = [.. bounds];
            _intervalMasks = new ulong[]?[_intervalStarts.Length];
        }

        public string Pattern { get; }

        public char? Escape { get; }

        public bool IsMatch(string value)
        {
            if (_elements is null)
            {
                return false;
            }

            var states = new ulong[_wordCount];
            var next = new ulong[_wordCount];
            states[0] = 1;
            AddEmptyMatches(states);
            foreach (var c in value)
            {
                var mask = GetMask(c);
                ulong carry = 0;
                ulong any = 0;
                for (var i = 0; i < states.Length; i++)
                {
                    var word = states[i];
                    next[i] = (((word << 1) | carry) & mask[i]) | (word & _loopMask[i]);
                    carry = word >> 63;
                    any |= next[i];
                }

                if (any == 0)
                {
                    return false;
                }

                AddEmptyMatches(next);
                (states, next) = (next, states);
            }

            return (states[_finalState / 64] & (1UL << (_finalState % 64))) != 0;
        }

        /// <summary>A '%' matches the empty string: the state before it implies the state after it.</summary>
        private void AddEmptyMatches(ulong[] states)
        {
            // '%' are never consecutive, so a single step reaches every state
            ulong carry = 0;
            for (var i = 0; i < states.Length; i++)
            {
                var before = states[i] & _beforeAnyStringMask[i];
                states[i] |= (before << 1) | carry;
                carry = before >> 63;
            }
        }

        private ulong[] GetMask(char c)
        {
            var index = Array.BinarySearch(_intervalStarts, c);
            if (index < 0)
            {
                index = ~index - 1;
            }

            return _intervalMasks[index] ??= CreateMask(c);
        }

        private ulong[] CreateMask(char c)
        {
            var mask = new ulong[_wordCount];
            for (var i = 0; i < _elements!.Count; i++)
            {
                if (_elements[i].Matches(c))
                {
                    SetBit(mask, i + 1);
                }
            }

            return mask;
        }

        private static void SetBit(ulong[] bits, int index) => bits[index / 64] |= 1UL << (index % 64);
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

        /// <summary>Gets the ranges of characters whose bounds change whether the element matches.</summary>
        public (char Start, char End)[] GetRanges()
        {
            return Kind switch
            {
                LikeElementKind.Literal => [(Value, Value)],
                LikeElementKind.Set or LikeElementKind.NegatedSet => Ranges!,
                _ => [],
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
