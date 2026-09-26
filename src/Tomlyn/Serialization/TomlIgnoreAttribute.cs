// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Instructs the <see cref="Tomlyn.TomlSerializer"/> when to ignore the field or property value.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class TomlIgnoreAttribute : TomlAttribute
{
    /// <summary>
    /// Gets or sets the condition that must be met before the member is ignored.
    /// </summary>
    /// <remarks>The default value is <see cref="TomlIgnoreCondition.Always"/>.</remarks>
    public TomlIgnoreCondition Condition { get; set; } = TomlIgnoreCondition.Always;
}
