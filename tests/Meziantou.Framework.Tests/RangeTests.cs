namespace Meziantou.Framework.Tests;

public class RangeTests
{
    [Fact]
    public void Documentation_Boundaries()
    {
        #region Range_Boundaries
        var range = Range.Create(1, 10);

        Assert.True(range.IsInRangeInclusive(10));       // 1 <= value <= 10
        Assert.False(range.IsInRangeExclusive(10));      // 1 < value < 10
        Assert.True(range.IsInRangeLowerInclusive(1));   // 1 <= value < 10
        Assert.False(range.IsInRangeLowerInclusive(10));
        Assert.True(range.IsInRangeUpperInclusive(10));  // 1 < value <= 10

        // A range is in another range when both of its bounds are
        Assert.True(range.IsInRangeInclusive(Range.Create(2, 5)));
        Assert.False(range.IsInRangeInclusive(Range.Create(5, 15)));
        #endregion
    }

    [Fact]
    public void Range_IsInRangeInclusive_Example()
    {
        #region Range_IsInRangeInclusive
        var range = new Range<int>(1, 10);
        Assert.True(range.IsInRangeInclusive(5));
        Assert.False(range.IsInRangeInclusive(15));
        #endregion
    }

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
        Assert.NotEqual(withFrom, withNullFrom);
        Assert.NotEqual(withNullFrom, withFrom);
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

        Assert.HasCount(2, dictionary);
        Assert.Equal(1, dictionary[new Range<string>(from: null!, to: "b")]);
        Assert.Equal(2, dictionary[new Range<string>("a", "b")]);
    }

    [Fact]
    public void Documentation_OverlapsAndIntersect()
    {
        #region Range_OverlapsAndIntersect
        var range = Range.Create(1, 10);

        Assert.True(range.Overlaps(Range.Create(5, 15)));
        Assert.True(range.Overlaps(Range.Create(10, 20))); // The bounds are inclusive
        Assert.False(range.Overlaps(Range.Create(11, 20)));

        Assert.Equal(Range.Create(5, 10), range.Intersect(Range.Create(5, 15)));
        Assert.Null(range.Intersect(Range.Create(11, 20)));
        #endregion
    }

    [Fact]
    public void Constructor_FromGreaterThanTo_Throws()
    {
        Assert.Throws<ArgumentException>("from", () => Range.Create(10, 1));
        Assert.Throws<ArgumentException>("from", () => new Range<string>("b", "a"));
    }

    [Fact]
    public void Constructor_FromEqualToTo()
    {
        var range = Range.Create(1, 1);
        Assert.True(range.IsInRangeInclusive(1));
    }

    [Theory]
    [InlineData(0, 10, 5, 8, true)]
    [InlineData(0, 10, -5, 0, true)]
    [InlineData(0, 10, 10, 15, true)]
    [InlineData(0, 10, -5, 15, true)]
    [InlineData(0, 10, -5, -1, false)]
    [InlineData(0, 10, 11, 15, false)]
    public void Overlaps(int from1, int to1, int from2, int to2, bool expected)
    {
        var range1 = Range.Create(from1, to1);
        var range2 = Range.Create(from2, to2);
        Assert.Equal(expected, range1.Overlaps(range2));
        Assert.Equal(expected, range2.Overlaps(range1));
    }

    [Theory]
    [InlineData(0, 10, 5, 8, 5, 8)]
    [InlineData(0, 10, -5, 0, 0, 0)]
    [InlineData(0, 10, 5, 15, 5, 10)]
    [InlineData(0, 10, -5, 15, 0, 10)]
    public void Intersect(int from1, int to1, int from2, int to2, int expectedFrom, int expectedTo)
    {
        var range1 = Range.Create(from1, to1);
        var range2 = Range.Create(from2, to2);
        Assert.Equal(Range.Create(expectedFrom, expectedTo), range1.Intersect(range2));
        Assert.Equal(Range.Create(expectedFrom, expectedTo), range2.Intersect(range1));
    }

    [Fact]
    public void OverlapsAndIntersect_NullBoundsAreOpen()
    {
        var unboundedLower = new Range<string>(from: null!, to: "m");
        var unboundedUpper = new Range<string>("f", to: null!);

        Assert.True(unboundedLower.Overlaps(unboundedUpper));
        Assert.Equal(new Range<string>("f", "m"), unboundedLower.Intersect(unboundedUpper));
        Assert.Equal(new Range<string>("f", "m"), unboundedUpper.Intersect(unboundedLower));

        Assert.False(unboundedLower.Overlaps(new Range<string>("n", null!)));
        Assert.Null(unboundedLower.Intersect(new Range<string>("n", null!)));
    }
}
