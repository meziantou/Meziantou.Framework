namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Specifies that a member is required during deserialization.</summary>
/// <remarks>
/// When a required member is missing from the YAML mapping, deserialization throws a <see cref="YamlException"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlRequiredAttribute : YamlAttribute
{
}

