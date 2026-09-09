namespace Meziantou.Framework.Language.Tests;

public sealed class TextChangeRangeTests
{
    [Fact]
    public void Constructor_SetsTheSpanAndTheNewLength()
    {
        var range = new TextChangeRange(new TextSpan(2, 3), 5);

        Assert.Equal(new TextSpan(2, 3), range.Span);
        Assert.Equal(5, range.NewLength);
    }

    [Fact]
    public void Constructor_RejectsANegativeNewLength()
    {
        Assert.Equal("newLength", Assert.Throws<ArgumentOutOfRangeException>(() => new TextChangeRange(default, -1)).ParamName);
    }

    [Theory]
    [InlineData(3, 5, 2)]
    [InlineData(3, 3, 0)]
    [InlineData(3, 1, -2)]
    public void Delta_IsTheGrowthOfTheText(int oldLength, int newLength, int expected)
    {
        Assert.Equal(expected, new TextChangeRange(new TextSpan(0, oldLength), newLength).Delta);
    }

    [Fact]
    public void Collapse_OfNothingIsDefault()
    {
        Assert.Equal(default, TextChangeRange.Collapse([]));
    }

    [Fact]
    public void Collapse_SpansEveryChangeAndSumsTheDeltas()
    {
        TextChangeRange[] changes =
        [
            new(new TextSpan(2, 3), 5),
            new(new TextSpan(10, 4), 1),
        ];

        var collapsed = TextChangeRange.Collapse(changes);

        Assert.Equal(TextSpan.FromBounds(2, 14), collapsed.Span);
        Assert.Equal(12 + 2 - 3, collapsed.NewLength);
    }

    [Fact]
    public void Collapse_ClampsANegativeNewLengthToZero()
    {
        TextChangeRange[] changes =
        [
            new(new TextSpan(0, 5), 0),
            new(new TextSpan(5, 5), 0),
        ];

        Assert.Equal(0, TextChangeRange.Collapse(changes).NewLength);
    }

    [Fact]
    public void Collapse_RejectsNull()
    {
        Assert.Equal("changes", Assert.Throws<ArgumentNullException>(() => TextChangeRange.Collapse(null!)).ParamName);
    }

    [Fact]
    public void Equality_ComparesTheSpanAndTheNewLength()
    {
        var range = new TextChangeRange(new TextSpan(2, 3), 5);

        Assert.True(range == new TextChangeRange(new TextSpan(2, 3), 5));
        Assert.False(range != new TextChangeRange(new TextSpan(2, 3), 5));
        Assert.Equal(new TextChangeRange(new TextSpan(2, 3), 5).GetHashCode(), range.GetHashCode());

        Assert.True(range != new TextChangeRange(new TextSpan(2, 4), 5));
        Assert.True(range != new TextChangeRange(new TextSpan(2, 3), 6));
        Assert.False(range.Equals("not a range"));
    }

    [Fact]
    public void ToString_ShowsTheSpanAndTheNewLength()
    {
        Assert.Equal("[2..5) -> 7", new TextChangeRange(new TextSpan(2, 3), 7).ToString());
    }
}
