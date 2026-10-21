using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>An immutable set of characters used by the matcher: a bitmap for ASCII, sorted ranges for the rest.</summary>
internal sealed class RegExpCharClass
{
    private readonly ulong _ascii0;
    private readonly ulong _ascii1;

    // The ranges of the non-ASCII values, as pairs of inclusive bounds
    private readonly int[] _ranges;

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
}
