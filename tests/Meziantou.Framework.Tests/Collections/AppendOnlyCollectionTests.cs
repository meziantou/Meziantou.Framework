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

    [Fact]
    public void CopyTo_CopiesAllItems()
    {
        ICollection<int> collection = new AppendOnlyCollection<int>(2);
        for (var i = 0; i < 10; i++)
        {
            collection.Add(i);
        }

        var array = Enumerable.Repeat(-1, 12).ToArray();
        collection.CopyTo(array, 1);

        Assert.Equal([-1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, -1], array);
    }

    [Fact]
    public void CopyTo_ThrowsIfArrayIsNull()
    {
        ICollection<int> collection = new AppendOnlyCollection<int>();
        Assert.Throws<ArgumentNullException>(() => collection.CopyTo(null!, 0));
    }

    [Fact]
    public void CopyTo_ThrowsIfIndexIsNegative()
    {
        ICollection<int> collection = new AppendOnlyCollection<int>();
        Assert.Throws<ArgumentOutOfRangeException>(() => collection.CopyTo([], -1));
    }

    [Fact]
    public void CopyTo_ThrowsIfIndexIsGreaterThanArrayLength()
    {
        ICollection<int> collection = new AppendOnlyCollection<int>();
        Assert.Throws<ArgumentOutOfRangeException>(() => collection.CopyTo([], 1));
    }

    [Fact]
    public void CopyTo_ThrowsIfArrayDoesNotHaveEnoughSpace()
    {
        ICollection<int> collection = new AppendOnlyCollection<int>();
        collection.Add(0);
        Assert.Throws<ArgumentException>(() => collection.CopyTo([], 0));
    }
}
