using Meziantou.Framework.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections;

public sealed class AppendOnlyCollectionTests
{
    [Fact]
    public void Test()
    {
        var collection = new AppendOnlyCollection<int>();
        Assert.Empty(collection);

        collection.Add(0);
        Assert.Equal(new int[] { 0, }, collection);

        collection.Add(1);
        Assert.Equal(new int[] { 0, 1 }, collection);

        for (var i = 2; i < 10_000; i++)
        {
            collection.Add(i);
        }

        Assert.Equal(10000, collection.Count);
        Assert.Equal(0, collection[0]);
        Assert.Equal(1000, collection[1000]);
    }
}
