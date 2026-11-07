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

    [Fact]
    public void NextFromList_List()
    {
        var random = new Random(42);
        var list = new List<int> { 1, 2, 3 };
        Assert.Contains(random.NextFromList(list), list);

        var collection = new System.Collections.ObjectModel.Collection<int> { 1, 2, 3 };
        Assert.Contains(random.NextFromList(collection), collection);

        var set = new HashSet<int> { 1, 2, 3 };
        Assert.Contains(random.NextFromList(set), set);
    }

    [Fact]
    public void NextByte_ReturnsEveryValue()
    {
        var random = new Random(42);
        var values = new HashSet<byte>();
        for (var i = 0; i < 20_000; i++)
        {
            values.Add(random.NextByte());
        }

        Assert.HasCount(256, values);
    }

    [Fact]
    public void NextSByte_ReturnsEveryValue()
    {
        var random = new Random(42);
        var values = new HashSet<sbyte>();
        for (var i = 0; i < 20_000; i++)
        {
            values.Add(random.NextSByte());
        }

        Assert.HasCount(256, values);
    }

    [Fact]
    public void NextInt16_ReturnsTheWholeRange()
    {
        var random = new Random(42);
        var min = short.MaxValue;
        var max = short.MinValue;
        for (var i = 0; i < 2_000_000 && (min != short.MinValue || max != short.MaxValue); i++)
        {
            var value = random.NextInt16();
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        Assert.Equal(short.MinValue, min);
        Assert.Equal(short.MaxValue, max);
    }

    [Fact]
    public void NextUInt16_ReturnsTheWholeRange()
    {
        var random = new Random(42);
        var min = ushort.MaxValue;
        var max = ushort.MinValue;
        for (var i = 0; i < 2_000_000 && (min != ushort.MinValue || max != ushort.MaxValue); i++)
        {
            var value = random.NextUInt16();
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        Assert.Equal(ushort.MinValue, min);
        Assert.Equal(ushort.MaxValue, max);
    }

    [Fact]
    public void NextByte_MaxIsExcluded()
    {
        var random = new Random(42);
        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(random.NextByte(10, 12), (byte)10, (byte)11);
            Assert.InRange(random.NextSByte(-12, -10), (sbyte)-12, (sbyte)-11);
            Assert.InRange(random.NextInt16(-12, -10), (short)-12, (short)-11);
            Assert.InRange(random.NextUInt16(10, 12), (ushort)10, (ushort)11);
            Assert.InRange(random.NextUInt32(10, 12), 10u, 11u);
        }
    }

    [Fact]
    public void NextUInt32_ReturnsLargeValues()
    {
        var random = new Random(42);
        var hasLargeValue = false;
        for (var i = 0; i < 100 && !hasLargeValue; i++)
        {
            hasLargeValue = random.NextUInt32() > int.MaxValue;
        }

        Assert.True(hasLargeValue);
    }

    [Fact]
    public void NextInt64_FullRangeDoesNotOverflow()
    {
        var random = new Random(42);
        var values = new HashSet<long>();
        for (var i = 0; i < 100; i++)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var value = RandomExtensions.NextInt64(random, long.MinValue, long.MaxValue);
#pragma warning restore CS0618
            Assert.NotEqual(long.MaxValue, value);
            values.Add(value);
        }

        Assert.HasCountGreaterThan(90, values);
    }

    [Fact]
    public void NextDouble_FullRangeIsFinite()
    {
        var random = new Random(42);
        for (var i = 0; i < 1000; i++)
        {
            var value = random.NextDouble(double.MinValue, double.MaxValue);
            Assert.True(double.IsFinite(value));
        }
    }

    [Fact]
    public void NextDouble_StaysWithinTheRange()
    {
        var random = new Random(42);
        var max = Math.BitIncrement(1D);
        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(random.NextDouble(-10, 10), -10D, Math.BitDecrement(10D));
            Assert.Equal(1D, random.NextDouble(1D, max));
        }
    }

    [Fact]
    public void NextDouble_EmptyRangeReturnsMin()
    {
        Assert.Equal(7D, new Random(42).NextDouble(7D, 7D));
    }

    [Fact]
    public void NextDouble_InvertedRangeThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Random(42).NextDouble(10D, 5D));
    }

    [Fact]
    public void NextSingle_FullRangeIsFinite()
    {
        var random = new Random(42);
        var max = MathF.BitIncrement(1f);
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(float.IsFinite(random.NextSingle(float.MinValue, float.MaxValue)));
            Assert.Equal(1f, random.NextSingle(1f, max));
        }
    }

    [Fact]
    public void NextDateTime_FullRange()
    {
        var random = new Random(42);
        var years = new HashSet<int>();
        for (var i = 0; i < 1000; i++)
        {
            var value = random.NextDateTime(DateTime.MinValue, DateTime.MaxValue);
            Assert.True(value < DateTime.MaxValue);
            years.Add(value.Year);
        }

        Assert.HasCountGreaterThan(900, years);
    }

    [Fact]
    public void NextDateTime_EmptyRangeReturnsMin()
    {
        var date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var value = new Random(42).NextDateTime(date, date);
        Assert.Equal(date, value);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
    }

    [Fact]
    public void NextDateTime_InvertedRangeThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Random(42).NextDateTime(new DateTime(2027, 1, 1), new DateTime(2026, 1, 1)));
    }
}
