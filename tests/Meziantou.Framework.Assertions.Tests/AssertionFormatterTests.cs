using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertionFormatterTests
{
    [Fact]
    public void AssertExposesFormatterOptions()
    {
        var originalOptions = AssertionsAssert.FormatterOptions;
        var options = new FormatterOptions();

        try
        {
            AssertionsAssert.FormatterOptions = options;

            AssertionsAssert.Same(options, AssertionsAssert.FormatterOptions);
        }
        finally
        {
            AssertionsAssert.FormatterOptions = originalOptions;
        }
    }

    [Fact]
    public void FormatterOptions_ValidatesValues()
    {
        var options = new FormatterOptions();

        AssertionsAssert.Throws<ArgumentOutOfRangeException>(() => options.MaxFormattedItems = 0);
        AssertionsAssert.Throws<ArgumentOutOfRangeException>(() => options.PrefixItemCount = -1);
        AssertionsAssert.Throws<ArgumentOutOfRangeException>(() => options.SuffixItemCount = -1);
        AssertionsAssert.Throws<ArgumentOutOfRangeException>(() => options.HighlightedContextItemCount = -1);
    }

    [Fact]
    public void UsesSuffixItemCount()
    {
        var formatter = new TestAssertionFormatter
        {
            MaxFormattedItems = 3,
            SuffixItemCount = 3,
        };

        var value = formatter.FormatValueForTest(Enumerable.Range(0, 10), highlightedIndex: 1);

        AssertionsAssert.Equal("[0, 1̲, 2, 3, 4, ...]", value);
    }

    private sealed class TestAssertionFormatter : AssertionFormatter
    {
        public string FormatValueForTest(object? value, int? highlightedIndex = null)
        {
            return FormatValue(value, highlightedIndex);
        }
    }
}
