using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class ArrayExtensionsTests
{
    [Fact]
    public void CountTest()
    {
        var array = new[] { 1, 2, 3 };
        Assert.Equal(3, array.Count);
        Assert.Equal(3, array.LongCount);
    }
}
