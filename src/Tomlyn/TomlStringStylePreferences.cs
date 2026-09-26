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
/// Configures string scalar style preferences for TOML serialization.
/// </summary>
public sealed record TomlStringStylePreferences
{
    /// <summary>Preferred default string style.</summary>
    public TomlStringStyle DefaultStyle { get; init; } = TomlStringStyle.Basic;

    /// <summary>If true, prefer literal strings when no escaping is required.</summary>
    public bool PreferLiteralWhenNoEscapes { get; init; }

    /// <summary>If true, allow emitting <c>\\xHH</c> escapes for control characters.</summary>
    public bool AllowHexEscapes { get; init; } = true;
}
