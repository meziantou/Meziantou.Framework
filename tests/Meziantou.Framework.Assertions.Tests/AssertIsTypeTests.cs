using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertIsTypeTests
{
    [Fact]
    public void Generic_Success()
    {
        object actual = "Hello";

        var result = AssertionsAssert.IsType<string>(actual);

        AssertionsAssert.Equal("Hello", result);
    }

    [Fact]
    public void Type_Success()
    {
        object actual = "Hello";

        var result = AssertionsAssert.IsType(typeof(string), actual);

        AssertionsAssert.Same(actual, result);
    }

    [Fact]
    public void ActualIsNotNullAfterTheAssertion()
    {
        object? generic = Environment.TickCount64 >= 0 ? "Hello" : null;
        object? type = Environment.TickCount64 >= 0 ? "Hello" : null;

        AssertionsAssert.IsType<string>(generic);
        AssertionsAssert.IsType(typeof(string), type);

        AssertionsAssert.Equal(generic.GetHashCode(), type.GetHashCode());
    }

    [Fact]
    public void FailsWhenTypeDiffers()
    {
        object actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.IsType<object>(actual), """
            Assert.IsType() assertion failed.
            Expression: actual
            Expected type: System.Object
            Actual type:   System.String
            Actual value: "Hello"
            """);
    }

    [Fact]
    public void FailsWhenNull()
    {
        object? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.IsType<string>(actual), """
            Assert.IsType() assertion failed.
            Expression: actual
            Expected type: System.String
            Actual type:   <null>
            Actual value: <null>
            """);
    }

    [Fact]
    public void NullableValueType_Success()
    {
        int? nullableValue = 5;
        object actual = 5;

        AssertionsAssert.Equal(5, AssertionsAssert.IsType<int?>(actual));
        AssertionsAssert.Equal(5, AssertionsAssert.IsType<int?>(nullableValue));
        AssertionsAssert.Equal(5, AssertionsAssert.IsType<Nullable<int>>(actual));
        AssertionsAssert.Same(actual, AssertionsAssert.IsType(typeof(int?), actual));
        AssertionsAssert.Equal(5, AssertionsAssert.IsType<int>(nullableValue));
    }

    // The expected type is not pinned in these messages: generic types are formatted with their assembly-qualified type arguments
    [Fact]
    public void NullableValueType_FailsWhenUnderlyingTypeDiffers()
    {
        object actual = 5L;

        var exception = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.IsType<int?>(actual));

        AssertionsAssert.Contains("Actual type:   System.Int64", exception.Message);
    }

    [Fact]
    public void NullableValueType_FailsWhenNull()
    {
        int? actual = null;

        var exception = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.IsType(typeof(int?), actual));

        AssertionsAssert.Contains("Actual type:   <null>", exception.Message);
    }

    [Fact]
    public void IsNotType_NullableValueType()
    {
        int? nullValue = null;

        AssertionsAssert.IsNotType<int?>(5L);
        AssertionsAssert.IsNotType<int?>(nullValue);
        AssertionsAssert.IsNotType(typeof(long?), 5);

        var genericException = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.IsNotType<int?>(5));
        var typeException = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.IsNotType(typeof(int?), 5));

        AssertionsAssert.StartsWith("Assert.IsNotType() assertion failed.", genericException.Message);
        AssertionsAssert.StartsWith("Assert.IsNotType() assertion failed.", typeException.Message);
    }

    [Fact]
    public void IsNotType_Success()
    {
        object actual = "Hello";

        AssertionsAssert.IsNotType<object>(actual);
    }

    [Fact]
    public void IsNotType_Fails()
    {
        object actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.IsNotType<string>(actual), """
            Assert.IsNotType() assertion failed.
            Expression: actual
            Not expected type: System.String
            Actual type:       System.String
            Actual value: "Hello"
            """);
    }
}
