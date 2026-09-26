// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

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
