using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Xunit;

namespace Meziantou.Framework.Toml.Tests;

public class TomlDateTimeTest
{
    [Theory]
    [InlineData("0001-01-01T00:00:00+00:00", -12, false)]
    [InlineData("0001-01-01T00:00:00+00:00", 14, true)]
    [InlineData("9999-12-31T23:59:59+00:00", 14, false)]
    [InlineData("9999-12-31T23:59:59+00:00", -12, true)]
    [InlineData("0001-01-01T03:00:00+01:00", -5, false)]
    [InlineData("0001-01-01T08:00:00+01:00", -5, true)]
    public void OffsetDateTime_LocalTimeOutOfRange_IsDetected(string value, int offsetHours, bool expected)
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(offsetHours), "test", "test");

        Assert.Equal(expected, TomlFormatHelper.IsLocalDateTimeInRange(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture), timeZone));
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00+00:00")]
    [InlineData("9999-12-31T23:59:59+00:00")]
    public void OffsetDateTime_ReadAsLocalDateTime_IsNotClamped(string value)
    {
        // A numeric offset is read as a local DateTime, which cannot hold every instant in every time zone
        var instant = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        var toml = "v = " + value;
        if (TomlFormatHelper.IsLocalDateTimeInRange(instant, TimeZoneInfo.Local))
        {
            Assert.Equal(instant.UtcDateTime, TomlSerializer.Deserialize<Dictionary<string, DateTime>>(toml)!["v"].ToUniversalTime());
        }
        else
        {
            Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Dictionary<string, DateTime>>(toml));
        }
    }

    // The machines of the CI use UTC, where every local value is in range
    [Theory]
    [InlineData(false, 14, true)]
    [InlineData(false, -12, false)]
    [InlineData(true, -12, true)]
    [InlineData(true, 14, false)]
    public void LocalDateTime_OutOfRangeInUtc_IsDetectedInEveryTimeZone(bool maxValue, int offsetHours, bool throws)
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(offsetHours), "test", "test");
        var value = DateTime.SpecifyKind(maxValue ? DateTime.MaxValue : DateTime.MinValue, DateTimeKind.Local);

        if (throws)
        {
            Assert.Throws<TomlException>(() => TomlFormatHelper.ToTomlDateTime(value, TomlPropertyDisplayKind.Default, timeZone));
        }
        else
        {
            var dateTime = TomlFormatHelper.ToTomlDateTime(value, TomlPropertyDisplayKind.Default, timeZone);
            Assert.Equal(TomlDateTimeKind.OffsetDateTimeByNumber, dateTime.Kind);
            Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.FromHours(offsetHours)), dateTime.DateTime);
        }
    }

    [Fact]
    public void LocalDateTime_OutOfRangeInUtc_ThrowsTomlException()
    {
        foreach (var value in new[] { DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Local), DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Local) })
        {
            var utcTicks = value.Ticks - TimeZoneInfo.Local.GetUtcOffset(value).Ticks;
            if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
            {
                Assert.Throws<TomlException>(() => TomlFormatHelper.ToString(value, TomlPropertyDisplayKind.Default));
                Assert.Throws<TomlException>(() => (TomlDateTime)value);
                Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new Dictionary<string, DateTime> { ["value"] = value }));
            }
            else
            {
                Assert.Equal(value, TomlSerializer.Deserialize<Dictionary<string, DateTime>>(TomlSerializer.Serialize(new Dictionary<string, DateTime> { ["value"] = value }))!["value"].ToLocalTime());
            }
        }
    }

    [Fact]
    public void TestIConvertibleToString_NoCulture()
    {
        AssertConvertsToIsoDate(cultureInfo: null);
    }

    [Theory]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData("IV")]
    [InlineData("fr-FR")]
    [InlineData("es-ES")]
    public void TestIConvertibleToString(string cultureName)
    {
        AssertConvertsToIsoDate(CultureInfo.GetCultureInfo(cultureName));
    }

    [Theory]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData("fi-FI")]
    [InlineData("da-DK")]
    public void TestIConvertibleToString_TimeUsesColons(string cultureName)
    {
        var value = new TomlDateTime(new DateTimeOffset(2022, 1, 27, 7, 32, 5, TimeSpan.FromHours(-7)), 0, TomlDateTimeKind.OffsetDateTimeByNumber);

        Assert.Equal("2022-01-27T07:32:05-07:00", Convert.ToString(value, CultureInfo.GetCultureInfo(cultureName)));
    }

    [Theory]
    [InlineData("value = 2024-01-02T03:04:05Z\n")]
    [InlineData("value = 2024-01-02T03:04:05+14:00\n")]
    [InlineData("value = 2024-01-02T03:04:05\n")]
    [InlineData("value = 2024-01-02\n")]
    public void TestIConvertibleToDateTime_MatchesTheDeserializer(string toml)
    {
        var tomlDateTime = (TomlDateTime)TomlSerializer.Deserialize<Model.TomlTable>(toml)!["value"];
        var expected = TomlSerializer.Deserialize<Dictionary<string, DateTime>>(toml)!["value"];

        var converted = Convert.ToDateTime(tomlDateTime, CultureInfo.InvariantCulture);

        Assert.Equal(expected, converted);
        Assert.Equal(expected.Kind, converted.Kind);
        Assert.Equal(expected, Convert.ChangeType(tomlDateTime, typeof(DateTime), CultureInfo.InvariantCulture));
    }

    private static void AssertConvertsToIsoDate(CultureInfo? cultureInfo)
    {
        var dateTime = new TomlDateTime(new DateTimeOffset(new DateTime(2022, 1, 27)), 0, TomlDateTimeKind.LocalDate);

        var converted = Convert.ToString(dateTime, cultureInfo);

        Assert.Equal("2022-01-27", converted);
    }

    [Fact]
    public void Serialize_DateTimeMembers_KeepFractionalSeconds()
    {
        var model = new DateTimeModel
        {
            Utc = new DateTime(2024, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc),
            Unspecified = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified).AddTicks(1234567),
            Offset = new DateTimeOffset(2024, 1, 2, 3, 4, 5, 500, TimeSpan.FromHours(14)),
            Time = new TimeOnly(3, 4, 5, 250),
        };

        var toml = TomlSerializer.Serialize(model);

        Assert.Equal(
            """
            Utc = 2024-01-02T03:04:05.678Z
            Unspecified = 2024-01-02T03:04:05.1234567
            Offset = 2024-01-02T03:04:05.5+14:00
            Time = 03:04:05.25

            """.ReplaceLineEndings("\n"),
            toml);

        var roundtrip = TomlSerializer.Deserialize<DateTimeModel>(toml)!;
        Assert.Equal(model.Utc, roundtrip.Utc);
        Assert.Equal(model.Unspecified, roundtrip.Unspecified);
        Assert.Equal(model.Offset, roundtrip.Offset);
        Assert.Equal(model.Offset.Offset, roundtrip.Offset.Offset);
        Assert.Equal(model.Time, roundtrip.Time);
    }

    [Fact]
    public void DateTime_EveryKind_Roundtrips()
    {
        var values = new Dictionary<string, DateTime>
        {
            ["utc"] = new DateTime(2024, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc),
            ["local"] = new DateTime(2024, 1, 2, 3, 4, 5, 678, DateTimeKind.Local),
            ["unspecified"] = new DateTime(2024, 1, 2, 3, 4, 5, 678, DateTimeKind.Unspecified),
        };

        var roundtrip = TomlSerializer.Deserialize<Dictionary<string, DateTime>>(TomlSerializer.Serialize(values))!;

        foreach (var (key, value) in values)
        {
            Assert.Equal(value, roundtrip[key]);
            Assert.Equal(value.Kind, roundtrip[key].Kind);
        }
    }

    [Fact]
    public void Deserialize_DateTimeWithNumericOffset_IsLocal()
    {
        var value = TomlSerializer.Deserialize<Dictionary<string, DateTime>>("value = 2024-01-02T03:04:05+14:00\n")!["value"];

        Assert.Equal(DateTimeKind.Local, value.Kind);
        Assert.Equal(new DateTime(2024, 1, 1, 13, 4, 5, DateTimeKind.Utc), value.ToUniversalTime());
    }

    [Fact]
    public void Serialize_TomlTableDateValues_DoNotDependOnTheMachineTimeZone()
    {
        var table = new Model.TomlTable
        {
            ["utc"] = new DateTime(2024, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc),
            ["unspecified"] = DateTime.MinValue,
            ["offset"] = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(14)),
            ["date"] = DateOnly.MinValue,
            ["time"] = new TimeOnly(3, 4, 5, 678),
            ["nanoseconds"] = new TomlDateTime(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1234567), 9, TomlDateTimeKind.OffsetDateTimeByZ),
        };

        var toml = TomlSerializer.Serialize(table);

        Assert.Equal(
            """
            utc = 2024-01-02T03:04:05.678Z
            unspecified = 0001-01-01T00:00:00
            offset = 2024-01-02T03:04:05+14:00
            date = 0001-01-01
            time = 03:04:05.678
            nanoseconds = 2024-01-02T03:04:05.1234567Z

            """.ReplaceLineEndings("\n"),
            toml);
    }

    [Fact]
    public void ImplicitConversion_FromDateTime_MatchesTheSerializer()
    {
        TomlDateTime utc = new DateTime(2024, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
        TomlDateTime unspecified = new DateTime(2024, 1, 2, 3, 4, 5, 678, DateTimeKind.Unspecified);
        TomlDateTime minValue = DateTime.MinValue;
        TomlDateTime maxValue = DateTime.MaxValue;

        Assert.Equal("2024-01-02T03:04:05.678Z", utc.ToString());
        Assert.Equal(TomlDateTimeKind.OffsetDateTimeByZ, utc.Kind);
        Assert.Equal("2024-01-02T03:04:05.678", unspecified.ToString());
        Assert.Equal(TomlDateTimeKind.LocalDateTime, unspecified.Kind);
        Assert.Equal(TimeSpan.Zero, unspecified.DateTime.Offset);
        Assert.Equal("0001-01-01T00:00:00", minValue.ToString());
        Assert.Equal("9999-12-31T23:59:59.9999999", maxValue.ToString());
        Assert.Equal(new TomlDateTime(DateTime.MinValue), minValue);
    }

    [Fact]
    public void Constructor_FromDateTime_KeepsFractionalSeconds()
    {
        var value = new TomlDateTime(new DateTime(2024, 1, 2, 3, 4, 5, 500, DateTimeKind.Utc));

        Assert.Equal(TomlDateTimeKind.LocalDateTime, value.Kind);
        Assert.Equal(1, value.SecondPrecision);
        Assert.Equal("2024-01-02T03:04:05.5", value.ToString());
    }

    [Theory]
    [InlineData("07:32:00.5", "07:32:00.50")]
    [InlineData("1979-05-27T07:32:00.1Z", "1979-05-27T07:32:00.100Z")]
    [InlineData("1979-05-27T07:32:00", "1979-05-27T07:32:00.000")]
    public void Equals_IgnoresTheNumberOfFractionalDigits(string left, string right)
    {
        var leftValue = (TomlDateTime)TomlSerializer.Deserialize<Model.TomlTable>("a = " + left)!["a"];
        var rightValue = (TomlDateTime)TomlSerializer.Deserialize<Model.TomlTable>("a = " + right)!["a"];

        Assert.NotEqual(leftValue.SecondPrecision, rightValue.SecondPrecision);
        Assert.Equal(leftValue, rightValue);
        Assert.Equal(leftValue.GetHashCode(), rightValue.GetHashCode());
    }

    [Fact]
    public void Equals_ComparesTheKind()
    {
        var utc = (TomlDateTime)TomlSerializer.Deserialize<Model.TomlTable>("a = 1979-05-27T07:32:00Z")!["a"];
        var zeroOffset = (TomlDateTime)TomlSerializer.Deserialize<Model.TomlTable>("a = 1979-05-27T07:32:00+00:00")!["a"];

        Assert.NotEqual(utc, zeroOffset);
    }

    [Fact]
    public void Deserialize_LocalTime_HasTheFirstDate()
    {
        var value = (TomlDateTime)TomlSerializer.Deserialize<Model.TomlTable>("a = 07:32:00.5")!["a"];

        Assert.Equal(new DateTimeOffset(1, 1, 1, 7, 32, 0, 500, TimeSpan.Zero), value.DateTime);
        Assert.Equal(Helpers.TomlFormatHelper.ToTomlDateTime(new TimeOnly(7, 32, 0, 500)), value);
    }

    private sealed class DateTimeModel
    {
        public DateTime Utc { get; set; }

        public DateTime Unspecified { get; set; }

        public DateTimeOffset Offset { get; set; }

        public TimeOnly Time { get; set; }
    }
}
