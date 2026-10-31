namespace Meziantou.Framework.Imaging.Tests;

public sealed class FrameDurationTests
{
    [Fact]
    public void DefaultIsValidZero()
    {
        var value = default(FrameDuration);
        Assert.Equal(FrameDuration.Zero, value);
        Assert.True(value.IsZero);
        Assert.Equal(0, value.Numerator);
        Assert.Equal(1, value.Denominator);
        Assert.Equal(TimeSpan.Zero, value.ToTimeSpan());
        Assert.Equal(new FrameDuration(0, 7), value);
    }

    [Fact]
    public void ValuesAreNormalized()
    {
        var value = new FrameDuration(10, 100);
        Assert.Equal(1, value.Numerator);
        Assert.Equal(10, value.Denominator);
        Assert.Equal(FrameDuration.FromMilliseconds(100), value);
        Assert.Equal(value.GetHashCode(), new FrameDuration(3, 30).GetHashCode());
    }

    [Fact]
    public void InvalidValuesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameDuration(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameDuration(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameDuration.FromTimeSpan(TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void TimeSpanConversionRoundsToNearestTick()
    {
        Assert.Equal(TimeSpan.FromTicks(3_333_333), new FrameDuration(1, 3).ToTimeSpan());
        Assert.Equal(TimeSpan.FromTicks(6_666_667), new FrameDuration(2, 3).ToTimeSpan());
        Assert.Equal(TimeSpan.FromTicks(1), new FrameDuration(1, 20_000_000).ToTimeSpan()); // half a tick rounds up
        Assert.Equal(new FrameDuration(123_456_789, 10_000_000), FrameDuration.FromTimeSpan(TimeSpan.FromTicks(123_456_789)));
        Assert.Throws<OverflowException>(() => new FrameDuration(long.MaxValue, 1).ToTimeSpan());
    }

    [Fact]
    public void ComparisonUsesExactRationalValues()
    {
        Assert.True(new FrameDuration(1, 3) < new FrameDuration(1, 2));
        Assert.True(new FrameDuration(long.MaxValue, long.MaxValue - 1) > new FrameDuration(1, 1));
        Assert.Equal(0, new FrameDuration(2, 6).CompareTo(new FrameDuration(1, 3)));
    }

    [Fact]
    public void DefaultZeroAndEquivalentZerosAreEqual()
    {
        FrameDuration[] zeros = [default, FrameDuration.Zero, new(0, 1), new(0, long.MaxValue), FrameDuration.FromMilliseconds(0), FrameDuration.FromTimeSpan(TimeSpan.Zero)];
        foreach (var zero in zeros)
        {
            Assert.Equal(default, zero);
            Assert.True(zero == FrameDuration.Zero);
            Assert.Equal(FrameDuration.Zero.GetHashCode(), zero.GetHashCode());
            Assert.Equal((0, 1), (zero.Numerator, zero.Denominator));
            Assert.Equal("0/1 s", zero.ToString());
        }
    }

    [Theory]
    [InlineData(2, 4, 1, 2)]
    [InlineData(3, 30, 1, 10)]
    [InlineData(50, 100, 1, 2)]
    [InlineData(7, 1000, 7, 1000)]
    [InlineData(10_000_000, 10_000_000, 1, 1)]
    [InlineData(long.MaxValue, long.MaxValue, 1, 1)]
    [InlineData(long.MaxValue - 1, 2, (long.MaxValue - 1) / 2, 1)]
    public void ValuesAreGcdReduced(long numerator, long denominator, long expectedNumerator, long expectedDenominator)
    {
        var value = new FrameDuration(numerator, denominator);
        Assert.Equal((expectedNumerator, expectedDenominator), (value.Numerator, value.Denominator));
        Assert.Equal(new FrameDuration(expectedNumerator, expectedDenominator), value);
    }

    [Fact]
    public void EqualityIsByRationalValue()
    {
        Assert.Equal(new FrameDuration(1, 10), new FrameDuration(10, 100));
        Assert.Equal(new FrameDuration(1, 10), FrameDuration.FromMilliseconds(100));
        Assert.Equal(new FrameDuration(1, 10), FrameDuration.FromTimeSpan(TimeSpan.FromMilliseconds(100)));
        Assert.NotEqual(new FrameDuration(1, 10), new FrameDuration(1, 11));
        Assert.True(new FrameDuration(1, 3) != new FrameDuration(333, 1000));
        Assert.True(new FrameDuration(1, 3).Equals((object)new FrameDuration(2, 6)));
        Assert.False(new FrameDuration(1, 3).Equals("1/3"));
    }

    [Fact]
    public void ConversionsRejectNegativeValuesWithTheRightParameterName()
    {
        Assert.Equal("numerator", Assert.Throws<ArgumentOutOfRangeException>(() => new FrameDuration(-1, 1)).ParamName);
        Assert.Equal("denominator", Assert.Throws<ArgumentOutOfRangeException>(() => new FrameDuration(1, -1)).ParamName);
        Assert.Equal("milliseconds", Assert.Throws<ArgumentOutOfRangeException>(() => FrameDuration.FromMilliseconds(-1)).ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => FrameDuration.FromTimeSpan(TimeSpan.MinValue)).ParamName);
    }

    [Fact]
    public void WholeTickDurationsConvertExactly()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(70), new FrameDuration(7, 100).ToTimeSpan()); // GIF hundredths
        Assert.Equal(TimeSpan.FromTicks(1), new FrameDuration(1, 10_000_000).ToTimeSpan());
        Assert.Equal(TimeSpan.MaxValue, FrameDuration.FromTimeSpan(TimeSpan.MaxValue).ToTimeSpan());
        foreach (var ticks in new long[] { 0, 1, 9_999_999, 10_000_001, 123_456_789_012 })
        {
            Assert.Equal(TimeSpan.FromTicks(ticks), FrameDuration.FromTimeSpan(TimeSpan.FromTicks(ticks)).ToTimeSpan());
        }
    }

