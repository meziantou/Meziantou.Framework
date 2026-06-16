namespace Meziantou.Framework.Yamlish;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlishSequenceStyleAttribute : YamlishAttribute
{
    public YamlishSequenceStyleAttribute(YamlishSequenceStyle style)
    {
        Style = style;
    }

    public YamlishSequenceStyle Style { get; }
}
