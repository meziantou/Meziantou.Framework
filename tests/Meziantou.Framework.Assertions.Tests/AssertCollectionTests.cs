using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertCollectionTests
{
    [Fact]
    public void Array_Success()
    {
        IEnumerable<int> actual = [1, 2, 3];
        Action<int>[] inspectors =
        [
            item => AssertionsAssert.Equal(1, item),
            item => AssertionsAssert.Equal(2, item),
            item => AssertionsAssert.Equal(3, item),
        ];

        AssertionsAssert.Collection(actual, inspectors);
    }

    [Fact]
    public void Empty_Success()
    {
        IEnumerable<int> actual = [];

        AssertionsAssert.Collection(actual);
    }

    [Fact]
    public void FixedOverloads_Success()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionsAssert.Collection(
            actual,
            item => AssertionsAssert.Equal(1, item),
            item => AssertionsAssert.Equal(2, item),
            item => AssertionsAssert.Equal(3, item));
    }

    [Fact]
    public void SixteenInspectors_Success()
    {
        IEnumerable<int> actual = Enumerable.Range(1, 16).ToArray();

        AssertionsAssert.Collection(
            actual,
            item => AssertionsAssert.Equal(1, item),
            item => AssertionsAssert.Equal(2, item),
            item => AssertionsAssert.Equal(3, item),
            item => AssertionsAssert.Equal(4, item),
            item => AssertionsAssert.Equal(5, item),
            item => AssertionsAssert.Equal(6, item),
            item => AssertionsAssert.Equal(7, item),
            item => AssertionsAssert.Equal(8, item),
            item => AssertionsAssert.Equal(9, item),
            item => AssertionsAssert.Equal(10, item),
            item => AssertionsAssert.Equal(11, item),
            item => AssertionsAssert.Equal(12, item),
            item => AssertionsAssert.Equal(13, item),
            item => AssertionsAssert.Equal(14, item),
            item => AssertionsAssert.Equal(15, item),
            item => AssertionsAssert.Equal(16, item));
    }

    [Fact]
    public void FailsWhenActualHasTooFewItems()
    {
        IEnumerable<int> actual = [1, 2];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Collection(
            actual,
            item => AssertionsAssert.Equal(1, item),
            item => AssertionsAssert.Equal(2, item),
            item => AssertionsAssert.Equal(3, item)), """
            Assert.Collection() assertion failed: Collection count does not match inspector count.
            Expression: actual
            Expected count: 3
            Actual count:   2
            Actual: [1, 2]
            """);
    }

    [Fact]
    public void FailsWhenActualHasTooManyItems()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Collection(
            actual,
            item => AssertionsAssert.Equal(1, item),
            item => AssertionsAssert.Equal(2, item)), """
            Assert.Collection() assertion failed: Collection count does not match inspector count.
            Expression: actual
            Expected count: 2
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void FailsWhenInspectorFails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Collection(
            actual,
            item => AssertionsAssert.Equal(1, item),
            item => AssertionsAssert.Equal(42, item),
            item => AssertionsAssert.Equal(3, item)), """
            Assert.Collection() assertion failed: Item at index 1 failed.
            Expression: actual
            Actual: [1, 2̲, 3]
            Exception: Assert.Equal() assertion failed.
                       Expected expression: 42
                       Actual expression:   item
                       Expected: 42
                       Actual:   2
            """);
    }
}
