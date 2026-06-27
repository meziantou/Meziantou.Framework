using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertAllTests
{
    [Fact]
    public void Span_Success()
    {
        AssertionsAssert.All<int>([1, 2, 3], item => AssertionsAssert.True(item > 0));
    }

    [Fact]
    public void Span_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.All<int>([1, -2, 3], item => AssertionsAssert.True(item > 0)), """
            Assert.All() assertion failed: Item at index 1 failed.
            Expression: [1, -2, 3]
            Assertion expression: item => AssertionsAssert.True(item > 0)
            Actual:     [1, -̲2̲, 3]
            Exception:  Assert.True() assertion failed.
                        Expression: item > 0
                        Expected: true
                        Actual:   false
            """);
    }

    [Fact]
    public void CharSpan_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.All("aBc".AsSpan(), item => AssertionsAssert.True(char.IsLower(item))), """
            Assert.All() assertion failed: Item at index 1 failed.
            Expression: "aBc".AsSpan()
            Assertion expression: item => AssertionsAssert.True(char.IsLower(item))
            Actual:     "aB̲c"
            Exception:  Assert.True() assertion failed.
                        Expression: char.IsLower(item)
                        Expected: true
                        Actual:   false
            """);
    }

    [Fact]
    public void Enumerable_Success()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionsAssert.All(actual, item => AssertionsAssert.True(item > 0));
    }

    [Fact]
    public void Enumerable_Fails()
    {
        IEnumerable<int> actual = [1, -2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.All(actual, item => AssertionsAssert.True(item > 0)), """
            Assert.All() assertion failed: Item at index 1 failed.
            Expression: actual
            Assertion expression: item => AssertionsAssert.True(item > 0)
            Actual:     [1, -̲2̲, 3]
            Exception:  Assert.True() assertion failed.
                        Expression: item > 0
                        Expected: true
                        Actual:   false
            """);
    }

    [Fact]
    public void EnumerableIndex_Fails()
    {
        IEnumerable<int> actual = [0, 1, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.All(actual, (item, index) => AssertionsAssert.Equal(index, item)), """
            Assert.All() assertion failed: Item at index 2 failed.
            Expression: actual
            Assertion expression: (item, index) => AssertionsAssert.Equal(index, item)
            Actual:     [0, 1, 3̲]
            Exception:  Assert.Equal() assertion failed.
                        Expected expression: index
                        Actual expression:   item
                        Expected: 2
                        Actual:   3
            """);
    }

    [Fact]
    public void NonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable actual = new object?[] { 1, -2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.All(actual, item => AssertionsAssert.True((int)item! > 0)), """
            Assert.All() assertion failed: Item at index 1 failed.
            Expression: actual
            Assertion expression: item => AssertionsAssert.True((int)item! > 0)
            Actual:     [1, -̲2̲, 3]
            Exception:  Assert.True() assertion failed.
                        Expression: (int)item! > 0
                        Expected: true
                        Actual:   false
            """);
    }

    [Fact]
    public async Task AsyncEnumerable_Success()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionsAssert.All(actual, item => AssertionsAssert.True(item > 0));
    }

    [Fact]
    public async Task AsyncEnumerable_Fails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, -2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.All(actual, item => AssertionsAssert.True(item > 0)), """
            Assert.All() assertion failed: Item at index 1 failed.
            Expression: actual
            Assertion expression: item => AssertionsAssert.True(item > 0)
            Actual:     [1, -̲2̲, 3]
            Exception:  Assert.True() assertion failed.
                        Expression: item > 0
                        Expected: true
                        Actual:   false
            """);
    }

    [Fact]
    public async Task AsyncEnumerableAsyncAssertion_Fails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([0, 1, 3]);
        async Task Assertion(int item, int index)
        {
            await Task.Yield();
            AssertionsAssert.Equal(index, item);
        }

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.All(actual, Assertion), """
            Assert.All() assertion failed: Item at index 2 failed.
            Expression: actual
            Assertion expression: Assertion
            Actual:     [0, 1, 3̲]
            Exception:  Assert.Equal() assertion failed.
                        Expected expression: index
                        Actual expression:   item
                        Expected: 2
                        Actual:   3
            """);
    }
}
