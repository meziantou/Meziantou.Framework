using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Marks a base type as polymorphic for TOML serialization.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class TomlPolymorphicAttribute : TomlAttribute
{
    /// <summary>
    /// Gets or sets the discriminator property name.
    /// </summary>
    public string? TypeDiscriminatorPropertyName { get; set; }

    /// <summary>
    /// Gets or sets the behavior when an unknown discriminator is encountered.
    /// When set to a value other than <see cref="TomlUnknownDerivedTypeHandling.Unspecified"/>,
    /// this overrides the global <see cref="TomlPolymorphismOptions.UnknownDerivedTypeHandling"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is not a defined <see cref="TomlUnknownDerivedTypeHandling"/>.</exception>
    public TomlUnknownDerivedTypeHandling UnknownDerivedTypeHandling
    {
        get;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"The value is not a defined {nameof(TomlUnknownDerivedTypeHandling)}.");
            }

            field = value;
        }
    } = TomlUnknownDerivedTypeHandling.Unspecified;
}
