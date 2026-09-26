using System;

namespace Tomlyn.Tests;

public sealed class NewApiWellKnownScalarTypeParityTests
{
    private sealed class HalfPayload
    {
        public Half Value { get; set; }
    }

    [Fact]
    public void Deserialize_Half_ShouldRoundTrip()
    {
        var toml = """
            Value = 1.5
            """;

        var payload = TomlSerializer.Deserialize<HalfPayload>(toml);

        Assert.NotNull(payload);
        Assert.Equal((Half)1.5, payload!.Value);

        var roundtripToml = TomlSerializer.Serialize(payload);
        var roundtrip = TomlSerializer.Deserialize<HalfPayload>(roundtripToml);

        Assert.NotNull(roundtrip);
        Assert.Equal(payload.Value, roundtrip!.Value);
    }

    [Fact]
    public void Deserialize_Half_OutOfRange_ShouldThrowWithLocation()
    {
        var toml = """
            Value = 1e100
            """;

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<HalfPayload>(toml));
        Assert.NotNull(ex);
        Assert.Equal(1, ex!.Line);
        Assert.True(ex.Column > 0);
    }

    private sealed class Int128Payload
    {
        public Int128 Value { get; set; }
    }

    private sealed class UInt128Payload
    {
        public UInt128 Value { get; set; }
    }

    [Fact]
    public void Deserialize_Int128_ShouldRoundTripWithinTomlRange()
    {
        var toml = """
            Value = 123
            """;

        var payload = TomlSerializer.Deserialize<Int128Payload>(toml);

        Assert.NotNull(payload);
        Assert.Equal((Int128)123, payload!.Value);

        var roundtripToml = TomlSerializer.Serialize(payload);
        var roundtrip = TomlSerializer.Deserialize<Int128Payload>(roundtripToml);

        Assert.NotNull(roundtrip);
        Assert.Equal(payload.Value, roundtrip!.Value);
    }

    [Fact]
    public void Serialize_Int128_OutOfTomlRange_ShouldThrow()
    {
        var payload = new Int128Payload { Value = (Int128)long.MaxValue + 1 };
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(payload));
    }

    [Fact]
    public void Deserialize_UInt128_ShouldRoundTripWithinTomlRange()
    {
        var toml = """
            Value = 42
            """;

        var payload = TomlSerializer.Deserialize<UInt128Payload>(toml);

        Assert.NotNull(payload);
        Assert.Equal((UInt128)42, payload!.Value);

        var roundtripToml = TomlSerializer.Serialize(payload);
        var roundtrip = TomlSerializer.Deserialize<UInt128Payload>(roundtripToml);

        Assert.NotNull(roundtrip);
        Assert.Equal(payload.Value, roundtrip!.Value);
    }

    [Fact]
    public void Serialize_UInt128_OutOfTomlRange_ShouldThrow()
    {
        var payload = new UInt128Payload { Value = (UInt128)long.MaxValue + 1 };
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(payload));
    }
}
