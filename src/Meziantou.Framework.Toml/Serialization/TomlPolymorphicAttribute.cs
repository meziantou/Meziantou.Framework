using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Helpers;

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
    public TomlUnknownDerivedTypeHandling UnknownDerivedTypeHandling { get; set; } = TomlUnknownDerivedTypeHandling.Unspecified;
}
