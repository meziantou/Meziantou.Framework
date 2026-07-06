namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Marks a base type as polymorphic for YAML serialization.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public sealed class YamlPolymorphicAttribute : YamlAttribute
{
    /// <summary>Gets or sets the discriminator property name.</summary>
    public string? TypeDiscriminatorPropertyName { get; set; }

    /// <summary>Gets or sets the discriminator style.</summary>
    public YamlTypeDiscriminatorStyle DiscriminatorStyle { get; set; } = YamlTypeDiscriminatorStyle.Unspecified;

    /// <summary>Gets or sets behavior when an unknown derived type discriminator is encountered.</summary>
    /// <remarks>
    /// When set to a value other than <see cref="YamlUnknownDerivedTypeHandling.Unspecified"/>,
    /// this overrides the value from <see cref="YamlPolymorphismOptions.UnknownDerivedTypeHandling"/>
    /// and any value from <see cref="System.Text.Json.Serialization.JsonPolymorphicAttribute"/>.
    /// </remarks>
    public YamlUnknownDerivedTypeHandling UnknownDerivedTypeHandling { get; set; } = YamlUnknownDerivedTypeHandling.Unspecified;
}
