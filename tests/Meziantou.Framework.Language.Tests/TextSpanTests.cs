namespace Meziantou.Framework.Language.Tests;

public sealed class TextSpanTests
{
    [Fact]
    public void Constructor_SetsBounds()
    {
        var span = new TextSpan(5, 3);

        Assert.Equal(5, span.Start);
        Assert.Equal(3, span.Length);
        Assert.Equal(8, span.End);
    }

    [Fact]
    public void Constructor_RejectsNegativeValues()
    {
        Assert.Equal("start", Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(-1, 0)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(0, -1)).ParamName);
    }

    [Fact]
    public void Default_IsEmptyAtZero()
    {
        Assert.Equal(0, default(TextSpan).Start);
        Assert.Equal(0, default(TextSpan).Length);
    }

    [Fact]
    public void FromBounds_MatchesTheEquivalentConstructor()
    {
        Assert.Equal(new TextSpan(5, 3), TextSpan.FromBounds(5, 8));
    }

    [Fact]
    public void FromBounds_RejectsANegativeStartOrAnEndBeforeTheStart()
    {
        Assert.Equal("start", Assert.Throws<ArgumentOutOfRangeException>(() => TextSpan.FromBounds(-1, 0)).ParamName);
        Assert.Equal("end", Assert.Throws<ArgumentOutOfRangeException>(() => TextSpan.FromBounds(5, 4)).ParamName);
    }

    [Fact]
    public void Equality_ComparesStartAndLength()
    {
        var span = new TextSpan(5, 3);

        Assert.True(span == new TextSpan(5, 3));
        Assert.False(span != new TextSpan(5, 3));
        Assert.Equal(new TextSpan(5, 3).GetHashCode(), span.GetHashCode());

        Assert.True(span != new TextSpan(6, 3));
        Assert.True(span != new TextSpan(5, 4));
        Assert.False(span.Equals("not a span"));
    }

    [Fact]
    public void ToString_ShowsAHalfOpenRange()
    {
        Assert.Equal("[5..8)", new TextSpan(5, 3).ToString());
    }

    [Fact]
    public void IsEmpty_IsTrueForAZeroLengthSpan()
    {
        Assert.True(new TextSpan(5, 0).IsEmpty);
        Assert.True(default(TextSpan).IsEmpty);
        Assert.False(new TextSpan(5, 1).IsEmpty);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void ContainsPosition_ExcludesTheEnd(int position, bool expected)
    {
        Assert.Equal(expected, new TextSpan(5, 3).Contains(position));
    }

    [Fact]
    public void ContainsPosition_IsFalseForEveryPositionOfAnEmptySpan()
    {
        Assert.False(new TextSpan(5, 0).Contains(5));
    }

    [Theory]
    [InlineData(5, 3, true)]
    [InlineData(6, 1, true)]
    [InlineData(5, 0, true)]
    [InlineData(8, 0, true)]
    [InlineData(4, 3, false)]
    [InlineData(6, 3, false)]
    public void ContainsSpan_ComparesBounds(int start, int length, bool expected)
    {
        Assert.Equal(expected, new TextSpan(5, 3).Contains(new TextSpan(start, length)));
    }

    [Fact]
    public void OverlapsWith_RequiresASharedCharacter()
    {
        var span = new TextSpan(5, 3);

        Assert.True(span.OverlapsWith(new TextSpan(7, 3)));
        Assert.True(span.OverlapsWith(new TextSpan(0, 6)));

        // Touching at an endpoint is not an overlap, and an empty span never overlaps.
        Assert.False(span.OverlapsWith(new TextSpan(8, 3)));
        Assert.False(span.OverlapsWith(new TextSpan(2, 3)));
        Assert.False(span.OverlapsWith(new TextSpan(6, 0)));
    }

    [Fact]
    public void IntersectsWith_AllowsTouchingEndpoints()
    {
        var span = new TextSpan(5, 3);

        Assert.True(span.IntersectsWith(new TextSpan(8, 3)));
        Assert.True(span.IntersectsWith(new TextSpan(2, 3)));
        Assert.True(span.IntersectsWith(new TextSpan(6, 0)));

        Assert.False(span.IntersectsWith(new TextSpan(9, 3)));
        Assert.False(span.IntersectsWith(new TextSpan(1, 3)));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void IntersectsWithPosition_IncludesTheEnd(int position, bool expected)
    {
        Assert.Equal(expected, new TextSpan(5, 3).IntersectsWith(position));
    }

    [Fact]
    public void Overlap_ReturnsTheSharedRangeOrNull()
    {
        var span = new TextSpan(5, 3);

        Assert.Equal(TextSpan.FromBounds(7, 8), span.Overlap(new TextSpan(7, 3)));
        Assert.Null(span.Overlap(new TextSpan(8, 3)));
        Assert.Null(span.Overlap(new TextSpan(9, 3)));
    }

    [Fact]
    public void Intersection_ReturnsAnEmptySpanForTouchingSpans()
    {
        var span = new TextSpan(5, 3);

        Assert.Equal(TextSpan.FromBounds(7, 8), span.Intersection(new TextSpan(7, 3)));
        Assert.Equal(TextSpan.FromBounds(8, 8), span.Intersection(new TextSpan(8, 3)));
        Assert.Null(span.Intersection(new TextSpan(9, 3)));
    }

    [Fact]
    public void CompareTo_OrdersByStartThenLength()
    {
        var span = new TextSpan(5, 3);

        Assert.True(span < new TextSpan(6, 0));
        Assert.True(span < new TextSpan(5, 4));
        Assert.True(span > new TextSpan(5, 2));
        Assert.True(span > new TextSpan(4, 99));
        Assert.True(span <= new TextSpan(5, 3));
        Assert.True(span >= new TextSpan(5, 3));
        Assert.Equal(0, span.CompareTo(new TextSpan(5, 3)));
    }
}
