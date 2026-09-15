using System.Runtime.InteropServices;
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
            Expected: in range [1, 3]
            Actual:   0
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
            Expected: in range [1, 3]
            Actual:   4
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
            Expected: in range ["A", "C"]
            Actual:   "d"
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

    [Theory]
    [InlineData(double.PositiveInfinity, 0d, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, double.NegativeInfinity, 0d)]
    [InlineData(5d, double.NegativeInfinity, double.PositiveInfinity)]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity)]
    public void Infinity_InRange(double actual, double low, double high)
    {
        AssertionsAssert.InRange(actual, low, high);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotInRange(actual, low, high));
    }

    [Theory]
    [InlineData(double.PositiveInfinity, 0d, 10d)]
    [InlineData(double.NegativeInfinity, 0d, 10d)]
    [InlineData(double.NegativeInfinity, 0d, double.PositiveInfinity)]
    public void Infinity_NotInRange(double actual, double low, double high)
    {
        AssertionsAssert.NotInRange(actual, low, high);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange(actual, low, high));
    }

    [Theory]
    [InlineData(double.NaN, 0d, 10d)]
    [InlineData(double.NaN, double.NegativeInfinity, double.PositiveInfinity)]
    [InlineData(double.NaN, double.NaN, 10d)]
    [InlineData(double.NaN, 0d, double.NaN)]
    [InlineData(double.NaN, double.NaN, double.NaN)]
    [InlineData(5d, double.NaN, 10d)]
    [InlineData(double.NegativeInfinity, double.NaN, 0d)]
    [InlineData(5d, 0d, double.NaN)]
    [InlineData(5d, double.NaN, double.NaN)]
    public void NaN_NotInRange(double actual, double low, double high)
    {
        AssertionsAssert.NotInRange(actual, low, high);
        AssertionsAssert.NotInRange(actual, low, high, Comparer<double>.Default);
        AssertionsAssert.NotInRange((float)actual, (float)low, (float)high);
        AssertionsAssert.NotInRange((Half)actual, (Half)low, (Half)high);
        AssertionsAssert.NotInRange((NFloat)actual, (NFloat)low, (NFloat)high);
        AssertionsAssert.NotInRange((double?)actual, low, high);

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange(actual, low, high));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange(actual, low, high, Comparer<double>.Default));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange((float)actual, (float)low, (float)high));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange((Half)actual, (Half)low, (Half)high));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange((NFloat)actual, (NFloat)low, (NFloat)high));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange((double?)actual, low, high));
    }

    [Theory]
    [InlineData(null, 0, 10)]
    [InlineData(5, null, 10)]
    [InlineData(5, 0, null)]
    [InlineData(null, null, 10)]
    [InlineData(null, 0, null)]
    [InlineData(5, null, null)]
    [InlineData(null, null, null)]
    public void NullNullable_NotInRange(int? actual, int? low, int? high)
    {
        AssertionsAssert.NotInRange(actual, low, high);
        AssertionsAssert.NotInRange(actual, low, high, Comparer<int?>.Default);

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange(actual, low, high));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange(actual, low, high, Comparer<int?>.Default));
    }

    [Fact]
    public void NullNullable_CustomComparerIsHonored()
    {
        int? low = null;

        AssertionsAssert.InRange(5, low, 10, Comparer<int?>.Create((x, y) => Comparer<int?>.Default.Compare(x, y)));
    }

    [Fact]
    public void NullNullable_FailureMessage()
    {
        int? low = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.InRange(5, low, 10), """
            Assert.InRange() assertion failed.
            Expression: 5
            Expected: in range [<null>, 10]
            Actual:   5
            """);
    }

    [Fact]
    public void NaN_CustomComparerIsHonored()
    {
        AssertionsAssert.InRange(double.NaN, double.NaN, 10d, Comparer<double>.Create((x, y) => x.CompareTo(y)));
    }

    [Fact]
    public void NaN_FailureMessage()
    {
        var actual = double.NaN;

        AssertionTestHelpers.Validate(() => AssertionsAssert.InRange(actual, double.NaN, 10d), """
            Assert.InRange() assertion failed.
            Expression: actual
            Expected: in range [NaN, 10]
            Actual:   NaN
            """);
    }
}
