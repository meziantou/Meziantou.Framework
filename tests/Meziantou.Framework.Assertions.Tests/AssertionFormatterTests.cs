namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertionFormatterTests
{
    [Fact]
    public void UsesSuffixItemCount()
    {
        var formatter = new TestAssertionFormatter
        {
            MaxFormattedItems = 3,
            SuffixItemCount = 3,
        };

        var value = formatter.FormatValueForTest(Enumerable.Range(0, 10), highlightedIndex: 1);

        global::Xunit.Assert.Equal("[0, 1̲, 2, 3, 4, ...]", value);
    }

    private sealed class TestAssertionFormatter : AssertionFormatter
    {
        public string FormatValueForTest(object? value, int? highlightedIndex = null)
        {
            return FormatValue(value, highlightedIndex);
        }
    }
}
