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
/// Specifies behavior for scalar/array roots that do not naturally map to a TOML document.
/// </summary>
public enum TomlRootValueHandling
{
    /// <summary>
    /// Throw when attempting to serialize/deserialize a non-table root.
    /// </summary>
    Error = 0,

    /// <summary>
    /// Wrap the root value under <see cref="TomlSerializerOptions.RootValueKeyName"/>.
    /// </summary>
    WrapInRootKey = 1,
}
