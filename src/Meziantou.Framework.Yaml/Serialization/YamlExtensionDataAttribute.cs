namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Indicates that a member should receive any unmapped YAML mapping keys encountered during deserialization.</summary>
/// <remarks>
/// This behaves similarly to <c>System.Text.Json</c> extension data. The extension data member is not emitted as a
/// regular mapping key; instead its contents are merged into the surrounding mapping during serialization.
/// <para>
/// The member must be a <see cref="Model.YamlMapping"/>, or a <see cref="Dictionary{TKey, TValue}"/>,
/// <see cref="IDictionary{TKey, TValue}"/>, or <see cref="IReadOnlyDictionary{TKey, TValue}"/> whose keys are
/// <see cref="string"/> and whose values are <see cref="object"/> or <see cref="Model.YamlNode"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlExtensionDataAttribute : YamlAttribute
{
}

