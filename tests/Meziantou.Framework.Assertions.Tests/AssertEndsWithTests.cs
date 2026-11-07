using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertEndsWithTests
{
    [Fact]
    public void Value_Success()
    {
        AssertionsAssert.EndsWith(3, new[] { 1, 2, 3 }.AsSpan());
        AssertionsAssert.EndsWith("c", new[] { "A", "B", "C" }.AsSpan(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Value_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(4, new[] { 1, 2, 3 }.AsSpan()), """
            Assert.EndsWith() assertion failed.
            Expected expression: 4
            Actual expression:   new[] { 1, 2, 3 }.AsSpan()
            Expected suffix: 4
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public void ValueEnumerable_Success()
    {
        AssertionsAssert.EndsWith(3, Enumerable.Range(1, 3));
        AssertionsAssert.EndsWith("c", new[] { "A", "B", "C" }.AsEnumerable(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueEnumerable_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(4, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected suffix: 4
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public void ValueEnumerable_FailsWhenActualIsNull()
    {
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(4, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected suffix: 4
            Actual:          <null>
            """);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionsAssert.EndsWith(3, actual);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(4, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected suffix: 4
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public void ValueNonGenericEnumerable_FailsWhenActualIsNull()
    {
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(4, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected suffix: 4
            Actual:          <null>
            """);
    }

    [Fact]
    public void Span_Success()
    {
        AssertionsAssert.EndsWith<int>([3, 4], [1, 2, 3, 4]);
        AssertionsAssert.EndsWith<string>(["b", "c"], ["A", "B", "C"], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Span_FailsWhenItemDiffers()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith<int>([2, 4], [1, 2, 3]), """
            Assert.EndsWith() assertion failed.
            Expected expression: [2, 4]
            Actual expression:   [1, 2, 3]
            Index of first difference: 1
            Expected suffix: [2, 4̲]
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public void Span_FailsWhenActualIsShorter()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith<int>([1, 2, 3], [2, 3]), """
            Assert.EndsWith() assertion failed.
            Expected expression: [1, 2, 3]
            Actual expression:   [2, 3]
            Index of first difference: 2
            Expected suffix: [1, 2, 3̲]
            Actual:          [2, 3̲]
            """);
    }

    [Fact]
    public void String_Success()
    {
        AssertionsAssert.EndsWith("llo", "Hello");
        AssertionsAssert.EndsWith("LLO", "Hello", ignoreCase: true);
    }

    [Fact]
    public void String_Fails()
    {
        var expected = "WORLD";
        var actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: Ordinal
            Index of first difference: 0
            Expected suffix: "W̲ORLD"
            Actual:          "H̲ello"
            """);
    }

    [Fact]
    public void String_FailsWhenActualIsNull()
    {
        var expected = "WORLD";
        string? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: Ordinal
            Expected suffix: "WORLD"
            Actual:          <null>
            """);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_Success()
    {
        IEnumerable<string> expected = ["c", "d"];
        var actual = AssertionTestHelpers.ToAsyncEnumerable(["A", "B", "C", "D"]);

        await AssertionsAssert.EndsWith(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_FailsWhenItemDiffers()
    {
        IEnumerable<int> expected = [2, 4];
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected suffix: [2, 4̲]
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_FailsWhenActualIsNull()
    {
        IEnumerable<int> expected = [2, 3];
        IAsyncEnumerable<int>? actual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected suffix: [2, 3]
            Actual:          <null>
            """);
    }

    [Fact]
    public void NonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable expected = new object[] { "c", "d" };
        System.Collections.IEnumerable actual = new object[] { "A", "B", "C", "D" };

        AssertionsAssert.EndsWith(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonGenericEnumerable_FailsWhenItemDiffers()
    {
        System.Collections.IEnumerable expected = new object[] { 2, 4 };
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected suffix: [2, 4̲]
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public void NonGenericEnumerable_FailsWhenActualIsNull()
    {
        System.Collections.IEnumerable expected = new object[] { 2, 3 };
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected suffix: [2, 3]
            Actual:          <null>
            """);
    }

    [Fact]
    public void DoesNotEndWith_Success()
    {
        AssertionsAssert.DoesNotEndWith(2, [1, 2, 3]);
        AssertionsAssert.DoesNotEndWith("He", "hello");
    }

    [Fact]
    public void DoesNotEndWith_ValueEnumerableFailsWhenActualIsNull()
    {
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith(3, actual), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: 3
            Actual expression:   actual
            Not expected suffix: 3
            Actual:              <null>
            """);
    }

    [Fact]
    public void DoesNotEndWith_ValueNonGenericEnumerableFailsWhenActualIsNull()
    {
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith(3, actual), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: 3
            Actual expression:   actual
            Not expected suffix: 3
            Actual:              <null>
            """);
    }

    [Fact]
    public void DoesNotEndWith_StringFailsWhenActualIsNull()
    {
        var expected = "lo";
        string? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith(expected, actual, ignoreCase: true), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: OrdinalIgnoreCase
            Not expected suffix: "lo"
            Actual:              <null>
            """);
    }

    [Fact]
    public async Task DoesNotEndWith_AsyncEnumerableFailsWhenActualIsNull()
    {
        IEnumerable<int> expected = [2, 3];
        IAsyncEnumerable<int>? actual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotEndWith(expected, actual), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected suffix: [2, 3]
            Actual:              <null>
            """);
    }

    [Fact]
    public void DoesNotEndWith_NonGenericEnumerableFailsWhenActualIsNull()
    {
        System.Collections.IEnumerable expected = new object[] { 2, 3 };
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith(expected, actual), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected suffix: [2, 3]
            Actual:              <null>
            """);
    }

    [Fact]
    public void DoesNotEndWith_Fails()
    {
        var expected = 3;
        var actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith(expected, actual), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected suffix: 3
            Actual:              [1, 2, 3]
            """);
    }

    [Fact]
    public async Task DoesNotEndWith_AsyncEnumerableFails()
    {
        IEnumerable<int> expected = [2, 3];
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotEndWith(expected, actual), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected suffix: [2, 3]
            Actual:              [1, 2, 3]
            """);
    }

    [Fact]
    public void DoesNotEndWith_EnumeratesASingleUseSequenceOnlyOnce()
    {
        var actual = AssertionTestHelpers.SingleUse(1, 2, 3);

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotEndWith(3, actual));
    }


    [Fact]
    public void DoesNotEndWith_StringExpectedAgainstNonGenericCollection_Fails()
    {
        System.Collections.IEnumerable actual = new[] { "a", "b" };

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotEndWith("b", actual));
        AssertionsAssert.DoesNotEndWith("a", actual);
    }

    [Fact]
    public void EndsWith_StringExpectedAgainstNonGenericCollection_ComparesTheLastItem()
    {
        System.Collections.IEnumerable actual = new[] { "a", "b" };

        AssertionsAssert.EndsWith("b", actual);
        AssertionsAssert.EndsWith("B", actual, StringComparer.OrdinalIgnoreCase);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.EndsWith("a", actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.EndsWith("", actual));
    }

    [Fact]
    public void EndsWith_StringExpectedAgainstNonGenericCharSequence_ComparesTheSuffix()
    {
        System.Collections.IEnumerable actual = "abc";

        AssertionsAssert.EndsWith("bc", actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.EndsWith("b", actual));
        AssertionsAssert.DoesNotEndWith("b", actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotEndWith("bc", actual));
    }

    [Fact]
    public void String_PositionalMessage_ComparesTheSuffix()
    {
        AssertionsAssert.EndsWith("bc", "abc", "custom message");
        AssertionsAssert.DoesNotEndWith("b", "abc", "custom message");
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith("b", "abc", "custom message"), """
            Assert.EndsWith() assertion failed.
            Message: custom message
            Expected expression: "b"
            Actual expression:   "abc"
            Comparison: Ordinal
            Index of first difference: 0
            Expected suffix: "b̲"
            Actual:          "abc̲"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith("bc", "abc", "custom message"), """
            Assert.DoesNotEndWith() assertion failed.
            Message: custom message
            Expected expression: "bc"
            Actual expression:   "abc"
            Not expected suffix: "bc"
            Actual:              "abc"
            """);
    }

    [Fact]
    public void String_ObjectTypedArguments_CompareTheSuffix()
    {
        object expected = "bc";
        System.Collections.IEnumerable actual = "abc";
        System.Collections.IEnumerable actualChars = "abc".ToCharArray();

        AssertionsAssert.EndsWith(expected, actual, "custom message");
        AssertionsAssert.EndsWith(expected, actualChars, "custom message");
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotEndWith(expected, actual, "custom message"));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotEndWith(expected, actualChars, "custom message"));
    }

    [Fact]
    public void Char_ComparesTheLastCharacter()
    {
        AssertionsAssert.EndsWith('c', "abc");
        AssertionsAssert.DoesNotEndWith('b', "abc");
        AssertionsAssert.DoesNotEndWith('c', "");
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith('b', "abc"), """
            Assert.EndsWith() assertion failed.
            Expected expression: 'b'
            Actual expression:   "abc"
            Expected suffix: 'b'
            Actual:          "abc̲"
            """);
    }

    [Fact]
    public void NullStringOrArray_Fails()
    {
        string? text = null;
        int[]? array = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith('a', text), """
            Assert.EndsWith() assertion failed.
            Expected expression: 'a'
            Actual expression:   text
            Expected suffix: 'a'
            Actual:          <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith('a', text), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: 'a'
            Actual expression:   text
            Not expected suffix: 'a'
            Actual:              <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(1, array), """
            Assert.EndsWith() assertion failed.
            Expected expression: 1
            Actual expression:   array
            Expected suffix: 1
            Actual:          <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith(1, array), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: 1
            Actual expression:   array
            Not expected suffix: 1
            Actual:              <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith([1], array), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: [1]
            Actual expression:   array
            Not expected suffix: [1]
            Actual:              <null>
            """);
    }

    [Fact]
    public void Overloads_BindLikeContains()
    {
        List<object> objects = ["a", "b", "c"];
        List<int> numbers = [1, 2, 3];

        AssertionsAssert.EndsWith("c", objects);
        AssertionsAssert.DoesNotEndWith("b", objects);
        AssertionsAssert.EndsWith([3], numbers);
        AssertionsAssert.EndsWith([2, 3], numbers);
        AssertionsAssert.DoesNotEndWith([2], numbers);
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith([1, 3], numbers), """
            Assert.EndsWith() assertion failed.
            Expected expression: [1, 3]
            Actual expression:   numbers
            Index of first difference: 0
            Expected suffix: [1̲, 3]
            Actual:          [1, 2̲, 3]
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith([2, 3], numbers), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: [2, 3]
            Actual expression:   numbers
            Not expected suffix: [2, 3]
            Actual:              [1, 2, 3]
            """);
    }

    [Fact]
    public void SequenceOfObjects_IsComparedAsASuffixOrAsTheLastItem()
    {
        List<object> actual = [1, 2, 3];
        var array = new object[] { 2, 3 };
        List<object> actualWithArray = [1, array];

        AssertionsAssert.EndsWith(new object[] { 2, 3 }, actual);
        AssertionsAssert.EndsWith(new object[] { 2, 3 }, (System.Collections.IEnumerable)actual);
        AssertionsAssert.EndsWith(array, actualWithArray);
        AssertionsAssert.EndsWith(array, (System.Collections.IEnumerable)actualWithArray);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotEndWith(new object[] { 2, 3 }, actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotEndWith(array, actualWithArray));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotEndWith(array, (System.Collections.IEnumerable)actualWithArray));
        AssertionsAssert.DoesNotEndWith(new object[] { 1, 2 }, actual);
    }

    [Fact]
    public void StringComparison_Success()
    {
        AssertionsAssert.EndsWith("LO", "Hello", StringComparison.OrdinalIgnoreCase);
        AssertionsAssert.EndsWith("LO".AsSpan(), "Hello".AsSpan(), StringComparison.OrdinalIgnoreCase);
        AssertionsAssert.DoesNotEndWith("LO", "Hello", StringComparison.Ordinal);
        AssertionsAssert.DoesNotEndWith("LO".AsSpan(), "Hello".AsSpan(), StringComparison.Ordinal);
    }

    [Fact]
    public void StringComparison_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith("LO", "Hello", StringComparison.Ordinal), """
            Assert.EndsWith() assertion failed.
            Expected expression: "LO"
            Actual expression:   "Hello"
            Comparison: Ordinal
            Index of first difference: 0
            Expected suffix: "L̲O"
            Actual:          "Hell̲o"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith("LO".AsSpan(), "Hello".AsSpan(), StringComparison.Ordinal), """
            Assert.EndsWith() assertion failed.
            Expected expression: "LO".AsSpan()
            Actual expression:   "Hello".AsSpan()
            Comparison: Ordinal
            Index of first difference: 0
            Expected suffix: "L̲O"
            Actual:          "Hell̲o"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith("LO", "Hello", StringComparison.OrdinalIgnoreCase), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: "LO"
            Actual expression:   "Hello"
            Not expected suffix: "LO"
            Actual:              "Hello"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith("LO".AsSpan(), "Hello".AsSpan(), StringComparison.OrdinalIgnoreCase), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: "LO".AsSpan()
            Actual expression:   "Hello".AsSpan()
            Not expected suffix: "LO"
            Actual:              "Hello"
            """);
    }

    [Fact]
    public void CharSpan_IgnoreCase()
    {
        AssertionsAssert.EndsWith("LO".AsSpan(), "Hello".AsSpan(), ignoreCase: true);
        AssertionsAssert.DoesNotEndWith("LO".AsSpan(), "Hello".AsSpan());
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith("LO".AsSpan(), "Hello".AsSpan()), """
            Assert.EndsWith() assertion failed.
            Expected expression: "LO".AsSpan()
            Actual expression:   "Hello".AsSpan()
            Comparison: Ordinal
            Index of first difference: 0
            Expected suffix: "L̲O"
            Actual:          "Hell̲o"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith("LO".AsSpan(), "Hello".AsSpan(), ignoreCase: true), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: "LO".AsSpan()
            Actual expression:   "Hello".AsSpan()
            Not expected suffix: "LO"
            Actual:              "Hello"
            """);
    }

    [Fact]
    public void DoesNotEndWith_SpanSuffix()
    {
        AssertionsAssert.DoesNotEndWith<int>([1, 2], [1, 2, 3]);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith<int>([2, 3], [1, 2, 3]), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: [2, 3]
            Actual expression:   [1, 2, 3]
            Not expected suffix: [2, 3]
            Actual:              [1, 2, 3]
            """);
    }

    [Fact]
    public async Task DoesNotEndWith_NonGenericAndAsyncSuccess()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionsAssert.DoesNotEndWith(2, actual);
        AssertionsAssert.DoesNotEndWith(new object[] { 1, 2 }, actual);
        await AssertionsAssert.DoesNotEndWith([1, 2], AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]));
    }

    [Fact]
    public void EndsWithAndDoesNotEndWith_AreComplements()
    {
        string[] texts = ["", "c", "bc", "BC", "abc", "bcx"];
        foreach (var expected in texts)
        {
            foreach (var actual in texts)
            {
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(expected, actual), () => AssertionsAssert.DoesNotEndWith(expected, actual));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(expected, actual, "message"), () => AssertionsAssert.DoesNotEndWith(expected, actual, "message"));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(expected, actual, ignoreCase: true), () => AssertionsAssert.DoesNotEndWith(expected, actual, ignoreCase: true));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(expected, actual, StringComparison.OrdinalIgnoreCase), () => AssertionsAssert.DoesNotEndWith(expected, actual, StringComparison.OrdinalIgnoreCase));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(expected.AsSpan(), actual.AsSpan()), () => AssertionsAssert.DoesNotEndWith(expected.AsSpan(), actual.AsSpan()));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(expected.AsSpan(), actual.AsSpan(), ignoreCase: true), () => AssertionsAssert.DoesNotEndWith(expected.AsSpan(), actual.AsSpan(), ignoreCase: true));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith((object)expected, (System.Collections.IEnumerable)actual), () => AssertionsAssert.DoesNotEndWith((object)expected, (System.Collections.IEnumerable)actual));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith((System.Collections.IEnumerable)expected, (System.Collections.IEnumerable)actual), () => AssertionsAssert.DoesNotEndWith((System.Collections.IEnumerable)expected, (System.Collections.IEnumerable)actual));
            }

            foreach (var character in "cCbx")
            {
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(character, expected), () => AssertionsAssert.DoesNotEndWith(character, expected));
            }
        }

        int[][] sequences = [[], [3], [2, 3], [1, 2], [1, 2, 3]];
        foreach (var expected in sequences)
        {
            foreach (var actual in sequences)
            {
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(new ReadOnlySpan<int>(expected), new ReadOnlySpan<int>(actual)), () => AssertionsAssert.DoesNotEndWith(new ReadOnlySpan<int>(expected), new ReadOnlySpan<int>(actual)));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(expected, actual.AsEnumerable()), () => AssertionsAssert.DoesNotEndWith(expected, actual.AsEnumerable()));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(expected, (System.Collections.IEnumerable)actual), () => AssertionsAssert.DoesNotEndWith(expected, (System.Collections.IEnumerable)actual));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(expected.Cast<object>(), actual.Cast<object>().ToList()), () => AssertionsAssert.DoesNotEndWith(expected.Cast<object>(), actual.Cast<object>().ToList()));
            }

            foreach (var item in new[] { 0, 1, 2, 3 })
            {
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(item, expected), () => AssertionsAssert.DoesNotEndWith(item, expected));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(item, new ReadOnlySpan<int>(expected)), () => AssertionsAssert.DoesNotEndWith(item, new ReadOnlySpan<int>(expected)));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(item, expected.Select(i => i)), () => AssertionsAssert.DoesNotEndWith(item, expected.Select(i => i)));
                AssertContainsTests.AssertComplements(() => AssertionsAssert.EndsWith(item, (System.Collections.IEnumerable)expected), () => AssertionsAssert.DoesNotEndWith(item, (System.Collections.IEnumerable)expected));
            }
        }
    }

    [Fact]
    public async Task AsyncEndsWithAndDoesNotEndWith_AreComplements()
    {
        int[][] sequences = [[], [3], [2, 3], [1, 2], [1, 2, 3]];
        foreach (var expected in sequences)
        {
            foreach (var actual in sequences)
            {
                await AssertContainsTests.AssertComplementsAsync(() => AssertionsAssert.EndsWith(expected, AssertionTestHelpers.ToAsyncEnumerable(actual)), () => AssertionsAssert.DoesNotEndWith(expected, AssertionTestHelpers.ToAsyncEnumerable(actual)));
            }
        }
    }
}
