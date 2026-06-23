using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertIsAssignableToTests
{
    [Fact]
    public void Generic_Success()
    {
        object actual = "Hello";

        var result = AssertionsAssert.IsAssignableTo<object>(actual);

        global::Xunit.Assert.Same(actual, result);
    }

    [Fact]
    public void Type_Success()
    {
        object actual = "Hello";

        var result = AssertionsAssert.IsAssignableTo(typeof(object), actual);

        global::Xunit.Assert.Same(actual, result);
    }

    [Fact]
    public void FailsWhenTypeIsNotAssignable()
    {
        object actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.IsAssignableTo<int>(actual), """
            Assert.IsAssignableTo() assertion failed.
            Expression:    actual
            Expected type: System.Int32
            Actual type:   System.String
            Actual value:  "Hello"
            """);
    }

    [Fact]
    public void FailsWhenNull()
    {
        object? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.IsAssignableTo<string>(actual), """
            Assert.IsAssignableTo() assertion failed.
            Expression:    actual
            Expected type: System.String
            Actual type:   <null>
            Actual value:  <null>
            """);
    }
}
