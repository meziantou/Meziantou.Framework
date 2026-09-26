using System;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiOptionsValidationTests
{
    [Fact]
    public void RootValueKeyName_Empty_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
        {
            _ = TomlSerializerOptions.Default with
            {
                RootValueKeyName = "",
            };
        });

        Assert.Equal("value", ex!.ParamName);
    }

    [Fact]
    public void RootValueKeyName_InvalidSurrogate_Throws()
    {
        var invalid = "\uD800";
        var ex = Assert.Throws<ArgumentException>(() =>
        {
            _ = TomlSerializerOptions.Default with
            {
                RootValueKeyName = invalid,
            };
        });

        Assert.Equal("value", ex!.ParamName);
    }

    [Fact]
    public void RootValueWrapping_DoesNotExpandDottedRootKey()
    {
        var options = TomlSerializerOptions.Default with
        {
            RootValueHandling = TomlRootValueHandling.WrapInRootKey,
            RootValueKeyName = "a.b",
            DottedKeyHandling = TomlDottedKeyHandling.Expand,
        };

        var toml = TomlSerializer.Serialize(1, options);

        Assert.Contains("\"a.b\"", toml);
        Assert.DoesNotContain("[a]", toml);

        var value = TomlSerializer.Deserialize<int>(toml, options);
        Assert.Equal(1, value);
    }

    [Fact]
    public void PreferredObjectCreationHandling_InvalidValue_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = TomlSerializerOptions.Default with
            {
                PreferredObjectCreationHandling = (JsonObjectCreationHandling)99,
            };
        });

        Assert.Equal("value", ex!.ParamName);
    }
}
