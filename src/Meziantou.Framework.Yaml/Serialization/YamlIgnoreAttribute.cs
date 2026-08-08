namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Instructs the <see cref="YamlSerializer"/> when to ignore the public field or public read/write property value.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class YamlIgnoreAttribute : YamlAttribute
{
    /// <summary>
    /// Gets or sets the condition that must be met before the member is ignored.
    /// </summary>
    /// <remarks>The default value is <see cref="YamlIgnoreCondition.Always"/>.</remarks>
    public YamlIgnoreCondition Condition { get; set; } = YamlIgnoreCondition.Always;
}