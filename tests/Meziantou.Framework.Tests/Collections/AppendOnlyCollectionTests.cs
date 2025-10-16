using Meziantou.Framework.Collections;

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
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(i, collection[i]);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void TestWithInitialCapacity(int capacity)
    {
        var collection = new AppendOnlyCollection<int>(capacity);
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
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(i, collection[i]);
        }
    }
}
