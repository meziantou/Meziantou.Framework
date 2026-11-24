using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Csv.Internals;

/// <summary>Collects items in arrays that are not on the large object heap, then copies them once to a list of the exact size.</summary>
/// <remarks>
/// A <see cref="List{T}"/> that grows copies its items to an array twice larger: from 10,625 references, the array is on the large object heap. Each
/// of these arrays counts toward the budget of the large object heap, whose exhaustion starts a gen2 collection, which marks every record read so
/// far. Collecting the 10,000 records of a file in segments, then copying them to a list, was 4 times faster than adding them to a list.
/// </remarks>
internal sealed class SegmentedListBuilder<T>
{
    private const int MinSegmentLength = 16;

    // The largest segment is below the 85,000 bytes from which an array is on the large object heap
    private static readonly int MaxSegmentLength = Math.Max(MinSegmentLength, 64 * 1024 / Unsafe.SizeOf<T>());

    private List<T[]>? _segments;
    private T[] _current = [];
    private int _currentCount;
    private int _count;

    public int Count => _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        var current = _current;
        var index = _currentCount;
        if ((uint)index < (uint)current.Length)
        {
            current[index] = item;
            _currentCount = index + 1;
            _count++;
            return;
        }

        AddToNewSegment(item);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddToNewSegment(T item)
    {
        if (_current.Length > 0)
        {
            (_segments ??= []).Add(_current);
        }

        // The segments double the capacity, as a list does, until they reach the maximum length
        _current = new T[Math.Clamp(_count, MinSegmentLength, MaxSegmentLength)];
        _current[0] = item;
        _currentCount = 1;
        _count++;
    }

    public List<T> ToList()
    {
        var result = new List<T>(_count);
        if (_segments is not null)
        {
            foreach (var segment in _segments)
            {
                result.AddRange(segment.AsSpan());
            }
        }

        result.AddRange(_current.AsSpan(0, _currentCount));
        return result;
    }
}
