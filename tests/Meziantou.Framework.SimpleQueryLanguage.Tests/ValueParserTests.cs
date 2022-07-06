using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.SimpleQueryLanguage.Tests;

public class ValueParserTests
{
    [Fact]
    public void ParseDateTimeOffset_DateOnly()
    {
        ValueConverter.TryParseValue<DateTimeOffset>("2022-01-01", out var result).Should().BeTrue();
        result.Should().Be(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ParseDateTimeOffset_DateTime()
    {
        ValueConverter.TryParseValue<DateTimeOffset>("2022-01-01T00:00:00", out var result).Should().BeTrue();
        result.Should().Be(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ParseDateTimeOffset_DateTimeAndZ()
    {
        ValueConverter.TryParseValue<DateTimeOffset>("2022-01-01T00:00:00Z", out var result).Should().BeTrue();
        result.Should().Be(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ParseDateTimeOffset_DateTimeAndOffset()
    {
        ValueConverter.TryParseValue<DateTimeOffset>("2022-01-01T00:00:00+0001", out var result).Should().BeTrue();
        result.Should().Be(new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ParseDateTime_DateOnly()
    {
        ValueConverter.TryParseValue<DateTime>("2022-01-01", out var result).Should().BeTrue();
        result.Should().Be(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ParseDateTime_DateTime()
    {
        ValueConverter.TryParseValue<DateTime>("2022-01-01T00:00:00", out var result).Should().BeTrue();
        result.Should().Be(new DateTime(2022, 1, 1, 0, 0, 0));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ParseDateTime_DateTimeAndZ()
    {
        ValueConverter.TryParseValue<DateTime>("2022-01-01T00:00:00Z", out var result).Should().BeTrue();
        result.Should().Be(new DateTime(2022, 1, 1, 0, 0, 0));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ParseDateTime_DateTimeAndOffset()
    {
        ValueConverter.TryParseValue<DateTime>("2022-01-01T00:00:00", out var result).Should().BeTrue();
        result.Should().Be(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ParseTimeSpan()
    {
        ValueConverter.TryParseValue<TimeSpan>("12:34:56", out var result).Should().BeTrue();
        result.Should().Be(new TimeSpan(12, 34, 56));
    }

    [Fact]
    public void TryParseMethod()
    {
        ValueConverter.TryParseValue<CustomTypeWithTryParse>("test", out var result).Should().BeTrue();
        result.Should().NotBeNull();
    }

    [Fact]
    public void TryParseMethod_Empty()
    {
        ValueConverter.TryParseValue<CustomTypeWithTryParse>("", out _).Should().BeFalse();
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
