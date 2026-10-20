namespace Meziantou.Framework.Tests;

public sealed class TupleExtensionsTests
{
    [Fact]
    public void ToArray_Example()
    {
        #region TupleExtensions_ToArray
        var tuple = (1, "hello", true);
        object?[] array = tuple.ToArray();
        Assert.Equal([1, "hello", true], array);
        #endregion
    }

    [Fact]
    public void ToArray_EmptyTuple()
    {
        Assert.Empty(ValueTuple.Create().ToArray());
    }
}
