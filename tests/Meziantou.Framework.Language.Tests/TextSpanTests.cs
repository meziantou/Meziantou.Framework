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
}
