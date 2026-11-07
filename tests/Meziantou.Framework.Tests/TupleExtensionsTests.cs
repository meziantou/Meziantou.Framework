namespace Meziantou.Framework.Tests;

public sealed class TupleExtensionsTests
{
    [Fact]
    public void Documentation_Optional()
    {
        #region Optional_Usage
        var notSet = new Optional<string?>();
        var setToNull = new Optional<string?>(null);

        // Unlike null, Optional tells a missing value from a value that is null
        Assert.False(notSet.HasValue);
        Assert.True(setToNull.HasValue);
        Assert.NotEqual(notSet, setToNull);

        var (hasValue, value) = setToNull;
        Assert.True(hasValue);
        Assert.Null(value);
        #endregion
    }

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
