using System;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Sets the name <see cref="TomlStringEnumConverter"/> uses for an enum value.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlStringEnumMemberNameAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlStringEnumMemberNameAttribute"/> class.
    /// </summary>
    /// <param name="name">The name of the enum value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public TomlStringEnumMemberNameAttribute(string name)
    {
        ArgumentGuard.ThrowIfNull(name, nameof(name));
        if (name.Length == 0)
        {
            throw new ArgumentException("The enum member name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    /// <summary>
    /// Gets the name of the enum value.
    /// </summary>
    public string Name { get; }
}
