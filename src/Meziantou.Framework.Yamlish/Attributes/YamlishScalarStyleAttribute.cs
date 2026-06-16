namespace Meziantou.Framework.Yamlish;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlishScalarStyleAttribute : YamlishAttribute
{
    public YamlishScalarStyleAttribute(YamlishScalarStyle style)
    {
        Style = style;
    }

    public YamlishScalarStyle Style { get; }

    public YamlishScalarChomping Chomping { get; set; } = YamlishScalarChomping.Clip;
}
