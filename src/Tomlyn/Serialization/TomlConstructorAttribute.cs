// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Specifies which constructor should be used during TOML deserialization.
/// </summary>
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false)]
public sealed class TomlConstructorAttribute : TomlAttribute
{
}
