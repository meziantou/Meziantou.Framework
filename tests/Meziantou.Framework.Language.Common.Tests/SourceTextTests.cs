namespace Meziantou.Framework.Language.Tests;

public sealed class SourceTextTests
{
    [Fact]
    public void From_TreatsNullAsEmptyText()
    {
        var source = SourceText.From(null!);

        Assert.Equal("", source.Text);
        Assert.Equal(0, source.Length);
        Assert.Equal("", source.ToString());

        var line = Assert.Single(source.Lines);
        Assert.Equal(new TextLine(0, 0, 0, ""), line);
    }

    [Theory]
    [InlineData("a", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\r\nb", 2)]
    [InlineData("a\rb", 2)]
    [InlineData("a\n", 2)]
    [InlineData("a\n\nb", 3)]
    public void Lines_SplitsOnEveryLineBreakStyle(string text, int expectedLineCount)
    {
        Assert.HasCount(expectedLineCount, SourceText.From(text).Lines);
    }

    [Fact]
    public void Lines_ReportsNumbersBoundsAndText()
    {
        var lines = SourceText.From("one\r\ntwo\nthree").Lines;

        Assert.Equal(new TextLine(0, 0, 3, "one"), lines[0]);
        Assert.Equal(new TextLine(1, 5, 8, "two"), lines[1]);
        Assert.Equal(new TextLine(2, 9, 14, "three"), lines[2]);
    }

    [Fact]
    public void Lines_KeepsATrailingLineBreakAsAnEmptyFinalLine()
    {
        var lines = SourceText.From("a\n").Lines;

        Assert.Equal(new TextLine(0, 0, 1, "a"), lines[0]);
        Assert.Equal(new TextLine(1, 2, 2, ""), lines[1]);
    }

    [Fact]
    public void GetLine_PutsBothHalvesOfACrlfOnTheSameLine()
    {
        var source = SourceText.From("a\r\nb");

        Assert.Equal(0, source.GetLine(0).LineNumber);
        Assert.Equal(0, source.GetLine(1).LineNumber);
        Assert.Equal(0, source.GetLine(2).LineNumber);
        Assert.Equal(1, source.GetLine(3).LineNumber);
    }

    [Fact]
    public void GetLine_PastTheLastCharacterReturnsTheLastLine()
    {
        var source = SourceText.From("a\nb");

        Assert.Equal(1, source.GetLine(source.Length).LineNumber);
    }

    [Fact]
    public void GetLine_RejectsANegativePosition()
    {
        Assert.Equal("position", Assert.Throws<ArgumentOutOfRangeException>(() => SourceText.From("a").GetLine(-1)).ParamName);
    }

    [Fact]
    public void WithChanges_RejectsNull()
    {
        Assert.Equal("changes", Assert.Throws<ArgumentNullException>(() => SourceText.From("a").WithChanges(null!)).ParamName);
    }

    [Fact]
    public void WithChanges_AppliesEveryChangeWhateverOrderTheyArePassedIn()
    {
        var source = SourceText.From("one two three");

        // Passed in ascending order: applying them front to back would invalidate the second span.
        var updated = source.WithChanges([
            new TextChange(new TextSpan(0, 3), "1"),
            new TextChange(new TextSpan(8, 5), "3"),
        ]);

        Assert.Equal("1 two 3", updated.Text);
    }

    [Fact]
    public void WithChanges_IgnoresAChangeReachingPastTheEndAndStillAppliesTheRest()
    {
        var updated = SourceText.From("abc").WithChanges([
            new TextChange(new TextSpan(0, 1), "X"),
            new TextChange(new TextSpan(2, 5), "Y"),
        ]);

        Assert.Equal("Xbc", updated.Text);
    }

    [Fact]
    public void WithChanges_SupportsInsertionsDeletionsAndNoChanges()
    {
        var source = SourceText.From("abc");

        Assert.Equal("abc", source.WithChanges([]).Text);
        Assert.Equal("aXbc", source.WithChanges([new TextChange(new TextSpan(1, 0), "X")]).Text);
        Assert.Equal("ac", source.WithChanges([new TextChange(new TextSpan(1, 1), "")]).Text);
    }

    [Fact]
    public void WithChanges_LeavesTheOriginalUntouched()
    {
        var source = SourceText.From("abc");
        _ = source.WithChanges([new TextChange(new TextSpan(0, 3), "xyz")]);

        Assert.Equal("abc", source.Text);
    }
}
