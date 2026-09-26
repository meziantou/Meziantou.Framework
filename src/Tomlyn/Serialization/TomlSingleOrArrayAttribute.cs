// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Allows a collection member to be deserialized from either a single TOML value or a TOML array.
/// </summary>
/// <remarks>
/// When the TOML input contains a single value instead of an array, Tomlyn treats it as a collection containing
/// exactly one element. For mutable read-only collection members, values are appended to the existing collection.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlSingleOrArrayAttribute : TomlAttribute
{
}
