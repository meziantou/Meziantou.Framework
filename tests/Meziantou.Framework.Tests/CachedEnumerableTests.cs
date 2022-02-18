using System.Collections;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class CachedEnumerableTests
{
    private sealed class SingleEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _enumerable;
        private bool _enumerated;

        public SingleEnumerable(IEnumerable<T> enumerable)
        {
            _enumerable = enumerable;
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (_enumerated)
                throw new InvalidOperationException();

            _enumerated = true;

            return _enumerable.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void MultipleEnumerations_ShouldEnumerateOnce()
    {
        var enumerable = new SingleEnumerable<int>(Enumerable.Range(1, 3));
        var cachedEnumerable = CachedEnumerable.Create(enumerable);

        cachedEnumerable.Should().Equal(new[] { 1, 2, 3 });
        cachedEnumerable.Should().Equal(new[] { 1, 2, 3 });
    }

    [Fact]
    public void MultipleConcurrentEnumerations_ShouldEnumerateOnce()
    {
        // Arrange
        var count = 0;
        var cachedEnumerable = CachedEnumerable.Create(new SingleEnumerable<int>(GetData()));

        IEnumerable<int> GetData()
        {
            yield return ++count;
            yield return ++count;
            yield return ++count;
        }

        var enumerator1 = cachedEnumerable.GetEnumerator();
        var enumerator2 = cachedEnumerable.GetEnumerator();

        // Act & assert
        enumerator1.MoveNext().Should().BeTrue();
        enumerator1.Current.Should().Be(1);
        count.Should().Be(1);

        enumerator2.MoveNext().Should().BeTrue();
        enumerator2.Current.Should().Be(1);
        count.Should().Be(1);

        enumerator2.MoveNext().Should().BeTrue();
        enumerator2.Current.Should().Be(2);
        count.Should().Be(2);

        enumerator2.MoveNext().Should().BeTrue();
        enumerator2.Current.Should().Be(3);
        count.Should().Be(3);

        enumerator1.MoveNext().Should().BeTrue();
        enumerator1.Current.Should().Be(2);
        count.Should().Be(3);

        enumerator2.MoveNext().Should().BeFalse();
        enumerator1.MoveNext().Should().BeTrue();
        enumerator1.MoveNext().Should().BeFalse();
        count.Should().Be(3);

        cachedEnumerable.Should().Equal(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task MultipleConcurrentEnumerations_ShouldEnumerateOnce_ThreadSafe()
    {
        // Arrange
        var maxCount = 1000;
        var threadCount = 16;
        var resetEvent = new ManualResetEventSlim(initialState: false);

        var count = 0;
        var cachedEnumerable = CachedEnumerable.Create(new SingleEnumerable<int>(GetData()));

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

        // Act & assert
        count.Should().Be(maxCount);
        foreach (var result in results)
        {
            result.Should().Equal(Enumerable.Range(1, maxCount));
        }
    }
}
