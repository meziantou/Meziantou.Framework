namespace Meziantou.Framework.Tests;

public class AnsiTextProcessorTests
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
        var actual = AnsiTextProcessor.RemoveAnsiSequences(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RemoveAnsiSequences_String_ReturnsOriginalWhenNoSequences()
    {
        var input = "Hello World";
        var actual = AnsiTextProcessor.RemoveAnsiSequences(input);
        Assert.Same(input, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hello World")]
    [InlineData("No escape sequences here")]
    public void ContainsAnsiSequences_NoSequences(string input)
    {
        Assert.False(AnsiTextProcessor.ContainsAnsiSequences(input));
        Assert.False(AnsiTextProcessor.ContainsAnsiSequences(input.AsSpan()));
    }

    [Theory]
    [InlineData("\x1b[31mRed Text\x1b[0m")]
    [InlineData("\x1b[1;31mBold Red Text\x1b[0m")]
    [InlineData("Normal \x1b[4mUnderlined\x1b[0m Text")]
    [InlineData("\x1b[38;5;208mOrange\x1b[0m")]
    public void ContainsAnsiSequences_HasSequences(string input)
    {
        Assert.True(AnsiTextProcessor.ContainsAnsiSequences(input));
        Assert.True(AnsiTextProcessor.ContainsAnsiSequences(input.AsSpan()));
    }

    [Theory]
    [InlineData("\u001b")]
    [InlineData("\u001b[")]
    [InlineData("Text\u001b")]
    [InlineData("abc\u001b[31")]
    [InlineData("\u001b[38;5")]
    public void ContainsAnsiSequences_IncompleteSequences(string input)
    {
        Assert.False(AnsiTextProcessor.ContainsAnsiSequences(input));
        Assert.False(AnsiTextProcessor.ContainsAnsiSequences(input.AsSpan()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hello World")]
    [InlineData("\u001b")]
    [InlineData("\u001b[")]
    [InlineData("Text\u001b")]
    [InlineData("abc\u001b[31")]
    [InlineData("\u001b[31mRed\u001b[0m")]
    [InlineData("Start\u001b[2KMiddle\u001b[0mEnd")]
    [InlineData("\u001b[?25lHidden cursor\u001b[?25h")]
    [InlineData("Text with\u001b[A cursor up")]
    [InlineData("\u001b[1m\u001b[31m\u001b[4mBold\u001b[0m")]
    public void ContainsAnsiSequences_AgreesWithRemoveAnsiSequences(string input)
    {
        var removeChangedTheText = !string.Equals(AnsiTextProcessor.RemoveAnsiSequences(input), input, StringComparison.Ordinal);
        Assert.Equal(removeChangedTheText, AnsiTextProcessor.ContainsAnsiSequences(input));
    }

    [Theory]
    [InlineData("\x1b", "\x1b")]
    [InlineData("\x1b[", "\x1b[")]
    [InlineData("Text\x1b", "Text\x1b")]
    public void RemoveAnsiSequences_IncompleteSequences(string input, string expected)
    {
        var actual = AnsiTextProcessor.RemoveAnsiSequences(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RemoveAnsiSequences_MultipleSequencesInRow()
    {
        var input = "\x1b[1m\x1b[31m\x1b[4mBold Red Underlined\x1b[0m";
        var expected = "Bold Red Underlined";
        var actual = AnsiTextProcessor.RemoveAnsiSequences(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RemoveAnsiSequences_PreservesUnicode()
    {
        var input = "\x1b[31m日本語\x1b[0m テキスト";
        var expected = "日本語 テキスト";
        var actual = AnsiTextProcessor.RemoveAnsiSequences(input);
        Assert.Equal(expected, actual);
    }
}
