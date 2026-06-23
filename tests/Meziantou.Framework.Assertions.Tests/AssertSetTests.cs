using System.Collections;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertSetTests
{
    [Fact]
    public void ProperSubset_Success()
    {
        var expected = new[] { 1, 1, 2 };
        var actual = new[] { 1, 2, 3 };

        AssertionsAssert.ProperSubset(expected, actual);
    }

    [Fact]
    public void ProperSubset_ComparerSuccess()
    {
        var expected = new[] { "a" };
        var actual = new[] { "A", "b" };

        AssertionsAssert.ProperSubset(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProperSubset_FailsWhenSetsAreEqual()
    {
        var expected = new[] { 1, 2 };
        var actual = new[] { 2, 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSubset(expected, actual), """
            Assert.ProperSubset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Expected subset: [1, 2]
            Actual:          [2, 1]
            """);
    }

    [Fact]
    public void ProperSubset_FailsWhenExpectedContainsMissingItem()
    {
        var expected = new[] { 1, 4 };
        var actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSubset(expected, actual), """
            Assert.ProperSubset() assertion failed.
            Expected subset expression: expected
            Actual expression:          actual
            Expected subset: [1, 4]
            Actual:          [1, 2, 3]
            """);
    }

    [Fact]
    public void ProperSubset_NonGenericSuccess()
    {
        IEnumerable expected = new[] { 1 };
        IEnumerable actual = new[] { 1, 2 };

        AssertionsAssert.ProperSubset(expected, actual);
    }

    [Fact]
    public void ProperSuperset_Success()
    {
        var expected = new[] { 1, 2, 3 };
        var actual = new[] { 1, 1, 2 };

        AssertionsAssert.ProperSuperset(expected, actual);
    }

    [Fact]
    public void ProperSuperset_ComparerSuccess()
    {
        var expected = new[] { "A", "b" };
        var actual = new[] { "a" };

        AssertionsAssert.ProperSuperset(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProperSuperset_FailsWhenSetsAreEqual()
    {
        var expected = new[] { 1, 2 };
        var actual = new[] { 2, 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSuperset(expected, actual), """
            Assert.ProperSuperset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Expected superset: [1, 2]
            Actual:            [2, 1]
            """);
    }

    [Fact]
    public void ProperSuperset_FailsWhenActualContainsMissingItem()
    {
        var expected = new[] { 1, 2, 3 };
        var actual = new[] { 1, 4 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ProperSuperset(expected, actual), """
            Assert.ProperSuperset() assertion failed.
            Expected superset expression: expected
            Actual expression:            actual
            Expected superset: [1, 2, 3]
            Actual:            [1, 4]
            """);
    }

    [Fact]
    public void ProperSuperset_NonGenericSuccess()
    {
        IEnumerable expected = new[] { 1, 2 };
        IEnumerable actual = new[] { 1 };

        AssertionsAssert.ProperSuperset(expected, actual);
    }
}
