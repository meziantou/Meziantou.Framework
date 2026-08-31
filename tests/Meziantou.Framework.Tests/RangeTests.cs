namespace Meziantou.Framework.Tests;

public class RangeTests
{
    [Theory]
    [InlineData(0, 10, 5, true)]
    [InlineData(0, 10, 0, true)]
    [InlineData(0, 10, 10, true)]
    [InlineData(0, 10, 11, false)]
    [InlineData(0, 10, -1, false)]
    public void Range_IsInRangeInclusive_Value(int from, int to, int value, bool expectedValue)
    {
        var range = Range.Create(from, to);
        var result = range.IsInRangeInclusive(value);
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData(0, 10, 5, 8, true)]
    [InlineData(0, 10, 0, 10, true)]
    [InlineData(0, 10, 0, 5, true)]
    [InlineData(0, 10, 5, 10, true)]
    [InlineData(0, 10, 1, 11, false)]
    [InlineData(0, 10, -1, 4, false)]
    public void Range_IsInRangeInclusive_Range(int from1, int to1, int from2, int to2, bool expectedValue)
    {
        var range1 = Range.Create(from1, to1);
        var range2 = Range.Create(from2, to2);
        var result = range1.IsInRangeInclusive(range2);
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData(0, 10, 5, true)]
    [InlineData(0, 10, 0, false)]
    [InlineData(0, 10, 10, false)]
    [InlineData(0, 10, 11, false)]
    [InlineData(0, 10, -1, false)]
    public void Range_IsInRangeExclusive_Value(int from, int to, int value, bool expectedValue)
    {
        var range = Range.Create(from, to);
        var result = range.IsInRangeExclusive(value);
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData(0, 10, 5, 8, true)]
    [InlineData(0, 10, 0, 10, false)]
    [InlineData(0, 10, 0, 5, false)]
    [InlineData(0, 10, 5, 10, false)]
    [InlineData(0, 10, 1, 11, false)]
    [InlineData(0, 10, -1, 4, false)]
    public void Range_IsInRangeExclusive_Range(int from1, int to1, int from2, int to2, bool expectedValue)
    {
        var range1 = Range.Create(from1, to1);
        var range2 = Range.Create(from2, to2);
        var result = range1.IsInRangeExclusive(range2);
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData(0, 10, 5, true)]
    [InlineData(0, 10, 0, true)]
    [InlineData(0, 10, 10, false)]
    [InlineData(0, 10, 11, false)]
    [InlineData(0, 10, -1, false)]
    public void Range_IsInRangeLowerInclusive_Value(int from, int to, int value, bool expectedValue)
    {
        var range = Range.Create(from, to);
        var result = range.IsInRangeLowerInclusive(value);
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData(0, 10, 5, 8, true)]
    [InlineData(0, 10, 0, 10, false)]
    [InlineData(0, 10, 0, 5, true)]
    [InlineData(0, 10, 5, 10, false)]
    [InlineData(0, 10, 1, 11, false)]
    [InlineData(0, 10, -1, 4, false)]
    public void Range_IsInRangeLowerInclusive_Range(int from1, int to1, int from2, int to2, bool expectedValue)
    {
        var range1 = Range.Create(from1, to1);
        var range2 = Range.Create(from2, to2);
        var result = range1.IsInRangeLowerInclusive(range2);
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData(0, 10, 5, true)]
    [InlineData(0, 10, 0, false)]
    [InlineData(0, 10, 10, true)]
    [InlineData(0, 10, 11, false)]
    [InlineData(0, 10, -1, false)]
    public void Range_IsInRangeUpperInclusive_Value(int from, int to, int value, bool expectedValue)
    {
        var range = Range.Create(from, to);
        var result = range.IsInRangeUpperInclusive(value);
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData(0, 10, 5, 8, true)]
    [InlineData(0, 10, 0, 10, false)]
    [InlineData(0, 10, 0, 5, false)]
    [InlineData(0, 10, 5, 10, true)]
    [InlineData(0, 10, 1, 11, false)]
    [InlineData(0, 10, -1, 4, false)]
    public void Range_IsInRangeUpperInclusive_Range(int from1, int to1, int from2, int to2, bool expectedValue)
    {
        var range1 = Range.Create(from1, to1);
        var range2 = Range.Create(from2, to2);
        var result = range1.IsInRangeUpperInclusive(range2);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void Equals_IsSymmetricWhenABoundIsNull()
    {
        var withNullFrom = new Range<string>(from: null!, to: "b");
        var withFrom = new Range<string>("a", "b");

        Assert.Equal(withNullFrom.Equals(withFrom), withFrom.Equals(withNullFrom));
        Assert.False(withNullFrom.Equals(withFrom));
        Assert.False(withFrom.Equals(withNullFrom));
    }

    [Fact]
    public void Equals_TwoRangesWithTheSameNullBoundAreEqual()
    {
        var first = new Range<string>(from: null!, to: "b");
        var second = new Range<string>(from: null!, to: "b");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_EqualRangesShareAHashCode()
    {
        var first = new Range<int>(1, 10);
        var second = new Range<int>(1, 10);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentRangesAreNotEqual()
    {
        Assert.NotEqual(new Range<int>(1, 10), new Range<int>(1, 11));
        Assert.NotEqual(new Range<int>(1, 10), new Range<int>(2, 10));
    }

    [Fact]
    public void CanBeUsedAsADictionaryKey()
    {
        var dictionary = new Dictionary<Range<string>, int>
        {
            [new Range<string>(from: null!, to: "b")] = 1,
            [new Range<string>("a", "b")] = 2,
        };

        Assert.Equal(2, dictionary.Count);
        Assert.Equal(1, dictionary[new Range<string>(from: null!, to: "b")]);
        Assert.Equal(2, dictionary[new Range<string>("a", "b")]);
    }
}
