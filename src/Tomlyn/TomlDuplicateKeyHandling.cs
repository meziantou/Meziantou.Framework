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
/// Specifies behavior when duplicate keys are encountered.
/// </summary>
public enum TomlDuplicateKeyHandling
{
    /// <summary>
    /// Reject duplicate keys.
    /// </summary>
    Error = 0,

    /// <summary>
    /// Keep the last value for a duplicate key.
    /// </summary>
    LastWins = 1,
}
