// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Overrides array-of-tables formatting for a collection member.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlTableArrayStyleAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlTableArrayStyleAttribute"/> class.
    /// </summary>
    /// <param name="style">The array-of-tables style.</param>
    public TomlTableArrayStyleAttribute(TomlTableArrayStyle style)
    {
        if (style is not TomlTableArrayStyle.Headers and not TomlTableArrayStyle.InlineArrayOfTables)
        {
            throw new ArgumentOutOfRangeException(nameof(style), style, "Invalid TOML table array style.");
        }

        Style = style;
    }

    /// <summary>
    /// Gets the array-of-tables style.
    /// </summary>
    public TomlTableArrayStyle Style { get; }
}
