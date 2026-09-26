using System;

namespace Tomlyn.Tests;

public sealed class NewApiNullableValueTypeTests
{
    private sealed class NullablePayload
    {
        public int? Count { get; set; }

        public DateTimeOffset? When { get; set; }
    }

    [Fact]
    public void Deserialize_NullableValueTypes_WhenMissing_ShouldRemainNull()
    {
        var payload = TomlSerializer.Deserialize<NullablePayload>(string.Empty);

        Assert.NotNull(payload);
        Assert.Null(payload!.Count);
        Assert.Null(payload.When);
    }

    [Fact]
    public void Deserialize_NullableValueTypes_WhenPresent_ShouldBind()
    {
        var toml = """
            Count = 1
            When = 1979-05-27T07:32:00Z
            """;

        var payload = TomlSerializer.Deserialize<NullablePayload>(toml);

        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Count);
        Assert.NotNull(payload.When);
        Assert.Equal(new DateTime(1979, 5, 27, 7, 32, 0, DateTimeKind.Utc), payload.When!.Value.UtcDateTime);
    }

    [Fact]
    public void Serialize_NullableValueTypes_WhenNull_ShouldBeOmittedByDefault()
    {
        var payload = new NullablePayload
        {
            Count = null,
            When = null,
        };

        var toml = TomlSerializer.Serialize(payload);

        Assert.DoesNotContain("Count", toml);
        Assert.DoesNotContain("When", toml);
    }
}

