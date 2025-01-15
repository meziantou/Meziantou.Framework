using Xunit;

namespace Meziantou.Framework.SimpleQueryLanguage.Tests;

public class ValueParserTests
{
    [Fact]
    public void ParseDateTimeOffset_DateOnly()
    {
        Assert.True(ValueConverter.TryParseValue<DateTimeOffset>("2022-01-01", out var result));
        Assert.Equal(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void ParseDateTimeOffset_DateTime()
    {
        Assert.True(ValueConverter.TryParseValue<DateTimeOffset>("2022-01-01T00:00:00", out var result));
        Assert.Equal(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void ParseDateTimeOffset_DateTimeAndZ()
    {
        Assert.True(ValueConverter.TryParseValue<DateTimeOffset>("2022-01-01T00:00:00Z", out var result));
        Assert.Equal(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void ParseDateTimeOffset_DateTimeAndOffset()
    {
        Assert.True(ValueConverter.TryParseValue<DateTimeOffset>("2022-01-01T00:00:00+0001", out var result));
        Assert.Equal(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(1)), result);
    }

    [Fact]
    public void ParseDateTime_DateOnly()
    {
        Assert.True(ValueConverter.TryParseValue<DateTime>("2022-01-01", out var result));
        Assert.Equal(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc), result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void ParseDateTime_DateTime()
    {
        Assert.True(ValueConverter.TryParseValue<DateTime>("2022-01-01T00:00:00", out var result));
        Assert.Equal(new DateTime(2022, 1, 1, 0, 0, 0), result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void ParseDateTime_DateTimeAndZ()
    {
        Assert.True(ValueConverter.TryParseValue<DateTime>("2022-01-01T00:00:00Z", out var result));
        Assert.Equal(new DateTime(2022, 1, 1, 0, 0, 0), result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void ParseDateTime_DateTimeAndOffset()
    {
        Assert.True(ValueConverter.TryParseValue<DateTime>("2022-01-01T00:00:00", out var result));
        Assert.Equal(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc), result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void ParseTimeSpan()
    {
        Assert.True(ValueConverter.TryParseValue<TimeSpan>("12:34:56", out var result));
        Assert.Equal(new TimeSpan(12, 34, 56), result);
    }

    [Fact]
    public void TryParseMethod()
    {
        Assert.True(ValueConverter.TryParseValue<CustomTypeWithTryParse>("test", out var result));
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParseMethod_Empty()
    {
        Assert.False(ValueConverter.TryParseValue<CustomTypeWithTryParse>("", out _));
    }

    private sealed class CustomTypeWithTryParse
    {
        public static bool TryParse(string value, out CustomTypeWithTryParse result)
        {
            if (string.IsNullOrEmpty(value))
            {
                result = null;
                return false;
            }

            result = new CustomTypeWithTryParse();
            return true;
        }
    }
}
