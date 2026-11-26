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
    public void ParseString_ReturnsTheValue()
    {
        var value = "text";

        Assert.True(ValueConverter.TryParseValue<string>(value, out var result));
        Assert.Same(value, result);
    }

    [Fact]
    public void ParseTypesWithTryParseMethod_RepeatedCalls()
    {
        // The TryParse methods are looked up once per type, so each type is parsed several times, successfully or not
        for (var i = 0; i < 3; i++)
        {
            Assert.True(ValueConverter.TryParseValue<bool>("True", out var boolean));
            Assert.True(boolean);
            Assert.False(ValueConverter.TryParseValue<bool>("yes", out _));

            Assert.True(ValueConverter.TryParseValue<char>("c", out var character));
            Assert.Equal('c', character);
            Assert.False(ValueConverter.TryParseValue<char>("cc", out _));

            Assert.True(ValueConverter.TryParseValue<Guid>("3f2504e0-4f89-11d3-9a0c-0305e82c3301", out var guid));
            Assert.Equal(new Guid("3f2504e0-4f89-11d3-9a0c-0305e82c3301"), guid);
            Assert.False(ValueConverter.TryParseValue<Guid>("not a guid", out _));

            Assert.True(ValueConverter.TryParseValue<Guid?>("3f2504e0-4f89-11d3-9a0c-0305e82c3301", out var nullableGuid));
            Assert.Equal(new Guid("3f2504e0-4f89-11d3-9a0c-0305e82c3301"), nullableGuid);

            Assert.True(ValueConverter.TryParseValue<CustomTypeWithTryParse>("test", out var custom));
            Assert.NotNull(custom);
            Assert.False(ValueConverter.TryParseValue<CustomTypeWithTryParse>("", out _));
        }
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

    [Fact]
    public void ParseNullableInt32()
    {
        Assert.True(ValueConverter.TryParseValue<int?>("10", out var result));
        Assert.Equal(10, result);
    }

    [Fact]
    public void ParseNullableInt32_Invalid()
    {
        Assert.False(ValueConverter.TryParseValue<int?>("not-a-number", out _));
    }

    [Fact]
    public void ParseNullableDateTimeOffset()
    {
        Assert.True(ValueConverter.TryParseValue<DateTimeOffset?>("2022-01-01", out var result));
        Assert.Equal(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void ParseNullableEnum()
    {
        Assert.True(ValueConverter.TryParseValue<DayOfWeek?>("monday", out var result));
        Assert.Equal(DayOfWeek.Monday, result);
    }

    [Fact]
    public void ParseNullableCustomTypeWithTryParse()
    {
        Assert.True(ValueConverter.TryParseValue<CustomStructWithTryParse?>("test", out var result));
        Assert.NotNull(result);
    }

    private readonly struct CustomStructWithTryParse
    {
        public static bool TryParse(string value, out CustomStructWithTryParse result)
        {
            result = default;
            return !string.IsNullOrEmpty(value);
        }
    }

    private sealed class CustomTypeWithTryParse
    {
        public static bool TryParse(string value, out CustomTypeWithTryParse? result)
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
