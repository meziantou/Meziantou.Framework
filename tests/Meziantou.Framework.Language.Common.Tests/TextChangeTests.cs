namespace Meziantou.Framework.Language.Tests;

public sealed class TextChangeTests
{
    [Fact]
    public void Constructor_RoundTripsTheSpanAndText()
    {
        var change = new TextChange(new TextSpan(10, 5), "2.0.0");

        Assert.Equal(new TextSpan(10, 5), change.Span);
        Assert.Equal("2.0.0", change.NewText);
    }

    [Fact]
    public void Constructor_RejectsNullText()
    {
        Assert.Equal("newText", Assert.Throws<ArgumentNullException>(() => new TextChange(default, null!)).ParamName);
    }

    [Fact]
    public void Equality_ComparesTheSpanAndTheText()
    {
        var change = new TextChange(new TextSpan(1, 2), "a");

        Assert.True(change == new TextChange(new TextSpan(1, 2), "a"));
        Assert.Equal(new TextChange(new TextSpan(1, 2), "a").GetHashCode(), change.GetHashCode());

        Assert.True(change != new TextChange(new TextSpan(3, 2), "a"));
        Assert.True(change != new TextChange(new TextSpan(1, 2), "b"));
    }

    [Fact]
    public void Equality_ComparesTheTextOrdinally()
    {
        Assert.True(new TextChange(default, "a") != new TextChange(default, "A"));
    }
}
