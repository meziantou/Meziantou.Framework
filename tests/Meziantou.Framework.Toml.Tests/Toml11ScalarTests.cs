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
}
