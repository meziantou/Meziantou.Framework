using System.Buffers;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>An immutable set of characters used by the matcher: a bitmap for ASCII, sorted ranges for the rest.</summary>
internal sealed class RegExpCharClass
{
    private readonly ulong _ascii0;
    private readonly ulong _ascii1;

    // The ranges of the non-ASCII values, as pairs of inclusive bounds
    private readonly int[] _ranges;

    // How to search the code units of the class in a span, created on first use
    private CodeUnitSearch? _search;

    public RegExpCharClass(CodePointSet set)
    {
        var count = set.RangeCount;
        var ranges = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var start = set.GetRangeStart(i);
            var end = set.GetRangeEnd(i);
            for (var c = start; c <= Math.Min(end, 127); c++)
            {
                if (c < 64)
                {
                    _ascii0 |= 1UL << c;
                }
                else
                {
                    _ascii1 |= 1UL << (c - 64);
                }
            }

            if (end >= 128)
            {
                ranges.Add(Math.Max(start, 128));
                ranges.Add(end);
            }
        }

        _ranges = [.. ranges];
        Set = set;
    }

    /// <summary>Gets the set the class was created from.</summary>
    public CodePointSet Set { get; }

    /// <summary>Gets whether the class contains a non-ASCII value.</summary>
    public bool HasNonAscii => _ranges.Length > 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int value)
    {
        if (value < 64)
            return (_ascii0 & (1UL << value)) != 0;

        if (value < 128)
            return (_ascii1 & (1UL << (value - 64))) != 0;

        return ContainsNonAscii(value);
    }

    /// <summary>Gets the index of the first code unit of <paramref name="span"/> in the class, or -1.</summary>
    public int IndexOfAny(ReadOnlySpan<char> span)
    {
        var search = _search ??= new CodeUnitSearch(Set);
        return search.Kind switch
        {
            CodeUnitSearchKind.Range => span.IndexOfAnyInRange(search.Low, search.High),
            CodeUnitSearchKind.ExceptRange => span.IndexOfAnyExceptInRange(search.Low, search.High),
            CodeUnitSearchKind.Values => span.IndexOfAny(search.Values!),
            CodeUnitSearchKind.ExceptValues => span.IndexOfAnyExcept(search.Values!),
            _ => IndexOfAnySlow(span),
        };
    }

    /// <summary>Gets the index of the first code unit of <paramref name="span"/> not in the class, or -1.</summary>
    public int IndexOfAnyExcept(ReadOnlySpan<char> span)
    {
        var search = _search ??= new CodeUnitSearch(Set);
        return search.Kind switch
        {
            CodeUnitSearchKind.Range => span.IndexOfAnyExceptInRange(search.Low, search.High),
            CodeUnitSearchKind.ExceptRange => span.IndexOfAnyInRange(search.Low, search.High),
            CodeUnitSearchKind.Values => span.IndexOfAnyExcept(search.Values!),
            CodeUnitSearchKind.ExceptValues => span.IndexOfAny(search.Values!),
            _ => IndexOfAnyExceptSlow(span),
        };
    }

    private int IndexOfAnySlow(ReadOnlySpan<char> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            if (Contains(span[i]))
                return i;
        }

        return -1;
    }

    private int IndexOfAnyExceptSlow(ReadOnlySpan<char> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            if (!Contains(span[i]))
                return i;
        }

        return -1;
    }

    public bool ContainsAscii(int value)
    {
        return value < 64 ? (_ascii0 & (1UL << value)) != 0 : (_ascii1 & (1UL << (value - 64))) != 0;
    }

    private bool ContainsNonAscii(int value)
    {
        var ranges = _ranges;
        var low = 0;
        var high = (ranges.Length / 2) - 1;
        while (low <= high)
        {
            var middle = (int)(((uint)low + (uint)high) / 2);
            if (value < ranges[middle * 2])
            {
                high = middle - 1;
            }
            else if (value > ranges[(middle * 2) + 1])
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private enum CodeUnitSearchKind
    {
        Loop,
        Range,
        ExceptRange,
        Values,
        ExceptValues,
    }

    /// <summary>
    /// A vectorized search of the code units of a class: one range, the complement of one range, or a few values (or
    /// the complement of a few values). The other classes are searched one code unit at a time.
    /// </summary>
    private sealed class CodeUnitSearch
    {
        private const int MaxValues = 512;

        public CodeUnitSearch(CodePointSet set)
        {
            var codeUnits = CodePointSet.Intersect(set, CodePointSet.Range(0, 0xFFFF));
            if (TryInitialize(codeUnits, CodeUnitSearchKind.Range, CodeUnitSearchKind.Values))
                return;

            TryInitialize(codeUnits.Complement(0xFFFF), CodeUnitSearchKind.ExceptRange, CodeUnitSearchKind.ExceptValues);
        }

        public CodeUnitSearchKind Kind { get; private set; }

        public char Low { get; private set; }

        public char High { get; private set; }

        public SearchValues<char>? Values { get; private set; }

        private bool TryInitialize(CodePointSet set, CodeUnitSearchKind rangeKind, CodeUnitSearchKind valuesKind)
        {
            var rangeCount = set.RangeCount;
            if (rangeCount == 1)
            {
                Kind = rangeKind;
                Low = (char)set.GetRangeStart(0);
                High = (char)set.GetRangeEnd(0);
                return true;
            }

            var count = 0;
            for (var i = 0; i < rangeCount; i++)
            {
                count += set.GetRangeEnd(i) - set.GetRangeStart(i) + 1;
                if (count > MaxValues)
                    return false;
            }

            var values = new char[count];
            var index = 0;
            for (var i = 0; i < rangeCount; i++)
            {
                for (var c = set.GetRangeStart(i); c <= set.GetRangeEnd(i); c++)
                {
                    values[index++] = (char)c;
                }
            }

            Kind = valuesKind;
            Values = SearchValues.Create(values);
            return true;
        }
    }
}
