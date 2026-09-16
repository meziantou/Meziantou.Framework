using System.Runtime.CompilerServices;
using Meziantou.Xunit;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertionFormatterTests
{
    [Fact]
    public void AssertExposesFormatterOptions()
    {
        // Tests run in parallel and many of them depend on the options set by the module initializer, so the global
        // options are set back to the same instance instead of being swapped for another one.
        var options = AssertionsAssert.FormatterOptions;
        AssertionsAssert.FormatterOptions = options;

        AssertionsAssert.Same(options, AssertionsAssert.FormatterOptions);
        AssertionsAssert.Throws<ArgumentNullException>(() => AssertionsAssert.FormatterOptions = null!);
        AssertionsAssert.Throws<ArgumentNullException>(() => AssertionsAssert.UseFormatterOptions(null!));
    }

    [Fact]
    public void UseFormatterOptions_AppliesOnlyInsideTheScope()
    {
        using (AssertionsAssert.UseFormatterOptions(new FormatterOptions { MaxFormattedItems = 2 }))
        {
            AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(new[] { 1, 2, 3, 4 }), """
                Assert.Empty() assertion failed.
                Expression: new[] { 1, 2, 3, 4 }
                Actual: [1̲, 2, ...]
                """);

            using (AssertionsAssert.UseFormatterOptions(new FormatterOptions { MaxFormattedItems = 3 }))
            {
                AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(new[] { 1, 2, 3, 4 }), """
                    Assert.Empty() assertion failed.
                    Expression: new[] { 1, 2, 3, 4 }
                    Actual: [1̲, 2, 3, ...]
                    """);
            }

            AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(new[] { 1, 2, 3, 4 }), """
                Assert.Empty() assertion failed.
                Expression: new[] { 1, 2, 3, 4 }
                Actual: [1̲, 2, ...]
                """);
        }

        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(new[] { 1, 2, 3, 4 }), """
            Assert.Empty() assertion failed.
            Expression: new[] { 1, 2, 3, 4 }
            Actual: [1̲, 2, 3, 4]
            """);
    }

    [Fact]
    public async Task UseFormatterOptions_DoesNotLeakOutOfTheAsynchronousFlow()
    {
        await UseOptionsWithoutDisposing();

        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(new[] { 1, 2, 3, 4 }), """
            Assert.Empty() assertion failed.
            Expression: new[] { 1, 2, 3, 4 }
            Actual: [1̲, 2, 3, 4]
            """);

        static async Task UseOptionsWithoutDisposing()
        {
            await Task.Yield();
            _ = AssertionsAssert.UseFormatterOptions(new FormatterOptions { MaxFormattedItems = 1 });
            await Task.Yield();

            AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(new[] { 1, 2, 3, 4 }), """
                Assert.Empty() assertion failed.
                Expression: new[] { 1, 2, 3, 4 }
                Actual: [1̲, ...]
                """);
        }
    }

    [Fact]
    public void FormatterOptions_AreReadOnceForTheWholeMessage()
    {
        var options = new FormatterOptions { MaxFormattedItems = 3 };
        using var scope = AssertionsAssert.UseFormatterOptions(options);

        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(ChangeOptionsWhileEnumerating(options)), """
            Assert.Empty() assertion failed.
            Expression: ChangeOptionsWhileEnumerating(options)
            Actual: [0̲, 1, 2, ...]
            """);

        static IEnumerable<int> ChangeOptionsWhileEnumerating(FormatterOptions options)
        {
            for (var i = 0; i < 100; i++)
            {
                if (i == 2)
                {
                    options.MaxFormattedItems = 100;
                }

                yield return i;
            }
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
        AssertionsAssert.Throws<ArgumentOutOfRangeException>(() => options.MaxFormattedStringLength = 0);
    }

    [Fact]
    public void FormatterOptions_UsesLargerDefaults()
    {
        var options = new FormatterOptions();

        AssertionsAssert.Equal(20, options.MaxFormattedItems);
        AssertionsAssert.Equal(6, options.PrefixItemCount);
        AssertionsAssert.Equal(0, options.SuffixItemCount);
        AssertionsAssert.Equal(4, options.HighlightedContextItemCount);
        AssertionsAssert.Equal(10_000, options.MaxFormattedStringLength);
    }

    [Fact]
    public void FormatterOptions_SetToMaxValueDoNotOverflow()
    {
        var contextFormatter = new TestAssertionFormatter { MaxFormattedItems = 3, HighlightedContextItemCount = int.MaxValue };
        AssertionsAssert.Equal("[0, 1, 2, 3, 4̲]", contextFormatter.FormatValueForTest(Enumerable.Range(0, 5).ToArray(), highlightedIndex: 4));
        AssertionsAssert.Equal("[0, 1, 2, 3, 4̲]", contextFormatter.FormatSpanForTest<int>(Enumerable.Range(0, 5).ToArray(), highlightedIndex: 4));

        var suffixFormatter = new TestAssertionFormatter { MaxFormattedItems = 3, SuffixItemCount = int.MaxValue };
        AssertionsAssert.Equal("[0, 1̲, 2, 3, 4]", suffixFormatter.FormatValueForTest(Enumerable.Range(0, 5).ToArray(), highlightedIndex: 1));

        var prefixFormatter = new TestAssertionFormatter { MaxFormattedItems = 1, PrefixItemCount = int.MaxValue, HighlightedContextItemCount = int.MaxValue };
        using var snapshot = CollectionSnapshot.Create(LazyRange(5));
        snapshot.TryGetItem(1, out _);
        AssertionsAssert.EndsWith("Actual: [0, 1̲, 2, 3, 4]", prefixFormatter.Format(new CollectionSingleAssertionError<int>(snapshot, "actual")));

        var stringFormatter = new TestAssertionFormatter { MaxFormattedStringLength = int.MaxValue };
        AssertionsAssert.Equal("\"abc̲\"", stringFormatter.FormatValueForTest("abc", highlightedIndex: 2));
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
            Note: The values differ but are formatted identically.
            """);
    }

    [Fact]
    public void UnequalValuesFormattedIdenticallyShowTheirTypes()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal((object)0.1f, (object)0.1), """
            Assert.Equal() assertion failed.
            Expected expression: (object)0.1f
            Actual expression:   (object)0.1
            Expected: 0.1
            Actual:   0.1
            Expected type: System.Single
            Actual type:   System.Double
            """);

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(new object[] { 1, 0.1f }, new object[] { 1, 0.1 }), """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: new object[] { 1, 0.1f }
            Actual expression:   new object[] { 1, 0.1 }
            Index of first difference: 1
            Expected item: [1, 0̲.̲1̲]
            Actual item:   [1, 0̲.̲1̲]
            Expected item type: System.Single
            Actual item type:   System.Double
            """);
    }

    [Fact]
    public void UnequalValuesOfTheSameTypeFormattedIdenticallyAreExplained()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(new RecordWithString(null), new RecordWithString("")), """
            Assert.Equal() assertion failed.
            Expected expression: new RecordWithString(null)
            Actual expression:   new RecordWithString("")
            Expected: RecordWithString { Value =  }
            Actual:   RecordWithString { Value =  }
            Note: The values differ but are formatted identically.
            """);
    }

    [Fact]
    public void EscapesLineBreaksAndInvisibleCharactersInToString()
    {
        var formatter = new TestAssertionFormatter();

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(new RecordWithString("a\r\nb"), new RecordWithString("a\nb")), """
            Assert.Equal() assertion failed.
            Expected expression: new RecordWithString("a\r\nb")
            Actual expression:   new RecordWithString("a\nb")
            Expected: RecordWithString { Value = a\r\nb }
            Actual:   RecordWithString { Value = a\nb }
            """);
        AssertionsAssert.Equal("RecordWithString { Value = a\\u200Bb\\U000E007F }", formatter.FormatValueForTest(new RecordWithString("a\u200Bb\U000E007F")));

        // Backslashes and quotes are not escaped, so ordinary values are written exactly as their ToString.
        AssertionsAssert.Equal("RecordWithString { Value = C:\\temp \"x\" }", formatter.FormatValueForTest(new RecordWithString("C:\\temp \"x\"")));
    }

    [Fact]
    public void EscapesInvisibleCharactersOutsideTheBasicMultilingualPlane()
    {
        var formatter = new TestAssertionFormatter();

        AssertionsAssert.Equal("\"admin\\U000E007F\"", formatter.FormatValueForTest("admin\U000E007F"));
        AssertionsAssert.Equal("\"\\U0001D173\\U0001D17A\"", formatter.FormatValueForTest("\U0001D173\U0001D17A"));
        AssertionsAssert.Equal("\"a\\\u0332U\u03320\u03320\u03320\u0332E\u03320\u03320\u03324\u03321\u0332\"", formatter.FormatValueForTest("a\U000E0041", highlightedIndex: 2));
        AssertionsAssert.Equal("\"😀𝄞\"", formatter.FormatValueForTest("😀𝄞"));
    }

    [Fact]
    public void TruncatesLongStringsAroundTheHighlightedCharacter()
    {
        var formatter = new TestAssertionFormatter { MaxFormattedStringLength = 10 };

        AssertionsAssert.Equal("\"0123456789\"", formatter.FormatValueForTest("0123456789"));
        AssertionsAssert.Equal("\"0123456789\"... (length: 16)", formatter.FormatValueForTest("0123456789ABCDEF"));
        AssertionsAssert.Equal("\"0123̲456789\"... (length: 16)", formatter.FormatValueForTest("0123456789ABCDEF", highlightedIndex: 3));
        AssertionsAssert.Equal("...\"ABCDEF̲GHIJ\"... (length: 30)", formatter.FormatValueForTest("0123456789ABCDEFGHIJKLMNOPQRST", highlightedIndex: 15));
        AssertionsAssert.Equal("...\"6789ABCDEF\" (length: 16)", formatter.FormatValueForTest("0123456789ABCDEF", highlightedIndex: 16));
        AssertionsAssert.Equal("...\"6789ABCDEF̲\" (length: 16)", formatter.FormatValueForTest("0123456789ABCDEF", highlightedIndex: 15));
        AssertionsAssert.Equal("\"012345678\\n\"... (length: 16)", formatter.FormatValueForTest("012345678\nABCDEF".AsMemory()));
        AssertionsAssert.Equal("RecordWith...", formatter.FormatValueForTest(new RecordWithString("0123456789")));

        // A surrogate pair at the edge of the window is kept whole.
        var surrogateFormatter = new TestAssertionFormatter { MaxFormattedStringLength = 3 };
        AssertionsAssert.Equal("\"ab😀\"... (length: 6)", surrogateFormatter.FormatValueForTest("ab😀cd"));
        AssertionsAssert.Equal("...\"😀cd\" (length: 6)", surrogateFormatter.FormatValueForTest("ab😀cd", highlightedIndex: 6));
    }

    [Fact]
    public void TruncatesLongStringsInAssertionMessages()
    {
        var expected = new string('a', 100) + "b" + new string('c', 100);
        var actual = new string('a', 100) + "X" + new string('c', 50);

        using var scope = AssertionsAssert.UseFormatterOptions(new FormatterOptions { MaxFormattedStringLength = 10 });
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 100
            Expected: ..."aaaaab̲cccc"... (length: 201)
            Actual:   ..."aaaaaX̲cccc"... (length: 151)
            """);
    }

    [Fact]
    public void HugeStringsProduceABoundedMessage()
    {
        var expected = new string('a', 1_000_000) + "b";
        var actual = new string('a', 1_000_000) + "c";

        var exception = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(expected, actual));

        AssertionsAssert.HasCountLessThan(25_000, exception.Message);
        AssertionsAssert.Contains("aaaab̲\" (length: 1000001)", exception.Message);
        AssertionsAssert.Contains("aaaac̲\" (length: 1000001)", exception.Message);
    }

    [Fact]
    public void EnumerableYieldingNewInstancesOfItselfStopsAtMaxDepth()
    {
        const int MaxDepth = 16;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Null(new InfinitelyNestedEnumerable()), $"""
            Assert.Null() assertion failed.
            Expression: new InfinitelyNestedEnumerable()
            Expected: <null>
            Actual:   {new string('[', MaxDepth)}<max depth reached>{new string(']', MaxDepth)}
            """);
    }

    [Fact]
    public void StackTraceStartsAtTheCallingMethod()
    {
        var exception = AssertionsAssert.Throws<AssertionException>(ThrowAssertionFailure);

        var firstFrame = exception.StackTrace!.Split('\n')[0];
        AssertionsAssert.Contains(nameof(ThrowAssertionFailure), firstFrame);
    }

    [Fact]
    public void StackTraceOfUserExceptionsIsKept()
    {
        var exception = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotThrow(ThrowUserException));

        var innerException = AssertionsAssert.IsType<InvalidOperationException>(exception.InnerException);
        AssertionsAssert.Contains(nameof(ThrowUserException), innerException.StackTrace!.Split('\n')[0]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowAssertionFailure()
    {
        AssertionsAssert.True(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowUserException()
    {
        throw new InvalidOperationException("user code");
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

    private sealed record RecordWithString(string? Value);

    private sealed class InfinitelyNestedEnumerable : IEnumerable<InfinitelyNestedEnumerable>
    {
        public IEnumerator<InfinitelyNestedEnumerable> GetEnumerator()
        {
            yield return new InfinitelyNestedEnumerable();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

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
