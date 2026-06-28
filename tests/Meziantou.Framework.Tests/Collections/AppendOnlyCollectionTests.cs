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

        Assert.HasCount(10000, collection);
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

        Assert.HasCount(10000, collection);
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

    [Fact]
    public void Contains_ValueType_ReturnsFalseWhenNoMatch()
    {
        var collection = new AppendOnlyCollection<int>();
        collection.Add(1);
        collection.Add(2);

        // 0 is default(int): a buggy implementation that relies on `Find(...) is not null`
        // returns true here because default(int) boxes to a non-null value.
        Assert.False(collection.Contains(x => x == 0));
        Assert.True(collection.Contains(x => x == 1));
    }

    [Fact]
    public void Contains_ValueType_DefaultValuePresent_ReturnsTrue()
    {
        var collection = new AppendOnlyCollection<int>();
        collection.Add(0);

        Assert.True(collection.Contains(x => x == 0));
    }

    [Fact]
    public void ICollectionContains_ValueType()
    {
        ICollection<int> collection = new AppendOnlyCollection<int>();
        collection.Add(1);
        collection.Add(2);

        Assert.False(collection.Contains(0));
        Assert.True(collection.Contains(1));
        Assert.True(collection.Contains(2));
    }

    [Fact]
    public void TryFind_ValueType()
    {
        var collection = new AppendOnlyCollection<int>();
        collection.Add(1);
        collection.Add(2);

        Assert.True(collection.TryFind(x => x == 2, out var found));
        Assert.Equal(2, found);

        Assert.False(collection.TryFind(x => x == 0, out var notFound));
        Assert.Equal(0, notFound);
    }

    [Fact]
    public void Find_ReferenceType()
    {
        var collection = new AppendOnlyCollection<string>();
        collection.Add("a");
        collection.Add("b");

        Assert.Equal("b", collection.Find(x => x == "b"));
        Assert.Null(collection.Find(x => x == "c"));
    }

    [Fact]
    public async Task ConcurrentEnumerationWhileAppending_NeverThrowsAndSeesConsistentPrefix()
    {
        // Small initial capacity forces many segments to be created while reading,
        // exercising the segment-linking/enumeration race.
        var collection = new AppendOnlyCollection<int>(1);
        const int Count = 50_000;

        using var cts = new CancellationTokenSource();
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < Count; i++)
            {
                collection.Add(i);
            }
        });

        var readers = new Task[4];
        for (var r = 0; r < readers.Length; r++)
        {
            readers[r] = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var index = 0;
                    foreach (var item in collection)
                    {
                        // Items are added in order (0, 1, 2, ...). Any concurrent snapshot
                        // must therefore be a prefix of that sequence.
                        Assert.Equal(index, item);
                        index++;
                    }
                }
            });
        }

        await writer;
        cts.Cancel();
        await Task.WhenAll(readers);

        Assert.HasCount(Count, collection);
    }
}
