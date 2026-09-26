using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Registers a derived type for polymorphic TOML serialization.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class TomlDerivedTypeAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlDerivedTypeAttribute"/> class with a string discriminator.
    /// </summary>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <param name="discriminator">The discriminator value.</param>
    public TomlDerivedTypeAttribute(Type derivedType, string discriminator)
    {
        ArgumentGuard.ThrowIfNull(derivedType, nameof(derivedType));
        ArgumentGuard.ThrowIfNull(discriminator, nameof(discriminator));

        DerivedType = derivedType;
        Discriminator = discriminator;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlDerivedTypeAttribute"/> class with an integer discriminator.
    /// </summary>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <param name="discriminator">The integer discriminator value (converted to string via invariant culture).</param>
    public TomlDerivedTypeAttribute(Type derivedType, int discriminator)
    {
        ArgumentGuard.ThrowIfNull(derivedType, nameof(derivedType));

        DerivedType = derivedType;
        Discriminator = discriminator.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlDerivedTypeAttribute"/> class as the default derived type (no discriminator).
    /// When a type is registered without a discriminator, it becomes the default type used when the discriminator property
    /// is missing or does not match any registered type.
    /// </summary>
    /// <param name="derivedType">The derived CLR type.</param>
    public TomlDerivedTypeAttribute(Type derivedType)
    {
        ArgumentGuard.ThrowIfNull(derivedType, nameof(derivedType));

        DerivedType = derivedType;
        Discriminator = null;
    }

    /// <summary>
    /// Gets the derived CLR type.
    /// </summary>
    public Type DerivedType { get; }

    /// <summary>
    /// Gets the discriminator value, or <see langword="null"/> if this is the default derived type.
    /// </summary>
    public string? Discriminator { get; }
}
