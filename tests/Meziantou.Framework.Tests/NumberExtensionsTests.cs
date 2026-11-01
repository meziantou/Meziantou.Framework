namespace Meziantou.Framework.Tests;

public sealed class NumberExtensionsTests
{
    [Theory]
    [InlineData(5, -1, -5)]
    [InlineData(-5, 3, 5)]
    [InlineData(5, 3, 5)]
    [InlineData(-5, -3, -5)]
    [InlineData(5, 0, 5)]
    [InlineData(-5, 0, 5)]
    [InlineData(0, -1, 0)]
    [InlineData(int.MinValue, -1, int.MinValue)]
    [InlineData(int.MaxValue, -1, -int.MaxValue)]
    public void MakeSameSignAs_Int32(int number, int sign, int expected)
    {
        Assert.Equal(expected, number.MakeSameSignAs(sign));
        Assert.Equal((long)expected, ((long)number).MakeSameSignAs(sign));
        Assert.Equal((decimal)expected, ((decimal)number).MakeSameSignAs(sign));
        Assert.Equal((double)expected, ((double)number).MakeSameSignAs(sign));
        Assert.Equal((float)expected, ((float)number).MakeSameSignAs(sign));
    }

    [Fact]
    public void MakeSameSignAs_Int64MinValue()
    {
        Assert.Equal(long.MinValue, long.MinValue.MakeSameSignAs(-1));
    }

    [Fact]
    public void MakeSameSignAs_MinValueWithPositiveSignOverflows()
    {
        Assert.Throws<OverflowException>(() => int.MinValue.MakeSameSignAs(1));
        Assert.Throws<OverflowException>(() => long.MinValue.MakeSameSignAs(1));
    }

    [Fact]
    public void MakeSameSignAs_NegativeZero()
    {
        Assert.Equal(-5m, 5m.MakeSameSignAs(decimal.Negate(0m)));
        Assert.Equal(-5d, 5d.MakeSameSignAs(-0d));
        Assert.Equal(-5f, 5f.MakeSameSignAs(-0f));
    }
}
