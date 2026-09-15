using System.Collections;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertSetTests
{
    [Fact]
    public void ProperSubset_Success()
    {
        var expected = new[] { 1, 2, 3 };
        var actual = new[] { 1, 1, 2 };

        AssertionsAssert.ProperSubset(expected, actual);
    }

    [Fact]
    public void ProperSubset_MatchesXunitArgumentOrder()
    {
        var expectedSuperset = new HashSet<int> { 1, 2, 3 };
        var actual = new HashSet<int> { 1, 2 };

        AssertionsAssert.ProperSubset(expectedSuperset, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.ProperSubset(actual, expectedSuperset));
    }

    [Fact]
    public void ProperSubset_ComparerSuccess()
    {
        var expected = new[] { "A", "b" };
        var actual = new[] { "a" };

        AssertionsAssert.ProperSubset(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProperSubset_FailsWhenSetsAreEqual()
    {
        var expected = new[] { 1, 2 };
        var actual = new[] { 2, 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSubset(expected, actual), """
            Assert.ProperSubset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Expected superset: [1, 2]
            Actual:            [2, 1]
            """);
    }

    [Fact]
    public void ProperSubset_FailsWhenActualContainsMissingItem()
    {
        var expected = new[] { 1, 2, 3 };
        var actual = new[] { 1, 4 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSubset(expected, actual), """
            Assert.ProperSubset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Expected superset: [1, 2, 3]
            Actual:            [1, 4]
            """);
    }

    [Fact]
    public void ProperSubset_FailsWhenActualIsNull()
    {
        var expected = new[] { 1, 2 };
        int[]? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSubset(expected, actual), """
            Assert.ProperSubset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Expected superset: [1, 2]
            Actual:            <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSubset((IEnumerable)expected, (IEnumerable?)actual), """
            Assert.ProperSubset() assertion failed.
            Expected superset expression: (IEnumerable)expected
            Actual expression:            (IEnumerable?)actual
            Expected superset: [1, 2]
            Actual:            <null>
            """);
    }

    [Fact]
    public void ProperSubset_NonGenericSuccess()
    {
        IEnumerable expected = new[] { 1, 2 };
        IEnumerable actual = new[] { 1 };

        AssertionsAssert.ProperSubset(expected, actual);
    }

    [Fact]
    public void ProperSuperset_Success()
    {
        var expected = new[] { 1, 1, 2 };
        var actual = new[] { 1, 2, 3 };

        AssertionsAssert.ProperSuperset(expected, actual);
    }

    [Fact]
    public void ProperSuperset_MatchesXunitArgumentOrder()
    {
        var expectedSubset = new HashSet<int> { 1, 2 };
        var actual = new HashSet<int> { 1, 2, 3 };

        AssertionsAssert.ProperSuperset(expectedSubset, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.ProperSuperset(actual, expectedSubset));
    }

    [Fact]
    public void ProperSuperset_ComparerSuccess()
    {
        var expected = new[] { "a" };
        var actual = new[] { "A", "b" };

        AssertionsAssert.ProperSuperset(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProperSuperset_FailsWhenSetsAreEqual()
    {
        var expected = new[] { 1, 2 };
        var actual = new[] { 2, 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSuperset(expected, actual), """
            Assert.ProperSuperset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Expected subset: [1, 2]
            Actual:          [2, 1]
            """);
    }

    [Fact]
    public void ProperSuperset_FailsWhenExpectedContainsMissingItem()
    {
        var expected = new[] { 1, 4 };
        var actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSuperset(expected, actual), """
            Assert.ProperSuperset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Expected subset: [1, 4]
            Actual:          [1, 2, 3]
            """);
    }

    [Fact]
    public void ProperSuperset_FailsWhenActualIsNull()
    {
        var expected = new[] { 1, 2 };
        int[]? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSuperset(expected, actual), """
            Assert.ProperSuperset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Expected subset: [1, 2]
            Actual:          <null>
            """);
    }

    [Fact]
    public void ProperSuperset_NonGenericSuccess()
    {
        IEnumerable expected = new[] { 1 };
        IEnumerable actual = new[] { 1, 2 };

        AssertionsAssert.ProperSuperset(expected, actual);
    }

    [Fact]
    public void Subset_Success()
    {
        AssertionsAssert.Subset([1, 2, 3], [2, 1]);
        AssertionsAssert.Subset([1, 2], [2, 1, 1]);
        AssertionsAssert.Subset([1, 2], Array.Empty<int>());
        AssertionsAssert.Subset(new HashSet<int> { 1, 2 }, new HashSet<int> { 1, 2 });
        AssertionsAssert.Subset((IEnumerable)new[] { 1, 2 }, (IEnumerable)new[] { 2, 1 });
        AssertionsAssert.Subset(["A", "b"], ["a", "B"], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Subset_Fails()
    {
        var expected = new[] { 1, 2 };
        var actual = new[] { 1, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Subset(expected, actual), """
            Assert.Subset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Expected superset: [1, 2]
            Actual:            [1, 3]
            """);
    }

    [Fact]
    public void Subset_FailsWhenActualIsNull()
    {
        var expected = new[] { 1, 2 };
        int[]? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Subset(expected, actual), """
            Assert.Subset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Expected superset: [1, 2]
            Actual:            <null>
            """);
    }

    [Fact]
    public void Superset_Success()
    {
        AssertionsAssert.Superset([2, 1], [1, 2, 3]);
        AssertionsAssert.Superset([2, 1, 1], [1, 2]);
        AssertionsAssert.Superset(Array.Empty<int>(), [1, 2]);
        AssertionsAssert.Superset(new HashSet<int> { 1, 2 }, new HashSet<int> { 1, 2 });
        AssertionsAssert.Superset((IEnumerable)new[] { 1, 2 }, (IEnumerable)new[] { 2, 1 });
        AssertionsAssert.Superset(["a", "B"], ["A", "b"], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Superset_Fails()
    {
        var expected = new[] { 1, 3 };
        var actual = new[] { 1, 2 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Superset(expected, actual), """
            Assert.Superset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Expected subset: [1, 3]
            Actual:          [1, 2]
            """);
    }

    [Fact]
    public void Superset_FailsWhenActualIsNull()
    {
        var expected = new[] { 1, 2 };
        int[]? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Superset(expected, actual), """
            Assert.Superset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Expected subset: [1, 2]
            Actual:          <null>
            """);
    }

    [Fact]
    public void Subset_UsesActualSetComparer()
    {
        var expected = new[] { "A", "b" };
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "B" };

        AssertionsAssert.Subset(expected, actual);
        AssertionsAssert.Superset(expected, actual);
    }

    [Fact]
    public void NotProperSubset_Success()
    {
        AssertionsAssert.NotProperSubset([1, 2], [2, 1]);
        AssertionsAssert.NotProperSubset([1], [1, 2]);
        AssertionsAssert.NotProperSubset((IEnumerable)new[] { 1 }, (IEnumerable)new[] { 1, 2 });
    }

    [Fact]
    public void NotProperSubset_SucceedsWhenActualIsNull()
    {
        int[]? actual = null;

        AssertionsAssert.NotProperSubset([1, 2], actual);
        AssertionsAssert.NotProperSubset((IEnumerable)new[] { 1, 2 }, (IEnumerable?)actual);
    }

    [Fact]
    public void NotProperSubset_Fails()
    {
        var expected = new[] { 1, 2 };
        var actual = new[] { 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotProperSubset(expected, actual), """
            Assert.NotProperSubset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Not expected superset: [1, 2]
            Actual:                [1]
            """);
    }

    [Fact]
    public void NotProperSuperset_Success()
    {
        AssertionsAssert.NotProperSuperset([1, 2], [2, 1]);
        AssertionsAssert.NotProperSuperset([1, 2], [1]);
        AssertionsAssert.NotProperSuperset([1, 4], [1, 2, 3]);
        AssertionsAssert.NotProperSuperset((IEnumerable)new[] { 1, 2 }, (IEnumerable)new[] { 1 });
        AssertionsAssert.NotProperSuperset(["a"], ["A", "b"], StringComparer.Ordinal);
    }

    [Fact]
    public void NotProperSuperset_SucceedsWhenActualIsNull()
    {
        int[]? actual = null;

        AssertionsAssert.NotProperSuperset([1, 2], actual);
        AssertionsAssert.NotProperSuperset((IEnumerable)new[] { 1, 2 }, (IEnumerable?)actual);
    }

    [Fact]
    public void NotProperSuperset_Fails()
    {
        var expected = new[] { 1 };
        var actual = new[] { 1, 2 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotProperSuperset(expected, actual), """
            Assert.NotProperSuperset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Not expected subset: [1]
            Actual:              [1, 2]
            """);
    }

    [Fact]
    public void ProperSubset_UsesActualSetComparer()
    {
        var expected = new[] { "A", "b" };
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };

        AssertionsAssert.ProperSubset(expected, actual);
    }

    [Fact]
    public void ProperSubset_FailsWithActualSetComparer()
    {
        var expected = AssertionTestHelpers.SingleUse("A");
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSubset(expected, actual), """
            Assert.ProperSubset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Expected superset: ["A"]
            Actual:            ["a"]
            """);
    }

    [Fact]
    public void ProperSubset_ExplicitComparerOverridesActualSetComparer()
    {
        var expected = new[] { "A", "b" };
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSubset(expected, actual, StringComparer.Ordinal), """
            Assert.ProperSubset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Expected superset: ["A", "b"]
            Actual:            ["a"]
            """);
    }

    [Fact]
    public void ProperSubset_IgnoresExpectedSetComparer()
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", "b" };
        var actual = new[] { "a" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSubset(expected, actual), """
            Assert.ProperSubset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Expected superset: ["A", "b"]
            Actual:            ["a"]
            """);
    }

    [Fact]
    public void ProperSuperset_UsesActualSetComparer()
    {
        var expected = new[] { "A" };
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" };

        AssertionsAssert.ProperSuperset(expected, actual);
    }

    [Fact]
    public void ProperSuperset_ExplicitComparerOverridesActualSetComparer()
    {
        var expected = new[] { "A" };
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSuperset(expected, actual, StringComparer.Ordinal), """
            Assert.ProperSuperset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Expected subset: ["A"]
            Actual:          ["a", "b"]
            """);
    }

    [Fact]
    public void NotProperSubset_EnumeratesSingleUseSequencesOnlyOnce()
    {
        var expected = AssertionTestHelpers.SingleUse(1, 2);
        var actual = AssertionTestHelpers.SingleUse(1);

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotProperSubset(expected, actual), """
            Assert.NotProperSubset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Not expected superset: [1, 2]
            Actual:                [1]
            """);
    }

    [Fact]
    public void NotProperSubset_NonGenericEnumeratesSingleUseSequencesOnlyOnce()
    {
        IEnumerable expected = AssertionTestHelpers.SingleUse(1, 2);
        IEnumerable actual = AssertionTestHelpers.SingleUse(1);

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotProperSubset(expected, actual), """
            Assert.NotProperSubset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Not expected superset: [1, 2]
            Actual:                [1]
            """);
    }

    [Fact]
    public void NotProperSuperset_EnumeratesSingleUseSequencesOnlyOnce()
    {
        var expected = AssertionTestHelpers.SingleUse(1);
        var actual = AssertionTestHelpers.SingleUse(1, 2);

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotProperSuperset(expected, actual), """
            Assert.NotProperSuperset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Not expected subset: [1]
            Actual:              [1, 2]
            """);
    }

    [Fact]
    public void NotProperSuperset_NonGenericEnumeratesSingleUseSequencesOnlyOnce()
    {
        IEnumerable expected = AssertionTestHelpers.SingleUse(1);
        IEnumerable actual = AssertionTestHelpers.SingleUse(1, 2);

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotProperSuperset(expected, actual), """
            Assert.NotProperSuperset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Not expected subset: [1]
            Actual:              [1, 2]
            """);
    }

    [Fact]
    public void NotProperSubset_UsesActualSetComparer()
    {
        var expected = new[] { "A", "b" };
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotProperSubset(expected, actual), """
            Assert.NotProperSubset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Not expected superset: ["A", "b"]
            Actual:                ["a"]
            """);
    }

    [Fact]
    public void NotProperSuperset_UsesActualSetComparer()
    {
        var expected = new[] { "A" };
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotProperSuperset(expected, actual), """
            Assert.NotProperSuperset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Not expected subset: ["A"]
            Actual:              ["a", "b"]
            """);
    }

    [Fact]
    public void SetAssertions_AgreeWithISetAndNegationsAreExactComplements()
    {
        (int[] Expected, int[]? Actual)[] cases =
        [
            ([], []),
            ([], [1]),
            ([1], []),
            ([1, 2], [1, 2]),
            ([1, 2], [2, 1, 1]),
            ([1, 2, 3], [1, 2]),
            ([1, 2], [1, 2, 3]),
            ([1, 2], [3]),
            ([1, 2], [2, 3]),
            ([1, 2], null),
        ];

        foreach (var (expected, actual) in cases)
        {
            var description = $"expected: [{string.Join(", ", expected)}], actual: {(actual is null ? "null" : $"[{string.Join(", ", actual)}]")}";
            var actualSet = actual is null ? null : new HashSet<int>(actual);
            IEnumerable? actualNonGeneric = actual;

            var isProperSubset = actualSet?.IsProperSubsetOf(expected) ?? false;
            AssertionsAssert.Equal(isProperSubset, Succeeds(() => AssertionsAssert.ProperSubset(expected, actual)), description);
            AssertionsAssert.Equal(isProperSubset, Succeeds(() => AssertionsAssert.ProperSubset(expected, actualSet)), description);
            AssertionsAssert.Equal(isProperSubset, Succeeds(() => AssertionsAssert.ProperSubset((IEnumerable)expected, actualNonGeneric)), description);
            AssertionsAssert.Equal(!isProperSubset, Succeeds(() => AssertionsAssert.NotProperSubset(expected, actual)), description);
            AssertionsAssert.Equal(!isProperSubset, Succeeds(() => AssertionsAssert.NotProperSubset(expected, actualSet)), description);
            AssertionsAssert.Equal(!isProperSubset, Succeeds(() => AssertionsAssert.NotProperSubset((IEnumerable)expected, actualNonGeneric)), description);

            var isProperSuperset = actualSet?.IsProperSupersetOf(expected) ?? false;
            AssertionsAssert.Equal(isProperSuperset, Succeeds(() => AssertionsAssert.ProperSuperset(expected, actual)), description);
            AssertionsAssert.Equal(isProperSuperset, Succeeds(() => AssertionsAssert.ProperSuperset(expected, actualSet)), description);
            AssertionsAssert.Equal(isProperSuperset, Succeeds(() => AssertionsAssert.ProperSuperset((IEnumerable)expected, actualNonGeneric)), description);
            AssertionsAssert.Equal(!isProperSuperset, Succeeds(() => AssertionsAssert.NotProperSuperset(expected, actual)), description);
            AssertionsAssert.Equal(!isProperSuperset, Succeeds(() => AssertionsAssert.NotProperSuperset(expected, actualSet)), description);
            AssertionsAssert.Equal(!isProperSuperset, Succeeds(() => AssertionsAssert.NotProperSuperset((IEnumerable)expected, actualNonGeneric)), description);

            var isSubset = actualSet?.IsSubsetOf(expected) ?? false;
            AssertionsAssert.Equal(isSubset, Succeeds(() => AssertionsAssert.Subset(expected, actual)), description);
            AssertionsAssert.Equal(isSubset, Succeeds(() => AssertionsAssert.Subset(expected, actualSet)), description);
            AssertionsAssert.Equal(isSubset, Succeeds(() => AssertionsAssert.Subset((IEnumerable)expected, actualNonGeneric)), description);

            var isSuperset = actualSet?.IsSupersetOf(expected) ?? false;
            AssertionsAssert.Equal(isSuperset, Succeeds(() => AssertionsAssert.Superset(expected, actual)), description);
            AssertionsAssert.Equal(isSuperset, Succeeds(() => AssertionsAssert.Superset(expected, actualSet)), description);
            AssertionsAssert.Equal(isSuperset, Succeeds(() => AssertionsAssert.Superset((IEnumerable)expected, actualNonGeneric)), description);
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
}
