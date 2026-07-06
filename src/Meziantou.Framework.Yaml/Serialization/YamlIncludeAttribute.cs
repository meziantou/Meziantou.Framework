namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Includes a non-public member in YAML serialization.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlIncludeAttribute : YamlAttribute
{
}

