using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertStartsWithTests
{
    [Fact]
    public void Value_Success()
    {
        AssertionsAssert.StartsWith(1, [1, 2, 3]);
        AssertionsAssert.StartsWith("a", ["A", "b"], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Value_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(1, [2, 3]), """
            Assert.StartsWith() assertion failed.
            Expected expression: 1
            Actual expression:   [2, 3]
            Expected prefix: 1
            Actual:          [2̲, 3]
            """);
    }

    [Fact]
    public void ValueEnumerable_Success()
    {
        AssertionsAssert.StartsWith(1, Enumerable.Range(1, 3));
        AssertionsAssert.StartsWith("a", new[] { "A", "b" }.AsEnumerable(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueEnumerable_Fails()
    {
        IEnumerable<int> actual = [2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(1, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: 1
            Actual expression:   actual
            Expected prefix: 1
            Actual:          [2̲, 3]
            """);
    }

    [Fact]
    public void ValueEnumerable_FailsWhenActualIsNull()
    {
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(1, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: 1
            Actual expression:   actual
            Expected prefix: 1
            Actual:          <null>
            """);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionsAssert.StartsWith(1, actual);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable actual = new object[] { 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(1, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: 1
            Actual expression:   actual
            Expected prefix: 1
            Actual:          [2̲, 3]
            """);
    }

    [Fact]
    public void ValueNonGenericEnumerable_FailsWhenActualIsNull()
    {
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(1, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: 1
            Actual expression:   actual
            Expected prefix: 1
            Actual:          <null>
            """);
    }

    [Fact]
    public void Span_Success()
    {
        AssertionsAssert.StartsWith<int>([1, 2], [1, 2, 3]);
        AssertionsAssert.StartsWith<string>(["a", "b"], ["A", "B", "c"], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Span_FailsWhenItemDiffers()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith<int>([1, 2], [1, 42, 3]), """
            Assert.StartsWith() assertion failed.
            Expected expression: [1, 2]
            Actual expression:   [1, 42, 3]
            Index of first difference: 1
            Expected prefix: [1, 2̲]
            Actual:          [1, 4̲2̲, 3]
            """);
    }

    [Fact]
    public void Span_FailsWhenActualIsShorter()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith<int>([1, 2, 3], [1, 2]), """
            Assert.StartsWith() assertion failed.
            Expected expression: [1, 2, 3]
            Actual expression:   [1, 2]
            Index of first difference: 2
            Expected prefix: [1, 2, 3̲]
            Actual:          [1, 2]
            """);
    }

    [Fact]
    public void CharSpan_Success()
    {
        AssertionsAssert.StartsWith("Hel".AsSpan(), "Hello".AsSpan());
        AssertionsAssert.StartsWith("hel".AsSpan(), "Hello".AsSpan(), ignoreCase: true);
    }

    [Fact]
    public void CharSpan_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith("He".AsSpan(), "hello".AsSpan()), """
            Assert.StartsWith() assertion failed.
            Expected expression: "He".AsSpan()
            Actual expression:   "hello".AsSpan()
            Comparison: Ordinal
            Index of first difference: 0
            Expected prefix: "H̲e"
            Actual:          "h̲ello"
            """);
    }

    [Fact]
    public void String_Success()
    {
        AssertionsAssert.StartsWith("Hel", "Hello");
        AssertionsAssert.StartsWith("hel", "Hello", ignoreCase: true);
    }

    [Fact]
    public void String_Fails()
    {
        var expected = "He";
        var actual = "hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(expected, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: Ordinal
            Index of first difference: 0
            Expected prefix: "H̲e"
            Actual:          "h̲ello"
            """);
    }

    [Fact]
    public void String_FailsWhenActualIsNull()
    {
        var expected = "He";
        string? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(expected, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: Ordinal
            Expected prefix: "He"
            Actual:          <null>
            """);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_Success()
    {
        IEnumerable<string> expected = ["a", "b"];
        var actual = AssertionTestHelpers.ToAsyncEnumerable(["A", "B", "c"]);

        await AssertionsAssert.StartsWith(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_FailsWhenItemDiffers()
    {
        IEnumerable<int> expected = Enumerable.Range(0, 20).ToArray();
        var actualValues = Enumerable.Range(0, 20).ToArray();
        actualValues[12] = 42;
        var actual = AssertionTestHelpers.ToAsyncEnumerable(actualValues);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.StartsWith(expected, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 12
            Expected prefix: [0, 1, 2, ..., 10, 11, 1̲2̲, 13, 14, ...]
            Actual:          [0, 1, 2, ..., 10, 11, 4̲2̲, 13, 14, ...]
            """);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_FailsWhenActualIsNull()
    {
        IEnumerable<int> expected = [1, 2];
        IAsyncEnumerable<int>? actual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.StartsWith(expected, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected prefix: [1, 2]
            Actual:          <null>
            """);
    }

    [Fact]
    public void NonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable expected = new object[] { "a", "b" };
        System.Collections.IEnumerable actual = new object[] { "A", "B", "c" };

        AssertionsAssert.StartsWith(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonGenericEnumerable_FailsWhenActualIsShorter()
    {
        System.Collections.IEnumerable expected = new object[] { 1, 2, 3 };
        System.Collections.IEnumerable actual = new object[] { 1, 2 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(expected, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 2
            Expected prefix: [1, 2, 3̲]
            Actual:          [1, 2]
            """);
    }

    [Fact]
    public void NonGenericEnumerable_FailsWhenActualIsNull()
    {
        System.Collections.IEnumerable expected = new object[] { 1, 2 };
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(expected, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected prefix: [1, 2]
            Actual:          <null>
            """);
    }

    [Fact]
    public void DoesNotStartWith_Success()
    {
        AssertionsAssert.DoesNotStartWith(2, [1, 2, 3]);
        AssertionsAssert.DoesNotStartWith("He", "hello");
    }

    [Fact]
    public void DoesNotStartWith_ValueEnumerableFailsWhenActualIsNull()
    {
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith(1, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: 1
            Actual expression:   actual
            Not expected prefix: 1
            Actual:              <null>
            """);
    }

    [Fact]
    public void DoesNotStartWith_ValueNonGenericEnumerableFailsWhenActualIsNull()
    {
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith(1, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: 1
            Actual expression:   actual
            Not expected prefix: 1
            Actual:              <null>
            """);
    }

    [Fact]
    public void DoesNotStartWith_StringFailsWhenActualIsNull()
    {
        var expected = "He";
        string? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith(expected, actual, ignoreCase: true), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: OrdinalIgnoreCase
            Not expected prefix: "He"
            Actual:              <null>
            """);
    }

    [Fact]
    public async Task DoesNotStartWith_AsyncEnumerableFailsWhenActualIsNull()
    {
        IEnumerable<int> expected = [1, 2];
        IAsyncEnumerable<int>? actual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotStartWith(expected, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected prefix: [1, 2]
            Actual:              <null>
            """);
    }

    [Fact]
    public void DoesNotStartWith_NonGenericEnumerableFailsWhenActualIsNull()
    {
        System.Collections.IEnumerable expected = new object[] { 1, 2 };
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith(expected, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected prefix: [1, 2]
            Actual:              <null>
            """);
    }

    [Fact]
    public void DoesNotStartWith_Fails()
    {
        var expected = 1;
        var actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith(expected, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected prefix: 1
            Actual:              [1, 2, 3]
            """);
    }

    [Fact]
    public async Task DoesNotStartWith_AsyncEnumerableFails()
    {
        IEnumerable<int> expected = [1, 2];
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotStartWith(expected, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected prefix: [1, 2]
            Actual:              [1, 2, 3]
            """);
    }

    [Fact]
    public void DoesNotStartWith_EnumeratesASingleUseSequenceOnlyOnce()
    {
        var actual = AssertionTestHelpers.SingleUse(1, 2, 3);

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotStartWith(1, actual));
    }


    [Fact]
    public void DoesNotStartWith_StringExpectedAgainstNonGenericCollection_Fails()
    {
        System.Collections.IEnumerable actual = new[] { "a", "b" };

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotStartWith("a", actual));
        AssertionsAssert.DoesNotStartWith("b", actual);
    }

    [Fact]
    public void StartsWith_StringExpectedAgainstNonGenericCollection_ComparesTheFirstItem()
    {
        System.Collections.IEnumerable actual = new[] { "a", "b" };

        AssertionsAssert.StartsWith("a", actual);
        AssertionsAssert.StartsWith("A", actual, StringComparer.OrdinalIgnoreCase);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.StartsWith("b", actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.StartsWith("", actual));
    }

    [Fact]
    public void StartsWith_StringExpectedAgainstNonGenericCharSequence_ComparesThePrefix()
    {
        System.Collections.IEnumerable actual = "abc";

        AssertionsAssert.StartsWith("ab", actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.StartsWith("b", actual));
        AssertionsAssert.DoesNotStartWith("b", actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotStartWith("ab", actual));
    }

    [Fact]
    public void DoesNotStartWith_ValueEnumerableFailsOnEndlessSequence()
    {
        var actual = AssertionTestHelpers.EndlessSequence();

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith(0, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: 0
            Actual expression:   actual
            Not expected prefix: 0
            Actual:              [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public void DoesNotStartWith_ValueNonGenericEnumerableFailsOnEndlessSequence()
    {
        System.Collections.IEnumerable actual = AssertionTestHelpers.EndlessSequence();

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith(0, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: 0
            Actual expression:   actual
            Not expected prefix: 0
            Actual:              [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public void DoesNotStartWith_StringExpectedAgainstNonGenericEndlessSequenceFails()
    {
        System.Collections.IEnumerable actual = AssertionTestHelpers.EndlessSequence().Select(i => i.ToString(CultureInfo.InvariantCulture));

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith("0", actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: "0"
            Actual expression:   actual
            Not expected prefix: "0"
            Actual:              ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9", ...]
            """);
    }

    [Fact]
    public async Task DoesNotStartWith_AsyncEnumerableFailsOnEndlessSequence()
    {
        IEnumerable<int> expected = [0, 1];
        var actual = AssertionTestHelpers.ToAsyncEnumerable(AssertionTestHelpers.EndlessSequence());

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotStartWith(expected, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected prefix: [0, 1]
            Actual:              [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public void DoesNotStartWith_NonGenericEnumerableFailsOnEndlessSequence()
    {
        System.Collections.IEnumerable expected = new object[] { 0, 1 };
        System.Collections.IEnumerable actual = AssertionTestHelpers.EndlessSequence();

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith(expected, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected prefix: [0, 1]
            Actual:              [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public void String_PositionalMessage_ComparesThePrefix()
    {
        AssertionsAssert.StartsWith("ab", "abc", "custom message");
        AssertionsAssert.DoesNotStartWith("b", "abc", "custom message");
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith("b", "abc", "custom message"), """
            Assert.StartsWith() assertion failed.
            Message: custom message
            Expected expression: "b"
            Actual expression:   "abc"
            Comparison: Ordinal
            Index of first difference: 0
            Expected prefix: "b̲"
            Actual:          "a̲bc"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith("ab", "abc", "custom message"), """
            Assert.DoesNotStartWith() assertion failed.
            Message: custom message
            Expected expression: "ab"
            Actual expression:   "abc"
            Not expected prefix: "ab"
            Actual:              "abc"
            """);
    }

    [Fact]
    public void String_ObjectTypedArguments_CompareThePrefix()
    {
        object expected = "ab";
        System.Collections.IEnumerable actual = "abc";
        System.Collections.IEnumerable actualChars = "abc".ToCharArray();

        AssertionsAssert.StartsWith(expected, actual, "custom message");
        AssertionsAssert.StartsWith(expected, actualChars, "custom message");
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotStartWith(expected, actual, "custom message"));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotStartWith(expected, actualChars, "custom message"));
    }

    [Fact]
    public void Char_ComparesTheFirstCharacter()
    {
        AssertionsAssert.StartsWith('a', "abc");
        AssertionsAssert.DoesNotStartWith('b', "abc");
        AssertionsAssert.DoesNotStartWith('a', "");
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith('b', "abc"), """
            Assert.StartsWith() assertion failed.
            Expected expression: 'b'
            Actual expression:   "abc"
            Expected prefix: 'b'
            Actual:          "a̲bc"
            """);
    }

    [Fact]
    public void NullStringOrArray_Fails()
    {
        string? text = null;
        int[]? array = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith('a', text), """
            Assert.StartsWith() assertion failed.
            Expected expression: 'a'
            Actual expression:   text
            Expected prefix: 'a'
            Actual:          <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith('a', text), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: 'a'
            Actual expression:   text
            Not expected prefix: 'a'
            Actual:              <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(1, array), """
            Assert.StartsWith() assertion failed.
            Expected expression: 1
            Actual expression:   array
            Expected prefix: 1
            Actual:          <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith(1, array), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: 1
            Actual expression:   array
            Not expected prefix: 1
            Actual:              <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith([1], array), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: [1]
            Actual expression:   array
            Not expected prefix: [1]
            Actual:              <null>
            """);
    }

    [Fact]
    public void Overloads_BindLikeContains()
    {
        List<object> objects = ["a", "b"];
        List<int> numbers = [1, 2, 3];

        AssertionsAssert.StartsWith("a", objects);
        AssertionsAssert.DoesNotStartWith("b", objects);
        AssertionsAssert.StartsWith([1], numbers);
        AssertionsAssert.StartsWith([1, 2], numbers);
        AssertionsAssert.DoesNotStartWith([2], numbers);
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith([1, 3], numbers), """
            Assert.StartsWith() assertion failed.
            Expected expression: [1, 3]
            Actual expression:   numbers
            Index of first difference: 1
            Expected prefix: [1, 3̲]
            Actual:          [1, 2̲, 3]
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith([1, 2], numbers), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: [1, 2]
            Actual expression:   numbers
            Not expected prefix: [1, 2]
            Actual:              [1, 2, 3]
            """);
    }

    [Fact]
    public void SequenceOfObjects_IsComparedAsAPrefixOrAsTheFirstItem()
    {
        List<object> actual = [1, 2, 3];
        var array = new object[] { 1, 2 };
        List<object> actualWithArray = [array, 3];

        AssertionsAssert.StartsWith(new object[] { 1, 2 }, actual);
        AssertionsAssert.StartsWith(new object[] { 1, 2 }, (System.Collections.IEnumerable)actual);
        AssertionsAssert.StartsWith(array, actualWithArray);
        AssertionsAssert.StartsWith(array, (System.Collections.IEnumerable)actualWithArray);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotStartWith(new object[] { 1, 2 }, actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotStartWith(array, actualWithArray));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotStartWith(array, (System.Collections.IEnumerable)actualWithArray));
        AssertionsAssert.DoesNotStartWith(new object[] { 2, 3 }, actual);
    }

    [Fact]
    public void StringComparison_Success()
    {
        AssertionsAssert.StartsWith("HE", "Hello", StringComparison.OrdinalIgnoreCase);
        AssertionsAssert.StartsWith("HE".AsSpan(), "Hello".AsSpan(), StringComparison.OrdinalIgnoreCase);
        AssertionsAssert.DoesNotStartWith("HE", "Hello", StringComparison.Ordinal);
        AssertionsAssert.DoesNotStartWith("HE".AsSpan(), "Hello".AsSpan(), StringComparison.Ordinal);
    }

    [Fact]
    public void StringComparison_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith("HE", "Hello", StringComparison.Ordinal), """
            Assert.StartsWith() assertion failed.
            Expected expression: "HE"
            Actual expression:   "Hello"
            Comparison: Ordinal
            Index of first difference: 1
            Expected prefix: "HE̲"
            Actual:          "He̲llo"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith("HE".AsSpan(), "Hello".AsSpan(), StringComparison.Ordinal), """
            Assert.StartsWith() assertion failed.
            Expected expression: "HE".AsSpan()
            Actual expression:   "Hello".AsSpan()
            Comparison: Ordinal
            Index of first difference: 1
            Expected prefix: "HE̲"
            Actual:          "He̲llo"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith("HE", "Hello", StringComparison.OrdinalIgnoreCase), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: "HE"
            Actual expression:   "Hello"
            Not expected prefix: "HE"
            Actual:              "Hello"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith("HE".AsSpan(), "Hello".AsSpan(), StringComparison.OrdinalIgnoreCase), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: "HE".AsSpan()
            Actual expression:   "Hello".AsSpan()
            Not expected prefix: "HE"
            Actual:              "Hello"
            """);
    }

    [Fact]
    public void DoesNotStartWith_CharSpan()
    {
        AssertionsAssert.DoesNotStartWith("HE".AsSpan(), "Hello".AsSpan());
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith("HE".AsSpan(), "Hello".AsSpan(), ignoreCase: true), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: "HE".AsSpan()
            Actual expression:   "Hello".AsSpan()
            Not expected prefix: "HE"
            Actual:              "Hello"
            """);
    }

    [Fact]
    public void DoesNotStartWith_SpanPrefix()
    {
        AssertionsAssert.DoesNotStartWith<int>([2, 3], [1, 2, 3]);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith<int>([1, 2], [1, 2, 3]), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: [1, 2]
            Actual expression:   [1, 2, 3]
            Not expected prefix: [1, 2]
            Actual:              [1, 2, 3]
            """);
    }

    [Fact]
    public async Task DoesNotStartWith_NonGenericAndAsyncSuccess()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionsAssert.DoesNotStartWith(2, actual);
        AssertionsAssert.DoesNotStartWith(new object[] { 2, 3 }, actual);
        await AssertionsAssert.DoesNotStartWith([2, 3], AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]));
    }

    [Fact]
    public void StartsWithAndDoesNotStartWith_AreComplements()
    {
        string[] texts = ["", "a", "ab", "AB", "abc", "xab"];
        foreach (var expected in texts)
        {
            foreach (var actual in texts)
            {
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(expected, actual), () => AssertionsAssert.DoesNotStartWith(expected, actual));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(expected, actual, "message"), () => AssertionsAssert.DoesNotStartWith(expected, actual, "message"));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(expected, actual, ignoreCase: true), () => AssertionsAssert.DoesNotStartWith(expected, actual, ignoreCase: true));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(expected, actual, StringComparison.OrdinalIgnoreCase), () => AssertionsAssert.DoesNotStartWith(expected, actual, StringComparison.OrdinalIgnoreCase));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(expected.AsSpan(), actual.AsSpan()), () => AssertionsAssert.DoesNotStartWith(expected.AsSpan(), actual.AsSpan()));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(expected.AsSpan(), actual.AsSpan(), ignoreCase: true), () => AssertionsAssert.DoesNotStartWith(expected.AsSpan(), actual.AsSpan(), ignoreCase: true));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith((object)expected, (System.Collections.IEnumerable)actual), () => AssertionsAssert.DoesNotStartWith((object)expected, (System.Collections.IEnumerable)actual));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith((System.Collections.IEnumerable)expected, (System.Collections.IEnumerable)actual), () => AssertionsAssert.DoesNotStartWith((System.Collections.IEnumerable)expected, (System.Collections.IEnumerable)actual));
            }

            foreach (var character in "aAbx")
            {
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(character, expected), () => AssertionsAssert.DoesNotStartWith(character, expected));
            }
        }

        int[][] sequences = [[], [1], [1, 2], [2, 3], [1, 2, 3]];
        foreach (var expected in sequences)
        {
            foreach (var actual in sequences)
            {
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(new ReadOnlySpan<int>(expected), new ReadOnlySpan<int>(actual)), () => AssertionsAssert.DoesNotStartWith(new ReadOnlySpan<int>(expected), new ReadOnlySpan<int>(actual)));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(expected, actual.AsEnumerable()), () => AssertionsAssert.DoesNotStartWith(expected, actual.AsEnumerable()));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(expected, (System.Collections.IEnumerable)actual), () => AssertionsAssert.DoesNotStartWith(expected, (System.Collections.IEnumerable)actual));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(expected.Cast<object>(), actual.Cast<object>().ToList()), () => AssertionsAssert.DoesNotStartWith(expected.Cast<object>(), actual.Cast<object>().ToList()));
            }

            foreach (var item in new[] { 0, 1, 2, 3 })
            {
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(item, expected), () => AssertionsAssert.DoesNotStartWith(item, expected));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(item, new ReadOnlySpan<int>(expected)), () => AssertionsAssert.DoesNotStartWith(item, new ReadOnlySpan<int>(expected)));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(item, expected.Select(i => i)), () => AssertionsAssert.DoesNotStartWith(item, expected.Select(i => i)));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.StartsWith(item, (System.Collections.IEnumerable)expected), () => AssertionsAssert.DoesNotStartWith(item, (System.Collections.IEnumerable)expected));
            }
        }
    }

    [Fact]
    public async Task AsyncStartsWithAndDoesNotStartWith_AreComplements()
    {
        int[][] sequences = [[], [1], [1, 2], [2, 3], [1, 2, 3]];
        foreach (var expected in sequences)
        {
            foreach (var actual in sequences)
            {
                await AssertContainsTests.AssertComplementsAsync(() => AssertionsAssert.StartsWith(expected, AssertionTestHelpers.ToAsyncEnumerable(actual)), () => AssertionsAssert.DoesNotStartWith(expected, AssertionTestHelpers.ToAsyncEnumerable(actual)));
            }
        }
    }
}
