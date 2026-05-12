using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Globbing.Internals;

internal abstract class CharacterSetMatcher
{
    public static CharacterSetMatcher Create(string set, bool ignoreCase)
    {
        if (set.Length == 0)
            return new StringContainsCharacterSetMatcher(set, ignoreCase);

        if (ignoreCase && !IsAscii(set))
            return new StringContainsCharacterSetMatcher(set, ignoreCase);

        Span<char> values = set.Length <= 64 ? stackalloc char[set.Length] : new char[set.Length];
        for (var i = 0; i < set.Length; i++)
        {
            values[i] = ignoreCase ? ToLowerAscii(set[i]) : set[i];
        }

        values.Sort();

        var distinctLength = 1;
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] == values[distinctLength - 1])
                continue;

            values[distinctLength] = values[i];
            distinctLength++;
        }

        var distinctValues = values[..distinctLength];
        if (distinctValues.Length == 1)
            return new SingleCharacterSetMatcher(distinctValues[0], ignoreCase);

        var rangeStart = distinctValues[0];
        var isConsecutiveRange = true;
        for (var i = 1; i < distinctValues.Length; i++)
        {
            if (distinctValues[i] != rangeStart + i)
            {
                isConsecutiveRange = false;
                break;
            }
        }

        if (isConsecutiveRange)
            return new CharacterRangeSetMatcher(new CharacterRange(rangeStart, distinctValues[^1]), ignoreCase);

        return new SearchValuesCharacterSetMatcher(SearchValues.Create(distinctValues), ignoreCase);
    }

    public abstract bool IsMatch(char value);

    private static bool IsAscii(string value)
    {
        foreach (var c in value)
        {
            if (c > 0x7F)
                return false;
        }

        return true;
    }

    private static char ToLowerAscii(char value)
    {
        if (CharacterRangeSegment.IsAsciiUpper(value))
            return (char)(value | 0x20);

        return value;
    }

    private sealed class StringContainsCharacterSetMatcher : CharacterSetMatcher
    {
        private readonly string _set;
        private readonly StringComparison _stringComparison;

        public StringContainsCharacterSetMatcher(string set, bool ignoreCase)
        {
            _set = set;
            _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public override bool IsMatch(char value)
        {
            return _set.Contains(value, _stringComparison);
        }
    }

    private sealed class SingleCharacterSetMatcher : CharacterSetMatcher
    {
        private readonly char _singleCharacter;
        private readonly bool _asciiIgnoreCase;

        public SingleCharacterSetMatcher(char singleCharacter, bool asciiIgnoreCase)
        {
            _singleCharacter = singleCharacter;
            _asciiIgnoreCase = asciiIgnoreCase;
        }

        public override bool IsMatch(char value)
        {
            if (_asciiIgnoreCase)
            {
                value = ToLowerAscii(value);
            }

            return value == _singleCharacter;
        }
    }

    private sealed class CharacterRangeSetMatcher : CharacterSetMatcher
    {
        private readonly CharacterRange _range;
        private readonly bool _asciiIgnoreCase;

        public CharacterRangeSetMatcher(CharacterRange range, bool asciiIgnoreCase)
        {
            _range = range;
            _asciiIgnoreCase = asciiIgnoreCase;
        }

        public override bool IsMatch(char value)
        {
            if (_asciiIgnoreCase)
            {
                value = ToLowerAscii(value);
            }

            return _range.IsInRange(value);
        }
    }

    private sealed class SearchValuesCharacterSetMatcher : CharacterSetMatcher
    {
        private readonly SearchValues<char> _searchValues;
        private readonly bool _asciiIgnoreCase;

        public SearchValuesCharacterSetMatcher(SearchValues<char> searchValues, bool asciiIgnoreCase)
        {
            _searchValues = searchValues;
            _asciiIgnoreCase = asciiIgnoreCase;
        }

        public override bool IsMatch(char value)
        {
            if (_asciiIgnoreCase)
            {
                value = ToLowerAscii(value);
            }

            return MemoryMarshal.CreateReadOnlySpan(ref value, 1).ContainsAny(_searchValues);
        }
    }
}
