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
    [InlineData(8)]
    [InlineData(100)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(5000)]
    public void RemoveAnsiSequences_LengthsAroundStackAllocThreshold(int inputLength)
    {
        var expected = new string('a', inputLength - 8);
        var input = "\u001b[31m" + expected + "\u001b[0m";
        Assert.Equal(expected, AnsiTextProcessor.RemoveAnsiSequences(input));
        Assert.Equal(expected, AnsiTextProcessor.RemoveAnsiSequences(input.AsSpan()));
    }

    [Fact]
    public void AnsiStyle_None_IsCached()
    {
        Assert.Same(AnsiTextProcessor.AnsiStyle.None, AnsiTextProcessor.AnsiStyle.None);
    }

    [Fact]
    public void AnsiStyle_None_EqualsAnEquivalentInstance()
    {
        var constructed = new AnsiTextProcessor.AnsiStyle(Foreground: null, Background: null, Bold: false, Italic: false, Underline: false, Inverse: false);
        Assert.Equal(AnsiTextProcessor.AnsiStyle.None, constructed);
        Assert.NotSame(AnsiTextProcessor.AnsiStyle.None, constructed);
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
    [InlineData("\u001b]0;window title\u0007hello")]
    [InlineData("\u001b]8;;https://example.com\u001b\\click me\u001b]8;;\u001b\\")]
    [InlineData("\u001b]8;;https://example.com")]
    [InlineData("\u001b]")]
    [InlineData("\u001b]0;no terminator here")]
    public void ContainsAnsiSequences_AgreesWithRemoveAnsiSequences(string input)
    {
        var removeChangedTheText = !string.Equals(AnsiTextProcessor.RemoveAnsiSequences(input), input, StringComparison.Ordinal);
        Assert.Equal(removeChangedTheText, AnsiTextProcessor.ContainsAnsiSequences(input));
    }

    [Theory]
    [InlineData("\u001b]0;window title\u0007hello", "hello")]
    [InlineData("\u001b]8;;https://example.com\u001b\\click me\u001b]8;;\u001b\\", "click me")]
    [InlineData("a\u001b]52;c;Zm9v\u0007b", "ab")]
    [InlineData("\u001b[31m\u001b]0;t\u0007red\u001b[0m", "red")]
    [InlineData("\u001b]0;a\u0007\u001b]0;b\u0007x", "x")]
    public void RemoveAnsiSequences_OscSequences(string input, string expected)
    {
        Assert.Equal(expected, AnsiTextProcessor.RemoveAnsiSequences(input));
        Assert.Equal(expected, AnsiTextProcessor.RemoveAnsiSequences(input.AsSpan()));
        Assert.True(AnsiTextProcessor.ContainsAnsiSequences(input));
    }

    [Theory]
    [InlineData("\u001b]")]
    [InlineData("\u001b]8;;https://example.com")]
    [InlineData("\u001b]0;window title")]
    [InlineData("text\u001b]0;cut off")]
    public void RemoveAnsiSequences_IncompleteOscSequenceIsKept(string input)
    {
        Assert.Equal(input, AnsiTextProcessor.RemoveAnsiSequences(input));
        Assert.False(AnsiTextProcessor.ContainsAnsiSequences(input));
    }

    [Fact]
    public void ParseTextWithAnsiStyles_OscSequenceIsRemovedAndCarriesNoStyle()
    {
        var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles("\u001b[1m\u001b]8;;https://example.com\u001b\\link\u001b]8;;\u001b\\");

        Assert.Equal("link", parsed.Text);
        var run = Assert.Single(parsed.Runs);
        Assert.True(run.Style.Bold);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_OscSequenceWithBellTerminator()
    {
        var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles("a\u001b]0;title\u0007b");

        Assert.Equal("ab", parsed.Text);
        Assert.Equal(AnsiTextProcessor.AnsiStyle.None, Assert.Single(parsed.Runs).Style);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_IncompleteOscSequenceIsKept()
    {
        var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles("a\u001b]0;cut off");

        Assert.Equal("a\u001b]0;cut off", parsed.Text);
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

    [Theory]
    [InlineData("\u001b[38:5:208m")]
    [InlineData("\u001b[38:2:10:20:30m")]
    [InlineData("\u001b[4:3m")]
    [InlineData("\u001b[99999999999m")]
    [InlineData("\u001b[<m")]
    [InlineData("\u001b[?m")]
    public void ParseTextWithAnsiStyles_UnparsableSgrParametersDoNotResetStyle(string sequence)
    {
        var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles("\u001b[1;4m" + sequence + "A");

        Assert.Equal("A", parsed.Text);
        var run = Assert.Single(parsed.Runs);
        Assert.True(run.Style.Bold);
        Assert.True(run.Style.Underline);
    }

    [Theory]
    [InlineData("\u001b[0m")]
    [InlineData("\u001b[m")]
    public void ParseTextWithAnsiStyles_ResetStillClearsStyle(string sequence)
    {
        var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles("\u001b[1;4m" + sequence + "A");

        var run = Assert.Single(parsed.Runs);
        Assert.False(run.Style.Bold);
        Assert.False(run.Style.Underline);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_PartlyParsableSgrParametersApplyTheKnownOnes()
    {
        var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles("\u001b[1;?;4mA");

        var run = Assert.Single(parsed.Runs);
        Assert.True(run.Style.Bold);
        Assert.True(run.Style.Underline);
    }

    [Theory]
    [InlineData("\u001b[38:5:208mA", 208)]
    [InlineData("\u001b[38:5:0mA", 0)]
    [InlineData("\u001b[38:5:255mA", 255)]
    public void ParseTextWithAnsiStyles_ColonIndexedForeground(string input, int expectedIndex)
    {
        var style = SingleColonRunStyle(input);
        Assert.NotNull(style.Foreground);
        Assert.Equal(AnsiTextProcessor.AnsiColorKind.Indexed, style.Foreground.Kind);
        Assert.Equal(expectedIndex, style.Foreground.IndexedValue);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_ColonIndexedBackground()
    {
        var style = SingleColonRunStyle("\u001b[48:5:208mA");
        Assert.NotNull(style.Background);
        Assert.Equal(208, style.Background.IndexedValue);
    }

    [Theory]
    [InlineData("\u001b[38:2:10:20:30mA")]
    [InlineData("\u001b[38:2::10:20:30mA")]
    [InlineData("\u001b[38:2:1:10:20:30mA")]
    public void ParseTextWithAnsiStyles_ColonRgbForeground(string input)
    {
        var style = SingleColonRunStyle(input);
        Assert.NotNull(style.Foreground);
        Assert.Equal(AnsiTextProcessor.AnsiColorKind.Rgb, style.Foreground.Kind);
        Assert.Equal(10, style.Foreground.Red);
        Assert.Equal(20, style.Foreground.Green);
        Assert.Equal(30, style.Foreground.Blue);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_ColonRgbBackground()
    {
        var style = SingleColonRunStyle("\u001b[48:2:10:20:30mA");
        Assert.NotNull(style.Background);
        Assert.Equal(AnsiTextProcessor.AnsiColorKind.Rgb, style.Background.Kind);
        Assert.Equal(10, style.Background.Red);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_ColonSubParametersOfOtherParametersAreIgnored()
    {
        // 4:3 is a curly underline: the underline applies, the style variant is not modelled
        var style = SingleColonRunStyle("\u001b[4:3mA");
        Assert.True(style.Underline);
        Assert.False(style.Italic);
        Assert.False(style.Bold);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_ColonParameterMixedWithClassicParameters()
    {
        var style = SingleColonRunStyle("\u001b[1;38:5:208;4mA");
        Assert.True(style.Bold);
        Assert.True(style.Underline);
        Assert.NotNull(style.Foreground);
        Assert.Equal(208, style.Foreground.IndexedValue);
    }

    [Theory]
    [InlineData("\u001b[38:5:300mA")]
    [InlineData("\u001b[38:2:300:1:1mA")]
    [InlineData("\u001b[38:9:1mA")]
    [InlineData("\u001b[38:5mA")]
    [InlineData("\u001b[38:2:1:2mA")]
    [InlineData("\u001b[38:2:1:2:3:4:5mA")]
    public void ParseTextWithAnsiStyles_InvalidColonExtendedColorIsIgnored(string input)
    {
        var style = SingleColonRunStyle(input);
        Assert.Null(style.Foreground);
        Assert.Null(style.Background);
    }

    [Theory]
    [InlineData("\u001b[ 4m")]
    [InlineData("\u001b[+4m")]
    [InlineData("\u001b[-4m")]
    public void ParseTextWithAnsiStyles_NonDigitSgrParametersAreIgnored(string sequence)
    {
        // ECMA-48 parameter bytes are digits, ';' and ':' only
        var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles("\u001b[1m" + sequence + "A");

        var run = Assert.Single(parsed.Runs);
        Assert.True(run.Style.Bold);
        Assert.False(run.Style.Underline);
    }

    private static AnsiTextProcessor.AnsiStyle SingleColonRunStyle(string input)
    {
        var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles(input);
        Assert.Equal("A", parsed.Text);
        return Assert.Single(parsed.Runs).Style;
    }

    [Fact]
    public void RemoveAnsiSequences_MultipleSequencesInRow()
    {
        var input = "\x1b[1m\x1b[31m\x1b[4mBold Red Underlined\x1b[0m";
        var expected = "Bold Red Underlined";
        var actual = AnsiTextProcessor.RemoveAnsiSequences(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("\u001b[1m")]
    [InlineData("\u001b[1m\u001b[0m")]
    [InlineData("\u001b[1ma")]
    [InlineData("a\u001b[1m")]
    [InlineData("\u001b[0ma")]
    [InlineData("\u001b[1m\u001b[22ma")]
    [InlineData("a\u001b[1mb\u001b[0mc")]
    [InlineData("a\u001b[2Kb")]
    [InlineData("\u001b[31m日本語\u001b[0m テキスト")]
    public void ParseTextWithAnsiStyles_RunsCoverAllTextWithoutGaps(string input)
    {
        var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles(input);

        var position = 0;
        foreach (var run in parsed.Runs)
        {
            Assert.Equal(position, run.Start);
            Assert.True(run.End > run.Start, "Runs must not be empty");
            position = run.End;
        }

        Assert.Equal(parsed.Text.Length, position);
    }

    [Fact]
    public void RemoveAnsiSequences_PreservesUnicode()
    {
        var input = "\x1b[31m日本語\x1b[0m テキスト";
        var expected = "日本語 テキスト";
        var actual = AnsiTextProcessor.RemoveAnsiSequences(input);
        Assert.Equal(expected, actual);
    }

    private static AnsiTextProcessor.AnsiText Parse(string input)
    {
        return AnsiTextProcessor.ParseTextWithAnsiStyles(input);
    }

    private static AnsiTextProcessor.AnsiStyle SingleRunStyle(string input, string expectedText)
    {
        var parsed = Parse(input);
        Assert.Equal(expectedText, parsed.Text);
        var run = Assert.Single(parsed.Runs);
        Assert.Equal(0, run.Start);
        Assert.Equal(expectedText.Length, run.End);
        return run.Style;
    }

    [Fact]
    public void ParseTextWithAnsiStyles_Null()
    {
        Assert.Throws<ArgumentNullException>(() => AnsiTextProcessor.ParseTextWithAnsiStyles(null!));
    }

    [Fact]
    public void ParseTextWithAnsiStyles_PlainText()
    {
        Assert.Equal(AnsiTextProcessor.AnsiStyle.None, SingleRunStyle("Hello World", "Hello World"));
    }

    [Fact]
    public void ParseTextWithAnsiStyles_EmptyString()
    {
        var parsed = Parse("");
        Assert.Equal("", parsed.Text);
        Assert.Empty(parsed.Runs);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_SequencesOnly()
    {
        var parsed = Parse("\u001b[1m\u001b[0m");
        Assert.Equal("", parsed.Text);
        Assert.Empty(parsed.Runs);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_PreservesUnicode()
    {
        Assert.Equal("日本語 テキスト", Parse("\u001b[31m日本語\u001b[0m テキスト").Text);
    }

    [Theory]
    [InlineData("\u001b[1mA", true, false, false, false)]
    [InlineData("\u001b[3mA", false, true, false, false)]
    [InlineData("\u001b[4mA", false, false, true, false)]
    [InlineData("\u001b[7mA", false, false, false, true)]
    [InlineData("\u001b[1;3;4;7mA", true, true, true, true)]
    public void ParseTextWithAnsiStyles_Attributes(string input, bool bold, bool italic, bool underline, bool inverse)
    {
        var style = SingleRunStyle(input, "A");
        Assert.Equal(bold, style.Bold);
        Assert.Equal(italic, style.Italic);
        Assert.Equal(underline, style.Underline);
        Assert.Equal(inverse, style.Inverse);
    }

    [Theory]
    [InlineData("\u001b[22mA", false, true, true, true)]
    [InlineData("\u001b[23mA", true, false, true, true)]
    [InlineData("\u001b[24mA", true, true, false, true)]
    [InlineData("\u001b[27mA", true, true, true, false)]
    public void ParseTextWithAnsiStyles_PartialResets(string reset, bool bold, bool italic, bool underline, bool inverse)
    {
        var style = SingleRunStyle("\u001b[1;3;4;7m" + reset, "A");
        Assert.Equal(bold, style.Bold);
        Assert.Equal(italic, style.Italic);
        Assert.Equal(underline, style.Underline);
        Assert.Equal(inverse, style.Inverse);
    }

    [Theory]
    [InlineData("\u001b[1m\u001b[0mA")]
    [InlineData("\u001b[1m\u001b[mA")]
    public void ParseTextWithAnsiStyles_Reset(string input)
    {
        Assert.Equal(AnsiTextProcessor.AnsiStyle.None, SingleRunStyle(input, "A"));
    }

    [Theory]
    [InlineData("\u001b[30mA", 0)]
    [InlineData("\u001b[31mA", 1)]
    [InlineData("\u001b[37mA", 7)]
    [InlineData("\u001b[90mA", 8)]
    [InlineData("\u001b[97mA", 15)]
    [InlineData("\u001b[38;5;208mA", 208)]
    public void ParseTextWithAnsiStyles_IndexedForeground(string input, int expectedIndex)
    {
        var style = SingleRunStyle(input, "A");
        Assert.Null(style.Background);
        Assert.NotNull(style.Foreground);
        Assert.Equal(AnsiTextProcessor.AnsiColorKind.Indexed, style.Foreground.Kind);
        Assert.Equal(expectedIndex, style.Foreground.IndexedValue);
    }

    [Theory]
    [InlineData("\u001b[40mA", 0)]
    [InlineData("\u001b[41mA", 1)]
    [InlineData("\u001b[47mA", 7)]
    [InlineData("\u001b[100mA", 8)]
    [InlineData("\u001b[107mA", 15)]
    [InlineData("\u001b[48;5;208mA", 208)]
    public void ParseTextWithAnsiStyles_IndexedBackground(string input, int expectedIndex)
    {
        var style = SingleRunStyle(input, "A");
        Assert.Null(style.Foreground);
        Assert.NotNull(style.Background);
        Assert.Equal(AnsiTextProcessor.AnsiColorKind.Indexed, style.Background.Kind);
        Assert.Equal(expectedIndex, style.Background.IndexedValue);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_RgbForeground()
    {
        var style = SingleRunStyle("\u001b[38;2;10;20;30mA", "A");
        Assert.NotNull(style.Foreground);
        Assert.Equal(AnsiTextProcessor.AnsiColorKind.Rgb, style.Foreground.Kind);
        Assert.Equal(10, style.Foreground.Red);
        Assert.Equal(20, style.Foreground.Green);
        Assert.Equal(30, style.Foreground.Blue);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_RgbBackground()
    {
        var style = SingleRunStyle("\u001b[48;2;10;20;30mA", "A");
        Assert.NotNull(style.Background);
        Assert.Equal(AnsiTextProcessor.AnsiColorKind.Rgb, style.Background.Kind);
        Assert.Equal(10, style.Background.Red);
        Assert.Equal(20, style.Background.Green);
        Assert.Equal(30, style.Background.Blue);
    }

    [Theory]
    [InlineData("\u001b[31;41m\u001b[39mA", true, false)]
    [InlineData("\u001b[31;41m\u001b[49mA", false, true)]
    public void ParseTextWithAnsiStyles_DefaultColors(string input, bool foregroundIsDefault, bool backgroundIsDefault)
    {
        var style = SingleRunStyle(input, "A");
        Assert.Equal(foregroundIsDefault, style.Foreground is null);
        Assert.Equal(backgroundIsDefault, style.Background is null);
    }

    [Theory]
    [InlineData("\u001b[38mA")]
    [InlineData("\u001b[38;5mA")]
    [InlineData("\u001b[38;2;1;2mA")]
    [InlineData("\u001b[38;5;300mA")]
    [InlineData("\u001b[38;2;300;1;1mA")]
    public void ParseTextWithAnsiStyles_InvalidExtendedColorIsIgnored(string input)
    {
        Assert.Null(SingleRunStyle(input, "A").Foreground);
    }

    [Theory]
    [InlineData("a\u001b[2Kb", "ab")]
    [InlineData("a\u001b[?25lb", "ab")]
    [InlineData("a\u001b[Ab", "ab")]
    [InlineData("a\u001b[1;2Hb", "ab")]
    public void ParseTextWithAnsiStyles_NonSgrSequencesAreRemovedWithoutStyling(string input, string expected)
    {
        Assert.Equal(AnsiTextProcessor.AnsiStyle.None, SingleRunStyle(input, expected));
    }

    [Fact]
    public void ParseTextWithAnsiStyles_RunsSplitAtStyleBoundaries()
    {
        var parsed = Parse("a\u001b[1mb\u001b[0mc");
        Assert.Equal("abc", parsed.Text);
        Assert.Equal(3, parsed.Runs.Count);

        Assert.Equal(0, parsed.Runs[0].Start);
        Assert.Equal(1, parsed.Runs[0].End);
        Assert.False(parsed.Runs[0].Style.Bold);

        Assert.Equal(1, parsed.Runs[1].Start);
        Assert.Equal(2, parsed.Runs[1].End);
        Assert.True(parsed.Runs[1].Style.Bold);

        Assert.Equal(2, parsed.Runs[2].Start);
        Assert.Equal(3, parsed.Runs[2].End);
        Assert.False(parsed.Runs[2].Style.Bold);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_AdjacentSequencesDoNotCreateEmptyRuns()
    {
        var style = SingleRunStyle("\u001b[1m\u001b[31m\u001b[4mX", "X");
        Assert.True(style.Bold);
        Assert.True(style.Underline);
        Assert.NotNull(style.Foreground);
    }

    [Fact]
    public void ParseTextWithAnsiStyles_RedundantSequenceDoesNotSplitRun()
    {
        var parsed = Parse("a\u001b[1mb\u001b[1mc");
        Assert.Equal("abc", parsed.Text);
        Assert.Equal(2, parsed.Runs.Count);
        Assert.Equal(1, parsed.Runs[1].Start);
        Assert.Equal(3, parsed.Runs[1].End);
    }
}
