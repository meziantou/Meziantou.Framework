namespace Meziantou.Framework.Tests;

public class AnsiUtilitiesTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("Hello World", "Hello World")]
    [InlineData("\x1b[31mRed Text\x1b[0m", "Red Text")]
    [InlineData("\x1b[1;31mBold Red Text\x1b[0m", "Bold Red Text")]
    [InlineData("\x1b[32mGreen\x1b[0m and \x1b[34mBlue\x1b[0m", "Green and Blue")]
    [InlineData("Normal \x1b[4mUnderlined\x1b[0m Text", "Normal Underlined Text")]
    [InlineData("\x1b[38;5;208mOrange\x1b[0m", "Orange")]
    [InlineData("\x1b[38;2;255;0;0mRGB Red\x1b[0m", "RGB Red")]
    [InlineData("Start\x1b[2KMiddle\x1b[0mEnd", "StartMiddleEnd")]
    [InlineData("\x1b[?25lHidden cursor\x1b[?25h", "Hidden cursor")]
    [InlineData("Text with\x1b[A cursor up", "Text with cursor up")]
    public void RemoveAnsiSequences_String(string input, string expected)
    {
        var actual = AnsiUtilities.RemoveAnsiSequences(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RemoveAnsiSequences_String_ReturnsOriginalWhenNoSequences()
    {
        var input = "Hello World";
        var actual = AnsiUtilities.RemoveAnsiSequences(input);
        Assert.Same(input, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hello World")]
    [InlineData("No escape sequences here")]
    public void ContainsAnsiSequences_NoSequences(string input)
    {
        Assert.False(AnsiUtilities.ContainsAnsiSequences(input));
        Assert.False(AnsiUtilities.ContainsAnsiSequences(input.AsSpan()));
    }

    [Theory]
    [InlineData("\x1b[31mRed Text\x1b[0m")]
    [InlineData("\x1b[1;31mBold Red Text\x1b[0m")]
    [InlineData("Normal \x1b[4mUnderlined\x1b[0m Text")]
    [InlineData("\x1b[38;5;208mOrange\x1b[0m")]
    public void ContainsAnsiSequences_HasSequences(string input)
    {
        Assert.True(AnsiUtilities.ContainsAnsiSequences(input));
        Assert.True(AnsiUtilities.ContainsAnsiSequences(input.AsSpan()));
    }

    [Theory]
    [InlineData("\x1b", "\x1b")]
    [InlineData("\x1b[", "\x1b[")]
    [InlineData("Text\x1b", "Text\x1b")]
    public void RemoveAnsiSequences_IncompleteSequences(string input, string expected)
    {
        var actual = AnsiUtilities.RemoveAnsiSequences(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RemoveAnsiSequences_MultipleSequencesInRow()
    {
        var input = "\x1b[1m\x1b[31m\x1b[4mBold Red Underlined\x1b[0m";
        var expected = "Bold Red Underlined";
        var actual = AnsiUtilities.RemoveAnsiSequences(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RemoveAnsiSequences_PreservesUnicode()
    {
        var input = "\x1b[31m日本語\x1b[0m テキスト";
        var expected = "日本語 テキスト";
        var actual = AnsiUtilities.RemoveAnsiSequences(input);
        Assert.Equal(expected, actual);
    }
}
