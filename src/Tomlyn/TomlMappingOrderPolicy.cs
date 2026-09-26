// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn.Helpers;
using Tomlyn.Serialization;

namespace Tomlyn;

/// <summary>
/// Specifies member ordering behavior for emitted mappings.
/// </summary>
public enum TomlMappingOrderPolicy
{
    /// <summary>
    /// Order members by declaration order.
    /// </summary>
    Declaration = 0,

    /// <summary>
    /// Order members alphabetically by serialized name.
    /// </summary>
    Alphabetical = 1,

    /// <summary>
    /// Order members by explicit order then declaration order.
    /// </summary>
    OrderThenDeclaration = 2,

    /// <summary>
    /// Order members by explicit order then alphabetical order.
    /// </summary>
    OrderThenAlphabetical = 3,
}
