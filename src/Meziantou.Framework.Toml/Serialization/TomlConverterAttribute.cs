using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Specifies a custom <see cref="TomlConverter"/> to use when serializing or deserializing a member or type.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface |
                AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlConverterAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlConverterAttribute"/> class.
    /// </summary>
    /// <param name="converterType">The converter type.</param>
    public TomlConverterAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type converterType)
    {
        ArgumentGuard.ThrowIfNull(converterType, nameof(converterType));
        ConverterType = converterType;
    }

    /// <summary>
    /// Gets the converter type.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type ConverterType { get; }
}
