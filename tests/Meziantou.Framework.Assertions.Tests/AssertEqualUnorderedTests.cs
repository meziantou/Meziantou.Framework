using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertEqualUnorderedTests
{
    [Fact]
    public void Enumerable_Success()
    {
        IEnumerable<int> expected = [1, 2];
        IEnumerable<int> actual = [2, 1];

        AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public void EnumerableWithDuplicates_Success()
    {
        IEnumerable<int> expected = [1, 1, 2];
        IEnumerable<int> actual = [2, 1, 1];

        AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public void EnumerableWithDuplicatesAndNulls_Success()
    {
        IEnumerable<string?> expected = ["a", null, "a", "b"];
        IEnumerable<string?> actual = ["b", "a", null, "a"];

        AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public void EnumerableWithDuplicates_Fails()
    {
        IEnumerable<int> expected = [1, 1, 2];
        IEnumerable<int> actual = [1, 2, 2];

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Missing expected item index: 1
            Unexpected actual item index: 2
            Expected: [1, 1̲, 2]
            Actual:   [1, 2, 2̲]
            """);
    }

    [Fact]
    public void Enumerable_FailsWhenActualHasMissingItem()
    {
        IEnumerable<int> expected = [1, 2, 3];
        IEnumerable<int> actual = [2, 1];

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Missing expected item index: 2
            Expected: [1, 2, 3̲]
            Actual:   [2, 1]
            """);
    }

    [Fact]
    public void Enumerable_FailsWhenActualIsNull()
    {
        IEnumerable<int> expected = [1, 2, 3];
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [1, 2, 3]
            Actual:   <null>
            """);
    }

    [Fact]
    public void Enumerable_FailsWhenActualHasUnexpectedItem()
    {
        IEnumerable<int> expected = [1, 2];
        IEnumerable<int> actual = [2, 1, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Unexpected actual item index: 2
            Expected: [1, 2]
            Actual:   [2, 1, 3̲]
            """);
    }

    [Fact]
    public void EnumerableComparer_Success()
    {
        IEnumerable<string> expected = ["a", "b"];
        IEnumerable<string> actual = ["B", "A"];

        AssertionsAssert.EqualUnordered(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnumerableComparer_FailsWithMessage()
    {
        IEnumerable<string> expected = ["a", "b"];
        IEnumerable<string> actual = ["B", "c"];

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual, StringComparer.OrdinalIgnoreCase, "custom message"), """
            Assert.EqualUnordered() assertion failed.
            Message: custom message
            Expected expression: expected
            Actual expression:   actual
            Missing expected item index: 0
            Unexpected actual item index: 1
            Expected: ["̲a̲"̲, "b"]
            Actual:   ["B", "̲c̲"̲]
            """);
    }

    [Fact]
    public void DifferentEnumerableTypes_Success()
    {
        IEnumerable<int> expected = [1, 2, 3];
        IEnumerable<long> actual = [3L, 2L, 1L];

        AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public void NonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable expected = new object[] { 1, "a", 3 };
        System.Collections.IEnumerable actual = new object[] { 3L, "a", 1L };

        AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public void NonGenericEnumerable_FailsWhenActualIsNull()
    {
        System.Collections.IEnumerable expected = new object[] { 1, 2, 3 };
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [1, 2, 3]
            Actual:   <null>
            """);
    }

    [Fact]
    public async Task AsyncEnumerable_Success()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable([3, 2, 1]);

        await AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public async Task AsyncEnumerable_Fails()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 1, 2]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 2]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Missing expected item index: 1
            Unexpected actual item index: 2
            Expected: [1, 1̲, 2]
            Actual:   [1, 2, 2̲]
            """);
    }

    [Fact]
    public async Task AsyncEnumerable_FailsWhenActualIsNull()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);
        IAsyncEnumerable<int>? actual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [1, 2, 3]
            Actual:   <null>
            """);
    }

    [Fact]
    public void NotEqualUnordered_Success()
    {
        AssertionsAssert.NotEqualUnordered([1, 2], [1, 3]);

        IEnumerable<int> expected = [1, 2];
        IEnumerable<int>? actual = null;
        System.Collections.IEnumerable expectedNonGeneric = new object[] { 1, 2 };
        System.Collections.IEnumerable? actualNonGeneric = null;

        AssertionsAssert.NotEqualUnordered(expected, actual);
        AssertionsAssert.NotEqualUnordered(expectedNonGeneric, actualNonGeneric);
    }

    [Fact]
    public async Task NotEqualUnordered_AsyncEnumerableSucceedsWhenActualIsNull()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2]);
        IAsyncEnumerable<int>? actual = null;

        await AssertionsAssert.NotEqualUnordered(expected, actual);
    }

    [Fact]
    public void NotEqualUnordered_Fails()
    {
        var expected = new[] { 1, 2 };
        var actual = new[] { 2, 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqualUnordered(expected, actual), """
            Assert.NotEqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: [1, 2]
            Actual:       [2, 1]
            """);
    }

    [Fact]
    public async Task NotEqualUnordered_AsyncEnumerableFails()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable([2, 1]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.NotEqualUnordered(expected, actual), """
            Assert.NotEqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: [1, 2]
            Actual:       [2, 1]
            """);
    }

    [Fact]
    public void EqualUnordered_ComparerWithInconsistentHashCode_Success()
    {
        double[] expected = [1.0, 2.0];
        double[] actual = [1.1, 2.1];

        AssertionsAssert.EqualUnordered(expected, actual, new ToleranceComparer());
    }

    [Fact]
    public void NotEqualUnordered_ComparerWithInconsistentHashCode_Fails()
    {
        double[] expected = [1.0, 2.0];
        double[] actual = [1.1, 2.1];

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqualUnordered(expected, actual, new ToleranceComparer()), """
            Assert.NotEqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: [1, 2]
            Actual:       [1.1, 2.1]
            """);
    }

    [Fact]
    public void NonTransitiveComparer_DoesNotReuseAnActualItem()
    {
        int[] expected = [1, 2];
        int[] actual = [0, 0];
        var comparer = new NonTransitiveToleranceComparer();

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.EqualUnordered(expected, actual, comparer));
        AssertionsAssert.NotEqualUnordered(expected, actual, comparer);
    }

    [Fact]
    public void NonTransitiveComparer_FindsPairingGreedyMatchingMisses()
    {
        int[] expected = [2, 1, 0];
        int[] actual = [1, 3, 0];
        var comparer = new NonTransitiveToleranceComparer();

        AssertionsAssert.EqualUnordered(expected, actual, comparer);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(expected, actual, comparer));
    }

    [Fact]
    public void NonTransitiveComparer_FindsPairingWithDuplicates()
    {
        int[] expected = [2, 1, 1];
        int[] actual = [1, 0, 3];
        var comparer = new NonTransitiveToleranceComparer();

        AssertionsAssert.EqualUnordered(expected, actual, comparer);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(expected, actual, comparer));
    }

    [Fact]
    public async Task NonTransitiveComparer_AsyncEnumerable()
    {
        var comparer = new NonTransitiveToleranceComparer();

        await AssertionsAssert.EqualUnordered(AssertionTestHelpers.ToAsyncEnumerable([2, 1]), AssertionTestHelpers.ToAsyncEnumerable([1, 3]), comparer);
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.EqualUnordered(AssertionTestHelpers.ToAsyncEnumerable([1, 2]), AssertionTestHelpers.ToAsyncEnumerable([0, 0]), comparer));
        await AssertionsAssert.NotEqualUnordered(AssertionTestHelpers.ToAsyncEnumerable([1, 2]), AssertionTestHelpers.ToAsyncEnumerable([0, 0]), comparer);
    }

    [Fact]
    public void NonTransitiveNonGenericComparer_FindsPairingGreedyMatchingMisses()
    {
        System.Collections.IEnumerable expected = new object[] { 2, 1 };
        System.Collections.IEnumerable actual = new object[] { 1, 3 };
        var comparer = new NonTransitiveToleranceComparer();

        AssertionsAssert.EqualUnordered(expected, actual, comparer);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(expected, actual, comparer));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.EqualUnordered(new object[] { 1, 2 }, new object[] { 0, 0 }, comparer));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void NonTransitiveComparer_MatchesExhaustiveSearch(bool consistentHashCode)
    {
        var random = new Random(42);
        var comparer = new NonTransitiveToleranceComparer(consistentHashCode);
        for (var iteration = 0; iteration < 2000; iteration++)
        {
            var count = random.Next(0, 7);
            var expected = new int[count];
            var actual = new int[count];
            for (var index = 0; index < count; index++)
            {
                expected[index] = random.Next(0, 6);
                actual[index] = random.Next(0, 6);
            }

            var areEqual = HasPerfectMatching(expected, actual, comparer, new bool[count], 0);
            var equalUnorderedSucceeded = Succeeds(() => AssertionsAssert.EqualUnordered(expected, actual, comparer));
            var notEqualUnorderedSucceeded = Succeeds(() => AssertionsAssert.NotEqualUnordered(expected, actual, comparer));

            AssertionsAssert.Equal(areEqual, equalUnorderedSucceeded, $"EqualUnordered([{string.Join(", ", expected)}], [{string.Join(", ", actual)}])");
            AssertionsAssert.Equal(!areEqual, notEqualUnorderedSucceeded, $"NotEqualUnordered([{string.Join(", ", expected)}], [{string.Join(", ", actual)}])");

            object[] expectedObjects = [.. expected.Cast<object>()];
            object[] actualObjects = [.. actual.Cast<object>()];
            AssertionsAssert.Equal(areEqual, Succeeds(() => AssertionsAssert.EqualUnordered(expectedObjects, actualObjects, comparer)));
            AssertionsAssert.Equal(!areEqual, Succeeds(() => AssertionsAssert.NotEqualUnordered(expectedObjects, actualObjects, comparer)));
        }

        static bool HasPerfectMatching(int[] expected, int[] actual, NonTransitiveToleranceComparer comparer, bool[] used, int expectedIndex)
        {
            if (expectedIndex == expected.Length)
                return true;

            for (var actualIndex = 0; actualIndex < actual.Length; actualIndex++)
            {
                if (used[actualIndex] || !comparer.Equals(expected[expectedIndex], actual[actualIndex]))
                    continue;

                used[actualIndex] = true;
                if (HasPerfectMatching(expected, actual, comparer, used, expectedIndex + 1))
                    return true;

                used[actualIndex] = false;
            }

            return false;
        }

        static bool Succeeds(Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (AssertionException)
            {
                return false;
            }
        }
    }

    [Fact]
    public void LargeCollectionsWithAnUnbalancedDuplicate_Fails()
    {
        var expected = Enumerable.Repeat("a", 5_000).Append("b").ToArray();
        var actual = Enumerable.Repeat("a", 4_999).Append("b").Append("c").ToArray();

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.EqualUnordered(expected, actual, new NonTransitiveStringComparer()));
        AssertionsAssert.NotEqualUnordered(expected, actual);
        AssertionsAssert.NotEqualUnordered(expected, actual, new NonTransitiveStringComparer());
    }

    [Fact]
    public void DefaultComparer_ComparesEachItemAConstantNumberOfTimes()
    {
        var comparer = new CountingComparer();
        var expected = Enumerable.Range(0, 2000).ToArray();
        var actual = expected.Reverse().ToArray();

        AssertionsAssert.EqualUnordered(expected, actual, comparer);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(expected, actual, comparer));

        AssertionsAssert.True(comparer.EqualsCallCount <= 4 * expected.Length, $"Equals was called {comparer.EqualsCallCount} times");
    }

    [Fact]
    public void NonGeneric_ComparesEachItemAConstantNumberOfTimes()
    {
        var counter = new StrongBox<int>();
        var expected = Enumerable.Range(0, 2000).Select(value => new CountingItem(value, counter)).ToArray();
        var actual = expected.Reverse().Select(item => new CountingItem(item.Value, counter)).ToArray();

        AssertionsAssert.EqualUnordered((System.Collections.IEnumerable)expected, (System.Collections.IEnumerable)actual);
        AssertionsAssert.EqualUnordered<CountingItem, CountingItem>(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered((System.Collections.IEnumerable)expected, (System.Collections.IEnumerable)actual));

        AssertionsAssert.True(counter.Value <= 6 * expected.Length, $"Equals was called {counter.Value} times");
    }

    [Fact]
    public void NonGeneric_PairsValuesOfDifferentTypes()
    {
        System.Collections.IEnumerable expected = new object[] { 1, -2L, new[] { 3 }, null! };
        System.Collections.IEnumerable actual = new object[] { null!, new List<int> { 3 }, -2, 1L };

        AssertionsAssert.EqualUnordered(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(expected, actual));
        AssertionsAssert.EqualUnordered<int, long>([1, -2, 3], [3L, 1L, -2L]);
        AssertionsAssert.NotEqualUnordered<int, long>([1, -2, 3], [3L, 1L, 2L]);
    }

    [Fact]
    public async Task AsyncEnumerableDifferentTypes_Success()
    {
        await AssertionsAssert.EqualUnordered(AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]), AssertionTestHelpers.ToAsyncEnumerable([3L, 2L, 1L]));
        await AssertionsAssert.NotEqualUnordered(AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]), AssertionTestHelpers.ToAsyncEnumerable([3L, 2L, 4L]));
    }

    [Fact]
    public async Task AsyncEnumerableDifferentTypes_Fails()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 1, 2]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1L, 2L, 2L]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Missing expected item index: 1
            Unexpected actual item index: 2
            Expected: [1, 1̲, 2]
            Actual:   [1, 2, 2̲]
            """);
    }

    [Fact]
    public async Task AsyncEnumerableDifferentTypes_NotEqualUnorderedFails()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable([2L, 1L]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.NotEqualUnordered(expected, actual), """
            Assert.NotEqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: [1, 2]
            Actual:       [2, 1]
            """);
    }

    [Fact]
    public async Task AsyncEnumerableDifferentTypes_ActualIsNull()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);
        IAsyncEnumerable<long>? actual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [1, 2, 3]
            Actual:   <null>
            """);
        await AssertionsAssert.NotEqualUnordered(AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]), actual);
    }

    [Fact]
    public async Task BothNull_EqualUnorderedSucceeds()
    {
        IEnumerable<int>? expected = null;
        IEnumerable<int>? actual = null;
        IEnumerable<long>? actualLong = null;
        System.Collections.IEnumerable? expectedNonGeneric = null;
        System.Collections.IEnumerable? actualNonGeneric = null;
        IAsyncEnumerable<int>? expectedAsync = null;
        IAsyncEnumerable<int>? actualAsync = null;
        IAsyncEnumerable<long>? actualAsyncLong = null;

        AssertionsAssert.EqualUnordered(expected, actual);
        AssertionsAssert.EqualUnordered(expected, actual, comparer: null);
        AssertionsAssert.EqualUnordered(expected, actualLong);
        AssertionsAssert.EqualUnordered(expectedNonGeneric, actualNonGeneric);
        AssertionsAssert.EqualUnordered(expectedNonGeneric, actualNonGeneric, comparer: null);
        await AssertionsAssert.EqualUnordered(expectedAsync, actualAsync);
        await AssertionsAssert.EqualUnordered(expectedAsync, actualAsync, comparer: null);
        await AssertionsAssert.EqualUnordered(expectedAsync, actualAsyncLong);
    }

    [Fact]
    public async Task BothNull_NotEqualUnorderedFails()
    {
        IEnumerable<int>? expected = null;
        IEnumerable<int>? actual = null;
        const string ExpectedMessage = """
            Assert.NotEqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: <null>
            Actual:       <null>
            """;

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqualUnordered(expected, actual), ExpectedMessage);
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqualUnordered(expected, actual, comparer: null), ExpectedMessage);
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqualUnordered<int, long>(expected, actual: null), ExpectedMessage.Replace("Actual expression:   actual", "Actual expression:   null", StringComparison.Ordinal));

        System.Collections.IEnumerable? expectedNonGeneric = null;
        System.Collections.IEnumerable? actualNonGeneric = null;
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqualUnordered(expectedNonGeneric, actualNonGeneric), ExpectedMessage.Replace("expression: expected", "expression: expectedNonGeneric", StringComparison.Ordinal).Replace("expression:   actual", "expression:   actualNonGeneric", StringComparison.Ordinal));

        IAsyncEnumerable<int>? expectedAsync = null;
        IAsyncEnumerable<int>? actualAsync = null;
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.NotEqualUnordered(expectedAsync, actualAsync), ExpectedMessage.Replace("expression: expected", "expression: expectedAsync", StringComparison.Ordinal).Replace("expression:   actual", "expression:   actualAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExpectedIsNull_Fails()
    {
        IEnumerable<int>? expected = null;
        IEnumerable<int> actual = [1, 2];
        const string ExpectedMessage = """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: <null>
            Actual:   [1, 2]
            """;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), ExpectedMessage);
        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual, comparer: null), ExpectedMessage);
        AssertionsAssert.NotEqualUnordered(expected, actual);

        System.Collections.IEnumerable? expectedNonGeneric = null;
        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expectedNonGeneric, actual), ExpectedMessage.Replace("expression: expected", "expression: expectedNonGeneric", StringComparison.Ordinal));
        AssertionsAssert.NotEqualUnordered(expectedNonGeneric, actual);

        IAsyncEnumerable<int>? expectedAsync = null;
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.EqualUnordered(expectedAsync, AssertionTestHelpers.ToAsyncEnumerable([1, 2])), ExpectedMessage.Replace("expression: expected", "expression: expectedAsync", StringComparison.Ordinal).Replace("expression:   actual", "expression:   AssertionTestHelpers.ToAsyncEnumerable([1, 2])", StringComparison.Ordinal));
        await AssertionsAssert.NotEqualUnordered(expectedAsync, AssertionTestHelpers.ToAsyncEnumerable([1, 2]));
    }

    [Fact]
    public void NestedCollections_AreComparedByValue()
    {
        List<int[]> expected = [[1, 2], [3]];
        List<int[]> actual = [[3], [1, 2]];

        AssertionsAssert.EqualUnordered(expected, actual);
        AssertionsAssert.EqualUnordered(expected, actual, comparer: null);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(expected, actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(expected, actual, comparer: null));
        AssertionsAssert.NotEqualUnordered(expected, [[3], [2, 1]]);
    }

    [Fact]
    public void NestedCollections_FailsWhenContentDiffers()
    {
        List<int[]> expected = [[1, 2], [3]];
        List<int[]> actual = [[3], [2, 1]];

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Missing expected item index: 0
            Unexpected actual item index: 1
            Expected: [[̲1̲,̲ ̲2̲]̲, [3]]
            Actual:   [[3], [̲2̲,̲ ̲1̲]̲]
            """);
    }

    [Fact]
    public void NestedCollections_ExplicitDefaultComparerComparesByReference()
    {
        List<int[]> expected = [[1, 2]];
        List<int[]> actual = [[1, 2]];

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.EqualUnordered(expected, actual, EqualityComparer<int[]>.Default));
        AssertionsAssert.NotEqualUnordered(expected, actual, EqualityComparer<int[]>.Default);
    }

    [Fact]
    public async Task NestedCollections_AsyncEnumerableAreComparedByValue()
    {
        await AssertionsAssert.EqualUnordered(AssertionTestHelpers.ToAsyncEnumerable<int[]>([[1, 2], [3]]), AssertionTestHelpers.ToAsyncEnumerable<int[]>([[3], [1, 2]]));
        await AssertionsAssert.EqualUnordered(AssertionTestHelpers.ToAsyncEnumerable<int[]>([[1, 2], [3]]), AssertionTestHelpers.ToAsyncEnumerable<int[]>([[3], [1, 2]]), comparer: null);
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(AssertionTestHelpers.ToAsyncEnumerable<int[]>([[1, 2], [3]]), AssertionTestHelpers.ToAsyncEnumerable<int[]>([[3], [1, 2]])));
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(AssertionTestHelpers.ToAsyncEnumerable<int[]>([[1, 2], [3]]), AssertionTestHelpers.ToAsyncEnumerable<int[]>([[3], [1, 2]]), comparer: null));
    }

    [Fact]
    public void BoxedNumbers_AreComparedByValue()
    {
        List<object> expected = [1, 2.5, "a", (byte)4];
        List<object> actual = ["a", 4m, 2.5f, 1L];

        AssertionsAssert.EqualUnordered(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(expected, actual));
    }

    [Fact]
    public void DefaultImmutableArrayItems_Success()
    {
        List<ImmutableArray<int>> expected = [default, default];
        List<ImmutableArray<int>> actual = [default, default];

        AssertionsAssert.EqualUnordered(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(expected, actual));
    }

    [Fact]
    public void ComparerWithoutHashCode_Success()
    {
        var comparer = new NoHashCodeComparer();

        AssertionsAssert.EqualUnordered([1, 2, 3], [3, 1, 2], comparer);
        AssertionsAssert.NotEqualUnordered([1, 2, 3], [3, 1, 4], comparer);
        AssertionsAssert.EqualUnordered((System.Collections.IEnumerable)new object[] { 1, 2, 3 }, new object[] { 3, 1, 2 }, comparer);
        AssertionsAssert.NotEqualUnordered((System.Collections.IEnumerable)new object[] { 1, 2, 3 }, new object[] { 3, 1, 4 }, comparer);
        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered([1, 2, 3], [3, 1, 4], comparer), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: [1, 2, 3]
            Actual expression:   [3, 1, 4]
            Missing expected item index: 1
            Unexpected actual item index: 2
            Expected: [1, 2̲, 3]
            Actual:   [3, 1, 4̲]
            """);
    }

    [Fact]
    public void Failure_ComparesEachItemAConstantNumberOfTimes()
    {
        var comparer = new CountingComparer();
        var expected = Enumerable.Range(0, 2000).ToArray();
        var actual = expected.Reverse().ToArray();
        actual[500] = -1;

        var exception = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.EqualUnordered(expected, actual, comparer));

        AssertionsAssert.Contains("Missing expected item index: 1499", exception.Message);
        AssertionsAssert.Contains("Unexpected actual item index: 500", exception.Message);
        AssertionsAssert.True(comparer.EqualsCallCount <= 4 * expected.Length, $"Equals was called {comparer.EqualsCallCount} times");
    }

    [Fact]
    public void Failure_ReportsItemsNoPairingCanUse()
    {
        // Without hash codes, the first expected item cannot be paired, and the first actual item is only paired by a later
        // expected item. The message must not report it as unexpected.
        var comparer = new NoHashCodeComparer();

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered([5, 1], [1, 7], comparer), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: [5, 1]
            Actual expression:   [1, 7]
            Missing expected item index: 0
            Unexpected actual item index: 1
            Expected: [5̲, 1]
            Actual:   [1, 7̲]
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered([5, 1, 2], [1, 2], comparer), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: [5, 1, 2]
            Actual expression:   [1, 2]
            Missing expected item index: 0
            Expected: [5̲, 1, 2]
            Actual:   [1, 2]
            """);
    }

    [Fact]
    public void NonGenericComparer_ComparesEachItemAConstantNumberOfTimes()
    {
        var comparer = new CountingComparer();
        System.Collections.IEnumerable expected = Enumerable.Range(0, 2000).Cast<object>().ToArray();
        System.Collections.IEnumerable actual = Enumerable.Range(0, 2000).Reverse().Cast<object>().ToArray();

        AssertionsAssert.EqualUnordered(expected, actual, comparer);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(expected, actual, comparer));

        AssertionsAssert.True(comparer.EqualsCallCount <= 4 * 2000, $"Equals was called {comparer.EqualsCallCount} times");
    }

    [Fact]
    public void NestedCollections_ComparesEachItemAConstantNumberOfTimes()
    {
        var counter = new StrongBox<int>();
        var expected = Enumerable.Range(0, 2000).Select(value => new[] { new CountingItem(value, counter) }).ToArray();
        var actual = expected.Reverse().Select(items => new List<CountingItem> { new(items[0].Value, counter) }).ToArray();

        AssertionsAssert.EqualUnordered((System.Collections.IEnumerable)expected, (System.Collections.IEnumerable)actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered((System.Collections.IEnumerable)expected, (System.Collections.IEnumerable)actual));

        AssertionsAssert.True(counter.Value <= 8 * expected.Length, $"Equals was called {counter.Value} times");
    }

    [Fact]
    public void NotEqualUnordered_EnumeratesASingleUseSequenceOnlyOnce()
    {
        var expected = new SingleUseEnumerable<int>([1, 2]);
        var actual = new SingleUseEnumerable<int>([2, 1]);

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqualUnordered(expected, actual));
    }

    private sealed class SingleUseEnumerable<T>(T[] items) : IEnumerable<T>
    {
        private int _enumerationCount;

        public IEnumerator<T> GetEnumerator()
        {
            _enumerationCount++;
            if (_enumerationCount > 1)
                throw new InvalidOperationException("The sequence was enumerated more than once.");

            return ((IEnumerable<T>)items).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A comparer whose <see cref="GetHashCode(double)"/> disagrees with its <see cref="Equals(double, double)"/>,
    /// so the hash-based multiset check cannot see the values as equal but the comparer itself does.
    /// </summary>
    private sealed class ToleranceComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 0.5;

        public int GetHashCode(double obj) => obj.GetHashCode();
    }

    /// <summary>
    /// A tolerance comparer, which is not transitive: 0 equals 1 and 1 equals 2, but 0 does not equal 2. Its hash code is
    /// consistent with <see cref="Equals(int, int)"/> unless the constructor says otherwise.
    /// </summary>
    private sealed class NonTransitiveToleranceComparer(bool consistentHashCode = true) : IEqualityComparer<int>, System.Collections.IEqualityComparer
    {
        public bool Equals(int x, int y) => Math.Abs(x - y) <= 1;

        public int GetHashCode(int obj) => consistentHashCode ? 0 : obj;

        bool System.Collections.IEqualityComparer.Equals(object? x, object? y) => x is int left && y is int right && Equals(left, right);

        int System.Collections.IEqualityComparer.GetHashCode(object obj) => obj is int value ? GetHashCode(value) : 0;
    }

    /// <summary>Compares strings ordinally, but puts every string in the same hash bucket.</summary>
    private sealed class NonTransitiveStringComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);

        public int GetHashCode(string obj) => 0;
    }

    private sealed class CountingComparer : IEqualityComparer<int>, System.Collections.IEqualityComparer
    {
        public int EqualsCallCount { get; private set; }

        public bool Equals(int x, int y)
        {
            EqualsCallCount++;
            return x == y;
        }

        public int GetHashCode(int obj) => obj;

        bool System.Collections.IEqualityComparer.Equals(object? x, object? y) => x is int left && y is int right && Equals(left, right);

        int System.Collections.IEqualityComparer.GetHashCode(object obj) => obj is int value ? value : 0;
    }

    /// <summary>A comparer written only for Equals, whose GetHashCode throws.</summary>
    [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations", Justification = "The comparer simulates one that does not implement GetHashCode.")]
    private sealed class NoHashCodeComparer : IEqualityComparer<int>, System.Collections.IEqualityComparer
    {
        public bool Equals(int x, int y) => x == y;

        public int GetHashCode(int obj) => throw new NotSupportedException();

        bool System.Collections.IEqualityComparer.Equals(object? x, object? y) => object.Equals(x, y);

        int System.Collections.IEqualityComparer.GetHashCode(object obj) => throw new NotSupportedException();
    }

    private sealed class CountingItem(int value, StrongBox<int> equalsCallCount) : IEquatable<CountingItem>
    {
        public int Value { get; } = value;

        public bool Equals([NotNullWhen(true)] CountingItem? other)
        {
            equalsCallCount.Value++;
            return other is not null && Value == other.Value;
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as CountingItem);

        public override int GetHashCode() => Value;
    }
}
