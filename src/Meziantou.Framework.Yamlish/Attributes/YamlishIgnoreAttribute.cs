namespace Meziantou.Framework.Yamlish;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlishIgnoreAttribute : YamlishAttribute
{
    public YamlishIgnoreCondition Condition { get; set; } = YamlishIgnoreCondition.Always;
}
