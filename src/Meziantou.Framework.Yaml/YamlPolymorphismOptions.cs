using System.Collections.ObjectModel;

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
    private IDictionary<Type, IList<YamlDerivedType>> _derivedTypeMappings = new Dictionary<Type, IList<YamlDerivedType>>();

    /// <summary>Gets an empty, read-only instance holding the default settings.</summary>
    internal static YamlPolymorphismOptions ReadOnlyDefault { get; } = CreateReadOnlyDefault();

    /// <summary>Gets runtime-registered derived type mappings, keyed by base type.</summary>
    /// <remarks>
    /// <para>
    /// Entries registered here are merged with the registrations declared by
    /// <see cref="Serialization.YamlDerivedTypeAttribute"/>, which take precedence when the same discriminator, tag,
    /// or type is registered in both. Registering the same derived type, discriminator, or tag more than once for a
    /// base type within the mappings is an error.
    /// </para>
    /// <para>
    /// This enables cross-project polymorphism where the base type and derived types
    /// live in different assemblies (e.g., clean architecture, plugin systems).
    /// </para>
    /// <para>
    /// The mappings can only be changed until the instance is assigned to
    /// <see cref="YamlSerializerOptions.PolymorphismOptions"/>. The mappings are copied at that point, and the
    /// instance becomes read-only: a later change throws <see cref="NotSupportedException"/>, so it cannot affect
    /// options that are already in use.
    /// </para>
    /// </remarks>
    public IDictionary<Type, IList<YamlDerivedType>> DerivedTypeMappings => _derivedTypeMappings;

    /// <summary>Makes the instance read-only, copying the mappings so that the lists they were built from cannot change them either.</summary>
    /// <param name="paramName">The name of the parameter reported when a mapping is invalid.</param>
    /// <exception cref="ArgumentException">A mapping has a <see langword="null"/> list or a <see langword="null"/> entry.</exception>
    internal void MakeReadOnly(string paramName)
    {
        if (_derivedTypeMappings is ReadOnlyDictionary<Type, IList<YamlDerivedType>>)
        {
            return;
        }

        var mappings = new Dictionary<Type, IList<YamlDerivedType>>(_derivedTypeMappings.Count);
        foreach (var mapping in _derivedTypeMappings)
        {
            if (mapping.Value is null)
            {
                throw new ArgumentException($"The derived type mappings of '{mapping.Key}' cannot be null.", paramName);
            }

            var derivedTypes = new YamlDerivedType[mapping.Value.Count];
            for (var i = 0; i < derivedTypes.Length; i++)
            {
                derivedTypes[i] = mapping.Value[i] ?? throw new ArgumentException($"The derived type mappings of '{mapping.Key}' cannot contain null entries.", paramName);
            }

            mappings.Add(mapping.Key, Array.AsReadOnly(derivedTypes));
        }

        _derivedTypeMappings = new ReadOnlyDictionary<Type, IList<YamlDerivedType>>(mappings);
    }

    private static YamlPolymorphismOptions CreateReadOnlyDefault()
    {
        var options = new YamlPolymorphismOptions();
        options.MakeReadOnly(nameof(DerivedTypeMappings));
        return options;
    }
}
