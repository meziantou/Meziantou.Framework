namespace Meziantou.Framework.Yamlish;

/// <summary>Specifies the scalar style used when serializing a field or property.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlishScalarStyleAttribute : YamlishAttribute
{
    /// <summary>Initializes a new instance of the <see cref="YamlishScalarStyleAttribute" /> class.</summary>
    /// <param name="style">The scalar style.</param>
    public YamlishScalarStyleAttribute(YamlishScalarStyle style)
    {
        Style = style;
    }

    /// <summary>Gets the scalar style.</summary>
    public YamlishScalarStyle Style { get; }

    /// <summary>Gets or sets the chomping behavior used for block scalar values.</summary>
    public YamlishScalarChomping Chomping { get; set; } = YamlishScalarChomping.Clip;
}
