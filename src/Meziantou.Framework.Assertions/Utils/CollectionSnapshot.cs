using System.Collections;
using System.Diagnostics;

namespace Meziantou.Framework.Assertions;

internal static class CollectionSnapshot
{
    public static CollectionSnapshot<T> Create<T>(IEnumerable<T> source)
    {
        return CollectionSnapshot<T>.Create(source);
    }

    public static CollectionSnapshot<object?> Create(IEnumerable source)
    {
        // The IList indexer of a multidimensional array throws, so such an array is read through its enumerator.
        if (source is IList list && (source is not Array || source.GetType().IsSZArray))
            return new NonGenericListSnapshot(list);

        return CollectionSnapshot<object?>.Create(EnumerateObjects(source));
    }

    public static AsyncCollectionSnapshot<T> Create<T>(IAsyncEnumerable<T> source)
    {
        return new AsyncCollectionSnapshot<T>(source);
    }

    /// <summary>
    /// Creates a snapshot for an assertion that reads the sequence once, in order, and reports a failure about the last
    /// observed item (or about no item). A lazy sequence is not buffered entirely: only the items a failure message can
    /// show are retained. Call <see cref="CollectionSnapshot{T}.StopDiscardingItems"/> before formatting the failure.
    /// </summary>
    public static CollectionSnapshot<T> CreateSinglePass<T>(IEnumerable<T> source)
    {
        if (source is IReadOnlyList<T> or IList<T>)
            return CollectionSnapshot<T>.Create(source);

        var (prefixCapacity, recentCapacity) = GetSinglePassRetention();

        return CollectionSnapshot<T>.CreateWindowed(source, prefixCapacity, recentCapacity);
    }

    /// <inheritdoc cref="CreateSinglePass{T}(IEnumerable{T})"/>
    public static AsyncCollectionSnapshot<T> CreateSinglePass<T>(IAsyncEnumerable<T> source)
    {
        var (prefixCapacity, recentCapacity) = GetSinglePassRetention();

        return new AsyncCollectionSnapshot<T>(source, prefixCapacity, recentCapacity);
    }

    /// <summary>
    /// Computes the items to retain so the formatter finds every item it writes when the highlighted item is the last
    /// observed one. Without a highlighted item, or when it is within the leading range, the formatter writes items from
    /// the start, up to the largest of <see cref="FormatterOptions.MaxFormattedItems"/> and
    /// <see cref="FormatterOptions.SuffixItemCount"/>; items after the highlighted one are read after the failure.
    /// Otherwise it writes <see cref="FormatterOptions.PrefixItemCount"/> leading items and
    /// <see cref="FormatterOptions.HighlightedContextItemCount"/> items before the highlighted one.
    /// </summary>
    private static (int PrefixCapacity, int RecentCapacity) GetSinglePassRetention()
    {
        var options = Assert.ErrorFormatter.CurrentOptions;
        var prefixCapacity = Math.Max(options.MaxFormattedItems, Math.Max(options.PrefixItemCount, options.SuffixItemCount));
        var recentCapacity = (int)Math.Min((long)options.HighlightedContextItemCount + 1, int.MaxValue);

        return (prefixCapacity, recentCapacity);
    }

    internal static string FormatCount(int observedCount, bool isComplete)
    {
        var count = observedCount.ToString(CultureInfo.InvariantCulture);

        return isComplete ? count : "at least " + count;
    }

    private static IEnumerable<object?> EnumerateObjects(IEnumerable value)
    {
        foreach (var item in value)
        {
            yield return item;
        }
    }

    private sealed class NonGenericListSnapshot(IList source) : CollectionSnapshot<object?>
    {
        private readonly ListAdapter _items = new(source);

        public override bool IsComplete => true;

        public override int ObservedCount => _items.Count;

        public override IReadOnlyList<object?> Items => _items;

        public override bool TryGetItem(int index, out object? item)
        {
            if (index < source.Count)
            {
                item = source[index];
                return true;
            }

            item = default;

            return false;
        }

        private sealed class ListAdapter(IList source) : IReadOnlyList<object?>
        {
            public int Count => source.Count;

            public object? this[int index] => source[index];

