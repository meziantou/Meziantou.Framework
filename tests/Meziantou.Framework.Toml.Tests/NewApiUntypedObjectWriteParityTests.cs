using System;

namespace Tomlyn.Tests;

public sealed class NewApiUntypedObjectWriteParityTests
{
    private sealed class ObjectPayload
    {
        public object? Value { get; set; }
    }

    [Fact]
    public void Serialize_ObjectProperty_Decimal_ShouldWrite()
    {
        var payload = new ObjectPayload { Value = 1.25m };

        var toml = TomlSerializer.Serialize(payload);

        Assert.Contains("Value", toml);
        Assert.Contains("1.25", toml);
    }

    [Fact]
    public void Serialize_ObjectProperty_Guid_ShouldWrite()
    {
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var payload = new ObjectPayload { Value = guid };

        var toml = TomlSerializer.Serialize(payload);

        Assert.Contains("Value", toml);
        Assert.Contains(guid.ToString(), toml);
    }

    [Fact]
    public void Serialize_ObjectInstance_ShouldThrow()
    {
        var payload = new ObjectPayload { Value = new object() };
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(payload));
    }
}

