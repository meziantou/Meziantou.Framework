using System.Collections.Generic;
using System.Collections.Immutable;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiCollectionTypeParityTests
{
    private static TomlSerializerOptions CreateRootArrayOptions()
        => new TomlSerializerOptions
        {
            RootValueHandling = TomlRootValueHandling.WrapInRootKey,
            RootValueKeyName = "value",
        };

    [Fact]
    public void Deserialize_ISet_ShouldReturnHashSet()
    {
        var options = CreateRootArrayOptions();
        var toml = """
            value = ["a", "b", "a"]
            """;

        var result = TomlSerializer.Deserialize<ISet<string>>(toml, options);

        Assert.NotNull(result);
        Assert.IsAssignableTo<HashSet<string>>(result);
        Assert.HasCount(2, result);
        Assert.True(result.Contains("a"));
        Assert.True(result.Contains("b"));
    }

    [Fact]
    public void Deserialize_HashSet_ShouldRoundTripValues()
    {
        var options = CreateRootArrayOptions();
        var toml = """
            value = [1, 2, 1]
            """;

        var result = TomlSerializer.Deserialize<HashSet<int>>(toml, options);

        Assert.NotNull(result);
        Assert.HasCount(2, result);
        Assert.True(result.SetEquals(new[] { 1, 2 }));
    }

    [Fact]
    public void Deserialize_ImmutableArray_ShouldRoundTripValues()
    {
        var options = CreateRootArrayOptions();
        var toml = """
            value = [10, 20]
            """;

        var result = TomlSerializer.Deserialize<ImmutableArray<int>>(toml, options);

        Assert.False(result.IsDefault);
        Assert.HasCount(2, result);
        Assert.Equal(10, result[0]);
        Assert.Equal(20, result[1]);
    }

    [Fact]
    public void Deserialize_ImmutableList_ShouldRoundTripValues()
    {
        var options = CreateRootArrayOptions();
        var toml = """
            value = ["a", "b"]
            """;

        var result = TomlSerializer.Deserialize<ImmutableList<string>>(toml, options);

        Assert.NotNull(result);
        Assert.HasCount(2, result);
        Assert.Equal("a", result[0]);
        Assert.Equal("b", result[1]);
    }

    [Fact]
    public void Deserialize_ImmutableHashSet_ShouldDeduplicateValues()
    {
        var options = CreateRootArrayOptions();
        var toml = """
            value = [1, 2, 1]
            """;

        var result = TomlSerializer.Deserialize<ImmutableHashSet<int>>(toml, options);

        Assert.NotNull(result);
        Assert.HasCount(2, result);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
    }

    [Fact]
    public void Serialize_ImmutableArray_Default_WritesEmptyArray()
    {
        var options = CreateRootArrayOptions();
        var toml = TomlSerializer.Serialize(default(ImmutableArray<int>), options);

        Assert.Contains("value", toml);
        Assert.Contains("[]", toml);
    }
}

