#pragma warning disable MA0048 // File name must match type name
using System.Text.Json.Serialization;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.AotTests;

public sealed class YamlSerializerAotTests
{
    [Fact]
    public void RoundTrip()
    {
        var typeInfo = SmokeYamlContext.Default.SmokeConfig;
        var yaml = YamlSerializer.Serialize(new SmokeConfig { Name = "aot", Enabled = true }, typeInfo);

        var model = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(model);
        Assert.Equal("aot", model.Name);
        Assert.True(model.Enabled);
    }

    // Non-public members and constructors are reached through [UnsafeAccessor] stubs, which NativeAOT supports.
    [Fact]
    public void NonPublicMembersAndConstructor()
    {
        var typeInfo = SmokeYamlContext.Default.SmokeRestrictedConfig;
        var restricted = YamlSerializer.Deserialize("name: aot\nretries: 3\n", typeInfo);
        Assert.NotNull(restricted);
        Assert.Equal("aot", restricted.GetName());
        Assert.Equal(3, restricted.Retries);

        var yaml = YamlSerializer.Serialize(restricted, typeInfo);
        Assert.Contains("name: aot", yaml, StringComparison.Ordinal);
        Assert.Contains("retries: 3", yaml, StringComparison.Ordinal);
    }

    // Members of a generic type are reached through a generic accessor class mirroring the type constraints.
    [Fact]
    public void NonPublicMembersOfGenericType()
    {
        var typeInfo = SmokeYamlContext.Default.SmokeGenericConfigSmokeConfig;
        var generic = YamlSerializer.Deserialize("name: generic\n", typeInfo);
        Assert.NotNull(generic);
        Assert.Equal("generic", generic.GetName());

        var yaml = YamlSerializer.Serialize(generic, typeInfo);
        Assert.Contains("name: generic", yaml, StringComparison.Ordinal);
    }
}

internal sealed class SmokeConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

internal sealed class SmokeRestrictedConfig
{
#pragma warning disable IDE0051 // Remove unused private member
    [YamlConstructor]
    private SmokeRestrictedConfig(int retries) => Retries = retries;
#pragma warning restore IDE0051

#pragma warning disable IDE0044 // Add readonly modifier
    [YamlInclude]
    [YamlPropertyName("name")]
    private string _name = string.Empty;
#pragma warning restore IDE0044

    [YamlPropertyName("retries")]
    public int Retries { get; }

    public string GetName() => _name;
}

internal sealed class SmokeGenericConfig<T>
    where T : notnull, new()
{
#pragma warning disable IDE0044 // Add readonly modifier
    [YamlInclude]
    [YamlPropertyName("name")]
    private string _name = string.Empty;
#pragma warning restore IDE0044

    public string GetName() => _name;
}

[YamlSerializable(typeof(SmokeConfig))]
[YamlSerializable(typeof(SmokeRestrictedConfig))]
[YamlSerializable(typeof(SmokeGenericConfig<SmokeConfig>))]
internal sealed partial class SmokeYamlContext : YamlSerializerContext
{
}
