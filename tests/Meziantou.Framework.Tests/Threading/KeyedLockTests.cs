using Meziantou.Framework.Threading;
using Xunit;

namespace Meziantou.Framework.Tests.Threading;

public sealed class KeyedLockTests
{
    [Fact]
    public void Test()
    {
        var locks = new KeyedLock<string>(StringComparer.Ordinal);
        using (locks.Lock("a"))
        using (locks.Lock("b"))
        {
            // If a and b are the same instance, this test should timeout
        }
    }
}
