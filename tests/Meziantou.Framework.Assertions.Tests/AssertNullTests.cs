using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertNullTests
{
    [Fact]
    public void Success()
    {
        object? actual = null;

        AssertionsAssert.Null(actual);
    }

    [Fact]
    public void Fails()
    {
        var actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Null(actual), """
            Assert.Null() assertion failed.
            Expression: actual
            Expected: <null>
            Actual:   "Hello"
            """);
    }

    [Fact]
    public void NullableValueType_Success()
    {
        int? actual = null;

        AssertionsAssert.Null(actual);
    }

    [Fact]
    public void NullableValueType_Fails()
    {
        int? actual = 42;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Null(actual), """
            Assert.Null() assertion failed.
            Expression: actual
            Expected: <null>
            Actual:   42
            """);
    }

    [Fact]
    public void NotNull_Success()
    {
        var actual = "Hello";

        var result = AssertionsAssert.NotNull(actual);

        AssertionsAssert.Same(actual, result);
    }

    [Fact]
    public void NotNull_Fails()
    {
        object? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotNull(actual), """
            Assert.NotNull() assertion failed.
            Expression: actual
            Not expected: <null>
            Actual:       <null>
            """);
    }

    [Fact]
    public void NotNull_ReferenceType_ReturnsTheValueWithItsStaticType()
    {
        var actual = FindName("Hello");

        var length = AssertionsAssert.NotNull(actual).Length;

        AssertionsAssert.Equal(5, length);
    }

    [Fact]
    public void NotNull_UnconstrainedTypeParameter()
    {
        AssertNotNull("Hello");
        AssertNotNull(42);
        AssertionTestHelpers.Validate(() => AssertNotNull<string?>(null), """
            Assert.NotNull() assertion failed.
            Expression: actual
            Not expected: <null>
            Actual:       <null>
            """);

        static void AssertNotNull<T>(T actual)
        {
            object result = AssertionsAssert.NotNull(actual);
            AssertionsAssert.Equal(actual, result);
        }
    }

    [Fact]
    public void NotNull_NullableValueType_ReturnsTheValue()
    {
        int? actual = 42;

        var result = AssertionsAssert.NotNull(actual);

        AssertionsAssert.Equal(42, result);
    }

    [Fact]
    public void NotNull_NullableValueType_Fails()
    {
        int? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotNull(actual), """
            Assert.NotNull() assertion failed.
            Expression: actual
            Not expected: <null>
            Actual:       <null>
            """);
    }

    private static string? FindName(string? name) => name;
}
