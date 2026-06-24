using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertInRangeTests
{
    [Fact]
    public void Success()
    {
        AssertionsAssert.InRange(2, 1, 3);
        AssertionsAssert.InRange(1, 1, 3);
        AssertionsAssert.InRange(3, 1, 3);
    }

    [Fact]
    public void ComparerSuccess()
    {
        AssertionsAssert.InRange("b", "A", "C", StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailsWhenValueIsBelowRange()
    {
        var actual = 0;
        var low = 1;
        var high = 3;

        AssertionTestHelpers.Validate(() => AssertionsAssert.InRange(actual, low, high), """
            Assert.InRange() assertion failed.
            Expression: actual
            Expected:   in range [1, 3]
            Actual:     0
            """);
    }

    [Fact]
    public void FailsWhenValueIsAboveRange()
    {
        var actual = 4;
        var low = 1;
        var high = 3;

        AssertionTestHelpers.Validate(() => AssertionsAssert.InRange(actual, low, high), """
            Assert.InRange() assertion failed.
            Expression: actual
            Expected:   in range [1, 3]
            Actual:     4
            """);
    }

    [Fact]
    public void FailsUsingComparer()
    {
        var actual = "d";
        var low = "A";
        var high = "C";

        AssertionTestHelpers.Validate(() => AssertionsAssert.InRange(actual, low, high, StringComparer.OrdinalIgnoreCase), """
            Assert.InRange() assertion failed.
            Expression: actual
            Expected:   in range ["A", "C"]
            Actual:     "d"
            """);
    }

    [Fact]
    public void NotInRange_Success()
    {
        AssertionsAssert.NotInRange(4, 1, 3);
    }

    [Fact]
    public void NotInRange_Fails()
    {
        var actual = 2;

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotInRange(actual, 1, 3), """
            Assert.NotInRange() assertion failed.
            Expression: actual
            Not expected: in range [1, 3]
            Actual:       2
            """);
    }
}
