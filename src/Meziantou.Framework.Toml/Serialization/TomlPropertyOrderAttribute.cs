using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Specifies the emitted order for a serialized member.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlPropertyOrderAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlPropertyOrderAttribute"/> class.
    /// </summary>
    /// <param name="order">The order value.</param>
    public TomlPropertyOrderAttribute(int order)
    {
        Order = order;
    }

    /// <summary>
    /// Gets the order value.
    /// </summary>
    public int Order { get; }
}
