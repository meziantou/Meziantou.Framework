using Xunit;

namespace Meziantou.Framework.Threading.Tests;

public sealed class KeyedAsyncLockTests
{
    [Fact]
    public async Task Test()
    {
        var locks = new KeyedAsyncLock<string>(StringComparer.Ordinal);
        using (await locks.LockAsync("a"))
        using (await locks.LockAsync("b"))
        {
            // If a and b are the same instance, this test should timeout
        }
    }
}
