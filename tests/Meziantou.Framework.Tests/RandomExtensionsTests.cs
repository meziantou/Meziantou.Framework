namespace Meziantou.Framework.Tests;

public sealed class RandomExtensionsTests
{
    [Fact]
    public void Documentation_Values()
    {
        #region RandomExtensions_Values
        // A seed gives the same values on every run; use Random.Shared otherwise
        var random = new Random(42);

        // The minimum is inclusive and the maximum is exclusive: a die roll is between 1 and 6
        var die = random.NextInt32(1, 7);
        Assert.InRange(die, 1, 6);

        string[] colors = ["red", "green", "blue"];
        Assert.Contains(random.NextFromList(colors), colors);

        var code = random.NextString(length: 6, chars: "ABCDEFGHJKLMNPQRSTUVWXYZ23456789");
        Assert.HasCount(6, code);

        var date = random.NextDateTime(new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));
        Assert.Equal(2026, date.Year);
        #endregion
    }

    [Fact]
    public void NextUInt64_SpreadsOverASmallRange()
    {
        var random = new Random(42);
        var values = new HashSet<ulong>();
        for (var i = 0; i < 20_000; i++)
        {
            values.Add(random.NextUInt64(0, 1000));
        }

        // The previous implementation overflowed and returned 0 for every draw
        Assert.HasCountGreaterThan(900, values);
    }

    [Fact]
    public void NextUInt64_StaysWithinTheRange()
    {
        var random = new Random(42);
        for (var i = 0; i < 20_000; i++)
        {
            var value = random.NextUInt64(100, 200);
            Assert.InRange(value, 100ul, 199ul);
        }
    }

    [Fact]
    public void NextUInt64_FullRangeIsNotConstant()
    {
        var random = new Random(42);
        var values = new HashSet<ulong>();
        for (var i = 0; i < 100; i++)
        {
            values.Add(random.NextUInt64());
        }

        Assert.HasCountGreaterThan(90, values);
    }

    [Fact]
    public void NextUInt64_EmptyRangeReturnsMin()
    {
        Assert.Equal(7ul, new Random(42).NextUInt64(7, 7));
    }

    [Fact]
    public void NextUInt64_InvertedRangeThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Random(42).NextUInt64(10, 5));
    }

    [Fact]
    public void NextDecimal_DefaultRangeDoesNotOverflow()
    {
        var random = new Random(42);
        for (var i = 0; i < 1000; i++)
        {
            var value = random.NextDecimal();
            Assert.InRange(value, decimal.MinValue, decimal.MaxValue);
        }
    }

    [Fact]
    public void NextDecimal_StaysWithinTheRange()
    {
        var random = new Random(42);
        for (var i = 0; i < 10_000; i++)
        {
            var value = random.NextDecimal(-10m, 10m);
            Assert.InRange(value, -10m, 10m);
        }
    }

    [Fact]
    public void NextDecimal_EmptyRangeReturnsMin()
    {
        Assert.Equal(7m, new Random(42).NextDecimal(7m, 7m));
    }

    [Fact]
    public void NextDecimal_InvertedRangeThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Random(42).NextDecimal(10m, 5m));
    }
}
