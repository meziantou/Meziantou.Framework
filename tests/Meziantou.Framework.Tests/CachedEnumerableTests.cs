using System.Collections;
using System.Runtime.CompilerServices;

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
    public void CachedEnumerable_Create_Example()
    {
        #region CachedEnumerable_Create
        var enumerationCount = 0;
        IEnumerable<int> GetExpensiveEnumerable()
        {
            enumerationCount++;
            yield return 1;
            yield return 2;
        }

        using var cached = CachedEnumerable.Create(GetExpensiveEnumerable());
        Assert.Equal([1, 2], cached); // First enumeration
        Assert.Equal([1, 2], cached); // Uses cached results
        Assert.Equal(1, enumerationCount);
        #endregion
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
    public void Enumerable_MultipleConcurrentEnumerations_ShouldEnumerateOnce_ThreadSafe()
    {
        // Arrange
        var maxCount = 1000;
        var threadCount = 16;

        var count = 0;
        using var cachedEnumerable = CachedEnumerable.Create(new SingleEnumerable<int>(GetData()));

        IEnumerable<int> GetData()
        {
            for (var i = 0; i < maxCount; i++)
            {
                yield return ++count;
            }
        }

        var results = new List<int>[10_000];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, i =>
        {
            results[i] = cachedEnumerable.ToList();
        });

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
        await Assert.Equal([1, 2, 3], cachedEnumerable);
        await Assert.Equal([1, 2, 3], cachedEnumerable);
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
        await Assert.Equal([1, 2, 3], cachedEnumerable);
    }

    [Fact]
    public async Task AsyncEnumerable_MultipleConcurrentEnumerations_ShouldEnumerateOnce_ThreadSafe()
    {
        // Arrange
        var maxCount = 1000;
        var threadCount = 16;

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

        var results = new List<int>[10_000];
        await Parallel.ForAsync(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, async (i, cancellationToken) =>
        {
            results[i] = await cachedEnumerable.ToListAsync(cancellationToken);
        });

        Assert.Equal(maxCount, count);
        foreach (var result in results)
        {
            Assert.Equal(Enumerable.Range(1, maxCount), result);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsyncEnumerable_DoesNotYieldATrailingDefaultItem(bool threadSafe)
    {
        await using var cachedEnumerable = CachedEnumerable.Create(new SingleAsyncEnumerable<int>(RangeAsync(1, 2)), threadSafe);

        await Assert.Equal([1, 2], cachedEnumerable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsyncEnumerable_EmptySourceYieldsNothing(bool threadSafe)
    {
        await using var cachedEnumerable = CachedEnumerable.Create(new SingleAsyncEnumerable<int>(RangeAsync(0, 0)), threadSafe);

        await Assert.Equal([], cachedEnumerable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Enumerable_MultipleEnumerations_ShouldEnumerateOnce_ExplicitThreadSafety(bool threadSafe)
    {
        using var cachedEnumerable = CachedEnumerable.Create(new SingleEnumerable<int>([1, 2, 3]), threadSafe);

        Assert.Equal([1, 2, 3], cachedEnumerable);
        Assert.Equal([1, 2, 3], cachedEnumerable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Enumerable_EnumerateAfterDispose_Throws(bool threadSafe)
    {
        var cachedEnumerable = CachedEnumerable.Create(new SingleEnumerable<int>(Enumerable.Range(1, 3)), threadSafe);
        using (var enumerator = cachedEnumerable.GetEnumerator())
        {
            Assert.True(enumerator.MoveNext());
        }

        cachedEnumerable.Dispose();
        cachedEnumerable.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cachedEnumerable.ToList());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Enumerable_SourceException_IsRethrownByLaterEnumerations(bool threadSafe)
    {
        var enumerationCount = 0;
        IEnumerable<int> GetData()
        {
            enumerationCount++;
            yield return 1;
            throw new InvalidOperationException("boom");
        }

        using var cachedEnumerable = CachedEnumerable.Create(GetData(), threadSafe);

        Assert.Equal("boom", Assert.Throws<InvalidOperationException>(() => cachedEnumerable.ToList()).Message);
        Assert.Equal("boom", Assert.Throws<InvalidOperationException>(() => cachedEnumerable.ToList()).Message);
        Assert.Equal(1, cachedEnumerable.First());
        Assert.Equal(1, enumerationCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsyncEnumerable_EnumerateAfterDispose_Throws(bool threadSafe)
    {
        var cachedEnumerable = CachedEnumerable.Create(new SingleAsyncEnumerable<int>(RangeAsync(1, 3)), threadSafe);
        await using (var enumerator = cachedEnumerable.GetAsyncEnumerator())
        {
            Assert.True(await enumerator.MoveNextAsync());
        }

        await cachedEnumerable.DisposeAsync();
        await cachedEnumerable.DisposeAsync();

        await Assert.Throws<ObjectDisposedException>(async () => { await cachedEnumerable.ToListAsync(); });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsyncEnumerable_SourceException_IsRethrownByLaterEnumerations(bool threadSafe)
    {
        var enumerationCount = 0;
        async IAsyncEnumerable<int> GetData()
        {
            enumerationCount++;
            yield return 1;
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }

        await using var cachedEnumerable = CachedEnumerable.Create(GetData(), threadSafe);

        Assert.Equal("boom", (await Assert.Throws<InvalidOperationException>(async () => { await cachedEnumerable.ToListAsync(); })).Message);
        Assert.Equal("boom", (await Assert.Throws<InvalidOperationException>(async () => { await cachedEnumerable.ToListAsync(); })).Message);
        Assert.Equal(1, await cachedEnumerable.FirstAsync());
        Assert.Equal(1, enumerationCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsyncEnumerable_CancellingAConsumer_DoesNotAffectTheOtherConsumers(bool threadSafe)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceToken = CancellationToken.None;
        async IAsyncEnumerable<int> GetData([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            sourceToken = cancellationToken;
            yield return 1;
            await gate.Task;
            yield return 2;
        }

        await using var cachedEnumerable = CachedEnumerable.Create(new SingleAsyncEnumerable<int>(GetData()), threadSafe);

        using var cts = new CancellationTokenSource();
        await using (var enumerator = cachedEnumerable.GetAsyncEnumerator(cts.Token))
        {
            Assert.True(await enumerator.MoveNextAsync());
            var moveNext = enumerator.MoveNextAsync().AsTask();

            // The consumer stops waiting for the source as soon as its token is cancelled
            await cts.CancelAsync();
            await Assert.ThrowsAny<OperationCanceledException>(() => moveNext);
        }

        // The source does not use the token of the first consumer
        Assert.False(sourceToken.IsCancellationRequested);

        gate.SetResult();
        await Assert.Equal([1, 2], cachedEnumerable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsyncEnumerable_Dispose_CancelsThePendingSourceOperation(bool threadSafe)
    {
        var sourceCancelled = false;
        async IAsyncEnumerable<int> GetData([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return 1;
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            finally
            {
                sourceCancelled = cancellationToken.IsCancellationRequested;
            }

            yield return 2;
        }

        var cachedEnumerable = CachedEnumerable.Create(GetData(), threadSafe);
        using var cts = new CancellationTokenSource();
        await using (var enumerator = cachedEnumerable.GetAsyncEnumerator(cts.Token))
        {
            Assert.True(await enumerator.MoveNextAsync());
            var moveNext = enumerator.MoveNextAsync().AsTask();
            await cts.CancelAsync();
            await Assert.ThrowsAny<OperationCanceledException>(() => moveNext);
        }

        await cachedEnumerable.DisposeAsync();
        Assert.True(sourceCancelled);
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
