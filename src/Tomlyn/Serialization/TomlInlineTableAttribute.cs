// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Overrides inline table formatting for a member.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlInlineTableAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlInlineTableAttribute"/> class.
    /// </summary>
    /// <param name="policy">The inline table policy.</param>
    public TomlInlineTableAttribute(TomlInlineTablePolicy policy)
    {
        if (policy is not TomlInlineTablePolicy.Never and not TomlInlineTablePolicy.WhenSmall and not TomlInlineTablePolicy.Always)
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Invalid TOML inline table policy.");
        }

        Policy = policy;
    }

    /// <summary>
    /// Gets the inline table policy.
    /// </summary>
    public TomlInlineTablePolicy Policy { get; }
}
