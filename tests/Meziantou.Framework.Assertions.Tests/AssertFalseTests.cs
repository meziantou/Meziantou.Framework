using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertFalseTests
{
    [Fact]
    public void Success()
    {
        AssertionsAssert.False(false);
        AssertionsAssert.False(false, "custom message");

        bool? nullableCondition = false;
        AssertionsAssert.False(nullableCondition);
    }

    [Fact]
    public void Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.False(true), """
            Assert.False() assertion failed.
            Expression: true
            Expected: false
            Actual:   true
            """);

        AssertionTestHelpers.Validate(() => AssertionsAssert.False(true, "custom message"), """
            Assert.False() assertion failed.
            Expression: true
            Expected: false
            Actual:   true
            Message: custom message
            """);

        bool? nullableTrue = true;
        AssertionTestHelpers.Validate(() => AssertionsAssert.False(nullableTrue), """
            Assert.False() assertion failed.
            Expression: nullableTrue
            Expected: false
            Actual:   true
            """);

        bool? nullableNull = null;
        AssertionTestHelpers.Validate(() => AssertionsAssert.False(nullableNull), """
            Assert.False() assertion failed.
            Expression: nullableNull
            Expected: false
            Actual:   <null>
            """);
    }
}
