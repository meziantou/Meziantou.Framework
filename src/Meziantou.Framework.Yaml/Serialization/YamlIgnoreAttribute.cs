namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Instructs the <see cref="YamlSerializer"/> when to ignore the public field or public read/write property value.</summary>
/// <remarks>
/// When applied to a type, it sets the default ignore condition for all its properties and fields.
/// When applied to a member, it overrides the type-level condition and <see cref="YamlSerializerOptions.DefaultIgnoreCondition"/> for that member.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
    AttributeTargets.Field | AttributeTargets.Property,
    AllowMultiple = false)]
public sealed class YamlIgnoreAttribute : YamlAttribute
{
    /// <summary>
    /// Gets or sets the condition that must be met before the member is ignored.
    /// </summary>
    /// <remarks>The default value is <see cref="YamlIgnoreCondition.Always"/>.</remarks>
    public YamlIgnoreCondition Condition { get; set; } = YamlIgnoreCondition.Always;
}
