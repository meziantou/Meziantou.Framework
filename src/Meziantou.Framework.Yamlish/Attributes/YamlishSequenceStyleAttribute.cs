namespace Meziantou.Framework.Yamlish;

/// <summary>Specifies the sequence style used when serializing a field or property.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlishSequenceStyleAttribute : YamlishAttribute
{
    /// <summary>Initializes a new instance of the <see cref="YamlishSequenceStyleAttribute" /> class.</summary>
    /// <param name="style">The sequence style.</param>
    public YamlishSequenceStyleAttribute(YamlishSequenceStyle style)
    {
        Style = style;
    }

    /// <summary>Gets the sequence style.</summary>
    public YamlishSequenceStyle Style { get; }
}
