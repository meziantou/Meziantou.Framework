using FluentAssertions;
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

        set.Should().Contain(1);
        set.Should().NotContain(4);

        set.Should().HaveCount(3);
        set.Should().BeEquivalentTo([1, 2, 3]);

        set.Clear();
        Assert.Empty(set);

        set.AddRange(4, 5, 6);
        set.Should().BeEquivalentTo([4, 5, 6]);
    }
}
