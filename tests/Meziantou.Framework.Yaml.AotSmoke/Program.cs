#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using System.Text.Json.Serialization;
using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Serialization;

var context = SmokeYamlContext.Default;
var typeInfo = context.SmokeConfig;

var yaml = YamlSerializer.Serialize(
    new SmokeConfig
    {
        Name = "aot",
        Enabled = true,
    },
    typeInfo);

var model = YamlSerializer.Deserialize(yaml, typeInfo);
if (model is null || model.Name != "aot" || !model.Enabled)
{
    return 1;
}

// Non-public members and constructors are reached through [UnsafeAccessor] stubs, which NativeAOT supports.
var restricted = YamlSerializer.Deserialize("name: aot\nretries: 3\n", context.SmokeRestrictedConfig);
if (restricted is null || restricted.GetName() != "aot" || restricted.Retries != 3)
{
    return 1;
}

Console.WriteLine(yaml);
Console.WriteLine(YamlSerializer.Serialize(restricted, context.SmokeRestrictedConfig));
return 0;

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

[YamlSerializable(typeof(SmokeConfig))]
[YamlSerializable(typeof(SmokeRestrictedConfig))]
internal sealed partial class SmokeYamlContext : YamlSerializerContext
{
}
