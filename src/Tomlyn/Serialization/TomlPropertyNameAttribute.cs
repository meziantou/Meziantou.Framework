// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

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
