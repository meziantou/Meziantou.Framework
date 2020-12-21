using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Tests
{
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

            Assert.Equal(new[] { 1, 2, 3 }, cachedEnumerable);
            Assert.Equal(new[] { 1, 2, 3 }, cachedEnumerable);
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

            Assert.Equal(new[] { 1, 2, 3 }, cachedEnumerable);
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
            Assert.Equal(maxCount, count);
            foreach (var result in results)
            {
                Assert.Equal(Enumerable.Range(1, maxCount), result);
            }
        }
    }
}
