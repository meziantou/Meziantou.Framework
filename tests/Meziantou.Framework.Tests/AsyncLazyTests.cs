using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class AsyncLazyTests
    {
        [Fact]
        public async Task GetValueAsync()
        {
            var count = 0;
            using var lazy = AsyncLazy.Create(async () =>
            {
                Interlocked.Increment(ref count);
                await Task.Yield();
                return 1;
            });

            var a = lazy.GetValueAsync();
            var value = await lazy.GetValueAsync().ConfigureAwait(false);
            await a.ConfigureAwait(false);

            Assert.Equal(1, value);
            Assert.Equal(1, count);
        }
    }
}
