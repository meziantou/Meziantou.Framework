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

    /// <summary>
    /// Gets or sets a value indicating whether derived type registrations are inferred from the compiler-provided
    /// metadata of a <c>closed</c> type hierarchy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Setting this property overrides <see cref="YamlPolymorphismOptions.InferClosedTypePolymorphism"/> for the
    /// annotated type, so an explicit <see langword="false"/> suppresses inference even when it is enabled on the
    /// serializer options. When the property is left unset the value from the options is used.
    /// </para>
    /// <para>
    /// Inference is skipped for a type that declares explicit <see cref="YamlDerivedTypeAttribute"/> registrations.
    /// Setting this property to <see langword="true"/> on a type that is not declared <c>closed</c> throws an
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// </remarks>
    public bool InferClosedTypePolymorphism
    {
        get => _inferClosedTypePolymorphism ?? false;
        set => _inferClosedTypePolymorphism = value;
    }

    /// <summary>
    /// Gets the explicitly configured <see cref="InferClosedTypePolymorphism"/> value,
    /// or <see langword="null"/> when the property has not been set.
    /// </summary>
    internal bool? InferClosedTypePolymorphismOrNull => _inferClosedTypePolymorphism;

    private bool? _inferClosedTypePolymorphism;
}
