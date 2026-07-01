using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertCountTests
{
    [Fact]
    public void HasCount_Success()
    {
        AssertionsAssert.HasCount<int>(3, [1, 2, 3]);
        AssertionsAssert.HasCount(3, "abc");
        AssertionsAssert.HasCount(3, new[] { 1, 2, 3 }.AsEnumerable());
    }

    [Fact]
    public void HasCount_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCount(2, actual), """
            Assert.HasCount() assertion failed.
            Expression: actual
            Expected count: 2
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void HasCount_StringFails()
    {
        var actual = "abc";

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCount(2, actual), """
            Assert.HasCount() assertion failed.
            Expression: actual
            Expected count: 2
            Actual count:   3
            Actual: "abc"
            """);
    }

    [Fact]
    public void HasCount_NonGenericEnumerableFails()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCount(2, actual), """
            Assert.HasCount() assertion failed.
            Expression: actual
            Expected count: 2
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public async Task HasCount_AsyncEnumerableFails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.HasCount(2, actual), """
            Assert.HasCount() assertion failed.
            Expression: actual
            Expected count: 2
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void HasCountGreaterThan_Success()
    {
        AssertionsAssert.HasCountGreaterThan<int>(2, [1, 2, 3]);
        AssertionsAssert.HasCountGreaterThan(2, "abc");
        AssertionsAssert.HasCountGreaterThan(2, new[] { 1, 2, 3 }.AsEnumerable());
    }

    [Fact]
    public void HasCountGreaterThan_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountGreaterThan<int>(3, [1, 2, 3]), """
            Assert.HasCountGreaterThan() assertion failed.
            Expression: [1, 2, 3]
            Expected count: > 3
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public async Task HasCountGreaterThan_AsyncEnumerableFails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.HasCountGreaterThan(3, actual), """
            Assert.HasCountGreaterThan() assertion failed.
            Expression: actual
            Expected count: > 3
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void HasCountGreaterThanOrEqual_Success()
    {
        AssertionsAssert.HasCountGreaterThanOrEqual<int>(3, [1, 2, 3]);
        AssertionsAssert.HasCountGreaterThanOrEqual(3, "abc");
        AssertionsAssert.HasCountGreaterThanOrEqual(3, new[] { 1, 2, 3 }.AsEnumerable());
    }

    [Fact]
    public void HasCountGreaterThanOrEqual_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountGreaterThanOrEqual(4, actual), """
            Assert.HasCountGreaterThanOrEqual() assertion failed.
            Expression: actual
            Expected count: >= 4
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void HasCountLessThan_Success()
    {
        AssertionsAssert.HasCountLessThan<int>(4, [1, 2, 3]);
        AssertionsAssert.HasCountLessThan(4, "abc");
        AssertionsAssert.HasCountLessThan(4, new[] { 1, 2, 3 }.AsEnumerable());
    }

    [Fact]
    public void HasCountLessThan_Fails()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountLessThan(3, actual), """
            Assert.HasCountLessThan() assertion failed.
            Expression: actual
            Expected count: < 3
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void HasCountLessThanOrEqual_Success()
    {
        AssertionsAssert.HasCountLessThanOrEqual<int>(3, [1, 2, 3]);
        AssertionsAssert.HasCountLessThanOrEqual(3, "abc");
        AssertionsAssert.HasCountLessThanOrEqual(3, new[] { 1, 2, 3 }.AsEnumerable());
    }

    [Fact]
    public void HasCountLessThanOrEqual_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountLessThanOrEqual(2, actual), """
            Assert.HasCountLessThanOrEqual() assertion failed.
            Expression: actual
            Expected count: <= 2
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void DoesNotHaveCount_Success()
    {
        AssertionsAssert.DoesNotHaveCount<int>(2, [1, 2, 3]);
        AssertionsAssert.DoesNotHaveCount(2, "abc");
    }

    [Fact]
    public void DoesNotHaveCount_Fails()
    {
        var actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotHaveCount(3, actual), """
            Assert.DoesNotHaveCount() assertion failed.
            Expression: actual
            Not expected count: 3
            Actual count:       3
            Actual: [1, 2, 3]
            """);
    }
}
