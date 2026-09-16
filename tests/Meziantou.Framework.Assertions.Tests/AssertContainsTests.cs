using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertContainsTests
{
    [Fact]
    public void Value_Success()
    {
        AssertionsAssert.Contains(2, new[] { 1, 2, 3 }.AsSpan());
        AssertionsAssert.Contains("b", new[] { "A", "B", "C" }.AsSpan(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Value_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(4, new[] { 1, 2, 3 }.AsSpan()), """
            Assert.Contains() assertion failed.
            Expected expression: 4
            Actual expression:   new[] { 1, 2, 3 }.AsSpan()
            Expected item: 4
            Actual:        [1, 2, 3]
            """);
    }

    [Fact]
    public void ValueEnumerable_Success()
    {
        AssertionsAssert.Contains(2, Enumerable.Range(1, 3));
        AssertionsAssert.Contains("b", new[] { "A", "B", "C" }.AsEnumerable(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueCollection_UsesCollectionComparer()
    {
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };

        AssertionsAssert.Contains("A", actual);
        AssertionsAssert.DoesNotContain("B", actual);
    }

    [Fact]
    public void ValueEnumerable_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(4, actual), """
            Assert.Contains() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected item: 4
            Actual:        [1, 2, 3]
            """);
    }

    [Fact]
    public void ValueEnumerable_FailsWhenActualIsNull()
    {
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(4, actual), """
            Assert.Contains() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected item: 4
            Actual:        <null>
            """);
    }

    [Fact]
    public void PredicateEnumerable_Success()
    {
        IEnumerable<string> collection = ["A", "sample", "C"];

        AssertionsAssert.Contains(collection, item => item == "sample");
    }

    [Fact]
    public void PredicateEnumerable_Fails()
    {
        IEnumerable<string> collection = ["A", "B", "C"];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(collection, item => item == "sample"), """
            Assert.Contains() assertion failed.
            Expression:           collection
            Predicate expression: item => item == "sample"
            Matching items: []
            """);
    }

    [Fact]
    public void PredicateEnumerable_FailsWhenActualIsNull()
    {
        IEnumerable<string>? collection = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(collection, item => item == "sample"), """
            Assert.Contains() assertion failed.
            Expression:           collection
            Predicate expression: item => item == "sample"
            Actual: <null>
            """);
    }

    [Fact]
    public void Dictionary_Success()
    {
        var actual = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        var result = AssertionsAssert.Contains("A", actual);

        AssertionsAssert.Equal(1, result);
    }

    [Fact]
    public void ReadOnlyDictionary_Success()
    {
        IReadOnlyDictionary<string, int> actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        var result = AssertionsAssert.Contains("a", actual);

        AssertionsAssert.Equal(1, result);
    }

    [Fact]
    public void GenericDictionary_Success()
    {
        IDictionary<string, int> actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        var result = AssertionsAssert.Contains("b", actual);

        AssertionsAssert.Equal(2, result);
    }

    [Fact]
    public void Dictionary_Fails()
    {
        var actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("c", actual), """
            Assert.Contains() assertion failed.
            Expected key expression: "c"
            Actual expression:       actual
            Expected key: "c"
            Actual:       ["a": 1, "b": 2]
            """);
    }

    [Fact]
    public void Dictionary_FailsWhenActualIsNull()
    {
        Dictionary<string, int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("c", actual), """
            Assert.Contains() assertion failed.
            Expected key expression: "c"
            Actual expression:       actual
            Expected key: "c"
            Actual:       <null>
            """);
    }

    [Fact]
    public void KeyValuePairEnumerableComparer_Success()
    {
        IEnumerable<KeyValuePair<string, int>> actual =
        [
            new("a", 1),
            new("b", 2),
        ];

        var result = AssertionsAssert.Contains("A", actual, StringComparer.OrdinalIgnoreCase);

        AssertionsAssert.Equal(1, result);
    }

    [Fact]
    public void KeyValuePairEnumerable_Fails()
    {
        IEnumerable<KeyValuePair<string, int>> actual =
        [
            new("a", 1),
            new("b", 2),
        ];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("c", actual), """
            Assert.Contains() assertion failed.
            Expected key expression: "c"
            Actual expression:       actual
            Expected key: "c"
            Actual:       ["a": 1, "b": 2]
            """);
    }

    [Fact]
    public void KeyValuePairEnumerable_FailsWhenActualIsNull()
    {
        IEnumerable<KeyValuePair<string, int>>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("c", actual), """
            Assert.Contains() assertion failed.
            Expected key expression: "c"
            Actual expression:       actual
            Expected key: "c"
            Actual:       <null>
            """);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionsAssert.Contains(2, actual);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(4, actual), """
            Assert.Contains() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected item: 4
            Actual:        [1, 2, 3]
            """);
    }

    [Fact]
    public void ValueNonGenericEnumerable_FailsWhenActualIsNull()
    {
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(4, actual), """
            Assert.Contains() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected item: 4
            Actual:        <null>
            """);
    }

    [Fact]
    public void NonGenericDictionary_Success()
    {
        System.Collections.IDictionary actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        var result = AssertionsAssert.Contains("b", actual);

        AssertionsAssert.Equal(2, result);
    }

    [Fact]
    public void NonGenericDictionary_Fails()
    {
        System.Collections.IDictionary actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("c", actual), """
            Assert.Contains() assertion failed.
            Expected key expression: "c"
            Actual expression:       actual
            Expected key: "c"
            Actual:       ["a": 1, "b": 2]
            """);
    }

    [Fact]
    public void NonGenericDictionary_FailsWhenActualIsNull()
    {
        System.Collections.IDictionary? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("c", actual), """
            Assert.Contains() assertion failed.
            Expected key expression: "c"
            Actual expression:       actual
            Expected key: "c"
            Actual:       <null>
            """);
    }

    [Fact]
    public void Span_Success()
    {
        AssertionsAssert.Contains<int>([2, 3], [1, 2, 3, 4]);
        AssertionsAssert.Contains<string>(["b", "c"], ["A", "B", "C"], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Span_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains<int>([2, 4], [1, 2, 3]), """
            Assert.Contains() assertion failed.
            Expected expression: [2, 4]
            Actual expression:   [1, 2, 3]
            Expected: [2, 4]
            Actual:   [1, 2, 3]
            """);
    }

    [Fact]
    public void String_Success()
    {
        AssertionsAssert.Contains("ell", "Hello");
        AssertionsAssert.Contains("ELL", "Hello", ignoreCase: true);
    }

    [Fact]
    public void String_Fails()
    {
        var expected = "WORLD";
        var actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(expected, actual), """
            Assert.Contains() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: Ordinal
            Expected: "WORLD"
            Actual:   "Hello"
            """);
    }

    [Fact]
    public void String_FailsWhenActualIsNull()
    {
        var expected = "WORLD";
        string? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(expected, actual), """
            Assert.Contains() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: Ordinal
            Expected: "WORLD"
            Actual:   <null>
            """);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_Success()
    {
        IEnumerable<string> expected = ["b", "c"];
        var actual = AssertionTestHelpers.ToAsyncEnumerable(["A", "B", "C", "D"]);

        await AssertionsAssert.Contains(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_Fails()
    {
        IEnumerable<int> expected = [2, 4];
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Contains(expected, actual), """
            Assert.Contains() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [2, 4]
            Actual:   [1, 2, 3]
            """);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_FailsWhenActualIsNull()
    {
        IEnumerable<int> expected = [2, 4];
        IAsyncEnumerable<int>? actual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Contains(expected, actual), """
            Assert.Contains() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [2, 4]
            Actual:   <null>
            """);
    }

    [Fact]
    public void NonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable expected = new object[] { "b", "c" };
        System.Collections.IEnumerable actual = new object[] { "A", "B", "C", "D" };

        AssertionsAssert.Contains(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable expected = new object[] { 2, 4 };
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(expected, actual), """
            Assert.Contains() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [2, 4]
            Actual:   [1, 2, 3]
            """);
    }

    [Fact]
    public void NonGenericEnumerable_FailsWhenActualIsNull()
    {
        System.Collections.IEnumerable expected = new object[] { 2, 4 };
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(expected, actual), """
            Assert.Contains() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [2, 4]
            Actual:   <null>
            """);
    }

    [Fact]
    public void DoesNotContain_Success()
    {
        AssertionsAssert.DoesNotContain(4, [1, 2, 3]);
        AssertionsAssert.DoesNotContain("z", "abc");
    }

    [Fact]
    public void DoesNotContain_ValueEnumerableFailsWhenActualIsNull()
    {
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(4, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Not expected item: 4
            Actual:            <null>
            """);
    }

    [Fact]
    public void DoesNotContain_ValueCollectionFailsWhenActualIsNull()
    {
        ICollection<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(4, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Not expected item: 4
            Actual:            <null>
            """);
    }

    [Fact]
    public void DoesNotContain_PredicateEnumerableFailsWhenActualIsNull()
    {
        IEnumerable<string>? collection = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(collection, item => item == "sample"), """
            Assert.DoesNotContain() assertion failed.
            Expression:           collection
            Predicate expression: item => item == "sample"
            Actual: <null>
            """);
    }

    [Fact]
    public void DoesNotContain_KeyValuePairEnumerableFailsWhenActualIsNull()
    {
        IEnumerable<KeyValuePair<string, int>>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("key", actual), """
            Assert.DoesNotContain() assertion failed.
            Expected key expression: "key"
            Actual expression:       actual
            Not expected key: "key"
            Actual:           <null>
            """);
    }

    [Fact]
    public void DoesNotContain_DictionaryFailsWhenActualIsNull()
    {
        Dictionary<string, int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("key", actual), """
            Assert.DoesNotContain() assertion failed.
            Expected key expression: "key"
            Actual expression:       actual
            Not expected key: "key"
            Actual:           <null>
            """);
    }

    [Fact]
    public void DoesNotContain_ValueNonGenericEnumerableFailsWhenActualIsNull()
    {
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(4, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Not expected: 4
            Actual:       <null>
            """);
    }

    [Fact]
    public void DoesNotContain_NonGenericDictionaryFailsWhenActualIsNull()
    {
        System.Collections.IDictionary? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("key", actual), """
            Assert.DoesNotContain() assertion failed.
            Expected key expression: "key"
            Actual expression:       actual
            Not expected key: "key"
            Actual:           <null>
            """);
    }

    [Fact]
    public void DoesNotContain_StringFailsWhenActualIsNull()
    {
        var expected = "z";
        string? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(expected, actual, ignoreCase: true), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: OrdinalIgnoreCase
            Not expected: "z"
            Actual:       <null>
            """);
    }

    [Fact]
    public async Task DoesNotContain_AsyncEnumerableFailsWhenActualIsNull()
    {
        IEnumerable<int> expected = [2, 3];
        IAsyncEnumerable<int>? actual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotContain(expected, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: [2, 3]
            Actual:       <null>
            """);
    }

    [Fact]
    public void DoesNotContain_NonGenericEnumerableFailsWhenActualIsNull()
    {
        System.Collections.IEnumerable expected = new object[] { 2, 3 };
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(expected, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: [2, 3]
            Actual:       <null>
            """);
    }

    [Fact]
    public void DoesNotContain_Fails()
    {
        var expected = 2;
        var actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(expected, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of found item: 1
            Not expected item: 2
            Actual:            [1, 2̲, 3]
            """);
    }

    [Fact]
    public async Task DoesNotContain_AsyncEnumerableFails()
    {
        IEnumerable<int> expected = [2, 3];
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3, 4]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotContain(expected, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of found subsequence: 1
            Not expected: [2, 3]
            Actual:       [1, 2̲, 3, 4]
            """);
    }

    [Fact]
    public void DoesNotContainPredicate_Success()
    {
        IEnumerable<string> collection = ["A", "B", "C"];

        AssertionsAssert.DoesNotContain(collection, item => item == "sample");
    }

    [Fact]
    public void DoesNotContainPredicate_Fails()
    {
        IEnumerable<string> collection = ["sample", "B", "sample"];

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(collection, item => item == "sample"), """
            Assert.DoesNotContain() assertion failed.
            Expression:           collection
            Predicate expression: item => item == "sample"
            Not expected:   any matching item
            Matching items: ["sample", "sample"]
            """);
    }

    [Fact]
    public void DoesNotContainDictionary_Fails()
    {
        var actual = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("a", actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: "a"
            Actual expression:   actual
            Index of found key: 0
            Not expected key: "a"
            Actual:           [[̲"̲a̲"̲,̲ ̲1̲]̲]
            """);
    }

    [Fact]
    public void DoesNotContain_EnumeratesASingleUseSequenceOnlyOnce()
    {
        var actual = AssertionTestHelpers.SingleUse(1, 2, 3);

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain(2, actual));
    }


    [Fact]
    public void DoesNotContain_StringExpectedAgainstNonGenericCollection_Fails()
    {
        System.Collections.IEnumerable actual = new[] { "a", "b" };

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain("b", actual));
        AssertionsAssert.DoesNotContain("c", actual);
    }

    [Fact]
    public void DoesNotContain_StringExpectedAgainstHashtableKeys_Fails()
    {
        var table = new System.Collections.Hashtable { { "a", 1 }, { "b", 2 } };

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain("b", table.Keys));
    }

    [Fact]
    public void Contains_StringExpectedAgainstNonGenericCollection_SearchesForTheItem()
    {
        System.Collections.IEnumerable actual = new[] { "a", "b" };

        AssertionsAssert.Contains("b", actual);
        AssertionsAssert.Contains("B", actual, StringComparer.OrdinalIgnoreCase);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Contains("B", actual));
        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("", actual), """
            Assert.Contains() assertion failed.
            Expected expression: ""
            Actual expression:   actual
            Expected item: ""
            Actual:        ["a", "b"]
            """);
    }

    [Fact]
    public void Contains_StringExpectedAgainstNonGenericCharSequence_SearchesForTheSubsequence()
    {
        System.Collections.IEnumerable actual = "abcd";

        AssertionsAssert.Contains("bc", actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Contains("ca", actual));
        AssertionsAssert.DoesNotContain("ca", actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain("bc", actual));
    }

    [Fact]
    public void DoesNotContain_KeyValuePairEnumerableUsesTheDictionaryComparerLikeContains()
    {
        IReadOnlyDictionary<string, int> actual = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1 };

        AssertionsAssert.Equal(1, AssertionsAssert.Contains("a", actual));
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("a", actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: "a"
            Actual expression:   actual
            Not expected key: "a"
            Actual:           [["A", 1]]
            """);

        AssertionsAssert.Equal(1, AssertionsAssert.Contains("a", actual, StringComparer.Ordinal));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain("a", actual, StringComparer.Ordinal));

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Contains("b", actual, StringComparer.OrdinalIgnoreCase));
        AssertionsAssert.DoesNotContain("b", actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoesNotContain_KeyValuePairEnumerableUsesTheComparerLikeContains()
    {
        IEnumerable<KeyValuePair<string, int>> actual = [new("A", 1)];

        AssertionsAssert.Equal(1, AssertionsAssert.Contains("a", actual, StringComparer.OrdinalIgnoreCase));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain("a", actual, StringComparer.OrdinalIgnoreCase));
        AssertionsAssert.DoesNotContain("a", actual);
    }

    [Fact]
    public void DoesNotContain_KeyValuePairEnumerableSucceedsForNullKeyAgainstDictionary()
    {
        IReadOnlyDictionary<string, int> actual = new Dictionary<string, int>(StringComparer.Ordinal) { ["A"] = 1 };
        string key = null!;

        AssertionsAssert.DoesNotContain(key, actual);
    }

    [Fact]
    public void DoesNotContain_ValueEnumerableFailsOnEndlessSequence()
    {
        var actual = AssertionTestHelpers.EndlessSequence();

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(5, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 5
            Actual expression:   actual
            Index of found item: 5
            Not expected item: 5
            Actual:            [0, 1, 2, 3, 4, 5̲, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public void DoesNotContain_KeyValuePairEnumerableFailsOnEndlessSequence()
    {
        var actual = AssertionTestHelpers.EndlessSequence().Select(i => KeyValuePair.Create(i, i));

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(5, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 5
            Actual expression:   actual
            Index of found key: 5
            Not expected key: 5
            Actual:           [[0, 0], [1, 1], [2, 2], [3, 3], [4, 4], [̲5̲,̲ ̲5̲]̲, [6, 6], [7, 7], [8, 8], [9, 9], ...]
            """);
    }

    [Fact]
    public void DoesNotContain_ValueNonGenericEnumerableFailsOnEndlessSequence()
    {
        System.Collections.IEnumerable actual = AssertionTestHelpers.EndlessSequence();

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(5, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 5
            Actual expression:   actual
            Index of found item: 5
            Not expected: 5
            Actual:       [0, 1, 2, 3, 4, 5̲, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public void DoesNotContain_StringExpectedAgainstNonGenericEndlessSequenceFails()
    {
        System.Collections.IEnumerable actual = AssertionTestHelpers.EndlessSequence().Select(i => i.ToString(CultureInfo.InvariantCulture));

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("5", actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: "5"
            Actual expression:   actual
            Index of found item: 5
            Not expected: "5"
            Actual:       ["0", "1", "2", "3", "4", "̲5̲"̲, "6", "7", "8", "9", ...]
            """);
    }

    [Fact]
    public void String_PositionalMessage_SearchesForTheSubstring()
    {
        AssertionsAssert.Contains("b", "abc", "custom message");
        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("z", "abc", "custom message"), """
            Assert.Contains() assertion failed.
            Message: custom message
            Expected expression: "z"
            Actual expression:   "abc"
            Comparison: Ordinal
            Expected: "z"
            Actual:   "abc"
            """);
    }

    [Fact]
    public void DoesNotContain_StringPositionalMessage_SearchesForTheSubstring()
    {
        var log = "the secret is 42";

        AssertionsAssert.DoesNotContain("password", log, "must not leak");
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("secret", log, "must not leak"), """
            Assert.DoesNotContain() assertion failed.
            Message: must not leak
            Expected expression: "secret"
            Actual expression:   log
            Index of found substring: 4
            Not expected: "secret"
            Actual:       "the s̲ecret is 42"
            """);
    }

    [Fact]
    public void String_ObjectTypedArguments_SearchForTheSubstring()
    {
        object expected = "secret";
        System.Collections.IEnumerable actual = "the secret is 42";
        System.Collections.IEnumerable actualChars = "the secret is 42".ToCharArray();

        AssertionsAssert.Contains(expected, actual, "custom message");
        AssertionsAssert.Contains(expected, actualChars, "custom message");
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain(expected, actual, "custom message"));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain(expected, actualChars, "custom message"));
    }

    [Fact]
    public void Char_SearchesTheString()
    {
        AssertionsAssert.Contains('b', "abc");
        AssertionsAssert.Contains('b', "abc", "custom message");
        AssertionsAssert.Contains('B', "abc", new IgnoreCaseCharComparer());
        AssertionsAssert.DoesNotContain('z', "abc");
        AssertionsAssert.DoesNotContain('z', "abc", "custom message");
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain('b', "abc"), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 'b'
            Actual expression:   "abc"
            Index of found item: 1
            Not expected item: 'b'
            Actual:            "ab̲c"
            """);
    }

    [Fact]
    public void Char_FailsWhenStringIsNull()
    {
        string? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains('x', actual), """
            Assert.Contains() assertion failed.
            Expected expression: 'x'
            Actual expression:   actual
            Expected item: 'x'
            Actual:        <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain('x', actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 'x'
            Actual expression:   actual
            Not expected item: 'x'
            Actual:            <null>
            """);
    }

    [Fact]
    public void Array_FailsWhenActualIsNull()
    {
        int[]? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(1, actual), """
            Assert.Contains() assertion failed.
            Expected expression: 1
            Actual expression:   actual
            Expected item: 1
            Actual:        <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(1, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 1
            Actual expression:   actual
            Not expected item: 1
            Actual:            <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain([1], actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: [1]
            Actual expression:   actual
            Not expected: [1]
            Actual:       <null>
            """);
    }

    [Fact]
    public void ValueEnumerable_UsesTheComparerOfTheCollection()
    {
        IEnumerable<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Alice" };

        AssertionsAssert.Contains("ALICE", names);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Contains("ALICE", names, StringComparer.Ordinal));
        AssertionsAssert.DoesNotContain("ALICE", names, StringComparer.Ordinal);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("ALICE", names), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: "ALICE"
            Actual expression:   names
            Not expected item: "ALICE"
            Actual:            ["Alice"]
            """);
    }

    [Fact]
    public void Subsequence_StopsAtTheFirstMatchOfAnEndlessSequence()
    {
        AssertionsAssert.Contains([2, 3], AssertionTestHelpers.EndlessSequence());
        AssertionsAssert.Contains(new[] { 2, 3 }, (System.Collections.IEnumerable)AssertionTestHelpers.EndlessSequence());
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain([2, 3], AssertionTestHelpers.EndlessSequence()), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: [2, 3]
            Actual expression:   AssertionTestHelpers.EndlessSequence()
            Index of found subsequence: 2
            Not expected: [2, 3]
            Actual:       [0, 1, 2̲, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(new[] { 2, 3 }, (System.Collections.IEnumerable)AssertionTestHelpers.EndlessSequence()), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: new[] { 2, 3 }
            Actual expression:   (System.Collections.IEnumerable)AssertionTestHelpers.EndlessSequence()
            Index of found subsequence: 2
            Not expected: [2, 3]
            Actual:       [0, 1, 2̲, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public async Task AsyncSubsequence_StopsAtTheFirstMatchOfAnEndlessSequence()
    {
        await AssertionsAssert.Contains([2, 3], AssertionTestHelpers.ToAsyncEnumerable(AssertionTestHelpers.EndlessSequence()));
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotContain([2, 3], AssertionTestHelpers.ToAsyncEnumerable(AssertionTestHelpers.EndlessSequence())), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: [2, 3]
            Actual expression:   AssertionTestHelpers.ToAsyncEnumerable(AssertionTestHelpers.EndlessSequence())
            Index of found subsequence: 2
            Not expected: [2, 3]
            Actual:       [0, 1, 2̲, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public void SequenceOfObjects_IsSearchedAsASubsequence()
    {
        List<object> actual = [1, 2, 3];
        System.Collections.IEnumerable nonGenericActual = actual;

        AssertionsAssert.Contains(new object[] { 1, 2 }, actual);
        AssertionsAssert.Contains(new object[] { 1, 2 }, nonGenericActual);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(new object[] { 1, 2 }, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: new object[] { 1, 2 }
            Actual expression:   actual
            Index of found subsequence: 0
            Not expected: [1, 2]
            Actual:       [1̲, 2, 3]
            """);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain(new object[] { 1, 2 }, nonGenericActual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Contains(new object[] { 1, 3 }, actual));
        AssertionsAssert.DoesNotContain(new object[] { 1, 3 }, actual);
        AssertionsAssert.DoesNotContain(new object[] { 1, 3 }, nonGenericActual);
    }

    [Fact]
    public void SequenceThatIsAnItem_IsFoundAsAnItem()
    {
        var item = new List<int> { 1 };
        List<object> actual = [item];
        System.Collections.IEnumerable nonGenericActual = new object[] { item };
        var array = new object[] { 1, 2 };
        List<object> actualWithArray = [array, 3];

        AssertionsAssert.Contains(item, actual);
        AssertionsAssert.Contains(item, nonGenericActual);
        AssertionsAssert.Contains(array, actualWithArray);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain(item, actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain(item, nonGenericActual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain(array, actualWithArray));
    }

    [Fact]
    public void Dictionary_NullKey_Fails()
    {
        string? key = null;
        var dictionary = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 };
        IReadOnlyDictionary<string, int> readOnlyDictionary = dictionary;
        System.Collections.IDictionary hashtable = new System.Collections.Hashtable { ["a"] = 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(key!, dictionary), """
            Assert.Contains() assertion failed.
            Expected key expression: key
            Actual expression:       dictionary
            Expected key: <null>
            Actual:       ["a": 1]
            """);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Contains(key!, readOnlyDictionary));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Contains((object?)key, hashtable));
        AssertionsAssert.DoesNotContain(key!, dictionary);
        AssertionsAssert.DoesNotContain(key!, readOnlyDictionary);
        AssertionsAssert.DoesNotContain((object?)key, hashtable);
    }

    [Fact]
    public void Dictionary_DictionaryComparerIsFinal()
    {
        var key = new string('a', 3);
        var otherKey = new string('a', 3);
        IReadOnlyDictionary<string, int> actual = new Dictionary<string, int>(ReferenceEqualityComparer.Instance) { [key] = 1 };

        AssertionsAssert.Equal(1, AssertionsAssert.Contains(key, actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Contains(otherKey, actual));
        AssertionsAssert.DoesNotContain(otherKey, actual);
        AssertionsAssert.Equal(1, AssertionsAssert.Contains(otherKey, actual, StringComparer.Ordinal));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotContain(otherKey, actual, StringComparer.Ordinal));
    }

    [Fact]
    public void DoesNotContain_ShowsTheFoundItem()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(500, Enumerable.Range(0, 1000).ToList()), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 500
            Actual expression:   Enumerable.Range(0, 1000).ToList()
            Index of found item: 500
            Not expected item: 500
            Actual:            [0, 1, 2, ..., 498, 499, 5̲0̲0̲, 501, 502, ...]
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(500, Enumerable.Range(0, 1000).ToArray()), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 500
            Actual expression:   Enumerable.Range(0, 1000).ToArray()
            Index of found item: 500
            Not expected item: 500
            Actual:            [0, 1, 2, ..., 498, 499, 5̲0̲0̲, 501, 502, ...]
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(500, Enumerable.Range(0, 1000)), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: 500
            Actual expression:   Enumerable.Range(0, 1000)
            Index of found item: 500
            Not expected item: 500
            Actual:            [0, 1, 2, ..., 498, 499, 5̲0̲0̲, 501, 502, ...]
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain([500, 501], Enumerable.Range(0, 1000)), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: [500, 501]
            Actual expression:   Enumerable.Range(0, 1000)
            Index of found subsequence: 500
            Not expected: [500, 501]
            Actual:       [0, 1, 2, ..., 498, 499, 5̲0̲0̲, 501, 502, ...]
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("World", "Hello World"), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: "World"
            Actual expression:   "Hello World"
            Index of found substring: 6
            Not expected: "World"
            Actual:       "Hello W̲orld"
            """);
    }

    [Fact]
    public async Task DoesNotContain_AsyncShowsTheFoundSubsequence()
    {
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotContain([500, 501], AssertionTestHelpers.ToAsyncEnumerable(Enumerable.Range(0, 1000))), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: [500, 501]
            Actual expression:   AssertionTestHelpers.ToAsyncEnumerable(Enumerable.Range(0, 1000))
            Index of found subsequence: 500
            Not expected: [500, 501]
            Actual:       [0, 1, 2, ..., 498, 499, 5̲0̲0̲, 501, 502, ...]
            """);
    }

    [Fact]
    public void StringComparison_Success()
    {
        AssertionsAssert.Contains("ELL", "Hello", StringComparison.OrdinalIgnoreCase);
        AssertionsAssert.Contains("ELL".AsSpan(), "Hello".AsSpan(), StringComparison.OrdinalIgnoreCase);
        AssertionsAssert.DoesNotContain("ELL", "Hello", StringComparison.Ordinal);
        AssertionsAssert.DoesNotContain("ELL".AsSpan(), "Hello".AsSpan(), StringComparison.Ordinal);
    }

    [Fact]
    public void StringComparison_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("ELL", "Hello", StringComparison.Ordinal), """
            Assert.Contains() assertion failed.
            Expected expression: "ELL"
            Actual expression:   "Hello"
            Comparison: Ordinal
            Expected: "ELL"
            Actual:   "Hello"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("ELL".AsSpan(), "Hello".AsSpan(), StringComparison.Ordinal), """
            Assert.Contains() assertion failed.
            Expected expression: "ELL".AsSpan()
            Actual expression:   "Hello".AsSpan()
            Comparison: Ordinal
            Expected: "ELL"
            Actual:   "Hello"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("ELL", "Hello", StringComparison.OrdinalIgnoreCase), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: "ELL"
            Actual expression:   "Hello"
            Index of found substring: 1
            Not expected: "ELL"
            Actual:       "He̲llo"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("ELL".AsSpan(), "Hello".AsSpan(), StringComparison.OrdinalIgnoreCase), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: "ELL".AsSpan()
            Actual expression:   "Hello".AsSpan()
            Index of found substring: 1
            Not expected: "ELL"
            Actual:       "He̲llo"
            """);
    }

    [Fact]
    public void CharSpan_IgnoreCase()
    {
        AssertionsAssert.Contains("ELL".AsSpan(), "Hello".AsSpan(), ignoreCase: true);
        AssertionsAssert.DoesNotContain("ELL".AsSpan(), "Hello".AsSpan());
        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("ELL".AsSpan(), "Hello".AsSpan()), """
            Assert.Contains() assertion failed.
            Expected expression: "ELL".AsSpan()
            Actual expression:   "Hello".AsSpan()
            Comparison: Ordinal
            Expected: "ELL"
            Actual:   "Hello"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("ELL".AsSpan(), "Hello".AsSpan(), ignoreCase: true), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: "ELL".AsSpan()
            Actual expression:   "Hello".AsSpan()
            Index of found substring: 1
            Not expected: "ELL"
            Actual:       "He̲llo"
            """);
    }

    [Fact]
    public void DoesNotContain_SpanSubsequence()
    {
        AssertionsAssert.DoesNotContain<int>([2, 4], [1, 2, 3]);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain<int>([2, 3], [1, 2, 3]), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: [2, 3]
            Actual expression:   [1, 2, 3]
            Index of found subsequence: 1
            Not expected: [2, 3]
            Actual:       [1, 2̲, 3]
            """);
    }

    [Fact]
    public async Task DoesNotContain_NonGenericAndAsyncSuccess()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionsAssert.DoesNotContain(4, actual);
        AssertionsAssert.DoesNotContain(new object[] { 3, 2 }, actual);
        await AssertionsAssert.DoesNotContain([3, 2], AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]));
    }

    [Fact]
    public void ContainsAndDoesNotContain_AreComplements()
    {
        string[] texts = ["", "a", "abc", "ABC", "xabcx"];
        foreach (var expected in texts)
        {
            foreach (var actual in texts)
            {
                AssertComplements(() => AssertionsAssert.Contains(expected, actual), () => AssertionsAssert.DoesNotContain(expected, actual));
                AssertComplements(() => AssertionsAssert.Contains(expected, actual, "message"), () => AssertionsAssert.DoesNotContain(expected, actual, "message"));
                AssertComplements(() => AssertionsAssert.Contains(expected, actual, ignoreCase: true), () => AssertionsAssert.DoesNotContain(expected, actual, ignoreCase: true));
                AssertComplements(() => AssertionsAssert.Contains(expected, actual, StringComparison.OrdinalIgnoreCase), () => AssertionsAssert.DoesNotContain(expected, actual, StringComparison.OrdinalIgnoreCase));
                AssertComplements(() => AssertionsAssert.Contains(expected.AsSpan(), actual.AsSpan()), () => AssertionsAssert.DoesNotContain(expected.AsSpan(), actual.AsSpan()));
                AssertComplements(() => AssertionsAssert.Contains(expected.AsSpan(), actual.AsSpan(), ignoreCase: true), () => AssertionsAssert.DoesNotContain(expected.AsSpan(), actual.AsSpan(), ignoreCase: true));
                AssertComplements(() => AssertionsAssert.Contains((object)expected, (System.Collections.IEnumerable)actual), () => AssertionsAssert.DoesNotContain((object)expected, (System.Collections.IEnumerable)actual));
                AssertComplements(() => AssertionsAssert.Contains((System.Collections.IEnumerable)expected, (System.Collections.IEnumerable)actual), () => AssertionsAssert.DoesNotContain((System.Collections.IEnumerable)expected, (System.Collections.IEnumerable)actual));
            }

            foreach (var character in "aAbx")
            {
                AssertComplements(() => AssertionsAssert.Contains(character, expected), () => AssertionsAssert.DoesNotContain(character, expected));
            }
        }

        int[][] sequences = [[], [1], [2, 3], [1, 2, 3], [3, 2]];
        foreach (var expected in sequences)
        {
            foreach (var actual in sequences)
            {
                AssertComplements(() => AssertionsAssert.Contains(new ReadOnlySpan<int>(expected), new ReadOnlySpan<int>(actual)), () => AssertionsAssert.DoesNotContain(new ReadOnlySpan<int>(expected), new ReadOnlySpan<int>(actual)));
                AssertComplements(() => AssertionsAssert.Contains(expected, actual.AsEnumerable()), () => AssertionsAssert.DoesNotContain(expected, actual.AsEnumerable()));
                AssertComplements(() => AssertionsAssert.Contains(expected, (System.Collections.IEnumerable)actual), () => AssertionsAssert.DoesNotContain(expected, (System.Collections.IEnumerable)actual));
                AssertComplements(() => AssertionsAssert.Contains(expected.Cast<object>(), actual.Cast<object>().ToList()), () => AssertionsAssert.DoesNotContain(expected.Cast<object>(), actual.Cast<object>().ToList()));
            }

            foreach (var item in new[] { 0, 1, 2, 3 })
            {
                AssertComplements(() => AssertionsAssert.Contains(item, expected), () => AssertionsAssert.DoesNotContain(item, expected));
                AssertComplements(() => AssertionsAssert.Contains(item, new ReadOnlySpan<int>(expected)), () => AssertionsAssert.DoesNotContain(item, new ReadOnlySpan<int>(expected)));
                AssertComplements(() => AssertionsAssert.Contains(item, expected.ToList()), () => AssertionsAssert.DoesNotContain(item, expected.ToList()));
                AssertComplements(() => AssertionsAssert.Contains(item, expected.Select(i => i)), () => AssertionsAssert.DoesNotContain(item, expected.Select(i => i)));
                AssertComplements(() => AssertionsAssert.Contains(item, (System.Collections.IEnumerable)expected), () => AssertionsAssert.DoesNotContain(item, (System.Collections.IEnumerable)expected));
                AssertComplements(() => AssertionsAssert.Contains(item, expected.ToDictionary(i => i, i => i)), () => AssertionsAssert.DoesNotContain(item, expected.ToDictionary(i => i, i => i)));
            }
        }
    }

    [Fact]
    public async Task AsyncContainsAndDoesNotContain_AreComplements()
    {
        int[][] sequences = [[], [1], [2, 3], [1, 2, 3], [3, 2]];
        foreach (var expected in sequences)
        {
            foreach (var actual in sequences)
            {
                await AssertComplementsAsync(() => AssertionsAssert.Contains(expected, AssertionTestHelpers.ToAsyncEnumerable(actual)), () => AssertionsAssert.DoesNotContain(expected, AssertionTestHelpers.ToAsyncEnumerable(actual)));
            }
        }
    }

    internal static void AssertComplements(Action positive, Action negative)
    {
        var positiveFailed = Fails(positive);
        var negativeFailed = Fails(negative);
        global::Xunit.Assert.True(positiveFailed != negativeFailed, positiveFailed ? "Both assertions failed" : "Both assertions succeeded");
    }

    internal static async Task AssertComplementsAsync(Func<Task> positive, Func<Task> negative)
    {
        var positiveFailed = await FailsAsync(positive);
        var negativeFailed = await FailsAsync(negative);
        global::Xunit.Assert.True(positiveFailed != negativeFailed, positiveFailed ? "Both assertions failed" : "Both assertions succeeded");
    }

    private static bool Fails(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (AssertionException)
        {
            return true;
        }
    }

    private static async Task<bool> FailsAsync(Func<Task> action)
    {
        try
        {
            await action();
            return false;
        }
        catch (AssertionException)
        {
            return true;
        }
    }

    private sealed class IgnoreCaseCharComparer : IEqualityComparer<char>
    {
        public bool Equals(char x, char y) => char.ToUpperInvariant(x) == char.ToUpperInvariant(y);

        public int GetHashCode(char obj) => char.ToUpperInvariant(obj).GetHashCode();
    }
}
