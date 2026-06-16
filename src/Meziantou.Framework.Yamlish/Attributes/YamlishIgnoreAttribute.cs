namespace Meziantou.Framework.Yamlish;

/// <summary>Controls whether a field or property is ignored during Yamlish serialization.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlishIgnoreAttribute : YamlishAttribute
{
    /// <summary>Gets or sets the condition that determines when the member is ignored.</summary>
    public YamlishIgnoreCondition Condition { get; set; } = YamlishIgnoreCondition.Always;
}
