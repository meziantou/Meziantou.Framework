namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Registers a derived type mapping on a source-generated context, enabling cross-project polymorphism.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class YamlDerivedTypeMappingAttribute : YamlAttribute
{
    /// <summary>Initializes a new instance with no discriminator, marking this derived type as the default when no discriminator matches.</summary>
    /// <param name="baseType">The polymorphic base type.</param>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="baseType"/> or <paramref name="derivedType"/> is <see langword="null"/>.</exception>
    public YamlDerivedTypeMappingAttribute(Type baseType, Type derivedType)
    {
        ArgumentNullException.ThrowIfNull(baseType);
        ArgumentNullException.ThrowIfNull(derivedType);

        BaseType = baseType;
        DerivedType = derivedType;
    }

    /// <summary>Initializes a new instance with a string discriminator.</summary>
    /// <param name="baseType">The polymorphic base type.</param>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <param name="discriminator">The discriminator value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="baseType"/>, <paramref name="derivedType"/>, or <paramref name="discriminator"/> is <see langword="null"/>.</exception>
    public YamlDerivedTypeMappingAttribute(Type baseType, Type derivedType, string discriminator)
    {
        ArgumentNullException.ThrowIfNull(baseType);
        ArgumentNullException.ThrowIfNull(derivedType);
        ArgumentNullException.ThrowIfNull(discriminator);

        BaseType = baseType;
        DerivedType = derivedType;
        Discriminator = discriminator;
    }

    /// <summary>Initializes a new instance with an integer discriminator.</summary>
    /// <param name="baseType">The polymorphic base type.</param>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <param name="discriminator">The integer discriminator value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="baseType"/> or <paramref name="derivedType"/> is <see langword="null"/>.</exception>
    public YamlDerivedTypeMappingAttribute(Type baseType, Type derivedType, int discriminator)
    {
        ArgumentNullException.ThrowIfNull(baseType);
        ArgumentNullException.ThrowIfNull(derivedType);

        BaseType = baseType;
        DerivedType = derivedType;
        Discriminator = discriminator.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Gets the polymorphic base type.</summary>
    public Type BaseType { get; }

    /// <summary>Gets the derived CLR type.</summary>
    public Type DerivedType { get; }

    /// <summary>
    /// Gets the discriminator value, or <see langword="null"/> if this is the default derived type.
    /// </summary>
    public string? Discriminator { get; }

    /// <summary>Gets or sets an optional explicit YAML tag for the derived type.</summary>
    public string? Tag { get; set; }
}
