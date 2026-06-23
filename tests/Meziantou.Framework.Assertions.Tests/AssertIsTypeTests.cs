using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertIsTypeTests
{
    [Fact]
    public void Generic_Success()
    {
        object actual = "Hello";

        var result = AssertionsAssert.IsType<string>(actual);

        global::Xunit.Assert.Equal("Hello", result);
    }

    [Fact]
    public void Type_Success()
    {
        object actual = "Hello";

        var result = AssertionsAssert.IsType(typeof(string), actual);

        global::Xunit.Assert.Same(actual, result);
    }

    [Fact]
    public void FailsWhenTypeDiffers()
    {
        object actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.IsType<object>(actual), """
            Assert.IsType() assertion failed.
            Expression:    actual
            Expected type: System.Object
            Actual type:   System.String
            Actual value:  "Hello"
            """);
    }

    [Fact]
    public void FailsWhenNull()
    {
        object? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.IsType<string>(actual), """
            Assert.IsType() assertion failed.
            Expression:    actual
            Expected type: System.String
            Actual type:   <null>
            Actual value:  <null>
            """);
    }
}
