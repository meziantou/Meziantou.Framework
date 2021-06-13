using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class AsyncEnumerableExtensionsTests
    {
        [Fact]
        public async Task AnyAsyncTest()
        {
            (await CreateEnumerable<int>().AnyAsync()).Should().BeFalse();
            (await CreateEnumerable(1, 2, 3).AnyAsync(item => item == 4)).Should().BeFalse();
            (await CreateEnumerable(1, 2, 3).AnyAsync()).Should().BeTrue();
            (await CreateEnumerable(1, 2, 3).AnyAsync(item => item == 2)).Should().BeTrue();
        }

        [Fact]
        public async Task ContainsAsyncTest()
        {
            (await CreateEnumerable<int>().ContainsAsync(1)).Should().BeFalse();
            (await CreateEnumerable(1, 2, 3).ContainsAsync(4)).Should().BeFalse();
            (await CreateEnumerable(1, 2, 3).ContainsAsync(2)).Should().BeTrue();
            (await CreateEnumerable("A").ContainsAsync("a", StringComparer.OrdinalIgnoreCase)).Should().BeTrue();
        }

        [Fact]
        public async Task CountAsyncTest()
        {
            (await CreateEnumerable<int>().CountAsync()).Should().Be(0);
            (await CreateEnumerable(1, 2, 3).CountAsync(item => item == 4)).Should().Be(0);
            (await CreateEnumerable(1, 2, 3).CountAsync()).Should().Be(3);
            (await CreateEnumerable(1, 2, 3).CountAsync(item => item >= 2)).Should().Be(2);

            (await CreateEnumerable<int>().LongCountAsync()).Should().Be(0L);
            (await CreateEnumerable(1, 2, 3).LongCountAsync(item => item == 4)).Should().Be(0L);
            (await CreateEnumerable(1, 2, 3).LongCountAsync()).Should().Be(3L);
            (await CreateEnumerable(1, 2, 3).LongCountAsync(item => item >= 2)).Should().Be(2L);
        }

        [Fact]
        public async Task DistinctAsyncTest()
        {
            (await CreateEnumerable<int>().DistinctAsync().ToListAsync()).Should().Equal(Array.Empty<int>());
            (await CreateEnumerable(1, 2, 1, 1, 2).DistinctAsync().ToListAsync()).Should().Equal(new[] { 1, 2 });
            (await CreateEnumerable("a", "A", "B", "b", "b").DistinctAsync(StringComparer.OrdinalIgnoreCase).ToListAsync()).Should().Equal(new[] { "a", "B" });
        }

        [Fact]
        public async Task DistinctByAsyncTest()
        {
            (await CreateEnumerable<Dummy>().DistinctByAsync(item => item.Value).ToListAsync()).Should().Equal(Array.Empty<Dummy>());
            (await CreateEnumerable("a", "A", "B", "b", "b").SelectAsync(item => new Dummy(item)).DistinctByAsync(item => item.Value.ToUpperInvariant()).ToListAsync()).Should().Equal(new[] { new Dummy("a"), new Dummy("B") });
        }

        [Fact]
        public async Task FirstAsyncTest()
        {
            await new Func<Task>(async () => await CreateEnumerable<int>().FirstAsync()).Should().ThrowExactlyAsync<InvalidOperationException>();
            await new Func<Task>(async () => await CreateEnumerable(1, 2).FirstAsync(item => item == 3)).Should().ThrowExactlyAsync<InvalidOperationException>();

            (await CreateEnumerable<int>().FirstOrDefaultAsync()).Should().Be(0);
            (await CreateEnumerable(1, 2, 3).FirstOrDefaultAsync()).Should().Be(1);
            (await CreateEnumerable(1, 2, 3).FirstOrDefaultAsync(i => i == 2)).Should().Be(2);
        }

        [Fact]
        public async Task LastAsyncTest()
        {
            await new Func<Task>(async () => await CreateEnumerable<int>().LastAsync()).Should().ThrowExactlyAsync<InvalidOperationException>();
            await new Func<Task>(async () => await CreateEnumerable(1, 2).LastAsync(item => item == 3)).Should().ThrowExactlyAsync<InvalidOperationException>();

            (await CreateEnumerable<int>().LastOrDefaultAsync()).Should().Be(0);
            (await CreateEnumerable(1, 2, 3).LastOrDefaultAsync()).Should().Be(3);
            (await CreateEnumerable(1, 2, 3).LastOrDefaultAsync(i => i == 2)).Should().Be(2);
        }

        [Fact]
        public async Task WhereAsyncTest()
        {
            (await CreateEnumerable(1, 2, 3, 4).WhereAsync(item => item < 3).ToListAsync()).Should().Equal(new[] { 1, 2 });
            (await CreateEnumerable("a", null, "", " ", "A", "b").WhereNotNull().ToListAsync()).Should().Equal(new[] { "a", "", " ", "A", "b" });
            (await CreateEnumerable("a", null, "", " ", "A", "b").WhereNotNullOrEmpty().ToListAsync()).Should().Equal(new[] { "a", " ", "A", "b" });
            (await CreateEnumerable("a", null, "", " ", "A", "b").WhereNotNullOrWhiteSpace().ToListAsync()).Should().Equal(new[] { "a", "A", "b" });
        }

        [Fact]
        public async Task SkipAsyncTest()
        {
            (await CreateEnumerable(0, 1, 2, 3, 4).SkipAsync(2).ToListAsync()).Should().Equal(new[] { 2, 3, 4 });
            (await CreateEnumerable(0, 1, 2, 3, 4).SkipWhileAsync(item => item < 2).ToListAsync()).Should().Equal(new[] { 2, 3, 4 });
        }

        [Fact]
        public async Task TakeAsyncTest()
        {
            (await CreateEnumerable(0, 1, 2, 3, 4).TakeAsync(3).ToListAsync()).Should().Equal(new[] { 0, 1, 2 });
            (await CreateEnumerable(0, 1, 2, 3, 4).TakeWhileAsync(item => item < 3).ToListAsync()).Should().Equal(new[] { 0, 1, 2 });
        }

        private record Dummy(string Value);

        private static async IAsyncEnumerable<T> CreateEnumerable<T>(params T[] items)
        {
            await Task.Yield();
            foreach (var item in items)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }
}
