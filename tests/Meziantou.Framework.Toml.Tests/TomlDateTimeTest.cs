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

    private sealed class DateTimeModel
    {
        public DateTime Utc { get; set; }

        public DateTime Unspecified { get; set; }

        public DateTimeOffset Offset { get; set; }

        public TimeOnly Time { get; set; }
    }
}
