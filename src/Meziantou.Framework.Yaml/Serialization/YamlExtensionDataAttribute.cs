namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Indicates that a member should receive any unmapped YAML mapping keys encountered during deserialization.</summary>
/// <remarks>
/// This behaves similarly to <c>System.Text.Json</c> extension data. The extension data member is not emitted as a
/// regular mapping key; instead its contents are merged into the surrounding mapping during serialization.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlExtensionDataAttribute : YamlAttribute
{
}

