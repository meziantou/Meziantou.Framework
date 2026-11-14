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
#if NET11_0_OR_GREATER
        AssertionsAssert.NotInRange((System.Numerics.BFloat16)actual, (System.Numerics.BFloat16)low, (System.Numerics.BFloat16)high);
        AssertionsAssert.NotInRange((System.Numerics.Decimal32)actual, (System.Numerics.Decimal32)low, (System.Numerics.Decimal32)high);
        AssertionsAssert.NotInRange((System.Numerics.Decimal64)actual, (System.Numerics.Decimal64)low, (System.Numerics.Decimal64)high);
        AssertionsAssert.NotInRange((System.Numerics.Decimal128)actual, (System.Numerics.Decimal128)low, (System.Numerics.Decimal128)high);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange((System.Numerics.BFloat16)actual, (System.Numerics.BFloat16)low, (System.Numerics.BFloat16)high));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange((System.Numerics.Decimal32)actual, (System.Numerics.Decimal32)low, (System.Numerics.Decimal32)high));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange((System.Numerics.Decimal64)actual, (System.Numerics.Decimal64)low, (System.Numerics.Decimal64)high));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange((System.Numerics.Decimal128)actual, (System.Numerics.Decimal128)low, (System.Numerics.Decimal128)high));
#endif
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

    [Fact]
    public void RefStruct_Success()
    {
        var actual = new Temperature(20);
        var low = new Temperature(0);
        var high = new Temperature(25);

        AssertionsAssert.InRange(actual, low, high);
        AssertionsAssert.InRange(new Temperature(0), new Temperature(0), new Temperature(25));
        AssertionsAssert.InRange(new Temperature(25), new Temperature(0), new Temperature(25));
        AssertionsAssert.InRange(in actual, in low, in high);
    }

    [Fact]
    public void RefStruct_FailsWhenValueIsBelowRange()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.InRange(new Temperature(-1), new Temperature(0), new Temperature(25), "custom message"), """
            Assert.InRange() assertion failed.
            Message: custom message
            Expression: new Temperature(-1)
            Expected: in range [new Temperature(0), new Temperature(25)]
            Type: Meziantou.Framework.Assertions.Tests.Temperature (ref struct values cannot be displayed)
            """);
    }

    [Fact]
    public void RefStruct_FailsWhenValueIsAboveRange()
    {
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.InRange(new Temperature(26), new Temperature(0), new Temperature(25)));
    }

    [Fact]
    public void NotInRange_RefStruct_Success()
    {
        var actual = new Temperature(30);
        var low = new Temperature(0);
        var high = new Temperature(25);

        AssertionsAssert.NotInRange(actual, low, high);
        AssertionsAssert.NotInRange(new Temperature(-1), new Temperature(0), new Temperature(25));
        AssertionsAssert.NotInRange(in actual, in low, in high);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(25)]
    public void NotInRange_RefStruct_FailsWhenValueIsInRange(int degrees)
    {
        AssertionsAssert.InRange(new Temperature(degrees), new Temperature(0), new Temperature(25));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotInRange(new Temperature(degrees), new Temperature(0), new Temperature(25)));
    }

    [Fact]
    public void NotInRange_RefStruct_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotInRange(new Temperature(20), new Temperature(0), new Temperature(25), "custom message"), """
            Assert.NotInRange() assertion failed.
            Message: custom message
            Expression: new Temperature(20)
            Not expected: in range [new Temperature(0), new Temperature(25)]
            Type: Meziantou.Framework.Assertions.Tests.Temperature (ref struct values cannot be displayed)
            """);
    }

    [Fact]
    public void Documentation_InRangeRefStruct()
    {
        #region Assert_InRangeRefStruct
        var temperature = new Temperature(20);
        Assert.InRange(temperature, new Temperature(0), new Temperature(25));   // compared with IComparable<Temperature>.CompareTo
        Assert.NotInRange(temperature, new Temperature(21), new Temperature(25));
        #endregion
    }

    [Fact]
    public void Documentation_InRange()
    {
        #region Assert_InRange
        var percentage = 42;

        Assert.InRange(percentage, 0, 100);         // the value comes first, then the inclusive bounds
        Assert.NotInRange(percentage, 50, 100);

        Assert.InRange("banana", "apple", "cherry", StringComparer.Ordinal);
        Assert.NotInRange(double.NaN, double.MinValue, double.MaxValue);
        #endregion
    }
}
