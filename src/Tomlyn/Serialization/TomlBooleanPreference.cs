// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Represents an optional boolean formatting preference on an attribute.
/// </summary>
public enum TomlBooleanPreference
{
    /// <summary>
    /// Use the value from the broader serializer options scope.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The preference is enabled.
    /// </summary>
    True = 1,

    /// <summary>
    /// The preference is disabled.
    /// </summary>
    False = 2,
}
