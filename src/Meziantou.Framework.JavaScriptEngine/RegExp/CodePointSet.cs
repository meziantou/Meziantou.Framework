using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A mutable set of code points (or code units), stored as sorted ranges. It is used while a pattern is compiled; the
/// matcher uses the immutable <see cref="RegExpCharClass"/>.
/// </summary>
internal sealed class CodePointSet
{
    public const int MaxCodePoint = 0x10FFFF;

    // Pairs of inclusive bounds. Once normalized, the ranges are sorted and neither overlap nor touch.
    private int[] _ranges;
    private int _count;
    private bool _normalized = true;

    public CodePointSet()
    {
        _ranges = new int[8];
    }

    private CodePointSet(int[] ranges, int count, bool normalized)
    {
        _ranges = ranges;
        _count = count;
        _normalized = normalized;
    }

    public static CodePointSet FromRanges(ReadOnlySpan<int> ranges)
    {
        var set = new CodePointSet(ranges.ToArray(), ranges.Length, normalized: false);
        set.Normalize();
        return set;
    }

    public static CodePointSet Single(int value)
    {
        var set = new CodePointSet();
        set.Add(value);
        return set;
    }

    public static CodePointSet Range(int start, int end)
    {
        var set = new CodePointSet();
        set.AddRange(start, end);
        return set;
    }

    /// <summary>Gets the number of ranges.</summary>
    public int RangeCount
    {
        get
        {
            Normalize();
            return _count / 2;
        }
    }

    public bool IsEmpty => _count == 0;

    public int GetRangeStart(int index)
    {
        Normalize();
        return _ranges[index * 2];
    }

    public int GetRangeEnd(int index)
    {
        Normalize();
        return _ranges[(index * 2) + 1];
    }

    public void Add(int value) => AddRange(value, value);

    public void AddRange(int start, int end)
    {
        if (start > end)
            return;

        if (_count + 2 > _ranges.Length)
        {
            Array.Resize(ref _ranges, _ranges.Length * 2);
        }

        if (_count > 0 && start <= _ranges[_count - 1] + 1)
        {
            // Appending in order (the common case) keeps the set normalized
            if (_normalized && start >= _ranges[_count - 2])
            {
                if (end > _ranges[_count - 1])
                {
                    _ranges[_count - 1] = end;
                }

                return;
            }

            _normalized = false;
        }

        _ranges[_count] = start;
        _ranges[_count + 1] = end;
        _count += 2;
    }

    public void AddSet(CodePointSet other)
    {
        other.Normalize();
        for (var i = 0; i < other._count; i += 2)
        {
            AddRange(other._ranges[i], other._ranges[i + 1]);
        }
    }

    public CodePointSet Clone()
    {
        Normalize();
        return new CodePointSet((int[])_ranges.Clone(), _count, normalized: true);
    }

    public bool Contains(int value)
    {
        Normalize();
        var low = 0;
        var high = (_count / 2) - 1;
        while (low <= high)
        {
            var middle = (int)(((uint)low + (uint)high) / 2);
            if (value < _ranges[middle * 2])
            {
                high = middle - 1;
            }
            else if (value > _ranges[(middle * 2) + 1])
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

    /// <summary>Gets whether the set contains exactly one value.</summary>
    public bool TryGetSingle(out int value)
    {
        Normalize();
        if (_count == 2 && _ranges[0] == _ranges[1])
        {
            value = _ranges[0];
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Gets the values in [0, max] that are not in the set.</summary>
    public CodePointSet Complement(int max)
    {
        Normalize();
        var result = new CodePointSet();
        var next = 0;
        for (var i = 0; i < _count; i += 2)
        {
            if (_ranges[i] > max)
                break;

            result.AddRange(next, _ranges[i] - 1);
            next = _ranges[i + 1] + 1;
        }

        result.AddRange(next, max);
        return result;
    }

    public static CodePointSet Union(CodePointSet left, CodePointSet right)
    {
        var result = left.Clone();
        result.AddSet(right);
        result.Normalize();
        return result;
    }

    public static CodePointSet Intersect(CodePointSet left, CodePointSet right)
    {
        left.Normalize();
        right.Normalize();
        var result = new CodePointSet();
        var i = 0;
        var j = 0;
        while (i < left._count && j < right._count)
        {
            var start = Math.Max(left._ranges[i], right._ranges[j]);
            var end = Math.Min(left._ranges[i + 1], right._ranges[j + 1]);
            if (start <= end)
            {
                result.AddRange(start, end);
            }

            if (left._ranges[i + 1] < right._ranges[j + 1])
            {
                i += 2;
            }
            else
            {
                j += 2;
            }
        }

        return result;
    }

    public static CodePointSet Subtract(CodePointSet left, CodePointSet right)
    {
        right.Normalize();
        var max = Math.Max(MaxCodePoint, right._count == 0 ? 0 : right._ranges[right._count - 1]);
        return Intersect(left, right.Complement(max));
    }

    public int[] ToRangeArray()
    {
        Normalize();
        return _ranges.AsSpan(0, _count).ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Normalize()
    {
        if (!_normalized)
        {
            NormalizeCore();
        }
    }

    private void NormalizeCore()
    {
        var pairs = new (int Start, int End)[_count / 2];
        for (var i = 0; i < pairs.Length; i++)
        {
            pairs[i] = (_ranges[i * 2], _ranges[(i * 2) + 1]);
        }

        Array.Sort(pairs, (a, b) => a.Start.CompareTo(b.Start));
        var count = 0;
        foreach (var (start, end) in pairs)
        {
            if (count > 0 && start <= _ranges[count - 1] + 1)
            {
                if (end > _ranges[count - 1])
                {
                    _ranges[count - 1] = end;
                }
            }
            else
            {
                _ranges[count] = start;
                _ranges[count + 1] = end;
                count += 2;
            }
        }

        _count = count;
        _normalized = true;
    }
}
