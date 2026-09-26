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
/// Specifies when a member should be ignored during TOML serialization or deserialization.
/// </summary>
public enum TomlIgnoreCondition
{
    /// <summary>
    /// Never ignore a member during serialization or deserialization.
    /// </summary>
    Never = 0,

    /// <summary>
    /// Ignore a member during serialization when its value is <see langword="null"/>.
    /// </summary>
    WhenWritingNull = 1,

    /// <summary>
    /// Ignore a member during serialization when its value is the type default.
    /// </summary>
    WhenWritingDefault = 2,

    /// <summary>
    /// Always ignore a member during serialization and deserialization.
    /// </summary>
    Always = 3,

    /// <summary>
    /// Ignore a member during serialization.
    /// </summary>
    WhenWriting = 4,

    /// <summary>
    /// Ignore a member during deserialization.
    /// </summary>
    WhenReading = 5,
}
