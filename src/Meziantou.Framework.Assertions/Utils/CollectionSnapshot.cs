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
        if (source is IList list)
            return new NonGenericListSnapshot(list);

        return CollectionSnapshot<object?>.Create(EnumerateObjects(source));
    }

    public static AsyncCollectionSnapshot<T> Create<T>(IAsyncEnumerable<T> source)
    {
        return new AsyncCollectionSnapshot<T>(source);
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

    public abstract bool TryGetItem(int index, out T item);

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    public virtual void Dispose()
    {
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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
        private readonly List<T> _cache;
        private readonly IEnumerable<T> _source;
        private IEnumerator<T>? _enumerator;
        private bool _isComplete;

        public LazySnapshot(IEnumerable<T> source)
        {
            _source = source;
            _cache = Enumerable.TryGetNonEnumeratedCount(source, out var count) ? new List<T>(count) : [];
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
            if (_enumerator.MoveNext())
            {
                item = _enumerator.Current;
                _cache.Add(item);
                return true;
            }

            _isComplete = true;
            _enumerator.Dispose();
            _enumerator = null;
            item = default!;

            return false;
        }

        public override void Dispose()
        {
            _enumerator?.Dispose();
            _enumerator = null;
            base.Dispose();
        }
    }
}
