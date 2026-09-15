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
    public void XunitSkip_IsNotWrapped()
    {
        IEnumerable<int> actual = [1, 2];

        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.Collection(
            actual,
            item => AssertionsAssert.Equal(1, item),
            _ => AssertionsAssert.XunitSkip("n/a")));
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

    [Fact]
    public void NullActual_Fails()
    {
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Collection(actual, item => AssertionsAssert.Equal(1, item)), """
            Assert.Collection() assertion failed.
            Expression: actual
            Expected count: 1
            Actual:         <null>
            """);
    }

    [Fact]
    public void EndlessSequenceFailsWithoutDrainingIt()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Collection(AssertionTestHelpers.EndlessSequence(), _ => { }, _ => { }), """
            Assert.Collection() assertion failed: Collection count does not match inspector count.
            Expression: AssertionTestHelpers.EndlessSequence()
            Expected count: 2
            Actual count:   at least 11
            Actual: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public void LazySequence_StopsOneItemPastTheInspectors()
    {
        var enumerated = 0;
        IEnumerable<int> Source()
        {
            for (var i = 0; i < 100; i++)
            {
                enumerated++;
                yield return i;
            }
        }

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Collection(Source(), [.. Enumerable.Repeat<Action<int>>(_ => { }, 20)]));

        AssertionsAssert.Equal(21, enumerated);
    }

    [Fact]
    public void EveryFixedArityOverload_RunsEachInspectorOnItsItem()
    {
        var calls = new List<int>();
        Action<int> At(int index) => item =>
        {
            AssertionsAssert.Equal(index, item);
            calls.Add(index);
        };

        void Verify(int count, Action<IEnumerable<int>> assertion)
        {
            calls.Clear();
            assertion(Enumerable.Range(0, count).ToList());
            AssertionsAssert.Equal(Enumerable.Range(0, count), calls);
            AssertionsAssert.Throws<AssertionException>(() => assertion(Enumerable.Range(0, count + 1).ToList()));
        }

        Verify(0, actual => AssertionsAssert.Collection(actual));
        Verify(1, actual => AssertionsAssert.Collection(actual, At(0)));
        Verify(2, actual => AssertionsAssert.Collection(actual, At(0), At(1)));
        Verify(3, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2)));
        Verify(4, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3)));
        Verify(5, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4)));
        Verify(6, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4), At(5)));
        Verify(7, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4), At(5), At(6)));
        Verify(8, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7)));
        Verify(9, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7), At(8)));
        Verify(10, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7), At(8), At(9)));
        Verify(11, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7), At(8), At(9), At(10)));
        Verify(12, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7), At(8), At(9), At(10), At(11)));
        Verify(13, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7), At(8), At(9), At(10), At(11), At(12)));
        Verify(14, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7), At(8), At(9), At(10), At(11), At(12), At(13)));
        Verify(15, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7), At(8), At(9), At(10), At(11), At(12), At(13), At(14)));
        Verify(16, actual => AssertionsAssert.Collection(actual, At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7), At(8), At(9), At(10), At(11), At(12), At(13), At(14), At(15)));
    }
}
