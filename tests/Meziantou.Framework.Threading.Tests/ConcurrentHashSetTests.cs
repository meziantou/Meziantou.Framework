using Meziantou.Framework.Collections.Concurrent;
using Xunit;

namespace Meziantou.Framework.Tests.Collections;

public class ConcurrentHashSetTests
{
    [Fact]
    public void TestConcurrentHashSet()
    {
        ConcurrentHashSet<int> set = [];
        Assert.True(set.Add(1));
        Assert.True(set.Add(2));
        Assert.True(set.Add(3));
        Assert.False(set.Add(3));

        Assert.Contains(1, set);
        Assert.DoesNotContain(4, set);

        Assert.Equal(3, set.Count);
        Assert.Equal([1, 2, 3], set.Order());

        set.Clear();
        Assert.Empty(set);

        set.AddRange(4, 5, 6);
        Assert.Equal([4, 5, 6], set.Order());
    }
}
