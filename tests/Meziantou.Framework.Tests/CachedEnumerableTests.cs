using System.Collections;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class CachedEnumerableTests
{
    private sealed class SingleEnumerable<T>(IEnumerable<T> enumerable) : IEnumerable<T>
    {
        private bool _enumerated;

        public IEnumerator<T> GetEnumerator()
        {
            if (_enumerated)
                throw new InvalidOperationException();

            _enumerated = true;
            return enumerable.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SingleAsyncEnumerable<T>(IAsyncEnumerable<T> enumerable) : IAsyncEnumerable<T>
    {
        private bool _enumerated;

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            if (_enumerated)
                throw new InvalidOperationException();

            _enumerated = true;
            return enumerable.GetAsyncEnumerator(cancellationToken);
        }
    }

    [Fact]
    public void Enumerable_MultipleEnumerations_ShouldEnumerateOnce()
    {
        var enumerable = new SingleEnumerable<int>(Enumerable.Range(1, 3));
        using var cachedEnumerable = CachedEnumerable.Create(enumerable);
        Assert.Equal([1, 2, 3], cachedEnumerable);
        Assert.Equal([1, 2, 3], cachedEnumerable);
    }

    [Fact]
    public void Enumerable_MultipleConcurrentEnumerations_ShouldEnumerateOnce()
    {
        // Arrange
        var count = 0;
        using var cachedEnumerable = CachedEnumerable.Create(new SingleEnumerable<int>(GetData()));

        IEnumerable<int> GetData()
        {
            yield return ++count;
            yield return ++count;
            yield return ++count;
        }

        var enumerator1 = cachedEnumerable.GetEnumerator();
        var enumerator2 = cachedEnumerable.GetEnumerator();
        Assert.True(enumerator1.MoveNext());
        Assert.Equal(1, enumerator1.Current);
        Assert.Equal(1, count);
        Assert.True(enumerator2.MoveNext());
        Assert.Equal(1, enumerator2.Current);
        Assert.Equal(1, count);
        Assert.True(enumerator2.MoveNext());
        Assert.Equal(2, enumerator2.Current);
        Assert.Equal(2, count);
        Assert.True(enumerator2.MoveNext());
        Assert.Equal(3, enumerator2.Current);
        Assert.Equal(3, count);
        Assert.True(enumerator1.MoveNext());
        Assert.Equal(2, enumerator1.Current);
        Assert.Equal(3, count);
        Assert.False(enumerator2.MoveNext());
        Assert.True(enumerator1.MoveNext());
        Assert.False(enumerator1.MoveNext());
        Assert.Equal(3, count);
        Assert.Equal([1, 2, 3], cachedEnumerable);
    }

    [Fact]
    public async Task Enumerable_MultipleConcurrentEnumerations_ShouldEnumerateOnce_ThreadSafe()
    {
        // Arrange
        var maxCount = 1000;
        var threadCount = 16;
        using var resetEvent = new ManualResetEventSlim(initialState: false);

        var count = 0;
        using var cachedEnumerable = CachedEnumerable.Create(new SingleEnumerable<int>(GetData()));

        IEnumerable<int> GetData()
        {
            for (var i = 0; i < maxCount; i++)
            {
                yield return ++count;
            }
        }

        var results = new List<int>[1000];
        var task = Task.Run(() => Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, i =>
        {
            results[i] = cachedEnumerable.ToList();
        }));

        resetEvent.Set();
        await task;
        Assert.Equal(maxCount, count);
        foreach (var result in results)
        {
            Assert.Equal(Enumerable.Range(1, maxCount), result);
        }
    }

    [Fact]
    public async Task AsyncEnumerable_MultipleEnumerations_ShouldEnumerateOnce()
    {
        var enumerable = new SingleAsyncEnumerable<int>(RangeAsync(1, 3));
        await using var cachedEnumerable = CachedEnumerable.Create(enumerable);
        Assert.Equal([1, 2, 3], cachedEnumerable);
        Assert.Equal([1, 2, 3], cachedEnumerable);
    }

    [Fact]
    public async Task AsyncEnumerable_MultipleConcurrentEnumerations_ShouldEnumerateOnce()
    {
        // Arrange
        var count = 0;
        await using var cachedEnumerable = CachedEnumerable.Create(new SingleAsyncEnumerable<int>(GetData()));

        async IAsyncEnumerable<int> GetData()
        {
            yield return ++count;
            await Task.Yield();
            yield return ++count;
            await Task.Yield();
            yield return ++count;
        }

        var enumerator1 = cachedEnumerable.GetAsyncEnumerator();
        var enumerator2 = cachedEnumerable.GetAsyncEnumerator();
        Assert.True(await enumerator1.MoveNextAsync());
        Assert.Equal(1, enumerator1.Current);
        Assert.Equal(1, count);
        Assert.True(await enumerator2.MoveNextAsync());
        Assert.Equal(1, enumerator2.Current);
        Assert.Equal(1, count);
        Assert.True(await enumerator2.MoveNextAsync());
        Assert.Equal(2, enumerator2.Current);
        Assert.Equal(2, count);
        Assert.True(await enumerator2.MoveNextAsync());
        Assert.Equal(3, enumerator2.Current);
        Assert.Equal(3, count);
        Assert.True(await enumerator1.MoveNextAsync());
        Assert.Equal(2, enumerator1.Current);
        Assert.Equal(3, count);
        Assert.False(await enumerator2.MoveNextAsync());
        Assert.True(await enumerator1.MoveNextAsync());
        Assert.False(await enumerator1.MoveNextAsync());
        Assert.Equal(3, count);
        Assert.Equal([1, 2, 3], cachedEnumerable);
    }

    [Fact]
    public async Task AsyncEnumerable_MultipleConcurrentEnumerations_ShouldEnumerateOnce_ThreadSafe()
    {
        // Arrange
        var maxCount = 1000;
        var threadCount = 16;
        using var resetEvent = new ManualResetEventSlim(initialState: false);

        var count = 0;
        await using var cachedEnumerable = CachedEnumerable.Create(new SingleAsyncEnumerable<int>(GetData()));

        async IAsyncEnumerable<int> GetData()
        {
            for (var i = 0; i < maxCount; i++)
            {
                yield return ++count;
                await Task.Yield();
            }
        }

        var results = new List<int>[1000];
        var task = Task.Run(() => Parallel.ForAsync(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, async (i, cancellationToken) =>
        {
            results[i] = await cachedEnumerable.ToListAsync(cancellationToken);
        }));

        resetEvent.Set();
        await task;
        Assert.Equal(maxCount, count);
        foreach (var result in results)
        {
            Assert.Equal(Enumerable.Range(1, maxCount), result);
        }
    }

    private static async IAsyncEnumerable<int> RangeAsync(int start, int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return start + i;
            await Task.Yield();
        }
    }
}