            public IEnumerator<object?> GetEnumerator()
            {
                for (var i = 0; i < source.Count; i++)
                {
                    yield return source[i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

internal abstract class CollectionSnapshot<T> : IEnumerable<T>, IDisposable
{
    public abstract bool IsComplete { get; }

    public abstract int ObservedCount { get; }

    public abstract IReadOnlyList<T> Items { get; }

    public static CollectionSnapshot<T> Create(IEnumerable<T> source)
    {
        if (source is IReadOnlyList<T> readOnlyList)
            return new ReadOnlyListSnapshot(readOnlyList);

        if (source is IList<T> list)
            return new ListSnapshot(list);

        return new LazySnapshot(source);
    }

    public static CollectionSnapshot<T> CreateWindowed(IEnumerable<T> source, int prefixCapacity, int recentCapacity)
    {
        return new WindowedLazySnapshot(source, new WindowedItemBuffer<T>(prefixCapacity, recentCapacity));
    }

    public abstract bool TryGetItem(int index, out T item);

    /// <summary>Formats the number of items, as a lower bound when the sequence was not read to its end.</summary>
    public string GetCountText()
    {
        return CollectionSnapshot.FormatCount(ObservedCount, IsComplete);
    }

    /// <summary>Keeps every item observed from now on, and the items currently retained, available to the formatter.</summary>
    public virtual void StopDiscardingItems()
    {
    }

    public void EnsureComplete()
    {
        if (IsComplete)
            return;

        EnsureCompleteCore();
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    public virtual void Dispose()
    {
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    protected virtual void EnsureCompleteCore()
    {
        for (var index = ObservedCount; TryGetItem(index, out _); index++)
        {
        }
    }

    public sealed class Enumerator(CollectionSnapshot<T> snapshot) : IEnumerator<T>
    {
        private int _index = -1;

        public T Current { get; private set; } = default!;

        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            _index++;
            if (snapshot.TryGetItem(_index, out var item))
            {
                Current = item;
                return true;
            }

            Current = default!;

            return false;
        }

        public void Reset()
        {
            _index = -1;
            Current = default!;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ReadOnlyListSnapshot(IReadOnlyList<T> source) : CollectionSnapshot<T>
    {
        public override bool IsComplete => true;

        public override int ObservedCount => source.Count;

        public override IReadOnlyList<T> Items => source;

        public override bool TryGetItem(int index, out T item)
        {
            if (index < source.Count)
            {
                item = source[index];
                return true;
            }

            item = default!;

            return false;
        }
    }

    private sealed class ListSnapshot(IList<T> source) : CollectionSnapshot<T>
    {
        private readonly ListAdapter _items = new(source);

        public override bool IsComplete => true;

        public override int ObservedCount => _items.Count;

        public override IReadOnlyList<T> Items => _items;

        public override bool TryGetItem(int index, out T item)
        {
            if (index < source.Count)
            {
                item = source[index];
                return true;
            }

            item = default!;

            return false;
        }

        private sealed class ListAdapter(IList<T> source) : IReadOnlyList<T>
        {
            public int Count => source.Count;

            public T this[int index] => source[index];

            public IEnumerator<T> GetEnumerator() => source.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }

    private sealed class LazySnapshot : CollectionSnapshot<T>
    {
        /// <summary>
        /// Upper bound of the capacity reserved before any item is read. Many assertions stop after a few items
        /// (NotEmpty, StartsWith, a failing All...), so sizing the cache for a large known count up front would
        /// allocate memory for items that are never observed.
        /// </summary>
        private const int MaxInitialCapacity = 64;

        private readonly List<T> _cache;
        private readonly IEnumerable<T> _source;
        private readonly int _knownCount;
        private IEnumerator<T>? _enumerator;
        private bool _isComplete;

        public LazySnapshot(IEnumerable<T> source)
        {
            _source = source;
            if (Enumerable.TryGetNonEnumeratedCount(source, out var count))
            {
                _knownCount = count;
                _cache = new List<T>(Math.Min(count, MaxInitialCapacity));
            }
            else
            {
                _cache = [];
            }
        }

        public override bool IsComplete => _isComplete;

        public override int ObservedCount => _cache.Count;

        public override IReadOnlyList<T> Items => _cache;

        public override bool TryGetItem(int index, out T item)
        {
            if (index < _cache.Count)
            {
                item = _cache[index];
                return true;
            }

            if (_isComplete)
            {
                item = default!;
                return false;
            }

            _enumerator ??= _source.GetEnumerator();

            Debug.Assert(_enumerator is not null);
            while (_cache.Count <= index && _enumerator.MoveNext())
            {
                item = _enumerator.Current;
                _cache.Add(item);
            }

            if (index < _cache.Count)
            {
                item = _cache[index];
                return true;
            }

            CompleteEnumeration();
            item = default!;

            return false;
        }

        protected override void EnsureCompleteCore()
        {
            // Every item is about to be read, so the known count can size the cache without over-allocating
            _cache.EnsureCapacity(_knownCount);
            _enumerator ??= _source.GetEnumerator();

            Debug.Assert(_enumerator is not null);
            while (_enumerator.MoveNext())
            {
                _cache.Add(_enumerator.Current);
            }

            CompleteEnumeration();
        }

        public override void Dispose()
        {
            _enumerator?.Dispose();
            _enumerator = null;
            base.Dispose();
        }

        private void CompleteEnumeration()
        {
            _isComplete = true;
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }

    private sealed class WindowedLazySnapshot(IEnumerable<T> source, WindowedItemBuffer<T> items) : CollectionSnapshot<T>
    {
        private IEnumerator<T>? _enumerator;
        private bool _isComplete;

        public override bool IsComplete => _isComplete;

        public override int ObservedCount => items.Count;

        public override IReadOnlyList<T> Items => items;

        public override bool TryGetItem(int index, out T item)
        {
            if (index < items.Count)
            {
                if (items.TryGetItem(index, out item))
                    return true;

                throw new InvalidOperationException($"The item at index {index.ToString(CultureInfo.InvariantCulture)} was discarded.");
            }

            if (_isComplete)
            {
                item = default!;
                return false;
            }

            _enumerator ??= source.GetEnumerator();

            Debug.Assert(_enumerator is not null);
            while (items.Count <= index && _enumerator.MoveNext())
            {
                items.Add(_enumerator.Current);
            }

            if (index < items.Count)
            {
                return items.TryGetItem(index, out item);
            }

            CompleteEnumeration();
            item = default!;

            return false;
        }

        public override void StopDiscardingItems()
        {
            items.RetainAll();
        }

        public override void Dispose()
        {
            _enumerator?.Dispose();
            _enumerator = null;
            base.Dispose();
        }

        private void CompleteEnumeration()
        {
            _isComplete = true;
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }
}