    [Theory]
    [InlineData(1, 20_000_000, 1)] // exactly half a tick: ties round up
    [InlineData(3, 20_000_000, 2)] // 1.5 ticks
    [InlineData(5, 20_000_000, 3)] // 2.5 ticks
    [InlineData(1, 20_000_001, 0)] // just below half a tick
    [InlineData(1, 3, 3_333_333)] // 3,333,333.33
    [InlineData(2, 3, 6_666_667)] // 6,666,666.67
    [InlineData(1, 30_000_000, 0)]
    public void NonTickDurationsRoundToNearestTickWithTiesUp(long numerator, long denominator, long expectedTicks)
        => Assert.Equal(TimeSpan.FromTicks(expectedTicks), new FrameDuration(numerator, denominator).ToTimeSpan());

    [Fact]
    public void TimeSpanOverflowIsReportedNotClamped()
    {
        // TimeSpan.MaxValue.Ticks is long.MaxValue: the largest representable duration converts exactly
        Assert.Equal(TimeSpan.MaxValue, new FrameDuration(long.MaxValue, TimeSpan.TicksPerSecond).ToTimeSpan());

        // Slightly larger durations overflow (128-bit intermediate arithmetic, no wrap-around)
        Assert.Throws<OverflowException>(() => new FrameDuration(long.MaxValue, TimeSpan.TicksPerSecond - 1).ToTimeSpan());
        Assert.Throws<OverflowException>(() => new FrameDuration(long.MaxValue, 1).ToTimeSpan());
        Assert.Throws<OverflowException>(() => FrameDuration.FromMilliseconds(long.MaxValue).ToTimeSpan());
    }

    [Fact]
    public void FloatingPointViewsAreApproximations()
    {
        Assert.Equal(0.1, new FrameDuration(1, 10).TotalSeconds);
        Assert.Equal(100.0, new FrameDuration(1, 10).TotalMilliseconds);
        Assert.Equal(1000.0 / 3, new FrameDuration(1, 3).TotalMilliseconds, 9);
        Assert.Equal((double)long.MaxValue * 1000, new FrameDuration(long.MaxValue, 1).TotalMilliseconds); // no overflow
    }

    [Fact]
    public void OrderingOperatorsAndCompareTo()
    {
        var third = new FrameDuration(1, 3);
        var half = new FrameDuration(1, 2);
        Assert.True(third < half);
        Assert.True(third <= half);
        Assert.True(half > third);
        Assert.True(half >= third);
        Assert.InRange(third, new FrameDuration(2, 6), new FrameDuration(2, 6));
        Assert.Equal(1, third.CompareTo(null));
        Assert.Equal(-1, Math.Sign(third.CompareTo((object)half)));
        Assert.Throws<ArgumentException>(() => third.CompareTo("1/3"));
        Assert.Equal([FrameDuration.Zero, third, half], new[] { half, FrameDuration.Zero, third }.Order());
    }

