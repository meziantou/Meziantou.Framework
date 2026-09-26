using System.Linq;
using Tomlyn.Serialization.Internal;

namespace Tomlyn.Tests;

public class PooledArrayBuilderTests
{
    [Fact]
    public void ToArrayAndReturn_EmptyBuilder_ReturnsEmptyArrayAndResetsCount()
    {
        var builder = new PooledArrayBuilder<int>(initialCapacity: 4);

        var result = builder.ToArrayAndReturn();

        Assert.Empty(result);
        Assert.Equal(0, builder.Count);
    }

    [Fact]
    public void Add_GrowingBeyondInitialCapacity_PreservesOrderAndCount()
    {
        var builder = new PooledArrayBuilder<int>(initialCapacity: 2);

        for (var value = 1; value <= 33; value++)
        {
            builder.Add(value);
            Assert.Equal(value, builder.Count);
        }

        var result = builder.ToArrayAndReturn();

        Assert.Equal(Enumerable.Range(1, 33).ToArray(), result);
        Assert.Equal(0, builder.Count);
    }

    [Fact]
    public void ToArrayAndReturn_AfterReuse_ReturnsOnlyNewValues()
    {
        var builder = new PooledArrayBuilder<int>(initialCapacity: 1);
        builder.Add(1);
        builder.Add(2);

        var first = builder.ToArrayAndReturn();

        builder.Add(3);
        builder.Add(4);
        var second = builder.ToArrayAndReturn();

        Assert.Equal(new[] { 1, 2 }, first);
        Assert.Equal(new[] { 3, 4 }, second);
    }

    [Fact]
    public void Add_AfterDispose_UsesFreshStorage()
    {
        var builder = new PooledArrayBuilder<string>(initialCapacity: 1);
        builder.Add("alpha");
        builder.Add("beta");

        builder.Dispose();
        builder.Add("gamma");

        var result = builder.ToArrayAndReturn();

        Assert.Equal(0, builder.Count);
        Assert.Equal(new[] { "gamma" }, result);
    }
}
