using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class AsyncLazyTests
    {
        [TestMethod]
        public async Task GetValueAsync()
        {
            var count = 0;
            var lazy = AsyncLazy.Create(async () =>
            {
                Interlocked.Increment(ref count);
                await Task.Yield();
                return 1;
            });

            var a = lazy.GetValueAsync();
            var value = await lazy.GetValueAsync();

            Assert.AreEqual(1, value);
            Assert.AreEqual(1, count);
        }
    }
}