    [Fact]
    public void AdditionIsExactAndNormalized()
    {
        // GIF hundredths plus an APNG fraction: 7/100 + 1/3 = 121/300, exactly
        Assert.Equal(new FrameDuration(121, 300), new FrameDuration(7, 100) + new FrameDuration(1, 3));
        Assert.Equal(new FrameDuration(121, 300), new FrameDuration(7, 100).Add(new FrameDuration(1, 3)));

        // The sum is normalized: 1/6 + 1/3 = 1/2
        var sum = new FrameDuration(1, 6) + new FrameDuration(1, 3);
        Assert.Equal(1, sum.Numerator);
        Assert.Equal(2, sum.Denominator);

        // Zero is the identity, and the same denominator adds the numerators
        Assert.Equal(new FrameDuration(2, 7), FrameDuration.Zero + new FrameDuration(2, 7));
        Assert.Equal(new FrameDuration(2, 7), new FrameDuration(2, 7) + default(FrameDuration));
        Assert.Equal(FrameDuration.FromMilliseconds(250), FrameDuration.FromMilliseconds(100) + FrameDuration.FromMilliseconds(150));
        Assert.Equal(FrameDuration.FromTimeSpan(TimeSpan.FromTicks(3)), FrameDuration.FromTimeSpan(TimeSpan.FromTicks(1)) + FrameDuration.FromTimeSpan(TimeSpan.FromTicks(2)));
    }

    [Fact]
    public void ManyFrameDurationsSumWithoutDrift()
    {
        // 300 frames of 1/30 s are exactly 10 s, where a sum of rounded milliseconds or of doubles would drift
        var total = FrameDuration.Zero;
        for (var i = 0; i < 300; i++)
        {
            total += new FrameDuration(1, 30);
        }

        Assert.Equal(new FrameDuration(10, 1), total);
        Assert.Equal(TimeSpan.FromSeconds(10), total.ToTimeSpan());
    }

    [Fact]
    public void SubtractionIsExactAndNeverNegative()
    {
        Assert.Equal(new FrameDuration(1, 6), new FrameDuration(1, 2) - new FrameDuration(1, 3));
        Assert.Equal(new FrameDuration(1, 6), new FrameDuration(1, 2).Subtract(new FrameDuration(1, 3)));
        Assert.Equal(FrameDuration.Zero, new FrameDuration(2, 6) - new FrameDuration(1, 3));
        Assert.Equal(1, (new FrameDuration(2, 6) - new FrameDuration(1, 3)).Denominator);
        Assert.Equal(new FrameDuration(5, 7), new FrameDuration(5, 7) - FrameDuration.Zero);
        Assert.Equal(new FrameDuration(long.MaxValue, 1), new FrameDuration(long.MaxValue, 1) - default(FrameDuration));

        // A frame duration is never negative: the difference throws instead of being clamped to zero
        Assert.Throws<OverflowException>(() => new FrameDuration(1, 3) - new FrameDuration(1, 2));
        Assert.Throws<OverflowException>(() => FrameDuration.Zero - new FrameDuration(1, long.MaxValue));
    }

    [Fact]
    public void ArithmeticOverflowIsReportedNotRounded()
    {
        // The numerator of the sum exceeds 64 bits
        Assert.Throws<OverflowException>(() => new FrameDuration(long.MaxValue, 1) + new FrameDuration(1, 1));
        Assert.Throws<OverflowException>(() => new FrameDuration(long.MaxValue, 1).Add(new FrameDuration(1, 2)));

        // Large coprime denominators: the exact denominator of the result needs more than 64 bits, even after normalization
        var left = new FrameDuration(1, long.MaxValue);
        var right = new FrameDuration(1, long.MaxValue - 1);
        Assert.Throws<OverflowException>(() => left + right);
        Assert.Throws<OverflowException>(() => left - right);

        // The 128-bit intermediate result is normalized before the range check: these fit once reduced
        Assert.Equal(new FrameDuration(2, long.MaxValue), left + left);
        Assert.Equal(new FrameDuration(long.MaxValue, 1), new FrameDuration(long.MaxValue - 1, 2) + new FrameDuration(long.MaxValue - 1, 2) + new FrameDuration(1, 1));
        Assert.Equal(new FrameDuration(long.MaxValue, 1), new FrameDuration(long.MaxValue, 1) - new FrameDuration(long.MaxValue - 1, long.MaxValue - 1) + new FrameDuration(1, 1));
    }

    [Fact]
    public void SumOfDurationsConvertsToTheSumOfTimeSpansWhenBothAreWholeTicks()
    {
        var first = TimeSpan.FromMilliseconds(40);
        var second = TimeSpan.FromTicks(123_457);
        Assert.Equal(first + second, (FrameDuration.FromTimeSpan(first) + FrameDuration.FromTimeSpan(second)).ToTimeSpan());
        Assert.Equal(first - second, (FrameDuration.FromTimeSpan(first) - FrameDuration.FromTimeSpan(second)).ToTimeSpan());

        // A sum of non-tick durations is rounded once, at the conversion: 1/3 + 1/3 + 1/3 is exactly 1 s, while the
        // sum of the three rounded conversions is one tick short
        var third = new FrameDuration(1, 3);
        Assert.Equal(TimeSpan.FromSeconds(1), (third + third + third).ToTimeSpan());
        Assert.Equal(TimeSpan.FromTicks(9_999_999), third.ToTimeSpan() + third.ToTimeSpan() + third.ToTimeSpan());
    }
}
