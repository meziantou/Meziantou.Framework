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
    public void FormatterOptions_UsesLargerDefaults()
    {
        var options = new FormatterOptions();

        AssertionsAssert.Equal(20, options.MaxFormattedItems);
        AssertionsAssert.Equal(6, options.PrefixItemCount);
        AssertionsAssert.Equal(0, options.SuffixItemCount);
        AssertionsAssert.Equal(4, options.HighlightedContextItemCount);
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

    [Fact]
    public void UserMessageIsWrittenBeforeDetails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Null("Hello", "custom message"), """
            Assert.Null() assertion failed.
            Message: custom message
            Expression: "Hello"
            Expected: <null>
            Actual:   "Hello"
            """);
    }

    [Fact]
    public void EmptyUserMessageIsOmitted()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.True(false, ""), """
            Assert.True() assertion failed.
            Expression: false
            Expected: true
            Actual:   false
            """);
    }

    [Fact]
    public void GroupsAlignLabels()
    {
        var message = new AssertionMessageBuilder("Header")
            .AppendGroup(
                ("Short", "1"),
                ("Long label", "2"))
            .ToString();

        AssertionsAssert.Equal("""
            Header
            Short:      1
            Long label: 2
            """, message);
    }

    [Fact]
    public async Task AsyncUserMessageIsWrittenBeforeDetails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Empty(actual, "custom message"), """
            Assert.Empty() assertion failed.
            Message: custom message
            Expression: actual
            Actual: [1̲, 2, 3]
            """);
    }

    private sealed class TestAssertionFormatter : AssertionFormatter
    {
        public string FormatValueForTest(object? value, int? highlightedIndex = null)
        {
            return FormatValue(value, highlightedIndex);
        }
    }
}
