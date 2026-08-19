namespace Meziantou.Framework.Yaml;

/// <summary>Configures polymorphic serialization behavior.</summary>
public sealed class YamlPolymorphismOptions
{
    /// <summary>Gets or sets how type discriminators are represented.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is <see cref="YamlTypeDiscriminatorStyle.Unspecified"/>.</exception>
    public YamlTypeDiscriminatorStyle DiscriminatorStyle
    {
        get => _discriminatorStyle;
        init
        {
            if (value == YamlTypeDiscriminatorStyle.Unspecified)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "DiscriminatorStyle cannot be Unspecified on options.");
            }

            _discriminatorStyle = value;
        }
    }

    /// <summary>Gets or sets the property name used for discriminator-based polymorphism.</summary>
    /// <exception cref="ArgumentException">Value is <see langword="null"/> or empty.</exception>
    public string TypeDiscriminatorPropertyName
    {
        get => _typeDiscriminatorPropertyName;
        init
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("TypeDiscriminatorPropertyName cannot be null or empty.", nameof(value));
            }

            _typeDiscriminatorPropertyName = value;
        }
    }

    /// <summary>Gets or sets behavior when an unknown derived type discriminator is encountered.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is <see cref="YamlUnknownDerivedTypeHandling.Unspecified"/>.</exception>
    public YamlUnknownDerivedTypeHandling UnknownDerivedTypeHandling
    {
        get => _unknownDerivedTypeHandling;
        init
        {
            if (value == YamlUnknownDerivedTypeHandling.Unspecified)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "UnknownDerivedTypeHandling cannot be Unspecified on options.");
            }

            _unknownDerivedTypeHandling = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether derived type registrations are inferred from the compiler-provided
    /// metadata of a <c>closed</c> type hierarchy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each inferred derived type uses its name, without the generic arity suffix, as its discriminator.
    /// Inference is skipped for a type that declares explicit registrations, either with
    /// <see cref="Serialization.YamlDerivedTypeAttribute"/> or in <see cref="DerivedTypeMappings"/>.
    /// </para>
    /// <para>
    /// <see cref="Serialization.YamlPolymorphicAttribute.InferClosedTypePolymorphism"/> overrides this value
    /// for the type it is applied to.
    /// </para>
    /// </remarks>
    public bool InferClosedTypePolymorphism { get; init; }

    private YamlTypeDiscriminatorStyle _discriminatorStyle = YamlTypeDiscriminatorStyle.Property;
    private string _typeDiscriminatorPropertyName = "$type";
    private YamlUnknownDerivedTypeHandling _unknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.Fail;

    /// <summary>Gets runtime-registered derived type mappings, keyed by base type.</summary>
    /// <remarks>
    /// <para>
    /// Entries registered here are merged with attribute-based registrations
    /// (<see cref="Serialization.YamlDerivedTypeAttribute"/> and
    /// <see cref="System.Text.Json.Serialization.JsonDerivedTypeAttribute"/>).
    /// Attribute-based entries take precedence when the same discriminator or type is registered in both.
    /// </para>
    /// <para>
    /// This enables cross-project polymorphism where the base type and derived types
    /// live in different assemblies (e.g., clean architecture, plugin systems).
    /// </para>
    /// </remarks>
    public IDictionary<Type, IList<YamlDerivedType>> DerivedTypeMappings { get; } = new Dictionary<Type, IList<YamlDerivedType>>();
}
