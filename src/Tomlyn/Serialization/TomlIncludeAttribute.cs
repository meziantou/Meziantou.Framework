// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Includes a non-public member in TOML serialization.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlIncludeAttribute : TomlAttribute
{
}
