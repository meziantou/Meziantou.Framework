using System;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Specifies a root type to include in a source-generated <see cref="TomlSerializerContext"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TomlSerializableAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlSerializableAttribute"/> class.
    /// </summary>
    /// <param name="type">The root type to include in the generated context.</param>
    public TomlSerializableAttribute(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
    }

    /// <summary>
    /// Gets the root type included in the generated context.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Gets or sets the generated <see cref="Meziantou.Framework.Toml.TomlTypeInfo"/> property name for this root type.
    /// </summary>
    public string? TypeInfoPropertyName { get; set; }
}
