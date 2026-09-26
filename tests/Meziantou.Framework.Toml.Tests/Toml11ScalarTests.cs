using System;

namespace Meziantou.Framework.Toml.Tests;

public sealed class Toml11ScalarTests
{
    [Fact]
    public void Deserialize_BasicString_SupportsEscapeAndHexEscapes()
    {
        var options = new TomlSerializerOptions
        {
            RootValueHandling = TomlRootValueHandling.WrapInRootKey,
        };

        var value = TomlSerializer.Deserialize<string>("value = \"A\\e\\x41\"\n", options);
        Assert.Equal("A\u001BA", value);
    }

    [Fact]
    public void Deserialize_TomlDateTime_SupportsMinuteOnlyLocalTime()
    {
        var options = new TomlSerializerOptions
        {
            RootValueHandling = TomlRootValueHandling.WrapInRootKey,
        };

        var value = TomlSerializer.Deserialize<TomlDateTime>("value = 07:32\n", options);
        Assert.Equal(TomlDateTimeKind.LocalTime, value.Kind);
        Assert.Equal(new TimeSpan(7, 32, 0), value.DateTime.TimeOfDay);
    }

    [Fact]
    public void Deserialize_TomlDateTime_SupportsMinuteOnlyOffsetDateTime()
    {
        var options = new TomlSerializerOptions
        {
            RootValueHandling = TomlRootValueHandling.WrapInRootKey,
        };

        var value = TomlSerializer.Deserialize<TomlDateTime>("value = 1979-05-27T07:32Z\n", options);
        Assert.Equal(TomlDateTimeKind.OffsetDateTimeByZ, value.Kind);
        Assert.Equal(1979, value.DateTime.ToUniversalTime().Year);
        Assert.Equal(5, value.DateTime.ToUniversalTime().Month);
        Assert.Equal(27, value.DateTime.ToUniversalTime().Day);
        Assert.Equal(7, value.DateTime.ToUniversalTime().Hour);
        Assert.Equal(32, value.DateTime.ToUniversalTime().Minute);
    }

    [Theory]
    [InlineData("0x7FFFFFFFFFFFFFFF", long.MaxValue)]
    [InlineData("0o777777777777777777777", long.MaxValue)]
    [InlineData("0b111111111111111111111111111111111111111111111111111111111111111", long.MaxValue)]
    [InlineData("0xDEAD_BEEF", 0xDEADBEEF)]
    public void Deserialize_NonDecimalInteger_MaxValue(string literal, long expected)
    {
        var table = TomlSerializer.Deserialize<Model.TomlTable>("a = " + literal + "\n")!;

        Assert.Equal(expected, table["a"]);
    }

    [Theory]
    [InlineData("0x8000000000000000")]
    [InlineData("0xFFFFFFFFFFFFFFFF")]
    [InlineData("0o1000000000000000000000")]
    [InlineData("0b1000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("0b1111111111111111111111111111111111111111111111111111111111111111")]
    public void Deserialize_NonDecimalInteger_GreaterThanInt64_Throws(string literal)
    {
        var toml = "a = " + literal + "\n";

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Model.TomlTable>(toml));
        Assert.Contains("greater than the maximum 64-bit signed integer", ex.Message, StringComparison.Ordinal);
        Assert.True(Parsing.SyntaxParser.Parse(toml).HasErrors);
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<ulong>(toml, new TomlSerializerOptions { RootValueHandling = TomlRootValueHandling.WrapInRootKey, RootValueKeyName = "a" }));
    }

    [Theory]
    [InlineData("1979-05-27T00:32:00.123456789Z", TomlDateTimeKind.OffsetDateTimeByZ, "00:32:00.1234567+00:00")]
    [InlineData("1979-05-27T00:32:00.999999999-07:00", TomlDateTimeKind.OffsetDateTimeByNumber, "00:32:00.9999999-07:00")]
    [InlineData("1979-05-27 00:32:00.12345678", TomlDateTimeKind.LocalDateTime, "00:32:00.1234567+00:00")]
    [InlineData("00:32:00.99999999999999999999", TomlDateTimeKind.LocalTime, "00:32:00.9999999+00:00")]
    public void Deserialize_TomlDateTime_TruncatesExtraFractionalDigits(string literal, TomlDateTimeKind kind, string expected)
    {
        var table = TomlSerializer.Deserialize<Model.TomlTable>("a = " + literal + "\n")!;

        var value = (TomlDateTime)table["a"];
        Assert.Equal(kind, value.Kind);
        Assert.Equal(7, value.SecondPrecision);
        Assert.Equal(expected, value.DateTime.ToString("HH:mm:ss.fffffffzzz", System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(Parsing.SyntaxParser.Parse("a = " + literal + "\n").HasErrors);
    }

    [Fact]
    public void Deserialize_DateTimeOffset_TruncatesNanoseconds()
    {
        var options = new TomlSerializerOptions { RootValueHandling = TomlRootValueHandling.WrapInRootKey };

        var value = TomlSerializer.Deserialize<DateTimeOffset>("value = 2024-01-01T00:00:00.123456789Z\n", options);

        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1234567), value);
    }

    [Theory]
    [InlineData("a = \"\"\"x\\ \ny\"\"\"\n", "xy")]
    [InlineData("a = \"\"\"x\\\t\r\n  \n y\"\"\"\n", "xy")]
    [InlineData("a = \"\"\"x\\\n\"\"\"\n", "x")]
    [InlineData("a = \"\"\"\\\n  x \\\n\n  y\"\"\"\n", "x y")]
    public void Deserialize_LineEndingBackslash_TrimsWhitespace(string toml, string expected)
    {
        Assert.False(Parsing.SyntaxParser.Parse(toml).HasErrors);
        Assert.Equal(expected, TomlSerializer.Deserialize<Model.TomlTable>(toml)!["a"]);
    }

    [Theory]
    [InlineData("a = \"abc\\\ndef\"\n")]
    [InlineData("a = \"x\\\ty\"\n")]
    [InlineData("a = \"x\\ y\"\n")]
    [InlineData("a = \"\"\"x\\ y\"\"\"\n")]
    [InlineData("a = \"\"\"abc\\\rdef\"\"\"\n")]
    [InlineData("a = \"\"\"abc\\\n\rdef\"\"\"\n")]
    public void Deserialize_InvalidLineEndingBackslash_Throws(string toml)
    {
        Assert.True(Parsing.SyntaxParser.Parse(toml).HasErrors);
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Model.TomlTable>(toml));
    }
}
