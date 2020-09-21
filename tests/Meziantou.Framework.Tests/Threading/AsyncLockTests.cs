using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Threading.Tests
{
    public class AsyncLockTests
    {
        [Fact]
        public async Task Lock()
        {
            var asyncLock = new AsyncLock();
            for (var i = 0; i < 2; i++)
            {
                using (await asyncLock.LockAsync().ConfigureAwait(false))
                {
                    if (asyncLock.TryLock(out var lockObject))
                    {
                        Assert.True(false, "Should not be able to acquire the lock");
                    }
                }
            }
        }
    }
}
