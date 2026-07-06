namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Registers a derived type for polymorphic YAML serialization.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
public sealed class YamlDerivedTypeAttribute : YamlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlDerivedTypeAttribute"/> class
    /// with no discriminator. When <see cref="Tag"/> is also <see langword="null"/>, this derived type
    /// becomes the default when no discriminator or tag matches. When <see cref="Tag"/> is set, this entry
    /// participates only in tag-based dispatch and does not become the default.
    /// </summary>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="derivedType"/> is <see langword="null"/>.</exception>
    public YamlDerivedTypeAttribute(Type derivedType)
    {
        ArgumentNullException.ThrowIfNull(derivedType);

        DerivedType = derivedType;
        Discriminator = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlDerivedTypeAttribute"/> class.
    /// </summary>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <param name="discriminator">The discriminator value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="derivedType"/> or <paramref name="discriminator"/> is <see langword="null"/>.</exception>
    public YamlDerivedTypeAttribute(Type derivedType, string discriminator)
    {
        ArgumentNullException.ThrowIfNull(derivedType);
        ArgumentNullException.ThrowIfNull(discriminator);

        DerivedType = derivedType;
        Discriminator = discriminator;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlDerivedTypeAttribute"/> class with an integer discriminator.
    /// </summary>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <param name="discriminator">The integer discriminator value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="derivedType"/> is <see langword="null"/>.</exception>
    public YamlDerivedTypeAttribute(Type derivedType, int discriminator)
    {
        ArgumentNullException.ThrowIfNull(derivedType);

        DerivedType = derivedType;
        Discriminator = discriminator.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Gets the derived CLR type.</summary>
    public Type DerivedType { get; }

    /// <summary>
    /// Gets the discriminator value, or <see langword="null"/> if this is the default derived type.
    /// </summary>
    public string? Discriminator { get; }

    /// <summary>Gets or sets an optional explicit YAML tag for the derived type.</summary>
    public string? Tag { get; set; }
}

