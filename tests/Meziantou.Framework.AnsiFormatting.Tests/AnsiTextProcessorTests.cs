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
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(208)]
    [InlineData(255)]
    public void AnsiColor_FromIndexed_InRange(int value)
    {
        var color = AnsiTextProcessor.AnsiColor.FromIndexed(value);
        Assert.Equal(AnsiTextProcessor.AnsiColorKind.Indexed, color.Kind);
        Assert.Equal(value, color.IndexedValue);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    [InlineData(300)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void AnsiColor_FromIndexed_OutOfRange(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AnsiTextProcessor.AnsiColor.FromIndexed(value));
    }

    [Theory]
    [InlineData("\u001b[38;5;300mA")]
    [InlineData("\u001b[48;5;300mA")]
    public void ParseTextWithAnsiStyles_OutOfRangeIndexedColorDoesNotThrow(string input)
    {
        var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles(input);
        Assert.Equal("A", parsed.Text);
        var run = Assert.Single(parsed.Runs);
        Assert.Null(run.Style.Foreground);
        Assert.Null(run.Style.Background);
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
