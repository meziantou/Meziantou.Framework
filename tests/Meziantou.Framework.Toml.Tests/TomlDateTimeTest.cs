using System;
using System.Globalization;
using Meziantou.Xunit;

namespace Meziantou.Framework.Toml.Tests;

public class TomlDateTimeTest
{
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

    private sealed class DateTimeModel
    {
        public DateTime Utc { get; set; }

        public DateTime Unspecified { get; set; }

        public DateTimeOffset Offset { get; set; }

        public TimeOnly Time { get; set; }
    }
}
