using Meziantou.Xunit;
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

    [Fact]
    public void ToStringExceptionDoesNotReplaceAssertionFailure()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(new ThrowingToString(), new ThrowingToString()), """
            Assert.Equal() assertion failed.
            Expected expression: new ThrowingToString()
            Actual expression:   new ThrowingToString()
            Expected: <ToString() threw System.InvalidOperationException: line 1\nline 2>
            Actual:   <ToString() threw System.InvalidOperationException: line 1\nline 2>
            """);
    }

    [Fact]
    public void SelfReferencingRecordDoesNotReplaceAssertionFailure()
    {
        var node = new SelfReferencingNode();
        node.Next = node;

        var exception = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Null(node));

        AssertionsAssert.StartsWith("""
            Assert.Null() assertion failed.
            Expression: node
            Expected: <null>
            Actual:   <ToString() threw System.InsufficientExecutionStackException:
            """, exception.Message);
    }

    [Fact]
    public void EnumerationExceptionDoesNotReplaceAssertionFailure()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Null(new ThrowingEnumerable()), """
            Assert.Null() assertion failed.
            Expression: new ThrowingEnumerable()
            Expected: <null>
            Actual:   [<enumeration threw System.InvalidOperationException: GetEnumerator failed>]
            """);

        AssertionTestHelpers.Validate(() => AssertionsAssert.Null(new ThrowingCurrentEnumerable()), """
            Assert.Null() assertion failed.
            Expression: new ThrowingCurrentEnumerable()
            Expected: <null>
            Actual:   [<enumeration threw System.InvalidOperationException: Current failed>]
            """);
    }

    [Fact]
    public void EnumerationExceptionAfterSomeItemsIsWrittenAfterTheItems()
    {
        object sequence = YieldThenThrow();

        AssertionTestHelpers.Validate(() => AssertionsAssert.Null(sequence), """
            Assert.Null() assertion failed.
            Expression: sequence
            Expected: <null>
            Actual:   [1, 2, <enumeration threw System.InvalidOperationException: MoveNext failed>]
            """);
    }

    [Fact]
    public void EnumerationExceptionWhileReadingMoreItemsForTheMessageDoesNotReplaceAssertionFailure()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(YieldThenThrow()), """
            Assert.Empty() assertion failed.
            Expression: YieldThenThrow()
            Actual: [1̲, 2, <enumeration threw System.InvalidOperationException: MoveNext failed>]
            """);
    }

    [Fact]
    public async Task AsyncEnumerationExceptionWhileReadingMoreItemsForTheMessageDoesNotReplaceAssertionFailure()
    {
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Empty(YieldThenThrowAsync()), """
            Assert.Empty() assertion failed.
            Expression: YieldThenThrowAsync()
            Actual: [1̲, 2, <enumeration threw System.InvalidOperationException: MoveNext failed>]
            """);
    }

    [Fact]
    public void FormatsDatesAndTimesWithoutLosingPrecision()
    {
        var formatter = new TestAssertionFormatter();
        var dateTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddMilliseconds(3);

        AssertionsAssert.Equal("10:00:30.0000000", formatter.FormatValueForTest(new TimeOnly(10, 0, 30)));
        AssertionsAssert.Equal("2024-01-02T03:04:05.0030000Z", formatter.FormatValueForTest(dateTime));
        AssertionsAssert.Equal("2024-01-02T03:04:05.0030000+02:00", formatter.FormatValueForTest(new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), TimeSpan.FromHours(2))));
        AssertionsAssert.Equal("2024-01-02", formatter.FormatValueForTest(new DateOnly(2024, 1, 2)));
        AssertionsAssert.Equal("00:00:00.0030000", formatter.FormatValueForTest(TimeSpan.FromMilliseconds(3)));
        AssertionsAssert.Equal("0.30000000000000004", formatter.FormatValueForTest(0.1 + 0.2));
        AssertionsAssert.Equal("1.00", formatter.FormatValueForTest(1.00m));
    }

    [Fact]
    public void TimeOnlyDifferenceIsVisible()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(new TimeOnly(10, 0, 0), new TimeOnly(10, 0, 30)), """
            Assert.Equal() assertion failed.
            Expected expression: new TimeOnly(10, 0, 0)
            Actual expression:   new TimeOnly(10, 0, 30)
            Expected: 10:00:00.0000000
            Actual:   10:00:30.0000000
            """);
    }

    [Fact]
    public void HighlightingNeverSplitsSurrogatePairs()
    {
        var formatter = new TestAssertionFormatter();

        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(new[] { "😀" }), """
            Assert.Empty() assertion failed.
            Expression: new[] { "😀" }
            Actual: ["̲😀̲"̲]
            """);
        AssertionsAssert.Equal("\"a😀̲\"", formatter.FormatValueForTest("a😀", highlightedIndex: 1));
        AssertionsAssert.Equal("\"a😀̲\"", formatter.FormatValueForTest("a😀", highlightedIndex: 2));
    }

    [Fact]
    public void StringDifferenceInsideSurrogatePairHighlightsThePair()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal("a😀", "a😁"), """
            Assert.Equal() assertion failed.
            Expected expression: "a😀"
            Actual expression:   "a😁"
            Index of first difference: 2
            Expected: "a😀̲"
            Actual:   "a😁̲"
            """);
    }

    [Fact]
    public void FormatsMemoryByContent()
    {
        var formatter = new TestAssertionFormatter();

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual((ReadOnlyMemory<int>)new[] { 1, 2, 3 }, (ReadOnlyMemory<int>)new[] { 1, 2, 3 }), """
            Assert.NotEqual() assertion failed.
            Expected expression: (ReadOnlyMemory<int>)new[] { 1, 2, 3 }
            Actual expression:   (ReadOnlyMemory<int>)new[] { 1, 2, 3 }
            Not expected: [1, 2, 3]
            Actual:       [1, 2, 3]
            """);
        AssertionsAssert.Equal("\"a\\nb\"", formatter.FormatValueForTest("a\nb".AsMemory()));
        AssertionsAssert.Equal("[1, 2̲, 3]", formatter.FormatValueForTest(new Memory<int>([1, 2, 3]), highlightedIndex: 1));
        AssertionsAssert.Equal("[[1, 2], [3]]", formatter.FormatValueForTest(new object[] { new ReadOnlyMemory<int>([1, 2]), new Memory<int>([3]) }));
    }

    [Fact]
    public void FormatsCircularReferenceThroughBoxedMemory()
    {
        var formatter = new TestAssertionFormatter();
        var array = new object[1];
        array[0] = new ReadOnlyMemory<object>(array);

        AssertionsAssert.Equal("[[<circular reference>]]", formatter.FormatValueForTest(array));
    }

    [Fact]
    public void EscapesChars()
    {
        var formatter = new TestAssertionFormatter();

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal('\n', '\r'), """
            Assert.Equal() assertion failed.
            Expected expression: '\n'
            Actual expression:   '\r'
            Expected: '\n'
            Actual:   '\r'
            """);
        AssertionsAssert.Equal("'a'", formatter.FormatValueForTest('a'));
        AssertionsAssert.Equal("'\"'", formatter.FormatValueForTest('"'));
        AssertionsAssert.Equal("'\\''", formatter.FormatValueForTest('\''));
        AssertionsAssert.Equal("'\\\\'", formatter.FormatValueForTest('\\'));
        AssertionsAssert.Equal("'\\u00A0'", formatter.FormatValueForTest('\u00A0'));
        AssertionsAssert.Equal("['a', '\\t']", formatter.FormatValueForTest(new[] { 'a', '\t' }));
    }

    [Fact]
    public void EscapesInvisibleAndAmbiguousCharactersInStrings()
    {
        var formatter = new TestAssertionFormatter();

        AssertionsAssert.Equal("\"\\u007F\\u0085\\u2028\\u2029\\u200B\\uFEFF\\u00A0\\u3000\\uD83D'\"", formatter.FormatValueForTest("\u007F\u0085\u2028\u2029\u200B\uFEFF\u00A0\u3000\uD83D'"));
        AssertionsAssert.Equal("\"héllo wörld 😀 日本 e\u0301\"", formatter.FormatValueForTest("héllo wörld 😀 日本 e\u0301"));
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal("a b", "a\u00A0b"), """
            Assert.Equal() assertion failed.
            Expected expression: "a b"
            Actual expression:   "a\u00A0b"
            Index of first difference: 1
            Expected: "a ̲b"
            Actual:   "a\̲u̲0̲0̲A̲0̲b"
            """);
    }

    [Fact]
    public void FormatsKeyValuePairsAndTuplesFromTheirParts()
    {
        var formatter = new TestAssertionFormatter();

        AssertionsAssert.Equal("[\"a\\nb\", 1.5]", formatter.FormatValueForTest(KeyValuePair.Create("a\nb", 1.5)));
        AssertionsAssert.Equal("(\"a\\nb\", 'c', <null>)", formatter.FormatValueForTest(("a\nb", 'c', (string?)null)));
        AssertionsAssert.Equal("(1, [2, 3])", formatter.FormatValueForTest(Tuple.Create(1, new[] { 2, 3 })));
        AssertionsAssert.Equal("(1, 2, 3, 4, 5, 6, 7, 8, 9)", formatter.FormatValueForTest((1, 2, 3, 4, 5, 6, 7, 8, 9)));
        AssertionsAssert.Equal("[[\"a\", 1]]", formatter.FormatValueForTest(new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 }));
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void FormatsNestedValuesWithInvariantCulture()
    {
        var formatter = new TestAssertionFormatter();
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentCulture = culture;

            AssertionsAssert.Equal("1.5", formatter.FormatValueForTest(1.5));
            AssertionsAssert.Equal("(1.5, 2)", formatter.FormatValueForTest((1.5, 2)));
            AssertionsAssert.Equal("[\"a\", 1.5]", formatter.FormatValueForTest(KeyValuePair.Create("a", 1.5)));
            AssertionsAssert.Equal("RecordWithDouble { Value = 1.5 }", formatter.FormatValueForTest(new RecordWithDouble(1.5)));
            AssertionsAssert.Equal("{ Value = 1.5 }", formatter.FormatValueForTest(new { Value = 1.5 }));
            AssertionsAssert.Same(culture, CultureInfo.CurrentCulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void SpanFormattingIsTruncatedLikeEnumerables()
    {
        var expected = Enumerable.Range(0, 1000).ToArray();
        var actual = Enumerable.Range(0, 1000).ToArray();
        actual[500] = -1;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal((ReadOnlyMemory<int>)expected, (ReadOnlyMemory<int>)actual), """
            Assert.Equal() assertion failed: Item at index 500 differs.
            Expected expression: (ReadOnlyMemory<int>)expected
            Actual expression:   (ReadOnlyMemory<int>)actual
            Index of first difference: 500
            Expected item: [0, 1, 2, ..., 498, 499, 5̲0̲0̲, 501, 502, ...]
            Actual item:   [0, 1, 2, ..., 498, 499, -̲1̲, 501, 502, ...]
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual((ReadOnlySpan<int>)expected, (ReadOnlySpan<int>)expected), """
            Assert.NotEqual() assertion failed.
            Expected expression: (ReadOnlySpan<int>)expected
            Actual expression:   (ReadOnlySpan<int>)expected
            Not expected: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            Actual:       [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public void ObservesAsManyItemsAsTheFormatterWrites()
    {
        var singleFormatter = new TestAssertionFormatter { MaxFormattedItems = 1 };
        using var singleSnapshot = CollectionSnapshot.Create(LazyRange(100));
        singleSnapshot.TryGetItem(1, out _);

        AssertionsAssert.EndsWith("Actual: [0, 1̲, 2, 3, 4, 5, ...]", singleFormatter.Format(new CollectionSingleAssertionError<int>(singleSnapshot, "actual")));

        var suffixFormatter = new TestAssertionFormatter { MaxFormattedItems = 3, SuffixItemCount = 5 };
        using var suffixSnapshot = CollectionSnapshot.Create(LazyRange(100));
        suffixSnapshot.TryGetItem(1, out _);

        AssertionsAssert.EndsWith("Actual: [0, 1̲, 2, 3, 4, 5, 6, ...]", suffixFormatter.Format(new CollectionSingleAssertionError<int>(suffixSnapshot, "actual")));
    }

    [Fact]
    public void WritesEllipsisWhenSequenceEndsWhileSkippingItems()
    {
        var formatter = new TestAssertionFormatter { HighlightedContextItemCount = 0 };

        AssertionsAssert.Equal("[0, 1, 2, 3, 4, 5, ...]", formatter.FormatValueForTest(LazyRange(25), highlightedIndex: 25));
        AssertionsAssert.Equal("[0, 1, 2, 3, 4, 5, ...]", formatter.FormatSpanForTest<int>(Enumerable.Range(0, 25).ToArray(), highlightedIndex: 25));
    }

    private static IEnumerable<int> LazyRange(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return i;
        }
    }

    private static IEnumerable<int> YieldThenThrow()
    {
        yield return 1;
        yield return 2;
        throw new InvalidOperationException("MoveNext failed");
    }

    private static async IAsyncEnumerable<int> YieldThenThrowAsync()
    {
        await Task.Yield();
        yield return 1;
        yield return 2;
        throw new InvalidOperationException("MoveNext failed");
    }

    private sealed class ThrowingToString
    {
        public override string ToString() => throw new InvalidOperationException("line 1\nline 2");
    }

    private sealed record SelfReferencingNode
    {
        public SelfReferencingNode? Next { get; set; }
    }

    private sealed record RecordWithDouble(double Value);

    private sealed class ThrowingEnumerable : System.Collections.IEnumerable
    {
        public System.Collections.IEnumerator GetEnumerator() => throw new InvalidOperationException("GetEnumerator failed");
    }

    private sealed class ThrowingCurrentEnumerable : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => new Enumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IEnumerator<int>
        {
            public int Current => throw new InvalidOperationException("Current failed");

            object System.Collections.IEnumerator.Current => Current;

            public bool MoveNext() => true;

            public void Reset()
            {
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class TestAssertionFormatter : AssertionFormatter
    {
        public string FormatValueForTest(object? value, int? highlightedIndex = null)
        {
            return FormatValue(value, highlightedIndex);
        }

        public string FormatSpanForTest<T>(ReadOnlySpan<T> value, int? highlightedIndex = null)
        {
            return FormatReadOnlySpanValue(value, highlightedIndex);
        }
    }
}
