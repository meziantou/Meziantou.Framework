using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class AsyncEnumerableExtensionsTests
    {
        [Fact]
        public async Task AnyAsyncTest()
        {
            Assert.False(await CreateEnumerable<int>().AnyAsync());
            Assert.False(await CreateEnumerable(1, 2, 3).AnyAsync(item => item == 4));
            Assert.True(await CreateEnumerable(1, 2, 3).AnyAsync());
            Assert.True(await CreateEnumerable(1, 2, 3).AnyAsync(item => item == 2));
        }

        [Fact]
        public async Task ContainsAsyncTest()
        {
            Assert.False(await CreateEnumerable<int>().ContainsAsync(1));
            Assert.False(await CreateEnumerable(1, 2, 3).ContainsAsync(4));
            Assert.True(await CreateEnumerable(1, 2, 3).ContainsAsync(2));
            Assert.True(await CreateEnumerable("A").ContainsAsync("a", StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task CountAsyncTest()
        {
            Assert.Equal(0, await CreateEnumerable<int>().CountAsync());
            Assert.Equal(0, await CreateEnumerable(1, 2, 3).CountAsync(item => item == 4));
            Assert.Equal(3, await CreateEnumerable(1, 2, 3).CountAsync());
            Assert.Equal(2, await CreateEnumerable(1, 2, 3).CountAsync(item => item >= 2));

            Assert.Equal(0L, await CreateEnumerable<int>().LongCountAsync());
            Assert.Equal(0L, await CreateEnumerable(1, 2, 3).LongCountAsync(item => item == 4));
            Assert.Equal(3L, await CreateEnumerable(1, 2, 3).LongCountAsync());
            Assert.Equal(2L, await CreateEnumerable(1, 2, 3).LongCountAsync(item => item >= 2));
        }

        [Fact]
        public async Task DistinctAsyncTest()
        {
            Assert.Equal(Array.Empty<int>(), await CreateEnumerable<int>().DistinctAsync().ToListAsync());
            Assert.Equal(new[] { 1, 2 }, await CreateEnumerable(1, 2, 1, 1, 2).DistinctAsync().ToListAsync());
            Assert.Equal(new[] { "a", "B" }, await CreateEnumerable("a", "A", "B", "b", "b").DistinctAsync(StringComparer.OrdinalIgnoreCase).ToListAsync());
        }

        [Fact]
        public async Task DistinctByAsyncTest()
        {
            Assert.Equal(Array.Empty<Dummy>(), await CreateEnumerable<Dummy>().DistinctByAsync(item => item.Value).ToListAsync());
            Assert.Equal(new[] { new Dummy("a"), new Dummy("B") }, await CreateEnumerable("a", "A", "B", "b", "b").SelectAsync(item => new Dummy(item)).DistinctByAsync(item => item.Value.ToUpperInvariant()).ToListAsync());
        }

        [Fact]
        public async Task FirstAsyncTest()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await CreateEnumerable<int>().FirstAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await CreateEnumerable(1, 2).FirstAsync(item => item == 3));

            Assert.Equal(0, await CreateEnumerable<int>().FirstOrDefaultAsync());
            Assert.Equal(1, await CreateEnumerable(1, 2, 3).FirstOrDefaultAsync());
            Assert.Equal(2, await CreateEnumerable(1, 2, 3).FirstOrDefaultAsync(i => i == 2));
        }

        [Fact]
        public async Task LastAsyncTest()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await CreateEnumerable<int>().LastAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await CreateEnumerable(1, 2).LastAsync(item => item == 3));

            Assert.Equal(0, await CreateEnumerable<int>().LastOrDefaultAsync());
            Assert.Equal(3, await CreateEnumerable(1, 2, 3).LastOrDefaultAsync());
            Assert.Equal(2, await CreateEnumerable(1, 2, 3).LastOrDefaultAsync(i => i == 2));
        }

        [Fact]
        public async Task WhereAsyncTest()
        {
            Assert.Equal(new[] { 1, 2 }, await CreateEnumerable(1, 2, 3, 4).WhereAsync(item => item < 3).ToListAsync());
            Assert.Equal(new[] { "a", "", " ", "A", "b" }, await CreateEnumerable("a", null, "", " ", "A", "b").WhereNotNull().ToListAsync());
            Assert.Equal(new[] { "a", " ", "A", "b" }, await CreateEnumerable("a", null, "", " ", "A", "b").WhereNotNullOrEmpty().ToListAsync());
            Assert.Equal(new[] { "a", "A", "b" }, await CreateEnumerable("a", null, "", " ", "A", "b").WhereNotNullOrWhiteSpace().ToListAsync());
        }

        [Fact]
        public async Task SkipAsyncTest()
        {
            Assert.Equal(new[] { 2, 3, 4 }, await CreateEnumerable(0, 1, 2, 3, 4).SkipAsync(2).ToListAsync());
            Assert.Equal(new[] { 2, 3, 4 }, await CreateEnumerable(0, 1, 2, 3, 4).SkipWhileAsync(item => item < 2).ToListAsync());
        }

        [Fact]
        public async Task TakeAsyncTest()
        {
            Assert.Equal(new[] { 0, 1, 2 }, await CreateEnumerable(0, 1, 2, 3, 4).TakeAsync(3).ToListAsync());
            Assert.Equal(new[] { 0, 1, 2 }, await CreateEnumerable(0, 1, 2, 3, 4).TakeWhileAsync(item => item < 3).ToListAsync());
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
