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
/// Specifies behavior for dictionary keys containing '.'.
/// </summary>
public enum TomlDottedKeyHandling
{
    /// <summary>
    /// Treat keys containing '.' as a literal key name by quoting/escaping as needed.
    /// </summary>
    Literal = 0,

    /// <summary>
    /// Treat keys containing '.' as a dotted path and expand into subtables.
    /// </summary>
    Expand = 1,
}
