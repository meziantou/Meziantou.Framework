using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Specifies the serialized TOML property name for a member.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlPropertyNameAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlPropertyNameAttribute"/> class.
    /// </summary>
    /// <param name="name">The serialized member name.</param>
    public TomlPropertyNameAttribute(string name)
    {
        ArgumentGuard.ThrowIfNull(name, nameof(name));
        if (name.Length == 0)
        {
            throw new ArgumentException("Property name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    /// <summary>
    /// Gets the serialized member name.
    /// </summary>
    public string Name { get; }
}
