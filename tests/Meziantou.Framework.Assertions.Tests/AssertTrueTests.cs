using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertTrueTests
{
    [Fact]
    public void Success()
    {
        AssertionsAssert.True(true);
        AssertionsAssert.True(true, "custom message");

        bool? nullableCondition = true;
        AssertionsAssert.True(nullableCondition);
    }

    [Fact]
    public void Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.True(false), """
            Assert.True() assertion failed.
            Expression: false
            Expected: true
            Actual:   false
            """);

        AssertionTestHelpers.Validate(() => AssertionsAssert.True(false, "custom message"), """
            Assert.True() assertion failed.
            Expression: false
            Expected: true
            Actual:   false
            Message: custom message
            """);

        bool? nullableFalse = false;
        AssertionTestHelpers.Validate(() => AssertionsAssert.True(nullableFalse), """
            Assert.True() assertion failed.
            Expression: nullableFalse
            Expected: true
            Actual:   false
            """);

        bool? nullableNull = null;
        AssertionTestHelpers.Validate(() => AssertionsAssert.True(nullableNull), """
            Assert.True() assertion failed.
            Expression: nullableNull
            Expected: true
            Actual:   <null>
            """);
    }
}
