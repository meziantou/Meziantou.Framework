using System.Collections;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class CollectionSnapshotTests
{
    [Fact]
    public void Create_UsesArrayDirectly()
    {
        var source = new[] { 1, 2, 3 };

        using var snapshot = CollectionSnapshot.Create<int>(source);

        AssertionsAssert.True(snapshot.IsComplete);
        AssertionsAssert.Equal(source.Length, snapshot.ObservedCount);
        AssertionsAssert.Same(source, snapshot.Items);
    }

    [Fact]
    public void Create_UsesListDirectly()
    {
        var source = new List<int> { 1, 2, 3 };

        using var snapshot = CollectionSnapshot.Create<int>(source);

        AssertionsAssert.True(snapshot.IsComplete);
        AssertionsAssert.Equal(source.Count, snapshot.ObservedCount);
        AssertionsAssert.Same(source, snapshot.Items);
    }

    [Fact]
    public void Equal_UsesReadOnlyListIndexer()
    {
        var actual = new ThrowingEnumerableReadOnlyList<int>([1, 2, 3]);

        AssertionsAssert.Equal<int>([1, 2, 3], actual);
    }

    [Fact]
    public void Equal_UsesListIndexer()
    {
        var actual = new ThrowingEnumerableList<int>([1, 2, 3]);

        AssertionsAssert.Equal<int>([1, 2, 3], actual);
    }

    [Fact]
    public void EnsureComplete_ReadOnlyList_IsNoOp()
    {
        var source = new ThrowingEnumerableReadOnlyList<int>([1, 2, 3]);

        using var snapshot = CollectionSnapshot.Create<int>(source);
        snapshot.EnsureComplete();

        AssertionsAssert.True(snapshot.IsComplete);
        AssertionsAssert.Same(source, snapshot.Items);
    }

    [Fact]
    public void Create_LazyEnumerableCachesItemsIncrementally()
    {
        var source = new TrackingEnumerable<int>([1, 2, 3]);

        using var snapshot = CollectionSnapshot.Create<int>(source);

        AssertionsAssert.False(snapshot.IsComplete);
        AssertionsAssert.Equal(0, snapshot.ObservedCount);
        AssertionsAssert.Equal(0, source.MoveNextCount);

        AssertionsAssert.True(snapshot.TryGetItem(0, out var item));
        AssertionsAssert.Equal(1, item);
        AssertionsAssert.Equal(1, snapshot.ObservedCount);
        AssertionsAssert.Equal(1, source.MoveNextCount);
        AssertionsAssert.False(snapshot.IsComplete);

        snapshot.Dispose();

        AssertionsAssert.True(source.EnumeratorDisposed);
    }

    [Fact]
    public void Create_LazyEnumerableCompletesWhenFullyObserved()
    {
        var source = new TrackingEnumerable<int>([1]);

        using var snapshot = CollectionSnapshot.Create<int>(source);

        AssertionsAssert.True(snapshot.TryGetItem(0, out var item));
        AssertionsAssert.Equal(1, item);
        AssertionsAssert.False(snapshot.TryGetItem(1, out _));
        AssertionsAssert.True(snapshot.IsComplete);
        AssertionsAssert.True(source.EnumeratorDisposed);
    }

    [Fact]
    public void Create_LazyEnumerable_CanFetchSkippedIndex()
    {
        var source = new TrackingEnumerable<int>([1, 2, 3]);

        using var snapshot = CollectionSnapshot.Create<int>(source);

        AssertionsAssert.True(snapshot.TryGetItem(2, out var item));
        AssertionsAssert.Equal(3, item);
        AssertionsAssert.Equal(3, snapshot.ObservedCount);
        AssertionsAssert.Equal(3, source.MoveNextCount);
        AssertionsAssert.False(snapshot.IsComplete);
    }

    [Fact]
    public async Task Create_AsyncEnumerableCachesItemsIncrementally()
    {
        var source = new TrackingAsyncEnumerable<int>([1, 2, 3]);

        await using var snapshot = CollectionSnapshot.Create<int>(source);

        AssertionsAssert.False(snapshot.IsComplete);
        AssertionsAssert.Equal(0, snapshot.ObservedCount);
        AssertionsAssert.Equal(0, source.MoveNextCount);

        var (success, item) = await snapshot.TryGetItem(0, TestContext.Current.CancellationToken);

        AssertionsAssert.True(success);
        AssertionsAssert.Equal(1, item);
        AssertionsAssert.Equal(1, snapshot.ObservedCount);
        AssertionsAssert.Equal(1, source.MoveNextCount);
        AssertionsAssert.False(snapshot.IsComplete);

        await snapshot.DisposeAsync();

        AssertionsAssert.True(source.EnumeratorDisposed);
    }

    [Fact]
    public async Task Create_AsyncEnumerable_CanFetchSkippedIndex()
    {
        var source = new TrackingAsyncEnumerable<int>([1, 2, 3]);

        await using var snapshot = CollectionSnapshot.Create<int>(source);

        var (success, item) = await snapshot.TryGetItem(2, TestContext.Current.CancellationToken);

        AssertionsAssert.True(success);
        AssertionsAssert.Equal(3, item);
        AssertionsAssert.Equal(3, snapshot.ObservedCount);
        AssertionsAssert.Equal(3, source.MoveNextCount);
        AssertionsAssert.False(snapshot.IsComplete);
    }

    [Fact]
    public async Task AsyncSnapshot_CanBeEnumerated()
    {
        var source = new TrackingAsyncEnumerable<int>([1, 2, 3]);
        var result = new List<int>();

        await using var snapshot = CollectionSnapshot.Create<int>(source);
        await foreach (var item in snapshot.WithCancellation(TestContext.Current.CancellationToken))
        {
            result.Add(item);
        }

        AssertionsAssert.Equal<int>([1, 2, 3], result);
        AssertionsAssert.True(snapshot.IsComplete);
        AssertionsAssert.True(source.EnumeratorDisposed);
    }

    private sealed class ThrowingEnumerableReadOnlyList<T>(IReadOnlyList<T> items) : IReadOnlyList<T>
    {
        public int Count => items.Count;

        public T this[int index] => items[index];

        public IEnumerator<T> GetEnumerator() => throw new AssertionException("GetEnumerator should not be used.");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ThrowingEnumerableList<T>(IReadOnlyList<T> items) : IList<T>
    {
        public int Count => items.Count;

        public bool IsReadOnly => true;

        public T this[int index]
        {
            get => items[index];
            set => throw new NotSupportedException();
        }

        public int IndexOf(T item) => throw new AssertionException("IndexOf should not be used.");

        public void Insert(int index, T item) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();

        public void Add(T item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public bool Contains(T item) => throw new AssertionException("Contains should not be used.");

        public void CopyTo(T[] array, int arrayIndex) => throw new AssertionException("CopyTo should not be used.");

        public bool Remove(T item) => throw new NotSupportedException();

        public IEnumerator<T> GetEnumerator() => throw new AssertionException("GetEnumerator should not be used.");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class TrackingEnumerable<T>(IReadOnlyList<T> items) : IEnumerable<T>
    {
        public int MoveNextCount { get; private set; }

        public bool EnumeratorDisposed { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(this, items);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator(TrackingEnumerable<T> owner, IReadOnlyList<T> items) : IEnumerator<T>
        {
            private int _index = -1;

            public T Current => items[_index];

            object? IEnumerator.Current => Current;

            public bool MoveNext()
            {
                owner.MoveNextCount++;
                _index++;

                return _index < items.Count;
            }

            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
                owner.EnumeratorDisposed = true;
            }
        }
    }

    private sealed class TrackingAsyncEnumerable<T>(IReadOnlyList<T> items) : IAsyncEnumerable<T>
    {
        public int MoveNextCount { get; private set; }

        public bool EnumeratorDisposed { get; private set; }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(this, items);
        }

        private sealed class Enumerator(TrackingAsyncEnumerable<T> owner, IReadOnlyList<T> items) : IAsyncEnumerator<T>
        {
            private int _index = -1;

            public T Current => items[_index];

            public ValueTask<bool> MoveNextAsync()
            {
                owner.MoveNextCount++;
                _index++;

                return ValueTask.FromResult(_index < items.Count);
            }

            public ValueTask DisposeAsync()
            {
                owner.EnumeratorDisposed = true;

                return ValueTask.CompletedTask;
            }
        }
    }
}
