namespace Meziantou.Framework.Language.Tests;

public sealed class LocationTests
{
    private const string MultiLineText = "line zero\nline one\nline two\nline three has more text here";

    [Fact]
    public void GetLineSpan_MapsARangeInsideOneLine()
    {
        var source = SourceText.From("one\ntwo\nthree line here");
        var location = new Location(new TextSpan(15, 7), source);

        Assert.Equal(new LinePositionSpan(new LinePosition(2, 7), new LinePosition(2, 14)), location.GetLineSpan());
    }

    [Fact]
    public void GetLineSpan_MapsARangeCrossingALineBreak()
    {
        var source = SourceText.From("one\ntwo");
        var lineSpan = new Location(TextSpan.FromBounds(1, 6), source).GetLineSpan();

        Assert.Equal(new LinePosition(0, 1), lineSpan.Start);
        Assert.Equal(new LinePosition(1, 2), lineSpan.End);
    }

    [Fact]
    public void GetLineSpan_MapsARangeEndingAtTheEndOfTheText()
    {
        var source = SourceText.From("one\ntwo");
        var lineSpan = new Location(TextSpan.FromBounds(4, source.Length), source).GetLineSpan();

        Assert.Equal(new LinePosition(1, 3), lineSpan.End);
    }

    [Fact]
    public void ToString_ShowsTheLineSpan()
    {
        var location = new Location(new TextSpan(43, 7), SourceText.From(MultiLineText));

        Assert.Equal("(3,15)-(3,22)", location.ToString());
    }

    [Fact]
    public void WithoutSourceText_HasNoLineInformationAndShowsTheRawSpan()
    {
        var location = new Location(new TextSpan(5, 3));

        Assert.Null(location.SourceText);
        Assert.Equal(default, location.GetLineSpan());
        Assert.Equal("[5..8)", location.ToString());
    }

    [Fact]
    public void None_PointsAtNoText()
    {
        Assert.Equal(default, Location.None.SourceSpan);
        Assert.Null(Location.None.SourceText);
    }

    [Fact]
    public void Equality_ComparesTheSpanByValueAndTheTextByReference()
    {
        var source = SourceText.From("abc");
        var location = new Location(new TextSpan(1, 1), source);

        Assert.Equal(new Location(new TextSpan(1, 1), source), location);
        Assert.Equal(new Location(new TextSpan(1, 1), source).GetHashCode(), location.GetHashCode());

        Assert.NotEqual(new Location(new TextSpan(2, 1), source), location);

        // A separate parse of the same text is a different instance, so the locations are not interchangeable.
        Assert.NotEqual(new Location(new TextSpan(1, 1), SourceText.From("abc")), location);

        Assert.False(location.Equals(null));
        Assert.False(location.Equals("not a location"));
    }
}
