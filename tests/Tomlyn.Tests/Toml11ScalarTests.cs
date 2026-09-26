using System;

namespace Tomlyn.Tests;

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
}
