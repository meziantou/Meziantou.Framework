namespace Meziantou.Framework.Yamlish;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlishIgnoreAttribute : Attribute
{
    public YamlishIgnoreCondition Condition { get; set; } = YamlishIgnoreCondition.Always;
}
