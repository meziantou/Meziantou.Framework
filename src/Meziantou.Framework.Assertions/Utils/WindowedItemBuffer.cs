using System.Collections;

namespace Meziantou.Framework.Assertions;

/// <summary>
/// Stores the items observed from a sequence, optionally keeping only the first items and a window of the most recent
/// ones. A single-pass assertion that fails reports the items around the failing one, which is always the last observed
/// item, so the items in between only need to be counted, not retained.
/// </summary>
/// <remarks>
/// Discarded items are yielded as <see langword="default"/> by the enumerator so indexes keep their meaning. The
/// formatter skips them: see <see cref="CollectionSnapshot.CreateSinglePass{T}(IEnumerable{T})"/> for the retention that
/// keeps every formatted item.
/// </remarks>
internal sealed class WindowedItemBuffer<T> : IReadOnlyList<T>
{
    /// <summary>Minimum number of items discarded at once, so trimming the window stays amortized O(1) per item.</summary>
    private const int MinimumDiscardCount = 64;

    private readonly List<T> _prefix = [];
    private readonly List<T> _recent = [];
    private readonly int _prefixCapacity;
    private readonly int _recentCapacity;
    private int _recentStartIndex;
    private bool _retainAll;

    /// <summary>Creates a buffer that retains every item.</summary>
    public WindowedItemBuffer()
        : this(int.MaxValue, int.MaxValue)
    {
    }

    public WindowedItemBuffer(int prefixCapacity, int recentCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(prefixCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(recentCapacity);

        _prefixCapacity = prefixCapacity;

        // The last observed item is always retained: the snapshot reads it back right after adding it.
        _recentCapacity = Math.Max(recentCapacity, 1);
    }

    public int Count { get; private set; }

    /// <summary>Gets the number of items currently held in memory.</summary>
    internal int RetainedCount => _prefix.Count + _recent.Count;

    public T this[int index]
    {
        get
        {
            if (TryGetItem(index, out var item))
                return item;

            if ((uint)index < (uint)Count)
                throw new InvalidOperationException($"The item at index {index.ToString(CultureInfo.InvariantCulture)} was discarded.");

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public void Add(T item)
    {
        if (Count < _prefixCapacity)
        {
            _prefix.Add(item);
        }
        else
        {
            if (_recent.Count is 0)
            {
                _recentStartIndex = Count;
            }

            _recent.Add(item);
            if (!_retainAll && _recent.Count - _recentCapacity >= Math.Max(_recentCapacity, MinimumDiscardCount))
            {
                var discardCount = _recent.Count - _recentCapacity;
                _recent.RemoveRange(0, discardCount);
                _recentStartIndex += discardCount;
            }
        }

        Count++;
    }

    /// <summary>Stops discarding items, so the items observed from now on and the current window stay available.</summary>
    public void RetainAll()
    {
        _retainAll = true;
    }

    public bool TryGetItem(int index, out T item)
    {
        if ((uint)index < (uint)_prefix.Count)
        {
            item = _prefix[index];
            return true;
        }

        var recentIndex = index - _recentStartIndex;
        if (index >= _prefix.Count && recentIndex >= 0 && recentIndex < _recent.Count)
        {
            item = _recent[recentIndex];
            return true;
        }

        item = default!;

        return false;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return TryGetItem(index, out var item) ? item : default!;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
