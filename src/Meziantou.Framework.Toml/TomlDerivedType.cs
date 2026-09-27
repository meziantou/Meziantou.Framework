using System;
using System.Globalization;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Represents a derived type mapping for runtime polymorphic TOML configuration.
/// </summary>
public sealed class TomlDerivedType
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlDerivedType"/> class with no discriminator.
    /// </summary>
    /// <param name="derivedType">The derived CLR type.</param>
    public TomlDerivedType(Type derivedType)
    {
        ArgumentNullException.ThrowIfNull(derivedType);

        DerivedType = derivedType;
        Discriminator = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlDerivedType"/> class with a string discriminator.
    /// </summary>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <param name="discriminator">The discriminator value.</param>
    public TomlDerivedType(Type derivedType, string discriminator)
    {
        ArgumentNullException.ThrowIfNull(derivedType);
        ArgumentNullException.ThrowIfNull(discriminator);

        DerivedType = derivedType;
        Discriminator = discriminator;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlDerivedType"/> class with an integer discriminator.
    /// </summary>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <param name="discriminator">The integer discriminator value.</param>
    public TomlDerivedType(Type derivedType, int discriminator)
    {
        ArgumentNullException.ThrowIfNull(derivedType);

        DerivedType = derivedType;
        Discriminator = discriminator.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets the derived CLR type.
    /// </summary>
    public Type DerivedType { get; }

    /// <summary>
    /// Gets the discriminator value, or <see langword="null"/> when this is the default derived type.
    /// </summary>
    public string? Discriminator { get; }
}
